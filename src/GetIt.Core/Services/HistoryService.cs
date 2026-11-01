using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using GetIt_App;

namespace GetIt_App.Services;

public class HistoryItem
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Thumbnail { get; set; } = string.Empty;
    public string LocalFilePath { get; set; } = string.Empty;
    public DateTime DownloadedAt { get; set; }

    [JsonIgnore]
    public string DownloadedAtDisplay
    {
        get
        {
            if (DownloadedAt == default)
                return string.Empty;

            var localTime = DownloadedAt.Kind switch
            {
                DateTimeKind.Utc => DownloadedAt.ToLocalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(DownloadedAt, DateTimeKind.Local),
                _ => DownloadedAt
            };
            return localTime.ToString("dd/MM/yyyy 'às' HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
        }
    }
}

public static class HistoryService
{
    private static readonly AtomicJsonFileStore<List<HistoryItem>> Store = new(
        Path.Combine(AppInfo.AppDataDirectory, "history.json"),
        new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

    public static string? LastLoadWarning { get; private set; }

    public static void AcknowledgeLoadWarning() => LastLoadWarning = null;

    public static async Task<List<HistoryItem>> LoadHistoryAsync()
    {
        try
        {
            var result = await Store.LoadAsync(static () => new List<HistoryItem>());
            SetRecoveryWarning(result.WasRecovered);
            return result.Value;
        }
        catch (JsonFilePersistenceException exception)
        {
            AppLogger.LogException(exception, "Failed to load history");
            LastLoadWarning = "O histórico não pôde ser recuperado. O arquivo original foi preservado; verifique os arquivos de backup na pasta de dados do GetIt.";
            return [];
        }
    }

    public static async Task SaveHistoryAsync(List<HistoryItem> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        try
        {
            var result = await Store.SaveAsync(history);
            SetRecoveryWarning(result.WasRecovered);
        }
        catch (JsonFilePersistenceException exception)
        {
            AppLogger.LogException(exception, "Failed to save history");
            throw;
        }
    }

    public static async Task AddHistoryItemAsync(HistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        try
        {
            var result = await Store.UpdateAsync(history =>
            {
                history.Insert(0, item);
                if (history.Count > 100)
                    history.RemoveRange(100, history.Count - 100);
                return history;
            }, static () => new List<HistoryItem>());
            SetRecoveryWarning(result.WasRecovered);
        }
        catch (JsonFilePersistenceException exception)
        {
            AppLogger.LogException(exception, "Failed to add an item to history");
            throw;
        }
    }

    public static Task ClearHistoryAsync() => SaveHistoryAsync([]);

    private static void SetRecoveryWarning(bool wasRecovered)
    {
        if (wasRecovered)
        {
            AppLogger.LogWarning("The download history was recovered from a local recovery file.");
            LastLoadWarning = "O histórico foi recuperado de uma cópia local. Confira os registros recentes.";
        }
    }
}
