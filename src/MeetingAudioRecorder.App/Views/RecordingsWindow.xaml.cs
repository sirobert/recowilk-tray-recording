using System.Windows;
using MeetingAudioRecorder.App.ViewModels;

namespace MeetingAudioRecorder.App.Views;

public partial class RecordingsWindow : Window
{
    private readonly RecordingsViewModel _viewModel;

    public RecordingsWindow(RecordingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Loaded += OnLoaded;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.RefreshAsync();
    }
}
