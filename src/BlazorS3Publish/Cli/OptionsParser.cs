using System.CommandLine;
using BlazorS3Publish.Models;

namespace BlazorS3Publish.Cli;

internal static class OptionsParser
{
    public static UploadOptions Parse(string[] arguments)
    {
        var sourceDirectoryOption = new Option<string>("--source-directory");
        var bucketNameOption = new Option<string>("--bucket-name");
        var keyPrefixOption = new Option<string>("--key-prefix");
        var maxParallelUploadsOption = new Option<int?>("--max-parallel-uploads");

        var rootCommand = new RootCommand("Uploads Blazor publish output to S3.")
        {
            sourceDirectoryOption,
            bucketNameOption,
            keyPrefixOption,
            maxParallelUploadsOption
        };

        var parseResult = rootCommand.Parse(arguments);
        if (parseResult.Errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, parseResult.Errors.Select(error => error.Message)));
        }

        var sourceDirectory = parseResult.GetValue(sourceDirectoryOption);
        var bucketName = parseResult.GetValue(bucketNameOption);
        var keyPrefix = parseResult.GetValue(keyPrefixOption) ?? string.Empty;
        var maxParallelUploads = parseResult.GetValue(maxParallelUploadsOption) ?? 8;

        if (maxParallelUploads is < 1 or > 64)
        {
            throw new InvalidOperationException("Argument '--max-parallel-uploads' must be an integer between 1 and 64.");
        }

        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new InvalidOperationException("Argument '--source-directory' is required.");
        }

        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new InvalidOperationException("Argument '--bucket-name' is required.");
        }

        return new UploadOptions
        {
            SourceDirectory = sourceDirectory,
            BucketName = bucketName,
            KeyPrefix = keyPrefix,
            MaxParallelUploads = maxParallelUploads
        };
    }
}
