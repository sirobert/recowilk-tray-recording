using System.Windows;
using System.Windows.Media;
using MeetingAudioRecorder.Core.Models;
using Microsoft.Win32;

namespace MeetingAudioRecorder.App.Views;

public partial class TranslationSettingsWindow : Window
{
    private readonly TranslationSettings original;
    private readonly Action<TranslationSettings> save;
    private bool initialized;
    public TranslationSettingsWindow(TranslationSettings value, Action<TranslationSettings> save)
    {
        InitializeComponent(); original = value.Clone(); this.save = save;
        Output.IsChecked = value.OutputEnabled; Microphone.IsChecked = value.MicrophoneEnabled;
        Ctrl.IsChecked = value.Hotkey.Control; Alt.IsChecked = value.Hotkey.Alt; Shift.IsChecked = value.Hotkey.Shift; Win.IsChecked = value.Hotkey.Windows; Key.Text = value.Hotkey.Key;
        FontBox.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(x => x).ToArray(); FontBox.SelectedItem = value.FontFamily;
        FontSizeSlider.Value = value.FontSize; TextColor.Text = value.Foreground; BackgroundColor.Text = value.Background;
        OpacitySlider.Value = value.BackgroundOpacity; Original.IsChecked = value.ShowOriginal; Models.Text = value.ModelsDirectory; Turbo.IsChecked = value.Turbo;
        initialized = true; UpdatePreview();
    }
    private void PreviewChanged(object sender, RoutedEventArgs e) { if (initialized) UpdatePreview(); }
    private void UpdatePreview()
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(BackgroundColor.Text); color.A = (byte)(255 * OpacitySlider.Value);
            PreviewSurface.Background = new SolidColorBrush(color); PreviewText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(TextColor.Text));
            PreviewText.FontFamily = new FontFamily(FontBox.SelectedItem as string ?? "Segoe UI"); PreviewText.FontSize = FontSizeSlider.Value; ErrorText.Text = "";
        }
        catch { ErrorText.Text = "Wpisz poprawny kolor, np. #20242A."; }
    }
    private void DownloadModels(object sender, RoutedEventArgs e) => new TranslationModelsWindow(Models.Text.Trim(), Turbo.IsChecked == true) { Owner = this }.ShowDialog();
    private void Browse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Folder modeli tłumacza" }; if (dialog.ShowDialog(this) == true) Models.Text = dialog.FolderName;
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = ColorConverter.ConvertFromString(TextColor.Text); _ = ColorConverter.ConvertFromString(BackgroundColor.Text);
            var next = original.Clone(); next.OutputEnabled = Output.IsChecked == true; next.MicrophoneEnabled = Microphone.IsChecked == true;
            next.Hotkey = new() { Key = Key.Text.Trim(), Control = Ctrl.IsChecked == true, Alt = Alt.IsChecked == true, Shift = Shift.IsChecked == true, Windows = Win.IsChecked == true };
            next.FontFamily = FontBox.SelectedItem as string ?? "Segoe UI"; next.FontSize = FontSizeSlider.Value; next.Foreground = TextColor.Text; next.Background = BackgroundColor.Text; next.BackgroundOpacity = OpacitySlider.Value;
            next.ShowOriginal = Original.IsChecked == true; next.ModelsDirectory = Models.Text.Trim(); next.Turbo = Turbo.IsChecked == true;
            save(next); Close();
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
}
