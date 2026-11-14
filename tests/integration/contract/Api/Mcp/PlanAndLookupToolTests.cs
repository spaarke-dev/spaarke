// CONTRACT — spaarke_get_decision_plan and spaarke_search_decision_lookup over /mcp/legal-operations (spaarkeai-mcp-bff-api-r1
// task 023, FR-06, FR-07, FR-09, FR-10, NFR-02). KEEP path: endpoint-contract.
//
// WHAT RUNS: the PRODUCTION AddMcpModule / MapMcpEndpoints on a real Kestrel listener behind a real JwtBearer scheme, serving the two
// production tool classes, next to the REAL decision REST routes (MapDecisionPlanEndpoints, MapSignalReadEndpoints) in the same host, so
// "the tool equals the REST route for the same caller" is compared on bytes. Behind both: the real DecisionCapability (access check, plan
// resolution, plan-read stamp) and the real DecisionLookupSearch, over the module-boundary fakes of DecisionCapabilityFixtures and
// DecisionLookupWorld (the caller's Dataverse client, the BFF's own entity reader, the tenant cache).
// SCRIPTED (and why): the worklist read and the suppression count. SignalReadService and SignalSuppressionService each need a full
// Dataverse world of their own and are not what this task decides; the capability's two members are forwarded to a script instead.
// NOT HERE: A10 token refusals and per-tool policies (task 021, tests/integration/auth/Mcp), the commit path (tasks 024, 010).

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Domain.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.DecisionCapabilityFixtures;

namespace Sprk.Bff.Api.Tests.Api.Mcp;

public sealed class PlanAndLookupToolTests
{
    private static readonly Guid Caller = Guid.Parse("66666666-7777-4888-9999-aaaaaaaaaaaa");
    private const string BudgetPlan = """{"actions":["revise-budget","assign-task"]}""";

    // ---- tools/list ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ToolsList_ShowsBothToolsAppOnly_ReadOnly_WithATitleAndAUseForNotForDescriptionUnderTheLimit()
    {
        await using var host = await PlanAndLookupHost.StartAsync();

        var list = await host.RpcAsync("tools/list", new { });

        var tools = list.GetProperty("tools").EnumerateArray().ToList();
        tools.Select(t => t.GetProperty("name").GetString()).Should().BeEquivalentTo(
            ["spaarke_get_decision_plan", "spaarke_search_decision_lookup"], "the served set of this host is the two production tools");
        foreach (var tool in tools)
        {
            var name = tool.GetProperty("name").GetString();
            tool.GetProperty("_meta").GetProperty("ui").GetProperty("visibility").EnumerateArray().Select(v => v.GetString())
                .Should().Equal(["app"], $"{name} is app-only: the host keeps it out of the model's tool set");
            tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean().Should().BeTrue($"{name} is readOnly");
            tool.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
            var description = tool.GetProperty("description").GetString()!;
            description.Length.Should().BeLessThanOrEqualTo(1024, $"{name}'s description");
            description.Should().Contain("Use for:").And.Contain("Not for:");
        }

        PropertyNames(tools.Single(t => t.GetProperty("name").GetString() == "spaarke_get_decision_plan")).Should().Equal("signalId");
        PropertyNames(tools.Single(t => t.GetProperty("name").GetString() == "spaarke_search_decision_lookup"))
            .Should().BeEquivalentTo("signalId", "parameterCode", "query");
    }

    // ---- spaarke_get_decision_plan ---------------------------------------------------------------------------------

