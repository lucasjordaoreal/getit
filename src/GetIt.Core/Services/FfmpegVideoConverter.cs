using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace GetIt_App.Services;

public sealed record VideoConversionProgress(double Percentage, string Status, string Encoder);

/// <summary>Converts an already downloaded video, trying usable hardware encoders before CPU.</summary>
public sealed class FfmpegVideoConverter
{
    private static readonly string[] HardwareEncoders = ["h264_nvenc", "h264_qsv", "h264_amf"];
    private static readonly ConcurrentDictionary<string, string[]> HardwareCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim ProbeGate = new(1, 1);
    private readonly string _ffmpegPath;
    private readonly IReadOnlyList<string>? _explicitHardwareCandidates;

    /// <param name="hardwareCandidates">Optional explicit order; otherwise actual hardware is probed and cached.</param>
    public FfmpegVideoConverter(string ffmpegPath, IReadOnlyList<string>? hardwareCandidates = null)
    {
        _ffmpegPath = Path.GetFullPath(ffmpegPath);
        _explicitHardwareCandidates = hardwareCandidates;
    }

    public async Task<string> ConvertToMovAsync(string sourcePath, string targetPath, bool overwrite,
        IProgress<VideoConversionProgress>? progress = null, CancellationToken cancellationToken = default,
        string? audioSourcePath = null)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        targetPath = Path.GetFullPath(targetPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("O vídeo original não foi encontrado para conversão.", sourcePath);
        if (audioSourcePath is not null)
        {
            audioSourcePath = Path.GetFullPath(audioSourcePath);
            if (!File.Exists(audioSourcePath)) throw new FileNotFoundException("O áudio original não foi encontrado para conversão.", audioSourcePath);
        }
        if (sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase)) return sourcePath;
        if (!overwrite && File.Exists(targetPath)) throw new IOException($"Arquivo já existe e não será sobrescrito: {targetPath}");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        var candidates = _explicitHardwareCandidates ?? await GetHardwareEncodersAsync(cancellationToken);
        var duration = await ReadDurationAsync(sourcePath, cancellationToken);
        string? lastError = null;
        foreach (var encoder in candidates.Concat(new[] { "libx264" }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cpu = encoder == "libx264";
            var status = cpu
                ? "Convertendo com CPU (GPU indisponível ou incompatível)…"
                : $"Convertendo com GPU ({EncoderLabel(encoder)})…";
            progress?.Report(new VideoConversionProgress(0, status, encoder));
            var temporaryOutput = Path.Combine(Path.GetDirectoryName(targetPath)!,
                $".getit-{Guid.NewGuid():N}.mov");
            try
            {
                var arguments = new List<string>
                {
                    "-hide_banner", "-nostdin", "-loglevel", "error", "-n", "-fflags", "+genpts", "-i", sourcePath
                };
                if (audioSourcePath is not null)
                    arguments.AddRange(new[] { "-i", audioSourcePath });
                // Decode the raw streams directly. Copying HLS video into an intermediate MKV
                // can fail on unset packet timestamps before a hardware encoder is ever reached.
                arguments.AddRange(new[]
                {
                    "-map", "0:v:0", "-map", audioSourcePath is null ? "0:a:0?" : "1:a:0",
                    "-map_metadata", "0", "-c:v", encoder, "-vf", "format=yuv420p"
                });
                arguments.AddRange(QualityArguments(encoder));
                // MOV cannot carry the Opus streams commonly returned by video sites.
                arguments.AddRange(new[] { "-c:a", "aac", "-b:a", "320k", "-movflags", "+faststart", "-progress", "pipe:1", "-nostats", temporaryOutput });
                var result = await RunAsync(arguments, cancellationToken, line =>
                {
                    if (line.StartsWith("out_time_us=", StringComparison.Ordinal)
                        && double.TryParse(line[12..], NumberStyles.Float, CultureInfo.InvariantCulture, out var microseconds)
                        && duration > 0)
                    {
                        progress?.Report(new VideoConversionProgress(Math.Clamp(microseconds / 1_000_000 / duration * 100, 0, 99.9), status, encoder));
                    }
                });
                if (result.ExitCode != 0 || !File.Exists(temporaryOutput) || new FileInfo(temporaryOutput).Length == 0)
                {
                    lastError = result.Error.Trim();
                    AppLogger.LogWarning($"Conversion with {encoder} failed: {lastError}");
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                // The old destination is replaced only after the new file has finished successfully.
                File.Move(temporaryOutput, targetPath, overwrite);
                progress?.Report(new VideoConversionProgress(100, cpu ? "Conversão concluída com CPU." : "Conversão concluída com GPU.", encoder));
                return targetPath;
            }
            finally
            {
                // Only this attempt's uniquely named partial output is removed; keep the source for retry.
                try { if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput); }
                catch (IOException ex) { AppLogger.LogWarning($"Partial conversion cleanup deferred: {ex.Message}"); }
                catch (UnauthorizedAccessException ex) { AppLogger.LogWarning($"Partial conversion cleanup deferred: {ex.Message}"); }
            }
        }
        throw new InvalidOperationException($"Não foi possível converter o vídeo com GPU nem CPU. O original foi preservado em '{sourcePath}'. {lastError}");
    }

