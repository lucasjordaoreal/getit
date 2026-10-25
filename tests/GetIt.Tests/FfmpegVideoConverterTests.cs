using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GetIt_App.Services;
using Xunit;
using Xunit.Abstractions;

namespace GetIt.Tests;

public sealed class FfmpegIntegrationFactAttribute : FactAttribute
{
    public FfmpegIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GETIT_FFMPEG_TEST_ENGINE")))
            Skip = "Set GETIT_FFMPEG_TEST_ENGINE to run the local FFmpeg integration checks.";
    }
}

public class FfmpegVideoConverterTests(ITestOutputHelper output)
{
    [FfmpegIntegrationFact]
    public async Task Mov_UsesWorkingHardwareOrCpuAndProducesCompatibleVideoAndAudio()
    {
        using var media = await TestMedia.CreateAsync();
        var updates = new List<VideoConversionProgress>();
        var converter = new FfmpegVideoConverter(media.Ffmpeg);
        var result = await converter.ConvertToMovAsync(media.Source, media.Target, false, new ImmediateProgress(updates.Add));
        var probe = await RunAsync(media.Ffprobe, "-v", "error", "-show_streams", "-of", "json", result);
        using var json = JsonDocument.Parse(probe);
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToList();
        Assert.Contains(streams, stream => stream.GetProperty("codec_name").GetString() == "h264"
            && stream.GetProperty("width").GetInt32() == 256 && stream.GetProperty("height").GetInt32() == 144);
        Assert.Contains(streams, stream => stream.GetProperty("codec_name").GetString() == "aac");
        Assert.Equal(100, updates[^1].Percentage);
        output.WriteLine($"Encoder used for real VP9/Opus → MOV conversion: {updates[^1].Encoder}");
        Assert.True(File.Exists(media.Source));
    }

    [FfmpegIntegrationFact]
    public async Task FailedHardwareAttempt_FallsBackToCpuUsingTheSameDownloadedFile()
    {
        using var media = await TestMedia.CreateAsync();
        var original = SHA256.HashData(await File.ReadAllBytesAsync(media.Source));
        var updates = new List<VideoConversionProgress>();
        var converter = new FfmpegVideoConverter(media.Ffmpeg, ["getit_nonexistent_gpu_encoder"]);
        await converter.ConvertToMovAsync(media.Source, media.Target, false, new ImmediateProgress(updates.Add));
        Assert.Contains(updates, update => update.Encoder == "getit_nonexistent_gpu_encoder");
        Assert.Equal("libx264", updates[^1].Encoder);
        output.WriteLine("Injected GPU encoder failure recovered with libx264; original source SHA256 unchanged.");
        Assert.True(File.Exists(media.Target));
        Assert.Equal(original, SHA256.HashData(await File.ReadAllBytesAsync(media.Source)));
        Assert.Empty(Directory.GetFiles(media.Directory, ".getit-*.mov"));
    }