    [Fact]
    public async Task Plan_ForASignalTheCallerCanRead_EqualsTheRestPlanSuppressionAndRow_AndWritesTheStamp()
    {
        await using var host = await PlanAndLookupHost.StartAsync();
        var restPlan = await host.RestAsync($"/api/v1/signals/{SignalId:D}/decision-plan");
        var restSuppression = await host.RestAsync($"/api/v1/signals/{SignalId:D}/suppression");
        var restWorklist = await host.RestAsync("/api/v1/signals/worklist");
        host.Stamps.Writes.Clear(); // the REST plan read stamped too; this test is about the tool's own read

        var result = await host.CallAsync("spaarke_get_decision_plan", new { signalId = SignalId });

        IsError(result).Should().BeFalse(result.GetRawText());
        var body = Node(result.GetProperty("structuredContent"));
        JsonNode.DeepEquals(body["plan"], restPlan).Should().BeTrue("the plan is the REST decision-plan body, member for member");
        JsonNode.DeepEquals(body["suppression"], restSuppression).Should().BeTrue("the suppression is the REST suppression body");
        var restRow = restWorklist!["lanes"]!.AsArray()
            .SelectMany(l => l!["groups"]!.AsArray()).SelectMany(g => g!["items"]!.AsArray())
            .Single(i => i!["signalId"]!.GetValue<Guid>() == SignalId);
        JsonNode.DeepEquals(body["row"], restRow).Should().BeTrue("the full row is the worklist route's row for the same Signal and caller");
        body.AsObject().ContainsKey("suppressionError").Should().BeFalse();
        body.AsObject().ContainsKey("rowError").Should().BeFalse();
        result.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("Decision plan loaded: 1 action(s), 0 next step(s).");

        host.Stamps.Writes.Should().ContainSingle().Which.Should().Be(new StampWrite(
            McpTestHost.TenantId, "decision-plan-read", $"{Caller:D}:{SignalId:D}", 1, PlanAndLookupHost.Now, TimeSpan.FromHours(4)));
        (await host.Stamps.Stamp.ReadAsync(McpTestHost.TenantId, Caller.ToString("D"), SignalId, CancellationToken.None))
            .Should().Be(PlanAndLookupHost.Now, "a later commit reads this stamp as the start of the review");
    }

    [Fact]
    public async Task Plan_ForASignalOutsideTheCallersCoreRecordAccess_IsAnIsErrorWithTheRestReasonCode_AndWritesNoStamp()
    {
        await using var host = await PlanAndLookupHost.StartAsync(matterReadable: false);
        var (restStatus, restProblem) = await host.RestProblemAsync($"/api/v1/signals/{SignalId:D}/decision-plan");
        host.Stamps.Writes.Clear();

        var result = await host.CallAsync("spaarke_get_decision_plan", new { signalId = SignalId });

        restStatus.Should().Be(HttpStatusCode.NotFound);
        IsError(result).Should().BeTrue();
        var reasonCode = result.GetProperty("structuredContent").GetProperty("reasonCode").GetString();
        reasonCode.Should().Be(restProblem["reasonCode"]!.GetValue<string>(), "one reasonCode vocabulary for both hosts (FR-09)");
        reasonCode.Should().Be(ProblemDetailsHelper.RecordUnavailableReasonCode);
        result.GetProperty("structuredContent").TryGetProperty("plan", out _).Should().BeFalse("a refusal carries no plan");
        host.Stamps.Writes.Should().BeEmpty("a plan that was not returned starts no review");
        host.Script.SuppressionCalls.Should().Be(0, "nothing else is read for a Signal the caller may not read");
        host.Script.ListCalls.Should().Be(0);
    }

    [Fact]
    public async Task Plan_WhenTheSuppressionOrTheRowCannotBeRead_StillReturnsThePlanAndItsStamp_AndNamesWhatFailed()
    {
        await using var host = await PlanAndLookupHost.StartAsync();
        host.Script.Suppression = SuppressionReadResult.Unavailable;
        host.Script.LaneFailure = SignalReadResult.Unavailable("too_many_signals");

        var result = await host.CallAsync("spaarke_get_decision_plan", new { signalId = SignalId });

        IsError(result).Should().BeFalse("the wizard opens without the dismissal count and the row; the plan is what a commit needs");
        var body = Node(result.GetProperty("structuredContent"));
        body["plan"]!["signalId"]!.GetValue<Guid>().Should().Be(SignalId);
        body["suppression"].Should().BeNull();
        body["row"].Should().BeNull();
        body["suppressionError"]!["reasonCode"]!.GetValue<string>().Should().Be("ontology.suppression.unavailable");
        body["rowError"]!["reasonCode"]!.GetValue<string>().Should().Be("ontology.signal.worklist_unavailable");
        host.Stamps.Writes.Should().ContainSingle("the plan was returned, so the stamp stands");
    }

