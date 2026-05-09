using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace UltraDownloader_App.Services;

public class HistoryItem
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Thumbnail { get; set; } = string.Empty;
    public string LocalFilePath { get; set; } = string.Empty;
    public DateTime DownloadedAt { get; set; }
}

public static class HistoryService
{
    private static readonly string HistoryFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UltraDownloader", "history.json");

    public static async Task<List<HistoryItem>> LoadHistoryAsync()
    {
        try
        {
            if (!File.Exists(HistoryFilePath))
                return new List<HistoryItem>();

            var json = await File.ReadAllTextAsync(HistoryFilePath);
            return JsonSerializer.Deserialize<List<HistoryItem>>(json) ?? new List<HistoryItem>();
        }
        catch
        {
            return new List<HistoryItem>();
        }
    }

    public static async Task SaveHistoryAsync(List<HistoryItem> history)
    {
        try
        {
            var dir = Path.GetDirectoryName(HistoryFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(HistoryFilePath, json);
        }
        catch { }
    }

    public static async Task AddHistoryItemAsync(HistoryItem item)
    {
        var history = await LoadHistoryAsync();
        // Insert at beginning
        history.Insert(0, item);
        // Keep last 100
        if (history.Count > 100) history.RemoveRange(100, history.Count - 100);
        await SaveHistoryAsync(history);
    }

    public static async Task ClearHistoryAsync()
    {
        try
        {
            if (File.Exists(HistoryFilePath))
            {
                await File.WriteAllTextAsync(HistoryFilePath, "[]");
            }
        }
        catch { }
    }
}
