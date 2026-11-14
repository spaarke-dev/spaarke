using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.Api.Mcp;
using Sprk.Bff.Api.Tests.Auth.Mcp;
using Sprk.Bff.Api.Tests.Domain.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 010 (spec FR-10 as amended 2026-11-07, option C), tests 3 and 4: the decision commit over the REAL MCP
/// route. The production <c>AddMcpModule</c> / <c>MapMcpEndpoints</c> host (<see cref="McpCallerPathHost"/>) serves the production
/// <c>spaarke_get_decision_plan</c> and <c>spaarke_complete_decision</c> over the REAL <see cref="DecisionCapability"/> plan and commit, the
/// REAL <see cref="DecisionCommitService"/> and <see cref="DecisionPlanReadStamp"/> of this class's rig, and T170's shipped
/// <see cref="CallerRecordAccessProbe"/> with only its wire steps answered and counted. Nothing sets the HttpContext by hand: the host does,
/// so <see cref="Sprk.Bff.Api.Infrastructure.Dataverse.CallerWhoAmIMemo"/> and the plan-read stamp see exactly what an MCP request gives
/// them. (The worklist and suppression reads the plan tool also makes are answered empty: they are not on the stamp or commit path.)
/// </summary>
public sealed partial class DecisionCommitServiceTests
{
    private static readonly JsonSerializerOptions McpArguments = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Mcp010_Test3_OneCompleteDecisionOverMcp_AsksWhoAmIOnce_AndAsksEveryRightsQuestion()
    {
        var rig = new Rig();
        var probe = new WireCountingProbe(Caller);
        rig.ProbeOverride = probe;
        await using var host = await McpRigHost(rig);

        var result = await CompleteOverMcp(host, PathBReview());

        IsError(result).Should().BeFalse(result.GetRawText());
        result.GetProperty("structuredContent").GetProperty("status").GetInt32().Should().Be((int)DecisionCommitStatus.Recorded);
        rig.Records.Should().ContainSingle();
        probe.CallerQuestions.Should().BeGreaterThan(1, "the commit asks the probe about the caller more than once");
        probe.WhoAmIs.Should().Be(1, "who the caller is is asked once per MCP request: the memo finds the request through IHttpContextAccessor");
        probe.RightsAsked.Should().BeGreaterThan(0).And.Be(probe.RightsQuestions, "every rights question still reaches Dataverse (FR-51)");
        probe.PrincipalsAskedAbout.Should().OnlyContain(p => p == Caller);
    }

    [Fact]
    public async Task Mcp010_Test4_APlanReadOverMcp_SatisfiesALaterCompleteDecisionOverMcp_ForTheSameCallerAndSignal()
    {
        var rig = new Rig { ActiveBudgets = 1 };
        rig.ProbeOverride = new WireCountingProbe(Caller);
        await using var host = await McpRigHost(rig);

        var plan = await host.CallToolAsync(GetDecisionPlanTool.ToolName, new { signalId = Primary }, CallerToken);
        IsError(plan).Should().BeFalse(plan.GetRawText());
        SystemClosesTheSignalAfterThePlanRead(rig);

        var result = await CompleteOverMcp(host, PathBReview()); // a separate MCP request: a new HttpContext, the same caller

        IsError(result).Should().BeFalse("the plan read over MCP stamped this caller's review start: " + result.GetRawText());
        var structured = result.GetProperty("structuredContent");
        structured.GetProperty("status").GetInt32().Should().Be((int)DecisionCommitStatus.Recorded);
        structured.GetProperty("signals").EnumerateArray().Single().GetProperty("warning").GetString()
            .Should().Be("reclosed_after_system_resolution", "the review started before the system closed the Signal");
        rig.Records.Should().ContainSingle();
    }

    [Fact]
    public async Task Mcp010_Test4_Negative_WithoutThePlanRead_TheCompleteDecisionOverMcp_IsThePlanReadRefusal_AndWritesNothing()
    {
        var rig = new Rig { ActiveBudgets = 1 };
        rig.ProbeOverride = new WireCountingProbe(Caller);
        await using var host = await McpRigHost(rig);
        SystemClosesTheSignalAfterThePlanRead(rig); // the same timeline, with no plan read

        var result = await CompleteOverMcp(host, PathBReview());

        IsError(result).Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be(DecisionCommitReasons.SignalNotOpen,
            "with no stamp the review starts now, so the system's earlier closure is the existing refusal");
        rig.Writes.Should().BeEmpty();
        rig.Records.Should().BeEmpty();
    }

    private static string CallerToken => McpTestHost.MintToken(CallerOid);

    private static bool IsError(JsonElement result) => result.TryGetProperty("isError", out var isError) && isError.GetBoolean();

    private static Task<JsonElement> CompleteOverMcp(McpCallerPathHost host, DecisionReview review) =>
        host.CallToolAsync(
            CompleteDecisionTool.ToolName,
            new { signalId = Primary, review = JsonSerializer.SerializeToElement(review, McpArguments) },
            CallerToken);

    /// <summary>One minute after the plan read the system closes the Signal; the commit comes four minutes later.</summary>
    private static void SystemClosesTheSignalAfterThePlanRead(Rig rig)
    {
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, Resolved, SignalWriter.ResolutionConditionCleared, rig.Clock.GetUtcNow().UtcDateTime.AddMinutes(1));
        rig.Clock.Advance(TimeSpan.FromMinutes(5));
    }

    /// <summary>The production MCP host serving the plan and commit tools over this rig's real capability.</summary>
    private static Task<McpCallerPathHost> McpRigHost(Rig rig)
    {
        var real = DecisionCapabilityFixtures.Capability(
            access: new SignalCoreRecordAccess(rig.User.Object, rig.Entities.Object, rig.Identity.Object, NullLogger<SignalCoreRecordAccess>.Instance),
            plans: new DecisionPlanService(rig.Entities.Object, NullLogger<DecisionPlanService>.Instance),
            describer: DecisionCapabilityFixtures.Describer(),
            caller: rig.User.Object,
            planReadStamp: rig.PlanReads,
            commits: rig.Service());
        return McpCallerPathHost.StartAsync(
            [typeof(GetDecisionPlanTool), typeof(CompleteDecisionTool)],
            services => services.AddSingleton<IDecisionCapability>(new PlanAndCommitCapability(real)));
    }

    /// <summary>The real capability's plan read and commit; the worklist and suppression reads (not on the stamp or commit path) answer empty.</summary>
    private sealed class PlanAndCommitCapability(DecisionCapability real) : IDecisionCapability
    {
        public Task<DecisionPlanReadResult> GetPlanAsync(HttpContext? http, Guid signalId, CancellationToken ct) => real.GetPlanAsync(http, signalId, ct);

        public Task<DecisionCommitResult> CommitAsync(HttpContext? http, Guid signalId, DecisionReview review, CancellationToken ct) =>
            real.CommitAsync(http, signalId, review, ct);

        public Task<SuppressionReadResult> GetSuppressionAsync(HttpContext? http, Guid signalId, CancellationToken ct) =>
            Task.FromResult(SuppressionReadResult.NotFound);

        public Task<SignalReadResult> ListDecideAsync(ClaimsPrincipal? user, CancellationToken ct, WorklistSet set = WorklistSet.Lanes) =>
            Task.FromResult(new SignalReadResult(SignalReadOutcome.Ok, new WorklistResponse("lanes", [])));

        public Task<DecidePageResult> ListDecidePageAsync(ClaimsPrincipal? user, string? cursor, CancellationToken ct) => throw new NotSupportedException();
    }
}
