using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using GetIt_App;

namespace GetIt_App.Services;

public sealed class JsonFilePersistenceException : IOException
{
    public JsonFilePersistenceException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed record JsonFileLoadResult<T>(T Value, bool WasRecovered);

public interface IAtomicFileSystem
{
    void CreateDirectory(string path);
    bool DirectoryExists(string path);
    bool FileExists(string path);
    IEnumerable<string> EnumerateFiles(string directory, string searchPattern);
    DateTime GetLastWriteTimeUtc(string path);
    Task<IDisposable> AcquireExclusiveLockAsync(string path);
    Task<byte[]> ReadAllBytesAsync(string path);
    Task WriteNewFileAsync(string path, ReadOnlyMemory<byte> contents);
    void CopyFile(string source, string destination);
    void MoveFile(string source, string destination);
    void ReplaceFile(string source, string destination, string? backup);
    void DeleteFile(string path);
}

public sealed class AtomicJsonFileStore<T> where T : class
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _filePath;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly IAtomicFileSystem _fileSystem;
    private readonly SemaphoreSlim _pathLock;

    public AtomicJsonFileStore(
        string filePath,
        JsonSerializerOptions? serializerOptions = null,
        IAtomicFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions();
        _fileSystem = fileSystem ?? new PhysicalAtomicFileSystem();
        _pathLock = PathLocks.GetOrAdd(_filePath, static _ => new SemaphoreSlim(1, 1));
    }

    public JsonFileLoadResult<T> Load(Func<T> defaultFactory) =>
        LoadAsync(defaultFactory).GetAwaiter().GetResult();

