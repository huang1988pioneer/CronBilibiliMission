using System.Text;
using Sodium;

namespace BilibiliCookieReader.Services;

public static class GitHubSecretProtector
{
    public static string Encrypt(string plainText, string publicKeyBase64)
    {
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("Secret 不能是空的。", nameof(plainText));
        if (string.IsNullOrWhiteSpace(publicKeyBase64))
            throw new ArgumentException("缺少 GitHub public key。", nameof(publicKeyBase64));

        var secretValue = Encoding.UTF8.GetBytes(plainText);
        var publicKey = Convert.FromBase64String(publicKeyBase64.Trim());
        var sealedPublicKeyBox = SealedPublicKeyBox.Create(secretValue, publicKey);
        return Convert.ToBase64String(sealedPublicKeyBox);
    }
}
