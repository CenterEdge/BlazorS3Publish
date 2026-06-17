using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Mime;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using BlazorS3Publish.Models;
using BlazorS3Publish.Serialization;

namespace BlazorS3Publish.Services;

internal sealed class UploadService
{
    public async Task UploadAsync(UploadOptions options, CancellationToken cancellationToken)
    {
        var (publishRoot, manifestPath) = ResolveSource(options.Source);

        var normalizedPrefix = NormalizePrefix(options.KeyPrefix);
        var assetRoot = Path.Combine(publishRoot, "wwwroot");
        if (!Directory.Exists(assetRoot))
        {
            throw new InvalidOperationException($"Source '{options.Source}' did not resolve to a publish root. Expected a 'wwwroot' directory directly under '{publishRoot}'.");
        }

        Console.WriteLine($"Asset root: {assetRoot}");
        Console.WriteLine($"Endpoints manifest: {manifestPath}");
        Console.WriteLine($"Destination: s3://{options.BucketName}/{normalizedPrefix}");

        // The endpoints manifest uses PascalCase today, but tools and SDK versions have emitted
        // mixed casing in related static-web-assets manifests. Keep parsing case-insensitive so
        // deployment behavior does not depend on exact serializer casing choices.
        await using var manifestStream = new FileStream(
            manifestPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
        var manifest = await JsonSerializer.DeserializeAsync(
            manifestStream,
            EndpointsSerializerContext.Default.EndpointsManifest,
            cancellationToken);

        if (manifest?.Endpoints is null || manifest.Endpoints.Count == 0)
        {
            throw new InvalidOperationException($"No endpoints were found in '{manifestPath}'.");
        }

        var uploadItems = new List<UploadItem>();
        // The same physical asset can appear more than once: once from manifest routing and again
        // from the fallback file scan. Track final keys so we never overwrite metadata from a
        // duplicate second upload and never pay duplicate transfer cost.
        var seenS3Keys = new HashSet<string>(StringComparer.Ordinal);
        var checksumCache = new Dictionary<string, string>(StringComparer.Ordinal);
        var manifestRouteCount = 0;
        foreach (var endpoint in manifest.Endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(endpoint.Route) || string.IsNullOrWhiteSpace(endpoint.AssetFile))
            {
                continue;
            }

            var normalizedRoute = endpoint.Route.Trim().TrimStart('/');
            var assetPath = ResolveEndpointSourcePath(assetRoot, normalizedRoute, endpoint.AssetFile);
            var manifestCacheControl = GetNameValue(endpoint.ResponseHeaders, "Cache-Control");
            var cacheControl = GetCacheControlValue(normalizedRoute, manifestCacheControl);
            var manifestContentType = GetNameValue(endpoint.ResponseHeaders, "Content-Type");
            var contentType = GetContentTypeValue(normalizedRoute, manifestContentType);
            var manifestContentEncoding = GetNameValue(endpoint.ResponseHeaders, "Content-Encoding");
            var contentEncoding = GetContentEncodingValue(manifestContentEncoding, assetPath);
            var checksumSha256 = await GetChecksumSha256ForFileAsync(assetPath, checksumCache, cancellationToken);

            var routeObjectPath = GetEndpointObjectRoute(normalizedRoute, endpoint.AssetFile);
            var s3Key = ConvertToS3ObjectKey(normalizedPrefix, routeObjectPath);
            if (!seenS3Keys.Add(s3Key))
            {
                continue;
            }

            uploadItems.Add(new UploadItem
            {
                Route = normalizedRoute,
                S3Key = s3Key,
                FilePath = assetPath,
                CacheControl = cacheControl,
                ContentType = contentType,
                ContentEncoding = contentEncoding,
                ChecksumSha256 = checksumSha256
            });

            manifestRouteCount++;
        }

        // Manifest-first is required to preserve endpoint-specific metadata, but the manifest can
        // miss files generated outside endpoint routing. This second pass keeps publish-folder
        // coverage complete so deploys do not silently omit assets.
        var fallbackRouteCount = 0;
        foreach (var filePath in Directory.EnumerateFiles(assetRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(assetRoot, filePath).Replace('\\', '/');
            var fallbackCacheControl = GetCacheControlValue(relativePath, null);
            var fallbackChecksum = await GetChecksumSha256ForFileAsync(filePath, checksumCache, cancellationToken);
            var fallbackS3Key = ConvertToS3ObjectKey(normalizedPrefix, relativePath);
            if (!seenS3Keys.Add(fallbackS3Key))
            {
                continue;
            }

            uploadItems.Add(new UploadItem
            {
                Route = relativePath,
                S3Key = fallbackS3Key,
                FilePath = filePath,
                CacheControl = fallbackCacheControl,
                ContentType = GetContentTypeValue(relativePath, null),
                ContentEncoding = GetContentEncodingValue(null, filePath),
                ChecksumSha256 = fallbackChecksum
            });

            fallbackRouteCount++;
        }

        if (uploadItems.Count == 0)
        {
            throw new InvalidOperationException($"No uploadable endpoint entries were found in '{manifestPath}'.");
        }

        Console.WriteLine($"Uploading {uploadItems.Count} routes ({manifestRouteCount} from manifest, {fallbackRouteCount} fallback) with max parallelism {options.MaxParallelUploads}...");

        // Keep deployment deterministic: honor AWS_REGION when present, otherwise use us-east-1
        // instead of relying on machine/profile defaults that vary between local and CI hosts.
        var regionName = Environment.GetEnvironmentVariable("AWS_REGION");
        var resolvedRegion = string.IsNullOrWhiteSpace(regionName) ? "us-east-1" : regionName;
        var s3Config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(resolvedRegion)
        };
        using var s3Client = new AmazonS3Client(s3Config);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxParallelUploads,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(uploadItems, parallelOptions, async (uploadItem, ct) =>
        {
            await UploadObjectAsync(
                s3Client,
                options.BucketName,
                uploadItem,
                ct);
        });

        Console.WriteLine("Upload completed successfully.");
    }

