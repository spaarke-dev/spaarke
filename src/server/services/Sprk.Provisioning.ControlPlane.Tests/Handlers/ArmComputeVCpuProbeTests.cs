// -----------------------------------------------------------------------------
// ArmComputeVCpuProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for ArmComputeVCpuProbe (task 120, Wave G-2).
// Same two-layer approach as ArmCognitiveServicesTpmProbeTests.cs: pure
// Evaluate() boundary-case tests + one CheckAsync() end-to-end test against a
// REAL ArmClient built with the shared fake-transport helper (ArmSdkTestFakes,
// task 121, reused per CLAUDE.md §11).
//
// ADR-038 alignment: pure C# unit tests. No live Azure, no
// Mock&lt;HttpMessageHandler&gt;, no SDK-client wrapper mock.
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.Preflight;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmComputeVCpuProbeTests
{
    private const string Region = "eastus";
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";

    private static readonly IReadOnlyDictionary<string, int> DefaultFamilyRequest =
        new Dictionary<string, int> { ["standardDv5Family"] = 8 };

    // ---------- Evaluate() boundary cases ----------

    [Fact]
    public void Evaluate_UnderThreshold_Passes()
    {
        var usage = new[] { new ComputeUsageEntry("standardDv5Family", CurrentValue: 4, Limit: 100) };

        var result = ArmComputeVCpuProbe.Evaluate(Region, DefaultFamilyRequest, usage);

        result.Passed.Should().BeTrue("observed 4 + requested 8 = 12 <= limit 100");
    }

    [Fact]
    public void Evaluate_ExactlyAtThreshold_Passes()
    {
        var usage = new[] { new ComputeUsageEntry("standardDv5Family", CurrentValue: 92, Limit: 100) };

        var result = ArmComputeVCpuProbe.Evaluate(Region, DefaultFamilyRequest, usage);

        result.Passed.Should().BeTrue("projected 100 == limit 100 is within threshold (<=)");
    }

    [Fact]
    public void Evaluate_OverThreshold_FailsWithShortfallDiagnostic()
    {
        var usage = new[] { new ComputeUsageEntry("standardDv5Family", CurrentValue: 95, Limit: 100) };

        var result = ArmComputeVCpuProbe.Evaluate(Region, DefaultFamilyRequest, usage);

        result.Passed.Should().BeFalse("observed 95 + requested 8 = 103 > limit 100");
        result.Diagnostic.Should().Contain("SHORTFALL: 3 vCPU");
    }

    [Fact]
    public void Evaluate_FamilyNotReported_FailsAsNotReported()
    {
        var usage = new[] { new ComputeUsageEntry("standardFsv2Family", CurrentValue: 2, Limit: 100) };

        var result = ArmComputeVCpuProbe.Evaluate(Region, DefaultFamilyRequest, usage);

        result.Passed.Should().BeFalse();
        result.Diagnostic.Should().Contain("NOT REPORTED");
    }

    [Fact]
    public void Evaluate_ExactCaseInsensitiveMatch_NotSuffixMatch()
    {
        // Compute family matching is EXACT (case-insensitive), unlike the TPM
        // probe's suffix-regex match — a partial name must NOT satisfy the request.
        var usage = new[] { new ComputeUsageEntry("STANDARDDV5FAMILY", CurrentValue: 1, Limit: 50) };

        var result = ArmComputeVCpuProbe.Evaluate(Region, DefaultFamilyRequest, usage);

        result.Passed.Should().BeTrue("case-insensitive exact match must still resolve");
    }

    // ---------- CheckAsync() end-to-end via real ArmClient + fake transport ----------

    [Fact]
    public async Task CheckAsync_CallsRealArmUsageEndpoint_AndEvaluatesResponse()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            request.RequestUri!.AbsolutePath.Should().Be(
                $"/subscriptions/{SubscriptionId}/providers/Microsoft.Compute/locations/{Region}/usages",
                "the probe must call the REAL usages endpoint, not a hard-coded stub");
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, """
                { "value": [ { "unit": "Count", "currentValue": 4, "limit": 100, "name": { "value": "standardDv5Family", "localizedValue": "Standard Dv5 Family vCPUs" } } ] }
                """);
        });
        var probe = new ArmComputeVCpuProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmComputeVCpuProbe>.Instance);
        var input = new PreflightProbeInput(
            "acme-corp", "tenant-1",
            new Dictionary<string, string> { ["region"] = Region, ["subscriptionId"] = SubscriptionId });

        var result = await probe.CheckAsync(input, CancellationToken.None);

        result.Passed.Should().BeTrue("4 + 8 = 12 <= limit 100");
        result.CheckName.Should().Be(PreflightCheckNames.SubscriptionVCpuQuota);
        handler.RequestedUris.Should().ContainSingle();
    }

    [Fact]
    public async Task CheckAsync_MissingSubscriptionIdParameter_ReturnsConfigErrorWithoutCallingArm()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ => throw new InvalidOperationException("must not call ARM"));
        var probe = new ArmComputeVCpuProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmComputeVCpuProbe>.Instance);
        var input = new PreflightProbeInput(
            "acme-corp", "tenant-1",
            new Dictionary<string, string> { ["region"] = Region }); // no subscriptionId

        var result = await probe.CheckAsync(input, CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.Diagnostic.Should().Contain("'subscriptionId'");
        handler.RequestedUris.Should().BeEmpty("config error must short-circuit before any ARM call");
    }
}
