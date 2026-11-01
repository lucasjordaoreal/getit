using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GetIt_App.Services;

namespace GetIt_App;

internal static class AppUpdateDownloadDialog
{
    public static async Task DownloadAndInstallAsync(
        GitHubRelease release,
        XamlRoot xamlRoot,
        CancellationToken cancellationToken = default)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var status = new TextBlock
        {
            Text = "Preparando a atualização verificada…",
            TextWrapping = TextWrapping.Wrap
        };
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };
        var content = new StackPanel
        {
            Spacing = 10,
            Children = { status, progressBar }
        };
        var dialog = new ContentDialog
        {
            Title = $"Baixando GetIt {GitHubReleaseUpdateClient.ParseStableVersionTag(release.TagName)}",
            Content = content,
            PrimaryButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };
        var progress = new Progress<UpdateDownloadProgress>(update =>
        {
            progressBar.Value = update.Percentage;
            status.Text = $"Baixando e verificando… {update.Percentage}% " +
                $"({update.BytesDownloaded / 1024d / 1024d:F1} / {update.TotalBytes / 1024d / 1024d:F1} MB)";
        });
        dialog.PrimaryButtonClick += (_, _) => operation.Cancel();

        var dialogTask = dialog.ShowAsync();
        var updateTask = UpdateService.DownloadAndInstallUpdateAsync(release, progress, operation.Token);
        _ = updateTask.ContinueWith(
            _ => App.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    dialog.Hide();
                }
                catch (InvalidOperationException)
                {
                }
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        var result = await dialogTask;
        if (result == ContentDialogResult.Primary)
        {
            operation.Cancel();
        }

        await updateTask;
    }
}
