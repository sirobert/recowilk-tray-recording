namespace MeetingAudioRecorder.Core.Models;

public sealed class GoogleOAuthToken
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
    public required string AccountEmail { get; init; }
    public required string AccountUserId { get; init; }
    public required string ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public required string TokenEndpoint { get; init; }
    public IReadOnlyList<string> GrantedScopes { get; init; } = Array.Empty<string>();
}
