using System.Net.Http.Headers;
using System.Text.Json;

namespace BilibiliCookieReader.Services;

public sealed record DailyHotSearchEntry(int Position, string Keyword, string Label);

public sealed record DailyHotSearchRecordResult(
    bool Recorded,
    DateOnly Date,
    DateTimeOffset CapturedAtTaipei,
    int EntryCount,
    string LogPath);

public sealed class DailyHotSearchRecorder
{
    private const string HotSearchUrl = "https://s.search.bilibili.com/main/hotword";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DailyHotSearchRecorder(HttpClient? http = null, string? logPath = null)
    {
        _http = http ?? CreateClient();
        LogPath = logPath ?? Path.Combine(GitHubSettingsStore.SettingsDirectory, "hot_search.jsonl");
    }

    public string LogPath { get; }

    public async Task<DailyHotSearchRecordResult> RecordTodayAsync(
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var capturedAt = TimeZoneInfo.ConvertTime(now ?? DateTimeOffset.UtcNow, TaipeiTimeZone);
            var date = DateOnly.FromDateTime(capturedAt.DateTime);
            var existing = await FindRecordAsync(date, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return new DailyHotSearchRecordResult(
                    false,
                    date,
                    existing.Value.CapturedAt,
                    existing.Value.EntryCount,
                    LogPath);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, HotSearchUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var entries = ParseEntries(json);

            var record = new StoredRecord(date, capturedAt, entries);
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            await File.AppendAllTextAsync(
                    LogPath,
                    JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine,
                    cancellationToken)
                .ConfigureAwait(false);
            return new DailyHotSearchRecordResult(true, date, capturedAt, entries.Count, LogPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static IReadOnlyList<DailyHotSearchEntry> ParseEntries(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("code", out var code)
            || code.GetInt32() != 0
            || !root.TryGetProperty("list", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Bilibili 熱搜回應格式不正確。");
        }

        var entries = new List<DailyHotSearchEntry>();
        foreach (var item in list.EnumerateArray())
        {
            var keyword = GetString(item, "show_name");
            if (string.IsNullOrWhiteSpace(keyword))
                keyword = GetString(item, "keyword");
            if (string.IsNullOrWhiteSpace(keyword))
                continue;
            var position = item.TryGetProperty("pos", out var positionElement)
                           && positionElement.TryGetInt32(out var parsedPosition)
                ? parsedPosition
                : entries.Count + 1;
            var wordType = item.TryGetProperty("word_type", out var wordTypeElement)
                           && wordTypeElement.TryGetInt32(out var parsedWordType)
                ? parsedWordType
                : 0;
            entries.Add(new DailyHotSearchEntry(position, keyword.Trim(), FormatLabel(wordType)));
        }
        if (entries.Count == 0)
            throw new JsonException("Bilibili 熱搜回應沒有可記錄的項目。");
        return entries;
    }

    private async Task<(DateTimeOffset CapturedAt, int EntryCount)?> FindRecordAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(LogPath))
            return null;
        var lines = await File.ReadAllLinesAsync(LogPath, cancellationToken).ConfigureAwait(false);
        foreach (var line in lines)
        {
            try
            {
                var record = JsonSerializer.Deserialize<StoredRecord>(line, JsonOptions);
                if (record?.Date == date)
                    return (record.CapturedAtTaipei, record.Entries?.Count ?? 0);
            }
            catch (JsonException)
            {
                // Keep valid history usable even if one line was manually damaged.
            }
        }
        return null;
    }

    private static TimeZoneInfo TaipeiTimeZone { get; } = ResolveTaipeiTimeZone();

    private static TimeZoneInfo ResolveTaipeiTimeZone()
    {
        foreach (var id in new[] { "Asia/Taipei", "Taipei Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }
        return TimeZoneInfo.CreateCustomTimeZone("UTC+08:00", TimeSpan.FromHours(8), "台北時間", "台北時間");
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string FormatLabel(int wordType) => wordType switch
    {
        4 => "新",
        5 => "熱",
        7 => "直播中",
        9 => "梗",
        11 => "話題",
        12 => "獨家",
        _ => string.Empty,
    };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private sealed record StoredRecord(
        DateOnly Date,
        DateTimeOffset CapturedAtTaipei,
        IReadOnlyList<DailyHotSearchEntry>? Entries);
}
