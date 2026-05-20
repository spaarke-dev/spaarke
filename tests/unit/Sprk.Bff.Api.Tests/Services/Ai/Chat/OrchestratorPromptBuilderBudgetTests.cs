using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;
using FluentAssertions;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Token budget validation tests for <see cref="OrchestratorPromptBuilder"/>.
///
/// Verifies that the prompt builder stays within the 9,000-token budget across
/// 10 representative scenarios covering: fresh conversations, multi-turn,
/// post-summarization, varying tool counts, broad mode, large prompts, and
/// near-budget stress conditions.
///
/// All scenarios use the chars/4 heuristic consistent with the production builder.
/// </summary>
public class OrchestratorPromptBuilderBudgetTests
{
    // ── Shared helpers ────────────────────────────────────────────────────────

    private static CapabilityManifestEntry MakeCapability(
        string name,
        string description = "A test capability for legal operations",
        string[]? toolNames = null) =>
        new(
            CapabilityName: name,
            Description: description,
            KeywordHints: ["hint"],
            PlaybookId: null,
            ToolNames: toolNames ?? [$"Tool_{name}"],
            IsEnabled: true,
            TenantRestrictions: []);

    private static OrchestratorPromptContext MakeContext(
        string tenantId = "tenant-abc",
        string user = "Jane Smith",
        string? matterName = null,
        int turnCount = 0,
        string? playbookName = null) =>
        new(UserDisplayName: user,
            TenantId: tenantId,
            MatterName: matterName,
            ConversationTurnCount: turnCount,
            ActivePlaybookName: playbookName);

    private static CapabilityRoutingResult ConfidentRouting(params string[] capabilityNames) =>
        CapabilityRoutingResult.Confident(
            selectedCapabilities: capabilityNames,
            confidence: 0.95,
            layer: 1,
            latencyMs: 12);

    private static CapabilityRoutingResult BroadRouting() =>
        CapabilityRoutingResult.Fallback([], selectedToolNames: [], latencyMs: 0);

    private static (Mock<ICapabilityManifest> Mock, OrchestratorPromptBuilder Builder) CreateSut(
        IReadOnlyList<CapabilityManifestEntry>? capabilities = null,
        DateTimeOffset? lastRefreshed = null)
    {
        capabilities ??= [];
        lastRefreshed ??= new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var mockManifest = new Mock<ICapabilityManifest>();
        mockManifest.Setup(m => m.GetAll()).Returns(capabilities);
        mockManifest.Setup(m => m.LastRefreshedUtc).Returns(lastRefreshed.Value);
        mockManifest
            .Setup(m => m.TryGet(It.IsAny<string>(), out It.Ref<CapabilityManifestEntry?>.IsAny))
            .Returns((string name, out CapabilityManifestEntry? entry) =>
            {
                entry = capabilities.FirstOrDefault(
                    c => string.Equals(c.CapabilityName, name, StringComparison.OrdinalIgnoreCase));
                return entry is not null;
            });

        var logger = Mock.Of<ILogger<OrchestratorPromptBuilder>>();
        var builder = new OrchestratorPromptBuilder(mockManifest.Object, logger);
        return (mockManifest, builder);
    }

    // ── S1: Fresh conversation ───────────────────────────────────────────────

