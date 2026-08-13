namespace BilibiliCookieReader.Services;

public enum ExpiryUrgency
{
    Unknown,
    Ok,
    Soon,
    Urgent,
    Expired,
}

public sealed record CookieExpiryReminder
{
    public DateTimeOffset? CookieExpiresAt { get; init; }
    public DateTimeOffset? SessionExpiresAt { get; init; }
    public DateTimeOffset? EffectiveExpiresAt { get; init; }
    public ExpiryUrgency Urgency { get; init; } = ExpiryUrgency.Unknown;
    public int? DaysRemaining { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public bool HasDate => EffectiveExpiresAt is not null;
}

public static class CookieExpiry
{
    public const int SoonDays = 14;
    public const int UrgentDays = 3;

    public static CookieExpiryReminder From(BilibiliCookieSet cookies, DateTimeOffset? now = null)
    {
        var cookieExpires = Min(
            cookies.SessData.ExpiresAt,
            cookies.BiliJct.ExpiresAt);
        var sessionExpires = cookies.SessData.SessionExpiresAt
                             ?? TryParseSessDataSessionExpiry(cookies.SessData.Value);
        return From(cookieExpires, sessionExpires, now);
    }

    public static CookieExpiryReminder From(
        DateTimeOffset? cookieExpiresAt,
        DateTimeOffset? sessionExpiresAt,
        DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        var effective = Min(cookieExpiresAt, sessionExpiresAt);
        if (effective is null)
        {
            return new CookieExpiryReminder
            {
                CookieExpiresAt = cookieExpiresAt,
                SessionExpiresAt = sessionExpiresAt,
                Title = "尚無 Cookie / Session 到期日",
                Detail = "讀取 Netscape cookies.txt 後，會顯示 SESSDATA 工作階段與 Cookie 檔的預定過期日。",
            };
        }

        var days = DaysRemaining(effective.Value, clock);
        var urgency = Classify(days);
        var when = FormatTaipei(effective.Value);
        var remaining = FormatRemaining(days);
        var title = urgency switch
        {
            ExpiryUrgency.Expired => "Cookie / Session 已過期",
            ExpiryUrgency.Urgent => "Cookie / Session 即將過期",
            ExpiryUrgency.Soon => "請預定更新 Cookie",
            _ => "Cookie / Session 預定過期日",
        };

        var source = DescribeSources(cookieExpiresAt, sessionExpiresAt);
        var action = urgency is ExpiryUrgency.Expired
            ? "請重新登入 Bilibili、匯出 cookies.txt，再更新 GitHub Secrets，否則每日任務會登入失敗。"
            : "到期前請重新登入 Bilibili、匯出 cookies.txt，並更新 GitHub Secrets。";

        return new CookieExpiryReminder
        {
            CookieExpiresAt = cookieExpiresAt,
            SessionExpiresAt = sessionExpiresAt,
            EffectiveExpiresAt = effective,
            Urgency = urgency,
            DaysRemaining = days,
            Title = $"{title}：{when}（{remaining}）",
            Detail = string.Join(" ", new[] { source, action }.Where(static text => text.Length > 0)),
        };
    }

    public static DateTimeOffset? TryParseSessDataSessionExpiry(string? sessData)
    {
        if (string.IsNullOrWhiteSpace(sessData))
            return null;

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(sessData);
        }
        catch (UriFormatException)
        {
            decoded = sessData;
        }

        var parts = decoded.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return null;
        if (!long.TryParse(parts[1], out var unix))
            return null;
        // 2015-01-01 .. 2040-01-01, to ignore junk numbers.
        if (unix is < 1_420_070_400L or > 2_208_988_800L)
            return null;

        return DateTimeOffset.FromUnixTimeSeconds(unix);
    }

    public static int DaysRemaining(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        var zone = TaipeiZone();
        var expireDate = TimeZoneInfo.ConvertTime(expiresAt, zone).Date;
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        return (expireDate - today).Days;
    }

    public static ExpiryUrgency Classify(int daysRemaining)
    {
        if (daysRemaining < 0)
            return ExpiryUrgency.Expired;
        if (daysRemaining <= UrgentDays)
            return ExpiryUrgency.Urgent;
        if (daysRemaining <= SoonDays)
            return ExpiryUrgency.Soon;
        return ExpiryUrgency.Ok;
    }

    public static string FormatTaipei(DateTimeOffset value)
    {
        var local = TimeZoneInfo.ConvertTime(value, TaipeiZone());
        return local.ToString("yyyy-MM-dd HH:mm") + " 台北";
    }

    public static string FormatRemaining(int days) => days switch
    {
        0 => "今天到期",
        1 => "還有 1 天",
        > 1 => $"還有 {days} 天",
        -1 => "已過期 1 天",
        _ => $"已過期 {Math.Abs(days)} 天",
    };

    public static DateTimeOffset? Min(DateTimeOffset? left, DateTimeOffset? right) => (left, right) switch
    {
        (null, null) => null,
        (null, { } onlyRight) => onlyRight,
        ({ } onlyLeft, null) => onlyLeft,
        ({ } a, { } b) => a <= b ? a : b,
    };

    public static bool SameMinute(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null || right is null)
            return false;
        return left.Value.ToUnixTimeSeconds() / 60 == right.Value.ToUnixTimeSeconds() / 60;
    }

    public static TimeZoneInfo TaipeiZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
        }
    }

    private static string DescribeSources(DateTimeOffset? cookieExpiresAt, DateTimeOffset? sessionExpiresAt)
    {
        if (cookieExpiresAt is not null && sessionExpiresAt is not null)
        {
            if (SameMinute(cookieExpiresAt, sessionExpiresAt))
                return $"Cookie 檔與 SESSDATA 工作階段都預定 {FormatTaipei(cookieExpiresAt.Value)} 過期。";
            return $"Cookie 檔預定 {FormatTaipei(cookieExpiresAt.Value)} 過期；SESSDATA 工作階段預定 {FormatTaipei(sessionExpiresAt.Value)} 過期。以較早的日期為準。";
        }

        if (sessionExpiresAt is not null)
            return $"SESSDATA 工作階段預定 {FormatTaipei(sessionExpiresAt.Value)} 過期。";
        if (cookieExpiresAt is not null)
            return $"Cookie 檔預定 {FormatTaipei(cookieExpiresAt.Value)} 過期。";
        return string.Empty;
    }
}
