using BlazorS3Publish.Cli;
using System.CommandLine;

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
        var rootCommand = OptionsParser.CreateRootCommand();
        return await rootCommand.Parse(args).InvokeAsync(new InvocationConfiguration(), cancellationTokenSource.Token);
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
