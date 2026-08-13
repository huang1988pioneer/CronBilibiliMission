using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BilibiliCookieReader.Services;

public sealed record GitHubSecretUpdateResult(bool Ok, string Message, IReadOnlyList<string> UpdatedNames);

public static class GitHubActionsSecretClient
{
    public static readonly IReadOnlyList<string> ActionSecretNames = ["SESSDATA", "BILI_JCT", "DEDEUSERID"];

    private const string ApiVersion = "2022-11-28";
    private const string UserAgent = "BilibiliCookieReader";

    private static readonly HttpClient Http = CreateClient();

    public static IReadOnlyList<(string Name, string Value)> SecretsFromCookies(BilibiliCookieSet cookies)
    {
        if (!cookies.HasAll)
            throw new InvalidOperationException("三個欄位不齊，無法更新 GitHub Secrets。");

        return
        [
            ("SESSDATA", cookies.SessData.Value),
            ("BILI_JCT", cookies.BiliJct.Value),
            ("DEDEUSERID", cookies.DedeUserId.Value),
        ];
    }

    public static async Task<GitHubSecretUpdateResult> UpdateAsync(
        GitHubRepoSlug repo,
        string token,
        IReadOnlyList<(string Name, string Value)> secrets,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new GitHubSecretUpdateResult(false, "缺少 GitHub 權杖。", []);

        try
        {
            var (keyId, publicKey) = await GetPublicKeyAsync(repo, token, cancellationToken).ConfigureAwait(false);
            var updated = new List<string>();

            foreach (var (name, value) in secrets)
            {
                var encrypted = GitHubSecretProtector.Encrypt(value, publicKey);
                await PutSecretAsync(repo, token, name, encrypted, keyId, cancellationToken).ConfigureAwait(false);
                updated.Add(name);
            }

            return new GitHubSecretUpdateResult(
                true,
                $"已更新 {repo.FullName} 的 {string.Join("、", updated)}。",
                updated);
        }
        catch (GitHubApiException ex)
        {
            return new GitHubSecretUpdateResult(false, ex.Message, []);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException or ArgumentException)
        {
            return new GitHubSecretUpdateResult(false, $"更新失敗：{ex.Message}", []);
        }
    }

    public static async Task<GitHubSecretUpdateResult> UpdateWithFallbackAsync(
        GitHubRepoSlug repo,
        string? token,
        IReadOnlyList<(string Name, string Value)> secrets,
        CancellationToken cancellationToken = default)
    {
        var resolvedToken = string.IsNullOrWhiteSpace(token) ? GitHubCli.TryGetToken() : token;
        if (!string.IsNullOrWhiteSpace(resolvedToken))
        {
            var api = await UpdateAsync(repo, resolvedToken, secrets, cancellationToken).ConfigureAwait(false);
            if (api.Ok)
                return api;
            if (!GitHubCli.IsAvailable())
                return api;

            var cli = await GitHubCli.SetSecretsAsync(repo, secrets, cancellationToken).ConfigureAwait(false);
            return new GitHubSecretUpdateResult(cli.Ok, cli.Ok ? cli.Message : $"{api.Message}；改用 gh 也失敗：{cli.Message}", cli.Ok ? secrets.Select(item => item.Name).ToArray() : []);
        }

        if (GitHubCli.IsAvailable())
        {
            var cli = await GitHubCli.SetSecretsAsync(repo, secrets, cancellationToken).ConfigureAwait(false);
            return new GitHubSecretUpdateResult(
                cli.Ok,
                cli.Message,
                cli.Ok ? secrets.Select(item => item.Name).ToArray() : []);
        }

        return new GitHubSecretUpdateResult(
            false,
            "沒有 GitHub 權杖。請貼上有 repo / Secrets 寫入權限的 PAT，或先安裝 GitHub CLI 並執行 gh auth login。",
            []);
    }

    private static async Task<(string KeyId, string Key)> GetPublicKeyAsync(
        GitHubRepoSlug repo,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/repos/{repo.Owner}/{repo.Name}/actions/secrets/public-key", token);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body, repo);

        using var doc = JsonDocument.Parse(body);
        var keyId = doc.RootElement.GetProperty("key_id").GetString();
        var key = doc.RootElement.GetProperty("key").GetString();
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(key))
            throw new GitHubApiException("GitHub 沒有回傳可用的 public key。");
        return (keyId, key);
    }

    private static async Task PutSecretAsync(
        GitHubRepoSlug repo,
        string token,
        string name,
        string encryptedValue,
        string keyId,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Put,
            $"/repos/{repo.Owner}/{repo.Name}/actions/secrets/{Uri.EscapeDataString(name)}",
            token);
        var payload = JsonSerializer.Serialize(new
        {
            encrypted_value = encryptedValue,
            key_id = keyId,
        });
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is not System.Net.HttpStatusCode.Created and not System.Net.HttpStatusCode.NoContent)
            EnsureSuccess(response, body, repo, name);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, "https://api.github.com" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body, GitHubRepoSlug repo, string? secretName = null)
    {
        if (response.IsSuccessStatusCode)
            return;

        var hint = (int)response.StatusCode switch
        {
            401 => "權杖無效或已過期。請重新貼上 PAT，或執行 gh auth login。",
            403 => "這個權杖沒有寫入 Actions Secrets 的權限。Classic PAT 需要 repo 範圍；fine-grained PAT 需要 Secrets: Read and write。",
            404 => $"找不到 {repo.FullName}，或權杖無法存取這個 repo。",
            _ => $"HTTP {(int)response.StatusCode}",
        };

        var apiMessage = TryReadMessage(body);
        var target = secretName is null ? repo.FullName : $"{repo.FullName} 的 {secretName}";
        throw new GitHubApiException($"更新 {target} 失敗：{hint}" + (string.IsNullOrWhiteSpace(apiMessage) ? "" : $"（{apiMessage}）"));
    }

    private static string? TryReadMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}

public sealed class GitHubApiException : Exception
{
    public GitHubApiException(string message) : base(message)
    {
    }
}
