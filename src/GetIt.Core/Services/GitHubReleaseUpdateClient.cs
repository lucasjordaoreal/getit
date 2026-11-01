using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GetIt_App.Services;

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = string.Empty;
}

public sealed record UpdateDownloadProgress(long BytesDownloaded, long TotalBytes)
{
    public int Percentage => TotalBytes <= 0
        ? 0
        : (int)Math.Clamp(BytesDownloaded * 100d / TotalBytes, 0, 100);
}

public sealed class VerifiedInstallerDownload : IDisposable
{
    private string? _directoryPath;

    internal VerifiedInstallerDownload(string path, string directoryPath)
    {
        Path = path;
        _directoryPath = directoryPath;
    }

    public string Path { get; }

    public void Dispose()
    {
        var directoryPath = Interlocked.Exchange(ref _directoryPath, null);
        if (directoryPath is not null && Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }
}

public sealed class GitHubReleaseUpdateClient
{
    public const long MaximumInstallerSize = 512L * 1024 * 1024;
    private const int MaximumReleaseMetadataSize = 1024 * 1024;
    private const int MaximumRedirects = 5;
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);
    private static readonly Uri LatestReleaseUri = new(
        "https://api.github.com/repos/lucasjordaoreal/getit/releases/latest");
    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdateClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<GitHubRelease?> GetLatestReleaseAsync(
        Version installedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installedVersion);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ApiTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
            request.Headers.UserAgent.ParseAdd("GetIt-UpdateService");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is { } responseUri && responseUri != LatestReleaseUri)
            {
                throw new InvalidDataException("A resposta de versões não veio do endpoint oficial do GitHub.");
            }

            response.EnsureSuccessStatusCode();
            await using var responseStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var metadata = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var read = await responseStream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (metadata.Length + read > MaximumReleaseMetadataSize)
                {
                    throw new InvalidDataException("Os metadados do release excedem o limite permitido.");
                }

                await metadata.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
            }

            metadata.Position = 0;
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                metadata,
                cancellationToken: timeout.Token).ConfigureAwait(false)
                ?? throw new InvalidDataException("O GitHub não retornou um release válido.");
            if (release.Draft || release.Prerelease)
            {
                throw new InvalidDataException("O GitHub retornou uma versão que não é um release estável publicado.");
            }

            var availableVersion = ParseStableVersionTag(release.TagName);
            if (availableVersion <= installedVersion)
            {
                return null;
            }

            _ = SelectInstaller(release);
            return release;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("A consulta de releases do GitHub excedeu o tempo limite de 30 segundos.");
        }
    }

    public static Version ParseStableVersionTag(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new InvalidDataException("A tag do release não pode estar vazia.");
        }
        var normalized = tagName.StartsWith('v') || tagName.StartsWith('V')
            ? tagName[1..]
            : tagName;
        var parts = normalized.Split('.');
        if (parts.Length != 3
            || parts.Any(part => part.Length == 0
                || (part.Length > 1 && part[0] == '0')
                || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            throw new InvalidDataException($"A tag do release não é uma versão estável válida: {tagName}");
        }

        var numbers = parts
            .Select(part => int.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture))
            .ToArray();
        return new Version(numbers[0], numbers[1], numbers[2]);
    }

    public static GitHubAsset SelectInstaller(GitHubRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.Draft || release.Prerelease)
        {
            throw new InvalidDataException("Não é permitido instalar um release preliminar ou não publicado.");
        }

        var version = ParseStableVersionTag(release.TagName);
        var expectedName = $"GetIt-Setup-v{version}.exe";
        var assets = release.Assets ?? Array.Empty<GitHubAsset>();
        var matchingAssets = assets
            .Where(asset => asset is not null)
            .Where(asset => string.Equals(asset.Name, expectedName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matchingAssets.Length != 1)
        {
            throw new InvalidDataException(matchingAssets.Length == 0
                ? $"O release não contém o instalador esperado '{expectedName}'."
                : $"O release contém mais de um asset chamado '{expectedName}'.");
        }

        var installer = matchingAssets[0];
        if (installer.Size <= 0 || installer.Size > MaximumInstallerSize)
        {
            throw new InvalidDataException("O tamanho do instalador está fora dos limites permitidos.");
        }

        if (!IsSupportedInstallerContentType(installer.ContentType))
        {
            throw new InvalidDataException("O tipo de conteúdo do instalador não é reconhecido.");
        }

        _ = ParseSha256Digest(installer.Digest);
        var expectedUri = CreateOfficialAssetUri(release.TagName, installer.Name);
        if (!Uri.TryCreate(installer.BrowserDownloadUrl, UriKind.Absolute, out var assetUri)
            || assetUri != expectedUri)
        {
            throw new InvalidDataException("A URL do instalador não corresponde ao asset oficial do release.");
        }

        return installer;
    }

    public async Task<VerifiedInstallerDownload> DownloadInstallerAsync(
        GitHubRelease release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? temporaryRoot = null)
    {
        var installer = SelectInstaller(release);
        var expectedDigest = ParseSha256Digest(installer.Digest);
        var expectedUri = CreateOfficialAssetUri(release.TagName, installer.Name);
        var root = Path.GetFullPath(temporaryRoot ?? Path.GetTempPath());
        Directory.CreateDirectory(root);
        var operationDirectory = Path.Combine(root, $"GetItUpdate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(operationDirectory);
        var installerPath = Path.Combine(operationDirectory, installer.Name);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DownloadTimeout);
        try
        {
            using var response = await SendAssetRequestAsync(expectedUri, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > MaximumInstallerSize
                || (contentLength.HasValue && contentLength.Value != installer.Size))
            {
                throw new InvalidDataException("O tamanho informado pelo servidor não corresponde ao asset publicado.");
            }

            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using var output = new FileStream(
                installerPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long totalBytes = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > MaximumInstallerSize || totalBytes > installer.Size)
                {
                    throw new InvalidDataException("O instalador excede o tamanho publicado ou o limite permitido.");
                }

                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                progress?.Report(new UpdateDownloadProgress(totalBytes, installer.Size));
            }

            if (totalBytes != installer.Size)
            {
                throw new EndOfStreamException("O download do instalador foi interrompido ou está incompleto.");
            }

            await output.FlushAsync(timeout.Token).ConfigureAwait(false);
            var actualDigest = hash.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(expectedDigest, actualDigest))
            {
                throw new CryptographicException("O SHA-256 do instalador não corresponde ao digest publicado pelo GitHub.");
            }

            progress?.Report(new UpdateDownloadProgress(totalBytes, installer.Size));
            return new VerifiedInstallerDownload(installerPath, operationDirectory);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            DeleteOperationDirectory(operationDirectory);
            throw new TimeoutException("O download do instalador excedeu o tempo limite de 30 minutos.");
        }
        catch
        {
            DeleteOperationDirectory(operationDirectory);
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendAssetRequestAsync(Uri assetUri, CancellationToken cancellationToken)
    {
        EnsureTrustedAssetUri(assetUri);
        var currentUri = assetUri;
        for (var redirectCount = 0; redirectCount <= MaximumRedirects; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.UserAgent.ParseAdd("GetIt-UpdateService");
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
            {
                throw new HttpRequestException("O servidor retornou um redirecionamento sem destino.");
            }

            if (redirectCount == MaximumRedirects)
            {
                throw new HttpRequestException("O download excedeu o limite de redirecionamentos.");
            }

            currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            EnsureTrustedAssetUri(currentUri);
        }

        throw new HttpRequestException("Não foi possível resolver o endereço do asset do GitHub.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static void EnsureTrustedAssetUri(Uri uri)
    {
        var trustedHost = string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !trustedHost
            || uri.Port != 443
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException("O destino do download não é um host HTTPS confiável do GitHub.");
        }
    }

    private static Uri CreateOfficialAssetUri(string tagName, string assetName)
    {
        var tag = ParseStableVersionTag(tagName);
        var expectedName = $"GetIt-Setup-v{tag}.exe";
        if (!string.Equals(assetName, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("O nome do asset não corresponde ao instalador oficial do GetIt.");
        }

        return new Uri(
            $"https://github.com/lucasjordaoreal/getit/releases/download/{Uri.EscapeDataString(tagName)}/{Uri.EscapeDataString(assetName)}");
    }

    private static byte[] ParseSha256Digest(string? digest)
    {
        if (digest is null
            || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            || digest.Length != 71)
        {
            throw new InvalidDataException("O asset não contém um digest SHA-256 válido. A instalação foi bloqueada.");
        }

        try
        {
            return Convert.FromHexString(digest[7..]);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("O asset contém um digest SHA-256 inválido. A instalação foi bloqueada.", exception);
        }
    }

    private static bool IsSupportedInstallerContentType(string? contentType) =>
        string.Equals(contentType, "application/x-msdownload", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "application/vnd.microsoft.portable-executable", StringComparison.OrdinalIgnoreCase);

    private static void DeleteOperationDirectory(string operationDirectory)
    {
        if (Directory.Exists(operationDirectory))
        {
            Directory.Delete(operationDirectory, recursive: true);
        }
    }
}
