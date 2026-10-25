using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GetIt_App.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GetIt_App;

public sealed partial class SettingsPage : Page
{
    private readonly YtDlpUpdateService _ytDlpUpdater = new();
    private CancellationTokenSource? _pageLifetime;
    private YtDlpVersionStatus? _ytDlpStatus;
    private bool _isLoadingSettings = true;
    private bool _ytDlpBusy;

    public SettingsPage()
    {
        this.InitializeComponent();
        this.Loaded += SettingsPage_Loaded;
        Unloaded += (_, _) =>
        {
            _pageLifetime?.Cancel();
            _pageLifetime?.Dispose();
            _pageLifetime = null;
        };
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoadingSettings = true;
        ThemeToggle.IsOn = MainPage.CurrentTheme == ElementTheme.Light;
        var settings = SettingsService.LoadSettings();
        AppAutoUpdateToggle.IsOn = settings.IsAppAutoUpdateEnabled;
        YtDlpAutoUpdateToggle.IsOn = settings.IsYtDlpAutoUpdateEnabled;
        AppVersionText.Text = $"GetIt {AppInfo.CurrentVersion.ToString(3)}";
        _isLoadingSettings = false;
        _pageLifetime?.Cancel();
        _pageLifetime?.Dispose();
        _pageLifetime = new CancellationTokenSource();
        await CheckYtDlpStatusAsync(_pageLifetime.Token);
    }

