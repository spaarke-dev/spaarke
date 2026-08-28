// -----------------------------------------------------------------------------
// ArmWhatIfDriftDetectorTests.cs
//
// L2 CONTROL-PLANE unit tests for ArmWhatIfDriftDetector (task 123, Wave
// G-2). Proves the REAL Azure.ResourceManager.Resources WhatIfAsync SDK call
// path via a fake HttpClientTransport — parity with ArmDeploymentRunnerTests.cs.
// ADR-038 path #1.
//
// GROUND-TRUTHED RESPONSE SHAPE (task 123 POML's xhigh-effort directive):
// a spike against the installed Azure.ResourceManager.Resources 1.11.2
// package found that WhatIfOperationResult.Changes deserializes ONLY when
// the fake response wraps `changes` under a `properties` object —
// `{ "status": "...", "properties": { "changes": [...] } }` — NOT the flatter
// `{ "status": "...", "changes": [...] }` shape a naive reading of the REST
// docs might suggest. Verified empirically (both via the full ArmClient
// pipeline AND via WhatIfOperationResult's internal
// DeserializeWhatIfOperationResult(JsonElement) called directly) before
// writing this file's fakes — this is exactly the class of ground-truthing
// gap the POML's xhigh-effort directive exists to catch; guessing the flat
// shape would have shipped a drift detector that silently reports zero
// changes on every real upgrade run.
//
// COVERAGE (maps to task 123 acceptance criterion #4 — "a fake WhatIfChange[]
// containing a Delete-class change is classified as a drift-Fail outcome,
// matching the handler's existing severity mapping"):
//   T1  All-benign changes (NoChange / Ignore / Create) -> NoDrift.
//   T2  A Delete-class change among otherwise-benign changes -> DriftDetected,
//       report captures the resourceId + changeType.
//   T3  A Modify-class change -> DriftDetected.
//   T4  No changes at all -> NoDrift.
//   T5  ARM rejects the what-if call (403) -> THROWS (infra fault; handler's
//       outer catch classifies Resumable per H2aBicepInfraDeployHandler's
//       upgrade-mode branch).
//   T6  Real-call assertion — the what-if POST was actually invoked
//       (RequestedUris), not a hard-coded NoDrift.
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmWhatIfDriftDetectorTests
{
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";

    private static BicepInfraDeployOptions NewOptions() => new()
    {
        ProvisioningArtifactsContainerUri = "https://fakeaccount.blob.core.windows.net/provisioning-artifacts",
        ArmManifestBlobName = "provisioning-arm-latest.json",
    };

    private static BicepDeployRequest NewRequest() => new(
        CustomerId: "acme",
        TenantId: "00000000-1111-2222-3333-444444444444",
        SubscriptionId: SubscriptionId,
        TenancyModel: "Model2Dedicated",
        BicepVersion: "abc123",
        EnvironmentName: "prod",
        Location: "westus2",
        SignalREnabled: false);

    private static ArmWhatIfDriftDetector NewDetector(Func<HttpRequestMessage, HttpResponseMessage> templateAndWhatIfResponder)
    {
        var handler = ArmSdkTestFakes.NewHandler(templateAndWhatIfResponder);
        return new ArmWhatIfDriftDetector(
            ArmSdkTestFakes.NewArmClient(handler),
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<ArmWhatIfDriftDetector>.Instance);
    }

    private static HttpResponseMessage RespondTemplateAndManifest(HttpRequestMessage request, Func<HttpResponseMessage> whatIfResponder)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("provisioning-arm-latest.json"))
        {
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.ArmManifestBody());
        }
        if (path.EndsWith("customer-arm-2026.08.19-1.json"))
        {
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, """{"resources":[]}""");
        }
        if (path.Contains("whatIf", StringComparison.OrdinalIgnoreCase))
        {
            return whatIfResponder();
        }
        throw new InvalidOperationException("unexpected request: " + path);
    }

    // ---------- T1 all-benign ----------

    [Fact]
    public async Task DetectDriftAsync_AllBenignChanges_ReturnsNoDrift()
    {
        var detector = NewDetector(request => RespondTemplateAndManifest(request, () =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.WhatIfResultBody(
                ("NoChange", "/subscriptions/x/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/foo"),
                ("Ignore", "/subscriptions/x/resourceGroups/rg/providers/Microsoft.KeyVault/vaults/bar"),
                ("Create", "/subscriptions/x/resourceGroups/rg/providers/Microsoft.ServiceBus/namespaces/baz")))));

        var result = await detector.DetectDriftAsync(NewRequest(), CancellationToken.None);

        result.Should().BeOfType<UpgradeDriftDetectionResult.NoDrift>();
    }

    // ---------- T2 Delete-class change ----------

    [Fact]
    public async Task DetectDriftAsync_DeleteClassChange_ReturnsDriftDetected()
    {
        var detector = NewDetector(request => RespondTemplateAndManifest(request, () =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.WhatIfResultBody(
                ("NoChange", "/subscriptions/x/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/foo"),
                ("Delete", "/subscriptions/x/resourceGroups/rg/providers/Microsoft.KeyVault/vaults/bar")))));

        var result = await detector.DetectDriftAsync(NewRequest(), CancellationToken.None);

        var drift = result.Should().BeOfType<UpgradeDriftDetectionResult.DriftDetected>().Subject;
        drift.DriftReport.Should().Contain("Delete");
        drift.DriftReport.Should().Contain("Microsoft.KeyVault/vaults/bar");
        drift.DriftReport.Should().NotContain("storageAccounts/foo", "benign NoChange entries must not be reported as drift");
    }

    // ---------- T3 Modify-class change ----------

    [Fact]
    public async Task DetectDriftAsync_ModifyClassChange_ReturnsDriftDetected()
    {
        var detector = NewDetector(request => RespondTemplateAndManifest(request, () =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.WhatIfResultBody(
                ("Modify", "/subscriptions/x/resourceGroups/rg/providers/Microsoft.DocumentDB/databaseAccounts/cosmos1")))));

        var result = await detector.DetectDriftAsync(NewRequest(), CancellationToken.None);

        result.Should().BeOfType<UpgradeDriftDetectionResult.DriftDetected>();
    }

    // ---------- T4 no changes ----------

    [Fact]
    public async Task DetectDriftAsync_NoChanges_ReturnsNoDrift()
    {
        var detector = NewDetector(request => RespondTemplateAndManifest(request, () =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.WhatIfResultBody())));

        var result = await detector.DetectDriftAsync(NewRequest(), CancellationToken.None);

        result.Should().BeOfType<UpgradeDriftDetectionResult.NoDrift>();
    }

    // ---------- T5 ARM rejection ----------

    [Fact]
    public async Task DetectDriftAsync_ArmRejectsWhatIf_Throws()
    {
        var detector = NewDetector(request => RespondTemplateAndManifest(request, () =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden,
                ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed", "The client does not have authorization."))));

        var act = async () => await detector.DetectDriftAsync(NewRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<Azure.RequestFailedException>();
    }

    // ---------- T6 real-call assertion ----------

    [Fact]
    public async Task DetectDriftAsync_InvokesRealWhatIfEndpoint_NotHardCoded()
    {
        Uri? whatIfUri = null;
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var response = RespondTemplateAndManifest(request, () =>
                ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, ArmSdkTestFakes.WhatIfResultBody()));
            if (request.RequestUri!.AbsolutePath.Contains("whatIf", StringComparison.OrdinalIgnoreCase))
            {
                whatIfUri = request.RequestUri;
            }
            return response;
        });
        var detector = new ArmWhatIfDriftDetector(
            ArmSdkTestFakes.NewArmClient(handler),
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<ArmWhatIfDriftDetector>.Instance);

        await detector.DetectDriftAsync(NewRequest(), CancellationToken.None);

        whatIfUri.Should().NotBeNull();
        whatIfUri!.AbsolutePath.Should().Contain($"/subscriptions/{SubscriptionId}/providers/Microsoft.Resources/deployments/");
        whatIfUri.AbsolutePath.Should().EndWith("/whatIf");
    }
}

/// <summary>
/// Task 123 (Wave G-2) extension of the shared <see cref="ArmSdkTestFakes"/>
/// partial class — WhatIf response body builder.
/// </summary>
internal static partial class ArmSdkTestFakes
{
    /// <summary>
    /// Builds a what-if result body. IMPORTANT: <c>changes</c> MUST be
    /// nested under <c>properties</c> — see this file's header note for the
    /// empirical ground-truthing that found the flat shape does not
    /// deserialize.
    /// </summary>
    public static string WhatIfResultBody(params (string ChangeType, string ResourceId)[] changes)
    {
        var items = changes.Select(c => $$"""{ "resourceId": "{{c.ResourceId}}", "changeType": "{{c.ChangeType}}" }""");
        return $$"""
        {
          "status": "Succeeded",
          "properties": {
            "changes": [{{string.Join(",", items)}}]
          }
        }
        """;
    }
}