    [Fact]
    public void S1_FreshConversation_StaysUnderBudget()
    {
        // 3 capabilities, 3 tools, turn 0, no matter, no playbook
        var caps = new[]
        {
            MakeCapability("search_docs", toolNames: ["SearchDocuments"]),
            MakeCapability("legal_research", toolNames: ["ResearchLegal"]),
            MakeCapability("write_record", toolNames: ["WriteRecord"]),
        };
        var (_, builder) = CreateSut(caps);
        var routing = ConfidentRouting("search_docs", "legal_research", "write_record");
        var context = MakeContext(turnCount: 0);

        var result = builder.BuildSystemPrompt(routing, context);

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "S1: fresh conversation must stay within 9000-token budget");
        result.ToolSchemaNames.Should().HaveCount(3);
    }

    // ── S2: 5-turn conversation ──────────────────────────────────────────────

    [Fact]
    public void S2_FiveTurnConversation_StaysUnderBudget()
    {
        // 5 capabilities, 5 tools, turn 5, with matter + playbook
        var caps = Enumerable.Range(1, 5)
            .Select(i => MakeCapability($"cap{i}", $"Legal capability number {i} for document analysis"))
            .ToList();
        var (_, builder) = CreateSut(caps);
        var routing = ConfidentRouting(caps.Select(c => c.CapabilityName).ToArray());
        var context = MakeContext(
            turnCount: 5,
            matterName: "Acme v. Widgets",
            playbookName: "Contract Review");

        var result = builder.BuildSystemPrompt(routing, context);

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "S2: 5-turn conversation with entity context must stay within budget");
    }

    // ── S3: 10-turn conversation ─────────────────────────────────────────────

    [Fact]
    public void S3_TenTurnConversation_StaysUnderBudget()
    {
        // 8 capabilities, 8 tools (at cap), turn 10, with matter + playbook
        var caps = Enumerable.Range(1, 8)
            .Select(i => MakeCapability($"cap{i}",
                $"Extended legal capability {i} for comprehensive document and matter analysis"))
            .ToList();
        var (_, builder) = CreateSut(caps);
        var routing = ConfidentRouting(caps.Select(c => c.CapabilityName).ToArray());
        var context = MakeContext(
            turnCount: 10,
            matterName: "GlobalCorp Acquisition",
            playbookName: "Due Diligence");

        var result = builder.BuildSystemPrompt(routing, context);

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "S3: 10-turn conversation at tool cap must stay within budget");
        result.ToolSchemaNames.Count.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.MaxToolsPerTurn);
    }

    // ── S4: Post-summarization ───────────────────────────────────────────────

    [Fact]
    public void S4_PostSummarization_StaysUnderBudget()
    {
        // 5 capabilities, turn 26 (after 25-message summarization threshold)
        var caps = Enumerable.Range(1, 5)
            .Select(i => MakeCapability($"cap{i}"))
            .ToList();
        var (_, builder) = CreateSut(caps);
        var routing = ConfidentRouting(caps.Select(c => c.CapabilityName).ToArray());
        var context = MakeContext(
            turnCount: 26,
            matterName: "Estate of Johnson",
            playbookName: "Estate Planning");

        var result = builder.BuildSystemPrompt(routing, context);

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "S4: post-summarization prompt must stay within budget");
    }

    // ── S5: 3 tools only (confident routing from 10 caps) ────────────────────

    [Fact]
    public void S5_ThreeToolsFromTenCapabilities_StaysUnderBudget()
    {
        // 10 capabilities, but routing selects only 3
        var caps = Enumerable.Range(1, 10)
            .Select(i => MakeCapability($"cap{i}", $"Description for legal capability {i}"))
            .ToList();
        var (_, builder) = CreateSut(caps);
        var routing = ConfidentRouting("cap1", "cap2", "cap3");
        var context = MakeContext(turnCount: 2);

        var result = builder.BuildSystemPrompt(routing, context);

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "S5: confident 3-tool routing from 10 caps must stay within budget");
        result.ToolSchemaNames.Should().HaveCount(3);
    }

    // ── S6: All tools (broad mode) ───────────────────────────────────────────

    [Fact]
    public void S6_BroadModeFifteenCapabilities_StaysUnderBudget()
    {
        // 15 capabilities, broad fallback routing, all tools up to MaxToolsPerTurn
        var caps = Enumerable.Range(1, 15)
            .Select(i => MakeCapability($"cap{i}", toolNames: [$"Tool{i}"]))
            .ToList();
        var (_, builder) = CreateSut(caps);
        var routing = BroadRouting();
        var context = MakeContext(turnCount: 0);

        var result = builder.BuildSystemPrompt(routing, context);

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "S6: broad mode with 15 caps capped to 8 tools must stay within budget");
        result.ToolSchemaNames.Count.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.MaxToolsPerTurn);
    }

    // ── S7: Large system prompt ──────────────────────────────────────────────

    [Fact]
    public void S7_LargeSystemPromptTwentyCapabilities_StaysUnderBudget()
    {
        // 20 capabilities with long descriptions, broad mode, max tools, matter + playbook
        var caps = Enumerable.Range(1, 20)
            .Select(i => MakeCapability($"cap{i}",
                $"This is a comprehensive description for legal capability number {i} " +
                $"covering document analysis, matter management, and AI-assisted drafting"))
            .ToList();
        var (_, builder) = CreateSut(caps);
        var routing = BroadRouting();
        var context = MakeContext(
            turnCount: 0,
            matterName: "Complex Multi-Party Litigation",
            playbookName: "Litigation Management");

        var result = builder.BuildSystemPrompt(routing, context);

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "S7: large prompt with 20 capabilities must stay within budget");
    }

    // ── S8: Long user message (builder unaffected) ───────────────────────────

    [Fact]
    public void S8_BuilderBudgetUnaffectedByUserMessage()
    {
        // The builder only controls prefix + suffix; user message is caller-managed.
        // Verify the builder's output is independent of user message length.
        var caps = new[]
        {
            MakeCapability("search", toolNames: ["Search"]),
            MakeCapability("draft", toolNames: ["Draft"]),
            MakeCapability("review", toolNames: ["Review"]),
        };
        var (_, builder) = CreateSut(caps);
        var routing = ConfidentRouting("search", "draft", "review");
        var context = MakeContext(turnCount: 3);

        var result = builder.BuildSystemPrompt(routing, context);

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "S8: builder budget is self-contained and independent of user message");

        // The system prompt should leave substantial room for user messages.
        var residual = OrchestratorPromptBuilder.TotalTokenBudget - result.EstimatedTokens;
        residual.Should().BeGreaterThan(3000,
            because: "builder output should leave at least 3000 tokens for history + user message");
    }

    // ── S9: Document context (entity enrichment active) ──────────────────────

    [Fact]
    public void S9_DocumentContextWithEnrichment_StaysUnderBudget()
    {
        var caps = Enumerable.Range(1, 5)
            .Select(i => MakeCapability($"cap{i}"))
            .ToList();
        var (_, builder) = CreateSut(caps);
        var routing = ConfidentRouting(caps.Select(c => c.CapabilityName).ToArray());
        var context = MakeContext(
            turnCount: 1,
            matterName: "Patent Review 2026-XYZ",
            playbookName: "IP Review");

        var result = builder.BuildSystemPrompt(routing, context);

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "S9: document context with entity enrichment must stay within budget");

        // Verify entity enrichment is present.
        result.SystemPromptPrefix.Should().Contain("Patent Review 2026-XYZ");
    }

    // ── S10: Near-budget stress test ─────────────────────────────────────────

    [Fact]
    public void S10_NearBudgetStressTest_StaysUnderBudget()
    {
        // Stress: 20 capabilities with maximum-length descriptions, 8 tools,
        // very long matter name, very long playbook name.
        var caps = Enumerable.Range(1, 20)
            .Select(i => MakeCapability(
                $"extended_capability_{i}_with_long_name",
                // ~120 chars (near contract max)
                $"This is a very detailed description for capability {i} covering advanced legal operations " +
                $"including multi-jurisdictional compliance, regulatory analysis, and cross-border transactions",
                toolNames: [$"ExtendedTool_{i}_LongName"]))
            .ToList();
        var (_, builder) = CreateSut(caps);
        var routing = BroadRouting();
        var context = MakeContext(
            turnCount: 0,
            matterName: "Very Long Matter Name That Pushes Entity Enrichment Section to Maximum Length for Stress Testing Purposes",
            playbookName: "Advanced Contract Lifecycle Management System with Extended Workflow Capabilities");

        var result = builder.BuildSystemPrompt(routing, context);

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "S10: near-budget stress test must stay within 9000-token budget " +
                     "(trim logic should fire if needed)");
    }

    // ── Budget overflow triggers trim logic ──────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_TrimsAndStaysUnderBudget_WhenOverflowOccurs()
    {
        // Construct a scenario that would exceed budget without trim logic.
        // Use 30 capabilities with very long descriptions to push prefix high,
        // plus broad routing to maximize suffix.
        var caps = Enumerable.Range(1, 30)
            .Select(i => MakeCapability(
                $"capability_{i}",
                new string('X', 120), // max description length
                toolNames: [$"Tool_{i}"]))
            .ToList();
        var (_, builder) = CreateSut(caps);
        var routing = BroadRouting();
        var context = MakeContext(
            matterName: new string('M', 200),
            playbookName: new string('P', 200));

        var result = builder.BuildSystemPrompt(routing, context);

        // Even if overflow was triggered, the final result must be within budget.
        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "trim logic must bring the prompt back under budget after overflow");
    }

    // ── Residual budget is always sufficient ─────────────────────────────────

    [Theory]
    [InlineData(3, false, null, null)]
    [InlineData(8, false, "Matter X", "Playbook Y")]
    [InlineData(8, true, "Long Matter Name for Comprehensive Testing", "Advanced Due Diligence Workflow")]
    public void BuildSystemPrompt_LeavesAdequateResidualBudget(
        int toolCount, bool broadMode, string? matterName, string? playbookName)
    {
        var caps = Enumerable.Range(1, Math.Max(toolCount, 10))
            .Select(i => MakeCapability($"cap{i}", toolNames: [$"Tool{i}"]))
            .ToList();
        var (_, builder) = CreateSut(caps);

        var routing = broadMode
            ? BroadRouting()
            : ConfidentRouting(caps.Take(toolCount).Select(c => c.CapabilityName).ToArray());

        var context = MakeContext(
            matterName: matterName,
            playbookName: playbookName);

        var result = builder.BuildSystemPrompt(routing, context);

        var residual = OrchestratorPromptBuilder.TotalTokenBudget - result.EstimatedTokens;
        residual.Should().BeGreaterThan(2000,
            because: "the builder must leave at least 2000 tokens for conversation history and user message");
    }
}
