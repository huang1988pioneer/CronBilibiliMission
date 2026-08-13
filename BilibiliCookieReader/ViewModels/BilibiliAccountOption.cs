namespace BilibiliCookieReader.ViewModels;

public sealed record BilibiliAccountOption(int Number, string UserName)
{
    public string DisplayName => $"帳號 {Number} · {UserName}";

    public string SecretSuffix => Number == 1 ? string.Empty : Number.ToString();
}
