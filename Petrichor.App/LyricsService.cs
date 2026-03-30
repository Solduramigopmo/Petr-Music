using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Petrichor.Core.Domain;

namespace Petrichor.App;

internal sealed class LyricsService
{
    private const string LrcLibBaseUrl = "https://lrclib.net/api/get";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;

    public LyricsService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task<LyricsLoadResult> LoadLyricsAsync(
        TrackReference track,
        bool allowOnlineFetch,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(track.Path) || !File.Exists(track.Path))
        {
            return new LyricsLoadResult(string.Empty, LyricsSourceKind.None);
        }

        var externalLyrics = await TryLoadExternalLyricsAsync(track.Path, cancellationToken);
        if (!string.IsNullOrWhiteSpace(externalLyrics.Text))
        {
            return externalLyrics;
        }

        var embeddedLyrics = TryLoadEmbeddedLyrics(track.Path);
        if (!string.IsNullOrWhiteSpace(embeddedLyrics))
        {
            return new LyricsLoadResult(StripTimestamps(embeddedLyrics), LyricsSourceKind.Embedded);
        }

        if (!allowOnlineFetch)
        {
            return new LyricsLoadResult(string.Empty, LyricsSourceKind.None);
        }

        var onlineLyrics = await TryLoadOnlineLyricsAsync(track, cancellationToken);
        if (!string.IsNullOrWhiteSpace(onlineLyrics))
        {
            return new LyricsLoadResult(StripTimestamps(onlineLyrics), LyricsSourceKind.Online);
        }

        return new LyricsLoadResult(string.Empty, LyricsSourceKind.None);
    }

    private static async Task<LyricsLoadResult> TryLoadExternalLyricsAsync(string trackPath, CancellationToken cancellationToken)
    {
        var basePath = Path.ChangeExtension(trackPath, null) ?? trackPath;
        var candidates = new[]
        {
            (Path: $"{basePath}.lrc", Source: LyricsSourceKind.Lrc, Parser: (Func<string, string>)ParseLrc),
            (Path: $"{basePath}.srt", Source: LyricsSourceKind.Srt, Parser: (Func<string, string>)ParseSrt)
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate.Path))
            {
                continue;
            }

            var text = await TryReadTextWithEncodingFallbackAsync(candidate.Path, cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            return new LyricsLoadResult(
                candidate.Parser(text),
                candidate.Source);
        }

        return new LyricsLoadResult(string.Empty, LyricsSourceKind.None);
    }

    private static async Task<string> TryReadTextWithEncodingFallbackAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                return System.Text.Encoding.Default.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    private static string TryLoadEmbeddedLyrics(string path)
    {
        try
        {
            using var tagFile = TagLib.File.Create(path);
            return tagFile.Tag.Lyrics ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<string> TryLoadOnlineLyricsAsync(TrackReference track, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(track.Title) ||
            string.IsNullOrWhiteSpace(track.Artist) ||
            string.Equals(track.Artist, "Unknown Artist", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var firstAttempt = await QueryLrcLibAsync(track, includeAlbum: true, cancellationToken);
        if (!string.IsNullOrWhiteSpace(firstAttempt))
        {
            return firstAttempt;
        }

        return await QueryLrcLibAsync(track, includeAlbum: false, cancellationToken);
    }

    private async Task<string> QueryLrcLibAsync(TrackReference track, bool includeAlbum, CancellationToken cancellationToken)
    {
        try
        {
            var query = new List<string>
            {
                $"track_name={Uri.EscapeDataString(track.Title)}",
                $"artist_name={Uri.EscapeDataString(track.Artist ?? string.Empty)}"
            };

            if (includeAlbum &&
                !string.IsNullOrWhiteSpace(track.Album) &&
                !string.Equals(track.Album, "Unknown Album", StringComparison.OrdinalIgnoreCase))
            {
                query.Add($"album_name={Uri.EscapeDataString(track.Album)}");
            }

            if (track.DurationSeconds > 0)
            {
                query.Add($"duration={track.DurationSeconds:0}");
            }

            var requestUrl = $"{LrcLibBaseUrl}?{string.Join("&", query)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.UserAgent.ParseAdd("Petrichor-Windows/0.1");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return string.Empty;
            }

            var payload = await response.Content.ReadFromJsonAsync<LrcLibResponse>(JsonSerializerOptions, cancellationToken);
            if (payload is null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(payload.SyncedLyrics))
            {
                return payload.SyncedLyrics;
            }

            return payload.PlainLyrics ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ParseLrc(string content)
    {
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var lyricsLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                var endBracketIndex = line.IndexOf(']');
                if (endBracketIndex > 0)
                {
                    var tag = line[1..endBracketIndex];
                    var hasDigits = tag.Any(char.IsDigit);
                    var hasColon = tag.Contains(':');

                    // Skip metadata tags such as [ar:], [ti:], [al:]
                    if (hasColon && !hasDigits)
                    {
                        continue;
                    }
                }
            }

            lyricsLines.Add(line);
        }

        return StripTimestamps(string.Join(Environment.NewLine, lyricsLines));
    }

    private static string ParseSrt(string content)
    {
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var lyricsLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.Contains("-->", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.All(char.IsDigit))
            {
                continue;
            }

            lyricsLines.Add(line);
        }

        return string.Join(Environment.NewLine, lyricsLines);
    }

    private static string StripTimestamps(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var lines = content.Split(['\r', '\n'], StringSplitOptions.None);
        var output = new List<string>(lines.Length);

        foreach (var rawLine in lines)
        {
            var line = rawLine;

            while (line.StartsWith("[", StringComparison.Ordinal))
            {
                var endBracket = line.IndexOf(']');
                if (endBracket < 0)
                {
                    break;
                }

                var tag = line[1..endBracket];
                var isTimestamp = tag.Contains(':') && tag.Any(char.IsDigit);
                if (!isTimestamp)
                {
                    break;
                }

                line = line[(endBracket + 1)..];
            }

            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                output.Add(trimmed);
            }
        }

        return string.Join(Environment.NewLine, output);
    }

    private sealed record LrcLibResponse(
        string? SyncedLyrics,
        string? PlainLyrics);
}
