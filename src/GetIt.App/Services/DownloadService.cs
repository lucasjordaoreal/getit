using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
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
    Task<string?> DownloadAsync(string url, string formatId, bool isAudioOnly, string audioFormat, string audioBitrate, string videoExt, string downloadDir, IProgress<DownloadProgressInfo> progress, CancellationToken cancellationToken = default, bool overwriteExistingFiles = false, string? titleHint = null, bool saveAsNewCopy = false);
}

public sealed class DownloadFileConflictException(string path)
    : IOException($"Arquivo já existe e não será sobrescrito: {path}")
{
    public string Path { get; } = path;
}

public interface IDownloadEngineHealthProvider
{
    Task<string?> GetEngineProblemAsync(CancellationToken cancellationToken = default);
}

public partial class DownloadService : IDownloadService, IDownloadEngineHealthProvider
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

    private static readonly SemaphoreSlim MetadataSlots = new(3, 3);
    private const string FinalPathMarker = "GETIT_FINAL_PATH:";
    private const string PostProcessingMarker = "GETIT_POSTPROCESSING:";

    private static void TerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { /* The process has not started or has already exited. */ }
        catch (Exception ex) { AppLogger.LogException(ex, "Failed to stop the media process tree"); }
    }

    private static async Task StopProcessAsync(Process process)
    {
        TerminateProcess(process);
        try
        {
            using var waitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(waitTimeout.Token);
        }
        catch (InvalidOperationException) { }
        catch (OperationCanceledException) { AppLogger.LogWarning("Media process did not exit within the cancellation timeout."); }
    }

    private static string EngineBinPath => ResolveBinPath();
    private static string FfmpegPath => Path.Combine(EngineBinPath, "ffmpeg.exe");

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

    private sealed record MovInputs(string Video, string? Audio, bool HasEmbeddedAudio);

    private async Task<MovInputs> ResolveMovInputsAsync(IEnumerable<string> candidates,
        string sourceDirectory, CancellationToken cancellationToken)
    {
        var paths = DownloadOutputResolver.GetProducedFiles(candidates, sourceDirectory);
        var videoCandidates = new List<(string Path, bool HasAudio)>();
        var audioCandidates = new List<string>();
        foreach (var path in paths)
        {
            var tracks = await ReadMediaTracksAsync(path, cancellationToken);
            if (tracks.Video) videoCandidates.Add((path, tracks.Audio));
            else if (tracks.Audio) audioCandidates.Add(path);
        }

        if (videoCandidates.Count == 0)
            throw new FileNotFoundException("Não foi possível localizar a faixa de vídeo para conversão.", sourceDirectory);
        if (videoCandidates.Count > 1)
            throw new InvalidDataException("Mais de uma faixa de vídeo foi produzida pela mesma operação de download.");
        if (audioCandidates.Count > 1)
            throw new InvalidDataException("Mais de uma faixa de áudio foi produzida pela mesma operação de download.");

        var video = videoCandidates[0];
        return new MovInputs(video.Path, audioCandidates.FirstOrDefault(), video.HasAudio);
    }

    private async Task<string> ConvertMovInputsAsync(MovInputs inputs, IEnumerable<string> downloadedPaths,
        string sourceDirectory, string downloadDir, string? titleHint, bool overwriteExistingFiles, bool saveAsNewCopy,
        IProgress<DownloadProgressInfo>? progress, CancellationToken cancellationToken)
    {
        var targetName = !string.IsNullOrWhiteSpace(titleHint)
            ? SanitizeFileName(titleHint)
            : Regex.Replace(Path.GetFileNameWithoutExtension(inputs.Video), @"\.f[^.]+$", string.Empty);
        targetName = targetName.TrimEnd(' ', '.');
        if (targetName.Length > 160)
        {
            var length = char.IsHighSurrogate(targetName[159]) ? 159 : 160;
            targetName = targetName[..length].TrimEnd(' ', '.');
        }
        if (string.IsNullOrWhiteSpace(targetName)) targetName = "download";
        var deviceName = targetName.Split('.')[0].ToUpperInvariant();
        if (deviceName is "CON" or "PRN" or "AUX" or "NUL"
            || (deviceName.Length == 4 && (deviceName.StartsWith("COM", StringComparison.Ordinal) || deviceName.StartsWith("LPT", StringComparison.Ordinal))
                && deviceName[3] is >= '1' and <= '9')) targetName = "_" + targetName;
        var target = Path.Combine(downloadDir, targetName + ".mov");
        var converter = new FfmpegVideoConverter(FfmpegPath);
        var conversionProgress = new Progress<VideoConversionProgress>(update => progress?.Report(new DownloadProgressInfo
        {
            Percentage = update.Percentage,
            Status = update.Status,
            Speed = string.Empty,
            Eta = string.Empty
        }));
        var converted = await converter.ConvertToMovAsync(inputs.Video, target, overwriteExistingFiles, conversionProgress, cancellationToken,
            audioSourcePath: inputs.Audio, saveAsNewCopy: saveAsNewCopy);
        try
        {
            var ownedDirectory = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var source in downloadedPaths.Append(inputs.Video).Append(inputs.Audio ?? inputs.Video).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Path.GetFullPath(source).StartsWith(ownedDirectory, StringComparison.OrdinalIgnoreCase) && File.Exists(source))
                    File.Delete(source);
            }
            if (!Directory.EnumerateFileSystemEntries(sourceDirectory).Any()) Directory.Delete(sourceDirectory);
        }
        catch (IOException ex) { AppLogger.LogWarning($"Intermediate cleanup deferred: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { AppLogger.LogWarning($"Intermediate cleanup deferred: {ex.Message}"); }
        return converted;
    }

    private sealed class TemporaryDownloadDirectory : IDisposable
    {
        public string Path { get; }

        public TemporaryDownloadDirectory(string downloadDirectory)
        {
            Path = System.IO.Path.Combine(downloadDirectory, ".getit-work", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }

                var workDirectory = Directory.GetParent(Path);
                if (workDirectory is not null && !Directory.EnumerateFileSystemEntries(workDirectory.FullName).Any())
                {
                    Directory.Delete(workDirectory.FullName);
                }
            }
            catch (IOException ex)
            {
                AppLogger.LogWarning($"Temporary download cleanup deferred: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.LogWarning($"Temporary download cleanup deferred: {ex.Message}");
            }
        }
    }

    private async Task<(bool Video, bool Audio)> ReadMediaTracksAsync(string path, CancellationToken cancellationToken)
    {
        var ffprobe = Path.Combine(EngineBinPath, "ffprobe.exe");
        var start = new ProcessStartInfo(ffprobe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-v", "error", "-show_entries", "stream=codec_type", "-of", "json", path })
            start.ArgumentList.Add(argument);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = new Process { StartInfo = start };
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            process.Start();
            using var registration = timeout.Token.Register(() => TerminateProcess(process));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token));
            if (process.ExitCode != 0) throw new InvalidOperationException($"Não foi possível identificar as faixas de mídia: {error.Result.Trim()}");
            using var document = JsonDocument.Parse(output.Result);
            var types = document.RootElement.GetProperty("streams").EnumerateArray()
                .Select(stream => stream.GetProperty("codec_type").GetString()).ToArray();
            return (types.Contains("video"), types.Contains("audio"));
        }
        catch (OperationCanceledException)
        {
            await StopProcessAsync(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("A identificação das faixas demorou demais. Tente novamente.");
        }
        catch
        {
            await StopProcessAsync(process);
            throw;
        }
    }

    public static string ResolveBinPath()
    {
        var envPath = Environment.GetEnvironmentVariable("GETIT_ENGINE_BIN");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            try
            {
                return Path.GetFullPath(envPath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                AppLogger.LogException(ex, "GETIT_ENGINE_BIN is not a valid path");
            }
        }

        var baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDir, "Engine", "Bin"),
            Path.Combine(baseDir, "Bin"),
            baseDir
        };
        var dirInfo = new DirectoryInfo(baseDir);
        while (dirInfo != null)
        {
            candidates.Add(Path.Combine(dirInfo.FullName, "Engine", "Bin"));
            dirInfo = dirInfo.Parent;
        }

        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "Engine", "Bin"));
        var availableDirectory = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((path, index) => new
            {
                Path = path,
                Index = index,
                Score = Directory.Exists(path) ? GetEngineComponentScore(path) : -1
            })
            .Where(candidate => candidate.Score >= 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Index)
            .FirstOrDefault();
        if (availableDirectory is not null)
        {
            return availableDirectory.Path;
        }

        return Path.Combine(baseDir, "Engine", "Bin");
    }

    public DownloadService()
    {
    }

    private static int GetEngineComponentScore(string directory)
    {
        var score = 0;
        foreach (var component in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe" })
        {
            if (File.Exists(Path.Combine(directory, component)))
            {
                score += component == "yt-dlp.exe" ? 5 : 2;
            }
        }

        return score;
    }

    public async Task<string?> GetEngineProblemAsync(CancellationToken cancellationToken = default)
    {
        var issues = await CheckEngineAsync(
            includeFfmpeg: true,
            includeFfprobe: true,
            cancellationToken: cancellationToken);
        return issues.Count == 0 ? null : FormatEngineIssues(issues);
    }

    private async Task EnsureEngineAvailableAsync(bool includeFfprobe, CancellationToken cancellationToken)
    {
        var issues = await CheckEngineAsync(
            includeFfmpeg: true,
            includeFfprobe: includeFfprobe,
            cancellationToken: cancellationToken);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(FormatEngineIssues(issues));
        }
    }

    private static async Task<IReadOnlyList<DownloadEngineIssue>> CheckEngineAsync(
        bool includeFfmpeg,
        bool includeFfprobe,
        CancellationToken cancellationToken)
    {
        var binPath = EngineBinPath;
        var components = new List<(string Name, string[] Arguments)> { ("yt-dlp.exe", ["--version"]) };
        if (includeFfmpeg)
        {
            components.Add(("ffmpeg.exe", ["-version"]));
        }
        if (includeFfprobe)
        {
            components.Add(("ffprobe.exe", ["-version"]));
        }

        var issues = new List<DownloadEngineIssue>();
        foreach (var (name, arguments) in components)
        {
            var issue = await DownloadEngineHealthCheck.CheckExecutableAsync(
                name,
                Path.Combine(binPath, name),
                arguments,
                cancellationToken);
            if (issue is not null)
            {
                issues.Add(issue);
            }
        }

        return issues;
    }

    private static string FormatEngineIssues(IReadOnlyList<DownloadEngineIssue> issues)
    {
        if (issues.Count == 0)
        {
            return string.Empty;
        }

        var details = string.Join(Environment.NewLine, issues.Select(issue =>
            $"{issue.Component}: {issue.Reason} ({issue.Path})"));
        return $"A engine de download está indisponível:{Environment.NewLine}{details}{Environment.NewLine}" +
            $"Coloque os executáveis em Engine\\Bin na pasta do aplicativo ou configure GETIT_ENGINE_BIN. " +
            "Depois, tente novamente; não é necessário reinstalar ou reiniciar o GetIt.";
    }

    private ProcessStartInfo CreateProcessStartInfo(IEnumerable<string> arguments)
    {
        var binPath = EngineBinPath;
        var ytdlpPath = Path.Combine(binPath, "yt-dlp.exe");

        var psi = new ProcessStartInfo
        {
            FileName = ytdlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = binPath,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        // Force yt-dlp to output UTF-8
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        // Add bin path to PATH so yt-dlp can find node.exe or deno.exe
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        psi.Environment["PATH"] = binPath + ";" + currentPath;
        return psi;
    }

    public async Task<VideoMetadata?> FetchMetadataAsync(string url, CancellationToken cancellationToken = default)
    {
        // Limit metadata subprocesses when several links are added together.
        await MetadataSlots.WaitAsync(cancellationToken);
        try
        {
            var issues = await CheckEngineAsync(
                includeFfmpeg: false,
                includeFfprobe: false,
                cancellationToken: cancellationToken);
            if (issues.Count > 0)
            {
                throw new InvalidOperationException(FormatEngineIssues(issues));
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            var processStartInfo = CreateProcessStartInfo(new[]
            {
                "--ignore-config", "--force-ipv4", "--no-playlist", "--playlist-items", "1",
                "--no-warnings", "--socket-timeout", "20", "--retries", "2", "--extractor-retries", "1",
                "--encoding", "UTF-8", "-J", "--", url
            });
            using var process = new Process { StartInfo = processStartInfo };
            try
            {
                timeout.Token.ThrowIfCancellationRequested();
                process.Start();
                using var registration = timeout.Token.Register(() => TerminateProcess(process));
                var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
                await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(timeout.Token));
                var jsonOutput = outputTask.Result;
                var errorOutput = errorTask.Result;
                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(jsonOutput))
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorOutput)
                        ? "Não foi possível analisar o link. Verifique o endereço e tente novamente."
                        : errorOutput.Trim());

                using var document = JsonDocument.Parse(jsonOutput);
                if (document.RootElement.TryGetProperty("_type", out var type) && type.GetString() is "playlist" or "multi_video")
                    throw new InvalidOperationException("Adicione o link de um vídeo individual. Listas de reprodução não são adicionadas como um único item.");
                return document.RootElement.Deserialize<VideoMetadata>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("O site não retornou informações desta mídia.");
            }
            catch (OperationCanceledException)
            {
                await StopProcessAsync(process);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("A análise demorou demais. Verifique sua conexão e tente novamente.");
            }
            catch
            {
                await StopProcessAsync(process);
                throw;
            }
        }
        finally { MetadataSlots.Release(); }
    }

    public async Task<string?> DownloadAsync(string url, string formatId, bool isAudioOnly, string audioFormat, string audioBitrate, string videoExt, string downloadDir, IProgress<DownloadProgressInfo> progress, CancellationToken cancellationToken = default, bool overwriteExistingFiles = false, string? titleHint = null, bool saveAsNewCopy = false)
    {
        string normalizedAudioFormat = audioFormat.Trim().ToLowerInvariant();
        if (isAudioOnly && normalizedAudioFormat is not ("mp3" or "wav" or "flac"))
        {
            throw new ArgumentOutOfRangeException(nameof(audioFormat), audioFormat, "Formato de áudio não suportado.");
        }

        var targetExtension = isAudioOnly ? normalizedAudioFormat : (string.IsNullOrWhiteSpace(videoExt) ? "mp4" : videoExt.TrimStart('.'));
        var convertMov = !isAudioOnly && targetExtension.Equals("mov", StringComparison.OrdinalIgnoreCase);
        await EnsureEngineAvailableAsync(convertMov, cancellationToken);

        ValidateDownloadDirectory(downloadDir);
        downloadDir = Path.GetFullPath(downloadDir);

        string expectedPath = GetFallbackTargetPath(downloadDir, targetExtension, titleHint);

        if (File.Exists(expectedPath) && !overwriteExistingFiles && !saveAsNewCopy)
            throw new DownloadFileConflictException(expectedPath);

        using var operationDirectory = new TemporaryDownloadDirectory(downloadDir);
        var sourceDirectory = operationDirectory.Path;

        var arguments = new List<string>
        {
            "--ignore-config", "--force-ipv4", "--no-playlist", "--playlist-items", "1",
            "--socket-timeout", "30", "--retries", "3", "--fragment-retries", "3",
            "--ffmpeg-location", FfmpegPath, "--encoding", "UTF-8", "--windows-filenames", "--trim-filenames", "180"
        };
        if (isAudioOnly)
        {
            arguments.AddRange(new[] { "-f", "bestaudio[format_note*=original]/bestaudio/best", "--extract-audio", "--audio-format", normalizedAudioFormat });
            if (normalizedAudioFormat == "mp3") arguments.AddRange(new[] { "--audio-quality", audioBitrate + "K" });
        }
        else
        {
            var ext = string.IsNullOrEmpty(videoExt) ? "mp4" : videoExt;
            if (ext.Equals("mov", StringComparison.OrdinalIgnoreCase))
            {
                // Comma selects independent downloads, not a '+' merge. Some HLS/TS packets have no
                // timestamps and cannot be stream-copied to MKV; the video encoder handles them directly.
                var videoSelector = string.IsNullOrEmpty(formatId) || formatId == "best" ? "bestvideo/best" : formatId;
                arguments.AddRange(new[] { "-f", $"({videoSelector}),(bestaudio[format_note*=original]/bestaudio/best)", "--fixup", "never" });
            }
            else
            {
                var formatSelector = string.IsNullOrEmpty(formatId)
                    ? "bestvideo+bestaudio[format_note*=original]/bestvideo+bestaudio/best"
                    : $"{formatId}+bestaudio[format_note*=original]/{formatId}+bestaudio/best";
                arguments.AddRange(new[] { "-f", formatSelector, "--merge-output-format", ext, "--remux-video", ext });
            }
        }
        arguments.AddRange(new[]
        {
            "--no-simulate", "--progress", "--newline", "--progress-delta", "0.2",
            "--print", $"post_process:{PostProcessingMarker}%(id)s",
            "--print", $"after_move:{FinalPathMarker}%(filepath)s",
            "-o", Path.Combine(sourceDirectory, convertMov ? "%(title)s.f%(format_id)s.%(ext)s" : "%(title)s.%(ext)s"), "--", url
        });
        using var process = new Process { StartInfo = CreateProcessStartInfo(arguments) };
        cancellationToken.ThrowIfCancellationRequested();
        process.Start();
        using var registration = cancellationToken.Register(() => TerminateProcess(process));

        string? finalPath = null;

        string? authoritativePath = null;
        var downloadedPaths = new List<string>();
        var errorOutput = new StringBuilder();
        string? lastError = null;
        async Task ReadProgressAsync(StreamReader reader, bool isErrorStream)
        {
            string? line;
            var currentStatus = "Iniciando download...";
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (line.StartsWith(PostProcessingMarker, StringComparison.Ordinal))
                {
                    progress?.Report(new DownloadProgressInfo { Percentage = 100, Status = "Finalizando arquivo…", Speed = string.Empty, Eta = string.Empty });
                    continue;
                }
                if (line.StartsWith(FinalPathMarker, StringComparison.Ordinal))
                {
                    authoritativePath = NormalizePath(line[FinalPathMarker.Length..]);
                    if (convertMov)
                    {
                        lock (downloadedPaths) downloadedPaths.Add(authoritativePath);
                    }
                    continue;
                }
                if (isErrorStream && !line.StartsWith("[download]", StringComparison.Ordinal))
                {
                    if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                        lastError = line[6..].Trim();
                    errorOutput.AppendLine(line);
                    if (errorOutput.Length > 8192) errorOutput.Remove(0, errorOutput.Length - 8192);
                }
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
                        currentStatus = $"Convertendo para {normalizedAudioFormat.ToUpperInvariant()}";
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
        }

        var readOutputTask = ReadProgressAsync(process.StandardOutput, false);
        var readErrorTask = ReadProgressAsync(process.StandardError, true);
        try
        {
            await Task.WhenAll(readOutputTask, readErrorTask, process.WaitForExitAsync(cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            await StopProcessAsync(process);
            throw;
        }
        if (process.ExitCode != 0)
        {
            var errorMessage = !string.IsNullOrWhiteSpace(lastError) ? lastError : errorOutput.Length == 0
                ? $"O processo de download terminou com código {process.ExitCode}."
                : $"Erro (Code {process.ExitCode}): {errorOutput.ToString().Trim()}";
            throw new InvalidOperationException(errorMessage);
        }
        finalPath = authoritativePath ?? finalPath;

        if (convertMov)
        {
            var inputs = await ResolveMovInputsAsync(downloadedPaths, sourceDirectory, cancellationToken);
            try
            {
                return await ConvertMovInputsAsync(inputs, downloadedPaths, sourceDirectory, downloadDir,
                    titleHint, overwriteExistingFiles, saveAsNewCopy, progress, cancellationToken);
            }
            catch (IOException) when (!overwriteExistingFiles && !saveAsNewCopy && File.Exists(expectedPath))
            {
                throw new DownloadFileConflictException(expectedPath);
            }
        }

        var resolvedPath = DownloadOutputResolver.ResolveSingleFile(finalPath, sourceDirectory, targetExtension);
        if (resolvedPath is null)
            throw new FileNotFoundException("Não foi possível localizar um arquivo completo produzido por esta operação de download.", sourceDirectory);

        var targetPath = Path.Combine(downloadDir, Path.GetFileName(resolvedPath));
        if (overwriteExistingFiles)
        {
            File.Move(resolvedPath, targetPath, overwrite: true);
            return targetPath;
        }

        try
        {
            return NonOverwritingFilePublisher.Move(resolvedPath, targetPath, saveAsNewCopy);
        }
        catch (IOException) when (!saveAsNewCopy && File.Exists(targetPath))
        {
            throw new DownloadFileConflictException(targetPath);
        }
    }
}
