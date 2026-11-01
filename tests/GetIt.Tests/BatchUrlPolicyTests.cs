using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public class BatchUrlPolicyTests
{
    [Fact]
    public void PreviewNormalizesUrlsAndPreservesTheirOrder()
    {
        var preview = BatchUrlPolicy.CreatePreview(
            "  HTTPS://EXAMPLE.COM/first  \r\n\nhttps://example.com/second",
            Array.Empty<string>());

        Assert.Equal(2, preview.ReadyCount);
        Assert.Equal(
            new[] { "https://example.com/first", "https://example.com/second" },
            preview.Entries.Select(entry => entry.NormalizedUrl));
        Assert.Equal(new[] { 1, 3 }, preview.Entries.Select(entry => entry.LineNumber));
    }

    [Fact]
    public void PreviewClassifiesInvalidQueuedAndRepeatedLinks()
    {
        var preview = BatchUrlPolicy.CreatePreview(
            "https://example.com/queued\nnot a URL\nhttps://example.com/new\nhttps://EXAMPLE.com/new",
            new[] { "https://example.com/queued" });

        Assert.Equal(1, preview.ReadyCount);
        Assert.Equal(1, preview.AlreadyInQueueCount);
        Assert.Equal(1, preview.InvalidCount);
        Assert.Equal(1, preview.DuplicateInBatchCount);
    }

    [Fact]
    public void PreviewIgnoresBlankLinesAndAllowsAValidSingleUrl()
    {
        var preview = BatchUrlPolicy.CreatePreview("\r\n https://example.com/video \n ", Array.Empty<string>());

        Assert.Equal(1, preview.NonEmptyLineCount);
        Assert.Single(preview.Entries);
        Assert.Equal(BatchUrlStatus.Ready, preview.Entries[0].Status);
    }

    [Fact]
    public void PreviewBlocksBatchesAboveTheLimit()
    {
        var input = string.Join("\n", Enumerable.Range(0, BatchUrlPolicy.MaximumLinks + 1)
            .Select(index => $"https://example.com/{index}"));

        var preview = BatchUrlPolicy.CreatePreview(input, Array.Empty<string>());

        Assert.True(preview.IsOverLimit);
        Assert.Equal(BatchUrlPolicy.MaximumLinks + 1, preview.NonEmptyLineCount);
        Assert.Equal(BatchUrlPolicy.MaximumLinks, preview.Entries.Count);
    }

    [Fact]
    public void PreviewDoesNotAggressivelyChangeUrlParameters()
    {
        const string input = "https://example.com/watch?v=first%2Fvalue&list=PL123";

        var preview = BatchUrlPolicy.CreatePreview(input, Array.Empty<string>());

        Assert.Equal(input, Assert.Single(preview.Entries).NormalizedUrl);
    }
}
