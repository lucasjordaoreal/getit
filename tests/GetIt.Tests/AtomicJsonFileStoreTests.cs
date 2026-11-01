using System.Text;
using System.Text.Json;
using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public sealed class AtomicJsonFileStoreTests
{
    [Fact]
    public async Task SaveAsync_FirstWriteCreatesReadableFile()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "data.json");
        var store = new AtomicJsonFileStore<TestDocument>(path);

        await store.SaveAsync(new TestDocument { Value = "first" });

        Assert.Equal("first", (await store.LoadAsync(static () => new TestDocument())).Value.Value);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_ExistingFileCreatesBackupAndWritesNewVersion()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "data.json");
        var store = new AtomicJsonFileStore<TestDocument>(path);
        await store.SaveAsync(new TestDocument { Value = "previous" });

        await store.SaveAsync(new TestDocument { Value = "current" });

        Assert.Equal("current", (await store.LoadAsync(static () => new TestDocument())).Value.Value);
        var backup = Assert.Single(Directory.GetFiles(directory.Path, "data.json.bak.*"));
        Assert.Equal("previous", JsonSerializer.Deserialize<TestDocument>(await File.ReadAllBytesAsync(backup))!.Value);
    }

    [Fact]
    public async Task SaveAsync_WriteFailurePreservesExistingFileAndCleansOperationTemp()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "data.json");
        var fileSystem = new FaultInjectingFileSystem();
        var store = new AtomicJsonFileStore<TestDocument>(path, fileSystem: fileSystem);
        await store.SaveAsync(new TestDocument { Value = "preserved" });
        fileSystem.FailWrites = true;

        await Assert.ThrowsAsync<JsonFilePersistenceException>(() =>
            store.SaveAsync(new TestDocument { Value = "not committed" }));

        Assert.Equal("preserved", JsonSerializer.Deserialize<TestDocument>(await File.ReadAllBytesAsync(path))!.Value);
        Assert.Empty(Directory.GetFiles(directory.Path, "data.json.*.tmp"));
    }

    [Fact]
    public async Task LoadAsync_RecoversValidBackupAndPreservesCorruptPrimary()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "history.json");
        var corruptContents = "{ incomplete";
        await File.WriteAllTextAsync(path, corruptContents);
        var backupPath = path + ".bak.0000000000000000001.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        await File.WriteAllTextAsync(backupPath, """[{"Title":"recoverable"}]""");
        var store = new AtomicJsonFileStore<List<HistoryItem>>(path);

        var result = await store.LoadAsync(static () => new List<HistoryItem>());

        Assert.True(result.WasRecovered);
        Assert.Single(result.Value);
        Assert.Equal("recoverable", result.Value[0].Title);
        Assert.Equal("recoverable", JsonSerializer.Deserialize<List<HistoryItem>>(
            await File.ReadAllBytesAsync(path))![0].Title);
        Assert.Contains(Directory.GetFiles(directory.Path, "history.json.corrupt.*"),
            corruptFile => File.ReadAllText(corruptFile) == corruptContents);
        Assert.True(File.Exists(backupPath));
    }

    [Theory]
    [InlineData("{ invalid JSON")]
    [InlineData("")]
    public async Task LoadAsync_InvalidJsonWithoutRecoveryPreservesOriginalAndFailsExplicitly(string invalidContents)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(path, invalidContents);
        var store = new AtomicJsonFileStore<AppSettings>(path);

        await Assert.ThrowsAsync<JsonFilePersistenceException>(() =>
            store.LoadAsync(static () => new AppSettings()));

        Assert.Equal(invalidContents, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task SaveAsync_ReplaceFailureLeavesExistingFileUntouched()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "data.json");
        var fileSystem = new FaultInjectingFileSystem();
        var store = new AtomicJsonFileStore<TestDocument>(path, fileSystem: fileSystem);
        await store.SaveAsync(new TestDocument { Value = "old" });
        fileSystem.FailReplace = true;

        await Assert.ThrowsAsync<JsonFilePersistenceException>(() =>
            store.SaveAsync(new TestDocument { Value = "new" }));

        Assert.Equal("old", JsonSerializer.Deserialize<TestDocument>(await File.ReadAllBytesAsync(path))!.Value);
        Assert.Empty(Directory.GetFiles(directory.Path, "data.json.bak.*"));
        Assert.Empty(Directory.GetFiles(directory.Path, "data.json.*.tmp"));
    }

    [Fact]
    public async Task LoadAsync_RecoversAValidInterruptedFirstWrite()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "data.json");
        await File.WriteAllTextAsync(path + "." + Guid.NewGuid().ToString("N") + ".tmp", """{"Value":"interrupted"}""");
        var store = new AtomicJsonFileStore<TestDocument>(path);

        var result = await store.LoadAsync(static () => new TestDocument());

        Assert.True(result.WasRecovered);
        Assert.Equal("interrupted", result.Value.Value);
        Assert.Equal("interrupted", JsonSerializer.Deserialize<TestDocument>(await File.ReadAllBytesAsync(path))!.Value);
    }

    [Fact]
    public async Task UpdateAsync_ConcurrentUpdatesAreSerializedWithoutLostEntries()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicJsonFileStore<TestDocument>(
            Path.Combine(directory.Path, "data.json"));

        await Task.WhenAll(Enumerable.Range(0, 40).Select(index =>
            store.UpdateAsync(document =>
            {
                document.Items.Add(index);
                return document;
            }, static () => new TestDocument())));

        var result = await store.LoadAsync(static () => new TestDocument());
        Assert.Equal(40, result.Value.Items.Count);
        Assert.Equal(Enumerable.Range(0, 40), result.Value.Items.Order());
    }

    [Fact]
    public async Task UpdateAsync_ConcurrentSettingsChangesPreserveIndependentFields()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicJsonFileStore<AppSettings>(
            Path.Combine(directory.Path, "settings.json"));
        await store.SaveAsync(new AppSettings());

        await Task.WhenAll(
            store.UpdateAsync(settings =>
            {
                settings.Theme = "Light";
                return settings;
            }, static () => new AppSettings()),
            store.UpdateAsync(settings =>
            {
                settings.LastDownloadFolder = "D:/Downloads";
                return settings;
            }, static () => new AppSettings()));

        var result = (await store.LoadAsync(static () => new AppSettings())).Value;
        Assert.Equal("Light", result.Theme);
        Assert.Equal("D:/Downloads", result.LastDownloadFolder);
    }

    [Fact]
    public async Task LoadAsync_ReadsExistingSettingsAndHistoryJsonFormats()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            """{"LastDownloadFolder":"D:/Downloads","Theme":"Light","IsAutoPasteEnabled":false}""");
        var settingsStore = new AtomicJsonFileStore<AppSettings>(settingsPath);

        var settings = (await settingsStore.LoadAsync(static () => new AppSettings())).Value;

        Assert.Equal("D:/Downloads", settings.LastDownloadFolder);
        Assert.Equal("Light", settings.Theme);
        Assert.False(settings.IsAutoPasteEnabled);
        Assert.True(settings.IsAppAutoUpdateEnabled);
        Assert.True(settings.IsYtDlpAutoUpdateEnabled);

        var historyPath = Path.Combine(directory.Path, "history.json");
        await File.WriteAllTextAsync(
            historyPath,
            """[{"Title":"Old item","Url":"https://example.invalid/video","LocalFilePath":"D:/video.mp4"}]""");
        var historyStore = new AtomicJsonFileStore<List<HistoryItem>>(historyPath);
        var history = (await historyStore.LoadAsync(static () => new List<HistoryItem>())).Value;

        Assert.Single(history);
        Assert.Equal("Old item", history[0].Title);
        Assert.Equal("D:/video.mp4", history[0].LocalFilePath);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotOverwriteCorruptDataWhenRecoveryIsImpossible()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "data.json");
        var original = Encoding.UTF8.GetBytes("not-json");
        await File.WriteAllBytesAsync(path, original);
        var store = new AtomicJsonFileStore<TestDocument>(path);

        await Assert.ThrowsAsync<JsonFilePersistenceException>(() =>
            store.UpdateAsync(document =>
            {
                document.Value = "replacement";
                return document;
            }, static () => new TestDocument()));

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    private sealed class TestDocument
    {
        public string Value { get; set; } = string.Empty;
        public List<int> Items { get; set; } = [];
    }

    private sealed class FaultInjectingFileSystem : IAtomicFileSystem
    {
        public bool FailWrites { get; set; }
        public bool FailReplace { get; set; }

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public bool FileExists(string path) => File.Exists(path);
        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) =>
            Directory.EnumerateFiles(directory, searchPattern);
        public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);
        public Task<IDisposable> AcquireExclusiveLockAsync(string path) =>
            Task.FromResult<IDisposable>(new NoOpDisposable());
        public Task<byte[]> ReadAllBytesAsync(string path) => File.ReadAllBytesAsync(path);

        public async Task WriteNewFileAsync(string path, ReadOnlyMemory<byte> contents)
        {
            if (FailWrites)
            {
                await File.WriteAllTextAsync(path, "partial");
                throw new IOException("Simulated disk write failure.");
            }

            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            await stream.WriteAsync(contents);
            await stream.FlushAsync();
            stream.Flush(flushToDisk: true);
        }

        public void CopyFile(string source, string destination) => File.Copy(source, destination);
        public void MoveFile(string source, string destination) => File.Move(source, destination);

        public void ReplaceFile(string source, string destination, string? backup)
        {
            if (FailReplace)
                throw new IOException("Simulated interrupted replacement.");
            File.Replace(source, destination, backup, ignoreMetadataErrors: true);
        }

        public void DeleteFile(string path) => File.Delete(path);
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "GetIt-JsonStoreTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
