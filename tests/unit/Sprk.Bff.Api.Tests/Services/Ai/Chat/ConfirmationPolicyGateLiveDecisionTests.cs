using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// FR-A1-03 (spaarke-ai-architecture-redesign-r2 task 044, gate G-R2-A) — the deterministic
/// Confirmation Policy v2 engine LIVE on the core chat gate path. These are behavior tests on the
/// REAL gate: they construct the real <see cref="SideEffectGateAIFunction"/> over the real
/// <c>ConfirmationPolicyEngine</c> / <c>RiskTierResolver</c> (NOT a mocked engine), a real
/// <see cref="ToolHandlerToAIFunctionAdapter"/> over a spy handler, and a real
/// <see cref="PendingPlanManager"/> + <see cref="ChatSessionManager"/> over the in-memory tenant
/// cache — so the ledger state IS the observable behavior (ADR-038 low-mock).
///
/// <para>
/// The seven observable behaviors the operator ratified (2026-07-09):
/// (i) clear+complete Tier-2b create → EXECUTES, no dialog, no suspend marker, Undo affordance;
/// (ii) email (communicate) → DRAFTS + review/send deep link, no dialog, no auto-send;
/// (iii) ambiguous instruction → the AGENT asks (layer 1 — asserted via the directive mechanism in
/// <see cref="SideEffectHonestyDirectiveTests"/>); (iv) incomplete args → ELICIT; (v) irreversible
/// in-system (delete/supersede escalated to Tier 4) → CONFIRM once; (vi) uncertain/unknown → CONFIRM
/// (fail-closed) — proven two ways so a FAKED signal fails; (vii) R5-E hard block → honest ❌ +
/// affordance, ZERO Dataverse writes.
/// </para>
/// </summary>
public class ConfirmationPolicyGateLiveDecisionTests
{
    private const string TenantId = "tenant-policy-live-tests";
    private const string EnvUrl = "https://spaarkedev1.crm.dynamics.com";

    // ---- Risk profiles (catalog sprk_configuration DATA) -------------------------------------
    private const string Tier2bReversibleCreate =
        """{"riskProfile":{"tier":"2b","reversible":true,"recordOfTruthImpact":true}}""";
    private const string Tier1EmailDraft =
        """{"riskProfile":{"tier":"1","reversible":true}}""";
    // Declared 2b but the factor floor (irreversible + record-of-truth) ESCALATES to Tier 4.
    private const string IrreversibleDeleteSupersede =
        """{"riskProfile":{"tier":"2b","reversible":false,"recordOfTruthImpact":true}}""";

    private const string SchemaTablenameRequired =
        """{"type":"object","properties":{"tablename":{"type":"string"},"subject":{"type":"string"}},"required":["tablename"]}""";
    private const string SchemaTablenameAndSubjectRequired =
        """{"type":"object","properties":{"tablename":{"type":"string"},"subject":{"type":"string"}},"required":["tablename","subject"]}""";

    // =====================================================================================
    // (i) clear + complete Tier-2b create → executes, no dialog, no suspend marker, Undo present
    // =====================================================================================
    [Fact]
    public async Task Invoke_ClearCompleteTier2bCreate_ExecutesNoDialog_WithUndoAffordance_NoSuspendMarker()
    {
        var handler = new SpyHandler
        {
            CreatedRecord = new ToolCreatedRecord("sprk_task", Guid.NewGuid()),
            UserSummary = "Created the follow-up task, due Friday, assigned to you.",
        };
        var gate = BuildGate(handler, Tier2bReversibleCreate, SchemaTablenameRequired,
            ToolSideEffectClass.Write, out var sessionManager, out var sessionId, out var sse);

        var result = await gate.InvokeAsync(new AIFunctionArguments { ["tablename"] = "sprk_task" }, CancellationToken.None);

        handler.ExecuteChatCalled.Should().BeTrue("a clear+complete low-risk (Tier 2b) request auto-executes");
        result.Should().BeOfType<string>().Which.Should().Contain("ACTION EXECUTED");

        sse.Should().NotContain(e => e.Type == "action_confirmation", "no confirmation dialog for the happy path");
        var outcome = sse.Should().ContainSingle(e => e.Type == "action_outcome").Subject.Data
            .Should().BeOfType<ChatSseActionOutcomeData>().Subject;
        outcome.NextSteps.Should().Contain("Undo", "Tier 2a/2b execute carries an Undo affordance chip");

        var gates = (await sessionManager.GetSessionAsync(TenantId, sessionId))!.Gates ?? [];
        gates.Should().NotContain(g => g.Status == PendingPlanManager.GateStatusPending,
            "an auto-executed invocation writes NO pending suspend marker");
        var outputs = (await sessionManager.GetSessionAsync(TenantId, sessionId))!.Outputs ?? [];
        outputs.Should().ContainSingle(o => o.BindingId == "loop", "the executed outcome is stored as a loop@t{n} ledger output (ADR-040)");
    }

