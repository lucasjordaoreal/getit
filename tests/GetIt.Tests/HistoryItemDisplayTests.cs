using System.Text.Json;
using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public sealed class HistoryItemDisplayTests
{
    [Fact]
    public void DownloadedAtDisplay_FormatsLocalAndUtcDatesInBrazilianPortuguese()
    {
        var localItem = new HistoryItem
        {
            DownloadedAt = new DateTime(2025, 6, 12, 14, 30, 0, DateTimeKind.Local)
        };
        var utcItem = new HistoryItem
        {
            DownloadedAt = new DateTime(2025, 6, 12, 17, 30, 0, DateTimeKind.Utc)
        };

        Assert.Equal("12/06/2025 às 14:30", localItem.DownloadedAtDisplay);
        Assert.Equal(
            utcItem.DownloadedAt.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")),
            utcItem.DownloadedAtDisplay);
    }

    [Fact]
    public void DownloadedAtDisplay_IsNotAddedToTheExistingJsonFormat()
    {
        var item = new HistoryItem
        {
            Title = "Mídia",
            DownloadedAt = new DateTime(2025, 6, 12, 14, 30, 0, DateTimeKind.Local)
        };

        var json = JsonSerializer.Serialize(item);

        Assert.Contains("\"DownloadedAt\"", json);
        Assert.DoesNotContain("DownloadedAtDisplay", json);
    }

    [Fact]
    public void PlayButtonTextTracksPlaybackStateWithoutChangingTheHistoryJson()
    {
        var item = new HistoryItem();
        var changedProperties = new List<string?>();
        item.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        Assert.Equal("Reproduzir", item.PlayButtonText);
        item.IsPlaying = true;

        Assert.Equal("Reproduzindo", item.PlayButtonText);
        Assert.Contains(nameof(HistoryItem.PlayButtonText), changedProperties);
        var json = JsonSerializer.Serialize(item);
        Assert.DoesNotContain("IsPlaying", json);
        Assert.DoesNotContain("PlayButtonText", json);
    }
}
