// -----------------------------------------------------------------------------
// ArmCognitiveServicesTpmProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for ArmCognitiveServicesTpmProbe (task 120,
// Wave G-2). Two layers:
//   1. Evaluate() boundary-case tests — the pure threshold-comparison logic
//      ported from Test-AzureOpenAiTpmHeadroom.ps1 (under / at / over
//      threshold + not-reported), exercised directly with hand-built
//      CognitiveServicesUsageEntry fixtures.
//   2. CheckAsync() end-to-end test — builds a REAL ArmClient against the
//      fake-transport helper (ArmSdkTestFakes, shared with task 121's
//      ArmSubscriptionReadinessProbeTests.cs per CLAUDE.md §11) so the SDK's
//      own request marshaling + STJ deserialization run unmodified; only the
//      HTTP socket is faked. Proves the probe genuinely calls the ARM usage
//      endpoint, not a hard-coded Passed.
//
// ADR-038 alignment: pure C# unit tests. No live Azure, no
// Mock&lt;HttpMessageHandler&gt;, no SDK-client wrapper mock.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.Preflight;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmCognitiveServicesTpmProbeTests
{
    private const string Region = "eastus";
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";

    private static readonly IReadOnlyDictionary<string, int> SingleModelRequest =
        new Dictionary<string, int> { ["gpt-4o"] = 150 };

    // ---------- Evaluate() boundary cases ----------

    [Fact]
    public void Evaluate_UnderThreshold_PassesWithFitsTrue()
    {
        var usage = new[] { new CognitiveServicesUsageEntry("Standard.gpt-4o", CurrentValue: 100, Limit: 1000) };

        var result = ArmCognitiveServicesTpmProbe.Evaluate(Region, SingleModelRequest, usage);

        result.Passed.Should().BeTrue("observed 100 + requested 150 = 250 <= limit 1000");
        result.Diagnostic.Should().Contain("headroom OK");
    }

    [Fact]
    public void Evaluate_ExactlyAtThreshold_Passes()
    {
        // observed 850 + requested 150 = 1000 == limit 1000 -> fits (per PS script's <= comparison).
        var usage = new[] { new CognitiveServicesUsageEntry("Standard.gpt-4o", CurrentValue: 850, Limit: 1000) };

        var result = ArmCognitiveServicesTpmProbe.Evaluate(Region, SingleModelRequest, usage);

        result.Passed.Should().BeTrue("projected 1000 == limit 1000 is within threshold (<=)");
    }

    [Fact]
    public void Evaluate_OverThreshold_FailsWithShortfallDiagnostic()
    {
        var usage = new[] { new CognitiveServicesUsageEntry("Standard.gpt-4o", CurrentValue: 900, Limit: 1000) };

        var result = ArmCognitiveServicesTpmProbe.Evaluate(Region, SingleModelRequest, usage);

        result.Passed.Should().BeFalse("observed 900 + requested 150 = 1050 > limit 1000");
        result.Diagnostic.Should().Contain("SHORTFALL: 50");
        result.Diagnostic.Should().Contain("INSUFFICIENT");
    }

    [Fact]
    public void Evaluate_ModelNotReported_FailsAsNotReported()
    {
        var usage = new[] { new CognitiveServicesUsageEntry("Standard.gpt-4o-mini", CurrentValue: 10, Limit: 1000) };

        var result = ArmCognitiveServicesTpmProbe.Evaluate(Region, SingleModelRequest, usage);

        result.Passed.Should().BeFalse("gpt-4o-mini must not satisfy a gpt-4o request (anchored suffix match)");
        result.Diagnostic.Should().Contain("NOT REPORTED");
    }

    [Fact]
    public void Evaluate_SuffixMatch_DoesNotConflateGpt4oWithGpt4oMini()
    {
        // gpt-4o-mini's own usage entry should NOT satisfy a 'gpt-4o' request
        // (regression guard for the PS script's anchored-suffix regex).
        var requested = new Dictionary<string, int> { ["gpt-4o"] = 10 };
        var usage = new[] { new CognitiveServicesUsageEntry("Standard.gpt-4o-mini", CurrentValue: 5, Limit: 1000) };

        var result = ArmCognitiveServicesTpmProbe.Evaluate(Region, requested, usage);

        result.Passed.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_MultipleMatchingEntries_SumsCurrentAndTakesMaxLimit()
    {
        // Standard + Provisioned variants both matching 'gpt-4o' -> sum currentValue, max limit.
        var usage = new[]
        {
            new CognitiveServicesUsageEntry("Standard.gpt-4o", CurrentValue: 100, Limit: 500),
            new CognitiveServicesUsageEntry("Provisioned.gpt-4o", CurrentValue: 50, Limit: 1000),
        };

        var result = ArmCognitiveServicesTpmProbe.Evaluate(Region, SingleModelRequest, usage);

        // observed = 100 + 50 = 150; limit = max(500,1000) = 1000; projected = 150+150=300 <= 1000.
        result.Passed.Should().BeTrue();
    }

    // ---------- CheckAsync() end-to-end via real ArmClient + fake transport ----------

    [Fact]
    public async Task CheckAsync_CallsRealArmUsageEndpoint_AndEvaluatesResponse()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            request.RequestUri!.AbsolutePath.Should().Be(
                $"/subscriptions/{SubscriptionId}/providers/Microsoft.CognitiveServices/locations/{Region}/usages",
                "the probe must call the REAL usages endpoint, not a hard-coded stub");
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, """
                { "value": [ { "unit": "Count", "name": { "value": "Standard.gpt-4o", "localizedValue": "gpt-4o" }, "currentValue": 50.0, "limit": 300.0 },
                              { "unit": "Count", "name": { "value": "Standard.gpt-4o-mini", "localizedValue": "gpt-4o-mini" }, "currentValue": 50.0, "limit": 300.0 },
                              { "unit": "Count", "name": { "value": "Standard.text-embedding-3-large", "localizedValue": "e3l" }, "currentValue": 5.0, "limit": 100.0 },
                              { "unit": "Count", "name": { "value": "Standard.text-embedding-3-small", "localizedValue": "e3s" }, "currentValue": 5.0, "limit": 500.0 } ] }
                """);
        });
        var probe = new ArmCognitiveServicesTpmProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmCognitiveServicesTpmProbe>.Instance);
        var input = new PreflightProbeInput(
            "acme-corp", "tenant-1",
            new Dictionary<string, string> { ["region"] = Region, ["subscriptionId"] = SubscriptionId });

        var result = await probe.CheckAsync(input, CancellationToken.None);

        result.Passed.Should().BeTrue("all 4 default NFR-12 models fit within the fake usage/limit values");
        result.CheckName.Should().Be(PreflightCheckNames.AzureOpenAiTpmHeadroom);
        handler.RequestedUris.Should().ContainSingle();
    }

    [Fact]
    public async Task CheckAsync_MissingRegionParameter_ReturnsConfigErrorWithoutCallingArm()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ => throw new InvalidOperationException("must not call ARM"));
        var probe = new ArmCognitiveServicesTpmProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmCognitiveServicesTpmProbe>.Instance);
        var input = new PreflightProbeInput(
            "acme-corp", "tenant-1",
            new Dictionary<string, string> { ["subscriptionId"] = SubscriptionId }); // no region

        var result = await probe.CheckAsync(input, CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.Diagnostic.Should().Contain("'region'");
        handler.RequestedUris.Should().BeEmpty("config error must short-circuit before any ARM call");
    }
}