    // =====================================================================================
    // (ii) email (communicate) → drafts + review/send deep link, no dialog, no auto-send
    // =====================================================================================
    [Fact]
    public async Task Invoke_EmailCommunicateDraft_ExecutesWithReviewSendDeepLink_NoDialog_NoAutoSend()
    {
        var communicationId = Guid.NewGuid();
        var handler = new SpyHandler
        {
            CreatedRecord = new ToolCreatedRecord("sprk_communication", communicationId),
            UserSummary = "Draft email created — ready for your review in Communications. No email was sent.",
        };
        var gate = BuildGate(handler, Tier1EmailDraft, SchemaTablenameRequired,
            ToolSideEffectClass.Communicate, out var sessionManager, out var sessionId, out var sse);

        var result = await gate.InvokeAsync(new AIFunctionArguments { ["tablename"] = "x" }, CancellationToken.None);

        handler.ExecuteChatCalled.Should().BeTrue("an email DRAFT is Tier 1 — it executes (drafts), never auto-sends");
        var message = result.Should().BeOfType<string>().Subject;
        message.Should().Contain("NOT SENT", "the grounded turn text tells the model the system did not send anything");
        message.Should().Contain($"etn=sprk_communication&id={communicationId:D}", "the review-and-send deep link is relayed verbatim");

        sse.Should().NotContain(e => e.Type == "action_confirmation", "an email draft is a handoff, never a confirm dialog");
        var outcome = sse.Should().ContainSingle(e => e.Type == "action_outcome").Subject.Data
            .Should().BeOfType<ChatSseActionOutcomeData>().Subject;
        outcome.LinkLabel.Should().Be("Review and send");
        outcome.LinkUrl.Should().Contain("pagetype=entityrecord").And.Contain("etn=sprk_communication");

        var gates = (await sessionManager.GetSessionAsync(TenantId, sessionId))!.Gates ?? [];
        gates.Should().NotContain(g => g.Status == PendingPlanManager.GateStatusPending, "no suspend marker for a draft handoff");
    }

    // =====================================================================================
    // (iv) incomplete args → elicit once, no execution, no suspend marker
    // =====================================================================================
    [Fact]
    public async Task Invoke_IncompleteRequiredArgs_Elicits_NoExecution_NoSuspendMarker()
    {
        var handler = new SpyHandler();
        var gate = BuildGate(handler, Tier2bReversibleCreate, SchemaTablenameAndSubjectRequired,
            ToolSideEffectClass.Write, out var sessionManager, out var sessionId, out var sse);

        // "subject" is a declared required arg and is missing → incomplete.
        var result = await gate.InvokeAsync(new AIFunctionArguments { ["tablename"] = "sprk_task" }, CancellationToken.None);

        var message = result.Should().BeOfType<string>().Subject;
        message.Should().Contain("NEEDS MORE INFORMATION");
        message.Should().Contain("subject", "the elicitation names the missing required detail");

        handler.ExecuteChatCalled.Should().BeFalse("elicit neither executes nor suspends");
        sse.Should().NotContain(e => e.Type == "action_confirmation");
        var gates = (await sessionManager.GetSessionAsync(TenantId, sessionId))!.Gates ?? [];
        gates.Should().NotContain(g => g.Status == PendingPlanManager.GateStatusPending, "elicitation writes no suspend marker");
    }

