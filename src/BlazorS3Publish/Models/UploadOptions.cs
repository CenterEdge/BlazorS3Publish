namespace BlazorS3Publish.Models;

internal sealed class UploadOptions
{
    public required string Source { get; set; }

    public required string BucketName { get; set; }

    public required string KeyPrefix { get; set; }

    public int MaxParallelUploads { get; set; }
}
