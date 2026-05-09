using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltraDownloader_App.Models;
using UltraDownloader_App.Services;

namespace UltraDownloader_App.ViewModels;

public class AudioBitrate
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public partial class MainPageViewModel : ObservableObject
{
    private readonly IDownloadService _downloadService;
    private CancellationTokenSource? _globalCts;

    [ObservableProperty]
    public partial string Url { get; set; } = string.Empty;

    public ObservableCollection<DownloadItemViewModel> DownloadQueue { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotDownloading))]
    [NotifyPropertyChangedFor(nameof(IsDownloadingVisibility))]
    public partial bool IsDownloadingAll { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QueueClearText))]
    [NotifyPropertyChangedFor(nameof(QueueDownloadText))]
    public partial bool IsQueueMode { get; set; }

    public string QueueClearText => IsQueueMode ? "Limpar Fila" : "Limpar";
    public string QueueDownloadText => IsQueueMode ? "Baixar Fila" : "Baixar Agora";

    partial void OnIsQueueModeChanged(bool value)
    {
        if (!value && DownloadQueue.Count > 1)
        {
            var first = DownloadQueue.First();
            DownloadQueue.Clear();
            DownloadQueue.Add(first);
        }

        foreach (var item in DownloadQueue)
        {
            item.IsQueueMode = value;
        }
    }

    public bool IsNotDownloading => !IsDownloadingAll;
    public Microsoft.UI.Xaml.Visibility IsDownloadingVisibility => IsDownloadingAll ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility HasItemsVisibility => DownloadQueue.Any() ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    [ObservableProperty]
    public partial string DownloadFolder { get; set; } = string.Empty;

    public MainPageViewModel()
    {
        _downloadService = new DownloadService();
        DownloadQueue.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasItemsVisibility));
        
        var settings = SettingsService.LoadSettings();
        DownloadFolder = settings.LastDownloadFolder;
        if (string.IsNullOrEmpty(DownloadFolder))
        {
            DownloadFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    partial void OnUrlChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && (value.StartsWith("http") || value.StartsWith("www.")))
        {
            if (!IsQueueMode)
            {
                ClearQueue();
            }
            AddNewDownloadItem(value);
            // Limpa o campo para poder colar mais
            Url = string.Empty;
        }
    }

    private void AddNewDownloadItem(string link)
    {
        var item = new DownloadItemViewModel(link, _downloadService);
        item.IsQueueMode = IsQueueMode;
        item.OnRemoveRequested = i => DownloadQueue.Remove(i);
        DownloadQueue.Add(item);
        
        // Start fetching metadata
        _ = item.FetchMetadataAsync();
    }

    [RelayCommand]
    private void ClearQueue()
    {
        _globalCts?.Cancel();
        DownloadQueue.Clear();
        IsDownloadingAll = false;
    }

    [RelayCommand]
    private async Task ChangeFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        var hwnd = App.WindowHandle;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        // Se já tiver uma pasta, tenta iniciar nela ou arredores
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            DownloadFolder = folder.Path;
            var settings = SettingsService.LoadSettings();
            settings.LastDownloadFolder = DownloadFolder;
            SettingsService.SaveSettings(settings);
        }
    }

    [RelayCommand]
    private async Task DownloadAllAsync()
    {
        if (!DownloadQueue.Any()) return;

        IsDownloadingAll = true;

        _globalCts?.Cancel();
        _globalCts = new CancellationTokenSource();

        try
        {
            foreach (var item in DownloadQueue.ToList())
            {
                if (_globalCts.Token.IsCancellationRequested) break;
                
                if (item.HasMetadata && !item.IsDownloadComplete)
                {
                    await item.StartDownloadAsync(DownloadFolder, _globalCts.Token);
                }
            }
        }
        finally
        {
            IsDownloadingAll = false;
        }
    }

    [RelayCommand]
    private void CancelAll()
    {
        _globalCts?.Cancel();
        IsDownloadingAll = false;
    }
}