    public async Task<JsonFileLoadResult<T>> LoadAsync(Func<T> defaultFactory)
    {
        ArgumentNullException.ThrowIfNull(defaultFactory);
        await _pathLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(defaultFactory, crossProcessLockHeld: false).ConfigureAwait(false);
        }
        catch (JsonFilePersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new JsonFilePersistenceException(
                "The stored data could not be read safely. The existing files were preserved.",
                exception);
        }
        finally
        {
            _pathLock.Release();
        }
    }

    public JsonFileLoadResult<T> Save(T value) =>
        SaveAsync(value).GetAwaiter().GetResult();

    public async Task<JsonFileLoadResult<T>> SaveAsync(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        await _pathLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var serialized = Serialize(value);
            using var crossProcessLock = await AcquireCrossProcessLockAsync().ConfigureAwait(false);
            var loaded = await LoadCoreAsync(() => value, crossProcessLockHeld: true).ConfigureAwait(false);
            await WriteCoreAsync(serialized).ConfigureAwait(false);
            return new JsonFileLoadResult<T>(value, loaded.WasRecovered);
        }
        catch (JsonFilePersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new JsonFilePersistenceException(
                "The data could not be saved. The existing valid file was preserved.",
                exception);
        }
        finally
        {
            _pathLock.Release();
        }
    }

    public async Task<JsonFileLoadResult<T>> UpdateAsync(Func<T, T> update, Func<T> defaultFactory)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(defaultFactory);
        await _pathLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var crossProcessLock = await AcquireCrossProcessLockAsync().ConfigureAwait(false);
            var loaded = await LoadCoreAsync(defaultFactory, crossProcessLockHeld: true).ConfigureAwait(false);
            var updated = update(loaded.Value)
                ?? throw new InvalidOperationException("The JSON update operation returned null.");
            var serialized = Serialize(updated);
            await WriteCoreAsync(serialized).ConfigureAwait(false);
            return new JsonFileLoadResult<T>(updated, loaded.WasRecovered);
        }
        catch (JsonFilePersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new JsonFilePersistenceException(
                "The data could not be updated. The existing valid file was preserved.",
                exception);
        }
        finally
        {
            _pathLock.Release();
        }
    }

    private async Task<JsonFileLoadResult<T>> LoadCoreAsync(
        Func<T> defaultFactory,
        bool crossProcessLockHeld)
    {
        var primaryExists = _fileSystem.FileExists(_filePath);
        Exception? primaryError = null;

        if (primaryExists)
        {
            var bytes = await _fileSystem.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
            if (TryDeserialize(bytes, out var value, out primaryError))
                return new JsonFileLoadResult<T>(value, false);
        }

        var backupPaths = GetBackupPaths();
        var temporaryPaths = GetTemporaryPaths();
        if (!crossProcessLockHeld
            && (primaryExists || backupPaths.Count > 0 || temporaryPaths.Count > 0))
        {
            using var crossProcessLock = await AcquireCrossProcessLockAsync().ConfigureAwait(false);
            return await LoadCoreAsync(defaultFactory, crossProcessLockHeld: true).ConfigureAwait(false);
        }

        foreach (var backupPath in backupPaths)
        {
            var bytes = await _fileSystem.ReadAllBytesAsync(backupPath).ConfigureAwait(false);
            if (!TryDeserialize(bytes, out var recoveredValue, out var backupError))
            {
                primaryError = CombineErrors(
                    primaryError,
                    backupError ?? new InvalidDataException("A recovery backup is not valid JSON."));
                continue;
            }

            await RestoreAsync(bytes, primaryExists).ConfigureAwait(false);
            return new JsonFileLoadResult<T>(recoveredValue, true);
        }

        var validTemporaryFiles = new List<(string Path, byte[] Bytes, T Value)>();
        foreach (var temporaryPath in temporaryPaths)
        {
            var bytes = await _fileSystem.ReadAllBytesAsync(temporaryPath).ConfigureAwait(false);
            if (TryDeserialize(bytes, out var temporaryValue, out var temporaryError))
            {
                validTemporaryFiles.Add((temporaryPath, bytes, temporaryValue));
            }
            else
            {
                primaryError = CombineErrors(
                    primaryError,
                    temporaryError ?? new InvalidDataException("An interrupted temporary file is not valid JSON."));
            }
        }

        if (validTemporaryFiles.Count == 1)
        {
            var candidate = validTemporaryFiles[0];
            await RestoreAsync(candidate.Bytes, primaryExists).ConfigureAwait(false);
            return new JsonFileLoadResult<T>(candidate.Value, true);
        }

        if (!primaryExists && backupPaths.Count == 0 && temporaryPaths.Count == 0)
            return new JsonFileLoadResult<T>(defaultFactory(), false);

        var recoveryError = validTemporaryFiles.Count > 1
            ? new InvalidDataException("Multiple valid interrupted writes were found; selecting one would be ambiguous.")
            : primaryError ?? new InvalidDataException("No valid JSON data was found in the primary file or recovery files.");
        throw new JsonFilePersistenceException(
            "The stored JSON is invalid or incomplete and no unambiguous valid recovery copy is available. Existing files were preserved.",
            recoveryError);
    }

    private async Task<IDisposable> AcquireCrossProcessLockAsync()
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("The data file has no parent directory.");
        _fileSystem.CreateDirectory(directory);
        return await _fileSystem.AcquireExclusiveLockAsync(_filePath + ".lock").ConfigureAwait(false);
    }

    private async Task RestoreAsync(byte[] bytes, bool primaryExists)
    {
        if (primaryExists)
        {
            _fileSystem.CopyFile(_filePath, CreateCorruptFilePath());
        }

        var temporaryPath = CreateTemporaryFilePath();
        try
        {
            await _fileSystem.WriteNewFileAsync(temporaryPath, bytes).ConfigureAwait(false);
            if (primaryExists)
                _fileSystem.ReplaceFile(temporaryPath, _filePath, null);
            else
                _fileSystem.MoveFile(temporaryPath, _filePath);
        }
        finally
        {
            DeleteTemporaryFileIfPresent(temporaryPath);
        }
    }

    private async Task WriteCoreAsync(byte[] serialized)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("The data file has no parent directory.");
        _fileSystem.CreateDirectory(directory);

        var temporaryPath = CreateTemporaryFilePath();
        try
        {
            await _fileSystem.WriteNewFileAsync(temporaryPath, serialized).ConfigureAwait(false);
            if (_fileSystem.FileExists(_filePath))
            {
                var backupPath = CreateBackupFilePath();
                _fileSystem.ReplaceFile(temporaryPath, _filePath, backupPath);
                if (!_fileSystem.FileExists(backupPath))
                {
                    throw new IOException("The atomic replace did not create its recovery backup.");
                }

                CleanupOldBackups(backupPath);
            }
            else
            {
                _fileSystem.MoveFile(temporaryPath, _filePath);
            }
        }
        finally
        {
            DeleteTemporaryFileIfPresent(temporaryPath);
        }
    }

    private byte[] Serialize(T value)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, _serializerOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new JsonFilePersistenceException(
                "The data could not be serialized. The existing file was not changed.",
                exception);
        }
    }

    private bool TryDeserialize(byte[] bytes, out T value, out Exception? error)
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(bytes, _serializerOptions)
                ?? throw new JsonException("The JSON document contains null instead of the expected data.");
            error = null;
            return true;
        }
        catch (JsonException exception)
        {
            value = null!;
            error = exception;
            return false;
        }
    }

    private List<string> GetBackupPaths()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (directory is null || !_fileSystem.DirectoryExists(directory))
            return [];

        var pattern = Path.GetFileName(_filePath) + ".bak.*";
        return _fileSystem.EnumerateFiles(directory, pattern)
            .Where(IsManagedBackupPath)
            .OrderByDescending(GetBackupSequence)
            .ThenByDescending(_fileSystem.GetLastWriteTimeUtc)
            .ToList();
    }

    private List<string> GetTemporaryPaths()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (directory is null || !_fileSystem.DirectoryExists(directory))
            return [];

        return _fileSystem.EnumerateFiles(directory, Path.GetFileName(_filePath) + ".*.tmp")
            .Where(IsManagedTemporaryPath)
            .ToList();
    }

    private bool IsManagedBackupPath(string path)
    {
        var marker = Path.GetFileName(_filePath) + ".bak.";
        var name = Path.GetFileName(path);
        if (!name.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = name[marker.Length..];
        var separator = suffix.IndexOf('.');
        if (separator <= 0 || separator == suffix.Length - 1)
            return false;

        return long.TryParse(suffix[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            && Guid.TryParseExact(suffix[(separator + 1)..], "N", out _);
    }

    private bool IsManagedTemporaryPath(string path)
    {
        var marker = Path.GetFileName(_filePath) + ".";
        var name = Path.GetFileName(path);
        if (!name.StartsWith(marker, StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.Length <= marker.Length + 4)
            return false;

        var identifier = name[marker.Length..^4];
        return Guid.TryParseExact(identifier, "N", out _);
    }

    private long GetBackupSequence(string path)
    {
        var marker = Path.GetFileName(_filePath) + ".bak.";
        var name = Path.GetFileName(path);
        var suffix = name.StartsWith(marker, StringComparison.OrdinalIgnoreCase)
            ? name[marker.Length..]
            : string.Empty;
        var separator = suffix.IndexOf('.');
        var sequence = separator < 0 ? suffix : suffix[..separator];
        return long.TryParse(sequence, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private void CleanupOldBackups(string currentBackup)
    {
        foreach (var backupPath in GetBackupPaths())
        {
            if (string.Equals(backupPath, currentBackup, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                _fileSystem.DeleteFile(backupPath);
            }
            catch (Exception exception)
            {
                AppLogger.LogException(exception, "Could not remove an obsolete JSON recovery backup");
            }
        }
    }

    private void DeleteTemporaryFileIfPresent(string path)
    {
        if (!_fileSystem.FileExists(path))
            return;

        try
        {
            _fileSystem.DeleteFile(path);
        }
        catch (Exception exception)
        {
            AppLogger.LogException(exception, "Could not remove a temporary JSON persistence file");
        }
    }

    private string CreateTemporaryFilePath() => $"{_filePath}.{Guid.NewGuid():N}.tmp";

    private string CreateBackupFilePath() =>
        $"{_filePath}.bak.{DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)}.{Guid.NewGuid():N}";

    private string CreateCorruptFilePath() =>
        $"{_filePath}.corrupt.{DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)}.{Guid.NewGuid():N}";

    private static Exception CombineErrors(Exception? previous, Exception current) =>
        previous is null ? current : new AggregateException(previous, current);
}

internal sealed class PhysicalAtomicFileSystem : IAtomicFileSystem
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) =>
        Directory.Exists(directory) ? Directory.EnumerateFiles(directory, searchPattern) : [];

    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

    public async Task<IDisposable> AcquireExclusiveLockAsync(string path)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                if (timer.Elapsed >= TimeSpan.FromSeconds(30))
                    throw new IOException("Timed out waiting for another GetIt process to finish writing this data file.", exception);

                await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
            }
        }
    }

    public Task<byte[]> ReadAllBytesAsync(string path) => File.ReadAllBytesAsync(path);

    public async Task WriteNewFileAsync(string path, ReadOnlyMemory<byte> contents)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(contents).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    public void CopyFile(string source, string destination) => File.Copy(source, destination);

    public void MoveFile(string source, string destination) => File.Move(source, destination);

    public void ReplaceFile(string source, string destination, string? backup) =>
        File.Replace(source, destination, backup, ignoreMetadataErrors: true);

    public void DeleteFile(string path) => File.Delete(path);

    private static bool IsSharingViolation(IOException exception) =>
        (exception.HResult & 0xFFFF) is 32 or 33;
}
