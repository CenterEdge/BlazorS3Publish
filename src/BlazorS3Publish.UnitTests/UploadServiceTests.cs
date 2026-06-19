using System.Text.Json;
using BlazorS3Publish.Models;
using BlazorS3Publish.Services;

namespace BlazorS3Publish.UnitTests;

public sealed class UploadServiceTests
{
    [Fact]
    public async Task UploadAsync_SourceDoesNotExist_ThrowsInvalidOperationException()
    {
        using var testDirectory = new TemporaryDirectory();
        var source = Path.Combine(testDirectory.DirectoryPath, "missing");
        var uploadService = CreateUploadService(source);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => uploadService.UploadAsync(CancellationToken.None));

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_SourceDirectoryMissingEndpointsManifest_ThrowsInvalidOperationException()
    {
        using var publishRoot = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(publishRoot.DirectoryPath, "wwwroot"));
        var uploadService = CreateUploadService(publishRoot.DirectoryPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => uploadService.UploadAsync(CancellationToken.None));

        Assert.Contains("Could not locate '*.staticwebassets.endpoints.json'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_SourceDirectoryWithMultipleEndpointsManifests_ThrowsInvalidOperationException()
    {
        using var publishRoot = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(publishRoot.DirectoryPath, "wwwroot"));
        File.WriteAllText(Path.Combine(publishRoot.DirectoryPath, "first.staticwebassets.endpoints.json"), "{\"Endpoints\":[]}");
        File.WriteAllText(Path.Combine(publishRoot.DirectoryPath, "second.staticwebassets.endpoints.json"), "{\"Endpoints\":[]}");
        var uploadService = CreateUploadService(publishRoot.DirectoryPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => uploadService.UploadAsync(CancellationToken.None));

        Assert.Contains("Found multiple endpoints manifests", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_PublishRootWithoutWwwroot_ThrowsInvalidOperationException()
    {
        using var publishRoot = new TemporaryDirectory();
        var manifestPath = Path.Combine(publishRoot.DirectoryPath, "app.staticwebassets.endpoints.json");
        File.WriteAllText(manifestPath, "{\"Endpoints\":[]}");
        var uploadService = CreateUploadService(manifestPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => uploadService.UploadAsync(CancellationToken.None));

        Assert.Contains("Expected a 'wwwroot' directory", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_ManifestContainsNoEndpoints_ThrowsInvalidOperationException()
    {
        using var publishRoot = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(publishRoot.DirectoryPath, "wwwroot"));
        var manifestPath = Path.Combine(publishRoot.DirectoryPath, "app.staticwebassets.endpoints.json");
        File.WriteAllText(manifestPath, "{\"Endpoints\":[]}");
        var uploadService = CreateUploadService(manifestPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => uploadService.UploadAsync(CancellationToken.None));

        Assert.Contains("No endpoints were found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        using var publishRoot = new TemporaryDirectory();
        var assetRoot = Path.Combine(publishRoot.DirectoryPath, "wwwroot");
        Directory.CreateDirectory(assetRoot);

        var assetFilePath = Path.Combine(assetRoot, "index.html");
        File.WriteAllText(assetFilePath, "<html></html>");

        var manifestPath = Path.Combine(publishRoot.DirectoryPath, "app.staticwebassets.endpoints.json");
        var manifest = new
        {
            Endpoints = new[]
            {
                new
                {
                    Route = "index.html",
                    AssetFile = "index.html",
                    ResponseHeaders = Array.Empty<object>()
                }
            }
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

        var uploadService = CreateUploadService(manifestPath);

        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => uploadService.UploadAsync(cancellationTokenSource.Token));
    }

    private static UploadService CreateUploadService(string source)
    {
        return new UploadService(
            new UploadOptions
            {
                Source = source,
                BucketName = "test-bucket",
                KeyPrefix = "test-prefix",
                MaxParallelUploads = 1
            });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"blazor-s3-publish-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, true);
            }
        }
    }
}
