using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using GetIt_App.Models;

namespace GetIt_App.Services;

public interface IDownloadService
{
    Task<VideoMetadata?> FetchMetadataAsync(string url, CancellationToken cancellationToken = default);
    Task<string?> DownloadAsync(string url, string formatId, bool isAudioOnly, string audioBitrate, string videoExt, string downloadDir, IProgress<double> progress, CancellationToken cancellationToken = default);
}

public partial class DownloadService : IDownloadService
{
    [GeneratedRegex(@"\[download\]\s+(?<percent>\d+\.\d+)%")]
    private static partial Regex ProgressRegex();

    [GeneratedRegex(@"Destination:\s+(?<path>.+)")]
    private static partial Regex DestinationRegex();

    [GeneratedRegex(@"Merging formats into\s+""(?<path>.+)""")]
    private static partial Regex MergerRegex();

    private readonly string _binPath;
    private readonly string _ytdlpPath;
    private readonly string _ffmpegPath;

    public DownloadService()
    {
        _binPath = @"D:\UltraDownloader\Engine\Bin";
        
        _ytdlpPath = Path.Combine(_binPath, "yt-dlp.exe");
        _ffmpegPath = Path.Combine(_binPath, "ffmpeg.exe");

        if (!File.Exists(_ytdlpPath)) 
            throw new FileNotFoundException($"yt-dlp.exe não encontrado em: {_ytdlpPath}");
    }

    private ProcessStartInfo CreateProcessStartInfo(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ytdlpPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = _binPath,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        // Force yt-dlp to output UTF-8
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        // Add bin path to PATH so yt-dlp can find node.exe or deno.exe
        psi.Environment["PATH"] = _binPath + ";" + Environment.GetEnvironmentVariable("PATH");
        return psi;
    }

    public async Task<VideoMetadata?> FetchMetadataAsync(string url, CancellationToken cancellationToken = default)
    {
        var processStartInfo = CreateProcessStartInfo($"--no-warnings -J \"{url}\"");

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        var jsonOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(jsonOutput))
        {
            throw new Exception("Falha ao buscar metadados. Verifique a URL.");
        }

        return JsonSerializer.Deserialize<VideoMetadata>(jsonOutput, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task<string?> DownloadAsync(string url, string formatId, bool isAudioOnly, string audioBitrate, string videoExt, string downloadDir, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(downloadDir);

        string formatArg;
        if (isAudioOnly)
        {
            formatArg = $"-f bestaudio --extract-audio --audio-format mp3 --audio-quality {audioBitrate}K";
        }
        else
        {
            string ext = string.IsNullOrEmpty(videoExt) ? "mp4" : videoExt;
            string formatSelector = string.IsNullOrEmpty(formatId) ? "\"bestvideo+bestaudio/best\"" : $"\"{formatId}+bestaudio/best\"";
            formatArg = $"-f {formatSelector} --merge-output-format {ext} --remux-video {ext}";
        }

        var arguments = $"--ffmpeg-location \"{_ffmpegPath}\" {formatArg} --newline -o \"{Path.Combine(downloadDir, "%(title)s.%(ext)s")}\" \"{url}\"";
        var processStartInfo = CreateProcessStartInfo(arguments);

        using var process = new Process { StartInfo = processStartInfo };

        await using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
        });

        process.Start();

        string? finalPath = null;

        var readOutputTask = Task.Run(async () =>
        {
            using var reader = process.StandardOutput;
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                var progMatch = ProgressRegex().Match(line);
                if (progMatch.Success && double.TryParse(progMatch.Groups["percent"].Value, System.Globalization.CultureInfo.InvariantCulture, out double percentValue))
                {
                    progress?.Report(percentValue);
                }

                var destMatch = DestinationRegex().Match(line);
                if (destMatch.Success)
                {
                    finalPath = destMatch.Groups["path"].Value.Trim();
                }

                var mergerMatch = MergerRegex().Match(line);
                if (mergerMatch.Success)
                {
                    finalPath = mergerMatch.Groups["path"].Value.Trim();
                }
            }
        }, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await readOutputTask;
        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new Exception($"Erro (Code {process.ExitCode}): {err}");
        }

        return finalPath;
    }
}

