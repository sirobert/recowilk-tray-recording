using System.Net;
using System.Security.Cryptography;
using MeetingAudioRecorder.Infrastructure.Translation;

namespace MeetingAudioRecorder.Audio.Tests;

public sealed class TranslationModelDownloadTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mar-model-test-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Data = [1, 2, 3, 4];
    private static ModelDownload Spec => new("nested/model.bin", new Uri("https://example.com/model"), Data.Length, Convert.ToHexString(SHA256.HashData(Data)));
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifiedFileIsPublishedAndSkippedOnRetry(bool trailingSeparator)
    {
        var handler = new Handler(Data); using var client = new HttpClient(handler);
        var service = new TranslationModelDownloader(client);
        var directory = trailingSeparator ? root + Path.DirectorySeparatorChar : root;
        await service.EnsureAsync(directory, [Spec], null, default);
        await service.EnsureAsync(directory, [Spec], null, default);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(Data, await File.ReadAllBytesAsync(Path.Combine(root, Spec.Path)));
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BadResponseDoesNotReplaceExistingFile(bool httpFailure)
    {
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        var target = Path.Combine(root, Spec.Path); await File.WriteAllTextAsync(target, "old");
        using var client = new HttpClient(new Handler([9, 9, 9, 9], httpFailure));
        await Assert.ThrowsAnyAsync<Exception>(() => new TranslationModelDownloader(client).EnsureAsync(root, [Spec], null, default));
        Assert.Equal("old", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task CancellationDoesNotPublishOrRequest()
    {
        var handler = new Handler(Data); using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TranslationModelDownloader(client).EnsureAsync(root, [Spec], null, cancellation.Token));
        Assert.Equal(0, handler.Calls); Assert.False(File.Exists(Path.Combine(root, Spec.Path)));
    }
    [Fact]
    public async Task CancellationAfterRequestLeavesNoPartialFile()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new Handler(Data, onRequest: cancellation.Cancel));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TranslationModelDownloader(client).EnsureAsync(root, [Spec], null, cancellation.Token));
        Assert.False(File.Exists(Path.Combine(root, Spec.Path)));
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task TraversalIsRejectedBeforeNetworkRequest()
    {
        var handler = new Handler(Data); using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new TranslationModelDownloader(client).EnsureAsync(root, [Spec with { Path = "../escaped.bin" }], null, default));
        Assert.Equal(0, handler.Calls);
    }
    private sealed class Handler(byte[] data, bool fail = false, Action? onRequest = null) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Assert.Null(request.Headers.Authorization); onRequest?.Invoke();
            return Task.FromResult(new HttpResponseMessage(fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
