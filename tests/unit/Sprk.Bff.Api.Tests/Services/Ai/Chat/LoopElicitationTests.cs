using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// FR-P2-03 acceptance tests (spaarke-ai-architecture-redesign-r1 task 032) —
/// loop-native elicitation, test-proving the canonical walkthrough steps 10-12
/// semantics (docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md §3.10):
///
/// Contract anchors (maintain-class per ADR-038 — each protects a production behavior):
///  1. Missing required args (per the Binding's DECLARED sprk_inputschema) produce a
///     clarifying turn grounded in declared field names/prompts — never execution
///     with absent/guessed values, never invented fields (ADR-039 grounded outputs).
///  2. capture_mode: modal routes to the wizard surface (elicitation_modal SSE event)
///     instead of conversational Q&amp;A.
///  3. The pending elicitation Gate marker is in the session ledger BEFORE the
///     clarifying/modal turn renders (ADR-040 storage-precedes-rendering); markers
///     carry binding id + missing-field identifiers only (NFR-07).
///  4. Mid-elicitation utterances parse as ANSWERS unless hard-slash / explicit
///     restart — deterministic string checks, no intent classification (ADR-039).
///  5. A successful dispatch of the awaited Binding resolves the pending elicitation
///     at the ONE dispatch seam; expiry/supersede close markers append-only.
/// </summary>
public class LoopElicitationTests
{
    private const string TenantId = "tenant-elicitation-tests";

    /// <summary>Walkthrough steps 10-13 schema: create_task requires due_date + owner.</summary>
    private const string CreateTaskSchema = """
        {
          "type": "object",
          "properties": {
            "title":    { "type": "string", "description": "Task title." },
            "due_date": { "type": "string", "elicitation_prompt": "What's the due date?" },
            "owner":    { "type": "string", "description": "Who should the task be assigned to?" }
          },
          "required": ["due_date", "owner"]
        }
        """;

    private readonly InMemoryTenantCache _cache = new();
    private readonly ChatSessionManager _sessionManager;
    private readonly PendingPlanManager _pendingManager;

    public LoopElicitationTests()
    {
        _sessionManager = new ChatSessionManager(
            _cache,
            new Mock<IChatDataverseRepository>().Object,
            new Mock<ILogger<ChatSessionManager>>().Object);
        _pendingManager = new PendingPlanManager(
            _cache,
            _sessionManager,
            new Mock<ILogger<PendingPlanManager>>().Object);
    }

    // =========================================================================
    // 1. Declared-schema validation (BindingInputSchemaValidator — pure)
    // =========================================================================

    [Fact]
    public void GetRequiredFields_RequiredArrayAndPrompts_ParsesDeclaredFieldsOnly()
    {
        var fields = BindingInputSchemaValidator.GetRequiredFields(CreateTaskSchema);

        fields.Select(f => f.Name).Should().BeEquivalentTo(new[] { "due_date", "owner" },
            "the required array is the declared contract; optional 'title' never elicits");
        fields.Single(f => f.Name == "due_date").Prompt.Should().Be("What's the due date?",
            "elicitation_prompt is the maker-authored wording source");
        fields.Single(f => f.Name == "owner").Prompt.Should().Be("Who should the task be assigned to?",
            "description is the fallback prompt when no elicitation_prompt is declared");
    }

    [Fact]
    public void GetRequiredFields_PerPropertyRequiredTrue_IsHonored()
    {
        var fields = BindingInputSchemaValidator.GetRequiredFields(
            """{"type":"object","properties":{"matterId":{"type":"string","required":true}}}""");

        fields.Should().ContainSingle().Which.Name.Should().Be("matterId",
            "maker-tolerant twin: per-property required:true declares the field required");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("[1,2,3]")]
    public void GetRequiredFields_MalformedOrAbsentSchema_YieldsNothingToElicit(string? schema)
    {
        BindingInputSchemaValidator.GetRequiredFields(schema).Should().BeEmpty(
            "maker data errors degrade, they never throw (routing tolerance rule)");
    }

    [Fact]
    public void FindMissingRequired_AbsentNullOrWhitespaceValues_AreMissing_ProvidedAreNot()
    {
        var missing = BindingInputSchemaValidator.FindMissingRequired(
            CreateTaskSchema,
            new AIFunctionArguments
            {
                ["title"] = "Review NDA issues",
                ["due_date"] = "  ",  // whitespace-only = missing
                // owner absent = missing
            });

        missing.Select(f => f.Name).Should().BeEquivalentTo(new[] { "due_date", "owner" });

        BindingInputSchemaValidator.FindMissingRequired(
            CreateTaskSchema,
            new AIFunctionArguments { ["due_date"] = "7/9/2026", ["owner"] = "me" })
            .Should().BeEmpty("presence satisfies elicitation; value quality is the executor's concern");
    }