    // =====================================================================================
    // (v) irreversible in-system (delete/supersede escalated to Tier 4) → confirm exactly once
    // =====================================================================================
    [Fact]
    public async Task Invoke_IrreversibleInSystemDeleteSupersede_ConfirmsExactlyOnce_DoesNotExecute()
    {
        var handler = new SpyHandler();
        var gate = BuildGate(handler, IrreversibleDeleteSupersede, SchemaTablenameRequired,
            ToolSideEffectClass.Write, out var sessionManager, out var sessionId, out var sse);

        var result = await gate.InvokeAsync(new AIFunctionArguments { ["tablename"] = "sprk_matter" }, CancellationToken.None);

        result.Should().BeOfType<string>().Which.Should().Contain("ACTION SUSPENDED",
            "an irreversible record-of-truth mutation (RiskTierResolver escalates 2b→Tier 4) always confirms");
        handler.ExecuteChatCalled.Should().BeFalse("the destructive tool never runs without explicit confirmation");

        sse.Should().ContainSingle(e => e.Type == "action_confirmation", "exactly one confirmation dialog");
        var gates = (await sessionManager.GetSessionAsync(TenantId, sessionId))!.Gates ?? [];
        gates.Should().ContainSingle(g => g.Status == PendingPlanManager.GateStatusPending, "exactly one suspend marker");
    }

    // =====================================================================================
    // (vi-a) UNKNOWN risk (no declared riskProfile) → confirm (fail-closed)
    // =====================================================================================
    [Fact]
    public async Task Invoke_WriteWithNoDeclaredRiskProfile_FailsClosedToConfirm_DoesNotExecute()
    {
        var handler = new SpyHandler();
        // Configuration present but declares NO riskProfile block ⇒ the engine cannot assess risk.
        var gate = BuildGate(handler, "{}", SchemaTablenameRequired,
            ToolSideEffectClass.Write, out var sessionManager, out var sessionId, out var sse);

        var result = await gate.InvokeAsync(new AIFunctionArguments { ["tablename"] = "sprk_task" }, CancellationToken.None);

        result.Should().BeOfType<string>().Which.Should().Contain("ACTION SUSPENDED",
            "no declared risk DATA ⇒ fail-closed confirm (an un-migrated write never auto-executes)");
        handler.ExecuteChatCalled.Should().BeFalse();
        (await sessionManager.GetSessionAsync(TenantId, sessionId))!.Gates!
            .Should().ContainSingle(g => g.Status == PendingPlanManager.GateStatusPending);
    }

    // =====================================================================================
    // (vi-b) ANTI-SHIM: a REAL low-confidence signal flips the SAME Tier-2b request to confirm.
    // If the ambiguity signal were hardcoded/ignored, this would auto-execute (test i) and FAIL.
    // =====================================================================================
    [Fact]
    public async Task Invoke_Tier2bButDispatchUncertainSignalFires_ConfirmsInsteadOfExecuting()
    {
        var handler = new SpyHandler
        {
            CreatedRecord = new ToolCreatedRecord("sprk_task", Guid.NewGuid()),
        };
        // SAME Tier-2b tool as test (i) — the ONLY difference is the honored low-confidence probe.
        var gate = BuildGate(handler, Tier2bReversibleCreate, SchemaTablenameRequired,
            ToolSideEffectClass.Write, out var sessionManager, out var sessionId, out var sse,
            dispatchUncertaintyProbe: () => true);

        var result = await gate.InvokeAsync(new AIFunctionArguments { ["tablename"] = "sprk_task" }, CancellationToken.None);

        result.Should().BeOfType<string>().Which.Should().Contain("ACTION SUSPENDED",
            "a real dispatch-uncertain signal is HONORED — it flips the otherwise-executing Tier 2b to confirm");
        handler.ExecuteChatCalled.Should().BeFalse("the low-confidence backstop suspends rather than executing a guess");
        (await sessionManager.GetSessionAsync(TenantId, sessionId))!.Gates!
            .Should().ContainSingle(g => g.Status == PendingPlanManager.GateStatusPending);
    }

