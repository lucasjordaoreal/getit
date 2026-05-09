using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GetIt_App.Services;
using Windows.Media.Core;

namespace GetIt_App;

public sealed partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        
        if (this.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = MainPage.CurrentTheme;
        }

        LoadHistory();
    }

    private async void LoadHistory()
    {
        var history = await HistoryService.LoadHistoryAsync();
        HistoryList.ItemsSource = history;
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        await HistoryService.ClearHistoryAsync();
        LoadHistory();
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Optional: auto play when selected
    }

    private string ResolveActualPath(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) return path;

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return path;

            var fileName = Path.GetFileName(path);
            if (fileName.Contains('\uFFFD'))
            {
                var pattern = fileName.Replace('\uFFFD', '?');
                var matches = Directory.GetFiles(dir, pattern);
                if (matches.Length > 0) return matches[0];
            }
        }
        catch { }

        return path;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string rawPath && !string.IsNullOrEmpty(rawPath))
        {
            var path = ResolveActualPath(rawPath);
            try
            {
                if (File.Exists(path))
                {
                    // Select file in explorer
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
                else if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", path);
                }
                else
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        Process.Start("explorer.exe", dir);
                }
            }
            catch { }
        }
    }

    private void PlayVideo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string rawPath && !string.IsNullOrEmpty(rawPath))
        {
            var path = ResolveActualPath(rawPath);
            if (File.Exists(path))
            {
                NoVideoText.Visibility = Visibility.Collapsed;
                PlayerElement.Source = MediaSource.CreateFromUri(new Uri(path));
                PlayerElement.MediaPlayer.Play();
            }
            else
            {
                NoVideoText.Text = "Arquivo não encontrado.";
                NoVideoText.Visibility = Visibility.Visible;
            }
        }
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        if (PlayerElement.MediaPlayer != null)
        {
            PlayerElement.MediaPlayer.Pause();
            PlayerElement.Source = null;
        }
    }
}

