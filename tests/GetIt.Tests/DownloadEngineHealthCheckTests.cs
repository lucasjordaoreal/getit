using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public sealed class DownloadEngineHealthCheckTests
{
    [Theory]
    [InlineData("yt-dlp.exe", "--version")]
    [InlineData("ffmpeg.exe", "-version")]
    [InlineData("ffprobe.exe", "-version")]
    public async Task CheckExecutableAsync_ReportsMissingComponentAndPath(string component, string argument)
    {
        using var directory = new TemporaryDirectory();
        var executable = Path.Combine(directory.Path, component);

        var issue = await DownloadEngineHealthCheck.CheckExecutableAsync(
            component, executable, [argument]);

        Assert.NotNull(issue);
        Assert.Equal(component, issue.Component);
        Assert.Equal(executable, issue.Path);
        Assert.Contains("não foi encontrado", issue.Reason);
    }

    [Fact]
    public async Task CheckExecutableAsync_ReportsInvalidExecutable()
    {
        using var directory = new TemporaryDirectory();
        var executable = Path.Combine(directory.Path, "ffmpeg.exe");
        File.WriteAllText(executable, "not a Windows executable");

        var issue = await DownloadEngineHealthCheck.CheckExecutableAsync(
            "ffmpeg.exe", executable, ["-version"]);

        Assert.NotNull(issue);
        Assert.Equal("ffmpeg.exe", issue.Component);
        Assert.Contains("não foi possível iniciar", issue.Reason);
    }

    [Fact]
    public async Task CheckExecutableAsync_AcceptsAnExecutableThatStartsSuccessfully()
    {
        var commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
        Assert.False(string.IsNullOrWhiteSpace(commandProcessor));

        var issue = await DownloadEngineHealthCheck.CheckExecutableAsync(
            "componente de teste", commandProcessor!, ["/c", "exit", "0"]);

        Assert.Null(issue);
    }

    [Fact]
    public async Task CheckExecutableAsync_RecognizesEngineRestoredAfterAnInitialFailure()
    {
        using var directory = new TemporaryDirectory();
        var executable = Path.Combine(directory.Path, "yt-dlp.exe");
        var missingIssue = await DownloadEngineHealthCheck.CheckExecutableAsync(
            "yt-dlp.exe", executable, ["/c", "exit", "0"]);
        Assert.NotNull(missingIssue);

        var commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
        Assert.False(string.IsNullOrWhiteSpace(commandProcessor));
        File.Copy(commandProcessor!, executable);

        var restoredIssue = await DownloadEngineHealthCheck.CheckExecutableAsync(
            "yt-dlp.exe", executable, ["/c", "exit", "0"]);

        Assert.Null(restoredIssue);
    }

    [Fact]
    public async Task CheckExecutableAsync_ReportsNonZeroExitAsUnavailable()
    {
        var commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
        Assert.False(string.IsNullOrWhiteSpace(commandProcessor));

        var issue = await DownloadEngineHealthCheck.CheckExecutableAsync(
            "componente de teste", commandProcessor!, ["/c", "exit", "7"]);

        Assert.NotNull(issue);
        Assert.Contains("código 7", issue.Reason);
    }

    [Fact]
    public async Task FfmpegEngineValidator_ReportsMissingExecutables()
    {
        using var directory = new TemporaryDirectory();

        var issues = await FfmpegEngineValidator.ValidateDirectoryAsync(directory.Path);

        Assert.Collection(
            issues,
            issue =>
            {
                Assert.Equal("ffmpeg.exe", issue.Component);
                Assert.Contains("não foi encontrado", issue.Reason);
            },
            issue =>
            {
                Assert.Equal("ffprobe.exe", issue.Component);
                Assert.Contains("não foi encontrado", issue.Reason);
            });
    }

    [Theory]
    [InlineData("ffmpeg version 8.1.1-full_build-www.gyan.dev", 8, 1, 1)]
    [InlineData("ffmpeg version n8.1.3-27-g8dca1586f4-20261031", 8, 1, 3)]
    public void FfmpegEngineValidator_ParsesVendorAndGitVersionStrings(
        string banner, int major, int minor, int build)
    {
        var version = FfmpegEngineValidator.ParseVersion(banner);

        Assert.Equal(new Version(major, minor, build), version);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"GetIt-engine-health-{Guid.NewGuid():N}");

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
