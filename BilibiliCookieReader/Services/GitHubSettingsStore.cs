using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BilibiliCookieReader.Services;

public sealed class GitHubSettings
{
    public string Repo { get; set; } = GitHubRepoSlug.Default;
    public string? Token { get; set; }
    public bool RememberToken { get; set; }
}

public static class GitHubSettingsStore
{
    private static readonly byte[] Entropy = "BilibiliCookieReader.GitHubToken.v1"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BilibiliCookieReader");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static GitHubSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new GitHubSettings();

            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                         ?? new StoredSettings();
            return new GitHubSettings
            {
                Repo = string.IsNullOrWhiteSpace(stored.Repo) ? GitHubRepoSlug.Default : stored.Repo,
                RememberToken = stored.RememberToken,
                Token = stored.RememberToken ? Unprotect(stored.ProtectedToken) : null,
            };
        }
        catch
        {
            return new GitHubSettings();
        }
    }

    public static void Save(GitHubSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var stored = new StoredSettings
        {
            Repo = string.IsNullOrWhiteSpace(settings.Repo) ? GitHubRepoSlug.Default : settings.Repo.Trim(),
            RememberToken = settings.RememberToken,
            ProtectedToken = settings.RememberToken ? Protect(settings.Token) : null,
        };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(stored, JsonOptions));
    }

    private static string? Protect(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var bytes = Encoding.UTF8.GetBytes(token);
        if (OperatingSystem.IsWindows())
        {
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        return Convert.ToBase64String(bytes);
    }

    private static string? Unprotect(string? protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken))
            return null;

        try
        {
            var bytes = Convert.FromBase64String(protectedToken);
            if (OperatingSystem.IsWindows())
                bytes = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    private sealed class StoredSettings
    {
        public string Repo { get; set; } = GitHubRepoSlug.Default;
        public string? ProtectedToken { get; set; }
        public bool RememberToken { get; set; }
    }
}
