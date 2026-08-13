namespace BilibiliCookieReader.Services;

public readonly record struct GitHubRepoSlug(string Owner, string Name)
{
    public const string Default = "huang1988pioneer/CronBilibiliMission";

    public string FullName => $"{Owner}/{Name}";

    public override string ToString() => FullName;

    public static bool TryParse(string? text, out GitHubRepoSlug slug)
    {
        slug = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim().TrimEnd('/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            value = value[..^4];

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Host.Contains("github", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return false;
            slug = new GitHubRepoSlug(parts[0], parts[1]);
            return IsValid(slug);
        }

        var slash = value.IndexOf('/');
        if (slash <= 0 || slash != value.LastIndexOf('/') || slash == value.Length - 1)
            return false;

        slug = new GitHubRepoSlug(value[..slash], value[(slash + 1)..]);
        return IsValid(slug);
    }

    public static GitHubRepoSlug ParseOrDefault(string? text)
    {
        if (TryParse(text, out var slug))
            return slug;
        if (!TryParse(Default, out slug))
            throw new InvalidOperationException("預設 repo 名稱無效。");
        return slug;
    }

    private static bool IsValid(GitHubRepoSlug slug) =>
        slug.Owner.Length > 0
        && slug.Name.Length > 0
        && slug.Owner.All(IsSlugChar)
        && slug.Name.All(IsSlugChar);

    private static bool IsSlugChar(char ch) =>
        char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.';
}
