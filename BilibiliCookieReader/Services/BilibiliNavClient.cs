using System.Net.Http.Headers;
using System.Text.Json;

namespace BilibiliCookieReader.Services;

public sealed record NavCheckResult(
    bool Ok,
    string Message,
    string? UserName = null,
    long? Mid = null,
    double? Coins = null,
    int? Level = null,
    long? CurrentExperience = null,
    long? NextExperience = null,
    string? ProfileSignature = null,
    byte[]? ProfileImage = null,
    LatestDynamicInfo? LatestDynamic = null,
    LatestSubmissionInfo? LatestSubmission = null,
    FavoriteInfo? Favorite = null,
    BangumiFollowInfo? BangumiFollow = null,
    HomepageRecommendationInfo? HomepageRecommendation = null,
    DynamicFeedInfo? DynamicFeed = null,
    BangumiRecommendationInfo? BangumiRecommendation = null,
    LiveRecommendationInfo? LiveRecommendation = null,
    PopularVideoInfo? PopularVideo = null,
    RankingVideoInfo? RankingVideo = null,
    HotSearchInfo? HotSearch = null);

public sealed record LatestDynamicInfo(
    string Type,
    string Text,
    DateTimeOffset? PublishedAt,
    string Url,
    byte[]? CoverImage);

public sealed record LatestSubmissionInfo(
    int? TotalCount,
    string? Title,
    DateTimeOffset? PublishedAt,
    string? Url,
    string? PlayCount,
    byte[]? CoverImage);

public sealed record FavoriteInfo(
    int FolderCount,
    string FolderTitle,
    int ItemCount,
    string? LatestTitle,
    DateTimeOffset? FavoritedAt,
    string? Url,
    byte[]? CoverImage);

public sealed record BangumiFollowInfo(
    int? AnimeCount,
    FollowedSeasonInfo? LatestAnime,
    int? DramaCount,
    FollowedSeasonInfo? LatestDrama);

public sealed record FollowedSeasonInfo(
    string Title,
    string Progress,
    string Url,
    byte[]? CoverImage);

public sealed record HomepageRecommendationInfo(
    string Title,
    string Uploader,
    string Statistics,
    string Url,
    byte[]? CoverImage);

public sealed record DynamicFeedInfo(
    string Author,
    string Type,
    string Text,
    DateTimeOffset? PublishedAt,
    string Url,
    byte[]? CoverImage);

public sealed record BangumiRecommendationInfo(
    string Title,
    string Subtitle,
    string Progress,
    string Rating,
    string Badge,
    string Url,
    byte[]? CoverImage);

public sealed record LiveRecommendationInfo(
    string Title,
    string Uploader,
    string Area,
    long? Online,
    string Url,
    byte[]? CoverImage);

public sealed record PopularVideoInfo(
    string Title,
    string Uploader,
    string Reason,
    long? Views,
    long? Danmaku,
    string Url,
    byte[]? CoverImage);

public sealed record RankingVideoInfo(
    int Position,
    string Title,
    string Uploader,
    long? Views,
    long? Danmaku,
    string Url,
    byte[]? CoverImage);

public sealed record HotSearchInfo(
    string Summary,
    string TopKeyword,
    string Url);

public static class BilibiliNavClient
{
    private sealed record ProfileDetails(string? Signature, byte[]? Image);

    private const string NavUrl = "https://api.bilibili.com/x/web-interface/nav";
    private const string MyInfoUrl = "https://api.bilibili.com/x/space/v2/myinfo";
    private const string DynamicSpaceUrl =
        "https://api.bilibili.com/x/polymer/web-dynamic/v1/feed/space";
    private const string SpaceNavCountUrl = "https://api.bilibili.com/x/space/navnum";
    private const string FavoriteFoldersUrl =
        "https://api.bilibili.com/x/v3/fav/folder/created/list-all";
    private const string FavoriteResourcesUrl =
        "https://api.bilibili.com/x/v3/fav/resource/list";
    private const string BangumiFollowUrl =
        "https://api.bilibili.com/x/space/bangumi/follow/list";
    private const string HomepageRecommendationUrl =
        "https://api.bilibili.com/x/web-interface/wbi/index/top/feed/rcmd";
    private const string DynamicFeedUrl =
        "https://api.bilibili.com/x/polymer/web-dynamic/v1/feed/all";
    private const string BangumiIndexUrl =
        "https://api.bilibili.com/pgc/season/index/result";
    private const string LiveRecommendationUrl =
        "https://api.live.bilibili.com/xlive/web-interface/v1/webMain/getMoreRecList";
    private const string PopularVideoUrl =
        "https://api.bilibili.com/x/web-interface/popular";
    private const string RankingVideoUrl =
        "https://api.bilibili.com/x/web-interface/ranking/v2";
    private const string HotSearchUrl = "https://s.search.bilibili.com/main/hotword";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    private static readonly HttpClient Http = CreateClient();

