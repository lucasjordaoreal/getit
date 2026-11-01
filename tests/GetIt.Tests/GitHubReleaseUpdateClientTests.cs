using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GetIt_App.Services;
using Xunit;

namespace GetIt.Tests;

public sealed class GitHubReleaseUpdateClientTests
{
    [Theory]
    [InlineData("0.1.2", 0, 1, 2)]
    [InlineData("v0.1.2", 0, 1, 2)]
    [InlineData("V1.20.300", 1, 20, 300)]
    public void ParseStableVersionTag_AcceptsPlainAndPrefixedVersions(string tag, int major, int minor, int patch)
    {
        Assert.Equal(new Version(major, minor, patch), GitHubReleaseUpdateClient.ParseStableVersionTag(tag));
    }

    [Theory]
    [InlineData("")]
    [InlineData("release-0.1.2")]
    [InlineData("0.1")]
    [InlineData("0.1.2-beta")]
    [InlineData("0.01.2")]
    public void ParseStableVersionTag_RejectsInvalidOrPrereleaseTags(string tag)
    {
        Assert.Throws<InvalidDataException>(() => GitHubReleaseUpdateClient.ParseStableVersionTag(tag));
    }

    [Fact]
    public async Task GetLatestReleaseAsync_ReturnsOnlyANewerPublishedStableRelease()
    {
        var payload = CreateRelease("v0.1.3", Encoding.UTF8.GetBytes("setup"));
        using var httpClient = new HttpClient(new FakeHandler((_, _) =>
            Task.FromResult(JsonResponse(payload))));
        var client = new GitHubReleaseUpdateClient(httpClient);

        var release = await client.GetLatestReleaseAsync(new Version(0, 1, 2, 0));

        Assert.NotNull(release);
        Assert.Equal("v0.1.3", release.TagName);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_DoesNotOfferAnOlderRelease()
    {
        var payload = CreateRelease("0.1.1", Encoding.UTF8.GetBytes("setup"));
        using var httpClient = new HttpClient(new FakeHandler((_, _) =>
            Task.FromResult(JsonResponse(payload))));
        var client = new GitHubReleaseUpdateClient(httpClient);

        var release = await client.GetLatestReleaseAsync(new Version(0, 1, 2));

        Assert.Null(release);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_RejectsDraftsAndPrereleases()
    {
        var draft = CreateRelease("0.1.3", Encoding.UTF8.GetBytes("setup"));
        draft.Draft = true;
        using var httpClient = new HttpClient(new FakeHandler((_, _) =>
            Task.FromResult(JsonResponse(draft))));
        var client = new GitHubReleaseUpdateClient(httpClient);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetLatestReleaseAsync(new Version(0, 1, 2)));
    }

    [Fact]
    public void SelectInstaller_IgnoresSourceArchivesAndSelectsTheExactReleaseAsset()
    {
        var release = CreateRelease("0.1.3", Encoding.UTF8.GetBytes("setup"));
        release.Assets =
        [
            new GitHubAsset { Name = "GetIt-Source.zip" },
            new GitHubAsset { Name = "GetIt-Source.tar.gz" },
            release.Assets[0]
        ];

        var installer = GitHubReleaseUpdateClient.SelectInstaller(release);

        Assert.Equal("GetIt-Setup-v0.1.3.exe", installer.Name);
    }

    [Fact]
    public void SelectInstaller_RejectsMissingAmbiguousOrUntrustedInstallerAssets()
    {
        var missing = CreateRelease("0.1.3", Encoding.UTF8.GetBytes("setup"));
        missing.Assets = [new GitHubAsset { Name = "source.zip" }];
        Assert.Throws<InvalidDataException>(() => GitHubReleaseUpdateClient.SelectInstaller(missing));

        var ambiguous = CreateRelease("0.1.3", Encoding.UTF8.GetBytes("setup"));
        ambiguous.Assets = [ambiguous.Assets[0], ambiguous.Assets[0]];
        Assert.Throws<InvalidDataException>(() => GitHubReleaseUpdateClient.SelectInstaller(ambiguous));

        var invalidDigest = CreateRelease("0.1.3", Encoding.UTF8.GetBytes("setup"));
        invalidDigest.Assets[0].Digest = null;
        Assert.Throws<InvalidDataException>(() => GitHubReleaseUpdateClient.SelectInstaller(invalidDigest));

        var invalidUrl = CreateRelease("0.1.3", Encoding.UTF8.GetBytes("setup"));
        invalidUrl.Assets[0].BrowserDownloadUrl = "https://example.invalid/GetIt-Setup-v0.1.3.exe";
        Assert.Throws<InvalidDataException>(() => GitHubReleaseUpdateClient.SelectInstaller(invalidUrl));
    }

    [Fact]
    public async Task DownloadInstallerAsync_StreamsAndVerifiesHashAfterTrustedGitHubRedirect()
    {
        var bytes = Encoding.UTF8.GetBytes("known test installer bytes");
        var release = CreateRelease("v0.1.3", bytes);
        using var directory = new TemporaryDirectory();
        var handler = new FakeHandler((request, _) =>
        {
            if (request.RequestUri!.Host == "github.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = new Uri("https://release-assets.githubusercontent.com/test-asset");
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        });
        using var httpClient = new HttpClient(handler);
        var client = new GitHubReleaseUpdateClient(httpClient);
        var progressUpdates = new List<UpdateDownloadProgress>();
        var progress = new InlineProgress<UpdateDownloadProgress>(progressUpdates.Add);

        using var downloaded = await client.DownloadInstallerAsync(release, progress, temporaryRoot: directory.Path);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(downloaded.Path));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("release-assets.githubusercontent.com", handler.Requests[1].Host);
        Assert.NotEmpty(progressUpdates);
    }

    [Fact]
    public async Task DownloadInstallerAsync_RejectsHashMismatchAndCleansItsTemporaryDirectory()
    {
        var release = CreateRelease("0.1.3", Encoding.UTF8.GetBytes("expected bytes"));
        using var directory = new TemporaryDirectory();
        using var httpClient = new HttpClient(new FakeHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("different byte"))
            })));
        var client = new GitHubReleaseUpdateClient(httpClient);

        await Assert.ThrowsAsync<CryptographicException>(() =>
            client.DownloadInstallerAsync(release, temporaryRoot: directory.Path));

        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task DownloadInstallerAsync_RejectsInterruptedDownloadsAndCleansPartialFiles()
    {
        var bytes = Encoding.UTF8.GetBytes("expected complete installer");
        var release = CreateRelease("0.1.3", bytes);
        using var directory = new TemporaryDirectory();
        using var httpClient = new HttpClient(new FakeHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingReadStream(bytes, 5))
            })));
        var client = new GitHubReleaseUpdateClient(httpClient);

        await Assert.ThrowsAsync<IOException>(() =>
            client.DownloadInstallerAsync(release, temporaryRoot: directory.Path));

        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task DownloadInstallerAsync_CancellationRemovesPartialDownload()
    {
        var bytes = Encoding.UTF8.GetBytes("expected complete installer");
        var release = CreateRelease("0.1.3", bytes);
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        using var httpClient = new HttpClient(new FakeHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new BlockingReadStream())
            })));
        var client = new GitHubReleaseUpdateClient(httpClient);
        var download = client.DownloadInstallerAsync(
            release,
            cancellationToken: cancellation.Token,
            temporaryRoot: directory.Path);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);

        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task DownloadInstallerAsync_RejectsUntrustedRedirectsAndHttpErrors()
    {
        var bytes = Encoding.UTF8.GetBytes("installer");
        var release = CreateRelease("0.1.3", bytes);
        using var directory = new TemporaryDirectory();
        var redirectHandler = new FakeHandler((_, _) =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri("https://example.invalid/asset.exe");
            return Task.FromResult(redirect);
        });
        using (var httpClient = new HttpClient(redirectHandler))
        {
            var client = new GitHubReleaseUpdateClient(httpClient);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                client.DownloadInstallerAsync(release, temporaryRoot: directory.Path));
            Assert.Single(redirectHandler.Requests);
        }

        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
        using var errorClient = new HttpClient(new FakeHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        var errorClientService = new GitHubReleaseUpdateClient(errorClient);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            errorClientService.DownloadInstallerAsync(release, temporaryRoot: directory.Path));
        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task GetLatestReleaseAsync_RejectsRedirectedMetadata()
    {
        using var httpClient = new HttpClient(new FakeHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://example.invalid/release.json");
            return Task.FromResult(response);
        }));
        var client = new GitHubReleaseUpdateClient(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetLatestReleaseAsync(new Version(0, 1, 2)));
    }

    private static GitHubRelease CreateRelease(string tag, byte[] payload)
    {
        var version = GitHubReleaseUpdateClient.ParseStableVersionTag(tag);
        var installerName = $"GetIt-Setup-v{version}.exe";
        return new GitHubRelease
        {
            TagName = tag,
            Assets =
            [
                new GitHubAsset
                {
                    Name = installerName,
                    BrowserDownloadUrl =
                        $"https://github.com/lucasjordaoreal/getit/releases/download/{Uri.EscapeDataString(tag)}/{installerName}",
                    Digest = $"sha256:{Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()}",
                    Size = payload.Length,
                    ContentType = "application/x-msdownload"
                }
            ]
        };
    }

    private static HttpResponseMessage JsonResponse(GitHubRelease release) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(release), Encoding.UTF8, "application/json")
    };

    private sealed class FakeHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return responseFactory(request, cancellationToken);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class FailingReadStream(byte[] content, int bytesBeforeFailure) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_position >= bytesBeforeFailure)
            {
                return ValueTask.FromException<int>(new IOException("Simulated interrupted stream."));
            }

            var count = Math.Min(Math.Min(buffer.Length, bytesBeforeFailure - _position), content.Length - _position);
            content.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"GetIt-release-update-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
