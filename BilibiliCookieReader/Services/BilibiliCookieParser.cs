using System.Text.Json;

namespace BilibiliCookieReader.Services;

/// <summary>
/// Reads Netscape cookies.txt (and a few common export shapes) and picks
/// Bilibili login cookies. .bilibili.com wins over same-named cookies on
/// other hosts such as huasheng.cn or biligame.com.
/// </summary>
public static class BilibiliCookieParser
{
    public const string SessDataName = "SESSDATA";
    public const string BiliJctName = "BILI_JCT";
    public const string DedeUserIdName = "DEDEUSERID";
    public const string Buvid3Name = "BUVID3";

    public static readonly IReadOnlyList<string> DefaultFileNames =
    [
        "abuhg17_cookies.txt",
        "cookies.txt",
        "bilibili_cookies.txt",
    ];

    public static string? FindDefaultCookieFile()
    {
        var dirs = new List<string>();
        AddIfPresent(dirs, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            AddIfPresent(dirs, Path.Combine(profile, "OneDrive", "Documents"));
            AddIfPresent(dirs, Path.Combine(profile, "OneDrive", "文件"));
        }

        foreach (var dir in dirs)
        {
            foreach (var name in DefaultFileNames)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }

    public static BilibiliCookieSet ParseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("路徑不能為空。", nameof(path));

        path = path.Trim().Trim('"');
        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到 Cookie 檔案：{path}", path);

        return ParseText(File.ReadAllText(path), path);
    }

