using GetIt_App;
using Xunit;

namespace GetIt.Tests;

public class AppInfoTests
{
    [Fact]
    public void ProductNameAndVersion_ShouldBeAvailable()
    {
        Assert.Equal("GetIt", AppInfo.ProductName);
        Assert.NotNull(AppInfo.CurrentVersion);
        Assert.True(AppInfo.CurrentVersion >= new Version(0, 0, 0));
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.AppDataDirectory));
    }
}
