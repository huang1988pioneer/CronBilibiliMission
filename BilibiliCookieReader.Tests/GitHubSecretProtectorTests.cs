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
            "SESSDATA=s1; bili_jct=j1; DedeUserID=123");
        var secrets = GitHubActionsSecretClient.SecretsFromCookies(cookies);

        Assert.Equal(
            new[] { "SESSDATA", "BILI_JCT", "DEDEUSERID" },
            secrets.Select(item => item.Name));
        Assert.Equal(new[] { "s1", "j1", "123" }, secrets.Select(item => item.Value));
    }
}