    public static async Task<NavCheckResult> CheckAsync(
        BilibiliCookieSet cookies,
        CancellationToken cancellationToken = default)
    {
        if (!cookies.HasAll)
        {
            return new NavCheckResult(false, "四個 Cookie 欄位不齊，無法驗證登入。");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, NavUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
        request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new NavCheckResult(false, $"連線失敗：{ex.Message}");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return new NavCheckResult(false, $"HTTP {(int)response.StatusCode}：{Trim(body)}");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var code = root.TryGetProperty("code", out var codeEl) ? codeEl.GetInt32() : int.MinValue;
            if (code == -101)
                return new NavCheckResult(false, "Cookie 已失效或尚未登入（code=-101）。");
            if (code != 0)
            {
                var message = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null;
                return new NavCheckResult(false, $"驗證失敗：{message ?? $"code={code}"}");
            }

            if (!root.TryGetProperty("data", out var data))
                return new NavCheckResult(false, "驗證回應缺少 data。");

            var isLogin = data.TryGetProperty("isLogin", out var loginEl) && loginEl.GetBoolean();
            if (!isLogin)
                return new NavCheckResult(false, "Bilibili 回報尚未登入。");

            var uname = data.TryGetProperty("uname", out var unameEl) ? unameEl.GetString() : null;
            long? mid = data.TryGetProperty("mid", out var midEl) && midEl.TryGetInt64(out var midValue)
                ? midValue
                : null;
            double? coins = data.TryGetProperty("money", out var moneyEl) && moneyEl.TryGetDouble(out var money)
                ? money
                : null;
            int? level = null;
            long? currentExperience = null;
            long? nextExperience = null;
            if (data.TryGetProperty("level_info", out var levelInfo)
                && levelInfo.TryGetProperty("current_level", out var levelEl)
                && levelEl.TryGetInt32(out var levelValue))
            {
                level = levelValue;
                currentExperience = levelInfo.TryGetProperty("current_exp", out var currentExpEl)
                    && currentExpEl.TryGetInt64(out var currentExpValue)
                        ? currentExpValue
                        : null;
                nextExperience = levelInfo.TryGetProperty("next_exp", out var nextExpEl)
                    && nextExpEl.TryGetInt64(out var nextExpValue)
                        ? nextExpValue
                        : null;
            }

            var bits = new List<string> { $"已登入 {uname ?? "Bilibili 使用者"}" };
            if (mid is not null)
                bits.Add($"UID {mid}");
            if (level is not null)
                bits.Add($"Lv{level}");
            if (coins is not null)
                bits.Add($"硬幣 {coins}");

            var profileTask = GetProfileAsync(cookies, cancellationToken);
            var dynamicTask = mid is null
                ? Task.FromResult<LatestDynamicInfo?>(null)
                : GetLatestDynamicAsync(cookies, mid.Value, cancellationToken);
            var submissionTask = mid is null
                ? Task.FromResult<LatestSubmissionInfo?>(null)
                : GetLatestSubmissionAsync(cookies, mid.Value, cancellationToken);
            var favoriteTask = mid is null
                ? Task.FromResult<FavoriteInfo?>(null)
                : GetFavoriteInfoAsync(cookies, mid.Value, cancellationToken);
            var bangumiTask = mid is null
                ? Task.FromResult<BangumiFollowInfo?>(null)
                : GetBangumiFollowAsync(cookies, mid.Value, cancellationToken);
            var homepageTask = GetHomepageRecommendationAsync(cookies, cancellationToken);
            var dynamicFeedTask = GetDynamicFeedAsync(cookies, cancellationToken);
            var bangumiRecommendationTask = GetBangumiRecommendationAsync(cookies, cancellationToken);
            var liveRecommendationTask = GetLiveRecommendationAsync(cookies, cancellationToken);
            var popularVideoTask = GetPopularVideoAsync(cookies, cancellationToken);
            var rankingVideoTask = GetRankingVideoAsync(cookies, cancellationToken);
            var hotSearchTask = GetHotSearchAsync(cookies, cancellationToken);
            await Task.WhenAll(
                    profileTask,
                    dynamicTask,
                    submissionTask,
                    favoriteTask,
                    bangumiTask,
                    homepageTask,
                    dynamicFeedTask,
                    bangumiRecommendationTask,
                    liveRecommendationTask,
                    popularVideoTask,
                    rankingVideoTask,
                    hotSearchTask)
                .ConfigureAwait(false);
            var profile = await profileTask.ConfigureAwait(false);
            var latestDynamic = await dynamicTask.ConfigureAwait(false);
            var latestSubmission = await submissionTask.ConfigureAwait(false);
            var favorite = await favoriteTask.ConfigureAwait(false);
            var bangumiFollow = await bangumiTask.ConfigureAwait(false);
            var homepageRecommendation = await homepageTask.ConfigureAwait(false);
            var dynamicFeed = await dynamicFeedTask.ConfigureAwait(false);
            var bangumiRecommendation = await bangumiRecommendationTask.ConfigureAwait(false);
            var liveRecommendation = await liveRecommendationTask.ConfigureAwait(false);
            var popularVideo = await popularVideoTask.ConfigureAwait(false);
            var rankingVideo = await rankingVideoTask.ConfigureAwait(false);
            var hotSearch = await hotSearchTask.ConfigureAwait(false);

            return new NavCheckResult(
                true,
                string.Join(" · ", bits),
                uname,
                mid,
                coins,
                level,
                currentExperience,
                nextExperience,
                profile.Signature,
                profile.Image,
                latestDynamic,
                latestSubmission,
                favorite,
                bangumiFollow,
                homepageRecommendation,
                dynamicFeed,
                bangumiRecommendation,
                liveRecommendation,
                popularVideo,
                rankingVideo,
                hotSearch);
        }
        catch (JsonException)
        {
            return new NavCheckResult(false, $"驗證回應不是 JSON：{Trim(body)}");
        }
    }

