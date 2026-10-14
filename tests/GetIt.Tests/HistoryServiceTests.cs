using GetIt_App;
using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public class HistoryServiceTests
{
    private static string HistoryPath => Path.Combine(AppInfo.AppDataDirectory, "history.json");

    [Fact]
    public async Task AddHistoryItemAsync_ShouldInsertNewestAtBeginning()
    {
        var original = File.Exists(HistoryPath) ? await File.ReadAllTextAsync(HistoryPath) : null;

        try
        {
            var initial = new List<HistoryItem>
            {
                new() { Title = "Primeiro", Url = "https://example.com/1", LocalFilePath = "C:/downloads/1.mp4" },
                new() { Title = "Segundo", Url = "https://example.com/2", LocalFilePath = "C:/downloads/2.mp4" }
            };

            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
            await File.WriteAllTextAsync(HistoryPath, System.Text.Json.JsonSerializer.Serialize(initial));

            await HistoryService.AddHistoryItemAsync(new HistoryItem
            {
                Title = "Mais recente",
                Url = "https://example.com/3",
                LocalFilePath = "C:/downloads/3.mp4"
            });

            var history = await HistoryService.LoadHistoryAsync();

            Assert.Equal("Mais recente", history[0].Title);
            Assert.Equal(3, history.Count);
        }
        finally
        {
            if (original is not null)
            {
                await File.WriteAllTextAsync(HistoryPath, original);
            }
            else if (File.Exists(HistoryPath))
            {
                File.Delete(HistoryPath);
            }
        }
    }
}
