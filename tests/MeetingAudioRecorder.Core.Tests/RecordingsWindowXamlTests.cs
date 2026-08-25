using System.Xml.Linq;

namespace MeetingAudioRecorder.Core.Tests;

public sealed class RecordingsWindowXamlTests
{
    [Fact]
    public void Window_exposes_recordings_metadata_status_and_retry_action()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "UiContracts", "RecordingsWindow.xaml");
        var document = XDocument.Load(path);
        var attributes = document.Descendants().Attributes().Select(a => a.Value).ToArray();

        Assert.Contains(attributes, value => value.Contains("Recordings", StringComparison.Ordinal));
        Assert.Contains(attributes, value => value.Contains("SelectedRecording.Participants", StringComparison.Ordinal));
        Assert.Contains(attributes, value => value.Contains("ExportStatusText", StringComparison.Ordinal));
        Assert.Contains(attributes, value => value.Contains("RetryExportCommand", StringComparison.Ordinal));
        Assert.Contains(attributes, value => value.Contains("TraceId", StringComparison.Ordinal));
    }
}
