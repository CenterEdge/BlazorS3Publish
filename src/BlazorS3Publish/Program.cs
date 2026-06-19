using System.CommandLine;
using BlazorS3Publish.Models;
using BlazorS3Publish.Services;

try
{
    using var cancellationTokenSource = new CancellationTokenSource();

    void CancelHandler(object? sender, ConsoleCancelEventArgs eventArgs)
    {
        // Keep process shutdown at the outermost boundary so all inner operations receive
        // cancellation consistently via one token flow.
        eventArgs.Cancel = true;
        cancellationTokenSource.Cancel();
    }
    Console.CancelKeyPress += CancelHandler;

    try
    {
        var sourceOption = new Option<string>("--source")
        {
            Required = true,
            Description = "Path to the Blazor publish output directory or staticassets.endpoints.json file."
        };
        var bucketNameOption = new Option<string>("--bucket-name")
        {
            Required = true,
            Description = "S3 bucket name to upload to."
        };
        var keyPrefixOption = new Option<string>("--key-prefix")
        {
            Description = "S3 key prefix to prepend to uploaded files."
        };
        var maxParallelUploadsOption = new Option<int?>("--max-parallel-uploads")
        {
            Description = "Maximum concurrent uploads (1-64).",
            Validators =
            {
                result =>
                {
                    var maxParallelUploads = result.GetValueOrDefault<int?>();
                    if (maxParallelUploads is < 1 or > 64)
                    {
                        result.AddError("Option '--max-parallel-uploads' must be an integer between 1 and 64.");
                    }
                }
            }
        };

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

                var uploadService = new UploadService(options);
                await uploadService.UploadAsync(cancellationToken);
                return 0;
            });

        return await rootCommand.Parse(args).InvokeAsync(new InvocationConfiguration(), cancellationTokenSource.Token);
    }
    finally
    {
        Console.CancelKeyPress -= CancelHandler;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
