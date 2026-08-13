using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BilibiliCookieReader.Services;

public sealed record RankingCategoryDefinition(
    string Key,
    string Name,
    string Api,
    int Value);

public sealed record DailyRankingEntry(
    int Position,
    string ContentId,
    string Title,
    string Uploader,
    long? Views,
    long? Danmaku,
    long? Favorites,
    long? Likes,
    long? Coins,
    long? Shares,
    long? Followers,
    double? Rating,
    long? RankingScore,
    string Progress,
    int? DurationSeconds,
    long? PublishedAtUnix,
    string CoverUrl,
    string Url);

public sealed record DailyRankingCategory(
    string Name,
    IReadOnlyList<DailyRankingEntry> Entries);

public sealed record DailyRankingRecordResult(
    bool Changed,
    bool Complete,
    DateOnly Date,
    DateTimeOffset CapturedAtTaipei,
    int CategoryCount,
    int ExpectedCategoryCount,
    string LogPath);

public sealed class DailyRankingRecorder
{
    private const string ApiRoot = "https://api.bilibili.com";
    private const string NormalPath = "/x/web-interface/ranking/v2";
    private const string PgcWebPath = "/pgc/web/rank/list";
    private const string PgcSeasonPath = "/pgc/season/rank/web/list";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    public static IReadOnlyList<RankingCategoryDefinition> Categories { get; } =
    [
        new("all", "全部", "normal", 0),
        new("anime", "番劇", "pgcWeb", 1),
        new("guochuang", "國創", "pgcSeason", 4),
        new("documentary", "紀錄片", "pgcSeason", 3),
        new("movie", "電影", "pgcSeason", 2),
        new("tv", "電視劇", "pgcSeason", 5),
        new("variety", "綜藝", "pgcSeason", 7),
        new("animation", "動畫", "normal", 1),
        new("game", "遊戲", "normal", 4),
        new("kichiku", "鬼畜", "normal", 119),
        new("music", "音樂", "normal", 3),
        new("dance", "舞蹈", "normal", 129),
        new("cinephile", "影視", "normal", 181),
        new("entertainment", "娛樂", "normal", 5),
        new("knowledge", "知識", "normal", 36),
        new("tech", "科技數碼", "normal", 188),
        new("food", "美食", "normal", 211),
        new("car", "汽車", "normal", 223),
        new("fashion", "時尚美妝", "normal", 155),
        new("sports", "體育運動", "normal", 234),
        new("animal", "動物", "normal", 217),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DailyRankingRecorder(HttpClient? http = null, string? logPath = null)
    {
        _http = http ?? CreateClient();
        LogPath = logPath ?? Path.Combine(GitHubSettingsStore.SettingsDirectory, "ranking.jsonl");
    }

    public string LogPath { get; }

    public async Task<DailyRankingRecordResult> RecordTodayAsync(
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var capturedAt = TimeZoneInfo.ConvertTime(now ?? DateTimeOffset.UtcNow, TaipeiTimeZone);
            var date = DateOnly.FromDateTime(capturedAt.DateTime);
            var records = await ReadRecordsAsync(cancellationToken).ConfigureAwait(false);
            var record = records.FirstOrDefault(item => item.Date == date);
            if (record is null)
            {
                record = new StoredRecord(
                    date,
                    capturedAt,
                    false,
                    0,
                    new Dictionary<string, DailyRankingCategory>());
                records.Add(record);
            }

            var categories = new Dictionary<string, DailyRankingCategory>(record.Categories);
            if (Categories.All(category => categories.ContainsKey(category.Key)))
            {
                return BuildResult(false, date, record.CapturedAtTaipei, categories.Count);
            }

            var changed = false;
            foreach (var category in Categories)
            {
                if (categories.ContainsKey(category.Key))
                    continue;
                try
                {
                    var entries = await FetchCategoryAsync(category, cancellationToken).ConfigureAwait(false);
                    categories[category.Key] = new DailyRankingCategory(category.Name, entries);
                    changed = true;
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
                {
                    if (ex is TaskCanceledException && cancellationToken.IsCancellationRequested)
                        throw;
                    // Keep successful categories; the hourly pass retries only missing ones.
                }
            }

            if (!changed && categories.Count == 0)
                throw new HttpRequestException("所有排行榜分類皆無法取得。");

            var updated = new StoredRecord(
                date,
                capturedAt,
                Categories.All(category => categories.ContainsKey(category.Key)),
                categories.Count,
                categories);
            records[records.IndexOf(record)] = updated;
            await WriteRecordsAsync(records, cancellationToken).ConfigureAwait(false);
            return BuildResult(changed, date, capturedAt, categories.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static IReadOnlyList<DailyRankingEntry> ParseNormalEntries(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!TryGetList(root, "data", out var list))
            throw new JsonException("Bilibili 一般排行榜回應格式不正確。");

        var entries = new List<DailyRankingEntry>();
        foreach (var item in list.EnumerateArray())
        {
            var bvid = GetString(item, "bvid");
            var title = GetString(item, "title");
            if (string.IsNullOrWhiteSpace(bvid) || string.IsNullOrWhiteSpace(title))
                continue;
            var owner = TryGetObject(item, "owner");
            var stat = TryGetObject(item, "stat");
            entries.Add(new DailyRankingEntry(
                entries.Count + 1,
                bvid,
                title,
                GetString(owner, "name"),
                GetLong(stat, "view"),
                GetLong(stat, "danmaku"),
                GetLong(stat, "favorite"),
                GetLong(stat, "like"),
                GetLong(stat, "coin"),
                GetLong(stat, "share"),
                null,
                null,
                GetLong(item, "score"),
                string.Empty,
                GetInt(item, "duration"),
                GetLong(item, "pubdate"),
                GetString(item, "pic"),
                $"https://www.bilibili.com/video/{bvid}"));
        }
        return entries.Count > 0
            ? entries
            : throw new JsonException("Bilibili 一般排行榜沒有可記錄的項目。");
    }

    public static IReadOnlyList<DailyRankingEntry> ParsePgcEntries(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var containerName = root.TryGetProperty("result", out _) ? "result" : "data";
        if (!TryGetList(root, containerName, out var list))
            throw new JsonException("Bilibili PGC 排行榜回應格式不正確。");

        var entries = new List<DailyRankingEntry>();
        foreach (var item in list.EnumerateArray())
        {
            var title = GetString(item, "title");
            if (string.IsNullOrWhiteSpace(title))
                continue;
            var stat = TryGetObject(item, "stat");
            var rating = TryGetObject(item, "rating");
            var newEpisode = TryGetObject(item, "new_ep");
            var url = GetString(item, "url");
            var seasonId = GetLong(item, "season_id");
            var progress = GetString(newEpisode, "index_show");
            if (string.IsNullOrWhiteSpace(progress))
                progress = GetString(item, "desc");
            entries.Add(new DailyRankingEntry(
                entries.Count + 1,
                seasonId?.ToString() ?? url,
                title,
                string.Empty,
                GetLong(stat, "view"),
                GetLong(stat, "danmaku"),
                null,
                null,
                null,
                null,
                GetLong(stat, "follow"),
                GetDouble(rating, "score"),
                null,
                progress,
                null,
                null,
                GetString(item, "cover"),
                url));
        }
        return entries.Count > 0
            ? entries
            : throw new JsonException("Bilibili PGC 排行榜沒有可記錄的項目。");
    }

    private async Task<IReadOnlyList<DailyRankingEntry>> FetchCategoryAsync(
        RankingCategoryDefinition category,
        CancellationToken cancellationToken)
    {
        var normal = category.Api == "normal";
        var path = normal ? NormalPath : category.Api == "pgcWeb" ? PgcWebPath : PgcSeasonPath;
        var query = normal
            ? $"rid={category.Value}&type=all"
            : $"day=3&season_type={category.Value}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiRoot}{path}?{query}");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/v/popular/rank/all/");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return normal ? ParseNormalEntries(json) : ParsePgcEntries(json);
    }

    private async Task<List<StoredRecord>> ReadRecordsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(LogPath))
            return [];
        var records = new List<StoredRecord>();
        foreach (var line in await File.ReadAllLinesAsync(LogPath, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var record = JsonSerializer.Deserialize<StoredRecord>(line, JsonOptions);
                if (record is not null)
                    records.Add(record);
            }
            catch (JsonException)
            {
            }
        }
        return records;
    }

