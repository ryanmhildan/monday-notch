using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MondayNotch.Services;

public sealed record LyricLine(TimeSpan Time, string Text);

public sealed class Lyrics
{
    public IReadOnlyList<LyricLine> Lines { get; init; } = Array.Empty<LyricLine>();

    /// <summary>True when every line has a timestamp; plain lyrics only have text.</summary>
    public bool IsSynced { get; init; }

    public bool IsInstrumental { get; init; }

    /// <summary>Index of the line being sung at <paramref name="position"/>, or -1 before the first line.</summary>
    public int LineAt(TimeSpan position)
    {
        int low = 0, high = Lines.Count - 1, found = -1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (Lines[mid].Time <= position)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return found;
    }
}

/// <summary>
/// Looks up lyrics on LRCLIB (lrclib.net). Only the title, artist, album and length of the song are sent.
/// </summary>
public sealed class LyricsService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex TimeTag = new(@"\[(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)\]", RegexOptions.Compiled);

    // Video-site decorations that never appear in LRCLIB titles, e.g. "Song (Official Music Video)".
    private static readonly Regex TitleNoise = new(
        @"\s*[\(\[][^\)\]]*\b(official|video|lyrics?|audio|visuali[sz]er|mv|hd|4k|remaster(ed)?)\b[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dictionary<string, Lyrics?> _cache = new();

    public async Task<Lyrics?> FindAsync(string title, string artist, string album, TimeSpan duration, CancellationToken cancellation)
    {
        string cacheKey = $"{artist}\n{title}\n{(int)duration.TotalSeconds}";
        lock (_cache)
        {
            if (_cache.TryGetValue(cacheKey, out Lyrics? cached))
                return cached;
        }

        Lyrics? result = null;
        foreach ((string candidateTitle, string candidateArtist) in Candidates(title, artist))
        {
            LrclibTrack? track = null;
            if (duration > TimeSpan.Zero && album.Length > 0 && candidateArtist.Length > 0)
                track = await GetExactAsync(candidateTitle, candidateArtist, album, duration, cancellation);
            track ??= await SearchAsync(candidateTitle, candidateArtist, duration, cancellation);

            result = track is null ? null : ToLyrics(track);
            if (result is not null)
                break;
        }

        // Network errors throw before this point, so only real "not found" answers are cached.
        lock (_cache)
            _cache[cacheKey] = result;
        return result;
    }

    private static IEnumerable<(string Title, string Artist)> Candidates(string title, string artist)
    {
        string cleanTitle = TitleNoise.Replace(title, "").Trim();
        string cleanArtist = artist.EndsWith(" - Topic", StringComparison.Ordinal) ? artist[..^8] : artist.Trim();
        yield return (cleanTitle, cleanArtist);

        // Browser tabs often report "Artist - Song" as the title and the channel name as the artist.
        int dash = cleanTitle.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
            yield return (cleanTitle[(dash + 3)..].Trim(), cleanTitle[..dash].Trim());
    }

    private static async Task<LrclibTrack?> GetExactAsync(string title, string artist, string album, TimeSpan duration, CancellationToken cancellation)
    {
        string url = "api/get"
            + $"?track_name={Uri.EscapeDataString(title)}"
            + $"&artist_name={Uri.EscapeDataString(artist)}"
            + $"&album_name={Uri.EscapeDataString(album)}"
            + $"&duration={(int)Math.Round(duration.TotalSeconds)}";

        using HttpResponseMessage response = await Http.GetAsync(url, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LrclibTrack>(Json, cancellation);
    }

    private static async Task<LrclibTrack?> SearchAsync(string title, string artist, TimeSpan duration, CancellationToken cancellation)
    {
        string url = artist.Length > 0
            ? $"api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}"
            : $"api/search?q={Uri.EscapeDataString(title)}";

        LrclibTrack[]? results = await Http.GetFromJsonAsync<LrclibTrack[]>(url, Json, cancellation);
        if (results is null)
            return null;

        // Prefer a recording whose length matches the one playing, then synced lyrics. The length is only a
        // preference: right after a track change the player may still report the previous track's length.
        double target = duration.TotalSeconds;
        double Distance(LrclibTrack r) => target <= 0 || r.Duration is null ? 0 : Math.Abs(r.Duration.Value - target);
        return results
            .Where(r => r.Instrumental || !string.IsNullOrWhiteSpace(r.SyncedLyrics) || !string.IsNullOrWhiteSpace(r.PlainLyrics))
            .OrderByDescending(r => Distance(r) <= 5)
            .ThenByDescending(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics))
            .ThenBy(Distance)
            .FirstOrDefault();
    }

    private static Lyrics? ToLyrics(LrclibTrack track)
    {
        if (!string.IsNullOrWhiteSpace(track.SyncedLyrics))
        {
            List<LyricLine> lines = ParseLrc(track.SyncedLyrics);
            if (lines.Count > 0)
                return new Lyrics { Lines = lines, IsSynced = true };
        }

        if (!string.IsNullOrWhiteSpace(track.PlainLyrics))
        {
            LyricLine[] lines = track.PlainLyrics
                .Split('\n')
                .Select(line => new LyricLine(TimeSpan.Zero, line.Trim()))
                .ToArray();
            return new Lyrics { Lines = lines };
        }

        return track.Instrumental ? new Lyrics { IsInstrumental = true } : null;
    }

    /// <summary>Parses LRC text: "[mm:ss.xx] line". A line may carry several time tags when it repeats.</summary>
    private static List<LyricLine> ParseLrc(string lrc)
    {
        var lines = new List<LyricLine>();
        foreach (string raw in lrc.Split('\n'))
        {
            MatchCollection tags = TimeTag.Matches(raw);
            if (tags.Count == 0)
                continue;

            string text = TimeTag.Replace(raw, "").Trim();
            foreach (Match tag in tags)
            {
                int minutes = int.Parse(tag.Groups[1].Value, CultureInfo.InvariantCulture);
                double seconds = double.Parse(tag.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                lines.Add(new LyricLine(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds), text));
            }
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://lrclib.net/"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        // LRCLIB asks clients to identify themselves.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MondayNotch/0.1.0");
        return client;
    }

    private sealed class LrclibTrack
    {
        public double? Duration { get; set; }
        public bool Instrumental { get; set; }
        public string? PlainLyrics { get; set; }
        public string? SyncedLyrics { get; set; }
    }
}
