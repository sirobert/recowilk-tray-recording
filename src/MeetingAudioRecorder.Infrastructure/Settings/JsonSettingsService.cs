using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;
using Microsoft.Extensions.Logging;

namespace MeetingAudioRecorder.Infrastructure.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private readonly ILogger<JsonSettingsService> _logger;
    private readonly object _lock = new();
    private AppSettings _current;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public JsonSettingsService(ILogger<JsonSettingsService> logger)
    {
        _logger = logger;
        _current = AppSettings.CreateDefault();
    }

    public AppSettings Current
    {
        get { lock (_lock) return _current.Clone(); }
    }

    public event EventHandler? SettingsChanged;

    public AppSettings Load()
    {
        lock (_lock)
        {
            AppPaths.EnsureDirectories();
            var path = AppPaths.SettingsPath;

            if (!File.Exists(path))
            {
                _current = AppSettings.CreateDefault();
                SaveUnsafe(_current);
                _logger.LogInformation("Utworzono domyślną konfigurację: {Path}", path);
                return _current.Clone();
            }

            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is null)
                    throw new InvalidDataException("Deserializacja zwróciła null.");

                var validation = SettingsValidator.Validate(loaded);
                if (!validation.IsValid)
                {
                    _logger.LogWarning("Konfiguracja nieprzechodzi walidacji: {Errors}", string.Join("; ", validation.Errors));
                    loaded = SettingsValidator.Sanitize(loaded);
                }

                if (string.IsNullOrWhiteSpace(loaded.RecordingsDirectory))
                    loaded.RecordingsDirectory = AppSettings.CreateDefault().RecordingsDirectory;

                _current = loaded;
                _logger.LogInformation("Wczytano konfigurację z {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Uszkodzony plik ustawień — przywracanie domyślnych");
                try
                {
                    var backup = path + $".corrupt.{DateTime.Now:yyyyMMddHHmmss}.bak";
                    File.Copy(path, backup, overwrite: true);
                    _logger.LogInformation("Kopia uszkodzonego pliku: {Backup}", backup);
                }
                catch (Exception backupEx)
                {
                    _logger.LogWarning(backupEx, "Nie udało się utworzyć kopii uszkodzonego pliku");
                }

                _current = AppSettings.CreateDefault();
                SaveUnsafe(_current);
            }

            return _current.Clone();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            var sanitized = SettingsValidator.Sanitize(settings);
            var validation = SettingsValidator.Validate(sanitized);
            if (!validation.IsValid)
                throw new InvalidOperationException("Nieprawidłowa konfiguracja: " + string.Join(" ", validation.Errors));

            SaveUnsafe(sanitized);
            _current = sanitized;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public ValidationResult Validate(AppSettings settings) => SettingsValidator.Validate(settings);

    private void SaveUnsafe(AppSettings settings)
    {
        AppPaths.EnsureDirectories();
        var path = AppPaths.SettingsPath;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Copy(temp, path, overwrite: true);
        File.Delete(temp);
        _logger.LogDebug("Zapisano ustawienia: {Path}", path);
    }
}
