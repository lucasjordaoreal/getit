using System;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.Win32;

namespace GetIt_App.Services;

public class AppSettings
{
    public string LastDownloadFolder { get; set; } = string.Empty;

    /// <summary>
    /// User-chosen theme override. "Dark", "Light", or null (follow Windows).
    /// </summary>
    public string? Theme { get; set; } = null;

    public bool IsAutoPasteEnabled { get; set; } = true;
}

public static class SettingsService
{
    private static readonly string SettingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UltraDownloader", "settings.json");

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }

        return new AppSettings 
        { 
            LastDownloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") 
        };
    }

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch { }
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
        catch { }

        return "Dark";
    }
}

