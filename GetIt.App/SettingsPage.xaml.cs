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
}