    private static async Task UploadObjectAsync(
        IAmazonS3 s3Client,
        string bucketName,
        UploadItem uploadItem,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var fileStream = File.OpenRead(uploadItem.FilePath);
        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = uploadItem.S3Key,
            InputStream = fileStream,
            ContentType = uploadItem.ContentType,
            // S3 checksum persistence for PutObject expects algorithm + value together.
            // API: https://docs.aws.amazon.com/AmazonS3/latest/API/API_PutObject.html
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
            ChecksumSHA256 = uploadItem.ChecksumSha256
        };

        request.Headers.CacheControl = uploadItem.CacheControl;
        if (!string.IsNullOrWhiteSpace(uploadItem.ContentEncoding))
        {
            request.Headers.ContentEncoding = uploadItem.ContentEncoding;
        }

        await s3Client.PutObjectAsync(request, cancellationToken);
        Console.WriteLine($"Uploaded: {uploadItem.S3Key}");
    }

    private static string NormalizePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return string.Empty;
        }

        // Deployment callers frequently pass Windows-style prefixes (e.g. "prod\\site\\").
        // S3 keys are slash-oriented, so normalize both separators to avoid split path trees.
        var segments = prefix.Trim()
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('/', segments);
    }

    private static string ResolveEndpointsManifestPath(string publishDirectory)
    {
        var manifestMatches = Directory
            .GetFiles(publishDirectory, "*.staticwebassets.endpoints.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        if (manifestMatches.Count == 0)
        {
            throw new InvalidOperationException($"Could not locate '*.staticwebassets.endpoints.json' directly under '{publishDirectory}'. Source must be a publish root when passing a directory.");
        }

        if (manifestMatches.Count > 1)
        {
            throw new InvalidOperationException($"Found multiple endpoints manifests under '{publishDirectory}'. Keep only one '*.staticwebassets.endpoints.json' file in the publish root.");
        }

        return manifestMatches[0].FullName;
    }

    private static (string PublishRoot, string ManifestPath) ResolveSource(string source)
    {
        var resolvedSource = Path.GetFullPath(source);

        if (File.Exists(resolvedSource))
        {
            var sourceDirectory = Path.GetDirectoryName(resolvedSource);
            if (sourceDirectory is null)
            {
                throw new InvalidOperationException($"Source '{source}' must include a parent directory.");
            }

            return (sourceDirectory, resolvedSource);
        }

        if (!Directory.Exists(resolvedSource))
        {
            throw new InvalidOperationException($"Source '{source}' does not exist.");
        }

        var manifestPath = ResolveEndpointsManifestPath(resolvedSource);
        return (resolvedSource, manifestPath);
    }

    private static string? GetNameValue(IReadOnlyList<NameValueEntry>? values, string name)
    {
        if (values is null)
        {
            return null;
        }

        // Raw manifest shape:
        //   "ResponseHeaders":[{"Name":"Content-Type","Value":"application/wasm"}]
        // We intentionally pick first match to keep deterministic behavior if duplicate names exist.
        return values
            .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static string GetCacheControlValue(string route, string? manifestCacheControl)
    {
        if (!string.IsNullOrWhiteSpace(manifestCacheControl))
        {
            return manifestCacheControl;
        }

        if (route.TrimStart('/').StartsWith(Constants.FrameworkAssetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Fingerprinted framework assets are content-addressed and safe to cache as immutable.
            // Cache directives reference: https://www.rfc-editor.org/rfc/rfc9111
            return Constants.ImmutableCacheControl;
        }

        // Default to revalidation-safe behavior for non-fingerprinted paths so new deployments
        // become visible immediately without waiting for stale browser caches to age out.
        return Constants.NoCacheControl;
    }

    private static string GetContentTypeValue(string route, string? manifestContentType)
    {
        if (!string.IsNullOrWhiteSpace(manifestContentType))
        {
            return manifestContentType;
        }

        var extension = Path.GetExtension(route);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return MediaTypeNames.Application.Octet;
        }

        return extension.ToLowerInvariant() switch
        {
            ".avif" => MediaTypeNames.Image.Avif,
            ".css" => MediaTypeNames.Text.Css,
            ".csv" => MediaTypeNames.Text.Csv,
            ".eot" => "application/vnd.ms-fontobject",
            ".gif" => MediaTypeNames.Image.Gif,
            ".htm" => MediaTypeNames.Text.Html,
            ".html" => MediaTypeNames.Text.Html,
            ".ico" => MediaTypeNames.Image.Icon,
            ".jpeg" => MediaTypeNames.Image.Jpeg,
            ".jpg" => MediaTypeNames.Image.Jpeg,
            ".js" => MediaTypeNames.Text.JavaScript,
            ".json" => MediaTypeNames.Application.Json,
            ".map" => MediaTypeNames.Application.Json,
            ".mjs" => MediaTypeNames.Text.JavaScript,
            ".otf" => "font/otf",
            ".pdf" => MediaTypeNames.Application.Pdf,
            ".png" => MediaTypeNames.Image.Png,
            ".svg" => MediaTypeNames.Image.Svg,
            ".txt" => MediaTypeNames.Text.Plain,
            ".wasm" => MediaTypeNames.Application.Wasm,
            ".webm" => "video/webm",
            ".webmanifest" => MediaTypeNames.Application.Manifest,
            ".webp" => MediaTypeNames.Image.Webp,
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".xml" => MediaTypeNames.Application.Xml,
            _ => MediaTypeNames.Application.Octet
        };
    }

    private static string? GetContentEncodingValue(string? manifestContentEncoding, string? assetFilePath)
    {
        if (!string.IsNullOrWhiteSpace(manifestContentEncoding))
        {
            // Observed raw values include comma-separated tokens such as:
            //   "gzip"
            //   "br"
            //   "gzip, identity"
            // We only propagate recognized encodings; otherwise we fall back to extension-based
            // inference to avoid emitting invalid Content-Encoding values.
            var firstToken = manifestContentEncoding.Split(',')[0].Trim().ToLowerInvariant();
            if (firstToken == "br")
            {
                return "br";
            }

            if (firstToken == "gzip")
            {
                return "gzip";
            }
        }

        if (string.IsNullOrWhiteSpace(assetFilePath))
        {
            return null;
        }

        return Path.GetExtension(assetFilePath).ToLowerInvariant() switch
        {
            ".br" => "br",
            ".gz" => "gzip",
            _ => null
        };
    }

    private static async Task<string> GetChecksumSha256ForFileAsync(string filePath, IDictionary<string, string> checksumCache, CancellationToken cancellationToken)
    {
        if (checksumCache.TryGetValue(filePath, out var cachedChecksum))
        {
            return cachedChecksum;
        }

        // One file can map to multiple routes (fingerprinted + non-fingerprinted aliases). Cache
        // digest by physical path so repeated route entries do not repeat disk reads/hash CPU.
        await using var fileStream = new FileStream(
            filePath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(fileStream, cancellationToken);
        var checksum = Convert.ToBase64String(hashBytes);
        checksumCache[filePath] = checksum;
        return checksum;
    }

    private static string ResolveEndpointSourcePath(string assetRoot, string route, string assetFile)
    {
        // AssetFile is the canonical manifest path. Route-path fallback is intentional because
        // some manifests omit AssetFile for certain static-file shapes while still emitting Route.
        var normalizedAssetFilePath = NormalizeRelativeAssetPath(assetFile);
        var assetFilePath = Path.Combine(assetRoot, normalizedAssetFilePath);
        if (File.Exists(assetFilePath))
        {
            return assetFilePath;
        }

        var normalizedRoutePath = NormalizeRelativeAssetPath(route);
        var routePath = Path.Combine(assetRoot, normalizedRoutePath);
        if (File.Exists(routePath))
        {
            return routePath;
        }

        throw new InvalidOperationException($"Endpoint route '{route}' was not found and asset fallback '{assetFile}' was also not found under '{assetRoot}'.");
    }

    private static string NormalizeRelativeAssetPath(string pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return string.Empty;
        }

        // Manifest and caller inputs can mix separators; normalize once so all path probes work on
        // both Windows and Linux runners.
        var segments = pathValue.Trim()
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return Path.Combine(segments);
    }

    private static string ConvertToS3ObjectKey(string prefix, string route)
    {
        var routePart = route.Trim().TrimStart('/').Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(routePart))
        {
            throw new InvalidOperationException("Manifest route cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return routePart;
        }

        return $"{prefix}/{routePart}";
    }

    private static string GetEndpointObjectRoute(string route, string assetFile)
    {
        var normalizedRoute = route.Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalizedRoute))
        {
            return string.Empty;
        }

        // Compressed variants often reuse the same logical route as uncompressed endpoints.
        // Persist .br/.gz in the object key so compressed and uncompressed artifacts do not collide.
        var assetExtension = Path.GetExtension(assetFile).ToLowerInvariant();
        if (assetExtension != ".br" && assetExtension != ".gz")
        {
            return normalizedRoute;
        }

        if (normalizedRoute.EndsWith(assetExtension, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedRoute;
        }

        return $"{normalizedRoute}{assetExtension}";
    }
}
