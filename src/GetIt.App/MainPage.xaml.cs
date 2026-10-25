using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
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
        Unloaded += (_, _) => ViewModel.DeactivateClipboardMonitoring();

        var focusLink = new KeyboardAccelerator { Key = VirtualKey.L, Modifiers = VirtualKeyModifiers.Control };
        focusLink.Invoked += (_, args) =>
        {
            UrlTextBox.Focus(FocusState.Keyboard);
            UrlTextBox.SelectAll();
            args.Handled = true;
        };
        KeyboardAccelerators.Add(focusLink);

        var startQueue = new KeyboardAccelerator { Key = VirtualKey.Enter, Modifiers = VirtualKeyModifiers.Control };
        startQueue.Invoked += (_, args) =>
        {
            if (ViewModel.CanStartDownloads) ViewModel.DownloadAllCommand.Execute(null);
            args.Handled = true;
        };
        KeyboardAccelerators.Add(startQueue);
    }

    private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        var controlDown = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        if (controlDown)
        {
            if (ViewModel.CanStartDownloads) ViewModel.DownloadAllCommand.Execute(null);
        }
        else if (ViewModel.CanAddUrl && ViewModel.AddUrlCommand.CanExecute(null))
        {
            ViewModel.AddUrlCommand.Execute(null);
        }
    }

    private async void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsNotDownloading) return;
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                var copiedText = await content.GetTextAsync();
                if (!ViewModel.IsNotDownloading) return;
                ViewModel.Url = copiedText.Trim();
                UrlTextBox.Focus(FocusState.Programmatic);
            }
            else ViewModel.InputError = "Copie um link de vídeo ou áudio para colar aqui.";
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "Failed to paste a link");
            ViewModel.InputError = "Não foi possível colar. Tente novamente ou use Ctrl+V no campo do link.";
        }
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // --- Theme resolution ---
        // Priority: saved user preference > Windows system theme
        _settings = SettingsService.LoadSettings();

        string resolvedTheme = _settings.Theme ?? SettingsService.GetWindowsTheme();
        CurrentTheme = resolvedTheme == "Light" ? ElementTheme.Light : ElementTheme.Dark;

        ApplyTheme(CurrentTheme);
#if DEBUG
        if (!LayoutRegressionProbe.IsEnabled)
#endif
        ViewModel.ActivateClipboardMonitoring();
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

