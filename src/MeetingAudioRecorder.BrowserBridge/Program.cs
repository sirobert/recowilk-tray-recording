using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using MeetingAudioRecorder.Core.Models;

const string AllowedExtensionOrigin = "chrome-extension://eljjpmlmlnjjpjlnhiilfclkhoecdlij";
const int MaximumMessageBytes = 64 * 1024;

if (args.Length == 0
    || !string.Equals(args[0].TrimEnd('/'), AllowedExtensionOrigin, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Niedozwolone źródło rozszerzenia.");
    return 2;
}

var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
var lengthBuffer = new byte[4];
var serializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

while (await ReadExactOrEndAsync(input, lengthBuffer))
{
    var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
    if (length is <= 0 or > MaximumMessageBytes)
    {
        await WriteResponseAsync(output, new { ok = false, error = "invalid_message_size" });
        return 3;
    }

    var payload = new byte[length];
    if (!await ReadExactOrEndAsync(input, payload))
        return 4;

    try
    {
        var message = JsonSerializer.Deserialize<ExtensionStateMessage>(payload, serializerOptions);
        await PersistStateAsync(message);
        await WriteResponseAsync(output, new { ok = true });
    }
    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(ex.Message);
        await WriteResponseAsync(output, new { ok = false, error = "state_write_failed" });
    }
}

return 0;

static async Task PersistStateAsync(ExtensionStateMessage? message)
{
    var links = (message?.Links ?? [])
        .Where(link => !string.IsNullOrWhiteSpace(link.MeetingCode))
        .Take(16)
        .Select(link => new PersistedLink
        {
            MeetingCode = link.MeetingCode!.Trim().ToLowerInvariant()[..Math.Min(link.MeetingCode.Trim().Length, 128)],
            Browser = string.IsNullOrWhiteSpace(link.Browser)
                ? null
                : link.Browser.Trim()[..Math.Min(link.Browser.Trim().Length, 32)]
        })
        .ToArray();
    var document = new PersistedState
    {
        Version = 1,
        ObservedAtUtc = DateTimeOffset.UtcNow,
        Links = links
    };

    Directory.CreateDirectory(AppPaths.BrowserDirectory);
    var temporaryPath = AppPaths.BrowserStatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(document),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, AppPaths.BrowserStatePath, overwrite: true);
    }
    finally
    {
        if (File.Exists(temporaryPath))
            File.Delete(temporaryPath);
    }
}

static async Task<bool> ReadExactOrEndAsync(Stream stream, Memory<byte> buffer)
{
    var total = 0;
    while (total < buffer.Length)
    {
        var read = await stream.ReadAsync(buffer[total..]);
        if (read == 0)
            return total == 0;
        total += read;
    }

    return true;
}

static async Task WriteResponseAsync(Stream output, object response)
{
    var payload = JsonSerializer.SerializeToUtf8Bytes(response);
    var prefix = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
    await output.WriteAsync(prefix);
    await output.WriteAsync(payload);
    await output.FlushAsync();
}

internal sealed class ExtensionStateMessage
{
    public ExtensionLink[]? Links { get; init; }
}

internal sealed class ExtensionLink
{
    public string? MeetingCode { get; init; }
    public string? Browser { get; init; }
}

internal sealed class PersistedState
{
    public int Version { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; }
    public required PersistedLink[] Links { get; init; }
}

internal sealed class PersistedLink
{
    public required string MeetingCode { get; init; }
    public string? Browser { get; init; }
}
