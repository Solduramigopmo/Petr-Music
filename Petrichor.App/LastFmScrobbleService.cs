using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Petrichor.Core.Domain;
using Petrichor.Media.Playback;

namespace Petrichor.App;

internal sealed class LastFmScrobbleService
{
    private const string ApiBaseUrl = "https://ws.audioscrobbler.com/2.0/";
    private const string AuthBaseUrl = "https://www.last.fm/api/auth/";
    private const double MinimumTrackDurationSeconds = 30;
    private const double ScrobbleThresholdPercentage = 0.5;
    private const double ScrobbleThresholdSeconds = 240;
    private const int MaxRequestAttempts = 3;

    private readonly HttpClient httpClient;
    private readonly LastFmSessionStore sessionStore;
    private readonly SemaphoreSlim sessionSync = new(1, 1);

    private LastFmSession? session;
    private TrackReference? activeTrack;
    private DateTimeOffset? activeTrackStartedAt;
    private double maxObservedPositionSeconds;
    private bool nowPlayingSent;
    private bool scrobbled;

    public LastFmScrobbleService(LastFmSessionStore sessionStore, HttpClient? httpClient = null)
    {
        this.sessionStore = sessionStore;
        this.httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public bool IsConnected => session is not null;
    public string Username => session?.Username ?? string.Empty;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        session = await sessionStore.LoadAsync(cancellationToken);
    }

    public async Task<(bool Success, string Message)> ConnectAsync(string apiKey, string sharedSecret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(sharedSecret))
        {
            return (false, "Enter Last.fm API key and shared secret first.");
        }

        var callbackPrefix = "http://127.0.0.1:51515/lastfm-callback/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(callbackPrefix);

        try
        {
            listener.Start();
        }
        catch
        {
            return (false, "Could not open local callback listener for Last.fm authentication.");
        }

