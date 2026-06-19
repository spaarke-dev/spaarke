using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Capabilities;

/// <summary>
/// Unit tests for the R6 Pillar 8 / task 082 / FR-50 Layer 0.5 soft-slash pre-pass on
/// <see cref="CapabilityRouter"/>. The pre-pass recognises the four closed-vocabulary
/// soft-slash <c>commandIntent</c> hints emitted by the frontend
/// <c>SoftSlashRouter.decorateBody()</c> and short-circuits routing to the synthetic
/// capability for that intent, biasing the LLM toward the matching playbook / handler
/// on FIRST try (spec FR-50 + Phase D exit criterion 3).
/// </summary>
public sealed class CapabilityRouterSoftSlashTests
{
    private static CapabilityManifestEntry MakeEntry(string name, string[] keywordHints) =>
        new(
            CapabilityName: name,
            Description: $"Description for {name}",
            KeywordHints: keywordHints,
            PlaybookId: null,
            ToolNames: [],
            IsEnabled: true,
            TenantRestrictions: []);

    private static CapabilityRouter BuildRouter()
    {
        var manifest = new CapabilityManifest(NullLogger<CapabilityManifest>.Instance);
        // Seed an unrelated baseline capability so Layer 1 has something to score against
        // when the pre-pass doesn't match — ensures Layer 0.5 is the only short-circuit path
        // for soft-slash test cases.
        manifest.Refresh(new[] { MakeEntry("legal_research", ["case law", "court", "precedent"]) });

        var opts = Options.Create(new CapabilityRouterOptions());
        return new CapabilityRouter(manifest, opts, NullLogger<CapabilityRouter>.Instance);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Closed vocabulary — one Theory entry per soft slash (4 cases)
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("summarize", CapabilityRouter.SoftSlashSummarizeCapabilityName)]
    [InlineData("draft", CapabilityRouter.SoftSlashDraftCapabilityName)]
    [InlineData("extract-entities", CapabilityRouter.SoftSlashExtractEntitiesCapabilityName)]
    [InlineData("analyze", CapabilityRouter.SoftSlashAnalyzeCapabilityName)]
    public void RouteSync_SoftSlashIntent_ShortCircuitsToSyntheticCapability(
        string commandIntent, string expectedCapabilityName)
    {
        var router = BuildRouter();

        // User message body is arbitrary (the soft-slash literal would appear in
        // production, but the pre-pass uses commandIntent NOT message text).
        var result = router.RouteSync(
            userMessage: "anything the user typed here",
            activePlaybookName: null,
            commandIntent: commandIntent);

        result.IsConfident.Should().BeTrue(
            because: "Layer 0.5 pre-pass produces a Confident result on recognised commandIntent");
        result.SelectedCapabilities.Should().ContainSingle()
            .Which.Should().Be(expectedCapabilityName,
                because: "the closed vocabulary maps {0} → {1}", commandIntent, expectedCapabilityName);
        result.Confidence.Should().Be(1.0,
            because: "deterministic vocabulary match — no ambiguity");
        result.Layer.Should().Be(1,
            because: "the pre-pass returns at the Layer-1 surface (Layer 0.5 is an internal sub-layer)");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // NFR-11 — null/empty commandIntent falls through to Layer 1 keyword scoring
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RouteSync_NullOrWhitespaceCommandIntent_FallsThroughToLayer1(string? commandIntent)
    {
        var router = BuildRouter();

        // Message has no Layer-1 keyword match (legal_research hints are "case law", etc.)
        var result = router.RouteSync(
            userMessage: "hello there",
            activePlaybookName: null,
            commandIntent: commandIntent);

        result.IsConfident.Should().BeFalse(
            because: "without a commandIntent OR keyword match, Layer 1 returns Uncertain");
        result.SelectedCapabilities.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Unrecognised commandIntent — falls through to Layer 1 (defensive)
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("unknown-intent")]
    [InlineData("Summarize")]    // wrong case — vocabulary is ordinal lowercase
    [InlineData("translate")]    // not in Q6 closed vocabulary
    [InlineData("clear")]        // hard-slash semantic, not a soft slash
    public void RouteSync_UnrecognisedCommandIntent_FallsThroughToLayer1(string commandIntent)
    {
        var router = BuildRouter();

        var result = router.RouteSync(
            userMessage: "hello there",
            activePlaybookName: null,
            commandIntent: commandIntent);

        result.IsConfident.Should().BeFalse(
            because: "unrecognised commandIntent falls through to Layer 1 keyword scoring (NFR-11 defensive)");
        result.SelectedCapabilities.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Voice-memory pre-pass (Layer 0) takes priority over soft-slash (Layer 0.5)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RouteSync_VoiceMemoryAndSoftSlashBothPresent_VoiceMemoryWins()
    {
        var router = BuildRouter();

        // Message matches voice-memory regex ("remember X") AND commandIntent is set.
        // The Layer 0 voice-memory pre-pass runs FIRST and short-circuits to
        // manage_pinned_context; Layer 0.5 soft-slash never runs.
        var result = router.RouteSync(
            userMessage: "remember to summarize concisely",
            activePlaybookName: null,
            commandIntent: "summarize");

        result.IsConfident.Should().BeTrue();
        result.SelectedCapabilities.Should().ContainSingle()
            .Which.Should().Be(CapabilityRouter.VoiceMemoryCapabilityName,
                because: "Layer 0 voice-memory pre-pass runs BEFORE Layer 0.5 soft-slash");
        result.Layer.Should().Be(0,
            because: "voice-memory match returns at Layer 0");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Backward compat — explicit 2-arg call still works (default commandIntent = null)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RouteSync_TwoArgCall_BackwardCompatibleWithExistingCallers()
    {
        var router = BuildRouter();

        // Pre-R6 callers (tests + legacy paths) using the 2-arg overload should compile
        // and behave EXACTLY as before — Layer 0.5 is skipped when commandIntent is null
        // (default).
        var result = router.RouteSync("hello there", activePlaybookName: null);

        result.IsConfident.Should().BeFalse(
            because: "no commandIntent, no keyword match → Layer 1 Uncertain");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // RouteAsync — Layer 0.5 short-circuits Layer 2 too
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("summarize", CapabilityRouter.SoftSlashSummarizeCapabilityName)]
    [InlineData("draft", CapabilityRouter.SoftSlashDraftCapabilityName)]
    [InlineData("extract-entities", CapabilityRouter.SoftSlashExtractEntitiesCapabilityName)]
    [InlineData("analyze", CapabilityRouter.SoftSlashAnalyzeCapabilityName)]
    public async Task RouteAsync_SoftSlashIntent_SkipsLayer2AndLayer3(
        string commandIntent, string expectedCapabilityName)
    {
        var router = BuildRouter();

        // RouteAsync invokes RouteSync first; when Layer 0.5 produces a Confident
        // result, Layer 2 LLM classification and Layer 3 fallback are BOTH skipped.
        var result = await router.RouteAsync(
            userMessage: "anything",
            activePlaybookName: null,
            ct: default,
            commandIntent: commandIntent);

        result.IsConfident.Should().BeTrue();
        result.SelectedCapabilities.Should().ContainSingle()
            .Which.Should().Be(expectedCapabilityName);
        result.Layer.Should().Be(1,
            because: "Layer 0.5 returns at the Layer-1 surface");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Closed vocabulary integrity — exactly 4 mappings
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SoftSlashIntentToCapabilityName_HasExactlyFourMappings()
    {
        // Q6 binding — vocabulary is owner-locked at 4. Failing this test means
        // someone extended the table without spec FR sign-off; reject in code review.
        CapabilityRouter.SoftSlashIntentToCapabilityName.Should().HaveCount(4,
            because: "Q6 closed vocabulary at exactly 4 soft slashes");
        CapabilityRouter.SoftSlashIntentToCapabilityName.Keys
            .Should().BeEquivalentTo(new[] { "summarize", "draft", "extract-entities", "analyze" });
    }
}
