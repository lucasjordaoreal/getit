namespace GetIt_App.Services;

public static class DownloadEngineEnvironment
{
    public static string BuildChildPath(
        string ytDlpDirectory,
        string ffmpegDirectory,
        string denoDirectory,
        string? inheritedPath)
    {
        return string.Join(Path.PathSeparator, new[]
            {
                ytDlpDirectory,
                ffmpegDirectory,
                denoDirectory,
                inheritedPath
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
