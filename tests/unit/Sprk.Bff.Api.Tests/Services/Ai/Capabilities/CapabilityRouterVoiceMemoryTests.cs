using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Capabilities;

/// <summary>
/// Unit tests for the R6 Pillar 7 / task 069 / FR-47 Layer 0 voice memory pre-pass on
/// <see cref="CapabilityRouter"/>. The pre-pass recognises the three voice commands
/// "remember X" / "forget X" / "always X" and short-circuits routing to the synthetic
/// <see cref="CapabilityRouter.VoiceMemoryCapabilityName"/> capability so the LLM is biased
/// toward invoking the <c>manage_pinned_context</c> tool.
/// </summary>
public sealed class CapabilityRouterVoiceMemoryTests
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
        // when the pre-pass doesn't match — ensures Layer 0 is the only short-circuit path.
        manifest.Refresh(new[] { MakeEntry("legal_research", ["case law", "court", "precedent"]) });

        var opts = Options.Create(new CapabilityRouterOptions());
        return new CapabilityRouter(manifest, opts, NullLogger<CapabilityRouter>.Instance);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // "remember X"
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("remember to use terse responses")]
    [InlineData("Remember that the client prefers terse responses")]
    [InlineData("  remember to never use bullet points")]
    public void RouteSync_RecognisesRememberCommand_AsVoiceMemory(string userMessage)
    {
        var router = BuildRouter();

        var result = router.RouteSync(userMessage, activePlaybookName: null);

        result.IsConfident.Should().BeTrue();
        result.SelectedCapabilities.Should().ContainSingle()
            .Which.Should().Be(CapabilityRouter.VoiceMemoryCapabilityName);
        result.Layer.Should().Be(0,
            because: "Layer 0 voice memory pre-pass short-circuits before Layer 1 keyword classification");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // "forget X"
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("forget the terse response preference")]
    [InlineData("Forget that I asked you to be terse")]
    public void RouteSync_RecognisesForgetCommand_AsVoiceMemory(string userMessage)
    {
        var router = BuildRouter();

        var result = router.RouteSync(userMessage, activePlaybookName: null);

        result.IsConfident.Should().BeTrue();
        result.SelectedCapabilities.Should().ContainSingle()
            .Which.Should().Be(CapabilityRouter.VoiceMemoryCapabilityName);
        result.Layer.Should().Be(0);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // "always X"
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("always cite the source statute")]
    [InlineData("Always respond with bullet points")]
    public void RouteSync_RecognisesAlwaysCommand_AsVoiceMemory(string userMessage)
    {
        var router = BuildRouter();

        var result = router.RouteSync(userMessage, activePlaybookName: null);

        result.IsConfident.Should().BeTrue();
        result.SelectedCapabilities.Should().ContainSingle()
            .Which.Should().Be(CapabilityRouter.VoiceMemoryCapabilityName);
        result.Layer.Should().Be(0);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Negative — false-positive avoidance
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("remembered that case last week")]            // word-boundary should NOT match "remember" inside "remembered"
    [InlineData("the client forgetfulness is a concern")]      // similar — "forget" must be a whole token
    public void RouteSync_DoesNotFalseFire_OnEmbeddedTokens(string userMessage)
    {
        var router = BuildRouter();

        var result = router.RouteSync(userMessage, activePlaybookName: null);

        // Result should NOT be the voice-memory capability — either uncertain or a Layer 1
        // match against the manifest's other capabilities.
        result.SelectedCapabilities.Should().NotContain(CapabilityRouter.VoiceMemoryCapabilityName,
            because: "word-boundary anchoring prevents 'remembered'/'forgetfulness' from triggering the voice memory pre-pass");
    }

    [Fact(Skip = "Watch: messages where 'remember' is NOT at the start (e.g. 'I cannot remember that case') currently never reach Layer 0 because the regex is start-anchored — but they could be added later with a stricter sentence parser. Documented for future tightening.")]
    public void RouteSync_FutureTightening_NonAnchoredRemember()
    {
        // Reserved for future stricter parsing — current implementation only matches
        // start-anchored voice openers, so "I cannot remember" never fires Layer 0.
    }

    [Fact]
    public void RouteSync_TreatsAlwaysHyphen_AsVoiceMemoryStart()
    {
        // "always-on alerts are noisy" — the trigger word 'always' is at the start with a
        // hyphen boundary. The regex requires \b after the trigger word; '-' is a word
        // boundary so this DOES match. Document the chosen semantics.
        var router = BuildRouter();

        var result = router.RouteSync("always-on alerts are noisy", activePlaybookName: null);

        result.SelectedCapabilities.Should().ContainSingle()
            .Which.Should().Be(CapabilityRouter.VoiceMemoryCapabilityName,
                because: "the chosen regex semantics treat 'always-on' as starting with a valid 'always' opener; the LLM will judge whether to invoke the tool from the description");
    }

    [Fact]
    public void RouteSync_NonVoiceMessage_FallsThroughTo_Layer1()
    {
        var router = BuildRouter();

        var result = router.RouteSync("find court precedent for negligence claims", activePlaybookName: null);

        // Layer 1 should pick this up (legal_research has "court" + "precedent" hints).
        result.Layer.Should().Be(1,
            because: "non-voice messages bypass Layer 0 and run normal Layer 1 keyword classification");
        result.SelectedCapabilities.Should().Contain("legal_research");
    }

    [Fact]
    public void RouteSync_EmptyMessage_DoesNotTrigger_Layer0()
    {
        var router = BuildRouter();

        var result = router.RouteSync("   ", activePlaybookName: null);

        result.IsConfident.Should().BeFalse();
        result.SelectedCapabilities.Should().BeEmpty();
        result.Layer.Should().Be(1,
            because: "Layer 0 short-circuit is skipped on whitespace-only input; Layer 1 returns Uncertain");
    }
}
