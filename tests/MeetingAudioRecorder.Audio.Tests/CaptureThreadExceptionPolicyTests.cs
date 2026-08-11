using System.Runtime.InteropServices;
using MeetingAudioRecorder.Audio.Capture;

namespace MeetingAudioRecorder.Audio.Tests;

public class CaptureThreadExceptionPolicyTests
{
    [Fact]
    public void Execute_WhenAudioClientStopThrows_ReturnsErrorWithoutEscapingThread()
    {
        var deviceInvalidated = new COMException("Endpoint audio został unieważniony.", unchecked((int)0x88890004));

        var error = CaptureThreadExceptionPolicy.Execute(
            captureLoop: () => { },
            stopAudioClient: () => throw deviceInvalidated);

        Assert.Same(deviceInvalidated, error);
    }

    [Fact]
    public void Execute_WhenCaptureAndStopThrow_PreservesBothErrors()
    {
        var captureError = new COMException("Odczyt pakietu nie powiódł się.", unchecked((int)0x88890004));
        var stopError = new COMException("Zatrzymanie klienta nie powiodło się.", unchecked((int)0x88890004));

        var error = CaptureThreadExceptionPolicy.Execute(
            captureLoop: () => throw captureError,
            stopAudioClient: () => throw stopError);

        var aggregate = Assert.IsType<AggregateException>(error);
        Assert.Equal(new Exception[] { captureError, stopError }, aggregate.InnerExceptions);
    }

    [Fact]
    public void Execute_WhenCaptureAndStopSucceed_ReturnsNoError()
    {
        var error = CaptureThreadExceptionPolicy.Execute(
            captureLoop: () => { },
            stopAudioClient: () => { });

        Assert.Null(error);
    }
}
