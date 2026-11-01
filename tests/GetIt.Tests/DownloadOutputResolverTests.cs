using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public sealed class DownloadOutputResolverTests
{
    [Fact]
    public void MissingReportedPath_ResolvesOnlyACompleteFileInTheOperationDirectory()
    {
        using var directory = new TemporaryDirectory();
        var destination = Directory.CreateDirectory(Path.Combine(directory.Path, "downloads")).FullName;
        var operation = Directory.CreateDirectory(Path.Combine(destination, ".getit-work", "operation")).FullName;
        var existingDownload = Path.Combine(destination, "older.mp4");
        var producedDownload = Path.Combine(operation, "current.mp4");
        File.WriteAllText(existingDownload, "previous file");
        File.WriteAllText(producedDownload, "current operation");

        var result = DownloadOutputResolver.ResolveSingleFile(null, operation, "mp4");

        Assert.Equal(producedDownload, result);
    }

    [Fact]
    public void ResolveSingleFile_RejectsAmbiguousOutputsInsteadOfChoosingOne()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "first.mp4"), "first");
        File.WriteAllText(Path.Combine(directory.Path, "second.mp4"), "second");

        Assert.Throws<InvalidDataException>(() =>
            DownloadOutputResolver.ResolveSingleFile(null, directory.Path, "mp4"));
    }

    [Theory]
    [InlineData("video.mp4.part")]
    [InlineData("video.mp4.ytdl")]
    [InlineData("video.temp.mp4")]
    public void ResolveSingleFile_IgnoresIncompleteAndTemporaryFiles(string fileName)
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, fileName), "partial");

        Assert.Null(DownloadOutputResolver.ResolveSingleFile(null, directory.Path, "mp4"));
    }

    [Fact]
    public void ResolveSingleFile_UsesTheReportedFileWhenItBelongsToTheOperation()
    {
        using var directory = new TemporaryDirectory();
        var reported = Path.Combine(directory.Path, "reported.mp3");
        File.WriteAllText(reported, "audio");

        Assert.Equal(reported, DownloadOutputResolver.ResolveSingleFile(reported, directory.Path, "mp3"));
    }

    [Fact]
    public void ResolveSingleFile_RejectsAReportedPathOutsideTheOperation()
    {
        using var directory = new TemporaryDirectory();
        var operation = Directory.CreateDirectory(Path.Combine(directory.Path, "operation")).FullName;
        var unrelated = Path.Combine(directory.Path, "older.mp4");
        File.WriteAllText(unrelated, "unrelated");

        Assert.Throws<InvalidDataException>(() =>
            DownloadOutputResolver.ResolveSingleFile(unrelated, operation, "mp4"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"GetIt-output-resolver-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
