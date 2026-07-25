using System.Windows;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.App.Views;

public partial class RecoveryWindow : Window
{
    private readonly IRecordingRecoveryService _recoveryService;
    private readonly IRecordingCoordinator _coordinator;
    private readonly List<RecoverableItem> _items;

    public RecoveryWindow(
        IReadOnlyList<RecoverableRecording> recordings,
        IRecordingRecoveryService recoveryService,
        IRecordingCoordinator coordinator)
    {
        InitializeComponent();
        _recoveryService = recoveryService;
        _coordinator = coordinator;
        _items = recordings.Select(r => new RecoverableItem(r)).ToList();
        List.ItemsSource = _items;
        if (_items.Count > 0)
            List.SelectedIndex = 0;
    }

    private async void Recover_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not RecoverableItem item)
        {
            MessageBox.Show("Wybierz nagranie z listy.", "Odzyskiwanie", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            IsEnabled = false;
            var result = await _coordinator.RecoverRecordingAsync(item.Recording);
            if (result.Success)
            {
                MessageBox.Show(
                    $"Odzyskano nagranie:\n{result.OutputPath}",
                    "Sukces", MessageBoxButton.OK, MessageBoxImage.Information);
                _items.Remove(item);
                List.Items.Refresh();
            }
            else
            {
                MessageBox.Show(result.ErrorMessage ?? "Nie udało się odzyskać.", "Błąd",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
        => _recoveryService.OpenTempFolder();

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not RecoverableItem item)
            return;

        var r = MessageBox.Show(
            "Na pewno usunąć pliki tymczasowe tego nagrania? Tej operacji nie można cofnąć.",
            "Usuń", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (r != MessageBoxResult.Yes)
            return;

        _recoveryService.DeleteRecoverable(item.Recording);
        _items.Remove(item);
        List.Items.Refresh();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class RecoverableItem
    {
        public RecoverableItem(RecoverableRecording recording)
        {
            Recording = recording;
            var micMb = recording.MicrophoneFileSize / (1024.0 * 1024.0);
            var loopMb = recording.LoopbackFileSize / (1024.0 * 1024.0);
            Display = $"{recording.RecordingId:N}  |  mic: {micMb:F1} MB  |  loop: {loopMb:F1} MB";
        }

        public RecoverableRecording Recording { get; }
        public string Display { get; }
    }
}