    private async Task<IReadOnlyList<string>> GetHardwareEncodersAsync(CancellationToken cancellationToken)
    {
        var cacheKey = _ffmpegPath + File.GetLastWriteTimeUtc(_ffmpegPath).Ticks.ToString(CultureInfo.InvariantCulture);
        if (HardwareCache.TryGetValue(cacheKey, out var cached)) return cached;
        await ProbeGate.WaitAsync(cancellationToken);
        try
        {
            if (HardwareCache.TryGetValue(cacheKey, out cached)) return cached;
            var available = new List<string>();
            foreach (var encoder in HardwareEncoders)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    var probe = new List<string>
                    {
                        "-hide_banner", "-nostdin", "-loglevel", "error", "-f", "lavfi", "-i", "color=black:s=640x360:r=30",
                        "-frames:v", "1", "-an", "-c:v", encoder, "-vf", "format=yuv420p"
                    };
                    probe.AddRange(QualityArguments(encoder));
                    probe.AddRange(new[] { "-f", "null", "-" });
                    var result = await RunAsync(probe, timeout.Token);
                    if (result.ExitCode == 0) available.Add(encoder);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            HardwareCache[cacheKey] = available.ToArray();
            return available;
        }
        finally { ProbeGate.Release(); }
    }

    private async Task<double> ReadDurationAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var ffprobe = Path.Combine(Path.GetDirectoryName(_ffmpegPath)!, "ffprobe.exe");
        if (!File.Exists(ffprobe)) return 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var result = await RunAsync(new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", sourcePath }, timeout.Token, executable: ffprobe);
            return double.TryParse(result.Output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) ? duration : 0;
        }
        catch (OperationCanceledException) { cancellationToken.ThrowIfCancellationRequested(); return 0; }
    }

    private async Task<ProcessResult> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken,
        Action<string>? onOutput = null, string? executable = null)
    {
        var info = new ProcessStartInfo(executable ?? _ffmpegPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        cancellationToken.ThrowIfCancellationRequested();
        process.Start();
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception ex) { AppLogger.LogWarning($"Could not stop the conversion process: {ex.Message}"); }
        });
        var output = new StringBuilder();
        var errors = new StringBuilder();
        async Task ReadAsync(StreamReader reader, StringBuilder buffer, bool notify)
        {
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                buffer.AppendLine(line);
                if (buffer.Length > 8192) buffer.Remove(0, buffer.Length - 8192);
                if (notify) onOutput?.Invoke(line);
            }
        }
        try
        {
            await Task.WhenAll(ReadAsync(process.StandardOutput, output, true), ReadAsync(process.StandardError, errors, false), process.WaitForExitAsync(cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            return new ProcessResult(process.ExitCode, output.ToString(), errors.ToString());
        }
        catch
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(wait.Token);
            }
            catch (InvalidOperationException) { }
            catch (OperationCanceledException) { }
            catch (System.ComponentModel.Win32Exception ex) { AppLogger.LogWarning($"Conversion cleanup failed: {ex.Message}"); }
            throw;
        }
    }

    private static string EncoderLabel(string encoder) => encoder switch
    {
        "h264_nvenc" => "NVIDIA", "h264_qsv" => "Intel", "h264_amf" => "AMD", _ => encoder
    };

    private static string[] QualityArguments(string encoder) => encoder switch
    {
        "h264_nvenc" => ["-preset", "p5", "-rc", "vbr", "-cq", "18", "-b:v", "0"],
        "h264_qsv" => ["-preset", "medium", "-global_quality", "18"],
        "h264_amf" => ["-quality", "quality", "-rc", "cqp", "-qp_i", "18", "-qp_p", "20", "-qp_b", "22"],
        _ => ["-preset", "medium", "-crf", "18"]
    };

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
