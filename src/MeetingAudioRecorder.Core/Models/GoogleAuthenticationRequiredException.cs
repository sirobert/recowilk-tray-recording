namespace MeetingAudioRecorder.Core.Models;

public sealed class GoogleAuthenticationRequiredException : InvalidOperationException
{
    public GoogleAuthenticationRequiredException(string message)
        : base(message)
    {
    }
}
