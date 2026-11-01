using GetIt_App;
using Xunit;

namespace GetIt.Tests;

[CollectionDefinition("Isolated application data", DisableParallelization = true)]
public sealed class AppDataTestCollection : ICollectionFixture<IsolatedAppDataFixture>
{
    public const string Name = "Isolated application data";
}

public sealed class IsolatedAppDataFixture : IDisposable
{
    private readonly string _directoryPath = Path.Combine(
        Path.GetTempPath(), "GetIt.Tests", $"appdata-{Guid.NewGuid():N}");

    public IsolatedAppDataFixture()
    {
        Directory.CreateDirectory(_directoryPath);
        AppInfo.SetAppDataDirectoryOverride(_directoryPath);
    }

    public void Dispose()
    {
        AppInfo.SetAppDataDirectoryOverride(null);
        if (Directory.Exists(_directoryPath))
            Directory.Delete(_directoryPath, recursive: true);
    }
}
