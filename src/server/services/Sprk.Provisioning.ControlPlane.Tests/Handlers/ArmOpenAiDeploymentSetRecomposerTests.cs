// -----------------------------------------------------------------------------
// ArmOpenAiDeploymentSetRecomposerTests.cs
//
// HANDLER-13 (Wave 2 pre-dispatch remediation 2026-08-27 — F5 verbatim).
//
// LIVE-BEHAVIOR TESTS for the production ArmOpenAiDeploymentSetRecomposer.
// Two layers, both ADR-038 path #1 pure C# unit tests (no live Azure):
//   1. Evaluate() boundary-case tests over the pure filter logic — hand-built
//      OpenAiRegionalUsageEntry fixtures.
//   2. RecomposeAsync() end-to-end test through a REAL ArmClient constructed
//      against the shared ArmSdkTestFakes fake HttpMessageHandler — proves
//      the recomposer genuinely calls the CognitiveServices usage endpoint
//      and correctly filters based on the returned JSON, not a hard-coded
//      Success.
//
// TEST BOUNDARY (ADR-038): mirrors ArmCognitiveServicesTpmProbeTests.cs +
// ArmSubscriptionReadinessProbeTests.cs — CLAUDE.md §11 reuse. NOT the
// banned Mock<HttpMessageHandler> pattern (this is a hand-rolled fake, not
// a Moq mock) and NOT a wrapper-mock of the SDK client itself; the real
// ArmClient's request pipeline (auth header injection, URL construction,
// STJ deserialization) runs unmodified against a canned HTTP response.
//
// COVERAGE:
//   E1  Evaluate: zero-Limit model → dropped with reason.
//   E2  Evaluate: model not reported in usage → dropped with "NOT REPORTED".
//   E3  Evaluate: model with positive Limit → preserved.
//   E4  Evaluate: mixed set (one preserved, two dropped) — punchlist SUCCESS
//       scenario. Preserved set has ONLY the positive-TPM model; note lists
//       both dropped reasons.
//   E5  Evaluate: all preserved → OperatorNote is empty (no drop occurred).
//   E6  Evaluate: gpt-4o-mini usage entry MUST NOT satisfy a gpt-4o request
//       (anchored-suffix regex parity with ArmCognitiveServicesTpmProbe).
//   R1  RecomposeAsync: hits the REAL /providers/Microsoft.CognitiveServices/
//       locations/{region}/usages endpoint (URL assertion via
//       FakeArmHttpMessageHandler.RequestedUris) — proves the impl is not a
//       hard-coded Success.
//   R2  RecomposeAsync: with a mock usage response where gpt-4o=0 TPM and
//       gpt-4o-mini=500 TPM → only gpt-4o-mini preserved; operator note
//       cites the drop reason (F5 SUCCESS scenario adapted to
//       PinnedModelCatalog's 4o names).
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;
using Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmOpenAiDeploymentSetRecomposerTests
{
    private const string Region = "westus3";
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";

    private static IReadOnlyList<PinnedModel> CanonicalSet => PinnedModelCatalog.Models;

    // ---------- Evaluate() boundary cases ----------

    [Fact]
    public void Evaluate_ZeroLimitReported_DropsWithReason()
    {
        var usage = new[]
        {
            new OpenAiRegionalUsageEntry("Standard.gpt-4o", Limit: 0),
        };
        var set = new[] { new PinnedModel("gpt-4o", "2024-08-06", ModelCapability.Chat) };

        var result = ArmOpenAiDeploymentSetRecomposer.Evaluate(Region, set, usage);

        result.PreservedSet.Should().BeEmpty();
        result.DroppedModelIds.Should().ContainSingle().Which.Should().Be("gpt-4o");
        result.OperatorNote.Should().Contain("gpt-4o");
        result.OperatorNote.Should().Contain("TPM = 0");
        result.OperatorNote.Should().Contain(Region);
    }

    [Fact]
    public void Evaluate_ModelNotReported_DropsWithNotReported()
    {
        // Empty usage → the requested model has no auto-granted quota at all.
        var usage = Array.Empty<OpenAiRegionalUsageEntry>();
        var set = new[] { new PinnedModel("gpt-4o", "2024-08-06", ModelCapability.Chat) };

        var result = ArmOpenAiDeploymentSetRecomposer.Evaluate(Region, set, usage);

        result.PreservedSet.Should().BeEmpty();
        result.DroppedModelIds.Should().ContainSingle().Which.Should().Be("gpt-4o");
        result.OperatorNote.Should().Contain("NOT REPORTED");
    }

    [Fact]
    public void Evaluate_PositiveLimit_PreservesModel()
    {
        var usage = new[]
        {
            new OpenAiRegionalUsageEntry("Standard.gpt-4o", Limit: 500),
        };
        var set = new[] { new PinnedModel("gpt-4o", "2024-08-06", ModelCapability.Chat) };

        var result = ArmOpenAiDeploymentSetRecomposer.Evaluate(Region, set, usage);

        result.PreservedSet.Should().ContainSingle().Which.ModelId.Should().Be("gpt-4o");
        result.DroppedModelIds.Should().BeEmpty();
        result.OperatorNote.Should().BeEmpty("no drop occurred, note MUST be empty per contract");
    }

    [Fact]
    public void Evaluate_MixedSet_OnlyPositiveTpmPreserved_DroppedReasonsListedInNote()
    {
        // Punchlist SUCCESS scenario adapted to the canonical 4o catalog:
        // gpt-4o = 0 TPM, gpt-4o-mini = 500 TPM, text-embedding-3-large NOT
        // REPORTED. Recomposer returns only gpt-4o-mini + a note listing the
        // two drops with reasons.
        var usage = new[]
        {
            new OpenAiRegionalUsageEntry("Standard.gpt-4o", Limit: 0),
            new OpenAiRegionalUsageEntry("Standard.gpt-4o-mini", Limit: 500),
        };

        var result = ArmOpenAiDeploymentSetRecomposer.Evaluate(Region, CanonicalSet, usage);

        result.PreservedSet.Should().ContainSingle()
            .Which.ModelId.Should().Be("gpt-4o-mini");
        result.DroppedModelIds.Should().BeEquivalentTo(
            new[] { "gpt-4o", "text-embedding-3-large" });

        // Note lists BOTH drop reasons + subscription-scoped remediation
        // guidance.
        result.OperatorNote.Should().Contain("gpt-4o");
        result.OperatorNote.Should().Contain("text-embedding-3-large");
        result.OperatorNote.Should().Contain("TPM = 0");
        result.OperatorNote.Should().Contain("NOT REPORTED");
        result.OperatorNote.Should().Contain("support ticket");
        result.OperatorNote.Should().Contain(Region);
    }

    [Fact]
    public void Evaluate_AllPreserved_OperatorNoteIsEmpty()
    {
        var usage = new[]
        {
            new OpenAiRegionalUsageEntry("Standard.gpt-4o", Limit: 1_000),
            new OpenAiRegionalUsageEntry("Standard.gpt-4o-mini", Limit: 1_000),
            new OpenAiRegionalUsageEntry("Standard.text-embedding-3-large", Limit: 350),
        };

        var result = ArmOpenAiDeploymentSetRecomposer.Evaluate(Region, CanonicalSet, usage);

        result.PreservedSet.Should().HaveCount(3);
        result.DroppedModelIds.Should().BeEmpty();
        result.OperatorNote.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_Gpt4oMiniUsageEntry_DoesNotSatisfyGpt4oRequest()
    {
        // Regression guard: anchored-suffix regex parity with
        // ArmCognitiveServicesTpmProbe — 'gpt-4o-mini' MUST NOT match a
        // 'gpt-4o' request. Absent the anchor, a fresh sub with 500 TPM on
        // gpt-4o-mini but 0 TPM on gpt-4o would falsely preserve gpt-4o.
        var usage = new[]
        {
            new OpenAiRegionalUsageEntry("Standard.gpt-4o-mini", Limit: 500),
        };
        var set = new[] { new PinnedModel("gpt-4o", "2024-08-06", ModelCapability.Chat) };

        var result = ArmOpenAiDeploymentSetRecomposer.Evaluate(Region, set, usage);

        result.PreservedSet.Should().BeEmpty();
        result.DroppedModelIds.Should().ContainSingle().Which.Should().Be("gpt-4o");
        result.OperatorNote.Should().Contain("NOT REPORTED");
    }

    // ---------- RecomposeAsync() end-to-end via real ArmClient + fake transport ----------

    [Fact]
    public async Task RecomposeAsync_HitsRealCognitiveServicesUsageEndpoint()
    {
        // Proves the impl is not a hard-coded Success: the URL the fake
        // handler observes MUST be the actual ARM usages endpoint.
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, """
                { "value": [ ] }
                """));
        var recomposer = new ArmOpenAiDeploymentSetRecomposer(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<ArmOpenAiDeploymentSetRecomposer>.Instance);

        _ = await recomposer.RecomposeAsync(
            new OpenAiDeploymentSetRecomposeRequest(SubscriptionId, Region, CanonicalSet),
            CancellationToken.None);

        handler.RequestedUris.Should().ContainSingle();
        handler.RequestedUris[0].AbsolutePath.Should().Be(
            $"/subscriptions/{SubscriptionId}/providers/Microsoft.CognitiveServices/locations/{Region}/usages");
    }

    [Fact]
    public async Task RecomposeAsync_Gpt4oZeroTpm_Gpt4oMini500Tpm_ReturnsOnlyMini_WithDowngradeNote()
    {
        // Punchlist SUCCESS scenario end-to-end: with a mock usage response
        // showing 0 TPM for gpt-4o and 500 TPM for gpt-4o-mini, the recomposer
        // must return ONLY gpt-4o-mini and log the downgrade in OperatorNote.
        // (text-embedding-3-large is intentionally absent from the mock
        // response to also cover the NOT-REPORTED branch, matching what a
        // fresh sub typically reports.)
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, """
                { "value": [
                    { "unit": "Count", "name": { "value": "Standard.gpt-4o",      "localizedValue": "gpt-4o"      }, "currentValue": 0.0, "limit": 0.0   },
                    { "unit": "Count", "name": { "value": "Standard.gpt-4o-mini", "localizedValue": "gpt-4o-mini" }, "currentValue": 0.0, "limit": 500.0 }
                ] }
                """));
        var recomposer = new ArmOpenAiDeploymentSetRecomposer(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<ArmOpenAiDeploymentSetRecomposer>.Instance);

        var result = await recomposer.RecomposeAsync(
            new OpenAiDeploymentSetRecomposeRequest(SubscriptionId, Region, CanonicalSet),
            CancellationToken.None);

        result.PreservedSet.Should().ContainSingle()
            .Which.ModelId.Should().Be("gpt-4o-mini",
                "punchlist SUCCESS: gpt-4o dropped for 0 TPM, text-embedding-3-large dropped as NOT REPORTED");
        result.DroppedModelIds.Should().BeEquivalentTo(
            new[] { "gpt-4o", "text-embedding-3-large" });
        result.OperatorNote.Should().NotBeEmpty(
            "punchlist requires an operator-visible downgrade note when models are dropped");
        result.OperatorNote.Should().Contain("gpt-4o");
        result.OperatorNote.Should().Contain("text-embedding-3-large");
        result.OperatorNote.Should().Contain(Region);
    }

    [Fact]
    public async Task RecomposeAsync_AllTpmAvailable_PreservesFullSet_EmptyNote()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, """
                { "value": [
                    { "unit": "Count", "name": { "value": "Standard.gpt-4o",                 "localizedValue": "gpt-4o"      }, "currentValue": 0.0, "limit": 500.0 },
                    { "unit": "Count", "name": { "value": "Standard.gpt-4o-mini",            "localizedValue": "gpt-4o-mini" }, "currentValue": 0.0, "limit": 500.0 },
                    { "unit": "Count", "name": { "value": "Standard.text-embedding-3-large", "localizedValue": "e3l"         }, "currentValue": 0.0, "limit": 100.0 }
                ] }
                """));
        var recomposer = new ArmOpenAiDeploymentSetRecomposer(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<ArmOpenAiDeploymentSetRecomposer>.Instance);

        var result = await recomposer.RecomposeAsync(
            new OpenAiDeploymentSetRecomposeRequest(SubscriptionId, Region, CanonicalSet),
            CancellationToken.None);

        result.PreservedSet.Should().HaveCount(3);
        result.DroppedModelIds.Should().BeEmpty();
        result.OperatorNote.Should().BeEmpty();
    }

    [Fact]
    public async Task RecomposeAsync_MissingSubscriptionId_ThrowsArgumentException()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            throw new InvalidOperationException("must not call ARM on config error"));
        var recomposer = new ArmOpenAiDeploymentSetRecomposer(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<ArmOpenAiDeploymentSetRecomposer>.Instance);

        var act = async () => await recomposer.RecomposeAsync(
            new OpenAiDeploymentSetRecomposeRequest(string.Empty, Region, CanonicalSet),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.RequestedUris.Should().BeEmpty();
    }

    [Fact]
    public async Task RecomposeAsync_MissingRegion_ThrowsArgumentException()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            throw new InvalidOperationException("must not call ARM on config error"));
        var recomposer = new ArmOpenAiDeploymentSetRecomposer(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<ArmOpenAiDeploymentSetRecomposer>.Instance);

        var act = async () => await recomposer.RecomposeAsync(
            new OpenAiDeploymentSetRecomposeRequest(SubscriptionId, string.Empty, CanonicalSet),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.RequestedUris.Should().BeEmpty();
    }
}
