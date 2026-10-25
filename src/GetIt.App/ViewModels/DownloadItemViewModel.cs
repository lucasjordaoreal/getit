using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GetIt_App.Models;
using GetIt_App.Services;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace GetIt_App.ViewModels;

public partial class DownloadItemViewModel : ObservableObject
{
    private static readonly HttpClient ThumbnailHttpClient = CreateThumbnailHttpClient();
    private readonly IDownloadService _downloadService;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _thumbnailCts;
    private int _thumbnailRequestId;

    public string Url { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(IsFetchingVisibility))]
    public partial bool IsFetching { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(IsNotDownloading))]
    [NotifyPropertyChangedFor(nameof(IsDownloadingVisibility))]
    [NotifyPropertyChangedFor(nameof(CanEditQueue))]
    [NotifyPropertyChangedFor(nameof(CanRetryDownload))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareAnotherDownloadCommand))]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditQueue))]
    [NotifyPropertyChangedFor(nameof(IsNotDownloading))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareAnotherDownloadCommand))]
    public partial bool IsQueueLocked { get; set; }

    public bool IsNotBusy => !IsFetching && !IsDownloading;
    public bool IsNotDownloading => !IsDownloading && !IsQueueLocked;
    public bool CanEditQueue => !IsDownloading && !IsQueueLocked;

    public Microsoft.UI.Xaml.Visibility IsFetchingVisibility => IsFetching ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility IsDownloadingVisibility => IsDownloading ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetadata))]
    [NotifyPropertyChangedFor(nameof(HasMetadataVisibility))]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    public partial VideoMetadata? Metadata { get; set; }

    [ObservableProperty]
    public partial BitmapImage? ThumbnailImage { get; set; }

    public bool HasMetadata => Metadata != null;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Metadata?.Title) ? Url : Metadata.Title;
    public Microsoft.UI.Xaml.Visibility HasMetadataVisibility => HasMetadata ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    partial void OnMetadataChanged(VideoMetadata? value)
    {
        var requestId = Interlocked.Increment(ref _thumbnailRequestId);
        ThumbnailImage = null;
        _thumbnailCts?.Cancel();
        _thumbnailCts = null;

        if (!Uri.TryCreate(value?.Thumbnail, UriKind.Absolute, out var thumbnailUri) ||
            (thumbnailUri.Scheme != Uri.UriSchemeHttp && thumbnailUri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        var thumbnailCts = new CancellationTokenSource();
        _thumbnailCts = thumbnailCts;
        _ = LoadThumbnailAsync(value, thumbnailUri, requestId, thumbnailCts);
    }

    private static HttpClient CreateThumbnailHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) GetIt/1.0");
        return client;
    }

    private async Task LoadThumbnailAsync(VideoMetadata? metadata, Uri thumbnailUri, int requestId, CancellationTokenSource operation)
    {
        using (operation)
        {
            try
            {
                foreach (var candidate in GetThumbnailCandidates(metadata, thumbnailUri))
                {
                    try
                    {
                        var imageBytes = await ThumbnailHttpClient.GetByteArrayAsync(candidate, operation.Token);
                        using var stream = new InMemoryRandomAccessStream();
                        using (var writer = new DataWriter(stream))
                        {
                            writer.WriteBytes(imageBytes);
                            await writer.StoreAsync();
                            await writer.FlushAsync();
                            writer.DetachStream();
                        }
                        operation.Token.ThrowIfCancellationRequested();
                        stream.Seek(0);
                        var image = new BitmapImage { DecodePixelWidth = 256 };
                        await image.SetSourceAsync(stream);
                        if (requestId == Volatile.Read(ref _thumbnailRequestId) && !operation.IsCancellationRequested)
                            ThumbnailImage = image;
                        return;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        AppLogger.LogWarning($"Failed to load thumbnail from {candidate}: {ex.Message}");
                    }
                }
                AppLogger.LogWarning($"No thumbnail could be loaded for video {metadata?.Id ?? "unknown"}.");
            }
            finally
            {
                if (ReferenceEquals(_thumbnailCts, operation)) _thumbnailCts = null;
            }
        }
    }

    private static IEnumerable<Uri> GetThumbnailCandidates(VideoMetadata? metadata, Uri thumbnailUri)
    {
        if (!thumbnailUri.Host.EndsWith("ytimg.com", StringComparison.OrdinalIgnoreCase))
        {
            yield return thumbnailUri;
            yield break;
        }

        var compatibleUri = new UriBuilder(thumbnailUri);
        compatibleUri.Path = compatibleUri.Path.Replace("/vi_webp/", "/vi/", StringComparison.OrdinalIgnoreCase);
        if (compatibleUri.Path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            compatibleUri.Path = compatibleUri.Path[..^5] + ".jpg";
        }

        yield return compatibleUri.Uri;

        if (!string.IsNullOrWhiteSpace(metadata?.Id))
        {
            yield return new Uri($"https://i.ytimg.com/vi/{Uri.EscapeDataString(metadata.Id)}/hqdefault.jpg");
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideoOptionsVisibility))]
    public partial bool IsMp4Selected { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideoOptionsVisibility))]
    public partial bool IsMovSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMp3OptionsVisibility))]
    public partial bool IsMp3Selected { get; set; }

    public Microsoft.UI.Xaml.Visibility IsVideoOptionsVisibility => (IsMp4Selected || IsMovSelected) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility IsMp3OptionsVisibility => IsMp3Selected ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    partial void OnIsMp4SelectedChanged(bool value)
    {
        if (value) { IsMp3Selected = false; IsMovSelected = false; IsWavSelected = false; IsFlacSelected = false; }
        else if (!IsMovSelected && !IsMp3Selected && !IsWavSelected && !IsFlacSelected) IsMp4Selected = true;
    }

    partial void OnIsMovSelectedChanged(bool value)
    {
        if (value) { IsMp3Selected = false; IsMp4Selected = false; IsWavSelected = false; IsFlacSelected = false; }
        else if (!IsMp4Selected && !IsMp3Selected && !IsWavSelected && !IsFlacSelected) IsMovSelected = true;
    }

    partial void OnIsMp3SelectedChanged(bool value)
    {
        if (value) { IsMp4Selected = false; IsMovSelected = false; IsWavSelected = false; IsFlacSelected = false; }
        else if (!IsMp4Selected && !IsMovSelected && !IsWavSelected && !IsFlacSelected) IsMp3Selected = true;
    }

    [ObservableProperty]
    public partial bool IsWavSelected { get; set; }

    partial void OnIsWavSelectedChanged(bool value)
    {
        if (value) { IsMp4Selected = false; IsMovSelected = false; IsMp3Selected = false; IsFlacSelected = false; }
        else if (!IsMp4Selected && !IsMovSelected && !IsMp3Selected && !IsFlacSelected) IsWavSelected = true;
    }

    [ObservableProperty]
    public partial bool IsFlacSelected { get; set; }

    partial void OnIsFlacSelectedChanged(bool value)
    {
        if (value) { IsMp4Selected = false; IsMovSelected = false; IsMp3Selected = false; IsWavSelected = false; }
        else if (!IsMp4Selected && !IsMovSelected && !IsMp3Selected && !IsWavSelected) IsFlacSelected = true;
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
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string Speed { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Eta { get; set; } = string.Empty;

    public string ProgressText => $"{Math.Round(Progress, 1)}%";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Aguardando na fila...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpenLocationVisibility))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    public partial bool IsDownloadComplete { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(CanRetryDownload))]
    public partial bool IsError { get; set; }

    public Microsoft.UI.Xaml.Media.SolidColorBrush StatusColor => new Microsoft.UI.Xaml.Media.SolidColorBrush(
        IsError ? Microsoft.UI.Colors.Red :
        IsDownloadComplete ? Microsoft.UI.Colors.LightGreen :
        Microsoft.UI.Colors.LightGray);

    public Microsoft.UI.Xaml.Visibility OpenLocationVisibility => IsDownloadComplete ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string DownloadedFolderPath { get; set; } = string.Empty;
    public string DownloadedFilePath { get; private set; } = string.Empty;
    public bool CanRetryDownload => !IsDownloading && IsError && Metadata != null && !string.IsNullOrWhiteSpace(DownloadedFolderPath);
    public bool ShouldOverwriteExistingFiles { get; set; }

    public Action<DownloadItemViewModel>? OnRemoveRequested { get; set; }

    public DownloadItemViewModel(string url, IDownloadService downloadService)
    {
        Url = url;
        _downloadService = downloadService;
        SelectedAudioBitrate = AvailableAudioBitrates[0];
    }

    public void CancelCurrentOperation()
    {
        _cts?.Cancel();
        _thumbnailCts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanEditQueue))]
    private void Remove()
    {
        if (!CanEditQueue) return;
        CancelCurrentOperation();
        OnRemoveRequested?.Invoke(this);
    }

    [RelayCommand]
    private void OpenLocation()
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            if (File.Exists(DownloadedFilePath))
            {
                start.ArgumentList.Add("/select,");
                start.ArgumentList.Add(DownloadedFilePath);
            }
            else if (Directory.Exists(DownloadedFolderPath)) start.ArgumentList.Add(DownloadedFolderPath);
            else
            {
                StatusMessage = "O arquivo ou a pasta não está mais disponível.";
                return;
            }
            System.Diagnostics.Process.Start(start);
        }
        catch (Exception ex)
        {
            AppLogger.LogException(ex, "Failed to reveal the downloaded media in Explorer");
            StatusMessage = "Não foi possível abrir o arquivo. Verifique se a pasta ainda existe.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditQueue))]
    private void PrepareAnotherDownload()
    {
        if (!CanEditQueue) return;
        IsDownloadComplete = false;
        IsError = false;
        Progress = 0;
        StatusMessage = "Selecione outro formato e baixe novamente.";
    }

    public async Task RetryDownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(DownloadedFolderPath) || Metadata == null)
        {
            return;
        }

        IsError = false;
        StatusMessage = "Tentando novamente...";
        await StartDownloadAsync(DownloadedFolderPath, CancellationToken.None);
    }

    private static string GetFriendlyMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "O download falhou. Tente novamente.";
        }

        var normalized = message.Trim();
        if (normalized.Contains("Arquivo já existe", StringComparison.OrdinalIgnoreCase))
        {
            return "Arquivo já existente. Habilite a sobrescrita para continuar ou escolha outra pasta.";
        }

        if (normalized.Contains("pasta de destino", StringComparison.OrdinalIgnoreCase) || normalized.Contains("Não foi possível gravar", StringComparison.OrdinalIgnoreCase))
        {
            return "Não foi possível gravar na pasta selecionada. Verifique as permissões ou escolha outra pasta.";
        }

        if (normalized.Contains("Não foi possível localizar o arquivo final", StringComparison.OrdinalIgnoreCase))
        {
            return "O processo terminou, mas o arquivo final não foi localizado. Verifique a pasta de destino.";
        }

        if (normalized.Contains("download cancelado", StringComparison.OrdinalIgnoreCase) || normalized.Contains("cancelado", StringComparison.OrdinalIgnoreCase))
        {
            return "Download cancelado pelo usuário.";
        }

        return normalized.Length > 180 ? normalized.Substring(0, 177) + "..." : normalized;
    }

    public async Task FetchMetadataAsync()
    {
        if (IsFetching || IsDownloading || IsQueueLocked) return;
        IsFetching = true;
        StatusMessage = "Analisando link...";

        using var operation = new CancellationTokenSource();
        _cts = operation;

        try
        {
            var meta = await _downloadService.FetchMetadataAsync(Url, operation.Token);
            if (meta != null)
            {
                Metadata = meta;

                var cleanFormats = meta.Formats
                    .Where(f => f.HasVideo && !string.IsNullOrEmpty(f.Resolution) && f.Resolution != "audio only")
                    .OrderByDescending(f => f.Height ?? 0)
                    .ThenByDescending(f => f.Fps)
                    .ThenBy(f => f.HasAudio ? 1 : 0)
                    .ToList();

                var grouped = cleanFormats.GroupBy(f => f.Resolution).Select(g => g.First()).ToList();
                AvailableFormats.Clear();

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
            StatusMessage = GetFriendlyMessage(ex.Message);
            IsError = true;
        }
        finally
        {
            if (ReferenceEquals(_cts, operation)) _cts = null;
            IsFetching = false;
        }
    }

    public async Task StartDownloadAsync(string destinationFolder, CancellationToken masterToken)
    {
        if (masterToken.IsCancellationRequested || IsFetching || IsDownloadComplete || Metadata == null)
        {
            return;
        }

        var lockAcquired = false;
        try
        {
            await _downloadLock.WaitAsync(masterToken);
            lockAcquired = true;

            if (IsFetching || IsDownloading || IsDownloadComplete || Metadata == null || masterToken.IsCancellationRequested)
            {
                return;
            }

            IsDownloading = true;
            IsError = false;
            Progress = 0;
            StatusMessage = "Iniciando download...";

            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(masterToken);

            try
            {
                var isAudio = IsMp3Selected || IsWavSelected || IsFlacSelected;
                var formatId = isAudio ? "bestaudio" : SelectedFormat?.FormatId;
                var audioFormat = IsWavSelected ? "wav" : IsFlacSelected ? "flac" : "mp3";
                var audioBitrate = isAudio ? (SelectedAudioBitrate?.Value ?? "192") : "";
                var videoExt = IsMovSelected ? "mov" : "mp4";

                var progressHandler = new Progress<DownloadProgressInfo>(p =>
                {
                    Progress = p.Percentage;
                    Speed = p.Speed;
                    Eta = p.Eta;
                    if (!string.IsNullOrEmpty(p.Status))
                    {
                        StatusMessage = p.Status;
                    }
                });

                DownloadedFolderPath = destinationFolder;

                var resultPath = await _downloadService.DownloadAsync(
                    Url,
                    formatId ?? "best",
                    isAudio,
                    audioFormat,
                    audioBitrate,
                    videoExt,
                    destinationFolder,
                    progressHandler,
                    _cts.Token,
                    overwriteExistingFiles: ShouldOverwriteExistingFiles,
                    titleHint: Metadata.Title
                );

                if (string.IsNullOrEmpty(resultPath))
                {
                    resultPath = destinationFolder;
                }

                DownloadedFilePath = resultPath;
                DownloadedFolderPath = Path.GetDirectoryName(resultPath) ?? destinationFolder;
                StatusMessage = $"Concluído! Arquivo salvo em: {resultPath}";
                Progress = 100;
                Speed = string.Empty;
                Eta = string.Empty;
                IsDownloadComplete = true;

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
                StatusMessage = "Download cancelado pelo usuário.";
                IsError = true;
            }
            catch (Exception ex)
            {
                var friendly = GetFriendlyMessage(ex.Message);
                StatusMessage = friendly;
                IsError = true;
                AppLogger.LogException(ex, $"Download failed for {Url}");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                IsDownloading = false;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (lockAcquired)
            {
                _downloadLock.Release();
            }
        }
    }
}
