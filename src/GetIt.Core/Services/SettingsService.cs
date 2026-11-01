using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Win32;
using GetIt_App;

namespace GetIt_App.Services;

public class AppSettings
{
    public string LastDownloadFolder { get; set; } = string.Empty;

    public string FfmpegBinPath { get; set; } = string.Empty;

    /// <summary>
    /// User-chosen theme override. "Dark", "Light", or null (follow Windows).
    /// </summary>
    public string? Theme { get; set; } = null;

    public bool IsAutoPasteEnabled { get; set; } = true;

    public bool IsAppAutoUpdateEnabled { get; set; } = true;

    public bool IsYtDlpAutoUpdateEnabled { get; set; } = true;

    public bool RestoreQueueOnStartup { get; set; }
}

public static class SettingsService
{
    private static readonly AtomicJsonFileStore<AppSettings> Store = new(
        Path.Combine(AppInfo.AppDataDirectory, "settings.json"),
        new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

    public static string? LastLoadWarning { get; private set; }
    public static event Action<bool>? QueuePersistenceSettingChanged;

    public static void AcknowledgeLoadWarning() => LastLoadWarning = null;

    public static AppSettings LoadSettings()
    {
        try
        {
            var result = Store.Load(CreateDefaultSettings);
            SetRecoveryWarning(result.WasRecovered);
            return result.Value;
        }
        catch (JsonFilePersistenceException exception)
        {
            AppLogger.LogException(exception, "Failed to load settings");
            LastLoadWarning = "As configurações não puderam ser recuperadas. O arquivo original foi preservado; verifique os arquivos de backup na pasta de dados do GetIt.";
            return CreateDefaultSettings();
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            var result = Store.Save(settings);
            SetRecoveryWarning(result.WasRecovered);
            QueuePersistenceSettingChanged?.Invoke(settings.RestoreQueueOnStartup);
        }
        catch (JsonFilePersistenceException exception)
        {
            AppLogger.LogException(exception, "Failed to save settings");
            throw;
        }
    }

    public static void UpdateSettings(Action<AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        try
        {
            var result = Store.UpdateAsync(settings =>
            {
                update(settings);
                return settings;
            }, CreateDefaultSettings).GetAwaiter().GetResult();
            SetRecoveryWarning(result.WasRecovered);
            QueuePersistenceSettingChanged?.Invoke(result.Value.RestoreQueueOnStartup);
        }
        catch (JsonFilePersistenceException exception)
        {
            AppLogger.LogException(exception, "Failed to update settings");
            throw;
        }
    }

    /// <summary>
    /// Reads the Windows registry to detect the system app theme.
    /// Returns "Light" when Windows is set to light mode, "Dark" otherwise.
    /// </summary>
    public static string GetWindowsTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value && value == 1)
                return "Light";
        }
        catch (Exception exception)
        {
            AppLogger.LogException(exception, "Failed to read Windows theme");
        }

        return "Dark";
    }

    private static AppSettings CreateDefaultSettings() => new()
    {
        LastDownloadFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads")
    };

    private static void SetRecoveryWarning(bool wasRecovered)
    {
        if (wasRecovered)
        {
            AppLogger.LogWarning("Application settings were recovered from a local recovery file.");
            LastLoadWarning = "As configurações foram recuperadas de uma cópia local. Confira suas preferências.";
        }
    }
}
