using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GetIt_App.Services;

public static class DenoEngineValidator
{
    private static readonly Version MinimumVersion = new(2, 3, 0);

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
            return [new("Deno", directory, $"o caminho informado é inválido: {exception.Message}")];
        }

        var executable = Path.Combine(fullDirectory, "deno.exe");
        var issue = await DownloadEngineHealthCheck.CheckExecutableAsync(
            "deno.exe", executable, ["--version"], cancellationToken);
        if (issue is not null) return [issue];

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
                WorkingDirectory = fullDirectory
            }
        };
        process.StartInfo.ArgumentList.Add("--version");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start())
            {
                return [new("deno.exe", executable, "o Windows não conseguiu iniciar o runtime")];
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
                    AppLogger.LogException(exception, "Failed to stop Deno version check");
                }
            });
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(timeout.Token));

            if (process.ExitCode != 0)
            {
                var detail = errorTask.Result.Trim();
                return [new("deno.exe", executable, string.IsNullOrWhiteSpace(detail)
                    ? $"o runtime encerrou com código {process.ExitCode}"
                    : $"o runtime encerrou com código {process.ExitCode}: {detail}")];
            }

            var version = ParseVersion($"{outputTask.Result}{Environment.NewLine}{errorTask.Result}");
            if (version is null)
            {
                return [new("deno.exe", executable, "não foi possível identificar a versão do runtime")];
            }

            return version < MinimumVersion
                ? [new("deno.exe", executable, $"a versão {version} é antiga; o yt-dlp requer Deno 2.3.0 ou posterior")]
                : [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            return [new("deno.exe", executable, "o runtime não respondeu em 10 segundos")];
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            TryTerminate(process);
            return [new("deno.exe", executable, $"não foi possível consultar a versão: {exception.Message}")];
        }
    }

    internal static Version? ParseVersion(string output)
    {
        var match = Regex.Match(output, @"(?m)^deno (?<version>\d+\.\d+\.\d+)\b");
        return match.Success && Version.TryParse(match.Groups["version"].Value, out var version)
            ? version
            : null;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception exception)
        {
            AppLogger.LogException(exception, "Failed to stop Deno version check");
        }
    }
}
