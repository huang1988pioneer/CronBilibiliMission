using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BilibiliCookieReader.Services;
using BilibiliCookieReader.ViewModels;
using BilibiliCookieReader.Views;
using Xunit;

namespace BilibiliCookieReader.Tests;

public sealed class MainWindowLayoutTests
{
    [Theory]
    [InlineData(960, 640, false)]
    [InlineData(1080, 740, false)]
    [InlineData(1180, 860, true)]
    [InlineData(1440, 900, true)]
    public async Task Loaded_fields_do_not_overlap_and_actions_remain_visible(int width, int height, bool reveal)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(LayoutTestApp));
        await session.Dispatch(() =>
        {
            var vm = CreateLoadedModel(reveal);
            var window = OpenWindow(vm, width, height);
            try
            {
                var fields = window.FindControl<ItemsControl>("CookieFields")!;
                var rows = fields.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Classes.Contains("row")).ToArray();
                Assert.Equal(4, rows.Length);
                Rect? previous = null;
                foreach (var row in rows)
                {
                    var field = Assert.IsType<CookieFieldViewModel>(row.DataContext);
                    var name = row.GetVisualDescendants().OfType<TextBlock>()
                        .Single(text => text.Classes.Contains("fieldname"));
                    var value = Assert.Single(row.GetVisualDescendants().OfType<TextBox>());
                    var meta = row.GetVisualDescendants().OfType<TextBlock>()
                        .Single(text => text.Text == field.MetaText);
                    var nameBounds = RelativeBounds(name, window);
                    var valueBounds = RelativeBounds(value, window);
                    var metaBounds = RelativeBounds(meta, window);
                    Assert.True(valueBounds.Top >= nameBounds.Bottom + 7);
                    Assert.True(metaBounds.Top >= valueBounds.Bottom + 7);
                    Assert.True(meta.Bounds.Height >= meta.DesiredSize.Height - 1);
                    Assert.True(value.Bounds.Width > 200);
                    var rowBounds = RelativeBounds(row, window);
                    Assert.True(rowBounds.Bottom >= metaBounds.Bottom + 10);
                    if (previous is { } before)
                        Assert.True(rowBounds.Top >= before.Bottom + 11);
                    previous = rowBounds;
                }

                var scroll = window.FindControl<ScrollViewer>("ResultsScroll")!;
                var actions = window.FindControl<Border>("ActionBar")!;
                Assert.True(RelativeBounds(scroll, window).Bottom <= RelativeBounds(actions, window).Top);
                Assert.True(RelativeBounds(actions, window).Bottom <= window.ClientSize.Height);
                Assert.True(scroll.Extent.Height > 0);
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                window.UpdateLayout();
                Assert.True(RelativeBounds(rows[^1], window).Bottom <= RelativeBounds(scroll, window).Bottom + 1);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Very_long_revealed_values_and_metadata_grow_without_clipping()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(LayoutTestApp));
        await session.Dispatch(() =>
        {
            var vm = CreateLoadedModel(reveal: true);
            vm.SessData.Value = new string('a', 900) + "%2Ctest";
            vm.SessData.MetaText = string.Concat(Enumerable.Repeat(".bilibili.com · 到期時間與工作階段資訊 ", 12));
            var window = OpenWindow(vm, 960, 640);
            try
            {
                var row = window.FindControl<ItemsControl>("CookieFields")!.GetVisualDescendants()
                    .OfType<Border>().Single(b => b.Classes.Contains("row") && b.DataContext == vm.SessData);
                var value = Assert.Single(row.GetVisualDescendants().OfType<TextBox>());
                var meta = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == vm.SessData.MetaText);
                Assert.True(value.Bounds.Height > 60);
                Assert.True(meta.Bounds.Height > 40);
                Assert.True(RelativeBounds(meta, window).Top >= RelativeBounds(value, window).Bottom + 7);
                Assert.True(RelativeBounds(row, window).Bottom >= RelativeBounds(meta, window).Bottom + 10);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Expanded_account_information_flows_below_fields_and_keeps_full_descriptions()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(LayoutTestApp));
        await session.Dispatch(() =>
        {
            var vm = CreateLoadedModel(reveal: false);
            vm.HasAccountStatus = true;
            vm.AccountUserName = "測試使用者";
            vm.AccountLevelText = "Lv.6";
            vm.AccountExperienceText = "28800 / 28800";
            vm.AccountCoinsText = "1000";
            vm.HasAccountSignature = true;
            vm.AccountSignature = string.Concat(Enumerable.Repeat("完整個人簡介應換行顯示。", 20));
            vm.HasLatestDynamic = true;
            vm.LatestDynamicText = string.Concat(Enumerable.Repeat("最新動態的長篇內容不應截斷。", 30));
            vm.HasLatestSubmission = true;
            vm.LatestSubmissionTitle = "最新投稿";
            vm.HasBangumiFollow = true;
            vm.HasLatestAnime = true;
            vm.HasLatestDrama = true;
            vm.LatestAnimeTitle = "追番";
            vm.LatestDramaTitle = "追劇";
            var window = OpenWindow(vm, 960, 640);
            try
            {
                var section = window.FindControl<Expander>("AccountSection")!;
                section.IsExpanded = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var fields = window.FindControl<ItemsControl>("CookieFields")!;
                Assert.True(RelativeBounds(section, window).Top >= RelativeBounds(fields, window).Bottom);
                var account = Assert.IsType<Border>(section.Content);
                var stack = Assert.IsType<StackPanel>(account.Child);
                Control? previous = null;
                foreach (var child in stack.Children.Where(child => child.IsVisible))
                {
                    Assert.True(child.Bounds.Height > 0);
                    if (previous is not null)
                        Assert.True(child.Bounds.Top >= previous.Bounds.Bottom + 15);
                    previous = child;
                }
                var dynamic = account.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == vm.LatestDynamicText);
                Assert.True(dynamic.Bounds.Height > 100);
                Assert.True(double.IsPositiveInfinity(dynamic.MaxHeight));
                Assert.True(dynamic.Bounds.Height >= dynamic.DesiredSize.Height - 1);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Empty_state_hides_result_fields_and_expanded_settings_can_scroll()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(LayoutTestApp));
        await session.Dispatch(() =>
        {
            var window = OpenWindow(new MainViewModel(), 960, 640);
            try
            {
                Assert.False(window.FindControl<ItemsControl>("CookieFields")!.IsVisible);
                window.FindControl<Expander>("GitHubSection")!.IsExpanded = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                window.UpdateLayout();
                var update = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, "更新 GitHub Secrets"));
                Assert.True(RelativeBounds(update, window).Bottom <= RelativeBounds(scroll, window).Bottom);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static MainViewModel CreateLoadedModel(bool reveal)
    {
        var vm = new MainViewModel { HasResult = true, RevealValues = reveal };
        var cookies = BilibiliCookieParser.ParseText(
            "SESSDATA=sample%2C1893456000%2Cencoded-session-value; bili_jct=sample-csrf-token; DedeUserID=12345678; buvid3=sample-device-id");
        for (var i = 0; i < vm.Fields.Count; i++)
        {
            vm.Fields[i].Apply(cookies.Fields[i], reveal);
            vm.Fields[i].MetaText = ".bilibili.com · Cookie 到期 2030-01-01 08:00 台北";
        }
        return vm;
    }

    private static MainWindow OpenWindow(MainViewModel vm, int width, int height)
    {
        // Attach data after opening to avoid starting network services or loading local credentials.
        var window = new MainWindow { Width = width, Height = height };
        window.Show();
        window.DataContext = vm;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    private static Rect RelativeBounds(Control control, Window window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
}

public static class LayoutTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
