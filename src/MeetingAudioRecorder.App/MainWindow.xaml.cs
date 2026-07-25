using System.Windows;

namespace MeetingAudioRecorder.App;

/// <summary>
/// Nieużywane okno startowe — aplikacja działa z tray (App.xaml ShutdownMode=OnExplicitShutdown).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
