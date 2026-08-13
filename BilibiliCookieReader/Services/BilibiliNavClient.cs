using System.Net.Http.Headers;
using System.Text.Json;

namespace BilibiliCookieReader.Services;

public sealed record NavCheckResult(
    bool Ok,
    string Message,
    string? UserName = null,
    long? Mid = null,
    double? Coins = null,
    int? Level = null);

public static class BilibiliNavClient
{
    private const string NavUrl = "https://api.bilibili.com/x/web-interface/nav";
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
            return new NavCheckResult(false, "三個欄位不齊，無法驗證登入。");
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
            if (data.TryGetProperty("level_info", out var levelInfo)
                && levelInfo.TryGetProperty("current_level", out var levelEl)
                && levelEl.TryGetInt32(out var levelValue))
            {
                level = levelValue;
            }

            var bits = new List<string> { $"已登入 {uname ?? "Bilibili 使用者"}" };
            if (mid is not null)
                bits.Add($"UID {mid}");
            if (level is not null)
                bits.Add($"Lv{level}");
            if (coins is not null)
                bits.Add($"硬幣 {coins}");

            return new NavCheckResult(true, string.Join(" · ", bits), uname, mid, coins, level);
        }
        catch (JsonException)
        {
            return new NavCheckResult(false, $"驗證回應不是 JSON：{Trim(body)}");
        }
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
