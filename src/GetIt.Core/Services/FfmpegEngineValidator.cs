using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GetIt_App.Services;

public static class FfmpegEngineValidator
{
    private static readonly string[] RequiredEncoders = ["libx264", "aac", "libmp3lame", "flac", "pcm_s16le"];

    public static async Task<IReadOnlyList<DownloadEngineIssue>> ValidateDirectoryAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        string fullDirectory;
        try
        {
            fullDirectory = Path.GetFullPath(directory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return [new("FFmpeg", directory, $"o caminho informado é inválido: {exception.Message}")];
        }

        var ffmpegPath = Path.Combine(fullDirectory, "ffmpeg.exe");
        var ffprobePath = Path.Combine(fullDirectory, "ffprobe.exe");
        var ffmpegIssue = await DownloadEngineHealthCheck.CheckExecutableAsync(
            "ffmpeg.exe", ffmpegPath, ["-version"], cancellationToken);
        var ffprobeIssue = await DownloadEngineHealthCheck.CheckExecutableAsync(
            "ffprobe.exe", ffprobePath, ["-version"], cancellationToken);
        var issues = new List<DownloadEngineIssue>();
        if (ffmpegIssue is not null) issues.Add(ffmpegIssue);
        if (ffprobeIssue is not null) issues.Add(ffprobeIssue);
        if (issues.Count > 0) return issues;

        var ffmpegVersionResult = await RunCommandAsync(ffmpegPath, ["-version"], cancellationToken);
        var ffprobeVersionResult = await RunCommandAsync(ffprobePath, ["-version"], cancellationToken);
        var ffmpegVersion = ParseVersion(ffmpegVersionResult.Output);
        var ffprobeVersion = ParseVersion(ffprobeVersionResult.Output);
        if (ffmpegVersion is null || ffprobeVersion is null)
        {
            return [new("FFmpeg/FFprobe", fullDirectory, "não foi possível identificar as versões dos executáveis")];
        }

        if (ffmpegVersion < new Version(6, 0))
        {
            return [new("ffmpeg.exe", ffmpegPath, $"a versão {ffmpegVersion} é antiga; o GetIt requer FFmpeg 6.0 ou posterior")];
        }

        if (ffmpegVersion.Major != ffprobeVersion.Major || ffmpegVersion.Minor != ffprobeVersion.Minor)
        {
            return [new("ffprobe.exe", ffprobePath, $"a versão {ffprobeVersion} não corresponde à versão {ffmpegVersion} do FFmpeg")];
        }

        var encoderResult = await RunCommandAsync(ffmpegPath, ["-hide_banner", "-encoders"], cancellationToken);
        var availableEncoders = encoderResult.Output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(columns => columns.Length >= 2)
            .Select(columns => columns[1])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingEncoders = RequiredEncoders.Where(encoder => !availableEncoders.Contains(encoder)).ToArray();
        if (encoderResult.ExitCode != 0 || missingEncoders.Length > 0)
        {
            var reason = missingEncoders.Length == 0
                ? "não foi possível consultar os encoders disponíveis"
                : $"faltam encoders exigidos pelo GetIt: {string.Join(", ", missingEncoders)}";
            return [new("ffmpeg.exe", ffmpegPath, reason)];
        }

        return [];
    }

    internal static Version? ParseVersion(string output)
    {
        var match = Regex.Match(output, @"(?m)^\w+ version (?<version>n?\d+\.\d+(?:\.\d+)?)\b");
        return match.Success && Version.TryParse(match.Groups["version"].Value.TrimStart('n'), out var version)
            ? version
            : null;
    }

    private static async Task<(int ExitCode, string Output)> RunCommandAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);

        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (!process.Start())
            {
                throw new InvalidOperationException($"Não foi possível iniciar '{executable}'.");
            }

            using var registration = timeout.Token.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception exception)
                {
                    AppLogger.LogException(exception, $"Failed to stop {Path.GetFileName(executable)}");
                }
            });
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(timeout.Token));
            return (process.ExitCode, $"{outputTask.Result}{Environment.NewLine}{errorTask.Result}");
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception exception)
            {
                AppLogger.LogException(exception, $"Failed to stop {Path.GetFileName(executable)}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"'{Path.GetFileName(executable)}' não respondeu em 10 segundos.");
        }
    }
}