    [Fact]
    public async Task Plan_ForAnItemNoLongerInTheCallersLanes_ReturnsANullRow_WithoutAnError()
    {
        await using var host = await PlanAndLookupHost.StartAsync();
        host.Script.Rows.Clear();

        var result = await host.CallAsync("spaarke_get_decision_plan", new { signalId = SignalId });

        IsError(result).Should().BeFalse();
        var body = Node(result.GetProperty("structuredContent"));
        body["row"].Should().BeNull();
        body.AsObject().ContainsKey("rowError").Should().BeFalse("an item that left the lane is not a failure");
        body["plan"].Should().NotBeNull();
    }

    // ---- spaarke_search_decision_lookup ----------------------------------------------------------------------------

    [Theory]
    [InlineData("budget", "Out")]
    [InlineData("budget", "")]
    [InlineData("assignee", "Ac")]
    public async Task Lookup_ReturnsTheRestRoutesResults_ForTheSameCallerAndInputs(string parameterCode, string query)
    {
        await using var host = await PlanAndLookupHost.StartAsync(BudgetPlan);
        var rest = await host.RestAsync($"/api/v1/signals/{SignalId:D}/lookup?parameterCode={parameterCode}&query={query}");

        var result = await host.CallAsync("spaarke_search_decision_lookup", new { signalId = SignalId, parameterCode, query });

        IsError(result).Should().BeFalse(result.GetRawText());
        JsonNode.DeepEquals(Node(result.GetProperty("structuredContent")), rest).Should().BeTrue("the tool's body is the REST body unchanged");
        result.GetProperty("content")[0].GetProperty("text").GetString().Should().Be($"{rest["candidates"]!.AsArray().Count} candidate(s) found.");
    }

    [Fact]
    public async Task Lookup_NeverReturnsABudgetOfAnotherMatter_OrARecordTheCallerCannotRead()
    {
        await using var host = await PlanAndLookupHost.StartAsync(BudgetPlan);

        var budgets = await host.CallAsync("spaarke_search_decision_lookup", new { signalId = SignalId, parameterCode = "budget", query = "" });
        var typedExactly = await host.CallAsync(
            "spaarke_search_decision_lookup", new { signalId = SignalId, parameterCode = "budget", query = "Other matter budget" });
        var people = await host.CallAsync("spaarke_search_decision_lookup", new { signalId = SignalId, parameterCode = "assignee", query = "Ac" });

        Candidates(budgets).Should().ContainSingle().Which.Should().Be(("Outside counsel 2026", DecisionLookupWorld.MatterBudget));
        Candidates(budgets).Select(c => c.Id).Should().NotContain(DecisionLookupWorld.OtherMatterBudget, "D-18: only the Signal's own matter's budgets");
        Candidates(typedExactly).Should().BeEmpty("the other matter's budget stays out even when the typed text matches it exactly");
        Candidates(people).Select(c => c.Name).Should().Equal("Ada Acme");
        Candidates(people).Select(c => c.Id).Should().NotContain(DecisionLookupWorld.UnreadableContact, "the caller's own read never returned it");
    }

    [Theory]
    [InlineData("contactid")] // a parameter the plan does not offer
    [InlineData("amount")]    // in the plan, but not a person or lookup parameter
    [InlineData(null)]        // no parameter at all: the REST route's refusal, never a transport error
    public async Task Lookup_ForAParameterTheDecisionPlanDoesNotOffer_IsAnIsErrorWithTheInvalidParameterReasonCode_AndSearchesNothing(string? parameterCode)
    {
        await using var host = await PlanAndLookupHost.StartAsync(BudgetPlan);

        var result = await host.CallAsync(
            "spaarke_search_decision_lookup",
            parameterCode is null ? new Dictionary<string, object> { ["signalId"] = SignalId, ["query"] = "Ac" }
                : new Dictionary<string, object> { ["signalId"] = SignalId, ["parameterCode"] = parameterCode, ["query"] = "Ac" });

        IsError(result).Should().BeTrue();
        var error = result.GetProperty("structuredContent");
        error.GetProperty("reasonCode").GetString().Should().Be("ontology.decision.invalid_parameter");
        error.TryGetProperty("candidates", out _).Should().BeFalse();

        // The message is the lookup's own (the REST lookup route's detail for the same inputs), not the commit route's text for the same code.
        var restPath = $"/api/v1/signals/{SignalId:D}/lookup?" + (parameterCode is null ? string.Empty : $"parameterCode={parameterCode}&") + "query=Ac";
        var (restStatus, restProblem) = await host.RestProblemAsync(restPath);
        restStatus.Should().Be(HttpStatusCode.UnprocessableEntity);
        error.GetProperty("message").GetString().Should().Be(restProblem["detail"]!.GetValue<string>());
        error.GetProperty("message").GetString().Should().Contain("This parameter cannot be searched for this item").And.Contain("at most 100 characters of search text");
        error.GetProperty("status").GetInt32().Should().Be((int)restStatus);
        host.World.Paths.Should().BeEmpty("nothing is searched for a parameter the plan does not offer");
    }

