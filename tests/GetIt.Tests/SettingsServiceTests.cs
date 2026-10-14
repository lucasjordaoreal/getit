using GetIt_App;
using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public class SettingsServiceTests
{
    private static string SettingsPath => Path.Combine(AppInfo.AppDataDirectory, "settings.json");

    [Fact]
    public void LoadSettings_WhenFileIsMissing_ShouldReturnDefaultDownloadsFolder()
    {
        var original = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null;

        try
        {
            if (File.Exists(SettingsPath))
            {
                File.Delete(SettingsPath);
            }

            var settings = SettingsService.LoadSettings();

            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), settings.LastDownloadFolder);
            Assert.True(settings.IsAutoPasteEnabled);
        }
        finally
        {
            if (original is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, original);
            }
            else if (File.Exists(SettingsPath))
            {
                File.Delete(SettingsPath);
            }
        }
    }

    [Fact]
    public void LoadSettings_WhenJsonIsInvalid_ShouldFallbackToDefaultSettings()
    {
        var original = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, "{ invalid json");

            var settings = SettingsService.LoadSettings();

            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), settings.LastDownloadFolder);
            Assert.True(settings.IsAutoPasteEnabled);
        }
        finally
        {
            if (original is not null)
            {
                File.WriteAllText(SettingsPath, original);
            }
            else if (File.Exists(SettingsPath))
            {
                File.Delete(SettingsPath);
            }
        }
    }
}
