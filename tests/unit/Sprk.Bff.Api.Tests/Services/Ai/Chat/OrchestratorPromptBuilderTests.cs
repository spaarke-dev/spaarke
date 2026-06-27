using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="OrchestratorPromptBuilder"/>.
///
/// Post-task-141 (FR-22 + FR-23): the prompt builder takes a per-turn list of active tool
/// names (derived from per-playbook tool filtering) instead of a CapabilityRoutingResult.
/// The capability index section is gone; the "Active Tools" section is driven from the
/// passed list.
///
/// Acceptance criteria:
///   - Prefix is byte-identical across two calls with the same playbook context.
///   - Prefix cache key varies per playbook so each playbook context has its own cached prefix.
///   - Total prompt stays within the 9000-token budget for a reasonable tool list.
///   - Per-turn suffix contains only the tool names passed in.
///   - MaxToolsPerTurn cap enforced; never more than 8 tool schemas in one prompt.
///   - Empty tool list → no "Active Tools" section.
/// </summary>
[Trait("status", "repaired")]
public class OrchestratorPromptBuilderTests
{
    // ── Shared helpers ────────────────────────────────────────────────────────

    private static OrchestratorPromptContext DefaultContext(
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

    // ── Prefix stability (caching) ────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_PrefixIsIdentical_AcrossTwoCallsWithSameContext()
    {
        // Arrange
        var builder = CreateSut();
        var context = DefaultContext(playbookName: "ContractReview");
        IReadOnlyList<string> tools = new[] { "Tool_A", "Tool_B" };

        // Act — two consecutive calls with identical playbook context.
        var result1 = builder.BuildSystemPrompt(tools, context);
        var result2 = builder.BuildSystemPrompt(tools, context);

        // Assert — prefix must be byte-identical (Azure OpenAI prompt cache requires this).
        result1.SystemPromptPrefix.Should().Be(result2.SystemPromptPrefix,
            because: "the same playbook context must produce an identical cached prefix");
        result2.PrefixCacheHit.Should().BeTrue(
            because: "second call with same playbook context must be a cache hit");
    }

    [Fact]
    public void BuildSystemPrompt_PrefixCacheHitIsFalse_OnFirstCall()
    {
        // Arrange
        var builder = CreateSut();

        // Act
        var result = builder.BuildSystemPrompt(Array.Empty<string>(), DefaultContext(playbookName: "PB1"));

        // Assert
        result.PrefixCacheHit.Should().BeFalse(
            because: "first call must populate the cache, not hit it");
    }

    [Fact]
    public void BuildSystemPrompt_PrefixVariesPerPlaybook()
    {
        // Arrange
        var builder = CreateSut();
        var ctxA = DefaultContext(playbookName: "PB-A");
        var ctxB = DefaultContext(playbookName: "PB-B");

        // Act
        var resultA = builder.BuildSystemPrompt(Array.Empty<string>(), ctxA);
        var resultB = builder.BuildSystemPrompt(Array.Empty<string>(), ctxB);

        // Assert — different playbooks must produce different prefixes (different persona section)
        resultA.SystemPromptPrefix.Should().Contain("PB-A");
        resultB.SystemPromptPrefix.Should().Contain("PB-B");
        resultA.SystemPromptPrefix.Should().NotBe(resultB.SystemPromptPrefix);
    }

    // ── Per-turn suffix ───────────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_PerTurnSuffix_IncludesPassedTools()
    {
        // Arrange
        var builder = CreateSut();
        IReadOnlyList<string> tools = new[] { "recall_session_file", "document_search" };

        // Act
        var result = builder.BuildSystemPrompt(tools, DefaultContext());

        // Assert
        result.PerTurnSuffix.Should().Contain("recall_session_file");
        result.PerTurnSuffix.Should().Contain("document_search");
        result.ToolSchemaNames.Should().Contain("recall_session_file");
        result.ToolSchemaNames.Should().Contain("document_search");
    }

    [Fact]
    public void BuildSystemPrompt_EmptyToolList_OmitsActiveToolsSection()
    {
        // Arrange
        var builder = CreateSut();

        // Act
        var result = builder.BuildSystemPrompt(Array.Empty<string>(), DefaultContext());

        // Assert
        result.PerTurnSuffix.Should().BeEmpty();
        result.ToolSchemaNames.Should().BeEmpty();
    }

    [Fact]
    public void BuildSystemPrompt_DeduplicatesToolNames()
    {
        // Arrange
        var builder = CreateSut();
        IReadOnlyList<string> tools = new[] { "Tool_A", "tool_a", "Tool_B" };

        // Act
        var result = builder.BuildSystemPrompt(tools, DefaultContext());

        // Assert — case-insensitive dedup
        result.ToolSchemaNames.Should().HaveCount(2);
    }

    [Fact]
    public void BuildSystemPrompt_CapsAtMaxToolsPerTurn()
    {
        // Arrange
        var builder = CreateSut();
        var manyTools = Enumerable.Range(1, 20).Select(i => $"Tool_{i}").ToArray();

        // Act
        var result = builder.BuildSystemPrompt(manyTools, DefaultContext());

        // Assert — at most MaxToolsPerTurn (8)
        result.ToolSchemaNames.Should().HaveCountLessThanOrEqualTo(OrchestratorPromptBuilder.MaxToolsPerTurn);
    }

    // ── Persona / context ─────────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_IncludesActivePlaybookName_WhenSupplied()
    {
        // Arrange
        var builder = CreateSut();
        var ctx = DefaultContext(playbookName: "ContractReview");

        // Act
        var result = builder.BuildSystemPrompt(Array.Empty<string>(), ctx);

        // Assert
        result.SystemPromptPrefix.Should().Contain("ContractReview");
    }

    [Fact]
    public void BuildSystemPrompt_IncludesMatterName_WhenSupplied()
    {
        // Arrange
        var builder = CreateSut();
        var ctx = DefaultContext(matterName: "Acme v. Widgets");

        // Act
        var result = builder.BuildSystemPrompt(Array.Empty<string>(), ctx);

        // Assert
        result.SystemPromptPrefix.Should().Contain("Acme v. Widgets");
    }

    [Fact]
    public void BuildSystemPrompt_IncludesTenantIsolationNotice_WhenTenantIdSupplied()
    {
        // Arrange
        var builder = CreateSut();
        var ctx = DefaultContext(tenantId: "tenant-xyz");

        // Act
        var result = builder.BuildSystemPrompt(Array.Empty<string>(), ctx);

        // Assert
        result.SystemPromptPrefix.Should().Contain("tenant-xyz");
    }

    [Fact]
    public void BuildSystemPrompt_FirstTurnIncludesOrientationParagraph()
    {
        // Arrange
        var builder = CreateSut();
        var ctx = DefaultContext(turnCount: 0);

        // Act
        var result = builder.BuildSystemPrompt(Array.Empty<string>(), ctx);

        // Assert
        result.SystemPromptPrefix.Should().Contain("set of tools");
    }

    // ── Budget ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_TotalTokens_WithinBudget_ForReasonableToolList()
    {
        // Arrange
        var builder = CreateSut();
        IReadOnlyList<string> tools = new[]
        {
            "recall_session_file", "get_workspace_tab_state",
            "document_search", "update_workspace_tab",
            "search_documents", "summarize_document"
        };

        // Act
        var result = builder.BuildSystemPrompt(tools, DefaultContext(playbookName: "ContractReview"));

        // Assert
        result.EstimatedTokens.Should().BeLessThan(OrchestratorPromptBuilder.TotalTokenBudget);
    }

    // ── Argument validation ───────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_ThrowsArgumentNullException_WhenToolListIsNull()
    {
        var builder = CreateSut();
        Action act = () => builder.BuildSystemPrompt(null!, DefaultContext());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildSystemPrompt_ThrowsArgumentNullException_WhenContextIsNull()
    {
        var builder = CreateSut();
        Action act = () => builder.BuildSystemPrompt(Array.Empty<string>(), null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
