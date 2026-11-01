using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace GetIt_App.Services;

public sealed record DependencySetupProgress(
    string Component,
    long BytesReceived,
    long? TotalBytes,
    string Status);

public sealed record DependencySetupResult(string FfmpegDirectory, string DenoDirectory);

internal sealed record DependencySetupAsset(
    Uri Uri,
    string Sha256,
    long MaximumBytes,
    string DirectoryName,
    IReadOnlyList<string> FilesToExtract);

public sealed class ExternalDependencySetupService
{
    internal static readonly DependencySetupAsset FfmpegAsset = new(
        new Uri("https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-31-12-59/ffmpeg-n8.1.3-27-g8dca1586f4-win64-gpl-8.1.zip"),
        "401dffcf14c301082e1fc03b0bc876b92ea676bfc521fc56e80a1537d137034f",
        260L * 1024 * 1024,
        "BtbN-FFmpeg-8.1.3-27-g8dca1586f4",
        ["ffmpeg.exe", "ffprobe.exe", "LICENSE.txt"]);
    internal static readonly DependencySetupAsset DenoAsset = new(
        new Uri("https://github.com/denoland/deno/releases/download/v2.7.1/deno-x86_64-pc-windows-msvc.zip"),
        "94d71d4772436de27a0495933ca4bab7b6895992622b65baeaf4b7995dae1e69",
        160L * 1024 * 1024,
        "Deno-2.7.1",
        ["deno.exe"]);
    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.None
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private readonly string _toolsRoot;
    private readonly HttpClient _httpClient;
    private readonly Func<AppSettings> _loadSettings;
    private readonly Action<Action<AppSettings>> _updateSettings;
    private readonly DependencySetupAsset _ffmpegAsset;
    private readonly DependencySetupAsset _denoAsset;
    private readonly string _denoLicenseSourcePath;

