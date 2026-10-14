using System;
using System.IO;

namespace GetIt_App;

public static class AppInfo
{
    public const string ProductName = "GetIt";

    public static Version CurrentVersion { get; } = typeof(AppInfo).Assembly
        .GetName()
        .Version ?? new Version(0, 0, 0);

    public static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ProductName);
}
