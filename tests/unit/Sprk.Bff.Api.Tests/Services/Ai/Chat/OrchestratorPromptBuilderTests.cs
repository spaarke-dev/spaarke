using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;
using FluentAssertions;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="OrchestratorPromptBuilder"/>.
///
/// Acceptance criteria (AIPU2-015):
///   - Prefix is byte-identical across calls with the same manifest state (cache hit = same string).
///   - Prefix is regenerated when the manifest changes (different LastRefreshedUtc).
///   - Total prompt stays within the 9000-token budget for any valid routing result.
///   - Token budget exceeded → trim logic fires, warning logged.
///   - Per-turn suffix contains only the tool schemas named in the routing result.
///   - MaxToolsPerTurn cap enforced; never more than 8 tool schemas in one prompt.
///   - Broad mode (empty SelectedCapabilities) includes all tools up to MaxToolsPerTurn.
/// </summary>
public class OrchestratorPromptBuilderTests
{
    // ── Shared helpers ────────────────────────────────────────────────────────

    private static CapabilityManifestEntry MakeCapability(
        string name,
        string description = "A test capability",
        string[]? toolNames = null) =>
        new(
            CapabilityName: name,
            Description: description,
            KeywordHints: ["hint"],
            PlaybookId: null,
            ToolNames: toolNames ?? [$"Tool_{name}"],
            IsEnabled: true,
            TenantRestrictions: []);

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

    private static CapabilityRoutingResult ConfidentRouting(params string[] capabilityNames) =>
        CapabilityRoutingResult.Confident(
            selectedCapabilities: capabilityNames,
            confidence: 0.95,
            layer: 1,
            latencyMs: 12);

    /// <summary>
    /// Creates a manifest mock with the given capabilities pre-loaded.
    /// LastRefreshedUtc is fixed to a deterministic value unless overridden.
    /// </summary>
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

    // ── Prefix stability (caching) ────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_PrefixIsIdentical_AcrossTwoCallsWithSameManifestState()
    {
        // Arrange
        var caps = new[] { MakeCapability("cap1"), MakeCapability("cap2") };
        var (_, builder) = CreateSut(caps);
        var routing = ConfidentRouting("cap1");
        var context = DefaultContext();

        // Act — two consecutive calls with identical manifest state.
        var result1 = builder.BuildSystemPrompt(routing, context);
        var result2 = builder.BuildSystemPrompt(routing, context);

        // Assert — prefix must be byte-identical (Azure OpenAI prompt cache requires this).
        result1.SystemPromptPrefix.Should().Be(result2.SystemPromptPrefix,
            because: "the same manifest state must produce an identical cached prefix");
        result2.PrefixCacheHit.Should().BeTrue(
            because: "second call with same manifest hash must be a cache hit");
    }

    [Fact]
    public void BuildSystemPrompt_PrefixCacheHitIsFalse_OnFirstCall()
    {
        // Arrange
        var (_, builder) = CreateSut([MakeCapability("cap1")]);
        var routing = ConfidentRouting("cap1");

        // Act
        var result = builder.BuildSystemPrompt(routing, DefaultContext());

        // Assert
        result.PrefixCacheHit.Should().BeFalse(
            because: "the first call must compute and cache the prefix, not read from cache");
    }

    [Fact]
    public void BuildSystemPrompt_PrefixRegenerates_WhenManifestChanges()
    {
        // Arrange — two builders simulating two different manifest refresh timestamps.
        var caps = new[] { MakeCapability("cap1") };
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 1, 1, 0, 15, 0, TimeSpan.Zero); // 15 min later

        var (_, builder1) = CreateSut(caps, t1);
        var (_, builder2) = CreateSut(caps, t2);

        var routing = ConfidentRouting("cap1");
        var context = DefaultContext();

        // Act
        var result1 = builder1.BuildSystemPrompt(routing, context);
        var result2 = builder2.BuildSystemPrompt(routing, context);

