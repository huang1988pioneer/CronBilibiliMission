using BilibiliCookieReader.Services;
using Xunit;

namespace BilibiliCookieReader.Tests;

public class CookieSessionTests
{
    [Fact]
    public void Open_text_returns_login_cookies_and_status()
    {
        var text = """
            .bilibili.com	TRUE	/	TRUE	1802178920	SESSDATA	1fe57d6c%2C1802178920%2C4fb69
            .bilibili.com	TRUE	/	TRUE	1802178920	bili_jct	aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            .bilibili.com	TRUE	/	TRUE	1802178920	DedeUserID	1
            """;

        var session = CookieSession.FromText(text, @"C:\tmp\cookies.txt");

        Assert.True(session.HasAll);
        Assert.Equal("1fe57d6c%2C1802178920%2C4fb69", session.Cookies.SessData.Value);
        Assert.Contains("讀到 3/3", session.Status);
        Assert.True(session.Reminder.HasDate);
        Assert.Equal(1802178920, session.Reminder.EffectiveExpiresAt!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void Saved_expiry_without_file_is_still_a_reminder()
    {
        var expires = DateTimeOffset.Parse("2026-08-20T00:00:00+08:00");
        var session = CookieSession.FromSavedExpiry(expires, expires);

        Assert.NotNull(session);
        Assert.False(session!.HasAny);
        Assert.True(session.Reminder.HasDate);
        Assert.Contains("上次讀取", session.Reminder.Detail);
    }

    [Fact]
    public async Task Publisher_rejects_bad_repo_without_calling_github()
    {
        var cookies = BilibiliCookieParser.ParseText("SESSDATA=s; bili_jct=j; DedeUserID=1");
        var result = await GitHubSecretPublisher.PublishAsync("not-a-repo", "token", cookies);

        Assert.False(result.Ok);
        Assert.Contains("owner/name", result.Message);
    }

    [Fact]
    public void Publisher_cannot_publish_without_repo()
    {
        Assert.False(GitHubSecretPublisher.CanPublish("bad", token: "x"));
        Assert.True(GitHubSecretPublisher.CanPublish("owner/name", token: "x"));
    }
}
