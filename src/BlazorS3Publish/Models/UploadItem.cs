namespace BlazorS3Publish.Models;

internal sealed class UploadItem
{
    public required string Route { get; set; }

    public required string S3Key { get; set; }

    public required string FilePath { get; set; }

    public required string CacheControl { get; set; }

    public required string ContentType { get; set; }

    public string? ContentEncoding { get; set; }

    public required string ChecksumSha256 { get; set; }
}
