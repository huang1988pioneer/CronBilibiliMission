using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using BilibiliCookieReader.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BilibiliCookieReader.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private TopLevel? _topLevel;
    private BilibiliCookieSet? _cookies;

    public CookieFieldViewModel SessData { get; } = new("SESSDATA", "SESSDATA");
    public CookieFieldViewModel BiliJct { get; } = new("BILI_JCT", "BILI_JCT");
    public CookieFieldViewModel DedeUserId { get; } = new("DEDEUSERID", "DEDEUSERID");

    public IReadOnlyList<CookieFieldViewModel> Fields { get; }

    [ObservableProperty]
    public partial string CookiePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "選擇或拖放 Netscape cookies.txt，讀取 SESSDATA、BILI_JCT、DEDEUSERID。";

    [ObservableProperty]
    public partial bool IsError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyAllEnvCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyAllSecretsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPowerShellCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyBashCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyCookieHeaderCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyLoginCommand))]
    public partial bool HasResult { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyLoginCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateGitHubSecretsCommand))]
    public partial bool HasAllThree { get; set; }

    [ObservableProperty]
    public partial bool RevealValues { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateGitHubSecretsCommand))]
    public partial string GitHubRepo { get; set; } = GitHubRepoSlug.Default;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateGitHubSecretsCommand))]
    public partial string GitHubToken { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool RememberGitHubToken { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateGitHubSecretsCommand))]
    public partial bool ConfirmOverwriteSecrets { get; set; }

    public string RevealButtonText => RevealValues ? "隱藏明文" : "顯示明文";

    public MainViewModel()
    {
        Fields = [SessData, BiliJct, DedeUserId];
        foreach (var field in Fields)
            field.CopyRequested = CopyFieldAsync;
    }

    public void Initialize(TopLevel topLevel)
    {
        _topLevel = topLevel;
        LoadGitHubSettings();
        _ = TryFillGhTokenAsync();
        var suggested = BilibiliCookieParser.FindDefaultCookieFile();
        if (string.IsNullOrWhiteSpace(suggested))
            return;

        CookiePath = suggested;
        LoadFromPath(suggested);
    }

    private void LoadGitHubSettings()
    {
        var settings = GitHubSettingsStore.Load();
        GitHubRepo = string.IsNullOrWhiteSpace(settings.Repo) ? GitHubRepoSlug.Default : settings.Repo;
        RememberGitHubToken = settings.RememberToken;
        if (settings.RememberToken && !string.IsNullOrWhiteSpace(settings.Token))
            GitHubToken = settings.Token;
    }

    private async Task TryFillGhTokenAsync()
    {
        if (!string.IsNullOrWhiteSpace(GitHubToken))
            return;

        var token = await Task.Run(() => GitHubCli.TryGetToken()).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(GitHubToken) && !string.IsNullOrWhiteSpace(token))
            GitHubToken = token;
    }

    public void LoadFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("請先選擇 cookies.txt。", isError: true);
            return;
        }

        path = path.Trim().Trim('"');
        CookiePath = path;

        try
        {
            var parsed = BilibiliCookieParser.ParseFile(path);
            Apply(parsed);
        }
        catch (Exception ex)
        {
            _cookies = null;
            HasResult = false;
            HasAllThree = false;
            foreach (var field in Fields)
                field.Clear(RevealValues);
            SetStatus(ex.Message, isError: true);
        }
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (_topLevel is null)
            return;

        var files = await _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "選擇 Netscape cookies.txt",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Cookie 文字檔")
                {
                    Patterns = ["*.txt", "cookies.txt"],
                    MimeTypes = ["text/plain"],
                },
                new FilePickerFileType("所有檔案")
                {
                    Patterns = ["*.*"],
                },
            ],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("已取消選擇檔案。", isError: false);
            return;
        }

        LoadFromPath(path);
    }

    [RelayCommand]
    private void Load() => LoadFromPath(CookiePath);

    [RelayCommand]
    private void ToggleReveal() => RevealValues = !RevealValues;

    partial void OnRevealValuesChanged(bool value)
    {
        OnPropertyChanged(nameof(RevealButtonText));
        foreach (var field in Fields)
            field.IsRevealed = value;
    }

    private async Task CopyFieldAsync(CookieFieldViewModel? field)
    {
        if (field is null || !field.HasValue)
        {
            SetStatus("這個欄位沒有值可複製。", isError: true);
            return;
        }

        if (await CopyTextAsync(field.Value))
            SetStatus($"已複製 {field.EnvName}。", isError: false);
    }

    [RelayCommand(CanExecute = nameof(HasResult))]
    private async Task CopyAllEnvAsync()
    {
        if (_cookies is null)
            return;
        if (await CopyTextAsync(_cookies.ToEnvBlock()))
            SetStatus("已複製 SESSDATA / BILI_JCT / DEDEUSERID。", isError: false);
    }

    [RelayCommand(CanExecute = nameof(HasResult))]
    private async Task CopyAllSecretsAsync()
    {
        if (_cookies is null)
            return;
        if (await CopyTextAsync(_cookies.ToGitHubSecretsBlock()))
            SetStatus("已複製 GitHub Secrets 名稱（SESSDATA / BILI_JCT / DEDEUSERID）。", isError: false);
    }

    [RelayCommand(CanExecute = nameof(HasResult))]
    private async Task CopyPowerShellAsync()
    {
        if (_cookies is null)
            return;
        if (await CopyTextAsync(_cookies.ToPowerShellBlock()))
            SetStatus("已複製 PowerShell 環境變數指令。", isError: false);
    }

    [RelayCommand(CanExecute = nameof(HasResult))]
    private async Task CopyBashAsync()
    {
        if (_cookies is null)
            return;
        if (await CopyTextAsync(_cookies.ToBashBlock()))
            SetStatus("已複製 bash export 指令。", isError: false);
    }

    [RelayCommand(CanExecute = nameof(HasResult))]
    private async Task CopyCookieHeaderAsync()
    {
        if (_cookies is null)
            return;
        if (await CopyTextAsync(_cookies.ToCookieHeader()))
            SetStatus("已複製 Cookie 字串。", isError: false);
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyLoginAsync()
    {
        if (_cookies is null || !CanVerify())
            return;

        IsBusy = true;
        SetStatus("正在向 Bilibili 驗證登入…", isError: false);
        try
        {
            var result = await BilibiliNavClient.CheckAsync(_cookies);
            SetStatus(result.Message, isError: !result.Ok);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void UseGhToken()
    {
        var token = GitHubCli.TryGetToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            SetStatus(
                GitHubCli.IsAvailable()
                    ? "gh 已安裝，但尚未登入。請先執行 gh auth login。"
                    : "找不到 GitHub CLI。請安裝 gh 並執行 gh auth login，或手動貼上 PAT。",
                isError: true);
            return;
        }

        GitHubToken = token;
        SetStatus("已填入 gh 目前的登入權杖。", isError: false);
    }

    [RelayCommand(CanExecute = nameof(CanUpdateGitHubSecrets))]
    private async Task UpdateGitHubSecretsAsync()
    {
        if (_cookies is null || !CanUpdateGitHubSecrets())
            return;

        if (!GitHubRepoSlug.TryParse(GitHubRepo, out var repo))
        {
            SetStatus("Repo 格式應為 owner/name，例如 huang1988pioneer/CronBilibiliMission。", isError: true);
            return;
        }

        IsBusy = true;
        SetStatus($"正在更新 {repo.FullName} 的 SESSDATA、BILI_JCT、DEDEUSERID…", isError: false);
        try
        {
            var secrets = GitHubActionsSecretClient.SecretsFromCookies(_cookies);
            var result = await GitHubActionsSecretClient.UpdateWithFallbackAsync(repo, GitHubToken, secrets);
            PersistGitHubSettings();
            ConfirmOverwriteSecrets = false;
            SetStatus(result.Message, isError: !result.Ok);
        }
        catch (Exception ex)
        {
            SetStatus($"更新失敗：{ex.Message}", isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanVerify() => HasAllThree && !IsBusy;

    private bool CanUpdateGitHubSecrets() =>
        HasAllThree
        && !IsBusy
        && ConfirmOverwriteSecrets
        && GitHubRepoSlug.TryParse(GitHubRepo, out _)
        && (!string.IsNullOrWhiteSpace(GitHubToken) || GitHubCli.IsAvailable());

    private void PersistGitHubSettings()
    {
        GitHubSettingsStore.Save(new GitHubSettings
        {
            Repo = GitHubRepo.Trim(),
            Token = GitHubToken,
            RememberToken = RememberGitHubToken,
        });
    }

    private void Apply(BilibiliCookieSet parsed)
    {
        _cookies = parsed;
        HasResult = parsed.HasAny;
        HasAllThree = parsed.HasAll;
        SessData.Apply(parsed.SessData, RevealValues);
        BiliJct.Apply(parsed.BiliJct, RevealValues);
        DedeUserId.Apply(parsed.DedeUserId, RevealValues);

        var found = parsed.Fields.Count(item => item.HasValue);
        var source = string.IsNullOrWhiteSpace(parsed.SourcePath)
            ? "檔案"
            : Path.GetFileName(parsed.SourcePath);
        var message = $"已從 {source} 讀到 {found}/3 個欄位。";
        if (parsed.Warnings.Count > 0)
            message += " " + string.Join(" ", parsed.Warnings);
        SetStatus(message, isError: !parsed.HasAll || parsed.Fields.Any(item => item.IsExpired));
    }

    private async Task<bool> CopyTextAsync(string text)
    {
        var clipboard = _topLevel?.Clipboard;
        if (clipboard is null)
        {
            SetStatus("無法存取剪貼簿。", isError: true);
            return false;
        }

        try
        {
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"複製失敗：{ex.Message}", isError: true);
            return false;
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsError = isError;
    }

    partial void OnIsBusyChanged(bool value)
    {
        VerifyLoginCommand.NotifyCanExecuteChanged();
        UpdateGitHubSecretsCommand.NotifyCanExecuteChanged();
    }
}
