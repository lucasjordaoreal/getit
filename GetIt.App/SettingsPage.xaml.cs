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
            
            if (this.XamlRoot?.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = newTheme;
            }
        }
    }

    private async void UpdateYtDlp_Click(object sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text = "Atualizando yt-dlp...";
        var updater = new YtDlpUpdateService();
        var success = await updater.UpdateAsync();
        UpdateStatusText.Text = success ? "yt-dlp atualizado com sucesso!" : "Falha ao atualizar o yt-dlp.";
    }
}
