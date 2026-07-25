using System.Windows;
using MeetingAudioRecorder.App.ViewModels;

namespace MeetingAudioRecorder.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Closed += (_, _) => _viewModel.StopTests();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StopTests();
        Close();
    }
}
