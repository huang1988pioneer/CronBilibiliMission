using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using BilibiliCookieReader.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;

namespace BilibiliCookieReader.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private TopLevel? _topLevel;
    private BilibiliCookieSet? _cookies;
    private readonly DailyHotSearchRecorder _dailyHotSearchRecorder = new();
    private readonly DailyRankingRecorder _dailyRankingRecorder = new();
    private CancellationTokenSource? _backgroundServicesCancellation;

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
    public CookieFieldViewModel Buvid3 { get; } = new("BUVID3", "BUVID3");

    public IReadOnlyList<CookieFieldViewModel> Fields { get; }

    [ObservableProperty]
    public partial string CookiePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "選擇或拖放 Netscape cookies.txt，讀取 SESSDATA、BILI_JCT、DEDEUSERID、BUVID3。";

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
    public partial bool HasCompleteCookies { get; set; }

    [ObservableProperty]
    public partial bool RevealValues { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool HasAccountStatus { get; set; }

    [ObservableProperty]
    public partial string AccountUserName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AccountLevelText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AccountExperienceText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AccountCoinsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasAccountSignature { get; set; }

    [ObservableProperty]
    public partial string AccountSignature { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasAccountAvatar { get; set; }

    [ObservableProperty]
    public partial Bitmap? AccountAvatar { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLatestDynamicCommand))]
    public partial bool HasLatestDynamic { get; set; }

    [ObservableProperty]
    public partial string LatestDynamicType { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestDynamicText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestDynamicPublishedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestDynamicUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasLatestDynamicCover { get; set; }

    [ObservableProperty]
    public partial Bitmap? LatestDynamicCover { get; set; }

    [ObservableProperty]
    public partial bool HasLatestSubmission { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLatestSubmissionCommand))]
    public partial bool HasLatestSubmissionLink { get; set; }

    [ObservableProperty]
    public partial string LatestSubmissionHeading { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestSubmissionTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestSubmissionMetaText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestSubmissionUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasLatestSubmissionCover { get; set; }

    [ObservableProperty]
    public partial Bitmap? LatestSubmissionCover { get; set; }

    [ObservableProperty]
    public partial bool HasFavoriteInfo { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLatestFavoriteCommand))]
    public partial bool HasLatestFavoriteLink { get; set; }

    [ObservableProperty]
    public partial string FavoriteHeading { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FavoriteFolderText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestFavoriteTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestFavoriteMetaText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestFavoriteUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasLatestFavoriteCover { get; set; }

    [ObservableProperty]
    public partial Bitmap? LatestFavoriteCover { get; set; }

    [ObservableProperty]
    public partial bool HasBangumiFollow { get; set; }

    [ObservableProperty]
    public partial bool HasLatestAnime { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLatestAnimeCommand))]
    public partial bool HasLatestAnimeLink { get; set; }

    [ObservableProperty]
    public partial string AnimeHeading { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestAnimeTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestAnimeProgress { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestAnimeUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasLatestAnimeCover { get; set; }

    [ObservableProperty]
    public partial Bitmap? LatestAnimeCover { get; set; }

    [ObservableProperty]
    public partial bool HasLatestDrama { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLatestDramaCommand))]
    public partial bool HasLatestDramaLink { get; set; }

    [ObservableProperty]
    public partial string DramaHeading { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestDramaTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestDramaProgress { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatestDramaUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasLatestDramaCover { get; set; }

    [ObservableProperty]
    public partial Bitmap? LatestDramaCover { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenHomepageRecommendationCommand))]
    public partial bool HasHomepageRecommendation { get; set; }

    [ObservableProperty]
    public partial string HomepageRecommendationTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HomepageRecommendationMetaText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HomepageRecommendationUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasHomepageRecommendationCover { get; set; }

    [ObservableProperty]
    public partial Bitmap? HomepageRecommendationCover { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDynamicFeedCommand))]
    public partial bool HasDynamicFeed { get; set; }

    [ObservableProperty]
    public partial string DynamicFeedHeading { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DynamicFeedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DynamicFeedMetaText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DynamicFeedUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasDynamicFeedCover { get; set; }

    [ObservableProperty]
    public partial Bitmap? DynamicFeedCover { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenBangumiRecommendationCommand))]
    public partial bool HasBangumiRecommendation { get; set; }

    [ObservableProperty]
    public partial string BangumiRecommendationTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BangumiRecommendationSubtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BangumiRecommendationMetaText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BangumiRecommendationUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasBangumiRecommendationCover { get; set; }

    [ObservableProperty]
    public partial Bitmap? BangumiRecommendationCover { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLiveRecommendationCommand))]
    public partial bool HasLiveRecommendation { get; set; }

    [ObservableProperty]
    public partial string LiveRecommendationTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LiveRecommendationMetaText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LiveRecommendationUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasLiveRecommendationCover { get; set; }

    [ObservableProperty]
    public partial Bitmap? LiveRecommendationCover { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenPopularVideoCommand))]
    public partial bool HasPopularVideo { get; set; }

    [ObservableProperty]
    public partial string PopularVideoTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PopularVideoMetaText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PopularVideoUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasPopularVideoCover { get; set; }

    [ObservableProperty]
    public partial Bitmap? PopularVideoCover { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenRankingVideoCommand))]
    public partial bool HasRankingVideo { get; set; }

    [ObservableProperty]
    public partial string RankingVideoHeading { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RankingVideoTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RankingVideoMetaText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RankingVideoUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRankingVideoCover { get; set; }

    [ObservableProperty]
    public partial Bitmap? RankingVideoCover { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenHotSearchCommand))]
    public partial bool HasHotSearch { get; set; }

    [ObservableProperty]
    public partial string HotSearchSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HotSearchTopKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HotSearchUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BackgroundHotSearchStatus { get; set; } = "每日熱搜背景紀錄尚未啟動";

    [ObservableProperty]
    public partial bool IsBackgroundHotSearchError { get; set; }

    [ObservableProperty]
    public partial string BackgroundRankingStatus { get; set; } = "每日排行榜背景紀錄尚未啟動";

    [ObservableProperty]
    public partial bool IsBackgroundRankingError { get; set; }

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
        Fields = [SessData, BiliJct, DedeUserId, Buvid3];
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
        StartBackgroundServices();
    }

    public void StopBackgroundServices()
    {
        _backgroundServicesCancellation?.Cancel();
        _backgroundServicesCancellation?.Dispose();
        _backgroundServicesCancellation = null;
    }

    private void StartBackgroundServices()
    {
        if (_backgroundServicesCancellation is not null)
            return;
        _backgroundServicesCancellation = new CancellationTokenSource();
        _ = RunBackgroundHotSearchAsync(_backgroundServicesCancellation.Token);
        _ = RunBackgroundRankingAsync(_backgroundServicesCancellation.Token);
    }

    private async Task RunBackgroundRankingAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (!cancellationToken.IsCancellationRequested)
        {
            BackgroundRankingStatus = "每日排行榜：背景檢查全部＋20 分類中…";
            IsBackgroundRankingError = false;
            try
            {
                var result = await _dailyRankingRecorder.RecordTodayAsync(
                    cancellationToken: cancellationToken);
                var action = result.Changed ? "已更新" : "今日已完成";
                var completion = result.Complete ? "完整" : "部分完成，1 小時後補抓";
                BackgroundRankingStatus =
                    $"每日排行榜：{action} {result.CategoryCount}/{result.ExpectedCategoryCount} 類（{completion}） · {result.CapturedAtTaipei:yyyy-MM-dd HH:mm} 台北 · {result.LogPath}";
                IsBackgroundRankingError = !result.Complete;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or UnauthorizedAccessException)
            {
                IsBackgroundRankingError = true;
                BackgroundRankingStatus = $"每日排行榜：背景紀錄失敗，1 小時後重試 · {ex.Message}";
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                    break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunBackgroundHotSearchAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (!cancellationToken.IsCancellationRequested)
        {
            BackgroundHotSearchStatus = "每日熱搜：背景檢查中…";
            IsBackgroundHotSearchError = false;
            try
            {
                var result = await _dailyHotSearchRecorder.RecordTodayAsync(
                    cancellationToken: cancellationToken);
                var action = result.Recorded ? "已記錄" : "今日已記錄";
                BackgroundHotSearchStatus =
                    $"每日熱搜：{action} {result.EntryCount} 筆 · {result.CapturedAtTaipei:yyyy-MM-dd HH:mm} 台北 · {_dailyHotSearchRecorder.LogPath}";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or UnauthorizedAccessException)
            {
                IsBackgroundHotSearchError = true;
                BackgroundHotSearchStatus = $"每日熱搜：背景紀錄失敗，1 小時後重試 · {ex.Message}";
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                    break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
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
        ClearAccountStatus();
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
            HasCompleteCookies = false;
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
            SetStatus("已複製 SESSDATA / BILI_JCT / DEDEUSERID / BUVID3。", isError: false);
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
        ClearAccountStatus();
        SetStatus("正在向 Bilibili 驗證登入…", isError: false);
        try
        {
            var result = await BilibiliNavClient.CheckAsync(_cookies);
            if (result.Ok)
                ApplyAccountStatus(result);
            SetStatus(result.Message, isError: !result.Ok);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasLatestDynamic))]
    private async Task OpenLatestDynamicAsync()
    {
        if (_topLevel is null
            || !Uri.TryCreate(LatestDynamicUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        await _topLevel.Launcher.LaunchUriAsync(uri);
    }

    [RelayCommand(CanExecute = nameof(HasLatestSubmissionLink))]
    private async Task OpenLatestSubmissionAsync()
    {
        if (_topLevel is null
            || !Uri.TryCreate(LatestSubmissionUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        await _topLevel.Launcher.LaunchUriAsync(uri);
    }

    [RelayCommand(CanExecute = nameof(HasLatestFavoriteLink))]
    private async Task OpenLatestFavoriteAsync()
    {
        if (_topLevel is null
            || !Uri.TryCreate(LatestFavoriteUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        await _topLevel.Launcher.LaunchUriAsync(uri);
    }

    [RelayCommand(CanExecute = nameof(HasLatestAnimeLink))]
    private async Task OpenLatestAnimeAsync()
    {
        if (_topLevel is null
            || !Uri.TryCreate(LatestAnimeUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        await _topLevel.Launcher.LaunchUriAsync(uri);
    }

    [RelayCommand(CanExecute = nameof(HasLatestDramaLink))]
    private async Task OpenLatestDramaAsync()
    {
        if (_topLevel is null
            || !Uri.TryCreate(LatestDramaUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        await _topLevel.Launcher.LaunchUriAsync(uri);
    }

    [RelayCommand(CanExecute = nameof(HasHomepageRecommendation))]
    private async Task OpenHomepageRecommendationAsync()
    {
        if (_topLevel is null
            || !Uri.TryCreate(HomepageRecommendationUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        await _topLevel.Launcher.LaunchUriAsync(uri);
    }

    [RelayCommand(CanExecute = nameof(HasDynamicFeed))]
    private async Task OpenDynamicFeedAsync()
    {
        if (_topLevel is null
            || !Uri.TryCreate(DynamicFeedUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        await _topLevel.Launcher.LaunchUriAsync(uri);
    }

    [RelayCommand(CanExecute = nameof(HasBangumiRecommendation))]
    private async Task OpenBangumiRecommendationAsync()
    {
        if (_topLevel is null
            || !Uri.TryCreate(BangumiRecommendationUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        await _topLevel.Launcher.LaunchUriAsync(uri);
    }

    [RelayCommand(CanExecute = nameof(HasLiveRecommendation))]
    private async Task OpenLiveRecommendationAsync()
    {
        if (_topLevel is null
            || !Uri.TryCreate(LiveRecommendationUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        await _topLevel.Launcher.LaunchUriAsync(uri);
    }

    [RelayCommand(CanExecute = nameof(HasPopularVideo))]
    private async Task OpenPopularVideoAsync()
    {
        if (_topLevel is null
            || !Uri.TryCreate(PopularVideoUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        await _topLevel.Launcher.LaunchUriAsync(uri);
    }

    [RelayCommand(CanExecute = nameof(HasRankingVideo))]
    private async Task OpenRankingVideoAsync()
    {
        if (_topLevel is null
            || !Uri.TryCreate(RankingVideoUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        await _topLevel.Launcher.LaunchUriAsync(uri);
    }

    [RelayCommand(CanExecute = nameof(HasHotSearch))]
    private async Task OpenHotSearchAsync()
    {
        if (_topLevel is null
            || !Uri.TryCreate(HotSearchUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        await _topLevel.Launcher.LaunchUriAsync(uri);
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

    private bool CanVerify() => HasCompleteCookies && !IsBusy;

    private bool CanUpdateGitHubSecrets() =>
        HasCompleteCookies
        && !IsBusy
        && ConfirmOverwriteSecrets
        && GitHubSecretPublisher.CanPublish(GitHubRepo, GitHubToken);

    private void Apply(CookieSession session)
    {
        ClearAccountStatus();
        _cookies = session.Cookies;
        HasResult = session.HasAny;
        HasCompleteCookies = session.HasAll;
        SessData.Apply(session.Cookies.SessData, RevealValues);
        BiliJct.Apply(session.Cookies.BiliJct, RevealValues);
        DedeUserId.Apply(session.Cookies.DedeUserId, RevealValues);
        Buvid3.Apply(session.Cookies.Buvid3, RevealValues);
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

    private void ApplyAccountStatus(NavCheckResult result)
    {
        AccountUserName = result.UserName ?? SelectedAccount.UserName;
        AccountLevelText = result.Level is null ? "—" : $"Lv.{result.Level}";
        AccountExperienceText = FormatExperience(result);
        AccountCoinsText = result.Coins is null ? "—" : $"{result.Coins:N1}";
        AccountSignature = result.ProfileSignature ?? string.Empty;
        HasAccountSignature = !string.IsNullOrWhiteSpace(AccountSignature);
        SetAccountAvatar(result.ProfileImage);
        SetLatestDynamic(result.LatestDynamic);
        SetLatestSubmission(result.LatestSubmission);
        SetFavoriteInfo(result.Favorite);
        SetBangumiFollow(result.BangumiFollow);
        SetHomepageRecommendation(result.HomepageRecommendation);
        SetDynamicFeed(result.DynamicFeed);
        SetBangumiRecommendation(result.BangumiRecommendation);
        SetLiveRecommendation(result.LiveRecommendation);
        SetPopularVideo(result.PopularVideo);
        SetRankingVideo(result.RankingVideo);
        SetHotSearch(result.HotSearch);
        HasAccountStatus = true;
    }

    private static string FormatExperience(NavCheckResult result)
    {
        if (result.CurrentExperience is null)
            return "—";
        if (result.Level >= 6)
            return $"{result.CurrentExperience:N0}（已達 Lv.6）";
        if (result.NextExperience is null)
            return $"{result.CurrentExperience:N0}";

        var remaining = Math.Max(0, result.NextExperience.Value - result.CurrentExperience.Value);
        return $"{result.CurrentExperience:N0} / {result.NextExperience:N0}（還差 {remaining:N0}）";
    }

    private void ClearAccountStatus()
    {
        HasAccountStatus = false;
        AccountUserName = string.Empty;
        AccountLevelText = string.Empty;
        AccountExperienceText = string.Empty;
        AccountCoinsText = string.Empty;
        HasAccountSignature = false;
        AccountSignature = string.Empty;
        AccountAvatar?.Dispose();
        AccountAvatar = null;
        HasAccountAvatar = false;
        HasLatestDynamic = false;
        LatestDynamicType = string.Empty;
        LatestDynamicText = string.Empty;
        LatestDynamicPublishedText = string.Empty;
        LatestDynamicUrl = string.Empty;
        LatestDynamicCover?.Dispose();
        LatestDynamicCover = null;
        HasLatestDynamicCover = false;
        HasLatestSubmission = false;
        HasLatestSubmissionLink = false;
        LatestSubmissionHeading = string.Empty;
        LatestSubmissionTitle = string.Empty;
        LatestSubmissionMetaText = string.Empty;
        LatestSubmissionUrl = string.Empty;
        LatestSubmissionCover?.Dispose();
        LatestSubmissionCover = null;
        HasLatestSubmissionCover = false;
        HasFavoriteInfo = false;
        HasLatestFavoriteLink = false;
        FavoriteHeading = string.Empty;
        FavoriteFolderText = string.Empty;
        LatestFavoriteTitle = string.Empty;
        LatestFavoriteMetaText = string.Empty;
        LatestFavoriteUrl = string.Empty;
        LatestFavoriteCover?.Dispose();
        LatestFavoriteCover = null;
        HasLatestFavoriteCover = false;
        HasBangumiFollow = false;
        HasLatestAnime = false;
        HasLatestAnimeLink = false;
        AnimeHeading = string.Empty;
        LatestAnimeTitle = string.Empty;
        LatestAnimeProgress = string.Empty;
        LatestAnimeUrl = string.Empty;
        LatestAnimeCover?.Dispose();
        LatestAnimeCover = null;
        HasLatestAnimeCover = false;
        HasLatestDrama = false;
        HasLatestDramaLink = false;
        DramaHeading = string.Empty;
        LatestDramaTitle = string.Empty;
        LatestDramaProgress = string.Empty;
        LatestDramaUrl = string.Empty;
        LatestDramaCover?.Dispose();
        LatestDramaCover = null;
        HasLatestDramaCover = false;
        HasHomepageRecommendation = false;
        HomepageRecommendationTitle = string.Empty;
        HomepageRecommendationMetaText = string.Empty;
        HomepageRecommendationUrl = string.Empty;
        HomepageRecommendationCover?.Dispose();
        HomepageRecommendationCover = null;
        HasHomepageRecommendationCover = false;
        HasDynamicFeed = false;
        DynamicFeedHeading = string.Empty;
        DynamicFeedText = string.Empty;
        DynamicFeedMetaText = string.Empty;
        DynamicFeedUrl = string.Empty;
        DynamicFeedCover?.Dispose();
        DynamicFeedCover = null;
        HasDynamicFeedCover = false;
        HasBangumiRecommendation = false;
        BangumiRecommendationTitle = string.Empty;
        BangumiRecommendationSubtitle = string.Empty;
        BangumiRecommendationMetaText = string.Empty;
        BangumiRecommendationUrl = string.Empty;
        BangumiRecommendationCover?.Dispose();
        BangumiRecommendationCover = null;
        HasBangumiRecommendationCover = false;
        HasLiveRecommendation = false;
        LiveRecommendationTitle = string.Empty;
        LiveRecommendationMetaText = string.Empty;
        LiveRecommendationUrl = string.Empty;
        LiveRecommendationCover?.Dispose();
        LiveRecommendationCover = null;
        HasLiveRecommendationCover = false;
        HasPopularVideo = false;
        PopularVideoTitle = string.Empty;
        PopularVideoMetaText = string.Empty;
        PopularVideoUrl = string.Empty;
        PopularVideoCover?.Dispose();
        PopularVideoCover = null;
        HasPopularVideoCover = false;
        HasRankingVideo = false;
        RankingVideoHeading = string.Empty;
        RankingVideoTitle = string.Empty;
        RankingVideoMetaText = string.Empty;
        RankingVideoUrl = string.Empty;
        RankingVideoCover?.Dispose();
        RankingVideoCover = null;
        HasRankingVideoCover = false;
        HasHotSearch = false;
        HotSearchSummary = string.Empty;
        HotSearchTopKeyword = string.Empty;
        HotSearchUrl = string.Empty;
    }

    private void SetAccountAvatar(byte[]? image)
    {
        AccountAvatar?.Dispose();
        AccountAvatar = null;
        HasAccountAvatar = false;
        if (image is null || image.Length == 0)
            return;

        try
        {
            using var stream = new MemoryStream(image, writable: false);
            AccountAvatar = new Bitmap(stream);
            HasAccountAvatar = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            AccountAvatar = null;
        }
    }

    private void SetLatestDynamic(LatestDynamicInfo? dynamic)
    {
        LatestDynamicCover?.Dispose();
        LatestDynamicCover = null;
        HasLatestDynamicCover = false;
        HasLatestDynamic = dynamic is not null;
        LatestDynamicType = dynamic?.Type ?? string.Empty;
        LatestDynamicText = dynamic?.Text ?? string.Empty;
        LatestDynamicPublishedText = dynamic?.PublishedAt is null
            ? string.Empty
            : dynamic.PublishedAt.Value
                .ToOffset(TimeSpan.FromHours(8))
                .ToString("yyyy-MM-dd HH:mm");
        LatestDynamicUrl = dynamic?.Url ?? string.Empty;

        if (dynamic?.CoverImage is null || dynamic.CoverImage.Length == 0)
            return;

        try
        {
            using var stream = new MemoryStream(dynamic.CoverImage, writable: false);
            LatestDynamicCover = new Bitmap(stream);
            HasLatestDynamicCover = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            LatestDynamicCover = null;
        }
    }

    private void SetLatestSubmission(LatestSubmissionInfo? submission)
    {
        LatestSubmissionCover?.Dispose();
        LatestSubmissionCover = null;
        HasLatestSubmissionCover = false;
        HasLatestSubmission = submission is not null;
        LatestSubmissionHeading = submission?.TotalCount is null
            ? "最新投稿"
            : $"投稿 {submission.TotalCount:N0}";
        LatestSubmissionTitle = submission?.Title ?? "尚無影片投稿";
        var metadata = new List<string>();
        if (submission?.PublishedAt is not null)
        {
            metadata.Add(submission.PublishedAt.Value
                .ToOffset(TimeSpan.FromHours(8))
                .ToString("yyyy-MM-dd HH:mm"));
        }
        if (!string.IsNullOrWhiteSpace(submission?.PlayCount))
            metadata.Add($"播放 {submission.PlayCount}");
        LatestSubmissionMetaText = string.Join(" · ", metadata);
        LatestSubmissionUrl = submission?.Url ?? string.Empty;
        HasLatestSubmissionLink = !string.IsNullOrWhiteSpace(LatestSubmissionUrl);

        if (submission?.CoverImage is null || submission.CoverImage.Length == 0)
            return;

        try
        {
            using var stream = new MemoryStream(submission.CoverImage, writable: false);
            LatestSubmissionCover = new Bitmap(stream);
            HasLatestSubmissionCover = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            LatestSubmissionCover = null;
        }
    }

    private void SetFavoriteInfo(FavoriteInfo? favorite)
    {
        LatestFavoriteCover?.Dispose();
        LatestFavoriteCover = null;
        HasLatestFavoriteCover = false;
        HasFavoriteInfo = favorite is not null;
        FavoriteHeading = favorite is null ? string.Empty : $"收藏 {favorite.FolderCount:N0}";
        FavoriteFolderText = favorite is null
            ? string.Empty
            : $"{favorite.FolderTitle} · {favorite.ItemCount:N0} 部影片";
        LatestFavoriteTitle = favorite?.LatestTitle ?? "收藏夾內尚無影片";
        LatestFavoriteMetaText = favorite?.FavoritedAt is null
            ? string.Empty
            : $"收藏於 {favorite.FavoritedAt.Value.ToOffset(TimeSpan.FromHours(8)):yyyy-MM-dd HH:mm}";
        LatestFavoriteUrl = favorite?.Url ?? string.Empty;
        HasLatestFavoriteLink = !string.IsNullOrWhiteSpace(LatestFavoriteUrl);

        if (favorite?.CoverImage is null || favorite.CoverImage.Length == 0)
            return;

        try
        {
            using var stream = new MemoryStream(favorite.CoverImage, writable: false);
            LatestFavoriteCover = new Bitmap(stream);
            HasLatestFavoriteCover = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            LatestFavoriteCover = null;
        }
    }

    private void SetBangumiFollow(BangumiFollowInfo? follow)
    {
        LatestAnimeCover?.Dispose();
        LatestAnimeCover = null;
        HasLatestAnimeCover = false;
        LatestDramaCover?.Dispose();
        LatestDramaCover = null;
        HasLatestDramaCover = false;
        HasBangumiFollow = follow is not null;

        SetFollowedSeason(
            follow?.LatestAnime,
            follow?.AnimeCount,
            "追番",
            value => HasLatestAnime = value,
            value => HasLatestAnimeLink = value,
            value => AnimeHeading = value,
            value => LatestAnimeTitle = value,
            value => LatestAnimeProgress = value,
            value => LatestAnimeUrl = value,
            value => LatestAnimeCover = value,
            value => HasLatestAnimeCover = value);
        SetFollowedSeason(
            follow?.LatestDrama,
            follow?.DramaCount,
            "追劇",
            value => HasLatestDrama = value,
            value => HasLatestDramaLink = value,
            value => DramaHeading = value,
            value => LatestDramaTitle = value,
            value => LatestDramaProgress = value,
            value => LatestDramaUrl = value,
            value => LatestDramaCover = value,
            value => HasLatestDramaCover = value);
    }

    private static void SetFollowedSeason(
        FollowedSeasonInfo? season,
        int? count,
        string label,
        Action<bool> setVisible,
        Action<bool> setLink,
        Action<string> setHeading,
        Action<string> setTitle,
        Action<string> setProgress,
        Action<string> setUrl,
        Action<Bitmap?> setCover,
        Action<bool> setHasCover)
    {
        setVisible(count is not null);
        setHeading(count is null ? label : $"{label} {count:N0}");
        setTitle(season?.Title ?? "尚無追蹤作品");
        setProgress(season?.Progress ?? string.Empty);
        setUrl(season?.Url ?? string.Empty);
        setLink(!string.IsNullOrWhiteSpace(season?.Url));
        if (season?.CoverImage is null || season.CoverImage.Length == 0)
            return;

        try
        {
            using var stream = new MemoryStream(season.CoverImage, writable: false);
            setCover(new Bitmap(stream));
            setHasCover(true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            setCover(null);
        }
    }

    private void SetHomepageRecommendation(HomepageRecommendationInfo? recommendation)
    {
        HomepageRecommendationCover?.Dispose();
        HomepageRecommendationCover = null;
        HasHomepageRecommendationCover = false;
        HasHomepageRecommendation = recommendation is not null;
        HomepageRecommendationTitle = recommendation?.Title ?? string.Empty;
        HomepageRecommendationMetaText = recommendation is null
            ? string.Empty
            : string.Join(
                " · ",
                new[] { recommendation.Uploader, recommendation.Statistics }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        HomepageRecommendationUrl = recommendation?.Url ?? string.Empty;

        if (recommendation?.CoverImage is null || recommendation.CoverImage.Length == 0)
            return;

        try
        {
            using var stream = new MemoryStream(recommendation.CoverImage, writable: false);
            HomepageRecommendationCover = new Bitmap(stream);
            HasHomepageRecommendationCover = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            HomepageRecommendationCover = null;
        }
    }

    private void SetDynamicFeed(DynamicFeedInfo? feed)
    {
        DynamicFeedCover?.Dispose();
        DynamicFeedCover = null;
        HasDynamicFeedCover = false;
        HasDynamicFeed = feed is not null;
        DynamicFeedHeading = feed is null
            ? string.Empty
            : string.Join(
                " · ",
                new[] { feed.Author, feed.Type }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        DynamicFeedText = feed?.Text ?? string.Empty;
        DynamicFeedMetaText = feed?.PublishedAt is null
            ? string.Empty
            : feed.PublishedAt.Value.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm");
        DynamicFeedUrl = feed?.Url ?? string.Empty;

        if (feed?.CoverImage is null || feed.CoverImage.Length == 0)
            return;

        try
        {
            using var stream = new MemoryStream(feed.CoverImage, writable: false);
            DynamicFeedCover = new Bitmap(stream);
            HasDynamicFeedCover = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            DynamicFeedCover = null;
        }
    }

    private void SetBangumiRecommendation(BangumiRecommendationInfo? recommendation)
    {
        BangumiRecommendationCover?.Dispose();
        BangumiRecommendationCover = null;
        HasBangumiRecommendationCover = false;
        HasBangumiRecommendation = recommendation is not null;
        BangumiRecommendationTitle = recommendation?.Title ?? string.Empty;
        BangumiRecommendationSubtitle = recommendation?.Subtitle ?? string.Empty;
        BangumiRecommendationMetaText = recommendation is null
            ? string.Empty
            : string.Join(
                " · ",
                new[] { recommendation.Progress, recommendation.Rating, recommendation.Badge }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        BangumiRecommendationUrl = recommendation?.Url ?? string.Empty;

        if (recommendation?.CoverImage is null || recommendation.CoverImage.Length == 0)
            return;

        try
        {
            using var stream = new MemoryStream(recommendation.CoverImage, writable: false);
            BangumiRecommendationCover = new Bitmap(stream);
            HasBangumiRecommendationCover = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            BangumiRecommendationCover = null;
        }
    }

    private void SetLiveRecommendation(LiveRecommendationInfo? recommendation)
    {
        LiveRecommendationCover?.Dispose();
        LiveRecommendationCover = null;
        HasLiveRecommendationCover = false;
        HasLiveRecommendation = recommendation is not null;
        LiveRecommendationTitle = recommendation?.Title ?? string.Empty;
        LiveRecommendationMetaText = recommendation is null
            ? string.Empty
            : string.Join(
                " · ",
                new[]
                {
                    recommendation.Uploader,
                    recommendation.Area,
                    recommendation.Online is null ? string.Empty : $"人氣 {recommendation.Online:N0}",
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
        LiveRecommendationUrl = recommendation?.Url ?? string.Empty;

        if (recommendation?.CoverImage is null || recommendation.CoverImage.Length == 0)
            return;

        try
        {
            using var stream = new MemoryStream(recommendation.CoverImage, writable: false);
            LiveRecommendationCover = new Bitmap(stream);
            HasLiveRecommendationCover = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            LiveRecommendationCover = null;
        }
    }

    private void SetPopularVideo(PopularVideoInfo? popular)
    {
        PopularVideoCover?.Dispose();
        PopularVideoCover = null;
        HasPopularVideoCover = false;
        HasPopularVideo = popular is not null;
        PopularVideoTitle = popular?.Title ?? string.Empty;
        PopularVideoMetaText = popular is null
            ? string.Empty
            : string.Join(
                " · ",
                new[]
                {
                    popular.Uploader,
                    popular.Views is null ? string.Empty : $"播放 {popular.Views:N0}",
                    popular.Danmaku is null ? string.Empty : $"彈幕 {popular.Danmaku:N0}",
                    popular.Reason,
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
        PopularVideoUrl = popular?.Url ?? string.Empty;

        if (popular?.CoverImage is null || popular.CoverImage.Length == 0)
            return;

        try
        {
            using var stream = new MemoryStream(popular.CoverImage, writable: false);
            PopularVideoCover = new Bitmap(stream);
            HasPopularVideoCover = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            PopularVideoCover = null;
        }
    }

    private void SetRankingVideo(RankingVideoInfo? ranking)
    {
        RankingVideoCover?.Dispose();
        RankingVideoCover = null;
        HasRankingVideoCover = false;
        HasRankingVideo = ranking is not null;
        RankingVideoHeading = ranking is null ? string.Empty : $"全站排行榜 · 第 {ranking.Position} 名";
        RankingVideoTitle = ranking?.Title ?? string.Empty;
        RankingVideoMetaText = ranking is null
            ? string.Empty
            : string.Join(
                " · ",
                new[]
                {
                    ranking.Uploader,
                    ranking.Views is null ? string.Empty : $"播放 {ranking.Views:N0}",
                    ranking.Danmaku is null ? string.Empty : $"彈幕 {ranking.Danmaku:N0}",
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
        RankingVideoUrl = ranking?.Url ?? string.Empty;

        if (ranking?.CoverImage is null || ranking.CoverImage.Length == 0)
            return;

        try
        {
            using var stream = new MemoryStream(ranking.CoverImage, writable: false);
            RankingVideoCover = new Bitmap(stream);
            HasRankingVideoCover = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            RankingVideoCover = null;
        }
    }

    private void SetHotSearch(HotSearchInfo? hotSearch)
    {
        HasHotSearch = hotSearch is not null;
        HotSearchSummary = hotSearch?.Summary ?? string.Empty;
        HotSearchTopKeyword = hotSearch?.TopKeyword ?? string.Empty;
        HotSearchUrl = hotSearch?.Url ?? string.Empty;
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
        HasCompleteCookies = false;
        ConfirmOverwriteSecrets = false;
        foreach (var field in Fields)
            field.Clear(RevealValues);
        ClearAccountStatus();
        ClearExpiryReminder();
        SetStatus($"已切換至 {value.DisplayName}，請選擇該帳號的 cookies.txt。", isError: false);
    }
}
