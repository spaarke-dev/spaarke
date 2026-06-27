using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Token budget validation tests for <see cref="OrchestratorPromptBuilder"/>.
///
/// Verifies that the prompt builder stays within the 9,000-token budget across
/// representative scenarios covering: fresh conversations, multi-turn, varying
/// tool counts, large prompts, and near-budget stress conditions.
///
/// Post-task-141: tests use the per-playbook tool-filter contract (no
/// CapabilityManifest, no CapabilityRoutingResult). All scenarios use the
/// chars/4 heuristic consistent with the production builder.
/// </summary>
public class OrchestratorPromptBuilderBudgetTests
{
    // ── Shared helpers ────────────────────────────────────────────────────────

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

    private static OrchestratorPromptBuilder CreateSut() =>
        new(Mock.Of<ILogger<OrchestratorPromptBuilder>>());

    // ── S1: Fresh conversation ───────────────────────────────────────────────

    [Fact]
    public void S1_FreshConversation_StaysUnderBudget()
    {
        var builder = CreateSut();
        IReadOnlyList<string> tools = new[] { "SearchDocuments", "ResearchLegal", "WriteRecord" };

        var result = builder.BuildSystemPrompt(tools, MakeContext(turnCount: 0));

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "S1: fresh conversation must stay within 9000-token budget");
        result.ToolSchemaNames.Should().HaveCount(3);
    }

    // ── S2: 5-turn conversation with matter + playbook ──────────────────────

    [Fact]
    public void S2_FiveTurnConversation_StaysUnderBudget()
    {
        var builder = CreateSut();
        IReadOnlyList<string> tools = new[] { "Tool_1", "Tool_2", "Tool_3", "Tool_4", "Tool_5" };
        var context = MakeContext(
            turnCount: 5,
            matterName: "Acme v. Widgets",
            playbookName: "ContractReview");

        var result = builder.BuildSystemPrompt(tools, context);

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "S2: 5-turn conversation must stay within 9000-token budget");
        result.ToolSchemaNames.Should().HaveCount(5);
    }

    // ── S3: Max tool count (8 tools) ─────────────────────────────────────────

    [Fact]
    public void S3_MaxToolCount_StaysUnderBudget()
    {
        var builder = CreateSut();
        IReadOnlyList<string> tools = Enumerable.Range(1, OrchestratorPromptBuilder.MaxToolsPerTurn)
            .Select(i => $"Tool_{i}")
            .ToArray();

        var result = builder.BuildSystemPrompt(tools, MakeContext(playbookName: "Workflow"));

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "S3: max tool count must stay within 9000-token budget");
        result.ToolSchemaNames.Should().HaveCount(OrchestratorPromptBuilder.MaxToolsPerTurn);
    }

    // ── S4: Empty tool list ──────────────────────────────────────────────────

    [Fact]
    public void S4_NoTools_StaysUnderBudget()
    {
        var builder = CreateSut();

        var result = builder.BuildSystemPrompt(Array.Empty<string>(), MakeContext());

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "S4: standalone conversational chat (no tools) must stay within budget");
        result.PerTurnSuffix.Should().BeEmpty();
    }

    // ── S5: Over-cap tool list still capped + within budget ─────────────────

    [Fact]
    public void S5_OverCapToolList_CapsAtMaxAndStaysUnderBudget()
    {
        var builder = CreateSut();
        IReadOnlyList<string> tools = Enumerable.Range(1, 20).Select(i => $"Tool_{i}").ToArray();

        var result = builder.BuildSystemPrompt(tools, MakeContext());

        result.ToolSchemaNames.Should().HaveCountLessThanOrEqualTo(OrchestratorPromptBuilder.MaxToolsPerTurn);
        result.EstimatedTokens.Should().BeLessThanOrEqualTo(OrchestratorPromptBuilder.TotalTokenBudget);
    }

    // ── S6: Long playbook name ───────────────────────────────────────────────

    [Fact]
    public void S6_LongPlaybookName_StaysUnderBudget()
    {
        var builder = CreateSut();
        IReadOnlyList<string> tools = new[] { "Tool_1", "Tool_2" };
        var context = MakeContext(
            playbookName: "A_Very_Long_Playbook_Name_That_Goes_On_And_On_To_Stress_The_Persona_Section_Length_Limit");

        var result = builder.BuildSystemPrompt(tools, context);

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(OrchestratorPromptBuilder.TotalTokenBudget);
    }

    // ── S7: Long matter name ────────────────────────────────────────────────

    [Fact]
    public void S7_LongMatterName_StaysUnderBudget()
    {
        var builder = CreateSut();
        IReadOnlyList<string> tools = new[] { "Tool_1" };
        var matterName = new string('M', 200);
        var context = MakeContext(matterName: matterName);

        var result = builder.BuildSystemPrompt(tools, context);

        result.EstimatedTokens.Should().BeLessThanOrEqualTo(OrchestratorPromptBuilder.TotalTokenBudget);
    }

    // ── S8: Suffix tokens within suffix budget ──────────────────────────────

    [Fact]
    public void S8_SuffixTokens_WithinSuffixBudget()
    {
        var builder = CreateSut();
        IReadOnlyList<string> tools = Enumerable.Range(1, OrchestratorPromptBuilder.MaxToolsPerTurn)
            .Select(i => $"Tool_{i}")
            .ToArray();

        var result = builder.BuildSystemPrompt(tools, MakeContext());

        var suffixTokens = OrchestratorPromptBuilder.EstimateTokens(result.PerTurnSuffix);
        suffixTokens.Should().BeLessThanOrEqualTo(OrchestratorPromptBuilder.MaxToolSchemasTokens);
    }
}
