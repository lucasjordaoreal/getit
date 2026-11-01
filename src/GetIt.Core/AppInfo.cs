using System;
using System.IO;

namespace GetIt_App;

public static class AppInfo
{
    public const string ProductName = "GetIt";
    private static string? _appDataDirectoryOverride;

    public static Version CurrentVersion { get; } =
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version
        ?? typeof(AppInfo).Assembly.GetName().Version
        ?? new Version(0, 0, 0);

    public static string AppDataDirectory => _appDataDirectoryOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ProductName);

    internal static void SetAppDataDirectoryOverride(string? directory) =>
        _appDataDirectoryOverride = directory is null ? null : Path.GetFullPath(directory);
}
