using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GetIt_App.Services;
using Windows.Media.Core;
using Microsoft.UI.Xaml.Navigation;
using Windows.Media.Playback;

namespace GetIt_App;

public sealed partial class HistoryPage : Page
{
    private List<HistoryItem> _allHistory = [];
    private HistoryItem? _currentPlayingItem;
    private MediaPlaybackSession? _trackedPlaybackSession;
    private readonly Dictionary<Button, PlaybackButtonState> _playbackButtons = [];

    private sealed class PlaybackButtonState(
        HistoryItem item,
        ScrollViewer scrollViewer,
        PropertyChangedEventHandler propertyChangedHandler)
    {
        public HistoryItem Item { get; } = item;
        public ScrollViewer ScrollViewer { get; } = scrollViewer;
        public TextBlock TextBlock { get; init; } = null!;
        public PropertyChangedEventHandler PropertyChangedHandler { get; } = propertyChangedHandler;
        public DispatcherTimer Timer { get; } = new() { Interval = TimeSpan.FromMilliseconds(16) };
        public EventHandler<object>? TickHandler { get; set; }
        public double Offset { get; set; }
        public long AnimationStartedAt { get; set; }
    }

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
            if (_currentPlayingItem is not null)
                _currentPlayingItem.IsPlaying = false;
            StopTrackingPlaybackState();
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
                if (_currentPlayingItem is not null)
                    _currentPlayingItem.IsPlaying = false;
                _currentPlayingItem = _allHistory.FirstOrDefault(item =>
                    string.Equals(item.LocalFilePath, rawPath, StringComparison.OrdinalIgnoreCase));
                TrackPlaybackState();
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

    private void PlayButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.DataContext is not HistoryItem item ||
            button.Content is not ScrollViewer scrollViewer ||
            scrollViewer.Content is not TextBlock textBlock)
        {
            return;
        }

        PlayButton_Unloaded(button, new RoutedEventArgs());
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(HistoryItem.IsPlaying))
                DispatcherQueue.TryEnqueue(() => UpdatePlayButtonMarquee(button, item));
        };
        var state = new PlaybackButtonState(item, scrollViewer, handler)
        {
            TextBlock = textBlock
        };
        state.TickHandler = (_, _) => PlaybackButtonMarquee_Tick(state);
        state.Timer.Tick += state.TickHandler;
        item.PropertyChanged += handler;
        _playbackButtons[button] = state;
        DispatcherQueue.TryEnqueue(() => UpdatePlayButtonMarquee(button, item));
    }

    private void PlayButton_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !_playbackButtons.Remove(button, out var registration))
            return;

        registration.Item.PropertyChanged -= registration.PropertyChangedHandler;
        registration.Timer.Stop();
        if (registration.TickHandler is not null)
            registration.Timer.Tick -= registration.TickHandler;
        registration.TextBlock.Width = double.NaN;
        registration.ScrollViewer.ChangeView(0, null, null, disableAnimation: true);
    }

    private void UpdatePlayButtonMarquee(Button button, HistoryItem item)
    {
        if (!_playbackButtons.TryGetValue(button, out var state) ||
            !ReferenceEquals(state.Item, item))
        {
            return;
        }

        state.Offset = 0;
        state.AnimationStartedAt = Stopwatch.GetTimestamp();
        state.TextBlock.Width = double.NaN;
        state.ScrollViewer.ChangeView(0, null, null, disableAnimation: true);
        if (!item.IsPlaying)
        {
            state.Timer.Stop();
            return;
        }

        SetNaturalTextWidth(state);
        state.Timer.Start();
    }

    private void PlaybackButtonMarquee_Tick(PlaybackButtonState state)
    {
        var scrollViewer = state.ScrollViewer;
        var maximumOffset = scrollViewer.ScrollableWidth;
        if (maximumOffset <= 2)
        {
            SetNaturalTextWidth(state);
            return;
        }

        const double cycleDurationSeconds = 6;
        var elapsedSeconds = (Stopwatch.GetTimestamp() - state.AnimationStartedAt) /
            (double)Stopwatch.Frequency;
        var progress = elapsedSeconds % cycleDurationSeconds / cycleDurationSeconds;
        var easedPosition = (1 - Math.Cos(progress * 2 * Math.PI)) / 2;
        state.Offset = easedPosition * maximumOffset;
        scrollViewer.ChangeView(state.Offset, null, null, disableAnimation: true);
    }

    private static void SetNaturalTextWidth(PlaybackButtonState state)
    {
        state.TextBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desiredWidth = state.TextBlock.DesiredSize.Width;
        if (double.IsFinite(desiredWidth) && desiredWidth > state.ScrollViewer.ViewportWidth)
            state.TextBlock.Width = desiredWidth;
    }

    private void TrackPlaybackState()
    {
        var session = PlayerElement.MediaPlayer?.PlaybackSession;
        if (ReferenceEquals(session, _trackedPlaybackSession))
            return;

        StopTrackingPlaybackState();
        _trackedPlaybackSession = session;
        if (_trackedPlaybackSession is not null)
            _trackedPlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
    }

    private void StopTrackingPlaybackState()
    {
        if (_trackedPlaybackSession is not null)
            _trackedPlaybackSession.PlaybackStateChanged -= PlaybackSession_PlaybackStateChanged;
        _trackedPlaybackSession = null;
    }

    private void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        var isPlaying = sender.PlaybackState == MediaPlaybackState.Playing;
        var item = _currentPlayingItem;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(item, _currentPlayingItem) && item is not null)
                item.IsPlaying = isPlaying;
        });
    }
}
