using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// FR-P2-01 (spaarke-ai-architecture-redesign-r1 task 030) — agent-turn loop
/// contract tests: per-turn tool BUDGET (default 8, graceful grounded end),
/// deterministic PRE-FILTER + prompt-cache-stable projection (NFR-04),
/// CITATION enforcement on reads (ADR-039 grounded outputs), and ToolChain
/// ledger PERSISTENCE bookkeeping (ADR-040 storage-precedes-rendering, NFR-07
/// identifiers/filters/counts only).
/// </summary>
public class AgentTurnLoopContractTests
{
    private static readonly ILogger TestLogger = Mock.Of<ILogger>();

    // ─────────────────────────────────────────────────────────────────────────
    // Budget (FR-P2-01 step 1 / NFR-09 / ADR-016)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AgentTurnOptions_ToolCallBudget_DefaultsToEight()
    {
        new AgentTurnOptions().ToolCallBudget.Should().Be(8,
            "FR-P2-01: the per-turn tool-call budget defaults to 8");
    }

    [Fact]
    public void TryBeginToolCall_WithinBudget_AllowsExactlyBudgetCalls()
    {
        var turn = new AgentTurnContract(toolCallBudget: 3);

        turn.TryBeginToolCall().Should().BeTrue();
        turn.TryBeginToolCall().Should().BeTrue();
        turn.TryBeginToolCall().Should().BeTrue();
        turn.BudgetSpent.Should().Be(3);
        turn.BudgetExhausted.Should().BeTrue();

        turn.TryBeginToolCall().Should().BeFalse("the 4th call exceeds a budget of 3");
        turn.BudgetSpent.Should().Be(3, "denied calls never consume budget");
        turn.DeniedCalls.Should().Be(1);
    }

    [Fact]
    public void Reset_AfterSpentBudget_RestoresFullBudgetForNextTurn()
    {
        var turn = new AgentTurnContract(toolCallBudget: 1);
        turn.TryBeginToolCall().Should().BeTrue();
        turn.TryBeginToolCall().Should().BeFalse();
        turn.RecordCall(new SessionToolCall { ToolId = "t" });

        turn.Reset();

        turn.BudgetSpent.Should().Be(0);
        turn.DeniedCalls.Should().Be(0);
        turn.Calls.Should().BeEmpty();
        turn.TryBeginToolCall().Should().BeTrue("budget is per-turn (reset per message)");
    }

