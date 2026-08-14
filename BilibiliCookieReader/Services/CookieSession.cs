namespace BilibiliCookieReader.Services;

/// <summary>
/// Deep intake module: open a Cookie Export and get Login Cookies,
/// Effective Expiry, and a status line. Callers do not parse or rank.
/// </summary>
public sealed class CookieSession
{
    private CookieSession(BilibiliCookieSet cookies, CookieExpiryReminder reminder, string status, bool isAlarming)
    {
        Cookies = cookies;
        Reminder = reminder;
        Status = status;
        IsAlarming = isAlarming;
    }

    public BilibiliCookieSet Cookies { get; }
    public CookieExpiryReminder Reminder { get; }
    public string Status { get; }
    public bool IsAlarming { get; }
    public bool HasAny => Cookies.HasAny;
    public bool HasAll => Cookies.HasAll;

    public static CookieSession OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("請先選擇 cookies.txt。", nameof(path));

        var cookies = BilibiliCookieParser.ParseFile(path.Trim().Trim('"'));
        return FromCookies(cookies);
    }

    public static CookieSession FromText(string text, string? sourcePath = null)
    {
        var cookies = BilibiliCookieParser.ParseText(text, sourcePath);
        return FromCookies(cookies);
    }

    public static CookieSession? FromSavedExpiry(DateTimeOffset? cookieExpiresAt, DateTimeOffset? sessionExpiresAt)
    {
        var reminder = CookieExpiry.From(cookieExpiresAt, sessionExpiresAt);
        if (!reminder.HasDate)
            return null;

        reminder = reminder with
        {
            Detail = "這是上次讀取時記下的預定過期日。請再讀一次 cookies.txt 確認是否仍有效。"
                     + " " + reminder.Detail,
        };

        return new CookieSession(
            new BilibiliCookieSet(),
            reminder,
            reminder.Title,
            reminder.Urgency is ExpiryUrgency.Expired or ExpiryUrgency.Urgent);
    }

    private static CookieSession FromCookies(BilibiliCookieSet cookies)
    {
        var reminder = CookieExpiry.From(cookies);
        var found = cookies.Fields.Count(item => item.HasValue);
        var source = string.IsNullOrWhiteSpace(cookies.SourcePath)
            ? "檔案"
            : Path.GetFileName(cookies.SourcePath);
        var status = $"已從 {source} 讀到 {found}/{cookies.Fields.Count} 個欄位。";
        if (reminder.HasDate)
            status += " " + reminder.Title;
        if (cookies.Warnings.Count > 0)
            status += " " + string.Join(" ", cookies.Warnings);

        var alarming = !cookies.HasAll
                       || cookies.Fields.Any(item => item.IsExpired)
                       || reminder.Urgency is ExpiryUrgency.Expired or ExpiryUrgency.Urgent;

        return new CookieSession(cookies, reminder, status, alarming);
    }
}