    public ExternalDependencySetupService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GetIt",
            "Tools"),
            SharedHttpClient,
            SettingsService.LoadSettings,
            SettingsService.UpdateSettings,
            FfmpegAsset,
            DenoAsset,
            Path.Combine(AppContext.BaseDirectory, "Licenses", "texts", "Deno-MIT-LICENSE.md"))
    {
    }

    internal ExternalDependencySetupService(
        string toolsRoot,
        HttpClient httpClient,
        Func<AppSettings> loadSettings,
        Action<Action<AppSettings>> updateSettings,
        DependencySetupAsset ffmpegAsset,
        DependencySetupAsset denoAsset,
        string denoLicenseSourcePath = "")
    {
        _toolsRoot = Path.GetFullPath(toolsRoot);
        _httpClient = httpClient;
        _loadSettings = loadSettings;
        _updateSettings = updateSettings;
        _ffmpegAsset = ffmpegAsset;
        _denoAsset = denoAsset;
        _denoLicenseSourcePath = denoLicenseSourcePath;
    }

    public async Task<DependencySetupResult> ConfigureAsync(
        IProgress<DependencySetupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_toolsRoot);
        var stagingRoot = Path.Combine(_toolsRoot, ".staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);

        try
        {
            var settings = _loadSettings();
            var ffmpegDirectory = await FindValidFfmpegDirectoryAsync(
                settings.FfmpegBinPath, cancellationToken);
            if (ffmpegDirectory is null)
            {
                ffmpegDirectory = await InstallFfmpegAsync(stagingRoot, progress, cancellationToken);
            }

            var denoDirectory = await FindValidDenoDirectoryAsync(
                settings.DenoBinPath, cancellationToken);
            if (denoDirectory is null)
            {
                denoDirectory = await InstallDenoAsync(stagingRoot, progress, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _updateSettings(current =>
            {
                current.FfmpegBinPath = ffmpegDirectory;
                current.DenoBinPath = denoDirectory;
            });

            return new DependencySetupResult(ffmpegDirectory, denoDirectory);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
    }

    private async Task<string?> FindValidFfmpegDirectoryAsync(
        string configuredDirectory,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory) &&
            await IsValidFfmpegDirectoryAsync(configuredDirectory, cancellationToken))
        {
            return Path.GetFullPath(configuredDirectory);
        }

        var managedDirectory = Path.Combine(_toolsRoot, "FFmpeg", _ffmpegAsset.DirectoryName);
        return await IsValidFfmpegDirectoryAsync(managedDirectory, cancellationToken)
            ? managedDirectory
            : null;
    }

    private async Task<string?> FindValidDenoDirectoryAsync(
        string configuredDirectory,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory) &&
            (await DenoEngineValidator.ValidateDirectoryAsync(configuredDirectory, cancellationToken)).Count == 0)
        {
            return Path.GetFullPath(configuredDirectory);
        }

        var managedDirectory = Path.Combine(_toolsRoot, "Deno", _denoAsset.DirectoryName);
        return (await DenoEngineValidator.ValidateDirectoryAsync(managedDirectory, cancellationToken)).Count == 0
            ? managedDirectory
            : null;
    }

    private static async Task<bool> IsValidFfmpegDirectoryAsync(
        string directory,
        CancellationToken cancellationToken) =>
        (await FfmpegEngineValidator.ValidateDirectoryAsync(directory, cancellationToken)).Count == 0;

    private async Task<string> InstallFfmpegAsync(
        string stagingRoot,
        IProgress<DependencySetupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var archivePath = Path.Combine(stagingRoot, "ffmpeg.zip");
        progress?.Report(new DependencySetupProgress("FFmpeg", 0, null, "Baixando o pacote oficial BtbN…"));
        await DownloadAndVerifyArchiveAsync(
            _ffmpegAsset,
            archivePath, "FFmpeg", progress, cancellationToken);

        var extractedDirectory = Path.Combine(stagingRoot, "ffmpeg");
        Directory.CreateDirectory(extractedDirectory);
        await ExtractExecutablesAsync(
            archivePath, extractedDirectory, _ffmpegAsset.FilesToExtract, cancellationToken);
        progress?.Report(new DependencySetupProgress("FFmpeg", 0, null, "Validando codecs e conversão H.264…"));

        var issues = await FfmpegEngineValidator.ValidateDirectoryAsync(extractedDirectory, cancellationToken);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(
                $"O pacote FFmpeg não passou na validação: {string.Join("; ", issues.Select(issue => issue.Reason))}");
        }

        await SmokeTestFfmpegAsync(extractedDirectory, stagingRoot, cancellationToken);
        return PromoteDirectory(
            extractedDirectory, Path.Combine(_toolsRoot, "FFmpeg"), _ffmpegAsset.DirectoryName);
    }

    private async Task<string> InstallDenoAsync(
        string stagingRoot,
        IProgress<DependencySetupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var archivePath = Path.Combine(stagingRoot, "deno.zip");
        progress?.Report(new DependencySetupProgress("Deno", 0, null, "Baixando Deno do release oficial…"));
        await DownloadAndVerifyArchiveAsync(
            _denoAsset,
            archivePath, "Deno", progress, cancellationToken);

        var extractedDirectory = Path.Combine(stagingRoot, "deno");
        Directory.CreateDirectory(extractedDirectory);
        await ExtractExecutablesAsync(archivePath, extractedDirectory, _denoAsset.FilesToExtract, cancellationToken);
        progress?.Report(new DependencySetupProgress("Deno", 0, null, "Validando Deno…"));

        var issues = await DenoEngineValidator.ValidateDirectoryAsync(extractedDirectory, cancellationToken);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(
                $"O pacote Deno não passou na validação: {string.Join("; ", issues.Select(issue => issue.Reason))}");
        }

        var denoExe = Path.Combine(extractedDirectory, "deno.exe");
        var evalOutput = await RunProcessAsync(
            denoExe,
            ["eval", "console.log(1 + 1)"],
            stagingRoot,
            cancellationToken,
            Path.Combine(stagingRoot, ".deno-cache"));
        if (!evalOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.Trim() == "2"))
        {
            throw new InvalidDataException("Deno não passou no teste sintético de execução JavaScript.");
        }

        if (!File.Exists(_denoLicenseSourcePath))
        {
            throw new FileNotFoundException(
                "O texto MIT do Deno não está disponível na instalação do GetIt.",
                _denoLicenseSourcePath);
        }

        File.Copy(
            _denoLicenseSourcePath,
            Path.Combine(extractedDirectory, "DENO-MIT-LICENSE.md"),
            overwrite: false);

        return PromoteDirectory(extractedDirectory, Path.Combine(_toolsRoot, "Deno"), _denoAsset.DirectoryName);
    }

    private async Task DownloadAndVerifyArchiveAsync(
        DependencySetupAsset asset,
        string destinationPath,
        string component,
        IProgress<DependencySetupProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Uri);
        request.Headers.UserAgent.ParseAdd("GetIt/0.1.2");
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || finalUri.Scheme != Uri.UriSchemeHttps ||
            !(finalUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
              finalUri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
              finalUri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"{component}: o servidor redirecionou para uma origem HTTPS não aprovada.");
        }

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > 0 && contentLength > asset.MaximumBytes)
        {
            throw new InvalidDataException($"{component}: o arquivo excede o limite de tamanho permitido.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long bytesReceived = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;

            bytesReceived += read;
            if (bytesReceived > asset.MaximumBytes)
            {
                throw new InvalidDataException($"{component}: o arquivo excede o limite de tamanho permitido.");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            progress?.Report(new DependencySetupProgress(
                component, bytesReceived, contentLength, $"Baixando {component}…"));
        }

        await output.FlushAsync(cancellationToken);
        var actualSha256 = Convert.ToHexString(hash.GetHashAndReset());
        if (!actualSha256.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{component}: SHA-256 divergente. Esperado {asset.Sha256}; obtido {actualSha256}.");
        }
    }

    private static async Task ExtractExecutablesAsync(
        string archivePath,
        string destinationDirectory,
        IReadOnlyCollection<string> requiredFiles,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var fileName in requiredFiles)
        {
            var matches = archive.Entries
                .Where(entry => string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    $"O arquivo ZIP deve conter exatamente um executável '{fileName}'.");
            }

            var entry = matches[0];
            var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixFileType == 0xA000 || entry.Length <= 0 || entry.Length > 300L * 1024 * 1024)
            {
                throw new InvalidDataException($"Entrada ZIP inválida para '{fileName}'.");
            }

            var destinationPath = Path.Combine(destinationDirectory, fileName);
            await using var source = entry.Open();
            await using var destination = new FileStream(
                destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await source.CopyToAsync(destination, 81920, cancellationToken);
        }
    }

    private static async Task SmokeTestFfmpegAsync(
        string ffmpegDirectory,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(stagingRoot, "smoke-test.mp4");
        var ffmpegPath = Path.Combine(ffmpegDirectory, "ffmpeg.exe");
        await RunProcessAsync(ffmpegPath,
        [
            "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
            "color=c=black:s=64x64:d=0.2", "-frames:v", "1",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-y", outputPath
        ], ffmpegDirectory, cancellationToken);

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            throw new InvalidDataException("A conversão sintética H.264 não gerou um arquivo válido.");
        }

        var probeOutput = await RunProcessAsync(
            Path.Combine(ffmpegDirectory, "ffprobe.exe"),
            ["-v", "error", "-show_entries", "format=format_name", "-of", "default=noprint_wrappers=1:nokey=1", outputPath],
            ffmpegDirectory,
            cancellationToken);
        if (!probeOutput.Contains("mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("O FFprobe não reconheceu a mídia MP4 de teste.");
        }
    }

    private static async Task<string> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        string? denoCacheDirectory = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        if (denoCacheDirectory is not null)
        {
            process.StartInfo.Environment["DENO_DIR"] = denoCacheDirectory;
        }

        if (!process.Start())
        {
            throw new InvalidOperationException($"Não foi possível iniciar '{Path.GetFileName(executable)}'.");
        }

        using var registration = timeout.Token.Register(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception exception)
            {
                AppLogger.LogException(exception, $"Failed to stop {Path.GetFileName(executable)}");
            }
        });

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(timeout.Token));
            if (process.ExitCode != 0)
            {
                throw new InvalidDataException(
                    $"{Path.GetFileName(executable)} encerrou com código {process.ExitCode}: {errorTask.Result.Trim()}");
            }

            return $"{outputTask.Result}{Environment.NewLine}{errorTask.Result}";
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception exception)
            {
                AppLogger.LogException(exception, $"Failed to stop {Path.GetFileName(executable)}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"{Path.GetFileName(executable)} não respondeu em 30 segundos.");
        }
    }

    private static string PromoteDirectory(string stagingDirectory, string parentDirectory, string directoryName)
    {
        Directory.CreateDirectory(parentDirectory);
        var destination = Path.Combine(parentDirectory, directoryName);
        if (Directory.Exists(destination))
        {
            destination = Path.Combine(parentDirectory, $"{directoryName}-{Guid.NewGuid():N}");
        }

        Directory.Move(stagingDirectory, destination);
        return destination;
    }
}