    [FfmpegIntegrationFact]
    public async Task Cancellation_NeverStartsCpuFallbackAndPreservesSource()
    {
        using var media = await TestMedia.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var updates = new List<VideoConversionProgress>();
        var converter = new FfmpegVideoConverter(media.Ffmpeg, ["getit_nonexistent_gpu_encoder"]);
        var progress = new ImmediateProgress(update => { updates.Add(update); cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.ConvertToMovAsync(media.Source, media.Target, false, progress, cancellation.Token));
        Assert.DoesNotContain(updates, update => update.Encoder == "libx264");
        Assert.False(File.Exists(media.Target));
        Assert.True(File.Exists(media.Source));
    }

    [FfmpegIntegrationFact]
    public async Task ConversionFailure_DoesNotOverwriteExistingDestination()
    {
        using var media = await TestMedia.CreateAsync();
        await File.WriteAllTextAsync(media.Source, "not-a-video");
        await File.WriteAllTextAsync(media.Target, "existing-user-file");
        var converter = new FfmpegVideoConverter(media.Ffmpeg, Array.Empty<string>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => converter.ConvertToMovAsync(media.Source, media.Target, true));
        Assert.Equal("existing-user-file", await File.ReadAllTextAsync(media.Target));
        Assert.Equal("not-a-video", await File.ReadAllTextAsync(media.Source));
    }

    [FfmpegIntegrationFact]
    public async Task SeparateHlsVideoWithoutPacketTimestamps_ConvertsWithAudioOnGpuAndCpuFallback()
    {
        using var media = await TestMedia.CreateAsync();
        var video = Path.Combine(media.Directory, "source.f270.mp4");
        var audio = Path.Combine(media.Directory, "source.f251.webm");
        await RunAsync(media.Ffmpeg, "-hide_banner", "-loglevel", "error", "-i", media.Source,
            "-map", "0:v:0", "-an", "-c:v", "libx264", "-bf", "2", "-f", "mpegts", video);
        await RunAsync(media.Ffmpeg, "-hide_banner", "-loglevel", "error", "-i", media.Source,
            "-map", "0:a:0", "-vn", "-c:a", "copy", audio);
        Assert.True(RemoveTransportTimestamps(video) > 0);
        var originalVideo = SHA256.HashData(await File.ReadAllBytesAsync(video));
        var originalAudio = SHA256.HashData(await File.ReadAllBytesAsync(audio));

        var oldMerge = await RunProcessAsync(media.Ffmpeg, "-hide_banner", "-loglevel", "error",
            "-fflags", "+genpts", "-i", video, "-i", audio, "-map", "0:v:0", "-map", "1:a:0",
            "-c", "copy", Path.Combine(media.Directory, "old-intermediate.mkv"));
        Assert.NotEqual(0, oldMerge.ExitCode);
        Assert.Contains("timestamp", oldMerge.Error, StringComparison.OrdinalIgnoreCase);

        foreach (var forceCpu in new[] { false, true })
        {
            var updates = new List<VideoConversionProgress>();
            var converter = new FfmpegVideoConverter(media.Ffmpeg, forceCpu ? ["getit_nonexistent_gpu_encoder"] : null);
            var destination = Path.Combine(media.Directory, forceCpu ? "separate-cpu.mov" : "separate-gpu.mov");
            await converter.ConvertToMovAsync(video, destination, false, new ImmediateProgress(updates.Add), audioSourcePath: audio);
            var probe = await RunAsync(media.Ffprobe, "-v", "error", "-show_streams", "-of", "json", destination);
            using var document = JsonDocument.Parse(probe);
            var codecs = document.RootElement.GetProperty("streams").EnumerateArray()
                .Select(stream => stream.GetProperty("codec_name").GetString()).ToList();
            Assert.Contains("h264", codecs);
            Assert.Contains("aac", codecs);
            if (forceCpu) Assert.Equal("libx264", updates[^1].Encoder);
            Assert.Equal(100, updates[^1].Percentage);
            Assert.Equal(originalVideo, SHA256.HashData(await File.ReadAllBytesAsync(video)));
            Assert.Equal(originalAudio, SHA256.HashData(await File.ReadAllBytesAsync(audio)));
            output.WriteLine($"Missing-PTS HLS + separate Opus converted with {updates[^1].Encoder}.");
        }
    }

    private static int RemoveTransportTimestamps(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var changed = 0;
        for (var start = 0; start + 188 <= bytes.Length; start += 188)
        {
            if (bytes[start] != 0x47 || (bytes[start + 1] & 0x40) == 0) continue;
            var mode = (bytes[start + 3] & 0x30) >> 4;
            if (mode is not (1 or 3)) continue;
            var position = start + 4;
            if (mode == 3) position += 1 + bytes[position];
            if (position + 9 >= start + 188) continue;
            if (bytes[position] != 0 || bytes[position + 1] != 0 || bytes[position + 2] != 1
                || (bytes[position + 3] & 0xF0) != 0xE0) continue;
            bytes[position + 7] &= 0x3F;
            changed++;
        }
        File.WriteAllBytes(path, bytes);
        return changed;
    }

    private sealed class ImmediateProgress(Action<VideoConversionProgress> handler) : IProgress<VideoConversionProgress>
    {
        public void Report(VideoConversionProgress value) => handler(value);
    }

    private sealed class TestMedia : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "GetIt-conversion-check-" + Guid.NewGuid().ToString("N"));
        public string Ffmpeg { get; } = Path.Combine(Environment.GetEnvironmentVariable("GETIT_FFMPEG_TEST_ENGINE")!, "ffmpeg.exe");
        public string Ffprobe => Path.Combine(Path.GetDirectoryName(Ffmpeg)!, "ffprobe.exe");
        public string Source => Path.Combine(Directory, "source-vp9-opus.mkv");
        public string Target => Path.Combine(Directory, "converted.mov");
        public static async Task<TestMedia> CreateAsync()
        {
            var media = new TestMedia();
            System.IO.Directory.CreateDirectory(media.Directory);
            await RunAsync(media.Ffmpeg, "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=256x144:rate=15",
                "-f", "lavfi", "-i", "sine=frequency=1000:sample_rate=48000", "-t", "0.8", "-c:v", "libvpx-vp9", "-c:a", "libopus", media.Source);
            return media;
        }
        public void Dispose()
        {
            // Exact, randomly created test folder; never touches the user's media folders.
            foreach (var file in System.IO.Directory.GetFiles(Directory)) File.Delete(file);
            System.IO.Directory.Delete(Directory);
        }
    }

    private static async Task<string> RunAsync(string executable, params string[] arguments)
    {
        var result = await RunProcessAsync(executable, arguments);
        Assert.True(result.ExitCode == 0, result.Error);
        return result.Output;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(output, error, process.WaitForExitAsync());
        return (process.ExitCode, output.Result, error.Result);
    }
}
