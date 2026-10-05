// -----------------------------------------------------------------------------
// ArmOpenAiPinFreshnessProbeTests.cs
//
// HANDLER-03 (Wave 2 pre-dispatch remediation 2026-08-27) — coverage for
// the F1-absorption probe that fails H0 fast when any ADR-020 pinned
// Azure OpenAI model version is Deprecating / already-Deprecated /
// not-reported. Follows the sibling ArmCognitiveServicesTpmProbeTests
// pattern verbatim: Evaluate() boundary-case tests over hand-built
// PinnedModelStatusEntry fixtures. Verification stanza on the punchlist:
// "Unit test: given probe returning one Deprecating pin, H0PreflightHandler
// returns HandlerResult.Failure(Resumable, 'quota-openai-pin-stale', ...)."
// The H0-handler wiring is covered by the code path
// (BuildRejectionCode(PreflightCheckNames.OpenAiPinFreshness) ==
// "quota-openai-pin-stale"); these tests prove the Evaluate() rule itself.
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.Preflight;
using Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmOpenAiPinFreshnessProbeTests
{
    private const string Region = "westus3";
    private static readonly DateTimeOffset Now = new(2026, 08, 27, 12, 00, 00, TimeSpan.Zero);
    private static readonly TimeSpan Threshold = TimeSpan.FromDays(90);

    private static readonly IReadOnlyList<PinnedModel> ThreePins = new[]
    {
        new PinnedModel("gpt-4o", "2024-08-06", ModelCapability.Chat),
        new PinnedModel("gpt-4o-mini", "2024-07-18", ModelCapability.Chat),
        new PinnedModel("text-embedding-3-large", "1", ModelCapability.Embedding),
    };

    // ---------- happy path — all three pins GA + no near-term deprecation ----------

    [Fact]
    public void Evaluate_AllPinsGaAndOutsideWindow_PassesWithOkDiagnostic()
    {
        var reported = new[]
        {
            new PinnedModelStatusEntry("gpt-4o", "2024-08-06", "OpenAI", InferenceDeprecation: null, LifecycleStatus: "GenerallyAvailable"),
            new PinnedModelStatusEntry("gpt-4o-mini", "2024-07-18", "OpenAI", InferenceDeprecation: Now.AddDays(400), LifecycleStatus: "GenerallyAvailable"),
            new PinnedModelStatusEntry("text-embedding-3-large", "1", "OpenAI", InferenceDeprecation: null, LifecycleStatus: "GenerallyAvailable"),
        };

        var result = ArmOpenAiPinFreshnessProbe.Evaluate(Region, ThreePins, reported, Now, Threshold);

        result.Passed.Should().BeTrue();
        result.CheckName.Should().Be(PreflightCheckNames.OpenAiPinFreshness);
        result.Diagnostic.Should().Contain("All 3 ADR-020 pinned OpenAI model versions are GA");
        result.Diagnostic.Should().Contain("westus3");
    }

    // ---------- pin not reported ----------

    [Fact]
    public void Evaluate_PinNotReported_FailsWithNotReportedDiagnostic()
    {
        // gpt-4o-mini absent; other 2 GA
        var reported = new[]
        {
            new PinnedModelStatusEntry("gpt-4o", "2024-08-06", "OpenAI", null, "GenerallyAvailable"),
            new PinnedModelStatusEntry("text-embedding-3-large", "1", "OpenAI", null, "GenerallyAvailable"),
        };

        var result = ArmOpenAiPinFreshnessProbe.Evaluate(Region, ThreePins, reported, Now, Threshold);

        result.Passed.Should().BeFalse();
        result.Diagnostic.Should().Contain("gpt-4o-mini@2024-07-18");
        result.Diagnostic.Should().Contain("NOT REPORTED");
        result.Diagnostic.Should().Contain("ADR-020 pin bump");
    }

    // ---------- Deprecating lifecycle status ----------

    [Fact]
    public void Evaluate_DeprecatingStatus_FailsWithDeprecatingDiagnostic()
    {
        var reported = new[]
        {
            new PinnedModelStatusEntry("gpt-4o", "2024-08-06", "OpenAI", null, "GenerallyAvailable"),
            new PinnedModelStatusEntry("gpt-4o-mini", "2024-07-18", "OpenAI", Now.AddDays(200), "Deprecating"),
            new PinnedModelStatusEntry("text-embedding-3-large", "1", "OpenAI", null, "GenerallyAvailable"),
        };

        var result = ArmOpenAiPinFreshnessProbe.Evaluate(Region, ThreePins, reported, Now, Threshold);

        result.Passed.Should().BeFalse();
        result.Diagnostic.Should().Contain("gpt-4o-mini@2024-07-18");
        result.Diagnostic.Should().Contain("Deprecating");
        result.Diagnostic.Should().Contain("ServiceModelDeprecated");
    }

    // ---------- Deprecation window expired ----------

    [Fact]
    public void Evaluate_DeprecationWithinFreshnessWindow_FailsWithWindowExpiredDiagnostic()
    {
        // gpt-4o inference-deprecation is 60 days out; threshold is 90 → within window.
        var reported = new[]
        {
            new PinnedModelStatusEntry("gpt-4o", "2024-08-06", "OpenAI", Now.AddDays(60), "GenerallyAvailable"),
            new PinnedModelStatusEntry("gpt-4o-mini", "2024-07-18", "OpenAI", null, "GenerallyAvailable"),
            new PinnedModelStatusEntry("text-embedding-3-large", "1", "OpenAI", null, "GenerallyAvailable"),
        };

        var result = ArmOpenAiPinFreshnessProbe.Evaluate(Region, ThreePins, reported, Now, Threshold);

        result.Passed.Should().BeFalse();
        result.Diagnostic.Should().Contain("gpt-4o@2024-08-06");
        result.Diagnostic.Should().Contain("freshness window");
        result.Diagnostic.Should().Contain("ADR-020 pin bump recommended");
    }

    // ---------- boundary: deprecation exactly AT the threshold treated as within window ----------

    [Fact]
    public void Evaluate_DeprecationExactlyAtThreshold_Fails()
    {
        var reported = new[]
        {
            new PinnedModelStatusEntry("gpt-4o", "2024-08-06", "OpenAI", Now.Add(Threshold), "GenerallyAvailable"),
            new PinnedModelStatusEntry("gpt-4o-mini", "2024-07-18", "OpenAI", null, "GenerallyAvailable"),
            new PinnedModelStatusEntry("text-embedding-3-large", "1", "OpenAI", null, "GenerallyAvailable"),
        };

        var result = ArmOpenAiPinFreshnessProbe.Evaluate(Region, ThreePins, reported, Now, Threshold);

        result.Passed.Should().BeFalse("deprecation date exactly at threshold is treated as inside the window (<=)");
    }

    // ---------- boundary: deprecation strictly outside threshold passes ----------

    [Fact]
    public void Evaluate_DeprecationJustOutsideThreshold_Passes()
    {
        var reported = new[]
        {
            new PinnedModelStatusEntry("gpt-4o", "2024-08-06", "OpenAI", Now.Add(Threshold).AddSeconds(1), "GenerallyAvailable"),
            new PinnedModelStatusEntry("gpt-4o-mini", "2024-07-18", "OpenAI", null, "GenerallyAvailable"),
            new PinnedModelStatusEntry("text-embedding-3-large", "1", "OpenAI", null, "GenerallyAvailable"),
        };

        var result = ArmOpenAiPinFreshnessProbe.Evaluate(Region, ThreePins, reported, Now, Threshold);

        result.Passed.Should().BeTrue("deprecation date just past the threshold is outside the freshness window");
    }

    // ---------- case-insensitive match on Name + Version ----------

    [Fact]
    public void Evaluate_NameAndVersionMatchIsCaseInsensitive()
    {
        var reported = new[]
        {
            new PinnedModelStatusEntry("GPT-4O", "2024-08-06", "OpenAI", null, "GenerallyAvailable"),
            new PinnedModelStatusEntry("gpt-4o-mini", "2024-07-18", "OpenAI", null, "GenerallyAvailable"),
            new PinnedModelStatusEntry("text-embedding-3-large", "1", "OpenAI", null, "GenerallyAvailable"),
        };

        var result = ArmOpenAiPinFreshnessProbe.Evaluate(Region, ThreePins, reported, Now, Threshold);

        result.Passed.Should().BeTrue();
    }

    // ---------- Headroom evidence payload shape ----------

    [Fact]
    public void Evaluate_HeadroomPayloadCarriesPerPinBreakdown()
    {
        var reported = new[]
        {
            new PinnedModelStatusEntry("gpt-4o", "2024-08-06", "OpenAI", null, "GenerallyAvailable"),
            new PinnedModelStatusEntry("gpt-4o-mini", "2024-07-18", "OpenAI", Now.AddDays(30), "Deprecating"),
            new PinnedModelStatusEntry("text-embedding-3-large", "1", "OpenAI", null, "GenerallyAvailable"),
        };

        var result = ArmOpenAiPinFreshnessProbe.Evaluate(Region, ThreePins, reported, Now, Threshold);

        result.Passed.Should().BeFalse();
        var headroomJson = result.Headroom.GetRawText();
        headroomJson.Should().Contain("perPin");
        headroomJson.Should().Contain("gpt-4o-mini@2024-07-18");
        headroomJson.Should().Contain("deprecating-status");
        headroomJson.Should().Contain("freshnessThresholdDays");
        // Evidence carries evaluatedAt so operators can reason about when the verdict landed.
        headroomJson.Should().Contain("evaluatedAt");
    }
}
