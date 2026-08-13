using System.Diagnostics;
using System.Text;

namespace BilibiliCookieReader.Services;

public static class GitHubCli
{
    public static bool IsAvailable() => !string.IsNullOrWhiteSpace(ResolveExecutable());

    public static string? TryGetToken()
    {
        var env = Environment.GetEnvironmentVariable("GH_TOKEN")
                  ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        var (ok, stdout, _) = Run("auth token");
        return ok && !string.IsNullOrWhiteSpace(stdout) ? stdout.Trim() : null;
    }

    public static async Task<(bool Ok, string Message)> SetSecretsAsync(
        GitHubRepoSlug repo,
        IReadOnlyList<(string Name, string Value)> secrets,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable())
            return (false, "找不到 GitHub CLI（gh）。請安裝後執行 gh auth login，或改貼 GitHub 權杖。");

        var updated = new List<string>();
        foreach (var (name, value) in secrets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (ok, stdout, stderr) = await RunAsync(
                $"secret set {Quote(name)} --repo {Quote(repo.FullName)}",
                value,
                cancellationToken).ConfigureAwait(false);
            if (!ok)
                return (false, $"更新 {name} 失敗：{Trim(stderr.Length > 0 ? stderr : stdout)}");
            updated.Add(name);
        }

        return (true, $"已用 GitHub CLI 更新 {string.Join("、", updated)}。");
    }

    private static (bool Ok, string Stdout, string Stderr) Run(string arguments) =>
        RunAsync(arguments, stdin: null, CancellationToken.None).GetAwaiter().GetResult();

    private static async Task<(bool Ok, string Stdout, string Stderr)> RunAsync(
        string arguments,
        string? stdin,
        CancellationToken cancellationToken)
    {
        var fileName = ResolveExecutable();
        if (fileName is null)
            return (false, "", "gh not found");

        var start = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return (false, "", "無法啟動 gh。");

            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return (process.ExitCode == 0, stdout, stderr);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return (false, "", ex.Message);
        }
    }

    private static string? ResolveExecutable()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var names = OperatingSystem.IsWindows()
            ? new[] { "gh.exe", "gh.cmd" }
            : new[] { "gh" };

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static string Trim(string text)
    {
        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 240 ? oneLine : oneLine[..240] + "…";
    }
}
