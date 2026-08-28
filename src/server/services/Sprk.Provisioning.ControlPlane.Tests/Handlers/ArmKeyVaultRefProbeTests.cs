// -----------------------------------------------------------------------------
// ArmKeyVaultRefProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for ArmKeyVaultRefProbe (task 123, Wave G-2).
// Proves the REAL Azure.ResourceManager.AppService SDK call path (site GET +
// slot GET) via a fake HttpClientTransport — parity with
// ArmSubscriptionReadinessProbeTests.cs (task 121) and
// ArmDeploymentRunnerTests.cs (task 123). ADR-038 path #1.
//
// COVERAGE (maps to task 123 acceptance criterion #3 — "given a site whose
// KeyVaultReferenceIdentity is NOT the UAMI resource id, the probe returns a
// Fail/InfraFault outcome (not silently Pass) — proving it is a real read,
// not a stub"):
//   T1  Both slots match the expected UAMI resource id -> Match.
//   T2  Production slot does NOT match (still on SystemAssigned, the classic
//       T1 symptom) -> Mismatch, observed values captured for the diagnostic.
//   T3  Staging slot does NOT match -> Mismatch.
//   T4  ARM read failure (403, missing RBAC) -> THROWS RequestFailedException
//       (infra fault; the handler's outer catch classifies this
//       QuarantineRequired — per IArmKeyVaultRefProbe contract: "only genuine
//       infra faults throw").
//   T5  Real-call assertion — RequestedUris shows both the site GET and the
//       slot GET were actually invoked (not a hard-coded Match).
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmKeyVaultRefProbeTests
{
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";
    private const string AppServiceName = "sprk-acme-prod-bff";
    private const string StagingSlotName = "staging";
    private const string ExpectedUami =
        "/subscriptions/22222222-3333-4444-5555-666666666666/resourceGroups/rg-spaarke-acme-prod/" +
        "providers/Microsoft.ManagedIdentity/userAssignedIdentities/sprk-acme-prod-uami";

    private static ArmKeyVaultRefProbeInput NewInput() => new(
        SubscriptionId: SubscriptionId,
        ResourceGroupName: ResourceGroupName,
        AppServiceName: AppServiceName,
        StagingSlotName: StagingSlotName,
        ExpectedUserAssignedIdentityResourceId: ExpectedUami);

    // ---------- T1 Match ----------

    [Fact]
    public async Task VerifyKeyVaultReferenceIdentityAsync_BothSlotsMatch_ReturnsMatchViaGenuineArmCalls()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    ArmSdkTestFakes.WebSiteBody(SubscriptionId, ResourceGroupName, AppServiceName + "/" + StagingSlotName, ExpectedUami));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                ArmSdkTestFakes.WebSiteBody(SubscriptionId, ResourceGroupName, AppServiceName, ExpectedUami));
        });

        var probe = new ArmKeyVaultRefProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmKeyVaultRefProbe>.Instance);

        var result = await probe.VerifyKeyVaultReferenceIdentityAsync(NewInput(), CancellationToken.None);

        result.Should().BeOfType<ArmKeyVaultRefProbeResult.Match>();
        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.EndsWith(AppServiceName) && !uri.AbsolutePath.Contains("/slots/"),
            "asserts the production-slot GET was actually invoked over HTTP");
        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.EndsWith("/slots/" + StagingSlotName),
            "asserts the staging-slot GET was actually invoked over HTTP — not a hard-coded Match");
    }

    // ---------- T2 production mismatch (classic T1 symptom) ----------

    [Fact]
    public async Task VerifyKeyVaultReferenceIdentityAsync_ProductionSlotStillSystemAssigned_ReturnsMismatch()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    ArmSdkTestFakes.WebSiteBody(SubscriptionId, ResourceGroupName, AppServiceName + "/" + StagingSlotName, ExpectedUami));
            }
            // Classic T1 trap: Bicep never PATCHed keyVaultReferenceIdentity —
            // the site still reports "SystemAssigned" instead of the UAMI resource id.
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                ArmSdkTestFakes.WebSiteBody(SubscriptionId, ResourceGroupName, AppServiceName, "SystemAssigned"));
        });

        var probe = new ArmKeyVaultRefProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmKeyVaultRefProbe>.Instance);

        var result = await probe.VerifyKeyVaultReferenceIdentityAsync(NewInput(), CancellationToken.None);

        var mismatch = result.Should().BeOfType<ArmKeyVaultRefProbeResult.Mismatch>().Subject;
        mismatch.ObservedProductionSlotIdentity.Should().Be("SystemAssigned");
        mismatch.ObservedStagingSlotIdentity.Should().Be(ExpectedUami);
    }

    // ---------- T3 staging mismatch ----------

    [Fact]
    public async Task VerifyKeyVaultReferenceIdentityAsync_StagingSlotBlank_ReturnsMismatch()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                // ARM returns an empty string (not null/absent) when the
                // property is genuinely unset — the probe must normalize
                // this to a non-matching observed value, not silently pass.
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    ArmSdkTestFakes.WebSiteBody(SubscriptionId, ResourceGroupName, AppServiceName + "/" + StagingSlotName, string.Empty));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                ArmSdkTestFakes.WebSiteBody(SubscriptionId, ResourceGroupName, AppServiceName, ExpectedUami));
        });

        var probe = new ArmKeyVaultRefProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmKeyVaultRefProbe>.Instance);

        var result = await probe.VerifyKeyVaultReferenceIdentityAsync(NewInput(), CancellationToken.None);

        var mismatch = result.Should().BeOfType<ArmKeyVaultRefProbeResult.Mismatch>().Subject;
        mismatch.ObservedStagingSlotIdentity.Should().BeNull();
    }

    // ---------- T4 ARM infra fault ----------

    [Fact]
    public async Task VerifyKeyVaultReferenceIdentityAsync_ArmReadForbidden_Throws()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden,
                ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed", "The client does not have authorization to perform action.")));

        var probe = new ArmKeyVaultRefProbe(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmKeyVaultRefProbe>.Instance);

        var act = async () => await probe.VerifyKeyVaultReferenceIdentityAsync(NewInput(), CancellationToken.None);

        await act.Should().ThrowAsync<Azure.RequestFailedException>();
    }

    // ---------- Argument guards ----------

    [Fact]
    public async Task VerifyKeyVaultReferenceIdentityAsync_MissingAppServiceName_Throws()
    {
        var probe = new ArmKeyVaultRefProbe(
            ArmSdkTestFakes.NewArmClient(ArmSdkTestFakes.NewHandler(_ => throw new InvalidOperationException("must not call ARM"))),
            NullLogger<ArmKeyVaultRefProbe>.Instance);

        var input = new ArmKeyVaultRefProbeInput(SubscriptionId, ResourceGroupName, string.Empty, StagingSlotName, ExpectedUami);

        var act = async () => await probe.VerifyKeyVaultReferenceIdentityAsync(input, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}

/// <summary>
/// Task 123 (Wave G-2) extension of the shared <see cref="ArmSdkTestFakes"/>
/// partial class — WebSite/WebSiteSlot response body builder.
/// </summary>
internal static partial class ArmSdkTestFakes
{
    public static string WebSiteBody(string subscriptionId, string resourceGroupName, string siteAndSlotName, string keyVaultReferenceIdentity) =>
        $$"""
        {
          "id": "/subscriptions/{{subscriptionId}}/resourceGroups/{{resourceGroupName}}/providers/Microsoft.Web/sites/{{siteAndSlotName}}",
          "name": "{{siteAndSlotName}}",
          "location": "westus2",
          "properties": {
            "keyVaultReferenceIdentity": "{{keyVaultReferenceIdentity}}"
          }
        }
        """;
}