        var callbackUrl = callbackPrefix;
        var authUrl = $"{AuthBaseUrl}?api_key={Uri.EscapeDataString(apiKey)}&cb={Uri.EscapeDataString(callbackUrl)}";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            return (false, "Could not open browser for Last.fm authentication.");
        }

        HttpListenerContext? context;
        try
        {
            var contextTask = listener.GetContextAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);
            var completed = await Task.WhenAny(contextTask, timeoutTask);
            if (completed != contextTask)
            {
                return (false, "Last.fm authentication timed out.");
            }

            context = await contextTask;
        }
        catch
        {
            return (false, "Failed to receive Last.fm authentication callback.");
        }

        var token = context.Request.QueryString["token"];
        await WriteAuthCallbackResponseAsync(context.Response, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "Last.fm callback did not contain auth token.");
        }

        var sessionResult = await ExchangeTokenAsync(apiKey, sharedSecret, token, cancellationToken);
        if (!sessionResult.Success || sessionResult.Session is null)
        {
            return (false, sessionResult.Message);
        }

        await sessionSync.WaitAsync(cancellationToken);
        try
        {
            session = sessionResult.Session;
            await sessionStore.SaveAsync(sessionResult.Session, cancellationToken);
        }
        finally
        {
            sessionSync.Release();
        }

        return (true, $"Connected as {sessionResult.Session.Username}");
    }

    public void Disconnect()
    {
        session = null;
        sessionStore.Delete();
    }

    public async Task HandlePlaybackSnapshotAsync(
        PlaybackTransportState state,
        TrackReference? track,
        TimeSpan position,
        TimeSpan duration,
        bool scrobblingEnabled,
        string apiKey,
        string sharedSecret,
        CancellationToken cancellationToken = default)
    {
        if (!scrobblingEnabled || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(sharedSecret))
        {
            return;
        }

        if (session is null)
        {
            return;
        }

        if (!TracksMatch(activeTrack, track))
        {
            await TryScrobbleIfEligibleAsync(apiKey, sharedSecret, cancellationToken);

            activeTrack = track;
            activeTrackStartedAt = track is null ? null : DateTimeOffset.UtcNow;
            maxObservedPositionSeconds = 0;
            nowPlayingSent = false;
            scrobbled = false;
        }

        if (track is null)
        {
            return;
        }

        maxObservedPositionSeconds = Math.Max(maxObservedPositionSeconds, Math.Max(0, position.TotalSeconds));

        if (state == PlaybackTransportState.Playing && !nowPlayingSent)
        {
            var nowPlayingSentSuccessfully = await SendNowPlayingAsync(track, apiKey, sharedSecret, cancellationToken);
            nowPlayingSent = nowPlayingSentSuccessfully;
        }

        if (state == PlaybackTransportState.Stopped)
        {
            await TryScrobbleIfEligibleAsync(apiKey, sharedSecret, cancellationToken);
        }
    }

    private async Task TryScrobbleIfEligibleAsync(string apiKey, string sharedSecret, CancellationToken cancellationToken)
    {
        if (activeTrack is null || scrobbled || session is null)
        {
            return;
        }

        var durationSeconds = activeTrack.DurationSeconds;
        if (durationSeconds < MinimumTrackDurationSeconds)
        {
            return;
        }

        var threshold = Math.Min(durationSeconds * ScrobbleThresholdPercentage, ScrobbleThresholdSeconds);
        var elapsedSinceStartSeconds = activeTrackStartedAt is null
            ? 0
            : Math.Max(0, (DateTimeOffset.UtcNow - activeTrackStartedAt.Value).TotalSeconds);
        var playedSeconds = Math.Max(maxObservedPositionSeconds, elapsedSinceStartSeconds);

        if (playedSeconds < threshold)
        {
            return;
        }

        scrobbled = await ScrobbleAsync(activeTrack, apiKey, sharedSecret, cancellationToken);
    }

    private async Task<bool> SendNowPlayingAsync(TrackReference track, string apiKey, string sharedSecret, CancellationToken cancellationToken)
    {
        if (session is null || string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
        {
            return false;
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "track.updateNowPlaying",
            ["api_key"] = apiKey,
            ["sk"] = session.SessionKey,
            ["artist"] = track.Artist ?? string.Empty,
            ["track"] = track.Title
        };

        if (!string.IsNullOrWhiteSpace(track.Album) &&
            !string.Equals(track.Album, "Unknown Album", StringComparison.OrdinalIgnoreCase))
        {
            parameters["album"] = track.Album;
        }

        if (track.DurationSeconds > 0)
        {
            parameters["duration"] = ((int)Math.Round(track.DurationSeconds)).ToString();
        }

        var response = await ExecuteSignedPostAsync(parameters, sharedSecret, cancellationToken);
        return response is not null && !response.RootElement.TryGetProperty("error", out _);
    }

    private async Task<bool> ScrobbleAsync(TrackReference track, string apiKey, string sharedSecret, CancellationToken cancellationToken)
    {
        if (session is null || string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
        {
            return false;
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "track.scrobble",
            ["api_key"] = apiKey,
            ["sk"] = session.SessionKey,
            ["artist"] = track.Artist ?? string.Empty,
            ["track"] = track.Title,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
        };

        if (!string.IsNullOrWhiteSpace(track.Album) &&
            !string.Equals(track.Album, "Unknown Album", StringComparison.OrdinalIgnoreCase))
        {
            parameters["album"] = track.Album;
        }

        if (track.DurationSeconds > 0)
        {
            parameters["duration"] = ((int)Math.Round(track.DurationSeconds)).ToString();
        }

        var response = await ExecuteSignedPostAsync(parameters, sharedSecret, cancellationToken);
        if (response is null)
        {
            return false;
        }

        if (response.RootElement.TryGetProperty("error", out _))
        {
            return false;
        }

        if (response.RootElement.TryGetProperty("scrobbles", out var scrobbles) &&
            scrobbles.TryGetProperty("@attr", out var attr) &&
            attr.TryGetProperty("accepted", out var acceptedElement))
        {
            return acceptedElement.TryGetInt32(out var accepted) && accepted > 0;
        }

        return false;
    }

    private async Task<(bool Success, LastFmSession? Session, string Message)> ExchangeTokenAsync(
        string apiKey,
        string sharedSecret,
        string token,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "auth.getSession",
            ["api_key"] = apiKey,
            ["token"] = token
        };

        var response = await ExecuteSignedGetAsync(parameters, sharedSecret, cancellationToken);
        if (response is null)
        {
            return (false, null, "Failed to exchange auth token with Last.fm.");
        }

        if (response.RootElement.TryGetProperty("error", out var errorElement))
        {
            var message = response.RootElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? "Last.fm returned an error."
                : "Last.fm returned an error.";
            return (false, null, $"Last.fm error {errorElement.GetRawText()}: {message}");
        }

        if (!response.RootElement.TryGetProperty("session", out var sessionElement))
        {
            return (false, null, "Last.fm response did not include a session.");
        }

        if (!sessionElement.TryGetProperty("key", out var keyElement) ||
            !sessionElement.TryGetProperty("name", out var nameElement))
        {
            return (false, null, "Last.fm response is missing session details.");
        }

        var sessionKey = keyElement.GetString();
        var username = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(sessionKey) || string.IsNullOrWhiteSpace(username))
        {
            return (false, null, "Last.fm session details are invalid.");
        }

        return (true, new LastFmSession(sessionKey, username), string.Empty);
    }

    private async Task<JsonDocument?> ExecuteSignedGetAsync(
        Dictionary<string, string> parameters,
        string sharedSecret,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxRequestAttempts; attempt++)
        {
            try
            {
                parameters["api_sig"] = GenerateSignature(parameters, sharedSecret);
                parameters["format"] = "json";

                var url = $"{ApiBaseUrl}?{BuildQueryString(parameters)}";
                using var response = await httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    if (attempt < MaxRequestAttempts)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                        continue;
                    }

                    return null;
                }

                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (attempt >= MaxRequestAttempts)
                {
                    return null;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }

        return null;
    }

    private async Task<JsonDocument?> ExecuteSignedPostAsync(
        Dictionary<string, string> parameters,
        string sharedSecret,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxRequestAttempts; attempt++)
        {
            try
            {
                parameters["api_sig"] = GenerateSignature(parameters, sharedSecret);
                parameters["format"] = "json";

                using var content = new FormUrlEncodedContent(parameters);
                using var response = await httpClient.PostAsync(ApiBaseUrl, content, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    if (attempt < MaxRequestAttempts)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                        continue;
                    }

                    return null;
                }

                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (attempt >= MaxRequestAttempts)
                {
                    return null;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }

        return null;
    }

    private static string GenerateSignature(Dictionary<string, string> parameters, string sharedSecret)
    {
        var signatureBase = string.Join(
            string.Empty,
            parameters
                .Where(pair => !string.Equals(pair.Key, "format", StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}{pair.Value}")) + sharedSecret;

        var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(signatureBase));
        return Convert.ToHexStringLower(hashBytes);
    }

    private static string BuildQueryString(Dictionary<string, string> parameters)
    {
        return string.Join("&", parameters.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    private static bool TracksMatch(TrackReference? left, TrackReference? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.TrackId is not null && right.TrackId is not null)
        {
            return left.TrackId == right.TrackId;
        }

        return string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteAuthCallbackResponseAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        try
        {
            const string html = """
                                <html><head><meta charset="utf-8"/></head>
                                <body style="font-family:Segoe UI;background:#101418;color:#EAF2FB;padding:24px;">
                                <h2>Petrichor</h2>
                                <p>Last.fm authorization received. You can close this tab and return to Petrichor.</p>
                                </body></html>
                                """;
            var bytes = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, cancellationToken);
        }
        catch
        {
            // best effort response body
        }
        finally
        {
            response.OutputStream.Close();
        }
    }
}