    // =====================================================================================
    // (vii) R5-E hard block → honest ❌ + affordance, ZERO Dataverse writes
    // =====================================================================================
    [Fact]
    public async Task Invoke_R5EHardBlock_RendersHonestRefusalWithAffordance_ZeroWrites()
    {
        const string doomed =
            "Spaarke document records can't be created from chat — REJECTED, nothing was written. " +
            "Open the correct path now: [Document Upload wizard](https://spaarkedev1.crm.dynamics.com/upload?matter=abc)";
        var handler = new SpyHandler { ValidationError = doomed, CreatedRecord = new ToolCreatedRecord("sprk_document", Guid.NewGuid()) };
        var gate = BuildGate(handler, Tier2bReversibleCreate, SchemaTablenameRequired,
            ToolSideEffectClass.Write, out var sessionManager, out var sessionId, out var sse);

        var result = await gate.InvokeAsync(new AIFunctionArguments { ["tablename"] = "sprk_document" }, CancellationToken.None);

        var message = result.Should().BeOfType<string>().Subject;
        message.Should().Contain("REJECTED");
        message.Should().Contain("[Document Upload wizard](https://spaarkedev1.crm.dynamics.com/upload?matter=abc)",
            "the D-F0(d) affordance link is relayed verbatim");
        message.Should().NotContain("ACTION SUSPENDED", "a doomed call is rejected, never suspended into a confirm");
        message.Should().NotContain("ACTION EXECUTED", "a doomed call never executes");

        handler.ExecuteChatCalled.Should().BeFalse("R5-E hard block: ZERO Dataverse writes");
        sse.Should().NotContain(e => e.Type == "action_confirmation");
        sse.Should().NotContain(e => e.Type == "action_outcome");

        var session = (await sessionManager.GetSessionAsync(TenantId, sessionId))!;
        (session.Gates ?? []).Should().Contain(g => g.Status == PendingPlanManager.GateStatusValidationFailed,
            "the honest ❌ is a persisted terminal ledger marker (store-before-render)");
        (session.Gates ?? []).Should().NotContain(g => g.Status == PendingPlanManager.GateStatusPending);
        (session.Outputs ?? []).Should().BeEmpty("no loop@t{n} output — nothing executed, zero writes");
    }

