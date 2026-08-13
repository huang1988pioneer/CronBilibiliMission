using System.Net;
using System.Text;
using BilibiliCookieReader.Services;
using Xunit;

namespace BilibiliCookieReader.Tests;

public sealed class DailyHotSearchRecorderTests
{
    [Fact]
    public void Parses_complete_hot_search_list_and_labels()
    {
        var entries = DailyHotSearchRecorder.ParseEntries(
            """{"code":0,"list":[{"pos":1,"show_name":"第一名","word_type":4},{"pos":2,"keyword":"第二名","word_type":8}]}""");

        Assert.Equal(2, entries.Count);
        Assert.Equal(new DailyHotSearchEntry(1, "第一名", "新"), entries[0]);
        Assert.Equal(new DailyHotSearchEntry(2, "第二名", ""), entries[1]);
    }

    [Fact]
    public async Task Records_once_per_taipei_date()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hot-search-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "hot_search.jsonl");
        try
        {
            var handler = new FakeHandler("""{"code":0,"list":[{"pos":1,"show_name":"第一名","word_type":5}]}""");
            var recorder = new DailyHotSearchRecorder(new HttpClient(handler), path);

            var first = await recorder.RecordTodayAsync(new DateTimeOffset(2026, 8, 13, 16, 30, 0, TimeSpan.Zero));
            var second = await recorder.RecordTodayAsync(new DateTimeOffset(2026, 8, 14, 8, 0, 0, TimeSpan.FromHours(8)));

            Assert.True(first.Recorded);
            Assert.False(second.Recorded);
            Assert.Equal(new DateOnly(2026, 8, 14), first.Date);
            Assert.Equal(1, handler.CallCount);
            Assert.Single(await File.ReadAllLinesAsync(path));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Malformed_history_line_does_not_hide_valid_history()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hot-search-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "hot_search.jsonl");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            path,
            "not-json\n{\"date\":\"2026-08-14\",\"capturedAtTaipei\":\"2026-08-14T01:00:00+08:00\",\"entries\":[]}\n");
        try
        {
            var handler = new FakeHandler("{}", HttpStatusCode.InternalServerError);
            var recorder = new DailyHotSearchRecorder(new HttpClient(handler), path);

            var result = await recorder.RecordTodayAsync(new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.FromHours(8)));

            Assert.False(result.Recorded);
            Assert.Equal(0, handler.CallCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeHandler(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
