// -----------------------------------------------------------------------------
// ArmAppServiceIdentityPatcherTests.cs
//
// L2 CONTROL-PLANE unit tests for ArmAppServiceIdentityPatcher (task 125,
// Wave G-2). Proves the REAL Azure.ResourceManager.AppService SDK PATCH call
// path (site PUT + slot PUT) via a fake HttpClientTransport — parity with
// ArmKeyVaultRefProbeTests.cs (task 123, the T1 READ-side sibling this
// class's WRITE-side pairs with). ADR-038 path #1.
//
// GROUND-TRUTHED SDK SHAPES (verified via reflection against the installed
// Azure.ResourceManager.AppService 1.5.0 package BEFORE writing this file):
//   - WebSiteResource.UpdateAsync(SitePatchInfo, ct) -> Task<Response<WebSiteResource>>
//   - WebSiteSlotResource.UpdateAsync(SitePatchInfo, ct) -> Task<Response<WebSiteSlotResource>>
//     (obtained via WebSiteResource.GetWebSiteSlotAsync(slotName, ct))
//   - SitePatchInfo has a parameterless ctor + settable KeyVaultReferenceIdentity.
//
// COVERAGE (maps directly to this project's MUST rule — "PATCH App Service
// keyVaultReferenceIdentity to UAMI on BOTH slots — do not patch production
// only" — and CLAUDE.md directive #4 "add a completeness ArchTest that
// verifies your H4 handler patches BOTH slots"):
//   T1  Both slots succeed -> Success; BOTH the site PUT and the slot PUT are
//       asserted to have actually reached ARM over HTTP (RequestedUris) —
//       THE completeness assertion.
//   T2  Production PATCH fails (403), staging PATCH succeeds -> staging PUT
//       is STILL attempted (unconditional-both-slots invariant) AND the
//       overall result is Failure (a partial patch is not success).
//   T3  Staging PATCH fails, production succeeds -> mirror of T2 (order-
//       independence — proves the "staging only if prod succeeds"
//       anti-pattern is NOT present).
//   T4  Both slots fail -> Failure with both diagnostics concatenated.
//   T5  Missing AppServiceName -> ArgumentException BEFORE any ARM call.
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmAppServiceIdentityPatcherTests
{
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";
    private const string AppServiceName = "sprk-acme-prod-bff";
    private const string StagingSlotName = "staging";
    private const string UamiResourceId =
        "/subscriptions/22222222-3333-4444-5555-666666666666/resourceGroups/rg-spaarke-acme-prod/" +
        "providers/Microsoft.ManagedIdentity/userAssignedIdentities/sprk-acme-prod-uami";

    private static AppServiceIdentityPatchInput NewInput() => new(
        SubscriptionId: SubscriptionId,
        ResourceGroupName: ResourceGroupName,
        AppServiceName: AppServiceName,
        StagingSlotName: StagingSlotName,
        UserAssignedIdentityResourceId: UamiResourceId);

    /// <summary>
    /// Slot response body with a CORRECT `.../sites/{app}/slots/{slot}` resource
    /// id. Unlike the shared <see cref="ArmSdkTestFakes.WebSiteBody"/> helper
    /// (task 123 — used ONLY by READ-only probes that never call
    /// <c>.UpdateAsync()</c> on the returned resource, so its `id` shape never
    /// mattered there), this patcher DOES call
    /// <c>WebSiteSlotResource.UpdateAsync</c> on the resource returned by
    /// <c>GetWebSiteSlotAsync</c> — the SDK derives the slot name from the
    /// resource's `Data.Id` path segments for that follow-up call, so the id
    /// MUST contain a genuine `/slots/{slot}` segment (ground-truthed via a
    /// scratch probe: an incorrect id shape throws
    /// `ArgumentException: Value cannot be an empty string. (Parameter 'slot')`
    /// from `WebAppsRestOperations.UpdateSlotAsync` — a real SDK behavior this
    /// test caught, not a guess).
    /// </summary>
    private static string SlotBody(string kvRefIdentity) => $$"""
        {
          "id": "/subscriptions/{{SubscriptionId}}/resourceGroups/{{ResourceGroupName}}/providers/Microsoft.Web/sites/{{AppServiceName}}/slots/{{StagingSlotName}}",
          "name": "{{AppServiceName}}/{{StagingSlotName}}",
          "location": "westus2",
          "properties": { "keyVaultReferenceIdentity": "{{kvRefIdentity}}" }
        }
        """;

    // ---------- T1 both slots patched (completeness) ----------

    [Fact]
    public async Task PatchAsync_BothSlotsSucceed_BothPutsReachArmAndReturnsSuccess()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SlotBody(UamiResourceId));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                ArmSdkTestFakes.WebSiteBody(SubscriptionId, ResourceGroupName, AppServiceName, UamiResourceId));
        });

        var patcher = new ArmAppServiceIdentityPatcher(
            ArmSdkTestFakes.NewArmClient(handler),
            Options.Create(new KvSecretsPopulationOptions()),
            NullLogger<ArmAppServiceIdentityPatcher>.Instance);

        var result = await patcher.PatchKeyVaultReferenceIdentityAsync(NewInput(), CancellationToken.None);

        result.Should().BeOfType<AppServiceIdentityPatchResult.Success>();

        // Production: WebSiteResource is a LOCAL reference (GetWebSiteResource
        // never calls ARM); the ONLY network call to this exact path is the
        // UpdateAsync PATCH itself — a single Contain proves the PATCH fired.
        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.EndsWith(AppServiceName) && !uri.AbsolutePath.Contains("/slots/"),
            "the production-slot PATCH must actually reach ARM over HTTP — not a hard-coded Success");

        // Staging: GetWebSiteSlotAsync (slot lookup) AND WebSiteSlotResource
        // .UpdateAsync (the PATCH) both hit the SAME path — a single Contain
        // would pass even if only the lookup happened and the PATCH never
        // fired. Assert the path was hit AT LEAST TWICE (lookup + PATCH) so
        // this is a genuine both-slots-PATCHED completeness proof, not just
        // a both-slots-READ proof.
        handler.RequestedUris.Count(uri => uri.AbsolutePath.EndsWith("/slots/" + StagingSlotName))
            .Should().BeGreaterThanOrEqualTo(2,
                "the staging-slot PATCH (not just the slot lookup) must actually reach ARM over HTTP — " +
                "the both-slots MUST rule's completeness check");
    }

    // ---------- T2 prod fails, staging still attempted ----------

    [Fact]
    public async Task PatchAsync_ProdFails_StagingStillAttemptedAndOverallFailure()
    {
        var stagingCalled = false;
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                stagingCalled = true;
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SlotBody(UamiResourceId));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden,
                ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed", "denied"));
        });

        var patcher = new ArmAppServiceIdentityPatcher(
            ArmSdkTestFakes.NewArmClient(handler),
            Options.Create(new KvSecretsPopulationOptions()),
            NullLogger<ArmAppServiceIdentityPatcher>.Instance);

        var result = await patcher.PatchKeyVaultReferenceIdentityAsync(NewInput(), CancellationToken.None);

        var failure = result.Should().BeOfType<AppServiceIdentityPatchResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("prod slot");
        stagingCalled.Should().BeTrue(
            "staging PATCH must be attempted UNCONDITIONALLY even though prod already failed — no short-circuit");
    }

    // ---------- T3 staging fails, prod still attempted (order-independence) ----------

    [Fact]
    public async Task PatchAsync_StagingFails_ProdStillSucceedsButOverallFailure()
    {
        var prodCalled = false;
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden,
                    ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed", "denied"));
            }
            prodCalled = true;
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                ArmSdkTestFakes.WebSiteBody(SubscriptionId, ResourceGroupName, AppServiceName, UamiResourceId));
        });

        var patcher = new ArmAppServiceIdentityPatcher(
            ArmSdkTestFakes.NewArmClient(handler),
            Options.Create(new KvSecretsPopulationOptions()),
            NullLogger<ArmAppServiceIdentityPatcher>.Instance);

        var result = await patcher.PatchKeyVaultReferenceIdentityAsync(NewInput(), CancellationToken.None);

        var failure = result.Should().BeOfType<AppServiceIdentityPatchResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("staging slot");
        prodCalled.Should().BeTrue("production PATCH is attempted regardless of staging's outcome (it runs first)");
    }

    // ---------- T4 both fail ----------

    [Fact]
    public async Task PatchAsync_BothSlotsFail_ReturnsFailureWithBothDiagnostics()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden, ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed", "denied")));

        var patcher = new ArmAppServiceIdentityPatcher(
            ArmSdkTestFakes.NewArmClient(handler),
            Options.Create(new KvSecretsPopulationOptions()),
            NullLogger<ArmAppServiceIdentityPatcher>.Instance);

        var result = await patcher.PatchKeyVaultReferenceIdentityAsync(NewInput(), CancellationToken.None);

        var failure = result.Should().BeOfType<AppServiceIdentityPatchResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("prod slot");
        failure.Diagnostic.Should().Contain("staging slot");
    }

    // ---------- T5 argument guard ----------

    [Fact]
    public async Task PatchAsync_MissingAppServiceName_ThrowsWithoutCallingArm()
    {
        var patcher = new ArmAppServiceIdentityPatcher(
            ArmSdkTestFakes.NewArmClient(ArmSdkTestFakes.NewHandler(_ => throw new InvalidOperationException("must not call ARM"))),
            Options.Create(new KvSecretsPopulationOptions()),
            NullLogger<ArmAppServiceIdentityPatcher>.Instance);

        var input = new AppServiceIdentityPatchInput(SubscriptionId, ResourceGroupName, string.Empty, StagingSlotName, UamiResourceId);

        var act = async () => await patcher.PatchKeyVaultReferenceIdentityAsync(input, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
