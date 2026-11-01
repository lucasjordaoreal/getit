using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public class PlaylistPreviewPolicyTests
{
    [Fact]
    public void ParseReturnsNullForAnIndividualVideo()
    {
        var preview = PlaylistPreviewPolicy.Parse("""{"_type":"video","id":"abc"}""");

        Assert.Null(preview);
    }

    [Fact]
    public void ParseExtractsPlaylistAndSafeVideoUrls()
    {
        const string json = """
            {
              "_type": "playlist",
              "title": "Collection",
              "uploader": "Channel",
              "playlist_count": 2,
              "entries": [
                {"_type":"url","id":"abcdefghijk","ie_key":"Youtube","title":"First","duration":12},
                {"_type":"url","webpage_url":"https://example.com/watch/second","title":"Second"}
              ]
            }
            """;

        var preview = PlaylistPreviewPolicy.Parse(json);

        Assert.NotNull(preview);
        Assert.Equal("Collection", preview.Title);
        Assert.Equal("Channel", preview.Uploader);
        Assert.Equal(2, preview.TotalCount);
        Assert.False(preview.IsPartial);
        Assert.Equal("https://www.youtube.com/watch?v=abcdefghijk", preview.Videos[0].Url);
        Assert.Equal("https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg", preview.Videos[0].Thumbnail);
        Assert.Equal("https://example.com/watch/second", preview.Videos[1].Url);
    }

    [Fact]
    public void ParseUsesAnAvailableThumbnailFromTheExtractor()
    {
        var preview = PlaylistPreviewPolicy.Parse("""
            {
              "_type":"playlist",
              "entries":[
                {
                  "webpage_url":"https://example.com/video",
                  "thumbnails":[
                    {"url":"javascript:alert(1)"},
                    {"url":"https://images.example.com/video.jpg"}
                  ]
                }
              ]
            }
            """);

        Assert.NotNull(preview);
        Assert.Equal("https://images.example.com/video.jpg", preview.Videos[0].Thumbnail);
    }

    [Fact]
    public void ParseMarksMissingOrPrivateEntriesUnavailable()
    {
        const string json = """
            {
              "_type":"playlist",
              "entries":[
                {"_type":"url","id":"not-a-video","ie_key":"Unknown","title":"Unknown"},
                {"_type":"url","id":"abcdefghijk","ie_key":"Youtube","availability":"private"},
                {"webpage_url":"https://test@example.com/video"}
              ]
            }
            """;

        var preview = PlaylistPreviewPolicy.Parse(json);

        Assert.NotNull(preview);
        Assert.All(preview.Videos, video => Assert.False(video.IsAvailable));
        Assert.All(preview.Videos, video => Assert.Null(video.Url));
    }

    [Fact]
    public void ParseLimitsLargePlaylistsAndReportsPartialResults()
    {
        var entries = string.Join(",", Enumerable.Range(0, PlaylistPreviewPolicy.MaximumPreviewItems + 1)
            .Select(index => $$"""{"_type":"url","webpage_url":"https://example.com/{{index}}"}"""));
        var json = $$"""{"_type":"playlist","entries":[{{entries}}]}""";

        var preview = PlaylistPreviewPolicy.Parse(json);

        Assert.NotNull(preview);
        Assert.Equal(PlaylistPreviewPolicy.MaximumPreviewItems, preview.Videos.Count);
        Assert.True(preview.IsPartial);
    }

    [Fact]
    public void ParseMarksAnUnknownCountAtThePreviewLimitAsPotentiallyPartial()
    {
        var entries = string.Join(",", Enumerable.Range(0, PlaylistPreviewPolicy.MaximumPreviewItems)
            .Select(index => $$"""{"webpage_url":"https://example.com/{{index}}"}"""));
        var json = $$"""{"_type":"playlist","entries":[{{entries}}]}""";

        var preview = PlaylistPreviewPolicy.Parse(json);

        Assert.NotNull(preview);
        Assert.Null(preview.TotalCount);
        Assert.True(preview.IsPartial);
    }

    [Fact]
    public void ParseAcceptsEmptyPlaylistAndIncompleteMetadata()
    {
        var preview = PlaylistPreviewPolicy.Parse("""{"_type":"playlist","entries":[null]}""");

        Assert.NotNull(preview);
        Assert.Null(preview.Title);
        Assert.Null(preview.TotalCount);
        Assert.Single(preview.Videos);
        Assert.False(preview.Videos[0].IsAvailable);
    }

    [Fact]
    public void ParseRecognizesAnActuallyEmptyPlaylistWithoutInventingItsCount()
    {
        var preview = PlaylistPreviewPolicy.Parse("""{"_type":"playlist","entries":[],"n_entries":42}""");

        Assert.NotNull(preview);
        Assert.Empty(preview.Videos);
        Assert.Null(preview.TotalCount);
        Assert.False(preview.IsPartial);
    }

    [Fact]
    public void ParseRejectsInvalidJson()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => PlaylistPreviewPolicy.Parse("{"));
    }

    [Fact]
    public void ResolveSelectionPreservesOrderAndSkipsDuplicatesAndUnavailableItems()
    {
        var preview = PlaylistPreviewPolicy.Parse("""
            {
              "_type":"playlist",
              "entries":[
                {"webpage_url":"https://example.com/a","title":"A"},
                {"webpage_url":"https://example.com/b","title":"B"},
                {"webpage_url":"https://example.com/a","title":"Duplicate"},
                {"id":"unknown","ie_key":"Unknown","title":"Unavailable"}
              ]
            }
            """)!;

        var result = PlaylistPreviewPolicy.ResolveSelection(
            preview,
            [1, 2, 3, 4],
            ["https://example.com/b"]);

        Assert.Equal(["https://example.com/a"], result.Urls);
        Assert.Equal(2, result.DuplicateCount);
        Assert.Equal(1, result.UnavailableCount);
    }

    [Fact]
    public void ResolveSelectionPreservesPlaylistOrderForSelectedItems()
    {
        var preview = PlaylistPreviewPolicy.Parse("""
            {
              "_type":"playlist",
              "entries":[
                {"webpage_url":"https://example.com/a"},
                {"webpage_url":"https://example.com/b"},
                {"webpage_url":"https://example.com/c"}
              ]
            }
            """)!;

        var result = PlaylistPreviewPolicy.ResolveSelection(preview, [3, 1], []);

        Assert.Equal(["https://example.com/a", "https://example.com/c"], result.Urls);
    }

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PL123", true)]
    [InlineData("https://example.com/collection/123", true)]
    [InlineData("https://www.youtube.com/watch?v=abcdefghijk&list=PL123", true)]
    [InlineData("https://www.youtube.com/watch?v=abcdefghijk", false)]
    public void PlaylistUrlPolicyRecognizesCollectionCandidates(string url, bool expected) =>
        Assert.Equal(expected, PlaylistUrlPolicy.MayRepresentPlaylist(url));

    [Fact]
    public void PlaylistUrlPolicyIdentifiesAmbiguousVideoAndPlaylistLinks()
    {
        Assert.True(PlaylistUrlPolicy.IsAmbiguousVideoAndPlaylistUrl(
            "https://www.youtube.com/watch?v=abcdefghijk&list=PL123"));
        Assert.True(PlaylistUrlPolicy.IsAmbiguousVideoAndPlaylistUrl(
            "https://www.youtube.com/shorts/abcdefghijk?list=PL123"));
        Assert.False(PlaylistUrlPolicy.IsAmbiguousVideoAndPlaylistUrl(
            "https://www.youtube.com/playlist?list=PL123"));
    }
}
