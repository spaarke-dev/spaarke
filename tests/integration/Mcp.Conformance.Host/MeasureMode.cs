using System.Diagnostics;
using System.Text;
using Sprk.Bff.Api.Tests.Api.Mcp;
using Sprk.Bff.Api.Tests.Regression.Mcp;

namespace Spaarke.Mcp.ConformanceHost;

/// <summary>
/// TESTS ONLY (spaarkeai-mcp-bff-api-r1 task 025, NFR-01 in-process, NFR-02). <c>dotnet run --project tests/integration/Mcp.Conformance.Host -c Release -- --measure</c>
/// (or the built dll with <c>--measure</c>): per tool, 5 warm-up calls and 50 timed <c>tools/call</c> round trips through the production MCP host
/// (<see cref="DecisionToolsTestHost"/>: real Kestrel loopback, real JwtBearer, the rate limiter, the A10 filter, the SDK transport and the tool) over the
/// module-boundary doubles of the size tests, in two cases: PLAUSIBLE (the 4-matter lane, 20 evidence lines) and WORST (200 items of escape-heavy text over
/// 50 matters, 1,000 evidence lines of the longest shape). Prints p50, p95, max and the largest result size per tool as a Markdown table.
/// <para>Exit code 1 when any call returns <c>isError</c>, throws, or returns 64 KiB or more. It never fails on elapsed time: this is a measurement, kept out
/// of xUnit discovery on purpose (a timing bound on a shared runner flakes; tests/CLAUDE.md bans Stopwatch in tests). Live latency on the dev BFF is task 042.</para>
/// </summary>
internal static class MeasureMode
{
    private const int Samples = 50;
    private const int WarmUp = 5; // the first calls pay JIT and first-request setup; not part of the 50

    public static async Task<int> RunAsync()
    {
        var failures = new List<string>();
        var output = new StringBuilder();
        output.AppendLine($"Measured {DateTime.Now:yyyy-MM-dd} on {Environment.OSVersion.VersionString}, .NET {Environment.Version}, {(IsDebug ? "Debug" : "Release")} build.");
        foreach (var (name, seed, lines, maxLength) in new (string, DecideLaneSeed, int, bool)[]
        {
            ("plausible: 4-matter lane, 20 evidence lines", DecideLaneSeed.Build(), 20, false),
            ("worst case: 50 matters of escape-heavy text, 1,000 evidence lines of the longest shape", DecideLaneSeed.BuildWorstCase(), 1_000, true),
        })
        {
            var capability = new FiveToolCapability(seed, lines, maxLength);
            await using var host = await DecisionToolsTestHost.StartAsync(capability, new FiveToolLookup());
            var signal = capability.TargetSignalId;
            var calls = new (string Tool, object Arguments)[]
            {
                ("spaarke_show_decisions", new { category = "all" }),
                ("spaarke_list_decision_page", new { }),
                ("spaarke_get_decision_plan", new { signalId = signal }),
                ("spaarke_search_decision_lookup", new { signalId = signal, parameterCode = "firm", query = "co" }),
                ("spaarke_complete_decision", new { signalId = signal, review = new { reviewId = Guid.NewGuid(), confirmed = true, actions = Array.Empty<object>() } }),
            };

            output.AppendLine().AppendLine($"### {name}").AppendLine();
            output.AppendLine("| Tool | p50 ms | p95 ms | max ms | largest result (bytes) |").AppendLine("|---|---|---|---|---|");
            foreach (var (tool, arguments) in calls)
            {
                // One caller per tool: the 60/min `mcp` rate limit is per caller, and 55 calls fit.
                var token = DecisionToolsTestHost.MintToken(Guid.NewGuid());
                var millis = new List<double>();
                var largest = 0;
                for (var i = 0; i < WarmUp + Samples; i++)
                {
                    var start = Stopwatch.GetTimestamp();
                    try
                    {
                        var result = await host.CallAsync(tool, arguments, token);
                        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        if (McpResult.IsError(result))
                        {
                            failures.Add($"{name} / {tool}: call {i + 1} returned isError: {result.GetRawText()[..Math.Min(300, result.GetRawText().Length)]}");
                            break;
                        }

                        var bytes = McpResult.Bytes(result);
                        largest = Math.Max(largest, bytes);
                        if (bytes >= McpResult.MaxBytes)
                        {
                            failures.Add($"{name} / {tool}: call {i + 1} returned {bytes} bytes (64 KiB or more)");
                            break;
                        }

                        if (i >= WarmUp)
                        {
                            millis.Add(elapsed);
                        }
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{name} / {tool}: call {i + 1} threw {ex.GetType().Name}: {ex.Message[..Math.Min(300, ex.Message.Length)]}");
                        break;
                    }
                }

                if (millis.Count == Samples)
                {
                    millis.Sort();
                    output.AppendLine($"| `{tool}` | {Percentile(millis, 0.50):F1} | {Percentile(millis, 0.95):F1} | {millis[^1]:F1} | {largest} |");
                }
                else
                {
                    output.AppendLine($"| `{tool}` | FAILED | | | {largest} |");
                }
            }
        }

        await Console.Out.WriteLineAsync(output.ToString());
        foreach (var failure in failures)
        {
            await Console.Error.WriteLineAsync("MEASURE FAILURE: " + failure);
        }

        return failures.Count == 0 ? 0 : 1;
    }

    private static bool IsDebug =>
#if DEBUG
        true;
#else
        false;
#endif

    private static double Percentile(List<double> sorted, double p) => sorted[(int)Math.Ceiling(p * sorted.Count) - 1];
}
