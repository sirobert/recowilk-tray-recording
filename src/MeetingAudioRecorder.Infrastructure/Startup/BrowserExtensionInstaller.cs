using System.Diagnostics;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace MeetingAudioRecorder.Infrastructure.Startup;

public sealed class BrowserExtensionInstaller : IBrowserExtensionInstaller
{
    public const string ExtensionId = "eljjpmlmlnjjpjlnhiilfclkhoecdlij";
    private static readonly string[] ExtensionFiles = ["manifest.json", "content.js", "service-worker.js"];
    private readonly ILogger<BrowserExtensionInstaller> _logger;

    public BrowserExtensionInstaller(ILogger<BrowserExtensionInstaller> logger)
    {
        _logger = logger;
    }

    public async Task<BrowserExtensionPreparationResult> PrepareAsync(
        SupportedBrowser browser,
        CancellationToken cancellationToken = default)
    {
        var sourceDirectory = Path.Combine(AppContext.BaseDirectory, "BrowserExtension");
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException("Pakiet rozszerzenia nie został dołączony do aplikacji.");

        AppPaths.EnsureDirectories();
        Directory.CreateDirectory(AppPaths.BrowserExtensionDirectory);
        foreach (var fileName in ExtensionFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.Combine(sourceDirectory, fileName);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Pakiet rozszerzenia jest niekompletny.", sourcePath);
            await CopyAtomicallyAsync(
                sourcePath,
                Path.Combine(AppPaths.BrowserExtensionDirectory, fileName),
                cancellationToken).ConfigureAwait(false);
        }

        var browserOpened = TryOpenExtensionsPage(browser);
        TryOpenExtensionDirectory();
        return new BrowserExtensionPreparationResult(
            AppPaths.BrowserExtensionDirectory,
            ExtensionId,
            browserOpened);
    }

    private static async Task CopyAtomicallyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var source = new FileStream(
                             sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            await using (var destination = new FileStream(
                             temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private bool TryOpenExtensionsPage(SupportedBrowser browser)
    {
        var executableName = browser == SupportedBrowser.Chrome ? "chrome.exe" : "msedge.exe";
        var executablePath = FindBrowserExecutable(executableName, browser);
        if (executablePath is null)
        {
            _logger.LogWarning("Nie znaleziono przeglądarki {Browser}", browser);
            return false;
        }

        try
        {
            var page = browser == SupportedBrowser.Chrome ? "chrome://extensions/" : "edge://extensions/";
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"--new-tab {page}",
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie można otworzyć strony rozszerzeń {Browser}", browser);
            return false;
        }
    }

    private void TryOpenExtensionDirectory()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.BrowserExtensionDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie można otworzyć folderu rozszerzenia");
        }
    }

    private static string? FindBrowserExecutable(string executableName, SupportedBrowser browser)
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
            if (key?.GetValue(null) is string registered && File.Exists(registered))
                return registered;
        }

        var candidates = browser == SupportedBrowser.Chrome
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", executableName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", executableName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", executableName)
            }
            : new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", executableName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", executableName)
            };
        return candidates.FirstOrDefault(File.Exists);
    }
}
