using System;
using System.Diagnostics;
using System.IO;

namespace GetIt_App;

public static class AppLogger
{
    private static readonly string LogFilePath = Path.Combine(AppInfo.AppDataDirectory, "logs", "getit.log");

    public static void LogException(Exception exception, string context)
    {
        var message = $"[{DateTimeOffset.Now:O}] {context}: {exception}";
        Debug.WriteLine(message);

        try
        {
            var directory = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(LogFilePath, message + Environment.NewLine);
            }
        }
        catch (Exception loggingException)
        {
            Debug.WriteLine($"[{DateTimeOffset.Now:O}] Failed to write app log: {loggingException}");
        }
    }

    public static void LogWarning(string message)
    {
        var formatted = $"[{DateTimeOffset.Now:O}] Warning: {message}";
        Debug.WriteLine(formatted);
    }
}
