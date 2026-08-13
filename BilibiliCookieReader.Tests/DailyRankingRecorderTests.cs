using System.Net;
using System.Text;
using BilibiliCookieReader.Services;
using Xunit;

namespace BilibiliCookieReader.Tests;

public sealed class DailyRankingRecorderTests
{
    [Fact]
    public void Category_catalog_contains_all_and_every_visible_category()
    {
        Assert.Equal(21, DailyRankingRecorder.Categories.Count);
        Assert.Equal(
            ["全部", "番劇", "國創", "紀錄片", "電影", "電視劇", "綜藝", "動畫", "遊戲", "鬼畜", "音樂", "舞蹈", "影視", "娛樂", "知識", "科技數碼", "美食", "汽車", "時尚美妝", "體育運動", "動物"],
            DailyRankingRecorder.Categories.Select(category => category.Name));
    }

    [Fact]
    public void Parses_normal_ranking_fields()
    {
        var entries = DailyRankingRecorder.ParseNormalEntries(
            """{"code":0,"data":{"list":[{"bvid":"BV1","title":"影片","owner":{"name":"UP"},"stat":{"view":100,"danmaku":10,"favorite":5,"like":20,"coin":8,"share":2},"score":999,"duration":180,"pubdate":1786636800,"pic":"https://i.example/a.jpg"}]}}""");

        Assert.Single(entries);
        Assert.Equal("BV1", entries[0].ContentId);
        Assert.Equal("UP", entries[0].Uploader);
        Assert.Equal(999, entries[0].RankingScore);
    }

    [Fact]
    public void Parses_pgc_rating_progress_and_followers()
    {
        var entries = DailyRankingRecorder.ParsePgcEntries(
            """{"code":0,"result":{"list":[{"season_id":123,"title":"番劇","url":"https://www.bilibili.com/bangumi/play/ss123","cover":"https://i.example/p.jpg","new_ep":{"index_show":"更新至第 3 話"},"rating":{"score":9.8},"stat":{"view":200,"danmaku":20,"follow":30}}]}}""");

        Assert.Single(entries);
        Assert.Equal(9.8, entries[0].Rating);
        Assert.Equal("更新至第 3 話", entries[0].Progress);
        Assert.Equal(30, entries[0].Followers);
    }

    [Fact]
    public async Task Records_all_categories_once_per_taipei_date()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ranking-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "ranking.jsonl");
        try
        {
            var handler = new RankingHandler();
            var recorder = new DailyRankingRecorder(new HttpClient(handler), path);

            var first = await recorder.RecordTodayAsync(new DateTimeOffset(2026, 8, 13, 16, 30, 0, TimeSpan.Zero));
            var second = await recorder.RecordTodayAsync(new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.FromHours(8)));

            Assert.True(first.Changed);
            Assert.True(first.Complete);
            Assert.Equal(21, first.CategoryCount);
            Assert.False(second.Changed);
            Assert.Equal(21, handler.CallCount);
            Assert.Single(await File.ReadAllLinesAsync(path));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RankingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var isPgc = request.RequestUri?.AbsolutePath.Contains("/pgc/") == true;
            var json = isPgc
                ? """{"code":0,"result":{"list":[{"season_id":123,"title":"PGC","url":"https://www.bilibili.com/bangumi/play/ss123","stat":{"view":1}}]}}"""
                : """{"code":0,"data":{"list":[{"bvid":"BV1","title":"影片","owner":{"name":"UP"},"stat":{"view":1}}]}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
