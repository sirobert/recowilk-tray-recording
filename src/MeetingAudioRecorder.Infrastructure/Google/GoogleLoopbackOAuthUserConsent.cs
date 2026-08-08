using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Infrastructure.Google;

public sealed class GoogleLoopbackOAuthUserConsent : IGoogleOAuthUserConsent
{
    public async Task<GoogleOAuthAuthorizationCode> RequestCodeAsync(
        Func<Uri, Uri> authorizationUriFactory,
        string expectedState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizationUriFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedState);

        var port = ReserveLoopbackPort();
        var redirectUri = new Uri($"http://127.0.0.1:{port}/oauth2/callback/", UriKind.Absolute);
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri.AbsoluteUri);
        listener.Start();

        var authorizationUri = authorizationUriFactory(redirectUri);
        OpenSystemBrowser(authorizationUri);

        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            listener.Stop();
            throw;
        }

        var query = context.Request.QueryString;
        var returnedState = query["state"];
        var code = query["code"];
        var error = query["error"];
        var stateMatches = FixedTimeEquals(expectedState, returnedState);
        var success = stateMatches && string.IsNullOrWhiteSpace(error) && !string.IsNullOrWhiteSpace(code);

        await WriteBrowserResponseAsync(context.Response, success, cancellationToken).ConfigureAwait(false);

        if (!stateMatches)
            throw new GoogleAuthenticationRequiredException("Odrzucono odpowiedź OAuth z nieprawidłowym parametrem state.");
        if (!string.IsNullOrWhiteSpace(error))
            throw new GoogleAuthenticationRequiredException("Logowanie Google zostało anulowane lub odrzucone.");
        if (string.IsNullOrWhiteSpace(code))
            throw new GoogleAuthenticationRequiredException("Google nie zwrócił kodu autoryzacyjnego.");

        return new GoogleOAuthAuthorizationCode(code, redirectUri);
    }

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void OpenSystemBrowser(Uri authorizationUri)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = authorizationUri.AbsoluteUri,
            UseShellExecute = true
        };
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Nie udało się otworzyć przeglądarki logowania Google.");
    }

    private static bool FixedTimeEquals(string expected, string? actual)
    {
        if (actual is null)
            return false;

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
               && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static async Task WriteBrowserResponseAsync(
        HttpListenerResponse response,
        bool success,
        CancellationToken cancellationToken)
    {
        var title = success ? "Połączono z Google" : "Nie udało się połączyć z Google";
        var message = success
            ? "Możesz zamknąć tę kartę i wrócić do Meeting Audio Recorder."
            : "Wróć do Meeting Audio Recorder i spróbuj ponownie.";
        var html = $$"""
            <!doctype html>
            <html lang="pl"><head><meta charset="utf-8"><title>{{title}}</title></head>
            <body style="font-family:Segoe UI,sans-serif;padding:2rem">
              <h1>{{title}}</h1><p>{{message}}</p>
            </body></html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        response.Close();
    }
}