    [Fact]
    public async Task Lookup_ForASignalOutsideTheCallersCoreRecordAccess_IsAnIsErrorWithTheRestReasonCode()
    {
        await using var host = await PlanAndLookupHost.StartAsync(BudgetPlan, matterReadable: false);
        var (_, restProblem) = await host.RestProblemAsync($"/api/v1/signals/{SignalId:D}/lookup?parameterCode=budget&query=Out");

        var result = await host.CallAsync("spaarke_search_decision_lookup", new { signalId = SignalId, parameterCode = "budget", query = "Out" });

        IsError(result).Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be(restProblem["reasonCode"]!.GetValue<string>());
        host.World.Paths.Should().BeEmpty();
    }

    // ---- one caller (FR-10) ----------------------------------------------------------------------------------------

    [Fact]
    public async Task EveryCapabilityAndSearchCall_ReceivesExactlyTheAccessorsHttpContext()
    {
        await using var host = await PlanAndLookupHost.StartAsync(BudgetPlan);

        await host.CallAsync("spaarke_get_decision_plan", new { signalId = SignalId });
        await host.CallAsync("spaarke_search_decision_lookup", new { signalId = SignalId, parameterCode = "budget", query = "Out" });

        host.Script.SameContext.Should().BeEquivalentTo(
            [new ContextUse("GetPlan", true), new ContextUse("GetSuppression", true), new ContextUse("ListDecide", true), new ContextUse("Lookup", true)],
            options => options.WithStrictOrdering(),
            "the tools pass IHttpContextAccessor.HttpContext itself (its User for the list read), never a context they built or cached");
    }

    [Fact]
    public async Task WithoutACaller_BothToolsAreAnIsErrorUserContextRequired_AndCallNothing()
    {
        var script = new PlanAndLookupAccessors();
        var capability = new Mock<IDecisionCapability>(MockBehavior.Strict);
        var search = new Mock<IDecisionLookupSearch>(MockBehavior.Strict);
        var anonymous = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var signedIn = SignedIn();

        foreach (var accessor in new[] { script.Accessor(null), script.Accessor(anonymous) })
        {
            var plan = await GetDecisionPlanTool.RunAsync(SignalId, accessor, capability.Object, CancellationToken.None);
            var lookup = await SearchDecisionLookupTool.RunAsync(SignalId, accessor, search.Object, CancellationToken.None, "budget", "Out");

            foreach (var result in new[] { plan, lookup })
            {
                result.IsError.Should().BeTrue();
                result.StructuredContent!.Value.GetProperty("reasonCode").GetString().Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
            }
        }

        capability.VerifyNoOtherCalls();
        search.VerifyNoOtherCalls();
        signedIn.User.Identities.Should().ContainSingle(i => i.IsAuthenticated, "the control: a signed-in context is not refused by the same guard");
        PlanAndLookupToolSupport.HasCaller(signedIn).Should().BeTrue();
    }

    // ---- error body (status + support reference) ---------------------------------------------------------------------

