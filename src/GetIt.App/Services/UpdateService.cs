using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using GetIt_App;

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
    public static Version CurrentVersion => AppInfo.CurrentVersion;
    private const string GitHubApiUrl = "https://api.github.com/repos/lucasjordaoreal/getit/releases/latest";

    public static async Task<GitHubRelease?> CheckForUpdatesAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", $"{AppInfo.ProductName}-UpdateService");

            var response = await client.GetAsync(GitHubApiUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release != null && !string.IsNullOrEmpty(release.TagName))
            {
                var latestVersion = release.TagName.TrimStart('v', 'V');
                var currentVersion = CurrentVersion.ToString();

                if (Version.TryParse(latestVersion, out var latest) && Version.TryParse(currentVersion, out var current))
                {
                    return latest > current ? release : null;
                }

                throw new InvalidDataException($"Versão inválida no release do GitHub: {release.TagName}");
            }

            throw new InvalidDataException("O GitHub não retornou um release válido.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error checking for updates: {ex.Message}");
            AppLogger.LogException(ex, "Failed to check for app updates");
            throw;
        }
    }

    public static async Task DownloadAndInstallUpdateAsync(string downloadUrl)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"{AppInfo.ProductName}Update_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var downloadUri = new Uri(downloadUrl);
            var isInstaller = Path.GetExtension(downloadUri.AbsolutePath).Equals(".exe", StringComparison.OrdinalIgnoreCase);
            var downloadedPath = Path.Combine(tempDir, isInstaller ? "update-installer.exe" : "update.zip");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", $"{AppInfo.ProductName}-UpdateService");
                var response = await client.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                using var fileStream = new FileStream(downloadedPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream);
            }

            if (isInstaller)
            {
                var installerProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = downloadedPath,
                    UseShellExecute = true
                });
                if (installerProcess == null)
                {
                    throw new InvalidOperationException("Não foi possível iniciar o instalador da atualização.");
                }

                await installerProcess.WaitForExitAsync();
                Directory.Delete(tempDir, true);
                return;
            }

            // 2. Extract the ZIP file
            var extractPath = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(downloadedPath, extractPath);

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