    public static BilibiliCookieSet ParseText(string text, string? sourcePath = null)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));

        var trimmed = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (trimmed.Length == 0)
            throw new InvalidOperationException("檔案是空的。");

        if (trimmed[0] is '{' or '[')
        {
            var jsonSet = TryParseJson(trimmed);
            if (jsonSet is not null)
                return Finalize(jsonSet, sourcePath);
        }

        var netscape = ParseNetscapeLines(SplitLines(text));
        if (netscape.HasAny)
            return Finalize(netscape, sourcePath);

        var header = ParseCookieHeader(text);
        if (header.HasAny)
            return Finalize(header, sourcePath);

        throw new InvalidOperationException(
            "找不到有效的 Bilibili Cookie（SESSDATA / bili_jct / DedeUserID / buvid3）。" +
            "請確認檔案是已登入 bilibili.com 後匯出的 Netscape cookies.txt。");
    }

    internal static BilibiliCookieSet ParseNetscapeLines(IEnumerable<string> lines)
    {
        var best = new Dictionary<string, RankedCookie>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in lines)
        {
            if (!TryReadNetscapeLine(raw, out var cookie))
                continue;

            if (!IsBilibiliRelatedDomain(cookie.Domain))
                continue;

            if (string.IsNullOrWhiteSpace(cookie.Name) || string.IsNullOrWhiteSpace(cookie.Value))
                continue;

            var ranked = new RankedCookie(cookie, DomainScore(cookie.Domain));
            if (!best.TryGetValue(cookie.Name, out var existing) || ranked.IsBetterThan(existing))
                best[cookie.Name] = ranked;
        }

        return FromMap(best);
    }

    internal static bool TryReadNetscapeLine(string? raw, out ParsedCookie cookie)
    {
        cookie = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var line = raw.TrimEnd('\r');
        if (line.StartsWith('#') && !line.StartsWith("#HttpOnly_", StringComparison.OrdinalIgnoreCase))
            return false;

        if (line.StartsWith("#HttpOnly_", StringComparison.OrdinalIgnoreCase))
            line = line["#HttpOnly_".Length..];

        var parts = line.Split('\t');
        if (parts.Length < 7)
            return false;

        var domain = parts[0].Trim();
        var name = parts[5].Trim();
        var value = string.Join('\t', parts.Skip(6)).Trim();
        DateTimeOffset? expires = null;
        if (long.TryParse(parts[4], out var exp) && exp > 0)
            expires = DateTimeOffset.FromUnixTimeSeconds(exp);

        cookie = new ParsedCookie(name, value, domain, expires);
        return true;
    }

    internal static BilibiliCookieSet ParseCookieHeader(string text)
    {
        var map = new Dictionary<string, RankedCookie>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = raw.Trim();
            var eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;

            var name = pair[..eq].Trim();
            var value = pair[(eq + 1)..].Trim();
            if (name.Length == 0 || value.Length == 0)
                continue;

            if (!IsKnownName(name))
                continue;

            map[name] = new RankedCookie(new ParsedCookie(name, value, "header", null), 1);
        }

        return FromMap(map);
    }

    internal static BilibiliCookieSet? TryParseJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var map = new Dictionary<string, RankedCookie>(StringComparer.OrdinalIgnoreCase);
            CollectJson(doc.RootElement, map);
            var set = FromMap(map);
            return set.HasAny ? set : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void CollectJson(JsonElement element, Dictionary<string, RankedCookie> map)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectJson(item, map);
                break;
            case JsonValueKind.Object:
                if (TryReadJsonCookieObject(element, out var cookie))
                {
                    if (IsBilibiliRelatedDomain(cookie.Domain) || cookie.Domain is "json" or "")
                    {
                        var ranked = new RankedCookie(cookie, DomainScore(cookie.Domain));
                        if (!map.TryGetValue(cookie.Name, out var existing) || ranked.IsBetterThan(existing))
                            map[cookie.Name] = ranked;
                    }
                    break;
                }

                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String && IsKnownName(prop.Name))
                    {
                        var parsed = new ParsedCookie(prop.Name, prop.Value.GetString() ?? "", "json", null);
                        map[prop.Name] = new RankedCookie(parsed, 1);
                    }
                    else
                    {
                        CollectJson(prop.Value, map);
                    }
                }
                break;
        }
    }

    private static bool TryReadJsonCookieObject(JsonElement element, out ParsedCookie cookie)
    {
        cookie = default;
        if (!element.TryGetProperty("name", out var nameEl) && !element.TryGetProperty("Name", out nameEl))
            return false;
        if (!element.TryGetProperty("value", out var valueEl) && !element.TryGetProperty("Value", out valueEl))
            return false;

        var name = nameEl.GetString();
        var value = valueEl.GetString();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
            return false;

        var domain = ReadString(element, "domain", "Domain") ?? "json";
        DateTimeOffset? expires = null;
        var expText = ReadString(element, "expirationDate", "expires", "Expires");
        if (double.TryParse(expText, out var exp) && exp > 0)
            expires = DateTimeOffset.FromUnixTimeSeconds((long)exp);

        cookie = new ParsedCookie(name, value, domain, expires);
        return true;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var prop))
                continue;
            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString(),
                JsonValueKind.Number => prop.ToString(),
                _ => null,
            };
        }

        return null;
    }

    private static BilibiliCookieSet FromMap(IReadOnlyDictionary<string, RankedCookie> map)
    {
        map.TryGetValue("SESSDATA", out var sess);
        map.TryGetValue("bili_jct", out var jct);
        if (jct is null)
            map.TryGetValue("BILI_JCT", out jct);
        map.TryGetValue("DedeUserID", out var mid);
        if (mid is null)
            map.TryGetValue("DEDEUSERID", out mid);
        map.TryGetValue("buvid3", out var buvid3);
        if (buvid3 is null)
            map.TryGetValue("BUVID3", out buvid3);

        return new BilibiliCookieSet
        {
            SessData = ToField(SessDataName, "SESSDATA", sess),
            BiliJct = ToField(BiliJctName, "BILI_JCT", jct),
            DedeUserId = ToField(DedeUserIdName, "DEDEUSERID", mid),
            Buvid3 = ToField(Buvid3Name, "BUVID3", buvid3),
        };
    }

    private static CookieField ToField(string envName, string secretName, RankedCookie? ranked)
    {
        if (ranked is null)
        {
            return new CookieField
            {
                EnvName = envName,
                SecretName = secretName,
            };
        }

        var sessionExpires = envName.Equals(SessDataName, StringComparison.OrdinalIgnoreCase)
            ? CookieExpiry.TryParseSessDataSessionExpiry(ranked.Cookie.Value)
            : null;

        return new CookieField
        {
            EnvName = envName,
            SecretName = secretName,
            Value = ranked.Cookie.Value,
            Domain = ranked.Cookie.Domain,
            ExpiresAt = ranked.Cookie.ExpiresAt,
            SessionExpiresAt = sessionExpires,
        };
    }

    private static BilibiliCookieSet Finalize(BilibiliCookieSet set, string? sourcePath)
    {
        var warnings = new List<string>();
        if (!set.SessData.HasValue)
            warnings.Add("缺少 SESSDATA。");
        if (!set.BiliJct.HasValue)
            warnings.Add("缺少 BILI_JCT（bili_jct）。");
        if (!set.DedeUserId.HasValue)
            warnings.Add("缺少 DEDEUSERID（DedeUserID）。");
        if (!set.Buvid3.HasValue)
            warnings.Add("缺少 BUVID3（buvid3）；投幣與分享可能被 Bilibili 風控拒絕。");

        foreach (var field in set.Fields)
        {
            if (field.IsExpired)
                warnings.Add($"{field.EnvName} 看起來已過期（{field.ExpiresText}）。");
        }

        if (!set.HasAny)
        {
            throw new InvalidOperationException(
                "找不到有效的 Bilibili Cookie（SESSDATA / bili_jct / DedeUserID / buvid3）。" +
                "請確認檔案是已登入 bilibili.com 後匯出的 Netscape cookies.txt。");
        }

        return set with
        {
            SourcePath = sourcePath ?? set.SourcePath,
            Warnings = warnings,
        };
    }

    internal static bool IsBilibiliRelatedDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return false;

        var d = NormalizeDomain(domain);
        if (d is "header" or "json")
            return true;

        return d == "bilibili.com"
               || d.EndsWith(".bilibili.com", StringComparison.Ordinal)
               || d == "biliapi.net"
               || d.EndsWith(".biliapi.net", StringComparison.Ordinal)
               || d == "biliapi.com"
               || d.EndsWith(".biliapi.com", StringComparison.Ordinal)
               || d == "biligame.com"
               || d.EndsWith(".biligame.com", StringComparison.Ordinal);
    }

    internal static int DomainScore(string? domain)
    {
        var d = NormalizeDomain(domain);
        return d switch
        {
            "bilibili.com" => 100,
            "www.bilibili.com" => 90,
            "api.bilibili.com" => 80,
            "biliapi.net" or "api.biliapi.net" => 40,
            "biliapi.com" or "api.biliapi.com" => 40,
            "biligame.com" => 10,
            "header" or "json" => 1,
            _ when d.EndsWith(".bilibili.com", StringComparison.Ordinal) => 70,
            _ when d.EndsWith(".biligame.com", StringComparison.Ordinal) => 8,
            _ => 0,
        };
    }

    internal static bool IsKnownName(string name) =>
        name.Equals("SESSDATA", StringComparison.OrdinalIgnoreCase)
        || name.Equals("bili_jct", StringComparison.OrdinalIgnoreCase)
        || name.Equals("BILI_JCT", StringComparison.OrdinalIgnoreCase)
        || name.Equals("DedeUserID", StringComparison.OrdinalIgnoreCase)
        || name.Equals("DEDEUSERID", StringComparison.OrdinalIgnoreCase)
        || name.Equals("buvid3", StringComparison.OrdinalIgnoreCase)
        || name.Equals("BUVID3", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return string.Empty;
        return domain.Trim().TrimStart('.').ToLowerInvariant();
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            yield return line;
    }

    private static void AddIfPresent(List<string> dirs, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && !dirs.Contains(path, StringComparer.OrdinalIgnoreCase))
            dirs.Add(path);
    }

    internal readonly record struct ParsedCookie(
        string Name,
        string Value,
        string Domain,
        DateTimeOffset? ExpiresAt);

    private sealed record RankedCookie(ParsedCookie Cookie, int Score)
    {
        public bool IsBetterThan(RankedCookie other)
        {
            if (Score != other.Score)
                return Score > other.Score;

            var thisExpired = IsExpired(Cookie.ExpiresAt);
            var otherExpired = IsExpired(other.Cookie.ExpiresAt);
            if (thisExpired != otherExpired)
                return !thisExpired;

            if (Cookie.ExpiresAt is { } a && other.Cookie.ExpiresAt is { } b)
                return a > b;

            return true;
        }

        private static bool IsExpired(DateTimeOffset? expires) =>
            expires is { } value && value < DateTimeOffset.UtcNow;
    }
}

