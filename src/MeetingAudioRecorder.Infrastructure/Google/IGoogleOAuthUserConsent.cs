namespace MeetingAudioRecorder.Infrastructure.Google;

public interface IGoogleOAuthUserConsent
{
    Task<GoogleOAuthAuthorizationCode> RequestCodeAsync(
        Func<Uri, Uri> authorizationUriFactory,
        string expectedState,
        CancellationToken cancellationToken);
}

public sealed record GoogleOAuthAuthorizationCode(string Code, Uri RedirectUri);
