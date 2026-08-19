using DotNetRepoInspector.Engine;

namespace DotNetRepoInspector.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using var cancellationSource = new CancellationTokenSource();
        ConsoleCancelEventHandler cancellationHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };

        Console.CancelKeyPress += cancellationHandler;
        try
        {
            var application = new CliApplication(new RepositoryInspector());
            return await application.RunAsync(
                args,
                Console.Out,
                Console.Error,
                cancellationSource.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancellationHandler;
        }
    }
}
