using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GetIt_App.Services;

public class YtDlpUpdateService
{
    private static readonly HttpClient ReleaseClient = CreateReleaseClient();
    private static readonly SemaphoreSlim OperationGate = new(1, 1);
    private readonly string _ytdlpPath;
    public static YtDlpUpdateResult? LastUpdateResult { get; private set; }

    public YtDlpUpdateService()
    {
        var binPath = DownloadService.ResolveBinPath();
        var candidate = Path.Combine(binPath, "yt-dlp.exe");
        _ytdlpPath = candidate;
    }

    public async Task<YtDlpVersionStatus> GetStatusAsync(CancellationToken cancellationToken = default, IProgress<string>? installedVersionProgress = null)
    {
        await OperationGate.WaitAsync(cancellationToken);
        try { return await ReadStatusAsync(cancellationToken, installedVersionProgress); }
        finally { OperationGate.Release(); }
    }

    public async Task<string> GetInstalledVersionAsync(CancellationToken cancellationToken = default)
    {
        await OperationGate.WaitAsync(cancellationToken);
        try { return await ReadInstalledVersionAsync(cancellationToken); }
        finally { OperationGate.Release(); }
    }

    // Kept for the existing automatic-update preference. A zero exit code alone is not proof of an update.
    public async Task<bool> UpdateAsync()
    {
        var status = await GetStatusAsync();
        if (!status.UpdateAvailable) return status.IsUpToDate;
        var result = await InstallUpdateAsync(status);
        if (!result.Succeeded) AppLogger.LogWarning(result.Message);
        return result.Succeeded;
    }

    public async Task<YtDlpUpdateResult> InstallUpdateAsync(YtDlpVersionStatus checkedStatus, CancellationToken cancellationToken = default)
    {
        await OperationGate.WaitAsync(cancellationToken);
        try
        {
            // Re-read after taking the gate: startup auto-update might already have changed this executable.
            var installed = await ReadInstalledVersionAsync(cancellationToken);
            var before = new YtDlpVersionStatus(installed, checkedStatus.LatestVersion, checkedStatus.Error);
            if (!before.UpdateAvailable)
                return LastUpdateResult = new(before, before, before.IsUpToDate, before.Message);

            if (!YtDlpVersionPolicy.IsSupportedVersion(before.LatestVersion))
                return LastUpdateResult = YtDlpVersionPolicy.VerifyUpdate(before, installed, "A versão disponível não foi reconhecida.");

            var run = await RunAsync(new[] { "--ignore-config", "--update-to", $"stable@{before.LatestVersion}" }, TimeSpan.FromSeconds(120), cancellationToken);
            var installedAfter = await ReadInstalledVersionAsync(cancellationToken);
            var error = run.ExitCode == 0 ? null : UsefulError(run.Output, run.Error);
            return LastUpdateResult = YtDlpVersionPolicy.VerifyUpdate(before, installedAfter, error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            AppLogger.LogException(exception, "Failed to update yt-dlp");
            return LastUpdateResult = YtDlpVersionPolicy.VerifyUpdate(checkedStatus, checkedStatus.InstalledVersion, exception.Message);
        }
        finally { OperationGate.Release(); }
    }

    private async Task<YtDlpVersionStatus> ReadStatusAsync(CancellationToken cancellationToken, IProgress<string>? installedVersionProgress)
    {
        string? installed = null;
        string? latest = null;
        string? error = null;
        try
        {
            installed = await ReadInstalledVersionAsync(cancellationToken);
            installedVersionProgress?.Report(installed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { error = exception.Message; }
        try
        {
            using var response = await ReleaseClient.GetAsync("https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            latest = document.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
            if (!YtDlpVersionPolicy.IsSupportedVersion(latest))
                throw new InvalidOperationException("O servidor não retornou uma versão reconhecida do yt-dlp.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            AppLogger.LogWarning($"Could not check the latest yt-dlp release: {exception.Message}");
            error = string.IsNullOrWhiteSpace(error) ? "Não foi possível consultar a versão mais recente. Verifique sua conexão e tente novamente." : error;
        }
        return new(installed, latest, error);
    }

    private async Task<string> ReadInstalledVersionAsync(CancellationToken cancellationToken)
    {
        var run = await RunAsync(new[] { "--ignore-config", "--version" }, TimeSpan.FromSeconds(10), cancellationToken);
        if (run.ExitCode != 0) throw new InvalidOperationException(UsefulError(run.Output, run.Error));
        var version = run.Output.Trim();
        if (string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("Não foi possível identificar a versão instalada do yt-dlp.");
        return version;
    }

    private async Task<ProcessResult> RunAsync(string[] arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!File.Exists(_ytdlpPath)) throw new FileNotFoundException("yt-dlp não encontrado na pasta de componentes do GetIt.", _ytdlpPath);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var start = new ProcessStartInfo(_ytdlpPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(_ytdlpPath)!
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            process.Start();
            using var registration = deadline.Token.Register(() => Stop(process));
            // Both streams must be consumed while -U is running, otherwise a full pipe can deadlock it.
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(deadline.Token));
            return new(process.ExitCode, output.Result, error.Result);
        }
        catch (OperationCanceledException)
        {
            Stop(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("A operação do yt-dlp demorou demais. Tente novamente.");
        }
        catch
        {
            Stop(process);
            throw;
        }
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Exception exception) { AppLogger.LogWarning($"Could not stop yt-dlp updater: {exception.Message}"); }
    }

    private static string UsefulError(string output, string error)
    {
        var message = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
        return string.IsNullOrWhiteSpace(message) ? "O mecanismo de atualização não concluiu a operação." : message.Length <= 600 ? message : message[^600..];
    }

    private static HttpClient CreateReleaseClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GetIt/0.1 (yt-dlp version check)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
