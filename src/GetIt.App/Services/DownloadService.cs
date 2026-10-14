using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using GetIt_App.Models;

namespace GetIt_App.Services;

public interface IDownloadService
{
    Task<VideoMetadata?> FetchMetadataAsync(string url, CancellationToken cancellationToken = default);
    Task<string?> DownloadAsync(string url, string formatId, bool isAudioOnly, string audioBitrate, string videoExt, string downloadDir, IProgress<DownloadProgressInfo> progress, CancellationToken cancellationToken = default, bool overwriteExistingFiles = false, string? titleHint = null);
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

    private static string GetSafeExecutablePath(string candidateDirectory, string executableName)
    {
        if (string.IsNullOrWhiteSpace(candidateDirectory))
        {
            return string.Empty;
        }

        try
        {
            var fullDirectory = Path.GetFullPath(candidateDirectory);
            var candidatePath = Path.Combine(fullDirectory, executableName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, $"Failed to resolve executable candidate for {executableName}");
        }

        return string.Empty;
    }

    private static bool IsExecutableAvailable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            return File.Exists(executablePath);
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, $"Failed to check existence of executable: {executablePath}");
            return false;
        }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var cleaned = path.Trim();
        if (cleaned.StartsWith('"') && cleaned.EndsWith('"') && cleaned.Length >= 2)
            cleaned = cleaned[1..^1];

        return cleaned;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "download";

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        sanitized = string.Join(" ", sanitized.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));

        return string.IsNullOrWhiteSpace(sanitized) ? "download" : sanitized;
    }

    private static string GetFallbackTargetPath(string downloadDir, string extension, string? titleHint)
    {
        var targetDir = Path.GetFullPath(downloadDir);
        var safeTitle = SanitizeFileName(titleHint ?? "download");
        var safeExt = string.IsNullOrWhiteSpace(extension) ? "mp4" : extension.TrimStart('.');
        return Path.Combine(targetDir, $"{safeTitle}.{safeExt}");
    }

    private static void ValidateDownloadDirectory(string downloadDir)
    {
        if (string.IsNullOrWhiteSpace(downloadDir))
            throw new DirectoryNotFoundException("A pasta de destino não foi informada.");

        var fullPath = Path.GetFullPath(downloadDir);
        if (File.Exists(fullPath))
            throw new IOException($"O caminho de destino '{fullPath}' é um arquivo e não uma pasta.");

        Directory.CreateDirectory(fullPath);

        var probePath = Path.Combine(fullPath, $".{AppInfo.ProductName}_write_test_{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probePath, "write-test");
        }
        catch (Exception ex)
        {
            throw new IOException($"Não foi possível gravar na pasta de destino '{fullPath}'.", ex);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    private static string? ResolveExistingFilePath(string? detectedPath, string downloadDir, string extension, string? titleHint)
    {
        var normalized = NormalizePath(detectedPath);
        if (!string.IsNullOrWhiteSpace(normalized) && File.Exists(normalized))
            return normalized;

        if (string.IsNullOrWhiteSpace(normalized))
        {
            var fallbackPath = GetFallbackTargetPath(downloadDir, extension, titleHint);
            if (File.Exists(fallbackPath))
                return fallbackPath;

            var files = Directory.Exists(downloadDir)
                ? Directory.GetFiles(downloadDir)
                : Array.Empty<string>();

            var candidates = files
                .Where(file => string.Equals(Path.GetExtension(file), $".{extension.TrimStart('.')}", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => File.GetLastWriteTimeUtc(file))
                .ToList();

            return candidates.FirstOrDefault();
        }

        return null;
    }

    public static string ResolveBinPath()
    {
        var envPath = Environment.GetEnvironmentVariable("GETIT_ENGINE_BIN");
        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath) && File.Exists(Path.Combine(envPath, "yt-dlp.exe")))
        {
            return envPath;
        }

        var baseDir = AppContext.BaseDirectory;
        var directEngineBin = Path.Combine(baseDir, "Engine", "Bin");
        if (Directory.Exists(directEngineBin) && File.Exists(Path.Combine(directEngineBin, "yt-dlp.exe")))
        {
            return directEngineBin;
        }

        var directBin = Path.Combine(baseDir, "Bin");
        if (Directory.Exists(directBin) && File.Exists(Path.Combine(directBin, "yt-dlp.exe")))
        {
            return directBin;
        }

        if (File.Exists(Path.Combine(baseDir, "yt-dlp.exe")))
        {
            return baseDir;
        }

        var dirInfo = new DirectoryInfo(baseDir);
        while (dirInfo != null)
        {
            var candidate = Path.Combine(dirInfo.FullName, "Engine", "Bin");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "yt-dlp.exe")))
            {
                return candidate;
            }
            dirInfo = dirInfo.Parent;
        }

        var cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), "Engine", "Bin");
        if (Directory.Exists(cwdCandidate) && File.Exists(Path.Combine(cwdCandidate, "yt-dlp.exe")))
        {
            return cwdCandidate;
        }

        return Path.Combine(baseDir, "Engine", "Bin");
    }

    public DownloadService()
    {
        var resolvedBin = ResolveBinPath();
        var ytdlpPath = GetSafeExecutablePath(resolvedBin, "yt-dlp.exe");
        var ffmpegPath = GetSafeExecutablePath(resolvedBin, "ffmpeg.exe");

        if (!IsExecutableAvailable(ytdlpPath))
        {
            throw new FileNotFoundException("yt-dlp.exe não foi encontrado. Verifique a pasta de engine do aplicativo.", "yt-dlp.exe");
        }

        if (!IsExecutableAvailable(ffmpegPath))
        {
            throw new FileNotFoundException("ffmpeg.exe não foi encontrado. Verifique a pasta de engine do aplicativo.", "ffmpeg.exe");
        }

        _binPath = resolvedBin;
        _ytdlpPath = ytdlpPath;
        _ffmpegPath = ffmpegPath;
    }

    private ProcessStartInfo CreateProcessStartInfo(string arguments)
    {
        if (!IsExecutableAvailable(_ytdlpPath))
        {
            throw new FileNotFoundException("O executável yt-dlp.exe não está disponível para iniciar o download.", _ytdlpPath);
        }

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
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        psi.Environment["PATH"] = string.IsNullOrWhiteSpace(_binPath)
            ? currentPath
            : _binPath + ";" + currentPath;
        return psi;
    }

    public async Task<VideoMetadata?> FetchMetadataAsync(string url, CancellationToken cancellationToken = default)
    {
        var processStartInfo = CreateProcessStartInfo($"--force-ipv4 --no-warnings --encoding UTF-8 -J \"{url}\"");

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        var readOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var readErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await Task.WhenAll(readOutTask, readErrTask);
        await process.WaitForExitAsync(cancellationToken);

        var jsonOutput = readOutTask.Result;
        var errOutput = readErrTask.Result;

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(jsonOutput))
        {
            string errorMessage = string.IsNullOrWhiteSpace(errOutput) ? "Falha ao buscar metadados. Verifique a URL." : $"Falha ao buscar metadados (ExitCode {process.ExitCode}): {errOutput.Trim()}";
            throw new Exception(errorMessage);
        }

        try
        {
            return JsonSerializer.Deserialize<VideoMetadata>(jsonOutput, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new Exception($"Falha ao analisar metadados: {ex.Message}");
        }
    }

    public async Task<string?> DownloadAsync(string url, string formatId, bool isAudioOnly, string audioBitrate, string videoExt, string downloadDir, IProgress<DownloadProgressInfo> progress, CancellationToken cancellationToken = default, bool overwriteExistingFiles = false, string? titleHint = null)
    {
        ValidateDownloadDirectory(downloadDir);

        string targetExtension = isAudioOnly ? "mp3" : (string.IsNullOrWhiteSpace(videoExt) ? "mp4" : videoExt.TrimStart('.'));
        string expectedPath = GetFallbackTargetPath(downloadDir, targetExtension, titleHint);

        if (File.Exists(expectedPath) && !overwriteExistingFiles)
        {
            throw new IOException($"Arquivo já existe e não será sobrescrito: {expectedPath}");
        }

        if (overwriteExistingFiles && File.Exists(expectedPath))
        {
            File.Delete(expectedPath);
        }

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

        var arguments = $"--force-ipv4 --ffmpeg-location \"{_ffmpegPath}\" --encoding UTF-8 {formatArg} {postprocessorArgs} --newline -o \"{Path.Combine(downloadDir, "%(title)s.%(ext)s")}\" \"{url}\"";
        var processStartInfo = CreateProcessStartInfo(arguments);

        using var process = new Process { StartInfo = processStartInfo };

        await using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "Failed to cancel download process");
            }
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
                    finalPath = NormalizePath(destMatch.Groups["path"].Value);
                }

                var mergerMatch = MergerRegex().Match(line);
                if (mergerMatch.Success)
                {
                    finalPath = NormalizePath(mergerMatch.Groups["path"].Value);
                }
            }
        }, cancellationToken);

        var readErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await readOutputTask;
        var errorOutput = await readErrorTask;
        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            var errorMessage = string.IsNullOrWhiteSpace(errorOutput)
                ? $"O processo de download terminou com código {process.ExitCode}."
                : $"Erro (Code {process.ExitCode}): {errorOutput.Trim()}";
            throw new InvalidOperationException(errorMessage);
        }

        var resolvedPath = ResolveExistingFilePath(finalPath, downloadDir, targetExtension, titleHint);
        if (!string.IsNullOrWhiteSpace(resolvedPath))
            return resolvedPath;

        if (!string.IsNullOrWhiteSpace(finalPath) && File.Exists(finalPath))
            return finalPath;

        var fallback = GetFallbackTargetPath(downloadDir, targetExtension, titleHint);
        if (File.Exists(fallback))
            return fallback;

        throw new FileNotFoundException($"Não foi possível localizar o arquivo final baixado em '{downloadDir}'.", downloadDir);
    }
}
