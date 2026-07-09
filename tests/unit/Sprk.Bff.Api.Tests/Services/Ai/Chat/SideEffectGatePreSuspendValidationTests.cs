using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// FR-A1-05 (spaarke-ai-architecture-redesign-r2 task 034) — GATE PRE-SUSPEND VALIDATION.
/// <para>
/// The behavioral contract this suite protects: <see cref="SideEffectGateAIFunction"/> runs the
/// wrapped handler's <c>ValidateChat</c> BEFORE suspending a side-effecting call into a
/// confirmation dialog. A PROVABLY-doomed call renders an honest ❌ (real reason + D-F0(d)
/// affordance) to a terminal <c>validation-failed</c> ledger marker (ADR-040 store-before-render)
/// with NO confirmation dialog fired — instead of the R5-E Confirm→❌ dead-end. A call that PASSES
/// validation still suspends and presents the dialog exactly as before: the gate is never weakened
/// toward execution.
/// </para>
/// </summary>
/// <remarks>
/// Low-mock per ADR-038: REAL <see cref="PendingPlanManager"/> + <see cref="ChatSessionManager"/>
/// over the in-memory tenant cache, so assertions read the ACTUAL append-only Gate ledger the
/// production suspend path writes (no <c>Verify.Once</c> interaction shapes — the ledger state IS
/// the observable behavior). Sibling to <c>TypedHandlerResumeExecutorTests</c>.
/// </remarks>
public class SideEffectGatePreSuspendValidationTests
{
    private const string TenantId = "tenant-presuspend-tests";
    private const string ToolName = "SYS-Dataverse_Create_Record";

    // Mimics the shape of the real R5-E sprk_document rejection: an honest reason carrying an
    // actionable D-F0(d) affordance (a working deep link, not a named wizard).
    private const string DoomedReasonWithAffordance =
        "Spaarke document records can't be created from chat — this create was REJECTED and nothing " +
        "was written. Open the correct path now: [Document Upload wizard](https://spaarkedev1.crm.dynamics.com/upload?matter=abc)";

    [Fact]
    public async Task InvokeAsync_DoomedCall_RendersHonestRefusalWithAffordance_NoConfirmDialog_NoPendingGate()
    {
        var handler = new FakeSideEffectHandler { ValidationError = DoomedReasonWithAffordance };
        var harness = BuildGate(handler, out var sessionManager, out var sessionId, out var sseEvents);

        var result = await harness.InvokeAsync(
            new AIFunctionArguments { ["tablename"] = "sprk_document" }, CancellationToken.None);

        // The model receives an honest ❌ that carries the real reason + the working deep link
        // verbatim, and is explicitly NOT the "ACTION SUSPENDED" text (no dialog to confirm).
        var message = result.Should().BeOfType<string>().Subject;
        message.Should().Contain("REJECTED");
        message.Should().Contain("[Document Upload wizard](https://spaarkedev1.crm.dynamics.com/upload?matter=abc)",
            "the D-F0(d) affordance link travels to the model verbatim so it can relay it to the user (NFR-10)");
        message.Should().NotContain("ACTION SUSPENDED",
            "a doomed call is never suspended into a confirmation — it is rejected outright");

        // The doomed call NEVER executed and NEVER suspended.
        handler.ExecuteChatCalled.Should().BeFalse("a validation failure short-circuits before execution");
        sseEvents.Should().NotContain(e => e.Type == "action_confirmation",
            "NEGATIVE (acceptance criterion): NO confirm dialog is presented for a provably-doomed call");

        // ADR-040 store-before-render: a terminal validation-failed marker is persisted; there is
        // NO pending gate awaiting confirmation.
        var gates = (await sessionManager.GetSessionAsync(TenantId, sessionId))!.Gates ?? [];
        gates.Should().Contain(g => g.Status == PendingPlanManager.GateStatusValidationFailed,
            "the honest ❌ is a persisted terminal ledger entry, not an ephemeral render (ADR-040)");
        gates.Should().NotContain(g => g.Status == PendingPlanManager.GateStatusPending,
            "no pending gate is left dangling for a call that was rejected before suspend");
    }

