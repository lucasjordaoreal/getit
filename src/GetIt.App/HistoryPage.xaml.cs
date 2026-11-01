using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GetIt_App.Services;
using Windows.Media.Core;
using Microsoft.UI.Xaml.Navigation;

namespace GetIt_App;

public sealed partial class HistoryPage : Page
{
    private List<HistoryItem> _allHistory = [];

    public HistoryPage()
    {
        this.InitializeComponent();
        this.Loaded += HistoryPage_Loaded;
    }

    private async void HistoryPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadHistoryAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (PlayerElement.MediaPlayer != null)
        {
            PlayerElement.MediaPlayer.Pause();
            PlayerElement.Source = null;
            PlayerEmptyState.Visibility = Visibility.Visible;
        }
        base.OnNavigatedFrom(e);
    }

    private async System.Threading.Tasks.Task LoadHistoryAsync()
    {
        _allHistory = await HistoryService.LoadHistoryAsync();
        var persistenceWarning = HistoryService.LastLoadWarning;
        HistoryPersistenceInfoBar.IsOpen = !string.IsNullOrWhiteSpace(persistenceWarning);
        if (persistenceWarning is not null)
        {
            HistoryPersistenceInfoBar.Message = persistenceWarning;
            HistoryPersistenceInfoBar.Severity = persistenceWarning.Contains("recuperado", StringComparison.OrdinalIgnoreCase)
                ? InfoBarSeverity.Warning
                : InfoBarSeverity.Error;
        }
        UpdateHistoryView();
    }

    private void UpdateHistoryView()
    {
        var selectedFilter = HistoryFilterComboBox.SelectedIndex switch
        {
            1 => HistoryMediaFilter.Videos,
            2 => HistoryMediaFilter.Audios,
            _ => HistoryMediaFilter.All
        };
        var visibleHistory = HistoryFilterPolicy.Filter(_allHistory, HistorySearchBox.Text, selectedFilter);
        HistoryList.ItemsSource = visibleHistory;
        var hasHistory = _allHistory.Count > 0;
        var hasVisibleItems = visibleHistory.Count > 0;
        HistoryList.Visibility = hasVisibleItems ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyState.Visibility = hasVisibleItems ? Visibility.Collapsed : Visibility.Visible;
        HistoryEmptyTitle.Text = hasHistory ? "Nenhum resultado encontrado" : "Nenhum download ainda";
        HistoryEmptyDescription.Text = hasHistory
            ? "Altere a pesquisa ou o filtro para ver outros downloads."
            : "As mídias concluídas aparecerão aqui.";
        ClearHistoryButton.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
        HistorySearchBox.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
        HistoryFilterComboBox.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = hasHistory && !string.IsNullOrWhiteSpace(HistorySearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateHistoryView();

    private void HistoryFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryEmptyTitle is not null)
            UpdateHistoryView();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e) => HistorySearchBox.Text = string.Empty;

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Limpar histórico?",
            Content = "Esta ação remove todos os downloads da lista do histórico.",
            PrimaryButtonText = "Limpar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            await HistoryService.ClearHistoryAsync();
            _allHistory.Clear();
            await LoadHistoryAsync();
        }
        catch (JsonFilePersistenceException)
        {
            HistoryPersistenceInfoBar.Severity = InfoBarSeverity.Error;
            HistoryPersistenceInfoBar.Message =
                "Não foi possível limpar o histórico. O arquivo anterior foi preservado; verifique as permissões e o espaço disponível.";
            HistoryPersistenceInfoBar.IsOpen = true;
        }
    }

    private void HistoryPersistenceInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args) =>
        HistoryService.AcknowledgeLoadWarning();

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
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "Failed to resolve actual file path from history item");
        }

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
            catch (Exception ex)
            {
                AppLogger.LogException(ex, $"Failed to open file/folder from history item: {rawPath}");
                NoVideoText.Text = "Não foi possível abrir o arquivo. Verifique se ele ainda existe.";
                PlayerEmptyState.Visibility = Visibility.Visible;
            }
        }
    }

    private void PlayVideo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string rawPath && !string.IsNullOrEmpty(rawPath))
        {
            var path = ResolveActualPath(rawPath);
            if (File.Exists(path))
            {
                PlayerEmptyState.Visibility = Visibility.Collapsed;
                PlayerElement.Source = MediaSource.CreateFromUri(new Uri(path));
                PlayerElement.MediaPlayer.Play();
            }
            else
            {
                NoVideoText.Text = "Mídia não encontrada.";
                PlayerEmptyState.Visibility = Visibility.Visible;
            }
        }
    }
}
