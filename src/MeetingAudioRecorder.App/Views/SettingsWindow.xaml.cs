using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel.StopTests();
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StopTests();
        Close();
    }

    private void RecowilkApiKey_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
            _viewModel.RecowilkApiKey = passwordBox.Password;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.RecowilkApiKey)
            && string.IsNullOrEmpty(_viewModel.RecowilkApiKey)
            && RecowilkApiKeyBox.Password.Length > 0)
            RecowilkApiKeyBox.Clear();
    }
}
