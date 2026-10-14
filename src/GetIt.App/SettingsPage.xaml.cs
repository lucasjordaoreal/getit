using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GetIt_App.Services;
using System;

namespace GetIt_App;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        this.InitializeComponent();
        this.Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ThemeToggle.IsOn = MainPage.CurrentTheme == ElementTheme.Light;
        var settings = SettingsService.LoadSettings();
        AppAutoUpdateToggle.IsOn = settings.IsAppAutoUpdateEnabled;
        YtDlpAutoUpdateToggle.IsOn = settings.IsYtDlpAutoUpdateEnabled;
        AppVersionText.Text = $"GetIt {AppInfo.CurrentVersion.ToString(3)}";
    }

    private void AppAutoUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            var settings = SettingsService.LoadSettings();
            settings.IsAppAutoUpdateEnabled = toggle.IsOn;
            SettingsService.SaveSettings(settings);
        }
    }

    private void YtDlpAutoUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            var settings = SettingsService.LoadSettings();
            settings.IsYtDlpAutoUpdateEnabled = toggle.IsOn;
            SettingsService.SaveSettings(settings);
        }
    }

    private void ThemeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts)
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
        UpdateButton.IsEnabled = false;
        UpdateProgressRing.IsActive = true;
        UpdateProgressRing.Visibility = Visibility.Visible;
        UpdateStatusInfoBar.Severity = InfoBarSeverity.Informational;
        UpdateStatusInfoBar.Message = "Verificando atualizações...";
        UpdateStatusInfoBar.IsOpen = true;

        try
        {
            var updater = new YtDlpUpdateService();
            var success = await updater.UpdateAsync();
            UpdateStatusInfoBar.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            UpdateStatusInfoBar.Message = success
                ? "yt-dlp atualizado com sucesso."
                : "Não foi possível atualizar o yt-dlp. Verifique se ele está instalado.";
        }
        catch (Exception)
        {
            UpdateStatusInfoBar.Severity = InfoBarSeverity.Error;
            UpdateStatusInfoBar.Message = "Ocorreu um erro ao atualizar o yt-dlp.";
        }
        finally
        {
            UpdateProgressRing.IsActive = false;
            UpdateProgressRing.Visibility = Visibility.Collapsed;
            UpdateButton.IsEnabled = true;
        }
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
