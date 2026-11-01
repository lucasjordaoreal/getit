using System.Diagnostics;
using GetIt_App;

namespace GetIt_App.Services;

public static class UpdateService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly GitHubReleaseUpdateClient ReleaseClient = new(HttpClient);

    public static Version CurrentVersion => AppInfo.CurrentVersion;

    public static GitHubAsset SelectInstaller(GitHubRelease release) =>
        GitHubReleaseUpdateClient.SelectInstaller(release);

    public static async Task<GitHubRelease?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReleaseClient.GetLatestReleaseAsync(CurrentVersion, cancellationToken);
        }
        catch (Exception exception)
        {
            AppLogger.LogException(exception, "Failed to check for app updates");
            throw;
        }
    }

    public static async Task DownloadAndInstallUpdateAsync(
        GitHubRelease release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var installer = await ReleaseClient.DownloadInstallerAsync(
                release,
                progress,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            using var installerProcess = Process.Start(new ProcessStartInfo
            {
                FileName = installer.Path,
                UseShellExecute = true
            }) ?? throw new InvalidOperationException("Não foi possível iniciar o instalador da atualização.");

            await installerProcess.WaitForExitAsync();
            if (installerProcess.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"O instalador terminou com código {installerProcess.ExitCode}. O GetIt permaneceu aberto.");
            }
        }
        catch (Exception exception)
        {
            AppLogger.LogException(exception, "Failed to download or install app update");
            throw;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        return client;
    }
}
