using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ElectronicLogbook.Updater;

internal interface ISystemBrowserGoogleOAuthFlow
{
    Task<Uri> AuthorizeAsync(
        Func<Uri, Uri> createAuthorizationUri,
        CancellationToken cancellationToken = default);
}

internal sealed class SystemBrowserGoogleOAuthFlow : ISystemBrowserGoogleOAuthFlow
{
    private const int MaximumRequestHeaderCharacters = 16_384;
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(10);

    public static SystemBrowserGoogleOAuthFlow Instance { get; } = new();

    private SystemBrowserGoogleOAuthFlow()
    {
    }

    public async Task<Uri> AuthorizeAsync(
        Func<Uri, Uri> createAuthorizationUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createAuthorizationUri);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 2);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var callbackToken = Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
        var callbackUri = new Uri($"http://127.0.0.1:{port}/flightlogx-auth/{callbackToken}/");
        var authorizationUri = createAuthorizationUri(callbackUri);
        if (authorizationUri.Scheme != Uri.UriSchemeHttps && !authorizationUri.IsLoopback)
        {
            throw new InvalidOperationException("Google sign-in did not return a secure browser address.");
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo(authorizationUri.AbsoluteUri)
            {
                UseShellExecute = true
            }) ?? throw new InvalidOperationException("Windows could not open the system browser for Google sign-in.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                "Windows could not open the system browser for Google sign-in.",
                ex);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SignInTimeout);
        while (true)
        {
            TcpClient browser;
            try
            {
                browser = await listener.AcceptTcpClientAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Google sign-in did not finish within 10 minutes.");
            }

            using (browser)
            {
                var callback = await ReadCallbackAsync(browser, callbackUri, timeout.Token);
                if (callback is null)
                {
                    continue;
                }

                await WriteBrowserResponseAsync(
                    browser,
                    HasAuthorizationCode(callback.Query),
                    timeout.Token);
                return callback;
            }
        }
    }

    private static async Task<Uri?> ReadCallbackAsync(
        TcpClient browser,
        Uri expectedCallback,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            browser.GetStream(),
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return null;
        }

        var headerCharacters = requestLine.Length;
        while (true)
        {
            var header = await reader.ReadLineAsync(cancellationToken);
            if (header is null || header.Length == 0)
            {
                break;
            }

            headerCharacters += header.Length;
            if (headerCharacters > MaximumRequestHeaderCharacters)
            {
                return null;
            }
        }

        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !string.Equals(parts[0], "GET", StringComparison.Ordinal))
        {
            return null;
        }

        if (!Uri.TryCreate(expectedCallback, parts[1], out var callback)
            || !string.Equals(callback.Scheme, expectedCallback.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(callback.Host, expectedCallback.Host, StringComparison.OrdinalIgnoreCase)
            || callback.Port != expectedCallback.Port
            || !string.Equals(callback.AbsolutePath, expectedCallback.AbsolutePath, StringComparison.Ordinal))
        {
            return null;
        }

        return callback;
    }

    private static async Task WriteBrowserResponseAsync(
        TcpClient browser,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        var title = succeeded ? "Google sign-in complete" : "Google sign-in was not completed";
        var detail = succeeded
            ? "You can close this window and return to FlightLogX."
            : "Return to FlightLogX for the next step.";
        var body = Encoding.UTF8.GetBytes($$"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><title>{{title}}</title></head>
            <body style="font-family:Segoe UI,Arial,sans-serif;margin:3rem;max-width:42rem">
            <h1>{{title}}</h1><p>{{detail}}</p>
            </body>
            </html>
            """);
        var header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n");
        var stream = browser.GetStream();
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static bool HasAuthorizationCode(string query) =>
        query.StartsWith("?code=", StringComparison.OrdinalIgnoreCase)
        || query.Contains("&code=", StringComparison.OrdinalIgnoreCase);

    internal static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
