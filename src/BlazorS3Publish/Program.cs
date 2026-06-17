using BlazorS3Publish.Cli;
using BlazorS3Publish.Services;

namespace BlazorS3Publish;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            using var cancellationTokenSource = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                // Keep process shutdown at the outermost boundary so all inner operations receive
                // cancellation consistently via one token flow.
                eventArgs.Cancel = true;
                cancellationTokenSource.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;

            try
            {
                var options = OptionsParser.Parse(args);
                var uploadService = new UploadService();
                await uploadService.UploadAsync(options, cancellationTokenSource.Token);
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
