using System.Text.Json;
using BilibiliCookieReader.Services;
using Xunit;

namespace BilibiliCookieReader.Tests;

public sealed class BilibiliNavClientTests
{
    [Fact]
    public void Parses_unix_timestamp_returned_as_a_numeric_string()
    {
        using var document = JsonDocument.Parse("""{"pub_ts":"1786636800"}""");

        var actual = BilibiliNavClient.ParseUnixTimestamp(document.RootElement, "pub_ts");

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786636800), actual);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("\"not-a-timestamp\"")]
    [InlineData("\"999999999999999999\"")]
    public void Invalid_unix_timestamp_is_ignored_instead_of_throwing(string value)
    {
        using var document = JsonDocument.Parse($"{{\"pub_ts\":{value}}}");

        var actual = BilibiliNavClient.ParseUnixTimestamp(document.RootElement, "pub_ts");

        Assert.Null(actual);
    }

    [Fact]
    public async Task Optional_api_schema_changes_do_not_fail_login_validation()
    {
        var fallback = new object();

        var actual = await BilibiliNavClient.RunOptionalApiAsync<object>(
            () => throw new InvalidOperationException("API field changed type"),
            fallback);

        Assert.Same(fallback, actual);
    }

    [Fact]
    public void Level_six_unbounded_next_experience_does_not_break_login_validation()
    {
        using var document = JsonDocument.Parse(
            """{"level_info":{"current_level":6,"current_exp":28800,"next_exp":"--"}}""");

        var actual = BilibiliNavClient.ParseLevelInfo(document.RootElement);

        Assert.Equal(6, actual.Level);
        Assert.Equal(28800, actual.CurrentExperience);
        Assert.Null(actual.NextExperience);
    }
}
