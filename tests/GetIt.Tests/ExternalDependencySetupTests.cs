using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public sealed class DependencySetupIntegrationFactAttribute : FactAttribute
{
    public DependencySetupIntegrationFactAttribute()
    {
        var assetsDirectory = Environment.GetEnvironmentVariable("GETIT_DEPENDENCY_SETUP_ASSET_DIR");
        if (string.IsNullOrWhiteSpace(assetsDirectory) ||
            !File.Exists(Path.Combine(assetsDirectory, "ffmpeg-n8.1.3-27-g8dca1586f4-win64-gpl-8.1.zip")) ||
            !File.Exists(Path.Combine(assetsDirectory, "deno-x86_64-pc-windows-msvc.zip")))
        {
            Skip = "Set GETIT_DEPENDENCY_SETUP_ASSET_DIR to the verified official FFmpeg and Deno archives.";
        }
    }
}

public sealed class ExternalDependencySetupTests
{
    [Fact]
    public async Task ConfigureAsync_RejectsHashMismatchBeforeExtractingOrChangingSettings()
    {
        using var temp = new TemporaryDirectory();
        var archivePath = Path.Combine(temp.Path, "malicious.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("bin/ffmpeg.exe").Open());
            writer.Write("not an executable");
        }

        var settings = new AppSettings { FfmpegBinPath = "previous-ffmpeg", DenoBinPath = "previous-deno" };
        var requests = 0;
        using var httpClient = new HttpClient(new ArchiveHandler((_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(File.OpenRead(archivePath))
            });
        }));
        var asset = new DependencySetupAsset(
            ExternalDependencySetupService.FfmpegAsset.Uri,
            new string('0', 64),
            1024 * 1024,
            "test-ffmpeg",
            ["ffmpeg.exe", "ffprobe.exe", "LICENSE.txt"]);
        var service = new ExternalDependencySetupService(
            Path.Combine(temp.Path, "tools"),
            httpClient,
            () => settings,
            update => update(settings),
            asset,
            ExternalDependencySetupService.DenoAsset);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ConfigureAsync());

        Assert.Equal(1, requests);
        Assert.Equal("previous-ffmpeg", settings.FfmpegBinPath);
        Assert.Equal("previous-deno", settings.DenoBinPath);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "tools", "FFmpeg")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(temp.Path, "tools", ".staging")));
    }

    [Fact]
    public async Task ConfigureAsync_CancellationPreservesSettingsAndRemovesPartialStagingFiles()
    {
        using var temp = new TemporaryDirectory();
        var settings = new AppSettings { FfmpegBinPath = "previous-ffmpeg", DenoBinPath = "previous-deno" };
        using var httpClient = new HttpClient(new ArchiveHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The canceled response should never complete.");
        }))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var service = new ExternalDependencySetupService(
            Path.Combine(temp.Path, "tools"),
            httpClient,
            () => settings,
            update => update(settings),
            ExternalDependencySetupService.FfmpegAsset,
            ExternalDependencySetupService.DenoAsset);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ConfigureAsync(cancellationToken: cancellation.Token));

        Assert.Equal("previous-ffmpeg", settings.FfmpegBinPath);
        Assert.Equal("previous-deno", settings.DenoBinPath);
        Assert.Empty(Directory.GetDirectories(Path.Combine(temp.Path, "tools", ".staging")));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "tools", "FFmpeg")));
    }

    [DependencySetupIntegrationFact]
    public async Task ConfigureAsync_ValidatesAndReusesVerifiedOfficialArchives()
    {
        var assetsDirectory = Environment.GetEnvironmentVariable("GETIT_DEPENDENCY_SETUP_ASSET_DIR")!;
        var ffmpegArchive = Path.Combine(assetsDirectory, "ffmpeg-n8.1.3-27-g8dca1586f4-win64-gpl-8.1.zip");
        var denoArchive = Path.Combine(assetsDirectory, "deno-x86_64-pc-windows-msvc.zip");
        using var ffmpegStream = File.OpenRead(ffmpegArchive);
        using var denoStream = File.OpenRead(denoArchive);
        Assert.Equal(
            ExternalDependencySetupService.FfmpegAsset.Sha256,
            Convert.ToHexString(SHA256.HashData(ffmpegStream)).ToLowerInvariant());
        Assert.Equal(
            ExternalDependencySetupService.DenoAsset.Sha256,
            Convert.ToHexString(SHA256.HashData(denoStream)).ToLowerInvariant());

        using var temp = new TemporaryDirectory();
        var settings = new AppSettings();
        var requests = 0;
        using var httpClient = new HttpClient(new ArchiveHandler((request, _) =>
        {
            requests++;
            var path = request.RequestUri!.AbsolutePath.EndsWith("ffmpeg-n8.1.3-27-g8dca1586f4-win64-gpl-8.1.zip", StringComparison.Ordinal)
                ? ffmpegArchive
                : request.RequestUri.AbsolutePath.EndsWith("deno-x86_64-pc-windows-msvc.zip", StringComparison.Ordinal)
                    ? denoArchive
                    : throw new InvalidOperationException($"Unexpected download URI: {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(File.OpenRead(path))
            });
        }))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var service = new ExternalDependencySetupService(
            Path.Combine(temp.Path, "local-app-data", "GetIt", "Tools"),
            httpClient,
            () => settings,
            update => update(settings),
            ExternalDependencySetupService.FfmpegAsset,
            ExternalDependencySetupService.DenoAsset,
            FindRepositoryLicensePath());

        var configured = await service.ConfigureAsync();

        Assert.Equal(configured.FfmpegDirectory, settings.FfmpegBinPath);
        Assert.Equal(configured.DenoDirectory, settings.DenoBinPath);
        Assert.True(File.Exists(Path.Combine(configured.FfmpegDirectory, "ffmpeg.exe")));
        Assert.True(File.Exists(Path.Combine(configured.FfmpegDirectory, "ffprobe.exe")));
        Assert.True(File.Exists(Path.Combine(configured.FfmpegDirectory, "LICENSE.txt")));
        Assert.True(File.Exists(Path.Combine(configured.DenoDirectory, "deno.exe")));
        Assert.True(File.Exists(Path.Combine(configured.DenoDirectory, "DENO-MIT-LICENSE.md")));
        Assert.Empty(await FfmpegEngineValidator.ValidateDirectoryAsync(configured.FfmpegDirectory));
        Assert.Empty(await DenoEngineValidator.ValidateDirectoryAsync(configured.DenoDirectory));
        await AssertSyntheticFormatsAsync(configured.FfmpegDirectory, temp.Path);
        Assert.Equal(2, requests);
        Assert.Empty(Directory.GetFiles(Path.Combine(temp.Path, "local-app-data", "GetIt", "Tools", ".staging"), "*", SearchOption.AllDirectories));

        var reused = await service.ConfigureAsync();
        Assert.Equal(configured, reused);
        Assert.Equal(2, requests);
    }

    private static async Task AssertSyntheticFormatsAsync(string engineDirectory, string workingDirectory)
    {
        var ffmpeg = Path.Combine(engineDirectory, "ffmpeg.exe");
        var ffprobe = Path.Combine(engineDirectory, "ffprobe.exe");
        var videoInput = Path.Combine(workingDirectory, "generated.mp4");
        await RunAsync(ffmpeg,
        [
            "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=64x64:d=0.3",
            "-f", "lavfi", "-i", "sine=frequency=1000:duration=0.3", "-shortest",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-y", videoInput
        ], engineDirectory);

        foreach (var extension in new[] { "mp4", "mov" })
        {
            var output = Path.Combine(workingDirectory, $"video.{extension}");
            await RunAsync(ffmpeg,
            [
                "-hide_banner", "-loglevel", "error", "-i", videoInput,
                "-c:v", "libx264", "-c:a", "aac", "-y", output
            ], engineDirectory);
            var probe = await RunAsync(
                ffprobe, ["-v", "error", "-show_entries", "stream=codec_name", "-of", "csv=p=0", output],
                engineDirectory);
            Assert.Contains("h264", probe, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("aac", probe, StringComparison.OrdinalIgnoreCase);
        }

        var formats = new (string Extension, string Encoder)[]
        {
            ("mp3", "libmp3lame"),
            ("wav", "pcm_s16le"),
            ("flac", "flac")
        };
        foreach (var (extension, encoder) in formats)
        {
            var output = Path.Combine(workingDirectory, $"audio.{extension}");
            await RunAsync(ffmpeg,
            [
                "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=1000:duration=0.3",
                "-c:a", encoder, "-y", output
            ], engineDirectory);
            var probe = await RunAsync(
                ffprobe, ["-v", "error", "-show_entries", "stream=codec_name", "-of", "csv=p=0", output],
                engineDirectory);
            Assert.NotEmpty(probe.Trim());
            Assert.True(new FileInfo(output).Length > 0);
        }
    }

    private static string FindRepositoryLicensePath()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "licenses", "texts", "Deno-MIT-LICENSE.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate the repository Deno MIT license file.");
    }

    private static async Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0, $"{Path.GetFileName(executable)} exited with {process.ExitCode}: {error}");
        return $"{output}{Environment.NewLine}{error}";
    }

    private sealed class ArchiveHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => SendAndAttachRequestAsync(request, cancellationToken);

        private async Task<HttpResponseMessage> SendAndAttachRequestAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = await send(request, cancellationToken);
            response.RequestMessage ??= request;
            return response;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"getit-dependency-setup-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
