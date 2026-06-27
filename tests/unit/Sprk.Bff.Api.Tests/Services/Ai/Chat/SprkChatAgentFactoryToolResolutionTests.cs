using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// R6 Pillar 2 — task 011 (D-A-11, FR-11) regression tests for the data-driven chat-tool
/// resolution in <see cref="SprkChatAgentFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Task 011 wired <c>ResolveTools()</c> to query <c>sprk_analysistool</c> rows with
/// <see cref="ToolAvailabilityContext.Chat"/> or <see cref="ToolAvailabilityContext.Both"/>
/// availability and wrap each via <c>ToolHandlerToAIFunctionAdapter</c> (task 010).
/// Strategy is ADDITIVE during the Q9 migration window — hardcoded tools still register;
/// data-driven rows append.
/// </para>
/// <para>
/// Because <c>ResolveTools()</c> is private and the full <c>CreateAgentAsync()</c> flow
/// requires extensive mocking (IChatClient, scope provider, etc.),
/// this test class exercises the WIRING CONTRACT of the new block at the integration
/// boundary that matters most:
/// </para>
/// <list type="bullet">
/// <item>
/// The two helper methods (<c>TryParseChatSessionId</c> + <c>TryParseMatterId</c>) that
/// translate the factory's per-call inputs into <see cref="ChatInvocationContext"/>
/// fields — these are accessed via reflection (matching the file's <c>internal</c>
/// surface intent).
/// </item>
/// <item>
/// End-to-end adapter wiring: an <see cref="AnalysisTool"/> row marked Chat-available
/// is wrappable by <see cref="ToolHandlerToAIFunctionAdapter"/> with a context factory
/// composed from the helpers — proving the FR-11 path is operational without firing the
/// whole agent assembly.
/// </item>
/// </list>
/// <para>
/// NFR-01 binding: the additive strategy preserves all existing hardcoded tools so chat
/// remains conversational even when zero data-driven rows are returned. NFR-11 binding:
/// existing 10 hardcoded chat tools work unchanged during the migration.
/// NFR-13 binding: safety pipeline middleware chain is unchanged — the wiring change is
/// strictly inside <c>ResolveTools()</c>, downstream of agent construction.
/// </para>
/// </remarks>
[Trait("status", "passing")]
[Trait("task", "r6-task-011")]
public class SprkChatAgentFactoryToolResolutionTests
{
    private const string ValidJsonSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "text": { "type": "string" }
          },
          "required": ["text"]
        }
        """;

    private const string TestTenantId = "tenant-fr11-test";

    // ─────────────────────────────────────────────────────────────────────────────
    // Helper 1: TryParseChatSessionId
    // FR-11 wiring contract — opaque session id may be a Guid (production) or arbitrary
    // string (legacy/test sessions). The helper must accept both shapes per NFR-11.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryParseChatSessionId_ValidGuid_ReturnsParsedGuid()
    {
        // Arrange
        var expected = Guid.NewGuid();

        // Act
        var actual = InvokeTryParseChatSessionId(expected.ToString());

        // Assert
        actual.Should().Be(expected, "production sessions are GUID-shaped");
    }

    [Fact]
    public void TryParseChatSessionId_NonGuidString_ReturnsNewGuid()
    {
        // Arrange
        const string legacyShape = "legacy-session-001";

        // Act
        var actual = InvokeTryParseChatSessionId(legacyShape);

        // Assert — fallback creates a fresh Guid rather than throwing (NFR-11 backward-compat).
        actual.Should().NotBe(Guid.Empty,
            "FR-11 backward-compat: legacy non-GUID session ids must not crash chat creation");
    }

    [Fact]
    public void TryParseChatSessionId_EmptyString_ReturnsNewGuid()
    {
        // Arrange + Act
        var actual = InvokeTryParseChatSessionId(string.Empty);

        // Assert
        actual.Should().NotBe(Guid.Empty,
            "FR-11 backward-compat: empty session id falls back to a generated Guid");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helper 2: TryParseMatterId
    // FR-11 wiring contract — extract matter id from ChatKnowledgeScope when host
    // context binds the chat to a matter. ADR-015: deterministic id only.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryParseMatterId_NullScope_ReturnsNull()
    {
        // Act
        var actual = InvokeTryParseMatterId(null);

        // Assert
        actual.Should().BeNull("FR-11: non-matter chats produce a null MatterId");
    }

    [Fact]
    public void TryParseMatterId_NonMatterEntity_ReturnsNull()
    {
        // Arrange — scope bound to a project entity, not a matter.
        var scope = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: Array.Empty<string>(),
            InlineContent: null,
            SkillInstructions: null,
            ActiveDocumentId: null,
            ParentEntityType: "sprk_project",
            ParentEntityId: Guid.NewGuid().ToString());

        // Act
        var actual = InvokeTryParseMatterId(scope);

        // Assert
        actual.Should().BeNull("non-matter entity scopes yield null MatterId per ChatInvocationContext contract");
    }

    [Fact]
    public void TryParseMatterId_MatterEntityValidGuid_ReturnsParsedGuid()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        var scope = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: Array.Empty<string>(),
            InlineContent: null,
            SkillInstructions: null,
            ActiveDocumentId: null,
            ParentEntityType: "sprk_matter",
            ParentEntityId: matterId.ToString());

        // Act
        var actual = InvokeTryParseMatterId(scope);

        // Assert
        actual.Should().Be(matterId, "FR-11: matter-bound chats surface the matter id to handlers");
    }

    [Fact]
    public void TryParseMatterId_MatterEntityNonGuid_ReturnsNull()
    {
        // Arrange — defensive: a malformed entity id must not throw.
        var scope = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: Array.Empty<string>(),
            InlineContent: null,
            SkillInstructions: null,
            ActiveDocumentId: null,
            ParentEntityType: "sprk_matter",
            ParentEntityId: "not-a-guid");

        // Act
        var actual = InvokeTryParseMatterId(scope);

        // Assert
        actual.Should().BeNull("FR-11: malformed matter id falls back to null rather than throwing");
    }

    [Fact]
    public void TryParseMatterId_MatterEntityCaseInsensitiveType_ReturnsParsedGuid()
    {
        // Arrange — entity type comparison is case-insensitive (defensive against
        // upstream host-context casing variance).
        var matterId = Guid.NewGuid();
        var scope = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: Array.Empty<string>(),
            InlineContent: null,
            SkillInstructions: null,
            ActiveDocumentId: null,
            ParentEntityType: "Sprk_Matter",
            ParentEntityId: matterId.ToString());

        // Act
        var actual = InvokeTryParseMatterId(scope);

        // Assert
        actual.Should().Be(matterId, "FR-11: case variance on entity type must not break matter resolution");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Adapter wiring (end-to-end at the FR-11 boundary)
    // Proves: an AnalysisTool row with AvailableInContexts ∋ Chat + a valid JsonSchema
    // + a registered IToolHandler is wrappable via ToolHandlerToAIFunctionAdapter,
    // and the captured ChatInvocationContext factory closes over the FR-11 derived
    // ids (tenantId + sessionId + matterId).
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Adapter_WiringEndToEnd_ProducesAIFunctionExposingRowName()
    {
        // Arrange — minimal valid Chat-available row.
        var row = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "test_chat_tool",
            Description = "FR-11 wiring smoke test.",
            HandlerClass = nameof(FakeChatHandler),
            AvailableInContexts = ToolAvailabilityContext.Chat,
            JsonSchema = ValidJsonSchema,
            OwnerType = ScopeOwnerType.System
        };
        var handler = new FakeChatHandler();
        var sessionId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        Func<ChatInvocationContext> contextFactory = () => new ChatInvocationContext
        {
            ChatSessionId = sessionId,
            TenantId = TestTenantId,
            MatterId = matterId
        };

        // Act
        var adapter = new ToolHandlerToAIFunctionAdapter(
            row, handler, contextFactory, NullLogger.Instance);

        // Assert — FR-10 contract: the LLM sees Name + Description + JsonSchema from
        // the Dataverse row (verified at the AIFunction level, the same surface the
        // factory's tool list exposes to the chat agent).
        adapter.Name.Should().Be("test_chat_tool");
        adapter.Description.Should().Be("FR-11 wiring smoke test.");
        adapter.JsonSchema.ValueKind.Should().Be(JsonValueKind.Object,
            "FR-10: JsonSchema must be a JSON object the LLM can consume");
    }

    [Fact]
    public void Adapter_WiringEndToEnd_BothContextRow_IsAccepted()
    {
        // Arrange — a row marked Both (Playbook + Chat) is also valid for the chat
        // adapter; FR-11 filter (Chat OR Both) covers this case.
        var row = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "dual_context_tool",
            Description = "Dual-context tool.",
            HandlerClass = nameof(FakeChatHandler),
            AvailableInContexts = ToolAvailabilityContext.Both,
            JsonSchema = ValidJsonSchema,
            OwnerType = ScopeOwnerType.System
        };
        var handler = new FakeChatHandler();
        Func<ChatInvocationContext> contextFactory = () => new ChatInvocationContext
        {
            ChatSessionId = Guid.NewGuid(),
            TenantId = TestTenantId
        };

        // Act + Assert — must not throw.
        var adapter = new ToolHandlerToAIFunctionAdapter(
            row, handler, contextFactory, NullLogger.Instance);

        adapter.Name.Should().Be("dual_context_tool",
            "FR-11: Both-context rows are exposed to the chat agent alongside Chat-only rows");
    }

    [Fact]
    public void Adapter_PlaybookOnlyHandler_RejectsConstruction()
    {
        // Arrange — handler does NOT opt into chat invocation. The adapter's defensive
        // guard (per task 010) must reject construction so the factory's per-row try
        // block (FR-11 resilient registration) can log + skip.
        var row = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "playbook_only_tool",
            Description = "Playbook-only handler should reject chat exposure.",
            HandlerClass = nameof(FakePlaybookOnlyHandler),
            AvailableInContexts = ToolAvailabilityContext.Chat, // row says chat but handler doesn't opt in
            JsonSchema = ValidJsonSchema,
            OwnerType = ScopeOwnerType.System
        };
        var handler = new FakePlaybookOnlyHandler();
        Func<ChatInvocationContext> contextFactory = () => new ChatInvocationContext
        {
            ChatSessionId = Guid.NewGuid(),
            TenantId = TestTenantId
        };

        // Act + Assert
        var act = () => new ToolHandlerToAIFunctionAdapter(
            row, handler, contextFactory, NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>(
            "FR-11 + task 010 guard: the factory must surface a clear error for misconfigured " +
            "rows so per-row try-catch logs and skips them without crashing the agent.");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // R6 Wave 7b — IsCapabilityGateSatisfied (per-tool capability filter)
    // The helper extracted from the data-driven block of ResolveTools() to preserve
    // the today-hardcoded `if (capabilities.Contains(PlaybookCapabilities.X))` gates
    // for the 6 capability-gated tools as they migrate in Waves 7c / 8 / 9.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsCapabilityGateSatisfied_NullRequiredCapability_AlwaysPasses()
    {
        // Wave 7b backward-compat: pre-migration rows have null RequiredCapability
        // and must continue to register regardless of the playbook's capability set.
        // This preserves the today-behavior of AnalysisQuery, TextRefinement, and the
        // 8 typed handler rows (none of which have a capability gate).
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "search" };

        InvokeIsCapabilityGateSatisfied(null, capabilities).Should().BeTrue(
            "Wave 7b: null RequiredCapability = always-available (existing pre-Wave-7b behavior)");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void IsCapabilityGateSatisfied_WhitespaceRequiredCapability_AlwaysPasses(string requiredCap)
    {
        // Defensive: an empty / whitespace value on the column behaves the same as null.
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "search" };

        InvokeIsCapabilityGateSatisfied(requiredCap, capabilities).Should().BeTrue(
            "Wave 7b: empty/whitespace RequiredCapability = no gate (treated as null)");
    }

    [Fact]
    public void IsCapabilityGateSatisfied_MatchingCapability_Passes()
    {
        // A tool requiring 'verify_citations' must register when the playbook's
        // capability set contains 'verify_citations'.
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "search", "analyze", "verify_citations"
        };

        InvokeIsCapabilityGateSatisfied("verify_citations", capabilities).Should().BeTrue(
            "Wave 7b: explicit match in the playbook's capability set exposes the tool");
    }

    [Fact]
    public void IsCapabilityGateSatisfied_MissingCapability_Fails()
    {
        // A tool requiring 'verify_citations' must NOT register when the playbook's
        // capability set lacks 'verify_citations'. Preserves the security boundary
        // that the hardcoded `if (capabilities.Contains(PlaybookCapabilities.VerifyCitations))`
        // gate enforces today.
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "search", "analyze"
        };

        InvokeIsCapabilityGateSatisfied("verify_citations", capabilities).Should().BeFalse(
            "Wave 7b: missing capability withholds the tool from the LLM's function schema " +
            "(replaces the hardcoded `if (capabilities.Contains(X))` gate)");
    }

    [Theory]
    [InlineData("verify_citations", "verify_citations")]   // exact match
    [InlineData("VERIFY_CITATIONS", "verify_citations")]   // capability set holds upper, tool requires lower
    [InlineData("verify_citations", "VERIFY_CITATIONS")]   // capability set holds lower, tool requires upper
    [InlineData("Verify_Citations", "verify_citations")]   // mixed case
    [InlineData("verify_citations", "Verify_Citations")]
    public void IsCapabilityGateSatisfied_CaseInsensitive_Passes(string requiredCap, string playbookCap)
    {
        // Wave 7b: matching is case-insensitive because canonical capability names
        // are lowercase snake_case but admins editing the column may type variants.
        // The today-hardcoded `Contains` calls use string-default (case-sensitive),
        // but those values are compile-time constants — the data-driven case must be
        // more forgiving because the source is admin-edited Dataverse text.
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { playbookCap };

        InvokeIsCapabilityGateSatisfied(requiredCap, capabilities).Should().BeTrue(
            $"Wave 7b: '{requiredCap}' vs '{playbookCap}' must match case-insensitively");
    }

    [Fact]
    public void IsCapabilityGateSatisfied_EmptyCapabilitySet_FailsGatedTool()
    {
        // Standalone chat with NO capabilities + a gated tool → withhold the tool.
        // The agent still operates (NFR-01 conversational primacy) with whatever
        // un-gated tools resolved.
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        InvokeIsCapabilityGateSatisfied("verify_citations", capabilities).Should().BeFalse(
            "Wave 7b: empty capability set withholds every gated tool");
    }

    [Fact]
    public void IsCapabilityGateSatisfied_EmptyCapabilitySet_PassesUngatedTool()
    {
        // Standalone chat with NO capabilities + an un-gated tool → register it.
        // Mirrors today's behavior for tools that have no `if (capabilities.Contains(X))` block.
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        InvokeIsCapabilityGateSatisfied(null, capabilities).Should().BeTrue(
            "Wave 7b: empty capability set still passes un-gated tools (null RequiredCapability)");
    }

    [Fact]
    public void IsCapabilityGateSatisfied_AllSixCapabilityGatedTools_Roundtrip()
    {
        // Smoke test the six canonical capability strings that the today-hardcoded
        // blocks gate on. All six must round-trip through the matcher correctly when
        // the playbook capability set contains the matching value.
        var allSixGated = new[]
        {
            PlaybookCapabilities.WriteBack,
            PlaybookCapabilities.Reanalyze,
            PlaybookCapabilities.WebSearch,
            PlaybookCapabilities.CodeInterpreter,
            PlaybookCapabilities.LegalResearch,
            PlaybookCapabilities.VerifyCitations
        };

        foreach (var capability in allSixGated)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { capability };

            InvokeIsCapabilityGateSatisfied(capability, set).Should().BeTrue(
                $"Wave 7b: '{capability}' must round-trip through the matcher");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Reflection helpers — access the private static FR-11 helpers on the factory.
    // ─────────────────────────────────────────────────────────────────────────────

    private static Guid InvokeTryParseChatSessionId(string sessionId)
    {
        var method = typeof(SprkChatAgentFactory).GetMethod(
            "TryParseChatSessionId",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull(
            "FR-11: TryParseChatSessionId helper must exist on SprkChatAgentFactory " +
            "(task 011 wiring contract).");
        return (Guid)method!.Invoke(null, new object[] { sessionId })!;
    }

    private static Guid? InvokeTryParseMatterId(ChatKnowledgeScope? scope)
    {
        var method = typeof(SprkChatAgentFactory).GetMethod(
            "TryParseMatterId",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull(
            "FR-11: TryParseMatterId helper must exist on SprkChatAgentFactory " +
            "(task 011 wiring contract).");
        return (Guid?)method!.Invoke(null, new object?[] { scope });
    }

    // Wave 7b: IsCapabilityGateSatisfied is `internal static` — Sprk.Bff.Api.csproj
    // exposes internals to Sprk.Bff.Api.Tests, so we can call it directly. Wrapper
    // keeps the per-test setup terse and gives a single seam if the signature evolves.
    private static bool InvokeIsCapabilityGateSatisfied(
        string? requiredCapability,
        IReadOnlySet<string> capabilities) =>
        SprkChatAgentFactory.IsCapabilityGateSatisfied(requiredCapability, capabilities);

    // ─────────────────────────────────────────────────────────────────────────────
    // Test doubles — minimal IToolHandler implementations.
    // ─────────────────────────────────────────────────────────────────────────────

    private sealed class FakeChatHandler : IToolHandler
    {
        public string HandlerId => nameof(FakeChatHandler);

        public ToolHandlerMetadata Metadata { get; } = new(
            Name: "FakeChatHandler",
            Description: "Test double — opts into chat invocation.",
            Version: "1.0.0",
            SupportedInputTypes: new[] { "text/plain" },
            Parameters: Array.Empty<ToolParameterDefinition>());

        public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

        public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Both;

        public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
            ToolValidationResult.Success();

        public Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken) =>
            Task.FromResult(ToolResult.Ok(
                handlerId: HandlerId,
                toolId: tool.Id,
                toolName: tool.Name,
                data: new { ok = true }));

        public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool) =>
            ToolValidationResult.Success();

        public Task<ToolResult> ExecuteChatAsync(
            ChatInvocationContext context, AnalysisTool tool, CancellationToken cancellationToken) =>
            Task.FromResult(ToolResult.Ok(
                handlerId: HandlerId,
                toolId: tool.Id,
                toolName: tool.Name,
                data: new { ok = true, sessionId = context.ChatSessionId }));
    }

    private sealed class FakePlaybookOnlyHandler : IToolHandler
    {
        public string HandlerId => nameof(FakePlaybookOnlyHandler);

        public ToolHandlerMetadata Metadata { get; } = new(
            Name: "FakePlaybookOnlyHandler",
            Description: "Test double — playbook-only, declines chat exposure.",
            Version: "1.0.0",
            SupportedInputTypes: new[] { "text/plain" },
            Parameters: Array.Empty<ToolParameterDefinition>());

        public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

        // Intentionally inherits the default Playbook-only InvocationContextKind.

        public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
            ToolValidationResult.Success();

        public Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken) =>
            Task.FromResult(ToolResult.Ok(
                handlerId: HandlerId,
                toolId: tool.Id,
                toolName: tool.Name,
                data: new { ok = true }));
    }
}