    [Fact]
    public async Task Plan_ARefusal_CarriesTheRestStatusAndTheRequestsCorrelationId_BesideTheReasonCodeAndMessage()
    {
        await using var host = await PlanAndLookupHost.StartAsync(matterReadable: false);
        var (restStatus, restProblem) = await host.RestProblemAsync($"/api/v1/signals/{SignalId:D}/decision-plan");

        var result = await host.CallAsync("spaarke_get_decision_plan", new { signalId = SignalId });

        IsError(result).Should().BeTrue();
        var error = result.GetProperty("structuredContent");
        error.EnumerateObject().Select(p => p.Name).Should().Equal("reasonCode", "message", "status", "correlationId");
        error.GetProperty("status").GetInt32().Should().Be((int)restStatus, "the status the REST route answers for the same failure");
        error.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
        restProblem["correlationId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace("the REST reference this one mirrors is the request's TraceIdentifier");
    }

    // ---- telemetry (NFR-05) ------------------------------------------------------------------------------------------

    [Fact]
    public async Task PlanAndLookup_EachCallLogsOneLine_WithToolOutcomeRowsLatencyAndCorrelation_AndNoArgumentOrRecordText()
    {
        await using var host = await PlanAndLookupHost.StartAsync(BudgetPlan);

        await host.CallAsync("spaarke_get_decision_plan", new { signalId = SignalId });
        await host.CallAsync("spaarke_search_decision_lookup", new { signalId = SignalId, parameterCode = "budget", query = "Out" });
        await host.CallAsync("spaarke_search_decision_lookup", new { signalId = SignalId, parameterCode = "contactid", query = "SecretQueryText" });

        var lines = host.Logs.Entries
            .Where(e => e.Category.EndsWith("." + nameof(GetDecisionPlanTool), StringComparison.Ordinal)
                        || e.Category.EndsWith("." + nameof(SearchDecisionLookupTool), StringComparison.Ordinal))
            .ToList();
        lines.Select(e => ((string)e.State["ToolName"]!, (string)e.State["Outcome"]!, (int)e.State["RowCount"]!)).Should().Equal(
            ("spaarke_get_decision_plan", "ok", 1),
            ("spaarke_search_decision_lookup", "ok", 1),
            ("spaarke_search_decision_lookup", "ontology.decision.invalid_parameter", 0));
        lines.Should().AllSatisfy(e =>
        {
            ((long)e.State["ElapsedMs"]!).Should().BeGreaterThanOrEqualTo(0);
            ((string?)e.State["CorrelationId"]).Should().NotBeNullOrWhiteSpace();
        });

        // No argument value, query text, record text or id in anything these tools logged.
        var logged = string.Join("\n", lines.Select(e => e.Message + " " + string.Join(" ", e.State.Values)));
        logged.Should().NotContain("SecretQueryText").And.NotContain("budget").And.NotContain(SignalId.ToString()).And.NotContain("Acme v. Roadrunner");
    }

    // ---- result size (NFR-02) --------------------------------------------------------------------------------------

    [Fact]
    public async Task EveryResult_IsUnder64KiB_At200SeededWorkItems_AndAFullLookupPage()
    {
        await using var host = await PlanAndLookupHost.StartAsync(BudgetPlan);
        host.Script.SeedItems(200);

        var plan = await host.CallAsync("spaarke_get_decision_plan", new { signalId = SignalId });
        var lookup = await host.CallAsync("spaarke_search_decision_lookup", new { signalId = SignalId, parameterCode = "budget", query = "" });

        IsError(plan).Should().BeFalse();
        Node(plan.GetProperty("structuredContent"))["row"].Should().NotBeNull("the row of the opened item is returned, and only that row");
        Encoding.UTF8.GetByteCount(plan.GetRawText()).Should().BeLessThan(64 * 1024);
        Encoding.UTF8.GetByteCount(lookup.GetRawText()).Should().BeLessThan(64 * 1024);
    }

    [Fact]
    public async Task ALookupWithMoreThanTheCapOfMatches_ReturnsTheCappedPage_UnderTheLimit()
    {
        await using var host = await PlanAndLookupHost.StartAsync("""{"actions":["send-budget-inquiry"]}""");

        var result = await host.CallAsync("spaarke_search_decision_lookup", new { signalId = SignalId, parameterCode = "outsideFirm", query = "Firm" });

        IsError(result).Should().BeFalse(result.GetRawText());
        Candidates(result).Should().HaveCount(DecisionLookupSearch.MaxResults, "the world holds 30 matching firms; the search returns at most 25");
        Encoding.UTF8.GetByteCount(result.GetRawText()).Should().BeLessThan(64 * 1024);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    private static bool IsError(JsonElement result) => result.TryGetProperty("isError", out var e) && e.GetBoolean();

    private static JsonNode Node(JsonElement element) => JsonNode.Parse(element.GetRawText())!;

    private static List<string> PropertyNames(JsonElement tool) =>
        tool.GetProperty("inputSchema").GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();

    private static List<(string Name, Guid Id)> Candidates(JsonElement result) =>
        result.GetProperty("structuredContent").GetProperty("candidates").EnumerateArray()
            .Select(c => (c.GetProperty("name").GetString()!, c.GetProperty("id").GetGuid())).ToList();

    internal sealed record StampWrite(string Tenant, string Resource, string Key, int Version, DateTimeOffset ReadOn, TimeSpan? Ttl);

    internal sealed record ContextUse(string Call, bool IsTheAccessorsContext);
}

/// <summary>What the scripted worklist and suppression reads answer, and what the capability and search were handed.</summary>
internal sealed class PlanAndLookupScript
{
    public List<WorklistItemRow> Rows { get; } = [PlanAndLookupHost.Row(DecisionCapabilityFixtures.SignalId)];

    public SignalReadResult? LaneFailure { get; set; }

    public SuppressionReadResult Suppression { get; set; } = SuppressionReadResult.Ok(
        new SuppressionResponse(DecisionCapabilityFixtures.SignalId, 1, 2, 3, 30, null, true));

    public int ListCalls { get; set; }

    public int SuppressionCalls { get; set; }

    public List<PlanAndLookupToolTests.ContextUse> SameContext { get; } = [];

    /// <summary>The target item (first) plus <paramref name="count"/> - 1 others, each with the longest sentence and evidence the row carries.</summary>
    public void SeedItems(int count)
    {
        Rows.Clear();
        Rows.Add(PlanAndLookupHost.Row(DecisionCapabilityFixtures.SignalId));
        for (var i = 1; i < count; i++)
        {
            Rows.Add(PlanAndLookupHost.Row(Guid.Parse($"02300000-0000-4000-8000-{i:D12}")));
        }
    }

    public SignalReadResult Lane() =>
        LaneFailure ?? new SignalReadResult(
            SignalReadOutcome.Ok,
            new WorklistResponse("lanes",
            [
                new WorklistLaneGroups("Decide",
                [
                    new WorklistGroup(
                        new WorklistCoreRecord("sprk_matter", DecisionCapabilityFixtures.MatterId, "Matter", "Acme v. Roadrunner", "M-100"), false, Rows.ToList()),
                ]),
                new WorklistLaneGroups("Do", []),
            ]));
}

/// <summary>The real capability for the plan (access, resolution, stamp); the worklist and suppression reads are the script's.</summary>
internal sealed class PlanAndLookupCapability(DecisionCapability real, IHttpContextAccessor accessor, PlanAndLookupScript script) : IDecisionCapability
{
    public Task<DecisionPlanReadResult> GetPlanAsync(HttpContext? http, Guid signalId, CancellationToken ct)
    {
        script.SameContext.Add(new("GetPlan", ReferenceEquals(http, accessor.HttpContext)));
        return real.GetPlanAsync(http, signalId, ct);
    }

    public Task<SuppressionReadResult> GetSuppressionAsync(HttpContext? http, Guid signalId, CancellationToken ct)
    {
        script.SuppressionCalls++;
        script.SameContext.Add(new("GetSuppression", ReferenceEquals(http, accessor.HttpContext)));
        return Task.FromResult(script.Suppression);
    }

    public Task<SignalReadResult> ListDecideAsync(ClaimsPrincipal? user, CancellationToken ct, WorklistSet set = WorklistSet.Lanes)
    {
        script.ListCalls++;
        script.SameContext.Add(new("ListDecide", ReferenceEquals(user, accessor.HttpContext?.User)));
        return Task.FromResult(script.Lane());
    }

    public Task<DecidePageResult> ListDecidePageAsync(ClaimsPrincipal? user, string? cursor, CancellationToken ct) => throw new NotSupportedException();

    public Task<DecisionCommitResult> CommitAsync(HttpContext? http, Guid signalId, DecisionReview review, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>The real lookup search, recording the context it was handed.</summary>
internal sealed class PlanAndLookupSearch(DecisionLookupSearch real, IHttpContextAccessor accessor, PlanAndLookupScript script) : IDecisionLookupSearch
{
    public Task<DecisionLookupResult> SearchAsync(HttpContext? http, Guid signalId, string? parameterCode, string? query, CancellationToken ct)
    {
        script.SameContext.Add(new("Lookup", ReferenceEquals(http, accessor.HttpContext)));
        return real.SearchAsync(http, signalId, parameterCode, query, ct);
    }
}

/// <summary>An <see cref="IHttpContextAccessor"/> holding a chosen context, for calling a tool's handler directly.</summary>
internal sealed class PlanAndLookupAccessors
{
    public IHttpContextAccessor Accessor(HttpContext? context) => new Holder { HttpContext = context };

    private sealed class Holder : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}

/// <summary>
/// The in-process host of task 023's tests: the production MCP module serving only the two tools under test, and the real decision REST
/// routes, over one real capability and search. Authentication is a real JwtBearer scheme that accepts the tokens
/// <see cref="McpTestHost.MintToken"/> signs, so the REST routes and the MCP route see the same caller.
/// </summary>
internal sealed class PlanAndLookupHost : IAsyncDisposable
{
    internal static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 30, 0, TimeSpan.Zero);
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("mcp-contract-test-signing-key-0123456789-0123456789"));

    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _token;

    private PlanAndLookupHost(
        WebApplication app, HttpClient client, StampStore stamps, PlanAndLookupScript script, DecisionLookupWorld world,
        DecisionToolsTestHost.CapturingLoggerProvider logs)
    {
        _app = app;
        _client = client;
        Stamps = stamps;
        Script = script;
        World = world;
        Logs = logs;
        _token = McpTestHost.MintToken(Guid.Parse("66666666-7777-4888-9999-aaaaaaaaaaaa"));
    }

    public StampStore Stamps { get; }

    public PlanAndLookupScript Script { get; }

    public DecisionLookupWorld World { get; }

    public DecisionToolsTestHost.CapturingLoggerProvider Logs { get; }

    public static async Task<PlanAndLookupHost> StartAsync(string plan = DecisionCapabilityFixtures.ApproveVariancePlan, bool matterReadable = true)
    {
        var (access, plans, users, _) = DecideSignalOnAMatter(matterReadable, plan);
        var world = new DecisionLookupWorld(users);
        var stamps = new StampStore();
        var script = new PlanAndLookupScript();
        var real = Capability(access, plans, Describer(), planReadStamp: stamps.Stamp);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var logs = new DecisionToolsTestHost.CapturingLoggerProvider();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Enabled"] = "true",
            ["AzureAd:TenantId"] = McpTestHost.TenantId,
            ["AzureAd:ClientId"] = McpTestHost.BffClientId,
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = $"https://login.microsoftonline.com/{McpTestHost.TenantId}/v2.0",
                ValidAudience = McpTestHost.BffClientId,
                IssuerSigningKey = SigningKey,
            });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimitingModule();
        builder.Services.AddMcpModule(builder.Configuration, [typeof(GetDecisionPlanTool), typeof(SearchDecisionLookupTool)]);

        // The decision slice at its module boundaries (the same fakes the lookup route and capability tests use).
        builder.Services.AddSingleton(script);
        builder.Services.AddSingleton(real);
        builder.Services.AddSingleton(access);
        builder.Services.AddSingleton(plans);
        builder.Services.AddSingleton(users.Object);
        builder.Services.AddScoped<IDecisionCapability>(sp =>
            new PlanAndLookupCapability(sp.GetRequiredService<DecisionCapability>(), sp.GetRequiredService<IHttpContextAccessor>(), script));
        builder.Services.AddSingleton(new DecisionLookupSearch(access, plans, users.Object, NullLogger<DecisionLookupSearch>.Instance));
        builder.Services.AddScoped<IDecisionLookupSearch>(sp =>
            new PlanAndLookupSearch(sp.GetRequiredService<DecisionLookupSearch>(), sp.GetRequiredService<IHttpContextAccessor>(), script));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapMcpEndpoints();
        app.MapDecisionPlanEndpoints();
        app.MapSignalReadEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new PlanAndLookupHost(app, new HttpClient { BaseAddress = new Uri(address) }, stamps, script, world, logs);
    }

