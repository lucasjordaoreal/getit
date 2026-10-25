using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
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
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
    private readonly HashSet<DownloadItemViewModel> _observedItems = new();
    private string _lastClipboardText = string.Empty;
    private bool _isClipboardMonitoringActive;
    private bool _isClipboardSubscribed;
    private bool _isReadingClipboard;
    private bool _isInitializing = true;
    private bool _isShuttingDown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddUrl))]
    [NotifyCanExecuteChangedFor(nameof(AddUrlCommand))]
    public partial string Url { get; set; } = string.Empty;

    public ObservableCollection<DownloadItemViewModel> DownloadQueue { get; } = new();

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
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        DownloadQueue.CollectionChanged += DownloadQueue_CollectionChanged;
        var settings = SettingsService.LoadSettings();
        DownloadFolder = string.IsNullOrEmpty(settings.LastDownloadFolder)
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : settings.LastDownloadFolder;
        IsAutoPasteEnabled = settings.IsAutoPasteEnabled;
        _isInitializing = false;
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
            var settings = SettingsService.LoadSettings();
            settings.IsAutoPasteEnabled = value;
            SettingsService.SaveSettings(settings);
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
        var item = new DownloadItemViewModel(link, _downloadService);
        item.OnRemoveRequested = i => DownloadQueue.Remove(i);
        DownloadQueue.Add(item);
        _ = item.FetchMetadataAsync();
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
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadItemViewModel.IsFetching) or nameof(DownloadItemViewModel.HasMetadata)
            or nameof(DownloadItemViewModel.IsDownloadComplete) or nameof(DownloadItemViewModel.IsDownloading)
            or nameof(DownloadItemViewModel.IsError)) NotifyQueueStateChanged();
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
            var settings = SettingsService.LoadSettings();
            settings.LastDownloadFolder = DownloadFolder;
            SettingsService.SaveSettings(settings);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartDownloads))]
    private async Task DownloadAllAsync()
    {
        if (!CanStartDownloads) return;
        IsCancelling = false;
        IsDownloadingAll = true;
        try { await _queueService.DownloadAllAsync(DownloadQueue, DownloadFolder); }
        finally
        {
            IsDownloadingAll = false;
            IsCancelling = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelDownloads))]
    private void CancelAll()
    {
        if (!CanCancelDownloads) return;
        IsCancelling = true;
        _queueService.CancelAll(DownloadQueue);
    }

    public void Shutdown()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;
        DeactivateClipboardMonitoring();
        _queueService.CancelAll(DownloadQueue);
        foreach (var item in _observedItems) item.PropertyChanged -= Item_PropertyChanged;
        _observedItems.Clear();
        DownloadQueue.CollectionChanged -= DownloadQueue_CollectionChanged;
    }
}