    [Fact]
    public async Task BudgetedFunction_InvokedPastBudget_ReturnsGroundedMessageWithoutExecutingInner()
    {
        var turn = new AgentTurnContract(toolCallBudget: 1);
        var innerInvocations = 0;
        var inner = AIFunctionFactory.Create(() => { innerInvocations++; return "inner-result"; }, "tool_a");
        var sut = new BudgetedAIFunction(inner, turn, citations: null, TestLogger);

        var first = await sut.InvokeAsync(new AIFunctionArguments());
        var second = await sut.InvokeAsync(new AIFunctionArguments());

        first!.ToString().Should().Contain("inner-result");
        innerInvocations.Should().Be(1, "the over-budget call must NOT execute the inner tool");
        second!.ToString().Should().Contain("TOOL BUDGET EXHAUSTED",
            "budget-exceeded behavior ends the turn gracefully with a grounded message");
        second!.ToString().Should().Contain("do not guess or fabricate",
            "the grounded message forbids fabrication (ADR-039)");
        turn.DeniedCalls.Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ToolChain recording + persistence bookkeeping (steps 1+5 / ADR-040 / NFR-07)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BudgetedFunction_ExecutedCall_RecordsNfr07SafeToolCall()
    {
        var turn = new AgentTurnContract(toolCallBudget: 8);
        var matterId = Guid.NewGuid();
        var inner = AIFunctionFactory.Create((string query, int top) => "ok", "search_documents");
        var sut = new BudgetedAIFunction(inner, turn, citations: null, TestLogger);

        await sut.InvokeAsync(new AIFunctionArguments
        {
            ["query"] = "the full text of the user's confidential question about the merger",
            ["top"] = 5,
            ["matterId"] = matterId,
        });

        var call = turn.Calls.Should().ContainSingle().Subject;
        call.ToolId.Should().Be("search_documents");
        call.DurationMs.Should().NotBeNull();
        call.ArgsSummary.Should().Contain("top=5").And.Contain(matterId.ToString("D"),
            "identifier-shaped values (numbers, GUIDs) are ledger-safe filters");
        call.ArgsSummary.Should().NotContain("confidential",
            "NFR-07: free-text argument values must NEVER appear in ToolChain entries");
        call.ArgsSummary.Should().Contain("query=<redacted:",
            "free text is redacted to a length marker");
    }

    [Fact]
    public async Task BudgetedFunction_ToolRegistersCitations_RecordsCitationIdsDelta()
    {
        var turn = new AgentTurnContract(toolCallBudget: 8);
        var citations = new CitationContext();
        var inner = AIFunctionFactory.Create(() =>
        {
            citations.AddCitation("chunk-42", "NDA.pdf", 3, "excerpt text");
            return "found";
        }, "read_tool");
        var sut = new BudgetedAIFunction(inner, turn, citations, TestLogger);

        await sut.InvokeAsync(new AIFunctionArguments());

        var call = turn.Calls.Should().ContainSingle().Subject;
        call.Citations.Should().ContainSingle().Which.Should().Be("chunk-42",
            "citation IDS (not quotes) are recorded on the chain — NFR-07");
        call.ResultCount.Should().Be(1);
    }

    [Fact]
    public void DrainUnpersistedCalls_InterleavedPhases_YieldsSegmentsInOrderExactlyOnce()
    {
        var turn = new AgentTurnContract(toolCallBudget: 8);
        turn.RecordCall(new SessionToolCall { ToolId = "a" });
        turn.RecordCall(new SessionToolCall { ToolId = "b" });

        turn.HasUnpersistedCalls.Should().BeTrue("a ledger write is due BEFORE the next rendered content");
        var segment1 = turn.DrainUnpersistedCalls();
        segment1.Select(c => c.ToolId).Should().ContainInOrder("a", "b");
        turn.HasUnpersistedCalls.Should().BeFalse();

        // Second tool phase after some text rendered (interleaved turn).
        turn.RecordCall(new SessionToolCall { ToolId = "c" });
        var segment2 = turn.DrainUnpersistedCalls();
        segment2.Should().ContainSingle().Which.ToolId.Should().Be("c",
            "each chain segment persists exactly once (append-only ledger)");
        turn.DrainUnpersistedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task AppendToolChainAsync_SessionExists_AppendsEntryAndPersists()
    {
        var manager = new RecordingChatSessionManager(
            new ChatSession(
                SessionId: "s1", TenantId: "t1", DocumentId: null, PlaybookId: null,
                CreatedAt: DateTimeOffset.UtcNow, LastActivity: DateTimeOffset.UtcNow,
                Messages: Array.Empty<Sprk.Bff.Api.Models.Ai.Chat.ChatMessage>()));

        var entry = new SessionToolChain
        {
            Turn = 1,
            Calls = new[] { new SessionToolCall { ToolId = "search_documents", ResultCount = 3 } },
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var updated = await manager.AppendToolChainAsync("t1", "s1", entry);

        updated.Should().NotBeNull();
        updated!.ToolChains.Should().ContainSingle().Which.Should().BeSameAs(entry);
        manager.PersistedSessions.Should().ContainSingle(
            "the ToolChain entry rides the existing persistence pipeline (ADR-040: WHAT persists changed, not WHERE)");
        manager.PersistedSessions[0].ToolChains.Should().ContainSingle();
    }

    [Fact]
    public async Task AppendToolChainAsync_SessionMissing_ReturnsNullWithoutPersisting()
    {
        var manager = new RecordingChatSessionManager(session: null);

        var updated = await manager.AppendToolChainAsync("t1", "missing", new SessionToolChain { Turn = 1 });

        updated.Should().BeNull();
        manager.PersistedSessions.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NFR-07 args summarization
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SummarizeArguments_MixedValues_KeepsIdentifiersRedactsFreeText()
    {
        var id = Guid.NewGuid();
        var summary = AgentTurnContract.SummarizeArguments(new AIFunctionArguments
        {
            ["documentId"] = id.ToString(),
            ["enabled"] = true,
            ["style"] = "concise",
            ["body"] = "This is a long free-text paragraph the user typed which must never reach the ledger.",
        });

        summary.Should().Contain($"documentId={id:D}");
        summary.Should().Contain("enabled=true");
        summary.Should().Contain("style=concise", "short whitespace-free tokens are identifier-shaped");
        summary.Should().NotContain("free-text paragraph", "NFR-07: no verbatim content");
        summary.Should().Contain("body=<redacted:");
    }

    [Fact]
    public void SummarizeArguments_ContentBearingArgName_RedactsEvenSingleTokenValues()
    {
        // Step 9.5 review W2: a single-token value under a content-bearing key
        // (query/text/body/...) is still verbatim user content - redact by NAME.
        var summary = AgentTurnContract.SummarizeArguments(new AIFunctionArguments
        {
            ["query"] = "Rosebud",
            ["top"] = 3,
        });

        summary.Should().NotContain("Rosebud", "NFR-07: content-bearing arg names redact regardless of shape");
        summary.Should().Contain("query=<redacted:7>");
        summary.Should().Contain("top=3");
    }

    [Fact]
    public void SummarizeArguments_EmailDraftSubject_RedactsByName()
    {
        // FR-P3-02 (task 041): email.draft's subject is drafted FROM document/session
        // content — even a single-token subject is verbatim content. Redact by NAME so
        // it never lands in the ToolChain audit or the confirmation-card summary (NFR-07).
        var summary = AgentTurnContract.SummarizeArguments(new AIFunctionArguments
        {
            ["subject"] = "Indemnity",
            ["to"] = "jane.smith@smithlegal.example",
        });

        summary.Should().NotContain("Indemnity", "NFR-07: the drafted subject is content, not an identifier");
        summary.Should().Contain("subject=<redacted:9>");
    }

    [Fact]
    public void SummarizeArguments_EmptyOrNull_ReturnsNull()
    {
        AgentTurnContract.SummarizeArguments(null).Should().BeNull();
        AgentTurnContract.SummarizeArguments(new AIFunctionArguments()).Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Deterministic pre-filter + prompt-cache-stable projection (steps 2+3 / NFR-04)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PreFilter_BindingSurfaceMismatch_ExcludesCapabilityTool()
    {
        var assistantTool = CreateBindingTool("chat-summarize", surfaces: new[] { "assistant" });
        var wizardOnlyTool = CreateBindingTool("matter-pre-fill", surfaces: new[] { "wizard" });
        var allSurfacesTool = CreateBindingTool("chat-classify", surfaces: Array.Empty<string>());
        var handlerTool = AIFunctionFactory.Create(() => "x", "web_search");
        var context = new AgentToolFilterContext(
            AgentToolFilterContext.AssistantSurface,
            HasSessionFiles: false, HasActiveDocument: false, HasAnalysisBinding: false);

        var survivors = AgentToolProjection
            .PreFilter(new AIFunction[] { assistantTool, wizardOnlyTool, allSurfacesTool, handlerTool }, context)
            .Select(t => t.Name)
            .ToList();

        survivors.Should().Contain(assistantTool.Name, "sprk_surfaces declares the assistant surface");
        survivors.Should().Contain(allSurfacesTool.Name, "empty surfaces = offered on ALL surfaces (column dictionary)");
        survivors.Should().Contain("web_search", "handler tools pass through (catalog availability already applied upstream)");
        survivors.Should().NotContain(wizardOnlyTool.Name,
            "a Binding scoped to the wizard surface must not project into the assistant loop (catalog-column-driven filter, no tool-name lists)");
    }

    // ── FR-H1 grounding predicate (task 044): requires-no-attached-record ────────────────────
    // A capability whose Binding declares requires-no-attached-record is a pure predicate over the
    // threaded HasAttachedRecord session fact — removed when a record is attached, kept otherwise.
    // No model call, no tool-name list (ADR-039 §3.2 removes-the-impossible).

    [Fact]
    public void PreFilter_RequiresNoAttachedRecord_WithAttachedRecord_ExcludesCapability()
    {
        var createMatter = CreateBindingTool("create-matter", surfaces: null, requiresNoAttachedRecord: true);
        var createTodo = CreateBindingTool("create-todo", surfaces: null, requiresNoAttachedRecord: false);
        var handlerTool = AIFunctionFactory.Create(() => "x", "web_search");
        // Session hosted ON a record (e.g. the user is inside a matter).
        var context = new AgentToolFilterContext(
            AgentToolFilterContext.AssistantSurface,
            HasSessionFiles: false, HasActiveDocument: false, HasAnalysisBinding: false,
            HasAttachedRecord: true);

        var survivors = AgentToolProjection
            .PreFilter(new AIFunction[] { createMatter, createTodo, handlerTool }, context)
            .Select(t => t.Name)
            .ToList();

        survivors.Should().NotContain(createMatter.Name,
            "requires-no-attached-record + an attached record removes the capability (hide 'Create matter' inside a matter)");
        survivors.Should().Contain(createTodo.Name,
            "a capability WITHOUT the predicate is unaffected by the attached record (create-todo is regarding-based)");
        survivors.Should().Contain("web_search", "handler tools are never grounding-filtered by this predicate");
    }

    [Fact]
    public void PreFilter_RequiresNoAttachedRecord_NoAttachedRecord_KeepsCapability()
    {
        var createMatter = CreateBindingTool("create-matter", surfaces: null, requiresNoAttachedRecord: true);
        // Session NOT hosted on a record (top-level assistant — no matter/account in context).
        var context = new AgentToolFilterContext(
            AgentToolFilterContext.AssistantSurface,
            HasSessionFiles: false, HasActiveDocument: false, HasAnalysisBinding: false,
            HasAttachedRecord: false);

        var survivors = AgentToolProjection
            .PreFilter(new AIFunction[] { createMatter }, context)
            .Select(t => t.Name)
            .ToList();

        survivors.Should().Contain(createMatter.Name,
            "the precondition IS met (no attached record) — the capability is offered");
    }

    [Fact]
    public void PreFilter_RequiresNoAttachedRecord_DefaultContext_KeepsCapability()
    {
        // The 4-arg AgentToolFilterContext (no HasAttachedRecord) defaults it to false — existing
        // construction sites (no host record threaded) never grounding-filter the capability.
        var createMatter = CreateBindingTool("create-matter", surfaces: null, requiresNoAttachedRecord: true);
        var context = new AgentToolFilterContext(
            AgentToolFilterContext.AssistantSurface, false, false, false);

        AgentToolProjection.PreFilter(new AIFunction[] { createMatter }, context)
            .Select(t => t.Name)
            .Should().Contain(createMatter.Name, "default HasAttachedRecord=false ⇒ no grounding removal");
    }

    [Fact]
    public void Finalize_AnyInputOrder_ProducesOrdinalNameOrderAndBudgetWrap()
    {
        var turn = new AgentTurnContract(toolCallBudget: 8);
        var context = new AgentToolFilterContext(
            AgentToolFilterContext.AssistantSurface, false, false, false);
        var toolsShuffled = new[]
        {
            AIFunctionFactory.Create(() => "x", "zeta_tool"),
            AIFunctionFactory.Create(() => "x", "alpha_tool"),
            AIFunctionFactory.Create(() => "x", "milo_tool"),
        };

        var finalized = AgentToolProjection.Finalize(toolsShuffled, context, turn, null, TestLogger);

        finalized.Select(t => t.Name).Should().ContainInOrder("alpha_tool", "milo_tool", "zeta_tool");
        finalized.Should().AllBeOfType<BudgetedAIFunction>(
            "every projected tool is budget-wrapped so calls are counted + recorded");
    }

    [Fact]
    public void ComputeProjectionFingerprint_SameToolsAcrossTurns_IsStable_NFR04()
    {
        var turn1 = new AgentTurnContract(8);
        var turn2 = new AgentTurnContract(8);
        var context = new AgentToolFilterContext(
            AgentToolFilterContext.AssistantSurface, false, false, false);

        // Two independent projections over equivalent tool declarations — simulates two turns.
        var fp1 = AgentToolProjection.ComputeProjectionFingerprint(AgentToolProjection.Finalize(
            new[] { AIFunctionFactory.Create((string q) => "x", "b_tool"), AIFunctionFactory.Create(() => "y", "a_tool") },
            context, turn1, null, TestLogger));
        var fp2 = AgentToolProjection.ComputeProjectionFingerprint(AgentToolProjection.Finalize(
            new[] { AIFunctionFactory.Create(() => "y", "a_tool"), AIFunctionFactory.Create((string q) => "x", "b_tool") },
            context, turn2, null, TestLogger));

        fp1.Should().Be(fp2,
            "NFR-04: the projected tool block serializes identically across turns regardless of input order — prompt-cache-stable");
    }

    [Fact]
    public void ComputeProjectionFingerprint_DescriptionChanges_ChangesFingerprint()
    {
        var fp1 = AgentToolProjection.ComputeProjectionFingerprint(
            new[] { AIFunctionFactory.Create(() => "x", "a_tool", "does A") });
        var fp2 = AgentToolProjection.ComputeProjectionFingerprint(
            new[] { AIFunctionFactory.Create(() => "x", "a_tool", "does A differently") });

        fp1.Should().NotBe(fp2, "a description edit is a real projection change (cache prefix rotates)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Binding capability-tool projection (step 2 / ADR-039 loop-as-dispatcher)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildFunctionName_ConsumerType_IsDeterministicAndSanitized()
    {
        BindingCapabilityTool.BuildFunctionName("chat-summarize").Should().Be("capability_chat-summarize");
        BindingCapabilityTool.BuildFunctionName("Email Analysis!").Should().Be("capability_email_analysis_",
            "non-identifier characters map to underscores (LLM function-name charset)");
        BindingCapabilityTool.BuildFunctionName("chat-summarize")
            .Should().Be(BindingCapabilityTool.BuildFunctionName("chat-summarize"),
                "same input always yields the same name (NFR-04)");
    }

    [Fact]
    public void BindingCapabilityTool_UsesMakerToolDescriptionAndActionSchema()
    {
        var tool = CreateBindingTool(
            "chat-summarize",
            surfaces: new[] { "assistant" },
            toolDescription: "Summarize the session's uploaded documents.",
            inputSchemaJson: """{"type":"object","properties":{"fileIds":{"type":"array"}}}""");

        tool.Name.Should().Be("capability_chat-summarize");
        tool.Description.Should().Be("Summarize the session's uploaded documents.",
            "the maker-authored sprk_tooldescription IS the intent surface the model sees");
        tool.JsonSchema.GetProperty("properties").TryGetProperty("fileIds", out _).Should().BeTrue();
    }

    [Fact]
    public void BindingCapabilityTool_MalformedMakerSchema_DegradesToDefaultObjectSchema()
    {
        var tool = CreateBindingTool("chat-summarize", surfaces: null, inputSchemaJson: "{not json");

        tool.JsonSchema.ValueKind.Should().Be(JsonValueKind.Object);
        tool.JsonSchema.GetProperty("type").GetString().Should().Be("object",
            "maker data errors degrade, they never throw (routing tolerance rule)");
    }

    [Fact]
    public void BindingCapabilityTool_NoToolDescription_IsRejectedAtConstruction()
    {
        var binding = CreateBinding("chat-summarize", surfaces: null, toolDescription: null);
        var act = () => new BindingCapabilityTool(
            binding, new ServiceCollection().BuildServiceProvider(), "t1", "s1", TestLogger);

        act.Should().Throw<ArgumentException>(
            "a Binding without sprk_tooldescription has not opted into text-path projection (ADR-039 closed catalog)");
    }

    [Fact]
    public async Task BindingCapabilityTool_DispatchServiceUnavailable_ReturnsHonestUnavailableMessage()
    {
        var tool = CreateBindingTool("chat-summarize", surfaces: new[] { "assistant" });

        var result = await tool.InvokeAsync(new AIFunctionArguments());

        result!.ToString().Should().Contain("not available",
            "the loop relays honest capability state — never fabricated output (ADR-039 grounded execution)");
    }

    [Fact]
    public void SelectTextProjectable_FiltersOptInAndEnvironment_OrdersDeterministically()
    {
        var b1 = CreateBinding("zeta-capability", surfaces: null, toolDescription: "desc");
        var b2 = CreateBinding("alpha-capability", surfaces: null, toolDescription: "desc");
        var noOptIn = CreateBinding("no-tool-description", surfaces: null, toolDescription: null);
        var otherEnv = CreateBinding("prod-only", surfaces: null, toolDescription: "desc") with { Environment = "prod" };

        var selected = ConsumerRoutingService.SelectTextProjectable(
            new[] { b1, noOptIn, otherEnv, b2 }, environment: "dev");

        selected.Select(b => b.ConsumerType).Should().ContainInOrder("alpha-capability", "zeta-capability");
        selected.Should().NotContain(b => b.ConsumerType == "no-tool-description",
            "sprk_tooldescription is the explicit catalog opt-in");
        selected.Should().NotContain(b => b.ConsumerType == "prod-only");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Citation enforcement on reads (step 4 / ADR-039 grounded outputs)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_NoReadResultsConsumed_NoViolation()
    {
        var result = AgentTurnCitationEnforcer.Evaluate("Plain conversational answer.", new CitationContext());

        result.Violation.Should().BeFalse("a turn without read-tool results has nothing to cite");
        result.ReadResultsConsumed.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ReadResultsConsumedButUncited_FailsTurnContract()
    {
        var citations = new CitationContext();
        citations.AddCitation("chunk-1", "NDA.pdf", 2, "excerpt");

        var result = AgentTurnCitationEnforcer.Evaluate(
            "The agreement terminates in 2027.", citations);

        result.Violation.Should().BeTrue(
            "FR-P2-01: uncited read-derived claims fail the turn contract");
        result.CitationsAvailable.Should().Be(1);
        result.CitationMarkersInText.Should().Be(0);
    }

    [Fact]
    public void Evaluate_ReadResultsCitedWithMarkers_PassesTurnContract()
    {
        var citations = new CitationContext();
        citations.AddCitation("chunk-1", "NDA.pdf", 2, "excerpt");

        var result = AgentTurnCitationEnforcer.Evaluate(
            "The agreement terminates in 2027 [1].", citations);

        result.Violation.Should().BeFalse();
        result.CitationMarkersInText.Should().Be(1);
    }

    [Fact]
    public void BuildRepairSuffix_ListsEveryCitationIdAndSource()
    {
        var citations = new CitationContext();
        citations.AddCitation("chunk-1", "NDA.pdf", 2, "excerpt one");
        citations.AddCitation("chunk-2", "MSA.docx", null, "excerpt two");

        var suffix = AgentTurnCitationEnforcer.BuildRepairSuffix(citations);

        suffix.Should().Contain("[1] NDA.pdf").And.Contain("[2] MSA.docx");
        suffix.Should().NotContain("excerpt", "the repair block carries identifiers + names only");
    }

    [Fact]
    public async Task SendMessageAsync_UncitedReadDerivedTurn_AppendsRepairSuffixToStream()
    {
        // Arrange: agent whose tool registered a citation but whose streamed text has no [N].
        var citations = new CitationContext();
        var turn = new AgentTurnContract(8);
        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<AiChatMessage> _, ChatOptions? _, CancellationToken _) =>
            {
                citations.AddCitation("chunk-9", "Lease.pdf", 1, "excerpt"); // simulates the read tool executing
                return ToAsyncEnumerable(new[]
                {
                    new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent("Uncited claim.")] },
                });
            });

        var agent = new SprkChatAgent(
            chatClient.Object,
            new ChatContext(SystemPrompt: "sp", DocumentSummary: null, AnalysisMetadata: null, PlaybookId: null),
            tools: [],
            citationContext: citations,
            Mock.Of<ILogger<SprkChatAgent>>(),
            turn);

        // Act
        var text = new System.Text.StringBuilder();
        await foreach (var update in agent.SendMessageAsync("q", [], CancellationToken.None))
        {
            text.Append(update.Text);
        }

        // Assert — the rendered turn ALWAYS carries its grounding.
        text.ToString().Should().Contain("Sources: [1] Lease.pdf",
            "a violating turn is repaired with the deterministic sources block before the stream ends");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Turn contract surfaces through the agent + middleware pipeline
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TurnContract_ExposedByAgent_AndDelegatedThroughMiddleware()
    {
        var turn = new AgentTurnContract(8);
        ISprkChatAgent agent = new SprkChatAgent(
            Mock.Of<IChatClient>(),
            new ChatContext(SystemPrompt: "sp", DocumentSummary: null, AnalysisMetadata: null, PlaybookId: null),
            tools: [], citationContext: null,
            Mock.Of<ILogger<SprkChatAgent>>(),
            turn);

        agent.TurnContract.Should().BeSameAs(turn);

        // The endpoint reaches the contract THROUGH the middleware pipeline.
        var wrapped = new Sprk.Bff.Api.Services.Ai.Chat.Middleware.AgentTelemetryMiddleware(
            agent, Mock.Of<ILogger<SprkChatAgentFactory>>());
        wrapped.TurnContract.Should().BeSameAs(turn,
            "middleware wrappers delegate the loop contract to their inner agent");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Binding CreateBinding(
        string consumerType,
        string[]? surfaces,
        string? toolDescription = "Maker-authored intent description.",
        string? inputSchemaJson = null,
        bool requiresNoAttachedRecord = false) => new()
        {
            BindingId = Guid.NewGuid(),
            ConsumerType = consumerType,
            ActionId = Guid.NewGuid(),
            ToolDescription = toolDescription,
            Surfaces = surfaces ?? Array.Empty<string>(),
            InputSchemaJson = inputSchemaJson,
            RequiresNoAttachedRecord = requiresNoAttachedRecord,
        };

    private static BindingCapabilityTool CreateBindingTool(
        string consumerType,
        string[]? surfaces,
        string? toolDescription = "Maker-authored intent description.",
        string? inputSchemaJson = null,
        bool requiresNoAttachedRecord = false) => new(
            CreateBinding(consumerType, surfaces, toolDescription, inputSchemaJson, requiresNoAttachedRecord),
            new ServiceCollection().BuildServiceProvider(),
            "tenant-1",
            "session-1",
            TestLogger);

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    /// <summary>
    /// Recording subclass over the virtual production seams
    /// (<see cref="ChatSessionManager.GetSessionAsync"/> +
    /// <see cref="ChatSessionManager.UpdateSessionCacheAsync"/>) — the ADR-038
    /// module boundary for the ledger-persistence tests (same pattern as
    /// OutputRouterTests.RecordingChatSessionManager).
    /// </summary>
    private sealed class RecordingChatSessionManager : ChatSessionManager
    {
        private readonly ChatSession? _session;

        public RecordingChatSessionManager(ChatSession? session) : base(
            cache: Mock.Of<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>(),
            dataverseRepository: Mock.Of<IChatDataverseRepository>(),
            logger: Mock.Of<ILogger<ChatSessionManager>>(),
            persistence: null,
            cleanupSignal: null)
        {
            _session = session;
        }

        public List<ChatSession> PersistedSessions { get; } = new();

        public override Task<ChatSession?> GetSessionAsync(string tenantId, string sessionId, CancellationToken ct = default)
            => Task.FromResult(_session);

        internal override Task UpdateSessionCacheAsync(ChatSession session, CancellationToken ct = default)
        {
            PersistedSessions.Add(session);
            return Task.CompletedTask;
        }
    }
}
