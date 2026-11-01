using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public sealed class NonOverwritingFilePublisherTests
{
    [Fact]
    public void Move_RejectsExistingDestinationAndPreservesBothFiles()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "new.mp4");
        var destination = Path.Combine(directory.Path, "video.mp4");
        File.WriteAllText(source, "new-content");
        File.WriteAllText(destination, "existing-content");

        Assert.Throws<IOException>(() => NonOverwritingFilePublisher.Move(source, destination));
        Assert.Equal("new-content", File.ReadAllText(source));
        Assert.Equal("existing-content", File.ReadAllText(destination));
    }

    [Fact]
    public void MoveWithAlternative_UsesFirstAvailableNumberedNameWithoutReplacingFiles()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "new.mp4");
        var destination = Path.Combine(directory.Path, "video.mp4");
        File.WriteAllText(source, "new-content");
        File.WriteAllText(destination, "existing-content");
        File.WriteAllText(Path.Combine(directory.Path, "video (1).mp4"), "also-existing");

        var publishedPath = NonOverwritingFilePublisher.Move(source, destination, createAlternative: true);

        Assert.Equal(Path.Combine(directory.Path, "video (2).mp4"), publishedPath);
        Assert.Equal("existing-content", File.ReadAllText(destination));
        Assert.Equal("also-existing", File.ReadAllText(Path.Combine(directory.Path, "video (1).mp4")));
        Assert.Equal("new-content", File.ReadAllText(publishedPath));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void Move_WhenSourceIsMissing_DoesNotChangeExistingDestination()
    {
        using var directory = new TemporaryDirectory();
        var destination = Path.Combine(directory.Path, "video.mp4");
        File.WriteAllText(destination, "existing-content");

        Assert.Throws<IOException>(() => NonOverwritingFilePublisher.Move(
            Path.Combine(directory.Path, "missing.mp4"), destination));
        Assert.Equal("existing-content", File.ReadAllText(destination));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"GetIt-publish-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
