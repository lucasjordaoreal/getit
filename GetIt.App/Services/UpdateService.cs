using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GetIt_App.Services;

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}

public class UpdateService
{
    public const string CurrentVersion = "0.0.5";
    private const string GitHubApiUrl = "https://api.github.com/repos/lucasjordaoreal/getit/releases/latest";

    public static async Task<GitHubRelease?> CheckForUpdatesAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "UltraDownloader-UpdateService");
            
            var response = await client.GetAsync(GitHubApiUrl);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release != null && !string.IsNullOrEmpty(release.TagName))
            {

                var latestVersion = release.TagName.TrimStart('v', 'V');
                var currentVersion = CurrentVersion.TrimStart('v', 'V');

                if (Version.TryParse(latestVersion, out var latest) && Version.TryParse(currentVersion, out var current))
                {
                    if (latest > current)
                    {
                        return release;
                    }
                }
                else if (release.TagName != CurrentVersion)
                {
                    // Fallback to string comparison if parsing fails
                    return release;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error checking for updates: {ex.Message}");
        }

        return null;
    }

    public static async Task DownloadAndInstallUpdateAsync(string downloadUrl)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "UltraDownloaderUpdate");
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
            Directory.CreateDirectory(tempDir);

            var zipPath = Path.Combine(tempDir, "update.zip");

            // 1. Download the ZIP file
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "UltraDownloader-UpdateService");
                var response = await client.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream);
            }

            // 2. Extract the ZIP file
            var extractPath = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(zipPath, extractPath);

            // Se o ZIP contém uma única pasta raiz (o que é comum no GitHub),
            // devemos pegar o conteúdo de dentro dela.
            var extractedDirs = Directory.GetDirectories(extractPath);
            var sourcePath = extractPath;
            if (extractedDirs.Length == 1 && Directory.GetFiles(extractPath).Length == 0)
            {
                sourcePath = extractedDirs[0];
            }

            // 3. Create a Batch file to replace the files and restart
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (currentExe == null) return;

            var currentDir = Path.GetDirectoryName(currentExe);
            if (currentDir == null) return;

            var batPath = Path.Combine(tempDir, "update.bat");
            
            // O script batch aguarda 2 segundos, copia os arquivos substituindo,
            // reinicia o app e deleta a si mesmo e a pasta temp.
            var batContent = $@"
@echo off
timeout /t 2 /nobreak > NUL
xcopy /s /y ""{sourcePath}\*.*"" ""{currentDir}\""
start """" ""{currentExe}""
rmdir /s /q ""{tempDir}""
del ""%~f0""
";
            File.WriteAllText(batPath, batContent);

            // 4. Run the Batch file and Exit the app
            var processInfo = new ProcessStartInfo
            {
                FileName = batPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(processInfo);

            // Exit the application
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error installing update: {ex.Message}");
            throw; // Let the caller know it failed
        }
    }
}

