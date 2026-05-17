using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Capabilities;

/// <summary>
/// Unit tests for <see cref="CapabilityRouter"/> Layer 1 keyword classifier (AIPU2-012).
///
/// Coverage:
///   1. Exact keyword match produces confidence >= 0.8 (Confident result).
///   2. Ambiguous turn (two equal-scoring capabilities) produces confidence &lt; 0.8 (Uncertain).
///   3. Empty turn returns Uncertain with confidence 0.
///   4. Disabled capabilities are never matched (manifest.GetAll excludes them).
///   5. Layer 1 completes in under 50ms for a 50-capability manifest.
///   6. No active playbook: default threshold (0.8) is applied.
///   7. Active playbook: biased threshold (0.65) applied to playbook capabilities.
///   8. Turn with no keyword hits returns Uncertain.
///   9. Capability with zero hints is never selected.
///  10. Factory methods on CapabilityRoutingResult are validated.
/// </summary>
public sealed class CapabilityRouterTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CapabilityManifestEntry MakeEntry(
        string name,
        string[] keywordHints,
        bool isEnabled = true,
        Guid? playbookId = null)
    {
        return new CapabilityManifestEntry(
            CapabilityName: name,
            Description: $"Description for {name}",
            KeywordHints: keywordHints,
            PlaybookId: playbookId,
            ToolNames: [],
            IsEnabled: isEnabled,
            TenantRestrictions: []);
    }

    private static IOptions<CapabilityRouterOptions> DefaultOptions(
        double threshold = 0.8,
        double playbookBiasThreshold = 0.65)
    {
        return Options.Create(new CapabilityRouterOptions
        {
            ConfidenceThreshold = threshold,
            PlaybookBiasThreshold = playbookBiasThreshold
        });
    }

    /// <summary>
    /// Builds a CapabilityRouter backed by a real CapabilityManifest seeded with the given entries.
    /// The manifest automatically filters disabled entries on Refresh.
    /// </summary>
    private static CapabilityRouter BuildRouter(
        IEnumerable<CapabilityManifestEntry> entries,
        CapabilityRouterOptions? options = null)
    {
        var manifest = new CapabilityManifest(NullLogger<CapabilityManifest>.Instance);
        manifest.Refresh(entries.ToList());

        var opts = Options.Create(options ?? new CapabilityRouterOptions());
        return new CapabilityRouter(manifest, opts, NullLogger<CapabilityRouter>.Instance);
    }

    // ── Test 1: Exact keyword match → Confident ───────────────────────────────

    [Fact]
    public void RouteSync_ReturnsConfident_WhenSingleCapabilityDominatesKeywords()
    {
        // Arrange — only one capability has "case law" hints; the other has unrelated hints.
        var legalResearch = MakeEntry("legal_research", ["case law", "legal precedent", "court decision"]);
        var invoiceReview = MakeEntry("invoice_review", ["invoice", "payment", "billing"]);
        var router = BuildRouter([legalResearch, invoiceReview]);

        // Act
        var result = router.RouteSync("find case law on negligence from 2023", activePlaybookName: null);

        // Assert
        result.IsConfident.Should().BeTrue("a single unambiguous keyword hit should exceed the 0.8 threshold");
        result.SelectedCapabilities.Should().Contain("legal_research");
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.8);
        result.Layer.Should().Be(1);
    }

    // ── Test 2: Ambiguous turn → Uncertain ────────────────────────────────────

    [Fact]
    public void RouteSync_ReturnsUncertain_WhenTwoCapabilitiesScoreEqually()
    {
        // Arrange — both capabilities share the same keyword so scores tie.
        var capA = MakeEntry("capability_a", ["document", "review"]);
        var capB = MakeEntry("capability_b", ["document", "analysis"]);
        var router = BuildRouter([capA, capB]);

        // Act — "document" matches both; "review" matches A; "analysis" matches B.
        // With "document review analysis", both get 2/2 = 1.0, so confidence ≈ 0.5.
        var result = router.RouteSync("document review analysis", activePlaybookName: null);

        // Assert
        result.IsConfident.Should().BeFalse("equal scores produce confidence ≈ 0.5 which is below 0.8");
        result.Confidence.Should().BeLessThan(0.8);
        result.Layer.Should().Be(1);
    }

    // ── Test 3: Empty turn → Uncertain ───────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RouteSync_ReturnsUncertain_WhenUserMessageIsEmpty(string? message)
    {
        // Arrange
        var cap = MakeEntry("legal_research", ["case law"]);
        var router = BuildRouter([cap]);

        // Act
        var result = router.RouteSync(message!, activePlaybookName: null);

        // Assert
        result.IsConfident.Should().BeFalse();
        result.Confidence.Should().Be(0.0);
        result.SelectedCapabilities.Should().BeEmpty();
        result.Layer.Should().Be(1);
    }

    // ── Test 4: Disabled capabilities are never matched ──────────────────────

    [Fact]
    public void RouteSync_NeverMatchesDisabledCapability()
    {
        // Arrange — disabled capability has the exact keyword; enabled capability has no match.
        var disabled = MakeEntry("disabled_cap", ["case law"], isEnabled: false);
        var enabled = MakeEntry("enabled_cap", ["invoice", "payment"]);
        var router = BuildRouter([disabled, enabled]);

        // Act — only "case law" keywords present; disabled capability excluded from manifest.
        var result = router.RouteSync("find case law on negligence", activePlaybookName: null);

        // Assert — disabled cap not in manifest so no match found → Uncertain.
        result.SelectedCapabilities.Should().NotContain("disabled_cap",
            "disabled capabilities must be filtered out by the manifest before routing");
        result.IsConfident.Should().BeFalse(
            "the only matching capability is disabled so no confident result is possible");
    }

    // ── Test 5: Performance — <50ms for 50 capabilities ──────────────────────

    [Fact]
    public void RouteSync_CompletesUnder50ms_ForFiftyCapabilityManifest()
    {
        // Arrange — 50 capabilities each with 5 hints.
        var entries = Enumerable.Range(1, 50)
            .Select(i => MakeEntry(
                $"capability_{i:D2}",
                [$"keyword{i}a", $"keyword{i}b", $"keyword{i}c", $"keyword{i}d", $"keyword{i}e"]))
            .ToList();

        var router = BuildRouter(entries);

        // Warm-up pass (JIT, string intern, etc.)
        router.RouteSync("warm up the classifier", activePlaybookName: null);

        // Act — time the routing call.
        var sw = Stopwatch.StartNew();
        router.RouteSync("keyword1a is a test message for performance verification", activePlaybookName: null);
        sw.Stop();

        // Assert
        sw.ElapsedMilliseconds.Should().BeLessThan(50,
            $"Layer 1 NFR requires <50ms; actual: {sw.ElapsedMilliseconds}ms");
    }

    // ── Test 6: No keyword hits → Uncertain ──────────────────────────────────

    [Fact]
    public void RouteSync_ReturnsUncertain_WhenNoKeywordsMatchAtAll()
    {
        // Arrange
        var cap = MakeEntry("legal_research", ["case law", "legal precedent"]);
        var router = BuildRouter([cap]);

        // Act — message has nothing to do with the capability's hints.
        var result = router.RouteSync("what is the weather like today", activePlaybookName: null);

        // Assert
        result.IsConfident.Should().BeFalse();
        result.SelectedCapabilities.Should().BeEmpty();
    }

    // ── Test 7: Capability with zero hints is never selected ─────────────────

    [Fact]
    public void RouteSync_NeverSelectsCapabilityWithNoHints()
    {
        // Arrange — one capability has no hints; another has matching hints.
        var noHints = MakeEntry("no_hints_cap", []);
        var withHints = MakeEntry("legal_research", ["case law"]);
        var router = BuildRouter([noHints, withHints]);

        // Act
        var result = router.RouteSync("find case law on employment", activePlaybookName: null);

        // Assert
        result.SelectedCapabilities.Should().NotContain("no_hints_cap");
    }

    // ── Test 8: Playbook bias lowers threshold ───────────────────────────────

    [Fact]
    public void RouteSync_ReturnsConfident_WhenPlaybookBiasLowersThreshold()
    {
        // Arrange — capability belongs to a playbook; without bias it would be Uncertain.
        // We set a high default threshold (0.99) so only the playbook bias path fires.
        var playbookId = Guid.NewGuid();
        var cap = MakeEntry("legal_research", ["case law"], playbookId: playbookId);
        var unrelated = MakeEntry("invoice_review", ["invoice", "billing", "payment"]);

        var options = new CapabilityRouterOptions
        {
            ConfidenceThreshold = 0.99,       // Too high without bias
            PlaybookBiasThreshold = 0.65      // Reachable with playbook boost
        };

        var router = BuildRouter([cap, unrelated], options);

        // Act — message hits "case law" in the playbook capability.
        // The boost multiplies the score, pushing confidence above 0.65.
        var result = router.RouteSync("find case law on negligence", activePlaybookName: "LegalWorkspace");

        // Assert
        result.IsConfident.Should().BeTrue(
            "the playbook bias lowers the effective threshold to 0.65");
        result.SelectedCapabilities.Should().Contain("legal_research");
    }

    // ── Test 9: Multiple keyword hits raise confidence ────────────────────────

    [Fact]
    public void RouteSync_HigherConfidence_WhenMoreKeywordsMatch()
    {
        // Arrange — two capabilities; one has 3 matching keywords, the other 0.
        var strong = MakeEntry("legal_research", ["case law", "legal precedent", "court decision"]);
        var weak = MakeEntry("invoice_review", ["invoice", "billing"]);
        var router = BuildRouter([strong, weak]);

        // Act — message hits all 3 hints of "strong".
        var result = router.RouteSync(
            "I need to find case law, legal precedent and court decision from 2024",
            activePlaybookName: null);

        // Assert
        result.IsConfident.Should().BeTrue();
        result.Confidence.Should().BeGreaterThan(0.9,
            "three matching hints with zero hits in the competing capability should produce near-1.0 confidence");
    }

    // ── Test 10: CapabilityRoutingResult factory methods ─────────────────────

    [Fact]
    public void CapabilityRoutingResult_Confident_SetsPropertiesCorrectly()
    {
        var result = CapabilityRoutingResult.Confident(["cap_a"], 0.95, layer: 1, latencyMs: 3);

        result.IsConfident.Should().BeTrue();
        result.SelectedCapabilities.Should().Equal(["cap_a"]);
        result.Confidence.Should().Be(0.95);
        result.Layer.Should().Be(1);
        result.LatencyMs.Should().Be(3);
    }

    [Fact]
    public void CapabilityRoutingResult_Uncertain_SetsPropertiesCorrectly()
    {
        var result = CapabilityRoutingResult.Uncertain(0.45, layer: 1, latencyMs: 2);

        result.IsConfident.Should().BeFalse();
        result.SelectedCapabilities.Should().BeEmpty();
        result.Confidence.Should().Be(0.45);
        result.Layer.Should().Be(1);
        result.LatencyMs.Should().Be(2);
    }

    [Fact]
    public void CapabilityRoutingResult_Fallback_SetsLayer3AndConfidence0()
    {
        var result = CapabilityRoutingResult.Fallback(["default_cap"], latencyMs: 5);

        result.IsConfident.Should().BeFalse();
        result.SelectedCapabilities.Should().Equal(["default_cap"]);
        result.Confidence.Should().Be(0.0);
        result.Layer.Should().Be(3);
        result.LatencyMs.Should().Be(5);
    }

    [Fact]
    public void CapabilityRoutingResult_Confident_ThrowsWhenCapabilitiesEmpty()
    {
        var act = () => CapabilityRoutingResult.Confident([], 0.9, layer: 1, latencyMs: 0);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*selectedCapabilities must not be empty*");
    }

    // ── Test 11: Empty manifest → Uncertain ──────────────────────────────────

    [Fact]
    public void RouteSync_ReturnsUncertain_WhenManifestIsEmpty()
    {
        // Arrange — no capabilities loaded.
        var router = BuildRouter([]);

        // Act
        var result = router.RouteSync("find case law", activePlaybookName: null);

        // Assert
        result.IsConfident.Should().BeFalse();
        result.Confidence.Should().Be(0.0);
    }

    // ── Test 12: Constructor guards ───────────────────────────────────────────

    [Fact]
    public void CapabilityRouter_ThrowsArgumentNull_WhenManifestIsNull()
    {
        var act = () => new CapabilityRouter(
            null!,
            DefaultOptions(),
            NullLogger<CapabilityRouter>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("manifest");
    }

    [Fact]
    public void CapabilityRouter_ThrowsArgumentNull_WhenOptionsIsNull()
    {
        var manifest = new CapabilityManifest(NullLogger<CapabilityManifest>.Instance);
        manifest.Refresh([]);

        var act = () => new CapabilityRouter(
            manifest,
            null!,
            NullLogger<CapabilityRouter>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