    private async Task WriteRecordsAsync(IReadOnlyList<StoredRecord> records, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(LogPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var builder = new StringBuilder();
        foreach (var record in records)
            builder.AppendLine(JsonSerializer.Serialize(record, JsonOptions));
        var temporary = LogPath + ".tmp";
        await File.WriteAllTextAsync(temporary, builder.ToString(), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, LogPath, overwrite: true);
    }

    private DailyRankingRecordResult BuildResult(
        bool changed,
        DateOnly date,
        DateTimeOffset capturedAt,
        int categoryCount) =>
        new(
            changed,
            categoryCount == Categories.Count,
            date,
            capturedAt,
            categoryCount,
            Categories.Count,
            LogPath);

    private static bool TryGetList(JsonElement root, string containerName, out JsonElement list)
    {
        list = default;
        return root.TryGetProperty("code", out var code)
               && code.GetInt32() == 0
               && root.TryGetProperty(containerName, out var container)
               && container.ValueKind == JsonValueKind.Object
               && container.TryGetProperty("list", out list)
               && list.ValueKind == JsonValueKind.Array;
    }

    private static JsonElement TryGetObject(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static string GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long? GetLong(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.TryGetInt64(out var parsed)
            ? parsed
            : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static double? GetDouble(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.TryGetDouble(out var parsed)
            ? parsed
            : null;

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

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private sealed record StoredRecord(
        DateOnly Date,
        DateTimeOffset CapturedAtTaipei,
        bool Complete,
        int CategoryCount,
        IReadOnlyDictionary<string, DailyRankingCategory> Categories);
}
