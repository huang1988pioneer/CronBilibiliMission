namespace BilibiliCookieReader.Services;

/// <summary>
/// Deep Secret Publish module. Callers pass a repo string, a token, and
/// Login Cookies. Encrypt, public-key fetch, and gh fallback stay inside.
/// </summary>
public static class GitHubSecretPublisher
{
    public static bool CanPublish(string? repoText, string? token) =>
        GitHubRepoSlug.TryParse(repoText, out _)
        && (!string.IsNullOrWhiteSpace(token) || GitHubCli.IsAvailable());

    public static async Task<GitHubSecretUpdateResult> PublishAsync(
        string repoText,
        string? token,
        BilibiliCookieSet cookies,
        CancellationToken cancellationToken = default)
    {
        if (!GitHubRepoSlug.TryParse(repoText, out var repo))
        {
            return new GitHubSecretUpdateResult(
                false,
                "Repo 格式應為 owner/name，例如 huang1988pioneer/CronBilibiliMission。",
                []);
        }

        IReadOnlyList<(string Name, string Value)> secrets;
        try
        {
            secrets = GitHubActionsSecretClient.SecretsFromCookies(cookies);
        }
        catch (InvalidOperationException ex)
        {
            return new GitHubSecretUpdateResult(false, ex.Message, []);
        }

        return await GitHubActionsSecretClient
            .UpdateWithFallbackAsync(repo, token, secrets, cancellationToken)
            .ConfigureAwait(false);
    }

    public static GitHubSettings LoadPreferences() => GitHubSettingsStore.Load();

    public static void SavePreferences(string repo, string? token, bool rememberToken)
    {
        var previous = GitHubSettingsStore.Load();
        GitHubSettingsStore.Save(new GitHubSettings
        {
            Repo = repo.Trim(),
            Token = token,
            RememberToken = rememberToken,
            LastCookieExpiresAt = previous.LastCookieExpiresAt,
            LastSessionExpiresAt = previous.LastSessionExpiresAt,
        });
    }

    public static void RememberExpiry(CookieExpiryReminder reminder) =>
        GitHubSettingsStore.SaveExpiry(reminder.CookieExpiresAt, reminder.SessionExpiresAt);

    public static string? TryResolveToken(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
            return preferred.Trim();
        return GitHubCli.TryGetToken();
    }
}
