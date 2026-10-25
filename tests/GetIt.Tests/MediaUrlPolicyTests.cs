using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public class MediaUrlPolicyTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abcdefghijk")]
    [InlineData("https://youtu.be/abcdefghijk?t=30")]
    [InlineData("https://m.youtube.com/shorts/abcdefghijk")]
    [InlineData("https://music.youtube.com/watch?v=abcdefghijk")]
    [InlineData("https://www.youtube.com/live/abcdefghijk")]
    public void AutomaticPasteAcceptsIndividualYouTubeVideos(string input) =>
        Assert.True(MediaUrlPolicy.IsYouTubeVideoUrl(input));

    [Theory]
    [InlineData("https://youtube.com.evil.example/watch?v=abcdefghijk")]
    [InlineData("https://example.com/?next=youtube.com/watch?v=abcdefghijk")]
    [InlineData("https://youtube.com/playlist?list=PL123")]
    [InlineData("https://youtube.com/watch?list=PL123")]
    [InlineData("https://youtu.be/")]
    [InlineData("file:///youtube.com/watch?v=abcdefghijk")]
    public void AutomaticPasteRejectsUnrelatedHostsAndPlaylistPages(string input) =>
        Assert.False(MediaUrlPolicy.IsYouTubeVideoUrl(input));

    [Fact]
    public void PastedSingleUrlIsTrimmedAndCanonicalized()
    {
        Assert.True(MediaUrlPolicy.TryNormalizeHttpUrl(" \nHTTPS://EXAMPLE.COM/a%20video?q=1\r\n", out var result));
        Assert.Equal("https://example.com/a%20video?q=1", result);
    }

    [Fact]
    public void MultipleLinesAreNotTreatedAsOneMediaLink() =>
        Assert.False(MediaUrlPolicy.TryNormalizeHttpUrl("https://example.com/a\nhttps://example.com/b", out _));

    [Fact]
    public void QuotationMarksAreEscapedBeforeTheUrlReachesTheDownloader()
    {
        Assert.True(MediaUrlPolicy.TryNormalizeHttpUrl("https://example.com/video?name=\"quoted\"", out var result));
        Assert.DoesNotContain('"', result);
        Assert.Contains("%22", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a link")]
    [InlineData("file:///C:/video.mp4")]
    [InlineData("ftp://example.com/video.mp4")]
    public void OnlyAbsoluteHttpAndHttpsLinksCanBeQueued(string input) =>
        Assert.False(MediaUrlPolicy.TryNormalizeHttpUrl(input, out _));
}
