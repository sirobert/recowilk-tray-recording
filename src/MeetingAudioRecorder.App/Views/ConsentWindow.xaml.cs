using System.Windows;

namespace MeetingAudioRecorder.App.Views;

public partial class ConsentWindow : Window
{
    public ConsentWindow()
    {
        InitializeComponent();
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