    // =====================================================================================
    // (iii) ambiguous instruction → the AGENT asks (LAYER 1) — the directive mechanism that makes
    // the agent turn ask a clarifying question rather than dispatch a guess, BEFORE the gate.
    // (The behavioral asking is LLM-driven and covered by the golden-utterance eval family; this
    // asserts the deterministic mechanism that drives it exists in the single steering directive.)
    // =====================================================================================
    [Fact]
    public void SideEffectHonestyDirective_InstructsAgentToAskWhenTornBetweenCapabilities()
    {
        var directive = SprkChatAgentFactory.SideEffectHonestyDirective;

        directive.Should().Contain("GENUINELY torn",
            "layer 1 (ADR-039's intent decider) tells the agent to ask when torn between capabilities");
        directive.Should().Contain("clarifying question");
        directive.Should().Contain("do NOT dispatch a guess",
            "wrong-choice risk is caught by asking BEFORE any tool call reaches the gate");
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

    private static SideEffectGateAIFunction BuildGate(
        SpyHandler handler,
        string configurationJson,
        string jsonSchema,
        ToolSideEffectClass sideEffectClass,
        out ChatSessionManager sessionManager,
        out string sessionId,
        out List<ChatSseEvent> sseEvents,
        Func<bool>? dispatchUncertaintyProbe = null)
    {
        var cache = new InMemoryTenantCache();
        sessionManager = new ChatSessionManager(
            cache,
            new Mock<IChatDataverseRepository>().Object,
            new Mock<ILogger<ChatSessionManager>>().Object);

        sessionId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        sessionManager.UpdateSessionCacheAsync(new ChatSession(
            SessionId: sessionId,
            TenantId: TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: now,
            LastActivity: now,
            Messages: new List<global::Sprk.Bff.Api.Models.Ai.Chat.ChatMessage>())).GetAwaiter().GetResult();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantCache>(cache);
        services.AddSingleton(sessionManager);
        services.AddSingleton<ILogger<PendingPlanManager>>(NullLogger<PendingPlanManager>.Instance);
        services.AddScoped<PendingPlanManager>();
        services.AddSingleton<IOptions<DataverseOptions>>(
            Options.Create(new DataverseOptions { EnvironmentUrl = EnvUrl }));
        var rootServices = services.BuildServiceProvider();

        var tool = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "SYS-Policy_Live_Tool",
            Description = "test side-effecting tool",
            Type = ToolType.Custom,
            HandlerClass = SpyHandler.Id,
            AvailableInContexts = ToolAvailabilityContext.Chat,
            JsonSchema = jsonSchema,
            Configuration = configurationJson,
            SideEffectClass = sideEffectClass,
        };

        var adapter = new ToolHandlerToAIFunctionAdapter(
            tool,
            handler,
            contextFactory: () => new ChatInvocationContext { ChatSessionId = Guid.NewGuid(), TenantId = TenantId });

        var captured = new List<ChatSseEvent>();
        sseEvents = captured;

        return new SideEffectGateAIFunction(
            adapter,
            sideEffectClass,
            rootServices,
            TenantId,
            sessionId,
            NullLogger.Instance,
            sseWriter: (evt, _) => { captured.Add(evt); return Task.CompletedTask; },
            dispatchUncertaintyProbe: dispatchUncertaintyProbe);
    }

    /// <summary>
    /// Spy handler: <c>ValidateChat</c> configurable (R5-E doom); <c>ExecuteChatAsync</c> records a
    /// flag and returns a success <see cref="ToolResult"/> carrying the audience-split UserSummary +
    /// a created-record reference (so the gate composes the Undo / review-and-send deep link).
    /// </summary>
    private sealed class SpyHandler : IToolHandler
    {
        public const string Id = "PolicyLiveSpyHandler";

        public string? ValidationError { get; init; }
        public ToolCreatedRecord? CreatedRecord { get; init; }
        public string? UserSummary { get; init; }
        public bool ExecuteChatCalled { get; private set; }

        public string HandlerId => Id;

        public ToolHandlerMetadata Metadata { get; } = new(
            Name: "Policy Live Spy Handler",
            Description: "Test double for the Policy v2 gate live path.",
            Version: "1.0.0",
            SupportedInputTypes: new[] { "text/plain" },
            Parameters: Array.Empty<ToolParameterDefinition>());

        public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

        public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

        public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool)
            => ToolValidationResult.Success();

        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken)
            => Task.FromResult(ToolResult.Ok(HandlerId, tool.Id, tool.Name, new { legacy = true }));

        public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
            => ValidationError is null ? ToolValidationResult.Success() : ToolValidationResult.Failure(ValidationError);

        public Task<ToolResult> ExecuteChatAsync(ChatInvocationContext context, AnalysisTool tool, CancellationToken cancellationToken)
        {
            ExecuteChatCalled = true;
            var metadata = new Dictionary<string, object?>();
            if (UserSummary is not null) metadata[ToolResultMetadataKeys.UserSummary] = UserSummary;
            if (CreatedRecord is not null) metadata[ToolResultMetadataKeys.CreatedRecord] = CreatedRecord;

            var result = ToolResult.Ok(HandlerId, tool.Id, tool.Name, new { ok = true }, summary: "executed");
            return Task.FromResult(metadata.Count == 0 ? result : result with { Metadata = metadata });
        }
    }
}