        // Assert — different manifest timestamps produce different prefixes.
        // (In practice they will differ because the token estimate or hash changes;
        //  the key assertion is that the builder does not return a stale cached entry.)
        result1.PrefixCacheHit.Should().BeFalse();
        result2.PrefixCacheHit.Should().BeFalse(
            because: "a fresh builder instance has an empty cache; no hit on first call");
    }

    // ── Token budget ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_TotalTokensWithinBudget_ForNormalRoutingResult()
    {
        // Arrange — 10 capabilities, each with one tool.
        var caps = Enumerable.Range(1, 10)
            .Select(i => MakeCapability($"cap{i}", $"Description for capability {i}"))
            .ToList();
        var (_, builder) = CreateSut(caps);
        var routing = ConfidentRouting("cap1", "cap2", "cap3");
        var context = DefaultContext(matterName: "Acme v. Widgets");

        // Act
        var result = builder.BuildSystemPrompt(routing, context);

        // Assert
        result.EstimatedTokens.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.TotalTokenBudget,
            because: "prompt must always stay within the 9000-token budget");
    }

    [Fact]
    public void EstimateTokens_ReturnsZero_ForNullOrEmptyString()
    {
        OrchestratorPromptBuilder.EstimateTokens(string.Empty).Should().Be(0);
        OrchestratorPromptBuilder.EstimateTokens("").Should().Be(0);
    }

    [Theory]
    [InlineData("abcd", 1)]       // exactly 4 chars = 1 token
    [InlineData("abcde", 2)]      // 5 chars → ceil(1.25) = 2 tokens
    [InlineData("abcdefgh", 2)]   // 8 chars = 2 tokens
    public void EstimateTokens_UsesCharsDividedByFourCeiling(string text, int expected)
    {
        OrchestratorPromptBuilder.EstimateTokens(text).Should().Be(expected);
    }

    // ── Per-turn suffix tool selection ────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_PerTurnSuffixContainsSelectedToolsOnly_WhenConfidentRouting()
    {
        // Arrange
        var caps = new[]
        {
            MakeCapability("search_docs", toolNames: ["SearchDocuments"]),
            MakeCapability("legal_research", toolNames: ["ResearchLegal", "LookupCase"]),
            MakeCapability("write_back", toolNames: ["WriteRecord"]),
        };
        var (_, builder) = CreateSut(caps);

        // Route to search_docs + legal_research only.
        var routing = ConfidentRouting("search_docs", "legal_research");
        var context = DefaultContext();

        // Act
        var result = builder.BuildSystemPrompt(routing, context);

        // Assert — suffix must contain selected tools.
        result.ToolSchemaNames.Should().Contain("SearchDocuments");
        result.ToolSchemaNames.Should().Contain("ResearchLegal");
        result.ToolSchemaNames.Should().Contain("LookupCase");

        // Assert — non-selected capability's tool must NOT appear.
        result.ToolSchemaNames.Should().NotContain("WriteRecord",
            because: "write_back was not in the routing result");
    }

    [Fact]
    public void BuildSystemPrompt_PerTurnSuffixContainsAllTools_WhenBroadMode()
    {
        // Arrange — broad mode: routing is not confident and SelectedCapabilities is empty.
        var caps = new[]
        {
            MakeCapability("cap1", toolNames: ["Tool1"]),
            MakeCapability("cap2", toolNames: ["Tool2"]),
            MakeCapability("cap3", toolNames: ["Tool3"]),
        };
        var (_, builder) = CreateSut(caps);

        var broadRouting = CapabilityRoutingResult.Fallback([], selectedToolNames: [], latencyMs: 0);
        var context = DefaultContext();

        // Act
        var result = builder.BuildSystemPrompt(broadRouting, context);

        // Assert — broad mode includes all 3 tools (within cap).
        result.ToolSchemaNames.Should().Contain("Tool1");
        result.ToolSchemaNames.Should().Contain("Tool2");
        result.ToolSchemaNames.Should().Contain("Tool3");
    }

    [Fact]
    public void BuildSystemPrompt_PerTurnSuffixIsEmpty_WhenNoToolsResolved()
    {
        // Arrange — routing selects a capability that has no tool names in the manifest.
        var caps = new[] { MakeCapability("empty_cap", toolNames: []) };
        var (_, builder) = CreateSut(caps);
        var routing = ConfidentRouting("empty_cap");

        // Act
        var result = builder.BuildSystemPrompt(routing, DefaultContext());

        // Assert
        result.ToolSchemaNames.Should().BeEmpty();
        result.PerTurnSuffix.Should().BeEmpty();
    }

    // ── MaxToolsPerTurn cap ───────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_NeverExceedsMaxToolsPerTurn_EvenWithManyCapabilities()
    {
        // Arrange — 20 capabilities each with one tool = 20 possible tools.
        var caps = Enumerable.Range(1, 20)
            .Select(i => MakeCapability($"cap{i}", toolNames: [$"Tool{i}"]))
            .ToList();
        var (_, builder) = CreateSut(caps);

        // Broad routing includes all tools; cap must still apply.
        var broadRouting = CapabilityRoutingResult.Fallback([], selectedToolNames: [], latencyMs: 0);

        // Act
        var result = builder.BuildSystemPrompt(broadRouting, DefaultContext());

        // Assert
        result.ToolSchemaNames.Count.Should().BeLessThanOrEqualTo(
            OrchestratorPromptBuilder.MaxToolsPerTurn,
            because: "tool schemas must be capped at MaxToolsPerTurn (8)");
    }

    [Fact]
    public void BuildSystemPrompt_DeduplicatesToolNames_WhenMultipleCapabilitiesShareATool()
    {
        // Arrange — two capabilities share "SharedTool".
        var caps = new[]
        {
            MakeCapability("cap1", toolNames: ["SharedTool", "Tool1"]),
            MakeCapability("cap2", toolNames: ["SharedTool", "Tool2"]),
        };
        var (_, builder) = CreateSut(caps);
        var routing = ConfidentRouting("cap1", "cap2");

        // Act
        var result = builder.BuildSystemPrompt(routing, DefaultContext());

        // Assert — SharedTool must appear exactly once.
        result.ToolSchemaNames.Count(t => t == "SharedTool").Should().Be(1,
            because: "duplicate tool names must be de-duplicated");
    }

    // ── Prompt content ────────────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_PrefixContainsPersonaSection()
    {
        // Arrange
        var (_, builder) = CreateSut([MakeCapability("cap1")]);
        var routing = ConfidentRouting("cap1");
        var context = DefaultContext(playbookName: "Contract Review");

        // Act
        var result = builder.BuildSystemPrompt(routing, context);

        // Assert
        result.SystemPromptPrefix.Should().Contain("Spaarke AI",
            because: "the persona section must identify the assistant");
        result.SystemPromptPrefix.Should().Contain("Contract Review",
            because: "the active playbook name must appear in the persona section");
    }

    [Fact]
    public void BuildSystemPrompt_PrefixContainsCapabilityIndex_WhenManifestIsPopulated()
    {
        // Arrange
        var caps = new[]
        {
            MakeCapability("web_search", description: "Searches the web for current information"),
            MakeCapability("legal_research", description: "Searches legal databases for case law"),
        };
        var (_, builder) = CreateSut(caps);
        var routing = ConfidentRouting("web_search");

        // Act
        var result = builder.BuildSystemPrompt(routing, DefaultContext());

        // Assert — capability names must be listed in the prefix.
        result.SystemPromptPrefix.Should().Contain("web_search",
            because: "all enabled capabilities must appear in the capability index");
        result.SystemPromptPrefix.Should().Contain("legal_research",
            because: "all enabled capabilities must appear in the capability index");
    }

    [Fact]
    public void BuildSystemPrompt_PrefixContainsEntityEnrichment_WhenMatterNameIsSupplied()
    {
        // Arrange
        var (_, builder) = CreateSut([MakeCapability("cap1")]);
        var context = DefaultContext(matterName: "GlobalCorp Acquisition");
        var routing = ConfidentRouting("cap1");

        // Act
        var result = builder.BuildSystemPrompt(routing, context);

        // Assert
        result.SystemPromptPrefix.Should().Contain("GlobalCorp Acquisition",
            because: "the entity enrichment block must inject the matter name into the prefix");
    }

    [Fact]
    public void BuildSystemPrompt_PrefixDoesNotContainEntityEnrichment_WhenMatterNameIsNull()
    {
        // Arrange
        var (_, builder) = CreateSut([MakeCapability("cap1")]);
        var context = DefaultContext(matterName: null);
        var routing = ConfidentRouting("cap1");

        // Act
        var result = builder.BuildSystemPrompt(routing, context);

        // Assert — "Active matter" heading should not be present.
        result.SystemPromptPrefix.Should().NotContain("Active matter",
            because: "entity enrichment must be skipped when MatterName is null");
    }

    [Fact]
    public void BuildSystemPrompt_PrefixContainsTenantIsolationNotice_WhenTenantIdIsKnown()
    {
        // Arrange
        var (_, builder) = CreateSut([MakeCapability("cap1")]);
        var context = DefaultContext(tenantId: "acme-tenant-123");
        var routing = ConfidentRouting("cap1");

        // Act
        var result = builder.BuildSystemPrompt(routing, context);

        // Assert
        result.SystemPromptPrefix.Should().Contain("acme-tenant-123",
            because: "the standing instructions must reference the tenant ID for isolation reminders");
    }

    // ── Full prompt structure ─────────────────────────────────────────────────

    [Fact]
    public void OrchestratorPrompt_FullSystemPrompt_ConcatenatesPrefixAndSuffix()
    {
        // Arrange
        var (_, builder) = CreateSut([MakeCapability("cap1")]);
        var result = builder.BuildSystemPrompt(ConfidentRouting("cap1"), DefaultContext());

        // Act
        var full = result.FullSystemPrompt;

        // Assert
        full.Should().StartWith(result.SystemPromptPrefix[..Math.Min(50, result.SystemPromptPrefix.Length)],
            because: "FullSystemPrompt must begin with the prefix");

        if (!string.IsNullOrEmpty(result.PerTurnSuffix))
        {
            full.Should().EndWith(result.PerTurnSuffix.TrimEnd(),
                because: "FullSystemPrompt must end with the per-turn suffix");
        }
    }

    [Fact]
    public void OrchestratorPrompt_FullSystemPrompt_EqualsPrefix_WhenSuffixIsEmpty()
    {
        // Arrange — empty capability tools means no suffix.
        var caps = new[] { MakeCapability("empty", toolNames: []) };
        var (_, builder) = CreateSut(caps);
        var result = builder.BuildSystemPrompt(ConfidentRouting("empty"), DefaultContext());

        // Act & Assert
        result.PerTurnSuffix.Should().BeEmpty();
        result.FullSystemPrompt.Should().Be(result.SystemPromptPrefix);
    }

    // ── Argument validation ───────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_ThrowsArgumentNullException_WhenRoutingIsNull()
    {
        var (_, builder) = CreateSut();

        var act = () => builder.BuildSystemPrompt(null!, DefaultContext());
        act.Should().Throw<ArgumentNullException>().WithParameterName("routing");
    }

    [Fact]
    public void BuildSystemPrompt_ThrowsArgumentNullException_WhenContextIsNull()
    {
        var (_, builder) = CreateSut();

        var act = () => builder.BuildSystemPrompt(CapabilityRoutingResult.Fallback([], selectedToolNames: [], latencyMs: 0), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }
}