    /// <summary>A Decide-lane row with the longest sentence and the heaviest evidence the row carries (NFR-02).</summary>
    public static WorklistItemRow Row(Guid signalId) =>
        new(signalId, "Decide", "Open", "Approve the variance on the Roadrunner invoice", "Acme v. Roadrunner", "Variance over threshold",
            "Approval", "Due in 2 days", Now.AddDays(-3), "2026-11-15",
            EvidenceRefs: "[" + string.Join(",", Enumerable.Range(0, 20).Select(i => $$"""{"kind":"note","id":"{{i}}","text":"{{new string('e', 120)}}"}""")) + "]",
            PolicyVersionId: DecisionCapabilityFixtures.VersionId,
            Sentence: new string('s', 2000));

    /// <summary>One JSON-RPC call; returns the <c>result</c> (the transport answers JSON or an SSE stream).</summary>
    public async Task<JsonElement> RpcAsync(string method, object parameters)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);
        var json = body.TrimStart().StartsWith('{')
            ? body
            : string.Join("\n", body.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
        using var doc = JsonDocument.Parse(json.Split('\n').First(l => l.Contains("\"result\"") || l.Contains("\"error\"")));
        doc.RootElement.TryGetProperty("error", out var error).Should().BeFalse($"a JSON-RPC error is a transport failure, not a domain result: {error}");
        return doc.RootElement.GetProperty("result").Clone();
    }

    public Task<JsonElement> CallAsync(string tool, object arguments) => RpcAsync("tools/call", new { name = tool, arguments });

    /// <summary>A successful REST GET as JSON.</summary>
    public async Task<JsonNode> RestAsync(string path)
    {
        using var response = await GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!;
    }

    /// <summary>A refused REST GET: its status and ProblemDetails body.</summary>
    public async Task<(HttpStatusCode Status, JsonNode Problem)> RestProblemAsync(string path)
    {
        using var response = await GetAsync(path);
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
    }

    private async Task<HttpResponseMessage> GetAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return await _client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>The tenant cache (Redis) at its boundary: records each plan-read stamp write and serves it back to the commit route's reader.</summary>
    public sealed class StampStore
    {
        private readonly Dictionary<string, DecisionPlanReadStamp.Stamp> _store = new(StringComparer.Ordinal);

        public StampStore()
        {
            var cache = new Mock<ITenantCache>();
            cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                    It.IsAny<DecisionPlanReadStamp.Stamp>(), It.IsAny<TimeSpan?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string tenant, string resource, string key, int version, DecisionPlanReadStamp.Stamp stamp, TimeSpan? ttl, string _, CancellationToken _) =>
                {
                    Writes.Add(new PlanAndLookupToolTests.StampWrite(tenant, resource, key, version, stamp.ReadOn, ttl));
                    _store[$"{tenant}|{resource}|{key}|{version}"] = stamp;
                })
                .Returns(Task.CompletedTask);
            cache.Setup(c => c.GetAsync<DecisionPlanReadStamp.Stamp>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string tenant, string resource, string key, int version, string _, CancellationToken _) =>
                    _store.TryGetValue($"{tenant}|{resource}|{key}|{version}", out var stamp) ? stamp : null);
            Stamp = new DecisionPlanReadStamp(cache.Object, new FakeTimeProvider(Now), NullLogger<DecisionPlanReadStamp>.Instance);
        }

        public DecisionPlanReadStamp Stamp { get; }

        public List<PlanAndLookupToolTests.StampWrite> Writes { get; } = [];
    }
}
