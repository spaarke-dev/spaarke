// MEASUREMENT, NOT AN ASSERTION ON TIME (spaarkeai-mcp-bff-api-r1 task 025, NFR-01 in-process, NFR-02). KEEP path: regression.
//
// This class only COLLECTS: per tool, 50 timed tools/call round trips through the production MCP host (real Kestrel loopback, real JwtBearer,
// the rate limiter, the A10 filter, the SDK transport and the tool) over the module-boundary doubles of McpFiveToolFixtures.cs, then the
// p95, the maximum and the largest result size. It asserts only that every call succeeded 50 times and stayed under 64 KiB; it asserts
// NOTHING about elapsed time, because a wall-clock bound on a shared CI runner is a flake (tests/CLAUDE.md bans Stopwatch, DateTime.UtcNow
// and Task.Delay in tests). The clock is TimeProvider.GetTimestamp, read around the call and never compared to a threshold.
// The table goes to the test output, and to the file named by MCP_NFR_MEASUREMENTS_FILE when that variable is set:
//   MCP_NFR_MEASUREMENTS_FILE=$PWD/measurements.md dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~McpToolMeasurementTests"
// Live latency on the dev BFF is task 042; this is the in-process floor.

using System.Text;
using FluentAssertions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.Api.Mcp;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Regression.Mcp;

public sealed class McpToolMeasurementTests(ITestOutputHelper output)
{
    private const int Samples = 50;
    private const int WarmUp = 5; // the first calls pay JIT and first-request setup; reported separately in the notes, not part of the 50

    [Fact]
    public async Task MeasureEveryTool_P95OverFiftyCalls_AndTheLargestResult()
    {
        var capability = new FiveToolCapability(DecideLaneSeed.Build(), evidenceLines: 20);
        await using var host = await DecisionToolsTestHost.StartAsync(capability, new FiveToolLookup());
        var signal = capability.TargetSignalId;
        var calls = new (string Tool, object Arguments)[]
        {
            (ShowDecisionsTool.Name, new { category = "all" }),
            (ListDecisionPageTool.Name, new { }),
            (GetDecisionPlanTool.ToolName, new { signalId = signal }),
            (SearchDecisionLookupTool.ToolName, new { signalId = signal, parameterCode = "firm", query = "co" }),
            (CompleteDecisionTool.ToolName, new { signalId = signal, review = new { reviewId = Guid.NewGuid(), confirmed = true, actions = Array.Empty<object>() } }),
        };

        var table = new StringBuilder();
        table.AppendLine("| Tool | p50 ms | p95 ms | max ms | largest result (bytes) |");
        table.AppendLine("|---|---|---|---|---|");
        foreach (var (tool, arguments) in calls)
        {
            // One caller per tool: the 60/min `mcp` rate limit is per caller, and 55 calls fit.
            var token = DecisionToolsTestHost.MintToken(Guid.NewGuid());
            for (var i = 0; i < WarmUp; i++)
            {
                await host.CallAsync(tool, arguments, token);
            }

            var millis = new List<double>();
            var largest = 0;
            for (var i = 0; i < Samples; i++)
            {
                var start = TimeProvider.System.GetTimestamp();
                var result = await host.CallAsync(tool, arguments, token);
                millis.Add(TimeProvider.System.GetElapsedTime(start).TotalMilliseconds);
                largest = Math.Max(largest, McpResult.Bytes(result));
            }

            millis.Should().HaveCount(Samples);
            largest.Should().BeLessThan(McpResult.MaxBytes);
            millis.Sort();
            table.AppendLine($"| `{tool}` | {Percentile(millis, 0.50):F1} | {Percentile(millis, 0.95):F1} | {millis[^1]:F1} | {largest} |");
        }

        Publish(table.ToString());
    }

    [Fact]
    public async Task MeasureThePlanResult_ByEvidenceLines_AndByPlanSize()
    {
        var table = new StringBuilder();
        table.AppendLine("| Plan | Evidence lines | evidenceRefs bytes | Result bytes | Under 64 KiB |");
        table.AppendLine("|---|---|---|---|---|");
        foreach (var lines in new[] { 0, 20, 50, 100, 150, 200, 300, 500, 1000 })
        {
            var capability = new FiveToolCapability(DecideLaneSeed.Build(), lines);
            await using var host = await DecisionToolsTestHost.StartAsync(capability, new FiveToolLookup());
            var result = await host.CallAsync(
                GetDecisionPlanTool.ToolName, new { signalId = capability.TargetSignalId }, DecisionToolsTestHost.MintToken(Guid.NewGuid()));
            var size = McpResult.Bytes(result);
            table.AppendLine($"| every catalog action as action and next step ({DecisionActionCatalog.All.Count} each) | {lines} | {capability.EvidenceRefsJson?.Length ?? 0} | {size} | {(size < McpResult.MaxBytes ? "yes" : "NO")} |");
        }

        Publish(table.ToString());
    }

    [Fact]
    public async Task MeasureTheWorklistReads_PerTool()
    {
        var capability = new FiveToolCapability(DecideLaneSeed.Build());
        await using var host = await DecisionToolsTestHost.StartAsync(capability, new FiveToolLookup());
        var token = DecisionToolsTestHost.MintToken(Guid.NewGuid());
        var table = new StringBuilder();
        table.AppendLine("| Call | worklist reads (lane + page) |");
        table.AppendLine("|---|---|");
        foreach (var (tool, arguments) in new (string, object)[]
        {
            (ShowDecisionsTool.Name, new { category = "all" }),
            (ListDecisionPageTool.Name, new { }),
            (GetDecisionPlanTool.ToolName, new { signalId = capability.TargetSignalId }),
        })
        {
            var before = capability.WorklistReads;
            await host.CallAsync(tool, arguments, token);
            table.AppendLine($"| `{tool}` | {capability.WorklistReads - before} |");
        }

        table.AppendLine($"| open the widget and one item (all three) | {capability.WorklistReads} |");
        Publish(table.ToString());
    }

    private static double Percentile(List<double> sorted, double p) => sorted[(int)Math.Ceiling(p * sorted.Count) - 1];

    private void Publish(string markdown)
    {
        output.WriteLine(markdown);
        if (Environment.GetEnvironmentVariable("MCP_NFR_MEASUREMENTS_FILE") is { Length: > 0 } path)
        {
            File.AppendAllText(path, markdown + Environment.NewLine);
        }
    }
}
