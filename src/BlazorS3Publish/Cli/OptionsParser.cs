using System.CommandLine;
using BlazorS3Publish.Models;
using BlazorS3Publish.Services;

namespace BlazorS3Publish.Cli;

internal static class OptionsParser
{
    public static RootCommand CreateRootCommand()
    {
        var sourceOption = new Option<string>("--source")
        {
            Required = true,
            Description = "Path to the Blazor publish output directory."
        };
        var bucketNameOption = new Option<string>("--bucket-name")
        {
            Required = true,
            Description = "S3 bucket name to upload to."
        };
        var keyPrefixOption = new Option<string>("--key-prefix")
        {
            Description = "Optional S3 key prefix to prepend to uploaded files."
        };
        var maxParallelUploadsOption = new Option<int?>("--max-parallel-uploads")
        {
            Description = "Maximum concurrent uploads (1-64)."
        };
        maxParallelUploadsOption.Validators.Add(
            result =>
            {
                var maxParallelUploads = result.GetValueOrDefault<int?>();
                if (maxParallelUploads is < 1 or > 64)
                {
                    result.AddError("Option '--max-parallel-uploads' must be an integer between 1 and 64.");
                }
            });

        var rootCommand = new RootCommand("Uploads Blazor publish output to S3.")
        {
            sourceOption,
            bucketNameOption,
            keyPrefixOption,
            maxParallelUploadsOption
        };
        rootCommand.SetAction(
            async (parseResult, cancellationToken) =>
            {
                var source = parseResult.GetValue(sourceOption);
                var bucketName = parseResult.GetValue(bucketNameOption);
                if (source is null || bucketName is null)
                {
                    throw new InvalidOperationException("Required options were not provided.");
                }

                var options = new UploadOptions
                {
                    Source = source,
                    BucketName = bucketName,
                    KeyPrefix = parseResult.GetValue(keyPrefixOption) ?? string.Empty,
                    MaxParallelUploads = parseResult.GetValue(maxParallelUploadsOption) ?? 8
                };

                var uploadService = new UploadService();
                await uploadService.UploadAsync(options, cancellationToken);
                return 0;
            });

        return rootCommand;
    }
}
