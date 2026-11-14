using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.Domain.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 024 (spec FR-08, FR-15 server half, NFR-06; ontology condition 2): <c>spaarke_complete_decision</c>, the
/// one write tool, over the REAL <see cref="DecisionCapability"/> and <see cref="DecisionCommitService"/> in this class's rig. The tool is a
/// thin adapter, so every case here is the service's own outcome seen through the tool: one completion writes one Decision Record and
/// closes the Signal, a repeated review id returns the first outcome, the refusals write nothing, a billing Signal gets the mapped
/// <c>decided_in_invoice_review</c> refusal before the rights probe (the ONE MCP billing test: the tool has no billing check of its
/// own), and the commit result reaches <c>structuredContent</c> unchanged. The wire (tools/list, argument binding) is proven in
/// <c>CompleteDecisionToolWireTests</c>.
/// </summary>
public sealed partial class DecisionCommitServiceTests
{
    private static readonly JsonSerializerOptions RestWire = new(JsonSerializerDefaults.Web);

    /// <summary>The tool's handler, called as the SDK calls it: the accessor holds <paramref name="http"/> (null: no request context).</summary>
    private static Task<CallToolResult> CompleteTool(Rig rig, DecisionReview review, HttpContext? http, Guid? signal = null) =>
        CompleteDecisionTool.CompleteAsync(
            signal ?? Primary, review, new HttpContextAccessor { HttpContext = http }, CapabilityOver(rig), CancellationToken.None);

    private static string ErrorReason(CallToolResult result)
    {
        result.IsError.Should().BeTrue();
        return result.StructuredContent!.Value.GetProperty("reasonCode").GetString()!;
    }

    // ── listing ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CompleteTool_IsListedAppOnly_NotReadOnly_Idempotent_AndNotDestructive_WithAUseForNotForDescription()
    {
        var services = new ServiceCollection()
            .AddHttpContextAccessor()
            .AddLogging()
            .AddSingleton(TimeProvider.System) // the handler binds TimeProvider and ILogger<T> as services, as the production host provides them
            .AddSingleton<IDecisionCapability>(CapabilityOver(new Rig()))
            .AddAuthorization()
            .BuildServiceProvider();

        // The production catalog: the per-tool rule (class [Authorize], explicit ReadOnly and title) runs on it here as at startup.
        var catalog = new McpToolCatalog([typeof(CompleteDecisionTool)], services);

        var tool = catalog.ToolsFor(McpEndpoints.LegalOperationsArea).Should().ContainSingle().Subject.ProtocolTool;
        tool.Name.Should().Be("spaarke_complete_decision");
        tool.Title.Should().NotBeNullOrWhiteSpace();
        tool.Annotations!.ReadOnlyHint.Should().BeFalse();
        tool.Annotations.DestructiveHint.Should().BeFalse();
        tool.Annotations.IdempotentHint.Should().BeTrue();
        tool.Description!.Length.Should().BeLessThanOrEqualTo(1024);
        tool.Description.Should().Contain("Use for:").And.Contain("Not for:");
        tool.Meta!["ui"]!["visibility"]!.AsArray().Select(v => v!.GetValue<string>()).Should().Equal("app");
        tool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("signalId", "review");
    }

    // ── the write ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteTool_OneCompletion_WritesOneDecisionRecord_ClosesTheSignal_AndReturnsTheCommitResultUnchanged()
    {
        var rig = new Rig();
        var review = PathBReview();

        var result = await CompleteTool(rig, review, Http());

        result.IsError.Should().NotBe(true);
        rig.Records.Should().ContainSingle("one completion writes one Decision Record");
        rig.Status(Primary).Should().Be(Resolved, "the Signal is closed");
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("status").GetInt32().Should().Be((int)DecisionCommitStatus.Recorded);
        structured.GetProperty("decisionRecordId").GetGuid().Should().Be(RecordId);
        structured.GetProperty("reviewId").GetGuid().Should().Be(review.ReviewId);

        // "Unchanged": byte for byte the body the REST route returns for the same input over the same rig.
        var restRig = new Rig();
        var rest = await DecisionCommitEndpoints.CommitDecisionAsync(Primary, review, Http(), CapabilityOver(restRig), CancellationToken.None);
        var restBody = rest.Should().BeOfType<Ok<DecisionCommitResult>>().Subject.Value!;
        structured.GetRawText().Should().Be(JsonSerializer.Serialize(restBody, RestWire));
    }

