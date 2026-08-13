using BilibiliCookieReader.Services;
using Xunit;

namespace BilibiliCookieReader.Tests;

public class BilibiliCookieParserTests
{
    [Fact]
    public void Prefers_bilibili_com_over_other_hosts()
    {
        var text = """
            # Netscape HTTP Cookie File
            .huasheng.cn	TRUE	/	TRUE	1999999999	SESSDATA	HUASHENG_SESS
            .huasheng.cn	TRUE	/	TRUE	1999999999	bili_jct	huasheng_jct
            .huasheng.cn	TRUE	/	TRUE	1999999999	DedeUserID	111
            .biligame.com	TRUE	/	TRUE	1999999999	SESSDATA	GAME_SESS
            .biligame.com	TRUE	/	TRUE	1999999999	bili_jct	game_jct
            .biligame.com	TRUE	/	TRUE	1999999999	DedeUserID	222
            .bilibili.com	TRUE	/	TRUE	1999999999	SESSDATA	BILI_SESS%2Ckeep
            .bilibili.com	TRUE	/	TRUE	1999999999	bili_jct	bili_jct_value
            .bilibili.com	TRUE	/	TRUE	1999999999	DedeUserID	2117627494
            """;

        var set = BilibiliCookieParser.ParseText(text);

        Assert.Equal("BILI_SESS%2Ckeep", set.SessData.Value);
        Assert.Equal("bili_jct_value", set.BiliJct.Value);
        Assert.Equal("2117627494", set.DedeUserId.Value);
        Assert.Equal(".bilibili.com", set.SessData.Domain);
        Assert.True(set.HasAll);
        Assert.Contains("SESSDATA=BILI_SESS%2Ckeep", set.ToCookieHeader());
        Assert.Contains("BILI_JCT=bili_jct_value", set.ToEnvBlock());
        Assert.Contains("BILI_JCT=bili_jct_value", set.ToGitHubSecretsBlock());
        Assert.Contains("DEDEUSERID=2117627494", set.ToEnvBlock());
        Assert.Contains("DEDEUSERID=2117627494", set.ToGitHubSecretsBlock());
    }

    [Fact]
    public void Reads_HttpOnly_prefix()
    {
        var text = """
            #HttpOnly_.bilibili.com	TRUE	/	TRUE	1999999999	SESSDATA	http_only_sess
            .bilibili.com	TRUE	/	TRUE	1999999999	bili_jct	jct
            .bilibili.com	TRUE	/	TRUE	1999999999	DedeUserID	99
            """;

        var set = BilibiliCookieParser.ParseText(text);

        Assert.Equal("http_only_sess", set.SessData.Value);
        Assert.Equal("jct", set.BiliJct.Value);
        Assert.Equal("99", set.DedeUserId.Value);
    }

    [Fact]
    public void Keeps_expired_value_if_it_is_the_only_one()
    {
        var text = """
            .bilibili.com	TRUE	/	TRUE	1000000000	SESSDATA	old_sess
            .bilibili.com	TRUE	/	TRUE	1999999999	bili_jct	jct
            .bilibili.com	TRUE	/	TRUE	1999999999	DedeUserID	1
            """;

        var set = BilibiliCookieParser.ParseText(text);

        Assert.Equal("old_sess", set.SessData.Value);
        Assert.True(set.SessData.IsExpired);
        Assert.Contains(set.Warnings, warning => warning.Contains("SESSDATA"));
    }

    [Fact]
    public void Prefers_unexpired_over_expired_same_domain()
    {
        var text = """
            .bilibili.com	TRUE	/	TRUE	1000000000	SESSDATA	old_sess
            .bilibili.com	TRUE	/	TRUE	1999999999	SESSDATA	new_sess
            .bilibili.com	TRUE	/	TRUE	1999999999	bili_jct	jct
            .bilibili.com	TRUE	/	TRUE	1999999999	DedeUserID	1
            """;

        var set = BilibiliCookieParser.ParseText(text);

        Assert.Equal("new_sess", set.SessData.Value);
        Assert.False(set.SessData.IsExpired);
    }

    [Fact]
    public void Parses_cookie_header_string()
    {
        var set = BilibiliCookieParser.ParseText(
            "SESSDATA=aaa; bili_jct=bbb; DedeUserID=123; other=ignore");

        Assert.Equal("aaa", set.SessData.Value);
        Assert.Equal("bbb", set.BiliJct.Value);
        Assert.Equal("123", set.DedeUserId.Value);
        Assert.Equal("SESSDATA=aaa; bili_jct=bbb; DedeUserID=123", set.ToCookieHeader());
    }

    [Fact]
    public void Parses_json_object_and_array()
    {
        var objectSet = BilibiliCookieParser.ParseText(
            """{"SESSDATA":"s1","BILI_JCT":"j1","DEDEUSERID":"u1"}""");
        Assert.Equal("s1", objectSet.SessData.Value);
        Assert.Equal("j1", objectSet.BiliJct.Value);
        Assert.Equal("u1", objectSet.DedeUserId.Value);

        var arraySet = BilibiliCookieParser.ParseText(
            """
            [
              {"name":"SESSDATA","value":"s2","domain":".bilibili.com"},
              {"name":"bili_jct","value":"j2","domain":".bilibili.com"},
              {"name":"DedeUserID","value":"u2","domain":".bilibili.com"}
            ]
            """);
        Assert.Equal("s2", arraySet.SessData.Value);
        Assert.Equal("j2", arraySet.BiliJct.Value);
        Assert.Equal("u2", arraySet.DedeUserId.Value);
    }

    [Fact]
    public void Real_documents_cookie_file_prefers_bilibili_domain()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "abuhg17_cookies.txt");
        if (!File.Exists(path))
            return;

        var set = BilibiliCookieParser.ParseFile(path);

        Assert.True(set.HasAll);
        Assert.Contains("bilibili.com", set.SessData.Domain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("huasheng", set.SessData.Domain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("huasheng", set.BiliJct.Domain, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(set.SessData.Value));
        Assert.Contains("%2C", set.SessData.Value);
        Assert.Equal(32, set.BiliJct.Value.Length);
        Assert.All(set.BiliJct.Value, ch => Assert.True(Uri.IsHexDigit(ch)));
    }

    [Fact]
    public void Empty_or_unrelated_file_throws()
    {
        Assert.Throws<InvalidOperationException>(() => BilibiliCookieParser.ParseText(""));
        Assert.Throws<InvalidOperationException>(() =>
            BilibiliCookieParser.ParseText(".example.com\tTRUE\t/\tFALSE\t0\tfoo\tbar\n"));
    }
}
