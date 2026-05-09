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

public partial class DownloadItemViewModel : ObservableObject
{
    private readonly IDownloadService _downloadService;
    private CancellationTokenSource? _cts;

    public string Url { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(IsFetchingVisibility))]
    public partial bool IsFetching { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(IsNotDownloading))]
    [NotifyPropertyChangedFor(nameof(IsDownloadingVisibility))]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoveVisibility))]
    public partial bool IsQueueMode { get; set; }

    public Microsoft.UI.Xaml.Visibility RemoveVisibility => IsQueueMode ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public bool IsNotBusy => !IsFetching && !IsDownloading;
    public bool IsNotDownloading => !IsDownloading;

    public Microsoft.UI.Xaml.Visibility IsFetchingVisibility => IsFetching ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility IsDownloadingVisibility => IsDownloading ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetadata))]
    [NotifyPropertyChangedFor(nameof(HasMetadataVisibility))]
    public partial VideoMetadata? Metadata { get; set; }

    public bool HasMetadata => Metadata != null;
    public Microsoft.UI.Xaml.Visibility HasMetadataVisibility => HasMetadata ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideoOptionsVisibility))]
    public partial bool IsMp4Selected { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideoOptionsVisibility))]
    public partial bool IsMovSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAudioOptionsVisibility))]
    public partial bool IsAudioSelected { get; set; }

    public Microsoft.UI.Xaml.Visibility IsVideoOptionsVisibility => (IsMp4Selected || IsMovSelected) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility IsAudioOptionsVisibility => IsAudioSelected ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    partial void OnIsMp4SelectedChanged(bool value)
    {
        if (value) { IsAudioSelected = false; IsMovSelected = false; }
        else if (!IsMovSelected && !IsAudioSelected) IsMp4Selected = true;
    }

    partial void OnIsMovSelectedChanged(bool value)
    {
        if (value) { IsAudioSelected = false; IsMp4Selected = false; }
        else if (!IsMp4Selected && !IsAudioSelected) IsMovSelected = true;
    }

    partial void OnIsAudioSelectedChanged(bool value)
    {
        if (value) { IsMp4Selected = false; IsMovSelected = false; }
        else if (!IsMp4Selected && !IsMovSelected) IsAudioSelected = true;
    }

    [ObservableProperty]
    public partial ObservableCollection<VideoFormat> AvailableFormats { get; set; } = new();

    [ObservableProperty]
    public partial VideoFormat? SelectedFormat { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<AudioBitrate> AvailableAudioBitrates { get; set; } = new()
    {
        new AudioBitrate { Name = "Alta (320 kbps)", Value = "320" },
        new AudioBitrate { Name = "Padrão (192 kbps)", Value = "192" },
        new AudioBitrate { Name = "Baixa (128 kbps)", Value = "128" }
    };

    [ObservableProperty]
    public partial AudioBitrate? SelectedAudioBitrate { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Aguardando na fila...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpenLocationVisibility))]
    public partial bool IsDownloadComplete { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    public partial bool IsError { get; set; }

    public Microsoft.UI.Xaml.Media.SolidColorBrush StatusColor => new Microsoft.UI.Xaml.Media.SolidColorBrush(
        IsError ? Microsoft.UI.Colors.Red : 
        IsDownloadComplete ? Microsoft.UI.Colors.LightGreen : 
        Microsoft.UI.Colors.LightGray);

    public Microsoft.UI.Xaml.Visibility OpenLocationVisibility => IsDownloadComplete ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string DownloadedFolderPath { get; set; } = string.Empty;

    public Action<DownloadItemViewModel>? OnRemoveRequested { get; set; }

    public DownloadItemViewModel(string url, IDownloadService downloadService)
    {
        Url = url;
        _downloadService = downloadService;
        SelectedAudioBitrate = AvailableAudioBitrates[0];
    }

    [RelayCommand]
    private void Remove()
    {
        _cts?.Cancel();
        OnRemoveRequested?.Invoke(this);
    }

    [RelayCommand]
    private void OpenLocation()
    {
        if (!string.IsNullOrEmpty(DownloadedFolderPath))
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", DownloadedFolderPath);
            }
            catch { }
        }
    }

    public async Task FetchMetadataAsync()
    {
        IsFetching = true;
        StatusMessage = "Analisando link...";
        
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            var meta = await _downloadService.FetchMetadataAsync(Url, _cts.Token);
            if (meta != null)
            {
                Metadata = meta;
                
                var cleanFormats = meta.Formats
                    .Where(f => f.HasVideo && !string.IsNullOrEmpty(f.Resolution) && f.Resolution != "audio only")
                    .OrderByDescending(f => f.Height ?? 0)
                    .ThenByDescending(f => f.Fps)
                    .ToList();

                var grouped = cleanFormats.GroupBy(f => f.Resolution).Select(g => g.First()).ToList();

                foreach (var f in grouped.Take(15))
                {
                    AvailableFormats.Add(f);
                }

                if (AvailableFormats.Any())
                {
                    SelectedFormat = AvailableFormats.First();
                }

                StatusMessage = "Pronto.";
                IsError = false;
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Análise cancelada.";
            IsError = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            IsError = true;
        }
        finally
        {
            IsFetching = false;
        }
    }

    public async Task StartDownloadAsync(string destinationFolder, CancellationToken masterToken)
    {
        if (IsDownloading || IsDownloadComplete || Metadata == null) return;

        IsDownloading = true;
        IsError = false;
        Progress = 0;
        StatusMessage = "Iniciando download...";

        _cts = CancellationTokenSource.CreateLinkedTokenSource(masterToken);

        try
        {
            var formatId = (IsMp4Selected || IsMovSelected) ? SelectedFormat?.FormatId : "bestaudio";
            var isAudio = IsAudioSelected;
            var audioBitrate = isAudio ? (SelectedAudioBitrate?.Value ?? "192") : "";
            var videoExt = IsMovSelected ? "mov" : "mp4";

            var progressHandler = new Progress<double>(p => Progress = p);

            DownloadedFolderPath = destinationFolder;

            var resultPath = await _downloadService.DownloadAsync(
                Url,
                formatId ?? "best",
                isAudio,
                audioBitrate,
                videoExt,
                destinationFolder,
                progressHandler,
                _cts.Token
            );

            StatusMessage = "Concluído!";
            Progress = 100;
            IsDownloadComplete = true;

            // Define final path, fallback to folder if not parsed
            if (string.IsNullOrEmpty(resultPath))
            {
                // try to guess or just use folder
                resultPath = destinationFolder; 
            }
            
            // Add to history
            var historyItem = new HistoryItem
            {
                Title = Metadata.Title,
                Url = Url,
                Thumbnail = Metadata.Thumbnail,
                LocalFilePath = resultPath,
                DownloadedAt = DateTime.Now
            };
            await HistoryService.AddHistoryItemAsync(historyItem);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download cancelado.";
            IsError = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Falha: {ex.Message}";
            IsError = true;
        }
        finally
        {
            IsDownloading = false;
        }
    }
}
