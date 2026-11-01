using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public class HistoryFilterPolicyTests
{
    [Fact]
    public void SearchMatchesTitleAndUrlWithoutCaseSensitivity()
    {
        var records = new[]
        {
            new HistoryItem { Title = "Ocean Song", Url = "https://example.test/video" },
            new HistoryItem { Title = "Other", Url = "https://example.test/OCEAN" }
        };

        Assert.Equal(2, HistoryFilterPolicy.Filter(records, "oCeAn", HistoryMediaFilter.All).Count);
    }

    [Fact]
    public void SearchSupportsPartialMatchesAndReturnsNoUnrelatedResults()
    {
        var record = new HistoryItem { Title = "A wonderful video", Url = "https://example.test/watch" };

        Assert.Single(HistoryFilterPolicy.Filter([record], "wonder", HistoryMediaFilter.All));
        Assert.Empty(HistoryFilterPolicy.Filter([record], "missing", HistoryMediaFilter.All));
    }

    [Fact]
    public void MediaFiltersUseFileExtensionAndKeepUnknownItemsInAll()
    {
        var records = new[]
        {
            new HistoryItem { LocalFilePath = @"C:\media\video.MP4" },
            new HistoryItem { LocalFilePath = @"C:\media\song.flac" },
            new HistoryItem { LocalFilePath = @"C:\media\unknown.data" }
        };

        Assert.Single(HistoryFilterPolicy.Filter(records, null, HistoryMediaFilter.Videos));
        Assert.Single(HistoryFilterPolicy.Filter(records, null, HistoryMediaFilter.Audios));
        Assert.Equal(3, HistoryFilterPolicy.Filter(records, null, HistoryMediaFilter.All).Count);
    }

    [Fact]
    public void FilterSortsNewestFirstAndKeepsMissingFiles()
    {
        var older = new HistoryItem
        {
            Title = "Older",
            LocalFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "gone.mp4"),
            DownloadedAt = DateTime.UtcNow.AddDays(-1)
        };
        var newer = new HistoryItem
        {
            Title = "Newer",
            LocalFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "gone.mp4"),
            DownloadedAt = DateTime.UtcNow
        };

        var results = HistoryFilterPolicy.Filter([older, newer], null, HistoryMediaFilter.All);

        Assert.Equal(new[] { "Newer", "Older" }, results.Select(item => item.Title));
        Assert.True(results.All(item => item.IsFileMissing));
        Assert.True(results.All(item => !item.IsFileAvailable));
    }

    [Fact]
    public void LegacyHistoryJsonWithoutOptionalPropertiesRemainsReadable()
    {
        const string json = """[{"Title":"Legacy","Url":"https://example.test","Thumbnail":"","LocalFilePath":"gone.mp4","DownloadedAt":"2025-01-01T10:00:00"}]""";

        var history = System.Text.Json.JsonSerializer.Deserialize<List<HistoryItem>>(json);

        Assert.NotNull(history);
        Assert.Single(history);
        Assert.Equal("Legacy", history[0].Title);
        Assert.True(history[0].IsFileMissing);
    }
}
