using System.Security.Cryptography;
using System.Text.Json;

namespace MeetingAudioRecorder.Core.Tests;

public sealed class BrowserExtensionManifestTests
{
    [Fact]
    public void Manifest_UsesRequestedNameAndLeastPrivilegeMeetAccess()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "BrowserExtension", "manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal("Meeting Orgniazer Gemini", root.GetProperty("name").GetString());
        Assert.Equal(3, root.GetProperty("manifest_version").GetInt32());
        Assert.Equal(
            ["nativeMessaging"],
            root.GetProperty("permissions").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal(
            ["https://meet.google.com/*"],
            root.GetProperty("host_permissions").EnumerateArray().Select(value => value.GetString()!).ToArray());
    }

    [Fact]
    public void NativeHost_AllowsOnlyStableManifestExtensionId()
    {
        var extensionPath = Path.Combine(AppContext.BaseDirectory, "BrowserExtension", "manifest.json");
        var hostPath = Path.Combine(AppContext.BaseDirectory, "BrowserExtension", "native-host-manifest.json");
        using var extension = JsonDocument.Parse(File.ReadAllText(extensionPath));
        using var host = JsonDocument.Parse(File.ReadAllText(hostPath));
        var publicKey = Convert.FromBase64String(extension.RootElement.GetProperty("key").GetString()!);
        var hash = SHA256.HashData(publicKey);
        const string alphabet = "abcdefghijklmnop";
        var extensionId = string.Concat(hash.Take(16).SelectMany(value => new[]
        {
            alphabet[value >> 4],
            alphabet[value & 0x0f]
        }));

        var allowedOrigin = Assert.Single(host.RootElement.GetProperty("allowed_origins").EnumerateArray());
        Assert.Equal($"chrome-extension://{extensionId}/", allowedOrigin.GetString());
        Assert.Equal("__NATIVE_HOST_PATH__", host.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public void Installer_ReplacesNativeHostPathWithAbsoluteInstalledExecutablePath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Installer", "installer.iss");
        var installer = File.ReadAllText(path);

        Assert.Contains("ExpandConstant('{app}\\MeetingAudioRecorder.BrowserBridge.exe')", installer);
        Assert.Contains("StringChangeEx(Contents, '__NATIVE_HOST_PATH__', HostPath, True)", installer);
        Assert.Contains("SaveStringToFile(ManifestPath, AnsiString(Contents), False)", installer);
    }
}