    // =========================================================================
    // 4. Walkthrough steps 10-12 — deterministic answer-vs-escape semantics
    // =========================================================================

    [Theory]
    [InlineData("7/9/2026 and yes me", false)]  // walkthrough step 12 — the ANSWER
    [InlineData("assign it to Jane, due next friday", false)]
    [InlineData("/summarize", true)]            // hard slash
    [InlineData("  /matter new", true)]
    [InlineData("start over", true)]            // explicit restart
    [InlineData("Start over.", true)]
    [InlineData("never mind", true)]
    [InlineData("cancel", true)]
    [InlineData("cancel the meeting on friday", false)] // NOT an exact restart phrase — it's an answer
    public void EscapeChecks_AreExactDeterministicStringChecks(string utterance, bool escapes)
    {
        var isEscape = ElicitationTurnRouter.IsHardSlash(utterance) ||
                       ElicitationTurnRouter.IsExplicitRestart(utterance);

        isEscape.Should().Be(escapes,
            "answer-vs-command disambiguation is deterministic (hard-slash/restart only) — " +
            "no intent classification (ADR-039; walkthrough steps 10-12)");
    }

    [Fact]
    public void BuildClarifyInstruction_NamesOnlyDeclaredFieldsAndPrompts()
    {
        var missing = BindingInputSchemaValidator.GetRequiredFields(CreateTaskSchema);

        var instruction = ElicitationTurnRouter.BuildClarifyInstruction(
            "create-task", "capability_create-task", missing);

        instruction.Should().Contain("'due_date'").And.Contain("What's the due date?");
        instruction.Should().Contain("'owner'").And.Contain("Who should the task be assigned to?");
        instruction.Should().Contain("capability_create-task",
            "the model re-invokes the SAME tool once answers arrive");
        instruction.Should().Contain("do not invent",
            "grounded outputs: the clarifying turn references declared schema only");
    }

    [Fact]
    public void BuildAnswerFrame_CarriesPendingInvocationStateAndUserMessage()
    {
        var frame = ElicitationTurnRouter.BuildAnswerFrame(
            new PendingInvocation
            {
                GateId = "elicitation-1",
                Kind = PendingPlanManager.GateKindElicitation,
                SessionId = "s1",
                TenantId = TenantId,
                ToolId = "capability_create-task",
                MissingFields = new[] { "due_date", "owner" },
                ArgsJson = """{"title":"Review NDA issues"}""",
            },
            "7/9/2026 and yes me");

        frame.Should().Contain("capability_create-task");
        frame.Should().Contain("due_date, owner");
        frame.Should().Contain("""{"title":"Review NDA issues"}""",
            "the loop re-invokes with previously provided args plus the parsed answers");
        frame.Should().Contain("USER MESSAGE: 7/9/2026 and yes me");
    }

    // =========================================================================
    // 1+3. Missing args at the tool seam → suspend (marker FIRST) + clarify turn
    // =========================================================================

