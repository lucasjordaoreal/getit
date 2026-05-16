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
    Task<string?> DownloadAsync(string url, string formatId, bool isAudioOnly, string audioBitrate, string videoExt, string downloadDir, IProgress<DownloadProgressInfo> progress, CancellationToken cancellationToken = default);
}

public partial class DownloadService : IDownloadService
{
    [GeneratedRegex(@"\[download\]\s+(?<percent>\d+\.\d+)%\s+of\s+.*?\s+at\s+(?<speed>.*?)\s+ETA\s+(?<eta>.*)")]
    private static partial Regex ProgressDetailedRegex();

    [GeneratedRegex(@"\[download\]\s+(?<percent>\d+\.\d+)%")]
    private static partial Regex ProgressSimpleRegex();

    [GeneratedRegex(@"Destination:\s+(?<path>.+)")]
    private static partial Regex DestinationRegex();

    [GeneratedRegex(@"Merging formats into\s+""(?<path>.+)""")]
    private static partial Regex MergerRegex();

    [GeneratedRegex(@"^([\d\.]+)(KiB/s|MiB/s|GiB/s|B/s)$")]
    private static partial Regex SpeedUnitRegex();

    private static string ConvertSpeedToDecimalPrefix(string speedStr)
    {
        var match = SpeedUnitRegex().Match(speedStr);
        if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out double val))
        {
            string unit = match.Groups[2].Value;
            double bytesPerSecond = val;
            if (unit == "KiB/s") bytesPerSecond = val * 1024;
            else if (unit == "MiB/s") bytesPerSecond = val * 1048576;
            else if (unit == "GiB/s") bytesPerSecond = val * 1073741824;

            if (bytesPerSecond >= 1000000000)
                return $"{(bytesPerSecond / 1000000000):F2} GB/s";
            if (bytesPerSecond >= 1000000)
                return $"{(bytesPerSecond / 1000000):F2} MB/s";
            if (bytesPerSecond >= 1000)
                return $"{(bytesPerSecond / 1000):F2} KB/s";
            
            return $"{bytesPerSecond:F0} B/s";
        }
        return speedStr;
    }

    private static string? _cachedGpuEncoder;

    private static async Task<string?> GetGpuEncoderAsync()
    {
        if (_cachedGpuEncoder != null) return _cachedGpuEncoder;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                output = output.ToLowerInvariant();
                
                if (output.Contains("nvidia"))
                    _cachedGpuEncoder = "h264_nvenc";
                else if (output.Contains("amd") || output.Contains("radeon"))
                    _cachedGpuEncoder = "h264_amf";
                else if (output.Contains("intel"))
                    _cachedGpuEncoder = "h264_qsv";
                else
                    _cachedGpuEncoder = "none";
            }
        }
        catch
        {
            _cachedGpuEncoder = "none";
        }

        return _cachedGpuEncoder;
    }

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
        var processStartInfo = CreateProcessStartInfo($"--no-warnings --encoding UTF-8 -J \"{url}\"");

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

    public async Task<string?> DownloadAsync(string url, string formatId, bool isAudioOnly, string audioBitrate, string videoExt, string downloadDir, IProgress<DownloadProgressInfo> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(downloadDir);

        string formatArg;
        string postprocessorArgs = string.Empty;
        var gpuEncoder = await GetGpuEncoderAsync();
        bool useGpu = gpuEncoder != "none" && !string.IsNullOrEmpty(gpuEncoder);

        if (isAudioOnly)
        {
            formatArg = $"-f \"bestaudio[format_note*=original]/bestaudio/best\" --extract-audio --audio-format mp3 --audio-quality {audioBitrate}K";
        }
        else
        {
            string ext = string.IsNullOrEmpty(videoExt) ? "mp4" : videoExt;
            string formatSelector = string.IsNullOrEmpty(formatId) 
                ? "\"bestvideo+bestaudio[format_note*=original]/bestvideo+bestaudio/best\"" 
                : $"\"{formatId}+bestaudio[format_note*=original]/{formatId}+bestaudio/best\"";
            
            if (ext.Equals("mov", StringComparison.OrdinalIgnoreCase))
            {
                formatArg = $"-f {formatSelector} --recode-video {ext}";
                if (useGpu)
                {
                    postprocessorArgs = $"--postprocessor-args \"VideoConvertor:-c:v {gpuEncoder}\"";
                }
            }
            else
            {
                formatArg = $"-f {formatSelector} --merge-output-format {ext} --remux-video {ext}";
            }
        }

        var arguments = $"--ffmpeg-location \"{_ffmpegPath}\" --encoding UTF-8 {formatArg} {postprocessorArgs} --newline -o \"{Path.Combine(downloadDir, "%(title)s.%(ext)s")}\" \"{url}\"";
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
            string currentStatus = "Iniciando download...";
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                var detailedMatch = ProgressDetailedRegex().Match(line);
                if (detailedMatch.Success && double.TryParse(detailedMatch.Groups["percent"].Value, System.Globalization.CultureInfo.InvariantCulture, out double p1))
                {
                    currentStatus = "Download em andamento";
                    string speed = ConvertSpeedToDecimalPrefix(detailedMatch.Groups["speed"].Value.Trim());
                    string eta = detailedMatch.Groups["eta"].Value.Trim();

                    if (eta.Contains('('))
                    {
                        eta = eta.Split('(')[0].Trim();
                    }

                    progress?.Report(new DownloadProgressInfo
                    {
                        Percentage = p1,
                        Speed = speed,
                        Eta = eta,
                        Status = currentStatus
                    });
                }
                else
                {
                    var simpleMatch = ProgressSimpleRegex().Match(line);
                    if (simpleMatch.Success && double.TryParse(simpleMatch.Groups["percent"].Value, System.Globalization.CultureInfo.InvariantCulture, out double p2))
                    {
                        currentStatus = "Download em andamento";
                        progress?.Report(new DownloadProgressInfo
                        {
                            Percentage = p2,
                            Speed = string.Empty,
                            Eta = string.Empty,
                            Status = currentStatus
                        });
                    }
                    else if (line.Contains("[ExtractAudio]") || line.Contains("Extracting audio"))
                    {
                        currentStatus = "Convertendo para MP3";
                        progress?.Report(new DownloadProgressInfo { Percentage = 100, Speed = string.Empty, Eta = string.Empty, Status = currentStatus });
                    }
                    else if (line.Contains("[VideoConvertor] Converting video from"))
                    {
                        string ext = string.IsNullOrEmpty(videoExt) ? "MP4" : videoExt.ToUpper();
                        currentStatus = $"Convertendo para {ext}";
                        progress?.Report(new DownloadProgressInfo { Percentage = 100, Speed = string.Empty, Eta = string.Empty, Status = currentStatus });
                    }
                    else if (line.Contains("[Merger] Merging formats into"))
                    {
                        currentStatus = "Mesclando arquivos...";
                        progress?.Report(new DownloadProgressInfo { Percentage = 100, Speed = string.Empty, Eta = string.Empty, Status = currentStatus });
                    }
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

