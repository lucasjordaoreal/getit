using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace GetIt_App.Services;

public class YtDlpUpdateService
{
    private readonly string _ytdlpPath;

    public YtDlpUpdateService()
    {
        var binPath = DownloadService.ResolveBinPath();
        var candidate = Path.Combine(binPath, "yt-dlp.exe");
        _ytdlpPath = File.Exists(candidate) ? candidate : string.Empty;
    }

    public async Task<bool> UpdateAsync()
    {
        if (string.IsNullOrWhiteSpace(_ytdlpPath) || !File.Exists(_ytdlpPath))
        {
            AppLogger.LogWarning("yt-dlp executable not found; update skipped.");
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ytdlpPath,
                Arguments = "-U",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update yt-dlp: {ex.Message}");
            return false;
        }
    }
}