public sealed record BilibiliCookieSet
{
    public CookieField SessData { get; init; } = new() { EnvName = "SESSDATA", SecretName = "SESSDATA" };
    public CookieField BiliJct { get; init; } = new() { EnvName = "BILI_JCT", SecretName = "BILI_JCT" };
    public CookieField DedeUserId { get; init; } = new() { EnvName = "DEDEUSERID", SecretName = "DEDEUSERID" };
    public CookieField Buvid3 { get; init; } = new() { EnvName = "BUVID3", SecretName = "BUVID3" };
    public string? SourcePath { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<CookieField> Fields => [SessData, BiliJct, DedeUserId, Buvid3];

    public bool HasAny => Fields.Any(item => item.HasValue);
    public bool HasAll => Fields.All(item => item.HasValue);

    public string ToCookieHeader()
    {
        var parts = new List<string>();
        if (SessData.HasValue)
            parts.Add($"SESSDATA={SessData.Value}");
        if (BiliJct.HasValue)
            parts.Add($"bili_jct={BiliJct.Value}");
        if (DedeUserId.HasValue)
            parts.Add($"DedeUserID={DedeUserId.Value}");
        if (Buvid3.HasValue)
            parts.Add($"buvid3={Buvid3.Value}");
        return string.Join("; ", parts);
    }

    public string ToEnvBlock() => string.Join(
        Environment.NewLine,
        Fields.Where(item => item.HasValue).Select(item => $"{item.EnvName}={item.Value}"));

    public string ToGitHubSecretsBlock() => string.Join(
        Environment.NewLine,
        Fields.Where(item => item.HasValue).Select(item => $"{item.SecretName}={item.Value}"));

    public string ToPowerShellBlock() => string.Join(
        Environment.NewLine,
        Fields.Where(item => item.HasValue)
            .Select(item => $"$env:{item.EnvName}='{item.Value.Replace("'", "''")}'"));

    public string ToBashBlock() => string.Join(
        Environment.NewLine,
        Fields.Where(item => item.HasValue)
            .Select(item => $"export {item.EnvName}='{item.Value.Replace("'", "'\\''")}'"));
}

public sealed record CookieField
{
    public required string EnvName { get; init; }
    public required string SecretName { get; init; }
    public string Value { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? SessionExpiresAt { get; init; }

    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    public DateTimeOffset? EffectiveExpiresAt => CookieExpiry.Min(ExpiresAt, SessionExpiresAt);

    public bool IsExpired => EffectiveExpiresAt is { } expires && expires < DateTimeOffset.UtcNow;

    public bool IsExpiringSoon =>
        !IsExpired
        && EffectiveExpiresAt is { } expires
        && CookieExpiry.DaysRemaining(expires, DateTimeOffset.UtcNow) <= CookieExpiry.SoonDays;

    public string ExpiresText => EffectiveExpiresAt is { } expires
        ? CookieExpiry.FormatTaipei(expires)
        : "到期時間未知";

    public string MetaText
    {
        get
        {
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(Domain) && Domain is not ("header" or "json"))
                bits.Add(Domain);

            if (ExpiresAt is not null && SessionExpiresAt is not null
                && !CookieExpiry.SameMinute(ExpiresAt, SessionExpiresAt))
            {
                bits.Add($"Cookie 到期 {CookieExpiry.FormatTaipei(ExpiresAt.Value)}");
                bits.Add($"工作階段到期 {CookieExpiry.FormatTaipei(SessionExpiresAt.Value)}");
            }
            else if (EffectiveExpiresAt is not null)
            {
                var label = SessionExpiresAt is not null && ExpiresAt is not null
                    ? "Cookie／工作階段"
                    : SessionExpiresAt is not null
                        ? "工作階段"
                        : "Cookie";
                bits.Add(IsExpired ? $"已過期 · {label} {ExpiresText}" : $"{label}到期 {ExpiresText}");
            }

            return bits.Count == 0 ? "—" : string.Join(" · ", bits);
        }
    }

    public string MaskedValue
    {
        get
        {
            if (!HasValue)
                return string.Empty;
            if (Value.Length <= 10)
                return new string('•', Value.Length);
            return $"{Value[..6]}…{Value[^4..]}";
        }
    }
}