    [Fact]
    public async Task InvokeAsync_ValidCall_SuspendsAndPresentsConfirmDialog_LeavesPendingGate()
    {
        var handler = new FakeSideEffectHandler(); // ValidateChat passes
        var harness = BuildGate(handler, out var sessionManager, out var sessionId, out var sseEvents);

        var result = await harness.InvokeAsync(
            new AIFunctionArguments { ["tablename"] = "sprk_event" }, CancellationToken.None);

        // A validated call suspends exactly as before — the gate is NOT weakened toward execution.
        var message = result.Should().BeOfType<string>().Subject;
        message.Should().Contain("ACTION SUSPENDED",
            "a call that passes validation still suspends into the ONE confirmation gate (no weakening)");
        handler.ExecuteChatCalled.Should().BeFalse("the tool never runs without explicit user confirmation");
        sseEvents.Should().ContainSingle(e => e.Type == "action_confirmation",
            "the confirmation dialog is presented for a call that could succeed");

        var gates = (await sessionManager.GetSessionAsync(TenantId, sessionId))!.Gates ?? [];
        gates.Should().Contain(g => g.Status == PendingPlanManager.GateStatusPending,
            "the validated invocation is suspended pending user confirmation");
        gates.Should().NotContain(g => g.Status == PendingPlanManager.GateStatusValidationFailed);
    }

    [Fact]
    public void ValidateForGate_HandlerRejects_SurfacesTheAffordanceBearingError()
    {
        var handler = new FakeSideEffectHandler { ValidationError = DoomedReasonWithAffordance };
        var adapter = BuildAdapter(handler);

        var validation = adapter.ValidateForGate(new AIFunctionArguments { ["tablename"] = "sprk_document" });

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle()
            .Which.Should().Contain("Document Upload wizard",
                "ValidateForGate surfaces the handler's ValidateChat rejection verbatim (affordance included)");
        handler.ExecuteChatCalled.Should().BeFalse("ValidateForGate is a pure validation peek — no execution");
    }

    [Fact]
    public void ValidateForGate_HandlerValid_ReturnsSuccess()
    {
        var adapter = BuildAdapter(new FakeSideEffectHandler());

        adapter.ValidateForGate(new AIFunctionArguments { ["tablename"] = "sprk_event" })
            .IsValid.Should().BeTrue("a call that passes validation falls through to the normal suspend");
    }

    // =========================================================================
    // Harness
    // =========================================================================

    private static ToolHandlerToAIFunctionAdapter BuildAdapter(FakeSideEffectHandler handler)
    {
        var tool = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = ToolName,
            Description = "test write tool",
            Type = ToolType.Custom,
            HandlerClass = FakeSideEffectHandler.Id,
            AvailableInContexts = ToolAvailabilityContext.Chat,
            JsonSchema = """{"type":"object","properties":{"tablename":{"type":"string"}}}""",
            SideEffectClass = ToolSideEffectClass.Write,
        };

        return new ToolHandlerToAIFunctionAdapter(
            tool,
            handler,
            contextFactory: () => new ChatInvocationContext
            {
                ChatSessionId = Guid.NewGuid(),
                TenantId = TenantId,
            });
    }

    private static SideEffectGateAIFunction BuildGate(
        FakeSideEffectHandler handler,
        out ChatSessionManager sessionManager,
        out string sessionId,
        out List<ChatSseEvent> sseEvents)
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

        // Root provider from which the gate resolves a fresh-scope PendingPlanManager over the
        // SAME cache + session manager, so ledger writes land in the session read back above.
        var services = new ServiceCollection();
        services.AddSingleton<ITenantCache>(cache);
        services.AddSingleton(sessionManager);
        services.AddSingleton<ILogger<PendingPlanManager>>(NullLogger<PendingPlanManager>.Instance);
        services.AddScoped<PendingPlanManager>();
        var rootServices = services.BuildServiceProvider();

        var captured = new List<ChatSseEvent>();
        sseEvents = captured;

        return new SideEffectGateAIFunction(
            BuildAdapter(handler),
            ToolSideEffectClass.Write,
            rootServices,
            TenantId,
            sessionId,
            NullLogger.Instance,
            sseWriter: (evt, _) => { captured.Add(evt); return Task.CompletedTask; });
    }

    /// <summary>
    /// Minimal chat-capable side-effect handler: <c>ValidateChat</c> is configurable; execution
    /// records a flag so tests can prove a doomed call never runs.
    /// </summary>
    private sealed class FakeSideEffectHandler : IToolHandler
    {
        public const string Id = "FakeSideEffectHandler";

        public string? ValidationError { get; init; }
        public bool ExecuteChatCalled { get; private set; }

        public string HandlerId => Id;

        public ToolHandlerMetadata Metadata { get; } = new(
            Name: "Fake Side Effect Handler",
            Description: "Test double for the pre-suspend gate.",
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
            => ValidationError is null
                ? ToolValidationResult.Success()
                : ToolValidationResult.Failure(ValidationError);

        public Task<ToolResult> ExecuteChatAsync(ChatInvocationContext context, AnalysisTool tool, CancellationToken cancellationToken)
        {
            ExecuteChatCalled = true;
            return Task.FromResult(ToolResult.Ok(HandlerId, tool.Id, tool.Name, new { ok = true }, summary: "executed"));
        }
    }
}