    [Fact]
    public async Task CompleteTool_TheSameReviewIdTwice_ReturnsTheFirstOutcome_AndWritesNothingNew()
    {
        var rig = new Rig();
        var review = PathBReview();

        var first = await CompleteTool(rig, review, Http());
        var writes = rig.Writes.Count();
        var second = await CompleteTool(rig, review, Http());

        second.IsError.Should().NotBe(true);
        var replay = second.StructuredContent!.Value;
        replay.GetProperty("replayed").GetBoolean().Should().BeTrue();
        replay.GetProperty("decisionRecordId").GetGuid().Should().Be(first.StructuredContent!.Value.GetProperty("decisionRecordId").GetGuid());
        rig.Writes.Should().HaveCount(writes, "a repeated review id performs no write");
        rig.Records.Should().ContainSingle();
    }

    [Fact]
    public async Task CompleteTool_TheBecauseValue_AppearsOnTheWrittenDecisionRecord()
    {
        var rig = new Rig();

        await CompleteTool(rig, PathBReview(), Http());

        using var snapshot = JsonDocument.Parse(rig.Record.GetAttributeValue<string>("sprk_factsnapshot"));
        snapshot.RootElement.GetProperty("because").GetString().Should().Be("The firm agreed the scope change");
    }

    // ── warnings reach structuredContent (FR-08, D-216) ──────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteTool_ABudgetInquiryWhoseArchivalFailed_ReturnsSuccess_WithTheWarningsAndTheStepWarning_AsRestDoes()
    {
        static Rig ArchiveFails()
        {
            var rig = new Rig();
            rig.Executors["send-budget-inquiry"].Result = DecisionActionOutcome.Done(
                "send-budget-inquiry",
                new DecisionRecordRef("sprk_servicerequest", ServiceRequestId), new DecisionRecordRef("sprk_communication", CommunicationId))
                with
            { Warning = "inquiry_email_not_archived" };
            return rig;
        }

        var review = PathBReview();

        var result = await CompleteTool(ArchiveFails(), review, Http());
        var rest = await DecisionCommitEndpoints.CommitDecisionAsync(Primary, review, Http(), CapabilityOver(ArchiveFails()), CancellationToken.None);

        result.IsError.Should().NotBe(true, "the decision is recorded; the warning is not a failure");
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).Should().Contain("inquiry_email_not_archived");
        structured.GetProperty("steps").EnumerateArray()
            .Single(s => s.GetProperty("code").GetString() == "send-budget-inquiry")
            .GetProperty("warning").GetString().Should().Be("inquiry_email_not_archived");
        var restBody = rest.Should().BeOfType<Ok<DecisionCommitResult>>().Subject.Value!;
        restBody.Warnings.Should().Contain("inquiry_email_not_archived", "REST returns the same warning for the same input");
        structured.GetRawText().Should().Be(JsonSerializer.Serialize(restBody, RestWire));
    }

    // ── refusals write nothing ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteTool_WithNoPriorPlanRead_OnASignalTheSystemClosed_IsSignalNotOpen_AndWritesNothing()
    {
        // A missing stamp means "review starts now" (ISS-004 ruling, 2026-11-07), so the system's earlier closure is a conflict.
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, Resolved, SignalWriter.ResolutionConditionCleared, Now.UtcDateTime.AddMinutes(-5));

        var result = await CompleteTool(rig, PathBReview(), Http());

        ErrorReason(result).Should().Be(DecisionCommitReasons.SignalNotOpen);
        rig.Writes.Should().BeEmpty();
        rig.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteTool_ACallerWithoutWriteOnTheSubject_IsNotAuthorized_AndWritesNothing()
    {
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        rig.Rights[("sprk_todos", ItemTodo)] = AccessRights.Read | AccessRights.AppendTo;
        var review = new DecisionReview
        {
            ReviewId = Guid.NewGuid(),
            Confirmed = true,
            Actions = [Take("mark-complete"), Skip("reschedule")],
        };

        var result = await CompleteTool(rig, review, Http(), DoSignal);

        ErrorReason(result).Should().Be(DecisionCommitReasons.NotAuthorized);
        rig.Writes.Should().BeEmpty();
        rig.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_invoice")]
    [InlineData("sprk_billingevent")]
    public async Task CompleteTool_ABillingSignal_IsTheMappedDecidedInInvoiceReviewRefusal_WritesNothing_AndNeverAsksTheRightsProbe(string subject)
    {
        // The ONE MCP billing test (ontology condition 2): the tool has no billing check; the refusal is DecisionCommitService's (1c-billing).
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: subject, subjectId: BillingSubjectId);

        var result = await CompleteTool(rig, PathBReview(inquiry: false, todo: false), Http());

        ErrorReason(result).Should().Be("ontology.decision.decided_in_invoice_review");
        AssertNoRightsAskedAndNothingWritten(rig, "an invoice or line is decided in Invoice Review");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteTool_WithNoHttpContextOrNoAuthenticatedCaller_IsUserContextRequired_AndWritesNothing(bool unauthenticated)
    {
        var rig = new Rig();
        var http = unauthenticated ? new DefaultHttpContext() : null;

        var result = await CompleteTool(rig, PathBReview(), http);

        ErrorReason(result).Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        rig.User.Invocations.Should().BeEmpty("nothing is read as the caller");
        rig.Org.Invocations.Should().BeEmpty("nothing falls back to an app-only client");
        rig.Writes.Should().BeEmpty();
        rig.Records.Should().BeEmpty();
        rig.Status(Primary).Should().Be(Open);
    }

    // ── a failed commit (D-17) ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteTool_AFailedCommit_IsAnError_WithThePartialFactsRestReturns()
    {
        static Rig TodoFails()
        {
            var rig = new Rig();
            rig.Executors["add-todo"].Result = DecisionActionOutcome.Failed("add-todo", DecisionActionReasons.WriteFailed, "Dataverse said no");
            return rig;
        }

        var review = PathBReview();

        var result = await CompleteTool(TodoFails(), review, Http());
        var rest = await DecisionCommitEndpoints.CommitDecisionAsync(Primary, review, Http(), CapabilityOver(TodoFails()), CancellationToken.None);

        var extensions = rest.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails.Extensions;
        ErrorReason(result).Should().Be((string)extensions["reasonCode"]!, "one reasonCode vocabulary across both hosts");
        var partial = result.StructuredContent!.Value.GetProperty("partial");
        partial.GetProperty("failedStep").GetInt32().Should().Be(3).And.Be((int)extensions["failedStep"]!);
        partial.GetProperty("landed").EnumerateArray().Select(l => l.GetProperty("entity").GetString())
            .Should().Equal(((IReadOnlyList<DecisionCommitRef>)extensions["landed"]!).Select(l => l.Entity)).And.Equal("sprk_budgetrevision", "sprk_budget");
        partial.GetProperty("possiblyLanded").GetArrayLength().Should().Be(((IReadOnlyList<DecisionCommitRef>)extensions["possiblyLanded"]!).Count);
        partial.GetProperty("resumable").GetBoolean().Should().Be((bool)extensions["resumable"]!);
        partial.TryGetProperty("replayed", out _).Should().BeTrue();
    }

    // ── the caller is the request's own (FR-10) ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteTool_HandsTheCapabilityExactlyTheAccessorsHttpContext()
    {
        var http = Http();
        var capability = new RecordingCommitCapability();

        await CompleteDecisionTool.CompleteAsync(
            Primary, PathBReview(), new HttpContextAccessor { HttpContext = http }, capability, CancellationToken.None);

        capability.Calls.Should().Be(1);
        capability.ReceivedContext.Should().BeSameAs(http, "one caller per call: never a constructed or cached context");
    }

    [Fact]
    public async Task CompleteTool_WithNoContext_StillCallsTheCapabilityWithNull_SoTheGuardIsTheCapabilitysOwn()
    {
        var capability = new RecordingCommitCapability();

        var result = await CompleteDecisionTool.CompleteAsync(
            Primary, PathBReview(), new HttpContextAccessor { HttpContext = null }, capability, CancellationToken.None);

        capability.ReceivedContext.Should().BeNull();
        capability.Calls.Should().Be(1);
        ErrorReason(result).Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
    }

    /// <summary>A boundary double: records the context it is handed and answers the capability's own no-caller result for a null one.</summary>
    private sealed class RecordingCommitCapability : IDecisionCapability
    {
        public int Calls { get; private set; }

        public HttpContext? ReceivedContext { get; private set; }

        public Task<DecisionCommitResult> CommitAsync(HttpContext? http, Guid signalId, DecisionReview review, CancellationToken ct)
        {
            Calls++;
            ReceivedContext = http;
            return Task.FromResult(http is null
                ? DecisionCapability.UserContextRequiredCommitResult
                : new DecisionCommitResult { Status = DecisionCommitStatus.Recorded, ReviewId = review.ReviewId });
        }

        public Task<SignalReadResult> ListDecideAsync(System.Security.Claims.ClaimsPrincipal? user, CancellationToken ct, WorklistSet set = WorklistSet.Lanes) =>
            throw new NotSupportedException();

        public Task<DecidePageResult> ListDecidePageAsync(System.Security.Claims.ClaimsPrincipal? user, string? cursor, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<DecisionPlanReadResult> GetPlanAsync(HttpContext? http, Guid signalId, CancellationToken ct) => throw new NotSupportedException();

        public Task<SuppressionReadResult> GetSuppressionAsync(HttpContext? http, Guid signalId, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
