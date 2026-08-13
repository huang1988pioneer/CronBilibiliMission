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

    public IReadOnlyList<BilibiliAccountOption> Accounts { get; } =
    [
        new(1, "huang1988pioneer"),
        new(2, "abuhg17"),
        new(3, "goldshoot0720"),
    ];

    [ObservableProperty]
    public partial BilibiliAccountOption SelectedAccount { get; set; }

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

    [ObservableProperty]
    public partial bool HasExpiryReminder { get; set; }

    [ObservableProperty]
    public partial string ExpiryTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ExpiryDetail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsExpiryOk { get; set; }

    [ObservableProperty]
    public partial bool IsExpirySoon { get; set; }

    [ObservableProperty]
    public partial bool IsExpiryUrgent { get; set; }

    [ObservableProperty]
    public partial bool IsExpiryExpired { get; set; }

    public string RevealButtonText => RevealValues ? "隱藏明文" : "顯示明文";

    public string SelectedSecretNames => string.Join(
        "、",
        GitHubActionsSecretClient.ActionSecretNames.Select(name => name + SelectedAccount.SecretSuffix));

    public string WindowTitle => HasExpiryReminder && !string.IsNullOrWhiteSpace(ExpiryTitle)
        ? "Bilibili Cookie 讀取器 · 到期提醒"
        : "Bilibili Cookie 讀取器";

    public MainViewModel()
    {
        Fields = [SessData, BiliJct, DedeUserId];
        foreach (var field in Fields)
            field.CopyRequested = CopyFieldAsync;
        SelectedAccount = Accounts[0];
    }

    public void Initialize(TopLevel topLevel)
    {
        _topLevel = topLevel;
        LoadGitHubSettings();
        ShowSavedExpiryReminder();
        _ = TryFillGhTokenAsync();
    }

    private void LoadGitHubSettings()
    {
        var settings = GitHubSecretPublisher.LoadPreferences();
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
            Apply(CookieSession.OpenFile(path));
        }
        catch (Exception ex)
        {
            _cookies = null;
            HasResult = false;
            HasAllThree = false;
            foreach (var item in Fields)
                item.Clear(RevealValues);
            ShowSavedExpiryReminder();
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
        var secrets = GitHubActionsSecretClient.SecretsFromCookies(_cookies, SelectedAccount.SecretSuffix);
        if (await CopyTextAsync(string.Join(Environment.NewLine, secrets.Select(item => $"{item.Name}={item.Value}"))))
            SetStatus($"已複製 {SelectedAccount.DisplayName} 的 GitHub Secrets。", isError: false);
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
        var token = GitHubSecretPublisher.TryResolveToken(null);
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

        IsBusy = true;
        SetStatus($"正在更新 {SelectedAccount.DisplayName} 的 GitHub Actions Secrets…", isError: false);
        try
        {
            var result = await GitHubSecretPublisher.PublishAsync(
                GitHubRepo,
                GitHubToken,
                _cookies,
                SelectedAccount.SecretSuffix);
            GitHubSecretPublisher.SavePreferences(GitHubRepo, GitHubToken, RememberGitHubToken);
            ConfirmOverwriteSecrets = false;
            var message = result.Message;
            if (result.Ok && HasExpiryReminder)
                message += " " + ExpiryTitle;
            SetStatus(message, isError: !result.Ok);
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
        && GitHubSecretPublisher.CanPublish(GitHubRepo, GitHubToken);

    private void Apply(CookieSession session)
    {
        _cookies = session.Cookies;
        HasResult = session.HasAny;
        HasAllThree = session.HasAll;
        SessData.Apply(session.Cookies.SessData, RevealValues);
        BiliJct.Apply(session.Cookies.BiliJct, RevealValues);
        DedeUserId.Apply(session.Cookies.DedeUserId, RevealValues);
        ApplyExpiryReminder(session.Reminder);
        if (session.HasAny)
            GitHubSecretPublisher.RememberExpiry(session.Reminder);
        SetStatus(session.Status, isError: session.IsAlarming);
    }

    private void ShowSavedExpiryReminder()
    {
        var settings = GitHubSecretPublisher.LoadPreferences();
        var saved = CookieSession.FromSavedExpiry(settings.LastCookieExpiresAt, settings.LastSessionExpiresAt);
        if (saved is null)
        {
            ClearExpiryReminder();
            return;
        }

        ApplyExpiryReminder(saved.Reminder);
    }

    private void ApplyExpiryReminder(CookieExpiryReminder reminder)
    {
        HasExpiryReminder = reminder.HasDate;
        ExpiryTitle = reminder.Title;
        ExpiryDetail = reminder.Detail;
        IsExpiryOk = reminder.Urgency == ExpiryUrgency.Ok;
        IsExpirySoon = reminder.Urgency == ExpiryUrgency.Soon;
        IsExpiryUrgent = reminder.Urgency == ExpiryUrgency.Urgent;
        IsExpiryExpired = reminder.Urgency == ExpiryUrgency.Expired;
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void ClearExpiryReminder()
    {
        HasExpiryReminder = false;
        ExpiryTitle = string.Empty;
        ExpiryDetail = string.Empty;
        IsExpiryOk = false;
        IsExpirySoon = false;
        IsExpiryUrgent = false;
        IsExpiryExpired = false;
        OnPropertyChanged(nameof(WindowTitle));
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

    partial void OnSelectedAccountChanged(BilibiliAccountOption value)
    {
        OnPropertyChanged(nameof(SelectedSecretNames));
        CookiePath = string.Empty;
        _cookies = null;
        HasResult = false;
        HasAllThree = false;
        ConfirmOverwriteSecrets = false;
        foreach (var field in Fields)
            field.Clear(RevealValues);
        ClearExpiryReminder();
        SetStatus($"已切換至 {value.DisplayName}，請選擇該帳號的 cookies.txt。", isError: false);
    }
}
