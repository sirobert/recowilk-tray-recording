using System.Windows.Threading;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.App.Services;

// Coalesce chunk/catalog notifications; decrypting and listing the catalog never runs on the UI thread.
internal sealed class TrayUploadMonitor : IDisposable
{
    private readonly IRecordingCatalog catalog;
    private readonly ISettingsService settings;
    private readonly TrayIconController icons;
    private readonly IRecowilkUploadQueue uploads;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool refreshing, dirty, disposed;
    public TrayUploadMonitor(IRecordingCatalog catalog, ISettingsService settings, IRecowilkUploadQueue uploads, TrayIconController icons)
    {
        this.catalog = catalog; this.settings = settings; this.icons = icons; this.uploads = uploads;
        catalog.Changed += Changed; settings.SettingsChanged += SettingsChanged; timer.Tick += Refresh;
        uploads.ActivityChanged += SettingsChanged;
        Request();
    }
    private void Changed(object? sender, RecordingCatalogChangedEventArgs e) => Request();
    private void SettingsChanged(object? sender, EventArgs e) => Request();
    private void Request()
    {
        if (timer.Dispatcher.HasShutdownStarted) return;
        _ = timer.Dispatcher.InvokeAsync(() =>
        {
            if (disposed) return;
            dirty = true;
            if (!refreshing) timer.Start();
        });
    }
    private async void Refresh(object? sender, EventArgs e)
    {
        timer.Stop(); if (disposed || refreshing) return;
        refreshing = true; dirty = false;
        try
        {
            var result = await Task.Run(() => UploadActivity.From(catalog.List(), settings.Current.RecowilkUploadEnabled, uploads.ActiveRecordingId));
            if (!disposed) icons.UpdateUploads(result);
        }
        catch
        {
            if (!disposed) icons.UpdateUploads(new("nie można odczytać kolejki — otwórz Nagrania i eksport", true));
        }
        finally { refreshing = false; if (dirty && !disposed) timer.Start(); }
    }
    public void Dispose()
    {
        disposed = true; timer.Stop(); timer.Tick -= Refresh;
        catalog.Changed -= Changed; settings.SettingsChanged -= SettingsChanged;
        uploads.ActivityChanged -= SettingsChanged;
    }
}
