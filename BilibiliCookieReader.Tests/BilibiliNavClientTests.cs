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
}
