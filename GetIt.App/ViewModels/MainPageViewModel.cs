using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GetIt_App.Models;
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
    private CancellationTokenSource? _globalCts;
    private readonly DispatcherTimer _clipboardTimer;
    private string _lastClipboardText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddUrl))]
    public partial string Url { get; set; } = string.Empty;

    public ObservableCollection<DownloadItemViewModel> DownloadQueue { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotDownloading))]
    [NotifyPropertyChangedFor(nameof(IsDownloadingVisibility))]
    [NotifyPropertyChangedFor(nameof(CanAddUrl))]
    [NotifyPropertyChangedFor(nameof(CanStartDownloads))]
    public partial bool IsDownloadingAll { get; set; }

    [ObservableProperty]
    public partial string InputError { get; set; } = string.Empty;

    public bool CanAddUrl => !IsDownloadingAll && !string.IsNullOrWhiteSpace(Url);
    public bool CanStartDownloads => !IsDownloadingAll &&
        DownloadQueue.Any(item => item.HasMetadata && !item.IsDownloadComplete) &&
        !DownloadQueue.Any(item => item.IsFetching);

    public bool IsNotDownloading => !IsDownloadingAll;
    public Microsoft.UI.Xaml.Visibility IsDownloadingVisibility => IsDownloadingAll ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility HasItemsVisibility => DownloadQueue.Any() ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility IsQueueEmptyVisibility => DownloadQueue.Any() ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    [ObservableProperty]
    public partial string DownloadFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAutoPasteEnabled { get; set; }

    public MainPageViewModel()
    {
        _downloadService = new DownloadService();
        DownloadQueue.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasItemsVisibility));
            OnPropertyChanged(nameof(IsQueueEmptyVisibility));
            OnPropertyChanged(nameof(CanStartDownloads));
        };
        
        var settings = SettingsService.LoadSettings();
        DownloadFolder = settings.LastDownloadFolder;
        if (string.IsNullOrEmpty(DownloadFolder))
        {
            DownloadFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        _clipboardTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _clipboardTimer.Tick += ClipboardTimer_Tick;

        IsAutoPasteEnabled = settings.IsAutoPasteEnabled;

        if (IsAutoPasteEnabled)
        {
            _clipboardTimer.Start();
        }
    }

    private async void ClipboardTimer_Tick(object? sender, object e)
    {
        if (!IsAutoPasteEnabled) return;

        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                var text = await content.GetTextAsync();
                
                // Limpa espaços e quebras de linha
                text = text?.Trim() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(text) && text != _lastClipboardText)
                {
                    _lastClipboardText = text;

                    if (IsYouTubeLink(text))
                    {
                        // Só cola se o campo estiver vazio ou se for um novo link
                        // Mas OnUrlChanged já limpa o campo, então basta setar.
                        Url = text;
                    }
                }
            }
        }
        catch { /* Clipboard access might fail if another app is using it */ }
    }

    private bool IsYouTubeLink(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        
        return text.Contains("youtube.com/watch", StringComparison.OrdinalIgnoreCase) || 
               text.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("youtube.com/shorts", StringComparison.OrdinalIgnoreCase);
    }

    partial void OnIsAutoPasteEnabledChanged(bool value)
    {
        var settings = SettingsService.LoadSettings();
        settings.IsAutoPasteEnabled = value;
        SettingsService.SaveSettings(settings);

        if (value) _clipboardTimer.Start();
        else _clipboardTimer.Stop();
    }

    partial void OnUrlChanged(string value)
    {
        InputError = string.Empty;
    }

    [RelayCommand]
    private void AddUrl()
    {
        var link = Url.Trim();
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            InputError = "Digite um endereço válido começando com https:// ou http://.";
            return;
        }

        Url = string.Empty;
        InputError = string.Empty;

        var item = new DownloadItemViewModel(link, _downloadService);
        item.OnRemoveRequested = i => DownloadQueue.Remove(i);
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(DownloadItemViewModel.IsFetching) or
                nameof(DownloadItemViewModel.HasMetadata) or
                nameof(DownloadItemViewModel.IsDownloadComplete))
            {
                OnPropertyChanged(nameof(CanStartDownloads));
            }
        };

        _ = item.FetchMetadataAsync();
        DownloadQueue.Add(item);
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
        foreach (var item in DownloadQueue)
        {
            item.IsQueueLocked = true;
        }

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
            foreach (var item in DownloadQueue)
            {
                item.IsQueueLocked = false;
            }
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