    private static async Task<ProfileDetails> GetProfileAsync(
        BilibiliCookieSet cookies,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MyInfoUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://space.bilibili.com/");
        request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new ProfileDetails(null, null);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl)
                || codeEl.GetInt32() != 0
                || !root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("profile", out var profile)
                || !profile.TryGetProperty("sign", out var signEl))
            {
                return new ProfileDetails(null, null);
            }

            var signature = signEl.GetString()?.Trim();
            signature = string.IsNullOrWhiteSpace(signature) ? null : signature;
            var image = profile.TryGetProperty("face", out var faceEl)
                ? await DownloadTrustedImageAsync(faceEl.GetString(), cancellationToken).ConfigureAwait(false)
                : null;
            return new ProfileDetails(signature, image);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new ProfileDetails(null, null);
        }
    }

    private static async Task<LatestDynamicInfo?> GetLatestDynamicAsync(
        BilibiliCookieSet cookies,
        long mid,
        CancellationToken cancellationToken)
    {
        var url = $"{DynamicSpaceUrl}?host_mid={mid}&timezone_offset=-480&platform=web&features=itemOpusStyle";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", $"https://space.bilibili.com/{mid}/dynamic");
        request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl)
                || codeEl.GetInt32() != 0
                || !root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array
                || items.GetArrayLength() == 0)
            {
                return null;
            }

            var item = items[0];
            var id = GetString(item, "id_str");
            if (string.IsNullOrWhiteSpace(id))
                return null;

            var type = FormatDynamicType(GetString(item, "type"));
            var modules = item.TryGetProperty("modules", out var modulesEl) ? modulesEl : default;
            var author = modules.ValueKind == JsonValueKind.Object
                && modules.TryGetProperty("module_author", out var authorEl)
                    ? authorEl
                    : default;
            DateTimeOffset? publishedAt = author.ValueKind == JsonValueKind.Object
                && author.TryGetProperty("pub_ts", out var timestampEl)
                && timestampEl.TryGetInt64(out var timestamp)
                    ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
                    : null;
            var dynamic = modules.ValueKind == JsonValueKind.Object
                && modules.TryGetProperty("module_dynamic", out var dynamicEl)
                    ? dynamicEl
                    : default;
            var text = GetDynamicText(dynamic);
            var coverUrl = GetDynamicCoverUrl(dynamic);
            var cover = await DownloadTrustedImageAsync(coverUrl, cancellationToken).ConfigureAwait(false);

            return new LatestDynamicInfo(
                type,
                string.IsNullOrWhiteSpace(text) ? "（沒有文字內容）" : text.Trim(),
                publishedAt,
                $"https://t.bilibili.com/{id}",
                cover);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static async Task<LatestSubmissionInfo?> GetLatestSubmissionAsync(
        BilibiliCookieSet cookies,
        long mid,
        CancellationToken cancellationToken)
    {
        var countTask = GetSubmissionCountAsync(cookies, mid, cancellationToken);
        var videoTask = GetLatestVideoDynamicAsync(cookies, mid, cancellationToken);
        await Task.WhenAll(countTask, videoTask).ConfigureAwait(false);
        var count = await countTask.ConfigureAwait(false);
        var video = await videoTask.ConfigureAwait(false);
        if (count is null && video is null)
            return null;

        return new LatestSubmissionInfo(
            count,
            video?.Title,
            video?.PublishedAt,
            video?.Url,
            video?.PlayCount,
            video?.CoverImage);
    }

    private static async Task<FavoriteInfo?> GetFavoriteInfoAsync(
        BilibiliCookieSet cookies,
        long mid,
        CancellationToken cancellationToken)
    {
        var referer = $"https://space.bilibili.com/{mid}/favlist";
        using var folderRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{FavoriteFoldersUrl}?up_mid={mid}");
        folderRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        folderRequest.Headers.TryAddWithoutValidation("Referer", referer);
        folderRequest.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());

        try
        {
            using var folderResponse = await Http.SendAsync(folderRequest, cancellationToken)
                .ConfigureAwait(false);
            if (!folderResponse.IsSuccessStatusCode)
                return null;

            var folderBody = await folderResponse.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            using var folderDoc = JsonDocument.Parse(folderBody);
            var root = folderDoc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl)
                || codeEl.GetInt32() != 0
                || !root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("list", out var folders)
                || folders.ValueKind != JsonValueKind.Array
                || folders.GetArrayLength() == 0)
            {
                return null;
            }

            var folder = folders[0];
            if (!folder.TryGetProperty("id", out var idEl)
                || !idEl.TryGetInt64(out var folderId))
            {
                return null;
            }

            var folderCount = data.TryGetProperty("count", out var countEl)
                && countEl.TryGetInt32(out var count)
                    ? count
                    : folders.GetArrayLength();
            var folderTitle = GetString(folder, "title");
            var itemCount = folder.TryGetProperty("media_count", out var mediaCountEl)
                && mediaCountEl.TryGetInt32(out var mediaCount)
                    ? mediaCount
                    : 0;

            var resourceUrl =
                $"{FavoriteResourcesUrl}?media_id={folderId}&pn=1&ps=1&order=mtime&type=0&tid=0&platform=web";
            using var resourceRequest = new HttpRequestMessage(HttpMethod.Get, resourceUrl);
            resourceRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            resourceRequest.Headers.TryAddWithoutValidation("Referer", referer);
            resourceRequest.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());
            using var resourceResponse = await Http.SendAsync(resourceRequest, cancellationToken)
                .ConfigureAwait(false);
            if (!resourceResponse.IsSuccessStatusCode)
            {
                return new FavoriteInfo(
                    folderCount,
                    string.IsNullOrWhiteSpace(folderTitle) ? "預設收藏夾" : folderTitle,
                    itemCount,
                    null,
                    null,
                    null,
                    null);
            }

            var resourceBody = await resourceResponse.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            using var resourceDoc = JsonDocument.Parse(resourceBody);
            var resourceRoot = resourceDoc.RootElement;
            if (!resourceRoot.TryGetProperty("code", out var resourceCodeEl)
                || resourceCodeEl.GetInt32() != 0
                || !resourceRoot.TryGetProperty("data", out var resourceData)
                || !resourceData.TryGetProperty("medias", out var medias)
                || medias.ValueKind != JsonValueKind.Array
                || medias.GetArrayLength() == 0)
            {
                return new FavoriteInfo(
                    folderCount,
                    string.IsNullOrWhiteSpace(folderTitle) ? "預設收藏夾" : folderTitle,
                    itemCount,
                    null,
                    null,
                    null,
                    null);
            }

            var media = medias[0];
            var bvid = GetString(media, "bvid");
            DateTimeOffset? favoritedAt = media.TryGetProperty("fav_time", out var favoriteTimeEl)
                && favoriteTimeEl.TryGetInt64(out var favoriteTime)
                    ? DateTimeOffset.FromUnixTimeSeconds(favoriteTime)
                    : media.TryGetProperty("ctime", out var createdTimeEl)
                      && createdTimeEl.TryGetInt64(out var createdTime)
                        ? DateTimeOffset.FromUnixTimeSeconds(createdTime)
                        : null;
            var cover = await DownloadTrustedImageAsync(GetString(media, "cover"), cancellationToken)
                .ConfigureAwait(false);
            return new FavoriteInfo(
                folderCount,
                string.IsNullOrWhiteSpace(folderTitle) ? "預設收藏夾" : folderTitle,
                itemCount,
                GetString(media, "title"),
                favoritedAt,
                string.IsNullOrWhiteSpace(bvid) ? null : $"https://www.bilibili.com/video/{bvid}",
                cover);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static async Task<BangumiFollowInfo?> GetBangumiFollowAsync(
        BilibiliCookieSet cookies,
        long mid,
        CancellationToken cancellationToken)
    {
        var animeTask = GetBangumiListAsync(cookies, mid, type: 1, cancellationToken);
        var dramaTask = GetBangumiListAsync(cookies, mid, type: 2, cancellationToken);
        await Task.WhenAll(animeTask, dramaTask).ConfigureAwait(false);
        var anime = await animeTask.ConfigureAwait(false);
        var drama = await dramaTask.ConfigureAwait(false);
        if (anime is null && drama is null)
            return null;

        return new BangumiFollowInfo(
            anime?.Count,
            anime?.Latest,
            drama?.Count,
            drama?.Latest);
    }

    private static async Task<(int Count, FollowedSeasonInfo? Latest)?> GetBangumiListAsync(
        BilibiliCookieSet cookies,
        long mid,
        int type,
        CancellationToken cancellationToken)
    {
        var url = $"{BangumiFollowUrl}?vmid={mid}&type={type}&pn=1&ps=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", $"https://space.bilibili.com/{mid}/bangumi");
        request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl)
                || codeEl.GetInt32() != 0
                || !root.TryGetProperty("data", out var data))
            {
                return null;
            }

            var count = data.TryGetProperty("total", out var totalEl)
                && totalEl.TryGetInt32(out var total)
                    ? total
                    : 0;
            if (!data.TryGetProperty("list", out var list)
                || list.ValueKind != JsonValueKind.Array
                || list.GetArrayLength() == 0)
            {
                return (count, null);
            }

            var season = list[0];
            var title = GetString(season, "title");
            var urlValue = GetString(season, "url");
            if (string.IsNullOrWhiteSpace(urlValue)
                && season.TryGetProperty("season_id", out var seasonIdEl)
                && seasonIdEl.TryGetInt64(out var seasonId))
            {
                urlValue = $"https://www.bilibili.com/bangumi/play/ss{seasonId}";
            }

            var progress = GetString(season, "progress");
            if (string.IsNullOrWhiteSpace(progress)
                && season.TryGetProperty("new_ep", out var newEpisode))
            {
                progress = GetString(newEpisode, "index_show");
            }
            var followStatus = season.TryGetProperty("follow_status", out var followStatusEl)
                && followStatusEl.TryGetInt32(out var status)
                    ? FormatFollowStatus(status)
                    : string.Empty;
            var progressParts = new[] { followStatus, progress }
                .Where(part => !string.IsNullOrWhiteSpace(part));
            var cover = await DownloadTrustedImageAsync(GetString(season, "cover"), cancellationToken)
                .ConfigureAwait(false);
            var latest = string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(urlValue)
                ? null
                : new FollowedSeasonInfo(
                    title,
                    string.Join(" · ", progressParts),
                    urlValue,
                    cover);
            return (count, latest);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static string FormatFollowStatus(int status) => status switch
    {
        1 => "想看",
        2 => "在看",
        3 => "看過",
        _ => string.Empty,
    };

    private static async Task<HomepageRecommendationInfo?> GetHomepageRecommendationAsync(
        BilibiliCookieSet cookies,
        CancellationToken cancellationToken)
    {
        var url = $"{HomepageRecommendationUrl}?fresh_type=4&ps=12&fresh_idx=1&web_location=1430650";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
        request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl)
                || codeEl.GetInt32() != 0
                || !root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("item", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in items.EnumerateArray())
            {
                var bvid = GetString(item, "bvid");
                var title = GetString(item, "title");
                if (GetString(item, "goto") != "av"
                    || string.IsNullOrWhiteSpace(bvid)
                    || string.IsNullOrWhiteSpace(title)
                    || item.TryGetProperty("business_info", out var businessInfo)
                       && businessInfo.ValueKind != JsonValueKind.Null)
                {
                    continue;
                }

                var uploader = item.TryGetProperty("owner", out var owner)
                    ? GetString(owner, "name")
                    : string.Empty;
                var statistics = new List<string>();
                if (item.TryGetProperty("stat", out var stat))
                {
                    if (stat.TryGetProperty("view", out var viewEl)
                        && viewEl.TryGetInt64(out var view))
                    {
                        statistics.Add($"播放 {view:N0}");
                    }
                    if (stat.TryGetProperty("danmaku", out var danmakuEl)
                        && danmakuEl.TryGetInt64(out var danmaku))
                    {
                        statistics.Add($"彈幕 {danmaku:N0}");
                    }
                }
                var cover = await DownloadTrustedImageAsync(GetString(item, "pic"), cancellationToken)
                    .ConfigureAwait(false);
                return new HomepageRecommendationInfo(
                    title,
                    uploader,
                    string.Join(" · ", statistics),
                    $"https://www.bilibili.com/video/{bvid}",
                    cover);
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static async Task<DynamicFeedInfo?> GetDynamicFeedAsync(
        BilibiliCookieSet cookies,
        CancellationToken cancellationToken)
    {
        var url =
            $"{DynamicFeedUrl}?type=all&timezone_offset=-480&platform=web&features=itemOpusStyle,listOnlyfans";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://t.bilibili.com/");
        request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl)
                || codeEl.GetInt32() != 0
                || !root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array
                || items.GetArrayLength() == 0)
            {
                return null;
            }

            var item = items[0];
            var id = GetString(item, "id_str");
            if (string.IsNullOrWhiteSpace(id)
                || !item.TryGetProperty("modules", out var modules))
            {
                return null;
            }

            var author = modules.TryGetProperty("module_author", out var authorModule)
                ? authorModule
                : default;
            var dynamic = modules.TryGetProperty("module_dynamic", out var dynamicModule)
                ? dynamicModule
                : default;
            DateTimeOffset? publishedAt = author.ValueKind == JsonValueKind.Object
                && author.TryGetProperty("pub_ts", out var timestampEl)
                && timestampEl.TryGetInt64(out var timestamp)
                    ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
                    : null;
            var text = GetDynamicText(dynamic).Trim();
            var cover = await DownloadTrustedImageAsync(
                    GetDynamicCoverUrl(dynamic),
                    cancellationToken)
                .ConfigureAwait(false);
            return new DynamicFeedInfo(
                GetString(author, "name"),
                FormatDynamicType(GetString(item, "type")),
                string.IsNullOrWhiteSpace(text) ? "此動態沒有文字內容" : text,
                publishedAt,
                $"https://t.bilibili.com/{id}",
                cover);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static async Task<BangumiRecommendationInfo?> GetBangumiRecommendationAsync(
        BilibiliCookieSet cookies,
        CancellationToken cancellationToken)
    {
        var url =
            $"{BangumiIndexUrl}?type=1&season_type=1&order=4&sort=0&page=1&pagesize=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/anime/");
        request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl)
                || codeEl.GetInt32() != 0
                || !root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("list", out var list)
                || list.ValueKind != JsonValueKind.Array
                || list.GetArrayLength() == 0)
            {
                return null;
            }

            var item = list[0];
            var title = GetString(item, "title");
            var link = GetString(item, "link");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                return null;

            var cover = await DownloadTrustedImageAsync(GetString(item, "cover"), cancellationToken)
                .ConfigureAwait(false);
            return new BangumiRecommendationInfo(
                title,
                GetString(item, "subTitle"),
                GetString(item, "index_show"),
                GetString(item, "order"),
                GetString(item, "badge"),
                link,
                cover);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static async Task<LiveRecommendationInfo?> GetLiveRecommendationAsync(
        BilibiliCookieSet cookies,
        CancellationToken cancellationToken)
    {
        var url = $"{LiveRecommendationUrl}?platform=web&web_location=333.1007";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://live.bilibili.com/");
        request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl)
                || codeEl.GetInt32() != 0
                || !root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("recommend_room_list", out var rooms)
                || rooms.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var room in rooms.EnumerateArray())
            {
                if (room.TryGetProperty("is_ad", out var isAdEl)
                    && isAdEl.ValueKind == JsonValueKind.True)
                {
                    continue;
                }
                if (!room.TryGetProperty("roomid", out var roomIdEl)
                    || !roomIdEl.TryGetInt64(out var roomId))
                {
                    continue;
                }

                var title = GetString(room, "title");
                if (string.IsNullOrWhiteSpace(title))
                    continue;
                long? online = room.TryGetProperty("online", out var onlineEl)
                    && onlineEl.TryGetInt64(out var onlineValue)
                        ? onlineValue
                        : null;
                var areaParts = new[]
                {
                    GetString(room, "area_v2_parent_name"),
                    GetString(room, "area_v2_name"),
                }.Where(value => !string.IsNullOrWhiteSpace(value));
                var cover = await DownloadTrustedImageAsync(GetString(room, "cover"), cancellationToken)
                    .ConfigureAwait(false);
                return new LiveRecommendationInfo(
                    title,
                    GetString(room, "uname"),
                    string.Join(" · ", areaParts),
                    online,
                    $"https://live.bilibili.com/{roomId}",
                    cover);
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static async Task<PopularVideoInfo?> GetPopularVideoAsync(
        BilibiliCookieSet cookies,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{PopularVideoUrl}?pn=1&ps=1");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/v/popular/all/");
        request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl)
                || codeEl.GetInt32() != 0
                || !root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("list", out var list)
                || list.ValueKind != JsonValueKind.Array
                || list.GetArrayLength() == 0)
            {
                return null;
            }

            var item = list[0];
            var title = GetString(item, "title");
            var bvid = GetString(item, "bvid");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(bvid))
                return null;

            var uploader = item.TryGetProperty("owner", out var owner)
                ? GetString(owner, "name")
                : string.Empty;
            var reason = item.TryGetProperty("rcmd_reason", out var reasonEl)
                ? GetString(reasonEl, "content")
                : string.Empty;
            long? views = null;
            long? danmaku = null;
            if (item.TryGetProperty("stat", out var stat))
            {
                views = stat.TryGetProperty("view", out var viewEl)
                    && viewEl.TryGetInt64(out var view)
                        ? view
                        : null;
                danmaku = stat.TryGetProperty("danmaku", out var danmakuEl)
                    && danmakuEl.TryGetInt64(out var danmakuValue)
                        ? danmakuValue
                        : null;
            }
            var cover = await DownloadTrustedImageAsync(GetString(item, "pic"), cancellationToken)
                .ConfigureAwait(false);
            return new PopularVideoInfo(
                title,
                uploader,
                reason,
                views,
                danmaku,
                $"https://www.bilibili.com/video/{bvid}",
                cover);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static async Task<RankingVideoInfo?> GetRankingVideoAsync(
        BilibiliCookieSet cookies,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{RankingVideoUrl}?rid=0&type=all");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/v/popular/rank/all/");
        request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl)
                || codeEl.GetInt32() != 0
                || !root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("list", out var list)
                || list.ValueKind != JsonValueKind.Array
                || list.GetArrayLength() == 0)
            {
                return null;
            }

            var item = list[0];
            var title = GetString(item, "title");
            var bvid = GetString(item, "bvid");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(bvid))
                return null;

            var uploader = item.TryGetProperty("owner", out var owner)
                ? GetString(owner, "name")
                : string.Empty;
            long? views = null;
            long? danmaku = null;
            if (item.TryGetProperty("stat", out var stat))
            {
                views = stat.TryGetProperty("view", out var viewEl)
                    && viewEl.TryGetInt64(out var view)
                        ? view
                        : null;
                danmaku = stat.TryGetProperty("danmaku", out var danmakuEl)
                    && danmakuEl.TryGetInt64(out var danmakuValue)
                        ? danmakuValue
                        : null;
            }
            var cover = await DownloadTrustedImageAsync(GetString(item, "pic"), cancellationToken)
                .ConfigureAwait(false);
            return new RankingVideoInfo(
                1,
                title,
                uploader,
                views,
                danmaku,
                $"https://www.bilibili.com/video/{bvid}",
                cover);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static async Task<HotSearchInfo?> GetHotSearchAsync(
        BilibiliCookieSet cookies,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, HotSearchUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
        request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl)
                || codeEl.GetInt32() != 0
                || !root.TryGetProperty("list", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var entries = new List<(int Position, string Keyword, string Label)>();
            foreach (var item in list.EnumerateArray())
            {
                var keyword = GetString(item, "show_name");
                if (string.IsNullOrWhiteSpace(keyword))
                    keyword = GetString(item, "keyword");
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                var position = item.TryGetProperty("pos", out var positionEl)
                    && positionEl.TryGetInt32(out var positionValue)
                        ? positionValue
                        : entries.Count + 1;
                var wordType = item.TryGetProperty("word_type", out var wordTypeEl)
                    && wordTypeEl.TryGetInt32(out var wordTypeValue)
                        ? wordTypeValue
                        : 0;
                entries.Add((position, keyword.Trim(), FormatHotSearchLabel(wordType)));
                if (entries.Count == 5)
                    break;
            }
            if (entries.Count == 0)
                return null;

            var lines = entries.Select(entry =>
                string.IsNullOrWhiteSpace(entry.Label)
                    ? $"{entry.Position}. {entry.Keyword}"
                    : $"{entry.Position}. {entry.Keyword}  [{entry.Label}]");
            var topKeyword = entries[0].Keyword;
            return new HotSearchInfo(
                string.Join(Environment.NewLine, lines),
                topKeyword,
                $"https://search.bilibili.com/all?keyword={Uri.EscapeDataString(topKeyword)}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static string FormatHotSearchLabel(int wordType) => wordType switch
    {
        4 => "新",
        5 => "熱",
        7 => "直播中",
        9 => "梗",
        11 => "話題",
        12 => "獨家",
        _ => string.Empty,
    };

    private static async Task<int?> GetSubmissionCountAsync(
        BilibiliCookieSet cookies,
        long mid,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{SpaceNavCountUrl}?mid={mid}");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", $"https://space.bilibili.com/{mid}/upload/video");
        request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());
        try
        {
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return root.TryGetProperty("code", out var codeEl)
                && codeEl.GetInt32() == 0
                && root.TryGetProperty("data", out var data)
                && data.TryGetProperty("video", out var videoEl)
                && videoEl.TryGetInt32(out var count)
                    ? count
                    : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static async Task<LatestSubmissionInfo?> GetLatestVideoDynamicAsync(
        BilibiliCookieSet cookies,
        long mid,
        CancellationToken cancellationToken)
    {
        var url = $"{DynamicSpaceUrl}?host_mid={mid}&timezone_offset=-480&platform=web&type=video&features=itemOpusStyle";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", $"https://space.bilibili.com/{mid}/upload/video");
        request.Headers.TryAddWithoutValidation("Cookie", cookies.ToCookieHeader());
        try
        {
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var codeEl)
                || codeEl.GetInt32() != 0
                || !root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (GetString(item, "type") != "DYNAMIC_TYPE_AV"
                    || !item.TryGetProperty("modules", out var modules)
                    || !modules.TryGetProperty("module_dynamic", out var dynamic)
                    || !dynamic.TryGetProperty("major", out var major)
                    || !major.TryGetProperty("archive", out var archive))
                {
                    continue;
                }

                var title = GetString(archive, "title");
                var bvid = GetString(archive, "bvid");
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(bvid))
                    continue;
                var author = modules.TryGetProperty("module_author", out var authorEl)
                    ? authorEl
                    : default;
                DateTimeOffset? publishedAt = author.ValueKind == JsonValueKind.Object
                    && author.TryGetProperty("pub_ts", out var timestampEl)
                    && timestampEl.TryGetInt64(out var timestamp)
                        ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
                        : null;
                var playCount = archive.TryGetProperty("stat", out var stat)
                    ? GetString(stat, "play")
                    : string.Empty;
                var cover = await DownloadTrustedImageAsync(
                        GetString(archive, "cover"),
                        cancellationToken)
                    .ConfigureAwait(false);
                return new LatestSubmissionInfo(
                    null,
                    title,
                    publishedAt,
                    $"https://www.bilibili.com/video/{bvid}",
                    string.IsNullOrWhiteSpace(playCount) ? null : playCount,
                    cover);
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static string GetDynamicText(JsonElement dynamic)
    {
        if (dynamic.ValueKind != JsonValueKind.Object)
            return string.Empty;
        if (dynamic.TryGetProperty("desc", out var desc) && desc.ValueKind == JsonValueKind.Object)
        {
            var text = GetString(desc, "text");
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }
        if (!dynamic.TryGetProperty("major", out var major) || major.ValueKind != JsonValueKind.Object)
            return string.Empty;

        foreach (var propertyName in new[] { "archive", "article", "common" })
        {
            if (major.TryGetProperty(propertyName, out var content)
                && content.ValueKind == JsonValueKind.Object)
            {
                var title = GetString(content, "title");
                if (!string.IsNullOrWhiteSpace(title))
                    return title;
            }
        }

        if (major.TryGetProperty("opus", out var opus)
            && opus.ValueKind == JsonValueKind.Object
            && opus.TryGetProperty("summary", out var summary)
            && summary.ValueKind == JsonValueKind.Object)
        {
            return GetString(summary, "text");
        }

        return string.Empty;
    }

    private static string? GetDynamicCoverUrl(JsonElement dynamic)
    {
        if (dynamic.ValueKind != JsonValueKind.Object
            || !dynamic.TryGetProperty("major", out var major)
            || major.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (major.TryGetProperty("archive", out var archive) && archive.ValueKind == JsonValueKind.Object)
            return GetString(archive, "cover");
        if (major.TryGetProperty("opus", out var opus)
            && opus.ValueKind == JsonValueKind.Object
            && opus.TryGetProperty("pics", out var pictures)
            && pictures.ValueKind == JsonValueKind.Array
            && pictures.GetArrayLength() > 0)
        {
            return GetString(pictures[0], "url");
        }
        if (major.TryGetProperty("draw", out var draw)
            && draw.ValueKind == JsonValueKind.Object
            && draw.TryGetProperty("items", out var items)
            && items.ValueKind == JsonValueKind.Array
            && items.GetArrayLength() > 0)
        {
            return GetString(items[0], "src");
        }
        return null;
    }

    private static string FormatDynamicType(string? type) => type switch
    {
        "DYNAMIC_TYPE_AV" => "影片",
        "DYNAMIC_TYPE_DRAW" => "圖文",
        "DYNAMIC_TYPE_WORD" => "文字",
        "DYNAMIC_TYPE_FORWARD" => "轉發",
        "DYNAMIC_TYPE_ARTICLE" => "專欄",
        _ => "動態",
    };

    private static string GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static async Task<byte[]?> DownloadTrustedImageAsync(
        string? imageUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme == Uri.UriSchemeHttp)
            uri = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
        if (uri.Scheme != Uri.UriSchemeHttps
            || (!uri.Host.Equals("hdslb.com", StringComparison.OrdinalIgnoreCase)
                && !uri.Host.EndsWith(".hdslb.com", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://space.bilibili.com/");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 5_000_000)
            return null;

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return bytes.Length is > 0 and <= 5_000_000 ? bytes : null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string Trim(string text)
    {
        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 180 ? oneLine : oneLine[..180] + "…";
    }
}
