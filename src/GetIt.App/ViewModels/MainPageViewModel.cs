using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GetIt_App.Services;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;

namespace GetIt_App.ViewModels;

public class AudioBitrate
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public partial class MainPageViewModel : ObservableObject
{
    private readonly IDownloadService _downloadService;
    private readonly DownloadQueueService _queueService;
    private readonly DownloadQueuePersistenceService _queuePersistenceService;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
    private readonly HashSet<DownloadItemViewModel> _observedItems = new();
    private string _lastClipboardText = string.Empty;
    private bool _isClipboardMonitoringActive;
    private bool _isClipboardSubscribed;
    private bool _isReadingClipboard;
    private bool _isInitializing = true;
    private bool _isShuttingDown;
    private bool _queueRestoreInitialized;
    private bool _queuePersistenceReady;
    private CancellationTokenSource? _queueSaveDebounce;
    private Task _queueSaveTask = Task.CompletedTask;
    private TaskCompletionSource? _queueOperationFinished;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddUrl))]
    [NotifyCanExecuteChangedFor(nameof(AddUrlCommand))]
    public partial string Url { get; set; } = string.Empty;

    public ObservableCollection<DownloadItemViewModel> DownloadQueue { get; } = new();
    public IReadOnlyList<DownloadPresetOption> AvailableDownloadPresets => DownloadPresetPolicy.Options;

    [ObservableProperty]
    public partial DownloadPresetOption? SelectedDownloadPreset { get; set; } = DownloadPresetPolicy.Options[0];

    public bool IsQueuePersistenceEnabled { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotDownloading))]
    [NotifyPropertyChangedFor(nameof(IsDownloadingVisibility))]
    [NotifyPropertyChangedFor(nameof(IsNotDownloadingVisibility))]
    [NotifyPropertyChangedFor(nameof(CanAddUrl))]
    [NotifyPropertyChangedFor(nameof(CanStartDownloads))]
    [NotifyPropertyChangedFor(nameof(CanClearQueue))]
    [NotifyPropertyChangedFor(nameof(CanChangeFolder))]
    [NotifyPropertyChangedFor(nameof(CanCancelDownloads))]
    [NotifyPropertyChangedFor(nameof(DownloadButtonText))]
    [NotifyPropertyChangedFor(nameof(QueueSummary))]
    [NotifyCanExecuteChangedFor(nameof(AddUrlCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearQueueCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelAllCommand))]
    public partial bool IsDownloadingAll { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancelDownloads))]
    [NotifyPropertyChangedFor(nameof(QueueSummary))]
    [NotifyCanExecuteChangedFor(nameof(CancelAllCommand))]
    public partial bool IsCancelling { get; set; }

    [ObservableProperty]
    public partial string InputError { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EngineStatusVisibility))]
    public partial string EngineStatusMessage { get; set; } = string.Empty;

    public bool CanAddUrl => !_isShuttingDown && !IsDownloadingAll && !string.IsNullOrWhiteSpace(Url);
    public bool CanStartDownloads => !_isShuttingDown && !IsDownloadingAll &&
        DownloadQueue.Any(item => item.HasMetadata && !item.IsDownloadComplete && !item.IsDownloading) &&
        !DownloadQueue.Any(item => item.IsFetching || item.IsDownloading);
    public bool CanClearQueue => !_isShuttingDown && !IsDownloadingAll && DownloadQueue.Count > 0;
    public bool CanChangeFolder => !_isShuttingDown && !IsDownloadingAll;
    public bool CanCancelDownloads => IsDownloadingAll && !IsCancelling;
    public bool IsNotDownloading => !IsDownloadingAll;
    public Visibility IsDownloadingVisibility => IsDownloadingAll ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsNotDownloadingVisibility => IsDownloadingAll ? Visibility.Collapsed : Visibility.Visible;
    public Visibility HasItemsVisibility => DownloadQueue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsQueueEmptyVisibility => DownloadQueue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EngineStatusVisibility => string.IsNullOrWhiteSpace(EngineStatusMessage) ? Visibility.Collapsed : Visibility.Visible;
    public string DownloadButtonText => IsDownloadingAll ? "Baixando…" : DownloadQueue.Count > 1 ? "Baixar fila" : "Baixar";

    public string QueueSummary
    {
        get
        {
            var count = DownloadQueue.Count;
            if (count == 0) return "Nenhum item na fila";
            if (IsCancelling) return "Cancelando downloads…";
            var completed = DownloadQueue.Count(item => item.IsDownloadComplete);
            if (IsDownloadingAll) return $"{completed} de {count} concluídos · download em andamento";
            var fetching = DownloadQueue.Count(item => item.IsFetching);
            if (fetching > 0) return $"{count} na fila · {fetching} em análise";
            if (completed == count) return count == 1 ? "Download concluído" : $"{count} downloads concluídos";
            var errors = DownloadQueue.Count(item => item.IsError);
            return errors > 0 ? $"{count} na fila · {errors} com erro" : count == 1 ? "1 item pronto para baixar" : $"{count} itens na fila";
        }
    }

    [ObservableProperty]
    public partial string DownloadFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAutoPasteEnabled { get; set; }

    public MainPageViewModel() : this(new DownloadService())
    {
    }

    public MainPageViewModel(IDownloadService downloadService)
    {
        _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));
        _queueService = new DownloadQueueService();
        _queuePersistenceService = new DownloadQueuePersistenceService();
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        DownloadQueue.CollectionChanged += DownloadQueue_CollectionChanged;
        var settings = SettingsService.LoadSettings();
        IsQueuePersistenceEnabled = settings.RestoreQueueOnStartup;
        SettingsService.QueuePersistenceSettingChanged += QueuePersistenceSettingChanged;
        DownloadFolder = string.IsNullOrEmpty(settings.LastDownloadFolder)
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : settings.LastDownloadFolder;
        IsAutoPasteEnabled = settings.IsAutoPasteEnabled;
        InputError = SettingsService.LastLoadWarning ?? string.Empty;
        _isInitializing = false;
        if (_downloadService is IDownloadEngineHealthProvider engineHealthProvider)
        {
            _ = CheckEngineHealthAsync(engineHealthProvider);
        }
    }

    private async Task CheckEngineHealthAsync(IDownloadEngineHealthProvider engineHealthProvider)
    {
        try
        {
            EngineStatusMessage = await engineHealthProvider.GetEngineProblemAsync() ?? string.Empty;
        }
        catch (Exception exception)
        {
            AppLogger.LogException(exception, "Could not check the download engine at startup");
            EngineStatusMessage = "Não foi possível verificar a engine de download. Confira se yt-dlp.exe, ffmpeg.exe e ffprobe.exe estão em Engine\\Bin e tente novamente.";
        }
    }

    public void ActivateClipboardMonitoring()
    {
        if (_isShuttingDown) return;
        _isClipboardMonitoringActive = true;
        UpdateClipboardSubscription();
        if (IsAutoPasteEnabled) _ = ReadClipboardIfAppropriateAsync();
    }

    public void DeactivateClipboardMonitoring()
    {
        _isClipboardMonitoringActive = false;
        UpdateClipboardSubscription();
    }

    private void UpdateClipboardSubscription()
    {
        var shouldSubscribe = _isClipboardMonitoringActive && IsAutoPasteEnabled && !_isShuttingDown;
        if (shouldSubscribe == _isClipboardSubscribed) return;
        if (shouldSubscribe) Clipboard.ContentChanged += Clipboard_ContentChanged;
        else Clipboard.ContentChanged -= Clipboard_ContentChanged;
        _isClipboardSubscribed = shouldSubscribe;
    }

    private void Clipboard_ContentChanged(object? sender, object e) =>
        _dispatcherQueue.TryEnqueue(() => _ = ReadClipboardIfAppropriateAsync());

    private async Task ReadClipboardIfAppropriateAsync()
    {
        if (_isReadingClipboard || !_isClipboardSubscribed || IsDownloadingAll || !string.IsNullOrWhiteSpace(Url)) return;
        _isReadingClipboard = true;
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;
            var copiedText = await content.GetTextAsync();
            if (!_isClipboardSubscribed || IsDownloadingAll || !string.IsNullOrWhiteSpace(Url) ||
                !MediaUrlPolicy.TryNormalizeHttpUrl(copiedText, out var normalized) ||
                normalized == _lastClipboardText || !MediaUrlPolicy.IsYouTubeVideoUrl(normalized)) return;
            _lastClipboardText = normalized;
            Url = normalized;
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "Clipboard access failed while auto-pasting a link");
        }
        finally { _isReadingClipboard = false; }
    }

    partial void OnIsAutoPasteEnabledChanged(bool value)
    {
        if (!_isInitializing)
        {
            try
            {
                SettingsService.UpdateSettings(settings => settings.IsAutoPasteEnabled = value);
            }
            catch (JsonFilePersistenceException)
            {
                InputError = "Não foi possível salvar as configurações. O arquivo anterior foi preservado; verifique as permissões e o espaço disponível.";
            }
        }
        UpdateClipboardSubscription();
        if (value && _isClipboardMonitoringActive) _ = ReadClipboardIfAppropriateAsync();
    }

    partial void OnUrlChanged(string value) => InputError = string.Empty;

    [RelayCommand(CanExecute = nameof(CanAddUrl))]
    private void AddUrl()
    {
        if (!CanAddUrl) return;
        if (!MediaUrlPolicy.TryNormalizeHttpUrl(Url, out var link))
        {
            InputError = "Digite um endereço válido começando com https:// ou http://.";
            return;
        }
        if (DownloadQueue.Any(item => string.Equals(item.Url, link, StringComparison.Ordinal)))
        {
            InputError = "Este link já foi adicionado à fila.";
            return;
        }
        Url = string.Empty;
        var item = CreateQueueItem(link);
        _ = FetchMetadataAndRefreshEngineAsync(item);
    }

    public BatchUrlPreview PreviewBatchUrls(string? input) =>
        BatchUrlPolicy.CreatePreview(input, DownloadQueue.Select(item => item.Url));

    public int AddBatchUrls(string? input)
    {
        if (_isShuttingDown || IsDownloadingAll)
            return 0;

        var preview = PreviewBatchUrls(input);
        if (preview.IsOverLimit)
        {
            InputError = $"O lote contém {preview.NonEmptyLineCount} links. O limite é {BatchUrlPolicy.MaximumLinks}; divida-o em lotes menores.";
            return 0;
        }

        var readyUrls = new List<string>();
        foreach (var entry in preview.Entries)
        {
            if (entry.Status == BatchUrlStatus.Ready && entry.NormalizedUrl is { } normalizedUrl)
                readyUrls.Add(normalizedUrl);
        }

        if (readyUrls.Count == 0)
        {
            InputError = "Nenhum link válido e novo para adicionar. Confira a prévia e tente novamente.";
            return 0;
        }

        var addedItems = new List<DownloadItemViewModel>(readyUrls.Count);
        foreach (var url in readyUrls)
            addedItems.Add(CreateQueueItem(url));

        InputError = string.Empty;
        _ = FetchBatchMetadataAndRefreshEngineAsync(addedItems);
        return addedItems.Count;
    }

    private DownloadItemViewModel CreateQueueItem(string link)
    {
        var item = new DownloadItemViewModel(link, _downloadService);
        item.ApplyPreset(SelectedDownloadPreset?.Preset ?? DownloadPreset.VideoMp4);
        item.OnRemoveRequested = i => DownloadQueue.Remove(i);
        item.OnRetryDownloadRequested = RetryDownloadAsync;
        DownloadQueue.Add(item);
        return item;
    }

    private async Task RetryDownloadAsync(DownloadItemViewModel item, bool saveAsNewCopy)
    {
        if (_isShuttingDown || IsDownloadingAll)
            return;

        IsCancelling = false;
        IsDownloadingAll = true;
        _queueOperationFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await _queueService.RetryItemAsync(DownloadQueue, item, DownloadFolder, saveAsNewCopy);
        }
        finally
        {
            IsDownloadingAll = false;
            IsCancelling = false;
            _queueOperationFinished.TrySetResult();
            _queueOperationFinished = null;
        }
    }

    private async Task FetchMetadataAndRefreshEngineAsync(DownloadItemViewModel item)
    {
        await item.FetchMetadataAsync();
        if (!_isShuttingDown && _downloadService is IDownloadEngineHealthProvider engineHealthProvider)
        {
            await CheckEngineHealthAsync(engineHealthProvider);
        }
    }

    private async Task FetchBatchMetadataAndRefreshEngineAsync(IReadOnlyCollection<DownloadItemViewModel> items)
    {
        await Task.WhenAll(items.Select(item => item.FetchMetadataAsync()));
        if (!_isShuttingDown && _downloadService is IDownloadEngineHealthProvider engineHealthProvider)
            await CheckEngineHealthAsync(engineHealthProvider);
    }

    private void DownloadQueue_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var removed in _observedItems.Where(item => !DownloadQueue.Contains(item)).ToList())
        {
            removed.PropertyChanged -= Item_PropertyChanged;
            _observedItems.Remove(removed);
        }
        foreach (var added in DownloadQueue.Where(item => !_observedItems.Contains(item)))
        {
            added.PropertyChanged += Item_PropertyChanged;
            _observedItems.Add(added);
        }
        NotifyQueueStateChanged();
        ScheduleQueueSave();
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadItemViewModel.IsFetching) or nameof(DownloadItemViewModel.HasMetadata)
            or nameof(DownloadItemViewModel.IsDownloadComplete) or nameof(DownloadItemViewModel.IsDownloading)
            or nameof(DownloadItemViewModel.IsError)) NotifyQueueStateChanged();

        if (e.PropertyName == nameof(DownloadItemViewModel.IsDownloadComplete))
            SaveQueueImmediately();
        else if (e.PropertyName is nameof(DownloadItemViewModel.IsMp4Selected)
            or nameof(DownloadItemViewModel.IsMovSelected)
            or nameof(DownloadItemViewModel.IsMp3Selected)
            or nameof(DownloadItemViewModel.IsWavSelected)
            or nameof(DownloadItemViewModel.IsFlacSelected)
            or nameof(DownloadItemViewModel.SelectedFormat)
            or nameof(DownloadItemViewModel.SelectedAudioBitrate))
            ScheduleQueueSave();
    }

    private void NotifyQueueStateChanged()
    {
        OnPropertyChanged(nameof(HasItemsVisibility));
        OnPropertyChanged(nameof(DownloadButtonText));
        OnPropertyChanged(nameof(IsQueueEmptyVisibility));
        OnPropertyChanged(nameof(CanStartDownloads));
        OnPropertyChanged(nameof(CanClearQueue));
        OnPropertyChanged(nameof(QueueSummary));
        DownloadAllCommand.NotifyCanExecuteChanged();
        ClearQueueCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanClearQueue))]
    private void ClearQueue()
    {
        if (CanClearQueue) _queueService.ClearQueue(DownloadQueue);
    }

    [RelayCommand(CanExecute = nameof(CanChangeFolder))]
    private async Task ChangeFolderAsync()
    {
        if (!CanChangeFolder) return;
        var picker = new Windows.Storage.Pickers.FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null && CanChangeFolder)
        {
            DownloadFolder = folder.Path;
            try
            {
                SettingsService.UpdateSettings(settings => settings.LastDownloadFolder = DownloadFolder);
            }
            catch (JsonFilePersistenceException)
            {
                InputError = "A pasta foi alterada nesta sessão, mas não foi possível salvar a preferência. O arquivo anterior foi preservado.";
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartDownloads))]
    private async Task DownloadAllAsync()
    {
        if (!CanStartDownloads) return;
        IsCancelling = false;
        IsDownloadingAll = true;
        _queueOperationFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try { await _queueService.DownloadAllAsync(DownloadQueue, DownloadFolder); }
        finally
        {
            IsDownloadingAll = false;
            IsCancelling = false;
            _queueOperationFinished.TrySetResult();
            _queueOperationFinished = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelDownloads))]
    private void CancelAll()
    {
        if (!CanCancelDownloads) return;
        IsCancelling = true;
        _queueService.CancelAll(DownloadQueue);
    }

    public async Task ShutdownAsync()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;
        DeactivateClipboardMonitoring();
        _queueService.CancelAll(DownloadQueue);
        if (_queueOperationFinished is { } queueOperationFinished)
            await queueOperationFinished.Task;
        SettingsService.QueuePersistenceSettingChanged -= QueuePersistenceSettingChanged;
        _queueSaveDebounce?.Cancel();
        _queueSaveDebounce?.Dispose();
        _queueSaveDebounce = null;
        try
        {
            await _queueSaveTask;
            if (IsQueuePersistenceEnabled && _queuePersistenceReady)
                await SaveQueueSnapshotAsync();
        }
        catch (JsonFilePersistenceException exception)
        {
            AppLogger.LogException(exception, "Failed to persist the download queue during shutdown");
        }
        foreach (var item in _observedItems) item.PropertyChanged -= Item_PropertyChanged;
        _observedItems.Clear();
        DownloadQueue.CollectionChanged -= DownloadQueue_CollectionChanged;
    }

    public async Task<DownloadQueueLoadResult?> LoadSavedQueueAsync()
    {
        if (!IsQueuePersistenceEnabled || _queueRestoreInitialized)
            return null;

        _queueRestoreInitialized = true;
        var result = await _queuePersistenceService.LoadAsync();
        if (result.Snapshot.Items.Count > 0)
            return result;

        _queuePersistenceReady = true;
        ScheduleQueueSave();
        return null;
    }

    public void RestoreSavedQueue(DownloadQueueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_isShuttingDown || !IsQueuePersistenceEnabled)
            return;

        var restoredItems = new List<(DownloadItemViewModel Item, PersistedQueueItem Preferences)>();
        foreach (var preferences in snapshot.Items)
        {
            if (DownloadQueue.Any(item => string.Equals(item.Url, preferences.Url, StringComparison.Ordinal)))
                continue;

            var item = CreateQueueItem(preferences.Url);
            item.RestoreQueuePreferences(preferences);
            restoredItems.Add((item, preferences));
        }

        _queueRestoreInitialized = true;
        _queuePersistenceReady = true;
        _ = FetchRestoredMetadataAsync(restoredItems);
    }

    public async Task DiscardSavedQueueAsync()
    {
        await _queuePersistenceService.DiscardAsync();
        _queueRestoreInitialized = true;
        _queuePersistenceReady = true;
        ScheduleQueueSave();
    }

    public void DeferSavedQueueRestoration()
    {
        _queueRestoreInitialized = true;
        _queuePersistenceReady = false;
        InputError = "A fila salva foi mantida. Restaure ou descarte-a para salvar alterações da fila atual.";
    }

    private async Task FetchRestoredMetadataAsync(
        IReadOnlyCollection<(DownloadItemViewModel Item, PersistedQueueItem Preferences)> items)
    {
        await Task.WhenAll(items.Select(async entry =>
        {
            await entry.Item.FetchMetadataAsync();
            if (entry.Item.IsError)
                entry.Item.StatusMessage = "Não foi possível atualizar os dados deste link. Tente analisar novamente.";
        }));
        if (!_isShuttingDown && _downloadService is IDownloadEngineHealthProvider engineHealthProvider)
            await CheckEngineHealthAsync(engineHealthProvider);
        ScheduleQueueSave();
    }

    private void QueuePersistenceSettingChanged(bool enabled)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            ApplyQueuePersistenceSetting(enabled);
            return;
        }

        _dispatcherQueue.TryEnqueue(() => ApplyQueuePersistenceSetting(enabled));
    }

    private void ApplyQueuePersistenceSetting(bool enabled)
    {
        if (_isShuttingDown || IsQueuePersistenceEnabled == enabled)
            return;

        IsQueuePersistenceEnabled = enabled;
        _queueRestoreInitialized = false;
        _queuePersistenceReady = false;
        if (enabled)
            return;

        _queueSaveDebounce?.Cancel();
        _queueSaveDebounce?.Dispose();
        _queueSaveDebounce = null;
    }

    private void ScheduleQueueSave()
    {
        if (!IsQueuePersistenceEnabled || !_queuePersistenceReady || _isShuttingDown)
            return;

        _queueSaveDebounce?.Cancel();
        _queueSaveDebounce?.Dispose();
        _queueSaveDebounce = new CancellationTokenSource();
        _queueSaveTask = SaveQueueAfterDelayAsync(_queueSaveDebounce.Token);
    }

    private void SaveQueueImmediately()
    {
        if (!IsQueuePersistenceEnabled || !_queuePersistenceReady || _isShuttingDown)
            return;

        _queueSaveDebounce?.Cancel();
        _queueSaveDebounce?.Dispose();
        _queueSaveDebounce = null;
        _queueSaveTask = SaveQueueImmediatelyAsync();
    }

    private async Task SaveQueueImmediatelyAsync()
    {
        try
        {
            await SaveQueueSnapshotAsync();
        }
        catch (JsonFilePersistenceException exception)
        {
            AppLogger.LogException(exception, "Failed to persist a completed download state");
            InputError = "Não foi possível atualizar a fila salva. A cópia anterior foi preservada; verifique permissões e espaço disponível.";
        }
    }

    private async Task SaveQueueAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await SaveQueueSnapshotAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (JsonFilePersistenceException exception)
        {
            AppLogger.LogException(exception, "Failed to persist the download queue");
            InputError = "Não foi possível salvar a fila. A cópia anterior foi preservada; verifique permissões e espaço disponível.";
        }
    }

    private Task SaveQueueSnapshotAsync()
    {
        var snapshot = new DownloadQueueSnapshot
        {
            Items = DownloadQueue
                .Where(item => !item.IsDownloadComplete)
                .Select(item => item.CaptureQueuePreferences())
                .ToList()
        };
        return _queuePersistenceService.SaveAsync(snapshot);
    }
}
