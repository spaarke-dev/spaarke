using Microsoft.AspNetCore.Mvc.Testing;

namespace Spaarke.Mcp.ConformanceHost;

/// <summary>
/// TESTS ONLY (spaarkeai-mcp-bff-api-r1 task 025). Starts <see cref="ConformanceBffFactory"/> and waits to be stopped.
/// Usage: <c>dotnet run --project tests/integration/Mcp.Conformance.Host --no-build -- [injectedPort] [directPort]</c> (defaults 5081, 5082).
/// Prints one line when the server is up: <c>READY &lt;injected url&gt; &lt;direct url&gt;</c>. Stops on Ctrl+C / SIGTERM.
/// </summary>
internal static class ConformanceHostMain
{
    private static async Task<int> Main(string[] args)
    {
        var injected = args.Length > 0 ? int.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 5081;
        var direct = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 5082;

        await using var factory = new ConformanceBffFactory(direct, injected);
        factory.StartServer();
        await Console.Out.WriteLineAsync($"READY http://127.0.0.1:{injected}/mcp/legal-operations http://127.0.0.1:{direct}/mcp/legal-operations");

        using var stop = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Set();
        stop.Wait();
        return 0;
    }
}
