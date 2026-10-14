using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using GetIt_App.ViewModels;
using GetIt_App.Services;

namespace GetIt_App;

public sealed partial class MainPage : Page
{
    public static ElementTheme CurrentTheme { get; set; } = ElementTheme.Default;

    // Loaded settings (kept in memory so we can update only the Theme field on toggle)
    private AppSettings _settings = new();

    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
    }

    private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.AddUrlCommand.CanExecute(null))
        {
            e.Handled = true;
            ViewModel.AddUrlCommand.Execute(null);
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // --- Theme resolution ---
        // Priority: saved user preference > Windows system theme
        _settings = SettingsService.LoadSettings();

        string resolvedTheme = _settings.Theme ?? SettingsService.GetWindowsTheme();
        CurrentTheme = resolvedTheme == "Light" ? ElementTheme.Light : ElementTheme.Dark;

        ApplyTheme(CurrentTheme);
    }

    /// <summary>Applies a theme to this page.</summary>
    private void ApplyTheme(ElementTheme theme)
    {
        if (XamlRoot?.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = theme;
        }

        RequestedTheme = theme;
    }
}

