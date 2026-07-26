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
}