    [Fact]
    public async Task CapabilityTool_MissingRequiredArgs_SuspendsWithMarkerAndReturnsClarifyingTurn()
    {
        var session = await SeedSessionAsync();
        var dispatcher = new RecordingDispatchOrchestrator();
        var tool = CreateTool(session.SessionId, dispatcher, BindingCaptureMode.LoopElicitation,
            out var binding);

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["title"] = "Review NDA issues" });

        // Clarifying turn, grounded in the declared schema.
        result!.ToString().Should().Contain("MISSING REQUIRED INPUTS")
            .And.Contain("'due_date'").And.Contain("'owner'");
        dispatcher.DispatchCount.Should().Be(0,
            "an invocation with missing required args must NOT execute (no guessing)");

        // Ledger pending marker: kind=elicitation, binding id + missing-field identifiers.
        var stored = await _sessionManager.GetSessionAsync(TenantId, session.SessionId);
        var marker = stored!.Gates.Should().ContainSingle().Subject;
        marker.Kind.Should().Be(PendingPlanManager.GateKindElicitation);
        marker.Status.Should().Be(PendingPlanManager.GateStatusPending);
        marker.BindingId.Should().Be(binding.BindingId.ToString());
        marker.MissingFields.Should().BeEquivalentTo(new[] { "due_date", "owner" });

        // Resumable payload in THE unified store (partial args preserved for the answer turn).
        var pending = await _pendingManager.GetInvocationAsync(TenantId, session.SessionId, marker.GateId);
        pending.Should().NotBeNull();
        pending!.Kind.Should().Be(PendingPlanManager.GateKindElicitation);
        pending.ToolId.Should().Be(tool.Name);
        pending.ArgsJson.Should().Contain("Review NDA issues");
    }

    [Fact]
    public async Task CapabilityTool_CompleteArgs_DispatchesWithoutElicitation()
    {
        var session = await SeedSessionAsync();
        var dispatcher = new RecordingDispatchOrchestrator();
        var tool = CreateTool(session.SessionId, dispatcher, BindingCaptureMode.LoopElicitation, out _);

        var result = await tool.InvokeAsync(new AIFunctionArguments
        {
            ["due_date"] = "7/9/2026",
            ["owner"] = "me",
        });

        dispatcher.DispatchCount.Should().Be(1, "complete declared args execute normally");
        // G-P3 UAT round-2 R2-B pin (2026-07-07): the capability result must frame
        // itself as GENERATION only — the old "completed." framing read as an executed
        // side effect and drove the round-2 "has now been created" fabrications.
        result!.ToString().Should().Contain("finished GENERATING");
        result.ToString().Should().Contain("did NOT create, save, send, or modify");
        // G-P3 UAT round-3 R3-1 pin (2026-07-07): the model re-invoked the drafting
        // capability on every user confirmation instead of the write tool — the result
        // must carry the confirmed→invoke-write-tool-NOW bridge + the no-re-ask rule.
        result.ToString().Should().Contain("If the user has ALREADY confirmed, invoke the write");
        result.ToString().Should().Contain("do NOT invoke this capability again");
        (await _sessionManager.GetSessionAsync(TenantId, session.SessionId))!
            .Gates.Should().BeNullOrEmpty("no elicitation gate for a complete invocation");
    }

    // =========================================================================
    // 2. capture_mode: modal — wizard-surface routing (marker BEFORE render)
    // =========================================================================

    [Fact]
    public async Task CapabilityTool_ModalCaptureMode_EmitsElicitationModalEventAfterMarker()
    {
        var session = await SeedSessionAsync();
        var dispatcher = new RecordingDispatchOrchestrator();
        var emitted = new List<Sprk.Bff.Api.Api.Ai.ChatSseEvent>();
        var gatesAtEmitTime = new List<int>();

        var tool = CreateTool(session.SessionId, dispatcher, BindingCaptureMode.Modal, out var binding,
            sseWriter: async (evt, ct) =>
            {
                emitted.Add(evt);
                // ADR-040 proof: at render time the pending marker is ALREADY in the ledger.
                var s = await _sessionManager.GetSessionAsync(TenantId, session.SessionId, ct);
                gatesAtEmitTime.Add(s!.Gates?.Count ?? 0);
            });

        var result = await tool.InvokeAsync(new AIFunctionArguments { ["title"] = "Review NDA issues" });

        dispatcher.DispatchCount.Should().Be(0);
        var evt = emitted.Should().ContainSingle().Subject;
        evt.Type.Should().Be("elicitation_modal",
            "capture_mode: modal routes elicitation to the wizard surface, not chat Q&A");
        gatesAtEmitTime.Should().ContainSingle().Which.Should().BeGreaterThan(0,
            "the pending Gate marker is ledger-written BEFORE the routing event renders (ADR-040)");

        var data = evt.Data.Should().BeOfType<Sprk.Bff.Api.Api.Ai.ChatSseElicitationModalData>().Subject;
        data.BindingId.Should().Be(binding.BindingId.ToString());
        data.MissingFields.Select(f => f.Name).Should().BeEquivalentTo(new[] { "due_date", "owner" });

        var stored = await _sessionManager.GetSessionAsync(TenantId, session.SessionId);
        stored!.Gates.Should().ContainSingle(g => g.GateId == data.GateId &&
            g.Kind == PendingPlanManager.GateKindElicitation);

        result!.ToString().Should().Contain("form",
            "the model is instructed to point the user at the form, not to elicit in chat");
    }

    [Fact]
    public async Task CapabilityTool_ModalWithoutSseSurface_DegradesToLoopElicitation()
    {
        var session = await SeedSessionAsync();
        var tool = CreateTool(session.SessionId, new RecordingDispatchOrchestrator(),
            BindingCaptureMode.Modal, out _, sseWriter: null);

        var result = await tool.InvokeAsync(new AIFunctionArguments());

        result!.ToString().Should().Contain("MISSING REQUIRED INPUTS",
            "no SSE surface = no wizard route; the clarifying turn still protects the invocation");
    }

    // =========================================================================
    // 3+5. Marker lifecycle — pending query, resolve-on-dispatch, close, expiry, W1
    // =========================================================================

    [Fact]
    public void FindPendingGate_LatestUnresolvedOfKindAndBinding_Wins()
    {
        var bindingA = Guid.NewGuid().ToString();
        var bindingB = Guid.NewGuid().ToString();
        var gates = new List<SessionGate>
        {
            Gate("g1", PendingPlanManager.GateKindElicitation, "pending", bindingA, turn: 1),
            Gate("g1", PendingPlanManager.GateKindElicitation, "confirmed", bindingA, turn: 1), // resolved
            Gate("g2", PendingPlanManager.GateKindConfirmation, "pending", bindingA, turn: 2),  // wrong kind
            Gate("g3", PendingPlanManager.GateKindElicitation, "pending", bindingB, turn: 3),
        };

        PendingPlanManager.FindPendingGate(gates, PendingPlanManager.GateKindElicitation)!
            .GateId.Should().Be("g3", "g1 is resolved (latest entry per gate id wins), g2 is a confirmation");
        PendingPlanManager.FindPendingGate(gates, PendingPlanManager.GateKindElicitation, bindingA)
            .Should().BeNull("binding A's elicitation resolved");
        PendingPlanManager.FindPendingGate(gates, PendingPlanManager.GateKindElicitation, bindingB)!
            .GateId.Should().Be("g3");
        PendingPlanManager.FindPendingGate(null, PendingPlanManager.GateKindElicitation).Should().BeNull();
    }

    [Fact]
    public async Task ResolveElicitationOnDispatch_PendingGate_WritesConfirmedMarkerAndDeletesPayload()
    {
        var session = await SeedSessionAsync();
        var bindingId = Guid.NewGuid();
        await _pendingManager.SuspendInvocationAsync(BuildElicitation(session.SessionId, "el-1", bindingId));

        var resolved = await _pendingManager.ResolveElicitationOnDispatchAsync(
            TenantId, session.SessionId, bindingId);

        resolved.Should().NotBeNull();
        resolved!.Status.Should().Be(PendingPlanManager.GateStatusConfirmed);
        resolved.GateId.Should().Be("el-1");

        (await _pendingManager.GetInvocationAsync(TenantId, session.SessionId, "el-1"))
            .Should().BeNull("the resumable payload is cleaned up on resolution");

        var stored = await _sessionManager.GetSessionAsync(TenantId, session.SessionId);
        var entries = stored!.Gates!.Where(g => g.GateId == "el-1").ToList();
        entries.Should().HaveCount(2, "pending + confirmed correlate by gate id (append-only)");
        entries.Select(g => g.Turn).Distinct().Should().HaveCount(1, "resolution reuses the pending turn");
    }

    [Fact]
    public async Task ResolveElicitationOnDispatch_NothingPending_IsNoOp()
    {
        var session = await SeedSessionAsync();

        var resolved = await _pendingManager.ResolveElicitationOnDispatchAsync(
            TenantId, session.SessionId, Guid.NewGuid());

        resolved.Should().BeNull();
        (await _sessionManager.GetSessionAsync(TenantId, session.SessionId))!
            .Gates.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task CloseInvocation_Superseded_DeletesPayloadAndWritesMarker()
    {
        var session = await SeedSessionAsync();
        await _pendingManager.SuspendInvocationAsync(
            BuildElicitation(session.SessionId, "el-2", Guid.NewGuid()));

        var closed = await _pendingManager.CloseInvocationAsync(
            TenantId, session.SessionId, "el-2", PendingPlanManager.GateStatusSuperseded);
        var closeAgain = await _pendingManager.CloseInvocationAsync(
            TenantId, session.SessionId, "el-2", PendingPlanManager.GateStatusSuperseded);

        closed.Should().BeTrue("the deterministic escape (hard slash / restart) supersedes the gate");
        closeAgain.Should().BeFalse("close is idempotent — TTL-expired/resolved payloads no-op");

        var stored = await _sessionManager.GetSessionAsync(TenantId, session.SessionId);
        stored!.Gates.Should().ContainSingle(g =>
            g.GateId == "el-2" && g.Status == PendingPlanManager.GateStatusSuperseded);
        (await _pendingManager.GetInvocationAsync(TenantId, session.SessionId, "el-2")).Should().BeNull();
    }

    [Fact]
    public async Task SuspendInvocation_SessionMissing_Throws_W1Tightening()
    {
        var act = () => _pendingManager.SuspendInvocationAsync(
            BuildElicitation("no-such-session", "el-x", Guid.NewGuid()));

        await act.Should().ThrowAsync<InvalidOperationException>(
            "task 031 review W1: a suspension whose ADR-040 pending marker cannot land " +
            "must abort loudly, not proceed as unmarked ledger state");
    }

    [Fact]
    public void SessionGate_MissingFields_SurviveStoredMappingRoundTrip()
    {
        var gates = new List<SessionGate>
        {
            Gate("el-rt", PendingPlanManager.GateKindElicitation, "pending", Guid.NewGuid().ToString(), 1)
                with { MissingFields = new[] { "due_date", "owner" } },
        };

        var roundTripped = SessionPersistenceService.MapGatesFromStored(
            SessionPersistenceService.MapGatesToStored(gates));

        roundTripped!.Single().MissingFields.Should().BeEquivalentTo(new[] { "due_date", "owner" },
            "elicitation markers survive the warm-store restore (ADR-040: ledger rides the 3-tier stack)");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<ChatSession> SeedSessionAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new ChatSession(
            SessionId: Guid.NewGuid().ToString("N"),
            TenantId: TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: now,
            LastActivity: now,
            Messages: new List<Sprk.Bff.Api.Models.Ai.Chat.ChatMessage>());
        await _sessionManager.UpdateSessionCacheAsync(session);
        return session;
    }

    private BindingCapabilityTool CreateTool(
        string sessionId,
        SessionDispatchOrchestrator dispatcher,
        BindingCaptureMode captureMode,
        out Binding binding,
        Func<Sprk.Bff.Api.Api.Ai.ChatSseEvent, CancellationToken, Task>? sseWriter = null)
    {
        binding = new Binding
        {
            BindingId = Guid.NewGuid(),
            ConsumerType = "create-task",
            ActionId = Guid.NewGuid(),
            ToolDescription = "Create a To Do / task record.",
            InputSchemaJson = CreateTaskSchema,
            CaptureMode = captureMode,
        };

        var services = new ServiceCollection();
        services.AddScoped(_ => dispatcher);
        services.AddScoped(_ => _pendingManager);
        services.AddScoped(_ => _sessionManager);

        return new BindingCapabilityTool(
            binding,
            services.BuildServiceProvider(),
            TenantId,
            sessionId,
            Mock.Of<ILogger>(),
            sseWriter);
    }

    private PendingInvocation BuildElicitation(string sessionId, string gateId, Guid bindingId) => new()
    {
        GateId = gateId,
        Kind = PendingPlanManager.GateKindElicitation,
        SessionId = sessionId,
        TenantId = TenantId,
        ToolId = "capability_create-task",
        BindingId = bindingId.ToString(),
        MissingFields = new[] { "due_date", "owner" },
        ArgsJson = """{"title":"Review NDA issues"}""",
    };

    private static SessionGate Gate(string gateId, string kind, string status, string bindingId, int turn) => new()
    {
        GateId = gateId,
        Kind = kind,
        Status = status,
        Turn = turn,
        BindingId = bindingId,
        CreatedAt = DateTimeOffset.UtcNow,
        ResolvedAt = status == "pending" ? null : DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// Recording subclass over the virtual dispatch seam (protected logger-only ctor —
    /// the same ADR-038 module-boundary pattern as NullSessionDispatchOrchestrator).
    /// </summary>
    private sealed class RecordingDispatchOrchestrator : SessionDispatchOrchestrator
    {
        public RecordingDispatchOrchestrator()
            : base(Mock.Of<ILogger<SessionDispatchOrchestrator>>())
        {
        }

        public int DispatchCount { get; private set; }

        public override async IAsyncEnumerable<AnalysisChunk> DispatchAsync(
            SessionDispatchRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            DispatchCount++;
            await Task.Yield();
            yield return AnalysisChunk.Completed("task created");
        }
    }
}
