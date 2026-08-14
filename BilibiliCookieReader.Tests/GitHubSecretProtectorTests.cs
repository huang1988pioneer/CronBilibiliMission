using System.Text;
using BilibiliCookieReader.Services;
using Sodium;
using Xunit;

namespace BilibiliCookieReader.Tests;

public class GitHubSecretProtectorTests
{
    [Fact]
    public void Encrypt_can_be_opened_with_matching_keypair()
    {
        var pair = PublicKeyBox.GenerateKeyPair();
        var publicKey = Convert.ToBase64String(pair.PublicKey);
        var encoded = GitHubSecretProtector.Encrypt("SESSDATA-value%2Ckeep", publicKey);
        var opened = SealedPublicKeyBox.Open(Convert.FromBase64String(encoded), pair);

        Assert.Equal("SESSDATA-value%2Ckeep", Encoding.UTF8.GetString(opened));
    }

    [Fact]
    public void SecretsFromCookies_uses_action_secret_names()
    {
        var cookies = BilibiliCookieParser.ParseText(
            "SESSDATA=s1; bili_jct=j1; DedeUserID=123; buvid3=b1");
        var secrets = GitHubActionsSecretClient.SecretsFromCookies(cookies);

        Assert.Equal(
            new[] { "SESSDATA", "BILI_JCT", "DEDEUSERID", "BUVID3" },
            secrets.Select(item => item.Name));
        Assert.Equal(new[] { "s1", "j1", "123", "b1" }, secrets.Select(item => item.Value));
    }

    [Theory]
    [InlineData("2", "SESSDATA2", "BILI_JCT2", "DEDEUSERID2", "BUVID32")]
    [InlineData("3", "SESSDATA3", "BILI_JCT3", "DEDEUSERID3", "BUVID33")]
    public void SecretsFromCookies_adds_account_suffix(
        string suffix,
        string sessDataName,
        string biliJctName,
        string dedeUserIdName,
        string buvid3Name)
    {
        var cookies = BilibiliCookieParser.ParseText(
            "SESSDATA=s1; bili_jct=j1; DedeUserID=123; buvid3=b1");

        var secrets = GitHubActionsSecretClient.SecretsFromCookies(cookies, suffix);

        Assert.Equal(
            new[] { sessDataName, biliJctName, dedeUserIdName, buvid3Name },
            secrets.Select(item => item.Name));
    }
}
