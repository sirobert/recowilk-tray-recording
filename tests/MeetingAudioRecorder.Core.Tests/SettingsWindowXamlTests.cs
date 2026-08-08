using System.Xml.Linq;

namespace MeetingAudioRecorder.Core.Tests;

public class SettingsWindowXamlTests
{
    [Fact]
    public void HotkeyPreview_ReadOnlyProperty_UsesOneWayBinding()
    {
        var xamlPath = Path.Combine(AppContext.BaseDirectory, "UiContracts", "SettingsWindow.xaml");
        var document = XDocument.Load(xamlPath);

        var preview = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Run"
                && element.Attribute("Text")?.Value.Contains("HotkeyPreview", StringComparison.Ordinal) == true);

        Assert.Contains("Mode=OneWay", preview.Attribute("Text")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void GoogleMeetAutomation_RequiresConnectedAccountAndUsesExplicitCommand()
    {
        var xamlPath = Path.Combine(AppContext.BaseDirectory, "UiContracts", "SettingsWindow.xaml");
        var document = XDocument.Load(xamlPath);

        var automationToggle = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "CheckBox"
                && element.Attribute("IsChecked")?.Value.Contains(
                    "GoogleMeetAutomationEnabled",
                    StringComparison.Ordinal) == true);
        var connectButton = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value.Contains(
                    "ConnectGoogleCommand",
                    StringComparison.Ordinal) == true);

        Assert.Contains("IsGoogleConnected", automationToggle.Attribute("IsEnabled")?.Value, StringComparison.Ordinal);
        Assert.Equal("Połącz z Google…", connectButton.Attribute("Content")?.Value);
    }

    [Fact]
    public void GoogleMeetAutomation_OffersBundledChromeAndEdgeExtension()
    {
        var xamlPath = Path.Combine(AppContext.BaseDirectory, "UiContracts", "SettingsWindow.xaml");
        var document = XDocument.Load(xamlPath);
        var commands = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element => element.Attribute("Command")?.Value)
            .Where(value => value is not null)
            .ToArray();

        Assert.Contains(commands, value => value!.Contains("PrepareChromeExtensionCommand", StringComparison.Ordinal));
        Assert.Contains(commands, value => value!.Contains("PrepareEdgeExtensionCommand", StringComparison.Ordinal));
    }
}
