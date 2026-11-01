using System.Text.Encodings.Web;
using System.Text.Json;
using GetIt_App;

namespace GetIt_App.Services;

public sealed class PersistedQueueItem
{
    public string Url { get; set; } = string.Empty;
    public string Format { get; set; } = "MP4";
    public string? VideoFormatId { get; set; }
    public string AudioBitrate { get; set; } = "320";
}

public sealed class DownloadQueueSnapshot
{
    public int Version { get; set; } = DownloadQueuePersistenceService.CurrentVersion;
    public List<PersistedQueueItem> Items { get; set; } = [];
}

public sealed record DownloadQueueLoadResult(DownloadQueueSnapshot Snapshot, bool WasRecovered, string? Warning);

public sealed class DownloadQueuePersistenceService
{
    public const int CurrentVersion = 1;
    public const int MaximumItems = 500;
    public const int MaximumUrlLength = 4096;
    public const int MaximumVideoFormatIdLength = 128;

    private readonly AtomicJsonFileStore<DownloadQueueSnapshot> _store;

    public DownloadQueuePersistenceService(string? filePath = null)
    {
        _store = new AtomicJsonFileStore<DownloadQueueSnapshot>(
            filePath ?? Path.Combine(AppInfo.AppDataDirectory, "queue.json"),
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
    }

    public async Task<DownloadQueueLoadResult> LoadAsync()
    {
        var result = await _store.LoadAsync(static () => new DownloadQueueSnapshot()).ConfigureAwait(false);
        var snapshot = result.Value;
        if (snapshot.Version != CurrentVersion)
            throw new JsonFilePersistenceException(
                $"The saved queue uses unsupported version {snapshot.Version}; its file was preserved.");
        if (snapshot.Items is null || snapshot.Items.Count > MaximumItems)
            throw new JsonFilePersistenceException(
                "The saved queue exceeds the supported size; its file was preserved.");

        var validItems = new List<PersistedQueueItem>(snapshot.Items.Count);
        var seenUrls = new HashSet<string>(StringComparer.Ordinal);
        var skippedItems = 0;
        foreach (var item in snapshot.Items)
        {
            if (item is null || string.IsNullOrEmpty(item.Url) || item.Url.Length > MaximumUrlLength ||
                !MediaUrlPolicy.TryNormalizeHttpUrl(item.Url, out var normalized) ||
                !IsSupportedFormat(item.Format) ||
                !IsSupportedBitrate(item.AudioBitrate) ||
                (item.VideoFormatId?.Length ?? 0) > MaximumVideoFormatIdLength)
            {
                skippedItems++;
                continue;
            }

            if (!seenUrls.Add(normalized))
            {
                skippedItems++;
                continue;
            }

            validItems.Add(new PersistedQueueItem
            {
                Url = normalized,
                Format = item.Format,
                VideoFormatId = string.IsNullOrWhiteSpace(item.VideoFormatId) ? null : item.VideoFormatId,
                AudioBitrate = item.AudioBitrate
            });
        }

        snapshot.Items = validItems;
        var warning = skippedItems > 0
            ? $"{skippedItems} item(ns) inválido(s) ou duplicado(s) da fila salva foram ignorados. O arquivo original foi preservado."
            : null;
        return new DownloadQueueLoadResult(snapshot, result.WasRecovered, warning);
    }

    public async Task SaveAsync(DownloadQueueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Items is null || snapshot.Items.Count > MaximumItems)
            throw new JsonFilePersistenceException(
                $"A fila excede o limite de {MaximumItems} itens persistíveis.");

        snapshot.Version = CurrentVersion;
        foreach (var item in snapshot.Items)
        {
            if (item is null || string.IsNullOrEmpty(item.Url) || item.Url.Length > MaximumUrlLength ||
                !MediaUrlPolicy.TryNormalizeHttpUrl(item.Url, out _) ||
                !IsSupportedFormat(item.Format) ||
                !IsSupportedBitrate(item.AudioBitrate) ||
                (item.VideoFormatId?.Length ?? 0) > MaximumVideoFormatIdLength)
                throw new JsonFilePersistenceException("A fila contém preferências inválidas e não foi salva.");
        }

        await _store.SaveAsync(snapshot).ConfigureAwait(false);
    }

    public Task DiscardAsync() => _store.DeleteAsync();

    private static bool IsSupportedFormat(string? format) =>
        format is "MP4" or "MOV" or "MP3" or "WAV" or "FLAC";

    private static bool IsSupportedBitrate(string? bitrate) =>
        bitrate is "320" or "192" or "128";
}
