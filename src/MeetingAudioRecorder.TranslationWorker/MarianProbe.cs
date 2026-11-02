using System.Diagnostics;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace MeetingAudioRecorder.TranslationWorker;

// Experimental binding for one pinned export, not a general Marian runtime.
internal sealed class MarianProbe : IDisposable
{
    readonly InferenceSession encoder;
    readonly InferenceSession decoder;
    readonly SentencePieceTokenizer tokenizer;
    readonly Dictionary<string, int> vocabulary;
    readonly Dictionary<int, string> inverse;

    public MarianProbe(string directory)
    {
        using var pinsStream = typeof(MarianProbe).Assembly.GetManifestResourceStream("MeetingAudioRecorder.TranslationWorker.marian-pins.json")
            ?? throw new InvalidDataException("Missing embedded experimental model pins.");
        using var pins = JsonDocument.Parse(pinsStream);
        foreach (var pin in pins.RootElement.EnumerateObject())
            ModelFiles.Verify(Path.Combine(directory, pin.Name), pin.Value.GetProperty("sha256").GetString()!, pin.Value.GetProperty("bytes").GetInt64());
        using var options = ModelFiles.Options(2);
        encoder = new InferenceSession(Path.Combine(directory, "encoder_model_int8.onnx"), options);
        decoder = new InferenceSession(Path.Combine(directory, "decoder_model_merged_int8.onnx"), options);
        using var source = File.OpenRead(Path.Combine(directory, "source.spm"));
        tokenizer = SentencePieceTokenizer.Create(source, addBeginningOfSentence: false, addEndOfSentence: false);
        vocabulary = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(directory, "vocab.json")))!;
        inverse = vocabulary.ToDictionary(kv => kv.Value, kv => kv.Key);
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "config.json")));
        if (config.RootElement.GetProperty("decoder_start_token_id").GetInt32() != 65000 || config.RootElement.GetProperty("eos_token_id").GetInt32() != 0)
            throw new InvalidDataException("Unexpected Marian token configuration.");
    }

    public long[] Encode(string text) => tokenizer.EncodeToTokens(text, out _)
        .Select(t => (long)vocabulary.GetValueOrDefault(t.Value, vocabulary["<unk>"]))
        .Append(0L).ToArray();

    public (string Text, long[] InputIds, List<long> OutputIds) Translate(string text, CancellationToken cancellationToken = default)
    {
        var ids = Encode(text);
        if (ids.Length > 512) throw new ArgumentException("Source exceeds 512 tokens.");
        var mask = new DenseTensor<long>(Enumerable.Repeat(1L, ids.Length).ToArray(), [1, ids.Length]);
        using var encoded = encoder.Run([
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, [1, ids.Length])),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask)
        ]);
        var hidden = encoded.First().AsTensor<float>();
        var past = decoder.InputMetadata.Keys.Where(k => k.StartsWith("past_key_values."))
            .ToDictionary(k => k, _ => (Tensor<float>)new DenseTensor<float>(new float[0], [1, 8, 0, 64]));
        long token = 65000;
        var output = new List<long>();
        for (int step = 0; step < 128; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(new[] { token }, [1, 1])),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", mask),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hidden),
                NamedOnnxValue.CreateFromTensor("use_cache_branch", new DenseTensor<bool>(new[] { step > 0 }, [1]))
            };
            inputs.AddRange(past.Select(kv => NamedOnnxValue.CreateFromTensor(kv.Key, kv.Value)));
            using var result = decoder.Run(inputs);
            var logits = result.First(x => x.Name == "logits").AsTensor<float>().ToArray();
            int best = 0;
            for (int i = 1; i < 65000; i++) if (logits[i] > logits[best]) best = i;
            if (best == 0)
            {
                // Marian's target SPM decodes pieces; ordinary pieces use the same underline convention.
                // Reference parity is required before production (especially unknown/special tokens).
                var decoded = string.Concat(output.Select(id => inverse[(int)id])).Replace('▁', ' ').Trim();
                return (decoded, ids, output);
            }
            output.Add(best);
            foreach (var key in past.Keys.ToArray())
            {
                // Cached branch can emit empty encoder KV: keep the first-step cross attention cache.
                if (step > 0 && key.Contains(".encoder.")) continue;
                var value = result.First(x => x.Name == key.Replace("past_key_values.", "present.")).AsTensor<float>();
                past[key] = new DenseTensor<float>(value.ToArray(), value.Dimensions.ToArray());
            }
            token = best;
        }
        throw new InvalidDataException("Decoder did not emit EOS within 128 steps.");
    }

    public void Dispose() { decoder.Dispose(); encoder.Dispose(); }
}
