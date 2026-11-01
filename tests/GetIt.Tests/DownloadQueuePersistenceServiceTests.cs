using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public class DownloadQueuePersistenceServiceTests
{
    [Fact]
    public async Task SavesAndRestoresQueueOrderAndIndividualPreferences()
    {
        using var directory = new TemporaryDirectory();
        var service = new DownloadQueuePersistenceService(Path.Combine(directory.Path, "queue.json"));
        var expected = new DownloadQueueSnapshot
        {
            Items =
            [
                new PersistedQueueItem
                {
                    Url = "https://example.test/first",
                    Format = "MP3",
                    AudioBitrate = "320"
                },
                new PersistedQueueItem
                {
                    Url = "https://example.test/second",
                    Format = "MOV",
                    VideoFormatId = "137",
                    AudioBitrate = "192"
                }
            ]
        };

        await service.SaveAsync(expected);
        var loaded = await service.LoadAsync();

        Assert.Equal(DownloadQueuePersistenceService.CurrentVersion, loaded.Snapshot.Version);
        Assert.Equal(new[] { "https://example.test/first", "https://example.test/second" },
            loaded.Snapshot.Items.Select(item => item.Url));
        Assert.Equal("MP3", loaded.Snapshot.Items[0].Format);
        Assert.Equal("320", loaded.Snapshot.Items[0].AudioBitrate);
        Assert.Equal("MOV", loaded.Snapshot.Items[1].Format);
        Assert.Equal("137", loaded.Snapshot.Items[1].VideoFormatId);
        Assert.Equal("192", loaded.Snapshot.Items[1].AudioBitrate);
    }

    [Fact]
    public async Task InvalidQueueFileIsPreservedWhenNoRecoveryCopyExists()
    {
        using var directory = new TemporaryDirectory();
        var file = Path.Combine(directory.Path, "queue.json");
        const string invalidJson = "{ invalid";
        await File.WriteAllTextAsync(file, invalidJson);
        var service = new DownloadQueuePersistenceService(file);

        await Assert.ThrowsAsync<JsonFilePersistenceException>(() => service.LoadAsync());

        Assert.Equal(invalidJson, await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task ValidBackupIsUsedAfterInterruptedPrimaryWrite()
    {
        using var directory = new TemporaryDirectory();
        var file = Path.Combine(directory.Path, "queue.json");
        var service = new DownloadQueuePersistenceService(file);
        await service.SaveAsync(new DownloadQueueSnapshot
        {
            Items = [new PersistedQueueItem { Url = "https://example.test/first" }]
        });
        await service.SaveAsync(new DownloadQueueSnapshot
        {
            Items = [new PersistedQueueItem { Url = "https://example.test/second" }]
        });
        await File.WriteAllTextAsync(file, "{ interrupted");

        var loaded = await service.LoadAsync();

        Assert.True(loaded.WasRecovered);
        Assert.Equal("https://example.test/first", Assert.Single(loaded.Snapshot.Items).Url);
    }

    [Fact]
    public async Task UnsupportedVersionIsPreserved()
    {
        using var directory = new TemporaryDirectory();
        var file = Path.Combine(directory.Path, "queue.json");
        const string futureVersion = """{"Version":99,"Items":[]}""";
        await File.WriteAllTextAsync(file, futureVersion);
        var service = new DownloadQueuePersistenceService(file);

        await Assert.ThrowsAsync<JsonFilePersistenceException>(() => service.LoadAsync());

        Assert.Equal(futureVersion, await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task ExplicitDiscardRemovesOnlyQueueDataAndItsBackups()
    {
        using var directory = new TemporaryDirectory();
        var file = Path.Combine(directory.Path, "queue.json");
        var settingsFile = Path.Combine(directory.Path, "settings.json");
        var historyFile = Path.Combine(directory.Path, "history.json");
        var corruptQueueBackup = Path.Combine(directory.Path, $"queue.json.corrupt.{DateTime.UtcNow.Ticks}.{Guid.NewGuid():N}");
        var unrelatedFile = Path.Combine(directory.Path, "queue.json.corrupt.not-managed");
        await File.WriteAllTextAsync(settingsFile, "{}");
        await File.WriteAllTextAsync(historyFile, "[]");
        await File.WriteAllTextAsync(corruptQueueBackup, "{ invalid }");
        await File.WriteAllTextAsync(unrelatedFile, "keep");
        var service = new DownloadQueuePersistenceService(file);
        await service.SaveAsync(new DownloadQueueSnapshot
        {
            Items = [new PersistedQueueItem { Url = "https://example.test/video" }]
        });
        await service.SaveAsync(new DownloadQueueSnapshot());

        await service.DiscardAsync();

        Assert.False(File.Exists(file));
        Assert.Empty(Directory.GetFiles(directory.Path, "queue.json.bak.*"));
        Assert.Empty(Directory.GetFiles(directory.Path, "queue.json.*.tmp"));
        Assert.False(File.Exists(corruptQueueBackup));
        Assert.True(File.Exists(unrelatedFile));
        Assert.True(File.Exists(settingsFile));
        Assert.True(File.Exists(historyFile));
    }

    [Fact]
    public async Task DuplicateAndInvalidEntriesAreSkippedWithoutAutoStartingAnyOperation()
    {
        using var directory = new TemporaryDirectory();
        var file = Path.Combine(directory.Path, "queue.json");
        await File.WriteAllTextAsync(file,
            """{"Version":1,"Items":[{"Url":"https://example.test/one","Format":"MP4","AudioBitrate":"320"},{"Url":"https://EXAMPLE.test/one","Format":"MP4","AudioBitrate":"320"},{"Url":"not a url","Format":"MP4","AudioBitrate":"320"}]}""");
        var service = new DownloadQueuePersistenceService(file);

        var result = await service.LoadAsync();

        Assert.Single(result.Snapshot.Items);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void QueueRestorationPreferenceIsOptInForExistingSettings()
    {
        Assert.False(new AppSettings().RestoreQueueOnStartup);
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!.RestoreQueueOnStartup);
    }

    [Fact]
    public async Task UnavailableMediaLinkRemainsRecoverableWithoutMetadata()
    {
        using var directory = new TemporaryDirectory();
        var service = new DownloadQueuePersistenceService(Path.Combine(directory.Path, "queue.json"));
        const string url = "https://unavailable.example.test/removed-media";
        await service.SaveAsync(new DownloadQueueSnapshot
        {
            Items = [new PersistedQueueItem { Url = url, Format = "FLAC" }]
        });

        var restored = await service.LoadAsync();

        Assert.Equal(url, Assert.Single(restored.Snapshot.Items).Url);
        Assert.Equal("FLAC", restored.Snapshot.Items[0].Format);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "GetIt.Tests", Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
