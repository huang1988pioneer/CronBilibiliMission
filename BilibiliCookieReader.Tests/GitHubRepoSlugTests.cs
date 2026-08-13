using BilibiliCookieReader.Services;
using Xunit;

namespace BilibiliCookieReader.Tests;

public class GitHubRepoSlugTests
{
    [Theory]
    [InlineData("huang1988pioneer/CronBilibiliMission", "huang1988pioneer", "CronBilibiliMission")]
    [InlineData("https://github.com/huang1988pioneer/CronBilibiliMission", "huang1988pioneer", "CronBilibiliMission")]
    [InlineData("https://github.com/huang1988pioneer/CronBilibiliMission.git", "huang1988pioneer", "CronBilibiliMission")]
    [InlineData("https://github.com/huang1988pioneer/CronBilibiliMission/", "huang1988pioneer", "CronBilibiliMission")]
    public void Parses_owner_and_name(string text, string owner, string name)
    {
        Assert.True(GitHubRepoSlug.TryParse(text, out var slug));
        Assert.Equal(owner, slug.Owner);
        Assert.Equal(name, slug.Name);
        Assert.Equal($"{owner}/{name}", slug.FullName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("onlyowner")]
    [InlineData("owner/name/extra")]
    [InlineData("https://example.com/owner/name")]
    public void Rejects_invalid_text(string text)
    {
        Assert.False(GitHubRepoSlug.TryParse(text, out _));
    }

    [Fact]
    public void ParseOrDefault_falls_back_to_this_repo()
    {
        var slug = GitHubRepoSlug.ParseOrDefault("not a repo");
        Assert.Equal("huang1988pioneer", slug.Owner);
        Assert.Equal("CronBilibiliMission", slug.Name);
    }
}
