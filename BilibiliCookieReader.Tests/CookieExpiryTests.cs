using BilibiliCookieReader.Services;
using Xunit;

namespace BilibiliCookieReader.Tests;

public class CookieExpiryTests
{
    [Fact]
    public void Parses_session_timestamp_inside_SESSDATA()
    {
        var expiry = CookieExpiry.TryParseSessDataSessionExpiry(
            "1fe57d6c%2C1802178920%2C4fb69%2A82CjCF");

        Assert.NotNull(expiry);
        Assert.Equal(1802178920, expiry.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void Parses_already_decoded_SESSDATA()
    {
        var expiry = CookieExpiry.TryParseSessDataSessionExpiry("abc,1700000000,xyz");
        Assert.Equal(1_700_000_000, expiry!.Value.ToUnixTimeSeconds());
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-comma")]
    [InlineData("abc,notanumber,xyz")]
    [InlineData("abc,100,xyz")]
    public void Ignores_invalid_SESSDATA_payload(string value)
    {
        Assert.Null(CookieExpiry.TryParseSessDataSessionExpiry(value));
    }

    [Fact]
    public void Reminder_uses_earlier_of_cookie_and_session()
    {
        var cookie = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var session = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var now = DateTimeOffset.FromUnixTimeSeconds(1_699_000_000);

        var reminder = CookieExpiry.From(cookie, session, now);

        Assert.Equal(session, reminder.EffectiveExpiresAt);
        Assert.Contains("以較早的日期為準", reminder.Detail);
    }

    [Fact]
    public void Classifies_expired_soon_and_ok()
    {
        var expires = DateTimeOffset.Parse("2026-08-20T00:00:00+08:00");
        var now = DateTimeOffset.Parse("2026-08-13T10:00:00+08:00");

        var soon = CookieExpiry.From(expires, expires, now);
        Assert.Equal(ExpiryUrgency.Soon, soon.Urgency);
        Assert.Equal(7, soon.DaysRemaining);
        Assert.Contains("2026-08-20", soon.Title);
        Assert.Contains("還有 7 天", soon.Title);

        var urgent = CookieExpiry.From(expires, expires, DateTimeOffset.Parse("2026-08-19T10:00:00+08:00"));
        Assert.Equal(ExpiryUrgency.Urgent, urgent.Urgency);
        Assert.Contains("即將過期", urgent.Title);

        var expired = CookieExpiry.From(expires, expires, DateTimeOffset.Parse("2026-08-21T10:00:00+08:00"));
        Assert.Equal(ExpiryUrgency.Expired, expired.Urgency);
        Assert.Contains("已過期", expired.Title);

        var ok = CookieExpiry.From(expires, expires, DateTimeOffset.Parse("2026-07-01T10:00:00+08:00"));
        Assert.Equal(ExpiryUrgency.Ok, ok.Urgency);
    }

    [Fact]
    public void Parser_attaches_session_expiry_from_SESSDATA()
    {
        var text = """
            .bilibili.com	TRUE	/	TRUE	1802178920	SESSDATA	1fe57d6c%2C1802178920%2C4fb69%2A82
            .bilibili.com	TRUE	/	TRUE	1802178920	bili_jct	aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            .bilibili.com	TRUE	/	TRUE	1802178920	DedeUserID	1
            """;

        var set = BilibiliCookieParser.ParseText(text);
        Assert.Equal(1802178920, set.SessData.SessionExpiresAt!.Value.ToUnixTimeSeconds());
        Assert.Equal(1802178920, set.SessData.ExpiresAt!.Value.ToUnixTimeSeconds());

        var reminder = CookieExpiry.From(set, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        Assert.True(reminder.HasDate);
        Assert.Contains("Cookie 檔與 SESSDATA 工作階段", reminder.Detail);
    }
}
