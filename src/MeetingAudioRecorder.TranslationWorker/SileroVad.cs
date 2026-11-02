using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
namespace MeetingAudioRecorder.TranslationWorker;

internal sealed class SileroVad : IDisposable
{
    readonly InferenceSession session;
    float[] state = new float[256];
    readonly float[] context = new float[64];


    public SileroVad(string path)
    {
        using var options = ModelFiles.Options(1);
        session = new InferenceSession(path, options);
        Validate("input", typeof(float), [1, 576]);
        Validate("state", typeof(float), [2, 1, 128]);
        Validate("sr", typeof(long), []);
    }
    void Validate(string name, Type type, int[] shape)
    {
        if (!session.InputMetadata.TryGetValue(name, out var m) || m.ElementType != type || m.Dimensions.Length != shape.Length || m.Dimensions.Where((d, i) => d > 0 && d != shape[i]).Any())
            throw new InvalidDataException($"Unexpected Silero input: {name}");
    }
    public float Process(ReadOnlySpan<float> frame)
    {
        if (frame.Length != 512) throw new ArgumentException("Expected 512 samples.");
        var input = new float[576];
        context.CopyTo(input, 0);
        frame.CopyTo(input.AsSpan(64));
        using var result = session.Run([
            NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(input, [1, 576])),
            NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(state, [2, 1, 128])),
            NamedOnnxValue.CreateFromTensor("sr", new DenseTensor<long>(new long[] { 16000 }, Array.Empty<int>()))
        ]);
        var probability = result.First(x => x.Name == "output").AsTensor<float>().First();
        var nextState = result.First(x => x.Name == "stateN").AsTensor<float>();
        if (!float.IsFinite(probability) || probability < 0 || probability > 1 || !nextState.Dimensions.SequenceEqual(new[] { 2, 1, 128 }))
            throw new InvalidDataException("Invalid Silero result.");
        state = nextState.ToArray();
        input.AsSpan(512, 64).CopyTo(context);
        return probability;
    }
    public void Reset() { Array.Clear(state); Array.Clear(context); }
    public void Dispose() => session.Dispose();
}