    private void AppAutoUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isLoadingSettings && sender is ToggleSwitch toggle)
        {
            var settings = SettingsService.LoadSettings();
            settings.IsAppAutoUpdateEnabled = toggle.IsOn;
            SettingsService.SaveSettings(settings);
        }
    }

    private void YtDlpAutoUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isLoadingSettings && sender is ToggleSwitch toggle)
        {
            var settings = SettingsService.LoadSettings();
            settings.IsYtDlpAutoUpdateEnabled = toggle.IsOn;
            SettingsService.SaveSettings(settings);
        }
    }

    private void ThemeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isLoadingSettings && sender is ToggleSwitch ts)
        {
            var newTheme = ts.IsOn ? ElementTheme.Light : ElementTheme.Dark;
            MainPage.CurrentTheme = newTheme;

            var settings = SettingsService.LoadSettings();
            settings.Theme = newTheme == ElementTheme.Light ? "Light" : "Dark";
            SettingsService.SaveSettings(settings);

            if (this.XamlRoot?.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = newTheme;
            }
        }
    }

    private async void UpdateYtDlp_Click(object sender, RoutedEventArgs e)
    {
        if (_pageLifetime is not null) await CheckYtDlpStatusAsync(_pageLifetime.Token);
    }

    private async Task CheckYtDlpStatusAsync(CancellationToken cancellationToken)
    {
        if (_ytDlpBusy) return;
        SetYtDlpBusy(true);
        UpdateStatusInfoBar.Severity = InfoBarSeverity.Informational;
        UpdateStatusInfoBar.Message = "Consultando a versão instalada e a versão mais recente…";
        UpdateStatusInfoBar.IsOpen = true;
        try
        {
            _ytDlpStatus = await _ytDlpUpdater.GetStatusAsync(cancellationToken, new Progress<string>(version =>
            {
                if (!cancellationToken.IsCancellationRequested) YtDlpInstalledVersionText.Text = $"yt-dlp instalado: {version}";
            }));
            if (cancellationToken.IsCancellationRequested) return;
            DisplayYtDlpStatus(_ytDlpStatus);
            var previousUpdate = YtDlpUpdateService.LastUpdateResult;
            if (previousUpdate?.Succeeded == true
                && YtDlpVersionPolicy.Compare(previousUpdate.Before.InstalledVersion, previousUpdate.After.InstalledVersion) is < 0
                && previousUpdate.After.InstalledVersion == _ytDlpStatus.InstalledVersion)
                UpdateStatusInfoBar.Message = $"{previousUpdate.Message} {_ytDlpStatus.Message}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLogger.LogException(exception, "Could not read yt-dlp versions");
            UpdateStatusInfoBar.Severity = InfoBarSeverity.Error;
            UpdateStatusInfoBar.Message = $"Não foi possível verificar o yt-dlp: {exception.Message}";
        }
        finally { SetYtDlpBusy(false); }
    }

    private async void InstallYtDlpUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_ytDlpBusy || _ytDlpStatus?.UpdateAvailable != true) return;
        SetYtDlpBusy(true);
        UpdateStatusInfoBar.Severity = InfoBarSeverity.Informational;
        UpdateStatusInfoBar.Message = $"Atualizando yt-dlp {_ytDlpStatus.InstalledVersion} para {_ytDlpStatus.LatestVersion}…";
        try
        {
            // Navigation does not interrupt an executable replacement already requested by the user.
            var result = await _ytDlpUpdater.InstallUpdateAsync(_ytDlpStatus);
            _ytDlpStatus = result.After;
            if (!IsLoaded) return;
            DisplayYtDlpStatus(result.After);
            UpdateStatusInfoBar.Severity = result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            UpdateStatusInfoBar.Message = result.Message;
        }
        catch (Exception exception)
        {
            AppLogger.LogException(exception, "Manual yt-dlp update failed");
            if (!IsLoaded) return;
            UpdateStatusInfoBar.Severity = InfoBarSeverity.Error;
            UpdateStatusInfoBar.Message = $"Não foi possível atualizar o yt-dlp: {exception.Message}";
        }
        finally { SetYtDlpBusy(false); }
    }

    private void DisplayYtDlpStatus(YtDlpVersionStatus status)
    {
        YtDlpInstalledVersionText.Text = $"yt-dlp instalado: {status.InstalledVersion ?? "não identificado"}";
        YtDlpLatestVersionText.Text = $"Última versão estável: {status.LatestVersion ?? "não foi possível consultar"}";
        InstallYtDlpUpdateButton.Visibility = status.UpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
        InstallYtDlpUpdateButton.Content = $"Atualizar para {status.LatestVersion}";
        UpdateStatusInfoBar.Severity = status.Error is not null ? InfoBarSeverity.Warning
            : status.UpdateAvailable ? InfoBarSeverity.Informational
            : status.IsUpToDate ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        UpdateStatusInfoBar.Message = status.Message;
        UpdateStatusInfoBar.IsOpen = true;
    }

    private void SetYtDlpBusy(bool busy)
    {
        _ytDlpBusy = busy;
        UpdateButton.IsEnabled = !busy;
        InstallYtDlpUpdateButton.IsEnabled = !busy;
        UpdateProgressRing.IsActive = busy;
        UpdateProgressRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void CheckAppUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckAppUpdateButton.IsEnabled = false;
        AppUpdateProgressRing.IsActive = true;
        AppUpdateProgressRing.Visibility = Visibility.Visible;
        AppUpdateStatusInfoBar.Severity = InfoBarSeverity.Informational;
        AppUpdateStatusInfoBar.Message = "Verificando atualizações do GetIt...";
        AppUpdateStatusInfoBar.IsOpen = true;

        try
        {
            var release = await UpdateService.CheckForUpdatesAsync();
            if (release == null)
            {
                AppUpdateStatusInfoBar.Severity = InfoBarSeverity.Success;
                AppUpdateStatusInfoBar.Message = $"O GetIt está atualizado (versão {AppInfo.CurrentVersion.ToString(3)}).";
                return;
            }

            var installer = release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, $"GetIt-Setup-v{release.TagName}.exe", StringComparison.OrdinalIgnoreCase));
            if (installer == null)
            {
                AppUpdateStatusInfoBar.Severity = InfoBarSeverity.Error;
                AppUpdateStatusInfoBar.Message = $"O release {release.TagName} não contém o instalador esperado GetIt-Setup-v{release.TagName}.exe.";
                return;
            }

            AppUpdateStatusInfoBar.Severity = InfoBarSeverity.Success;
            AppUpdateStatusInfoBar.Message = $"Nova versão {release.TagName} encontrada. Confirme na janela para iniciar a instalação.";
            var dialog = new ContentDialog
            {
                Title = "Atualização disponível",
                Content = $"A versão {release.TagName} do GetIt está disponível. Deseja baixar e instalar agora?",
                PrimaryButtonText = "Atualizar",
                CloseButtonText = "Agora não",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await UpdateService.DownloadAndInstallUpdateAsync(installer.BrowserDownloadUrl);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "Manual app update check failed");
            AppUpdateStatusInfoBar.Severity = InfoBarSeverity.Error;
            AppUpdateStatusInfoBar.Message = $"Não foi possível verificar atualizações: {ex.Message}";
        }
        finally
        {
            AppUpdateProgressRing.IsActive = false;
            AppUpdateProgressRing.Visibility = Visibility.Collapsed;
            CheckAppUpdateButton.IsEnabled = true;
        }
    }
}
