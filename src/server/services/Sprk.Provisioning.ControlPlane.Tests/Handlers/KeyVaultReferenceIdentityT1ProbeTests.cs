// -----------------------------------------------------------------------------
// KeyVaultReferenceIdentityT1ProbeTests.cs
//
// L2 CONTROL-PLANE unit tests for KeyVaultReferenceIdentityT1Probe (task 171,
// Wave G-7 Batch G-7A2.1 -- pipelined with H2a task 123 + H4 task 125).
// ADR-038 path #1 -- fake-transport SDK tests via the shared ArmSdkTestFakes
// (parity with ArmKeyVaultRefProbeTests / ArmSubscriptionReadinessProbeTests /
// T5SlotMiKvRbacTrapProbeTests). No live Azure / no Mock<HttpMessageHandler>
// per testing.md's ban.
//
// COVERAGE (maps to POML acceptance criteria):
//   T1  Both slots consistent (kv-ref binds a UAMI attached to that slot)
//       -> Passed(T1). Real ARM HTTP calls confirmed via RequestedUris.
//   T2  Production slot missing keyVaultReferenceIdentity (T1 not patched)
//       -> Failed(T1), diagnostic mentions MISSING PATCH + prod slot.
//   T3  Staging slot has keyVaultReferenceIdentity == "SystemAssigned"
//       sentinel -> Failed(T1), diagnostic mentions SYSTEM-ASSIGNED + staging.
//   T4  Production slot's keyVaultReferenceIdentity points at a UAMI RID not
//       in that slot's Identity.UserAssignedIdentities (ORPHAN REFERENCE)
//       -> Failed(T1) with OrphanReference classification.
//   T5  Both slots inconsistent (both MissingPatch) -> Failed(T1) mentions
//       both slots.
//   T6  ARM read forbidden (403) on prod slot GET -> InfraFault(T1) with
//       RequestFailedException diagnostic.
//   T7  Missing SubscriptionId -> InfraFault(T1). ARM never called.
//   T8  Missing ResourceGroupName -> InfraFault(T1). ARM never called.
//   T9  Missing AppServiceName -> InfraFault(T1). ARM never called.
//   T10 Cancellation (OperationCanceledException) propagates -- never
//       swallowed as InfraFault (parity with sibling T2 / T5 probes).
//   T11 Kind property is TrapKind.T1KeyVaultReferenceIdentity (constant
//       contract for task 185 aggregate composition).
//   T12 IsSlotConsistent classifier unit tests (case-insensitive kv-ref
//       matching + null/empty/sentinel handling).
//   T13 ClassifyFailureShape classifier -- pins the enum mapping so unit
//       tests can be relied on when diagnostic strings change.
//   T14 ExtractUserAssignedIdentityRids handles null Identity + empty dict.
// -----------------------------------------------------------------------------

using System.Net;
using Azure.ResourceManager.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class KeyVaultReferenceIdentityT1ProbeTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h13-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";
    private const string AppServiceName = "sprk-acme-prod-bff";
    private const string ExpectedUamiRid =
        "/subscriptions/22222222-3333-4444-5555-666666666666/resourceGroups/rg-spaarke-acme-prod/" +
        "providers/Microsoft.ManagedIdentity/userAssignedIdentities/sprk-acme-prod-uami";
    private const string OtherUamiRid =
        "/subscriptions/22222222-3333-4444-5555-666666666666/resourceGroups/rg-spaarke-acme-prod/" +
        "providers/Microsoft.ManagedIdentity/userAssignedIdentities/some-other-uami";

    private static TrapVerificationRequest BuildRequest(
        string? subscriptionId = SubscriptionId,
        string? resourceGroupName = ResourceGroupName,
        string? appServiceName = AppServiceName) => new(
        CustomerId: CustomerId,
        RunId: RunId,
        TenantId: TenantId,
        SubscriptionId: subscriptionId ?? string.Empty,
        DataverseUrl: string.Empty,
        BffAppRegId: string.Empty,
        UamiClientId: string.Empty,
        KeyVaultName: string.Empty,
        AppServiceName: appServiceName ?? string.Empty,
        ResourceGroupName: resourceGroupName ?? string.Empty);

    private static KeyVaultReferenceIdentityT1Probe BuildProbe(FakeArmHttpMessageHandler handler)
        => new(
            ArmSdkTestFakes.NewArmClient(handler),
            NullLogger<KeyVaultReferenceIdentityT1Probe>.Instance);

    // ---------- T1 happy path ----------

    [Fact]
    public async Task ProbeAsync_BothSlotsConsistent_ReturnsPassedViaGenuineArmCalls()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var siteName = path.EndsWith("/slots/staging") ? AppServiceName + "/staging" : AppServiceName;
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                ArmSdkTestFakes.WebSiteBodyWithIdentity(
                    SubscriptionId, ResourceGroupName, siteName,
                    keyVaultReferenceIdentity: ExpectedUamiRid,
                    userAssignedIdentityRids: new[] { ExpectedUamiRid }));
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>()
            .Which.Kind.Should().Be(TrapKind.T1KeyVaultReferenceIdentity);
        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.EndsWith(AppServiceName) && !uri.AbsolutePath.Contains("/slots/"),
            "asserts the production-slot GET was actually invoked over HTTP");
        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.EndsWith("/slots/staging"),
            "asserts the staging-slot GET was actually invoked over HTTP -- not a hard-coded Pass");
    }

    // ---------- T2 production slot MISSING PATCH ----------

    [Fact]
    public async Task ProbeAsync_ProductionSlotMissingKvRef_ReturnsFailedWithMissingPatchDiagnostic()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/slots/staging"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    ArmSdkTestFakes.WebSiteBodyWithIdentity(
                        SubscriptionId, ResourceGroupName, AppServiceName + "/staging",
                        keyVaultReferenceIdentity: ExpectedUamiRid,
                        userAssignedIdentityRids: new[] { ExpectedUamiRid }));
            }
            // Classic T1: Bicep never PATCHed keyVaultReferenceIdentity on prod slot.
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                ArmSdkTestFakes.WebSiteBodyWithIdentity(
                    SubscriptionId, ResourceGroupName, AppServiceName,
                    keyVaultReferenceIdentity: null,
                    userAssignedIdentityRids: new[] { ExpectedUamiRid }));
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(TrapKind.T1KeyVaultReferenceIdentity);
        failed.Diagnostic.Should().Contain("VIOLATED")
            .And.Contain(AppServiceName)
            .And.Contain("MISSING PATCH")
            .And.Contain("production");
    }

    // ---------- T3 staging slot SYSTEM-ASSIGNED sentinel ----------

    [Fact]
    public async Task ProbeAsync_StagingSlotSystemAssignedSentinel_ReturnsFailedWithSystemAssignedDiagnostic()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/slots/staging"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    ArmSdkTestFakes.WebSiteBodyWithIdentity(
                        SubscriptionId, ResourceGroupName, AppServiceName + "/staging",
                        keyVaultReferenceIdentity: KeyVaultReferenceIdentityT1Probe.SystemAssignedSentinel,
                        userAssignedIdentityRids: new[] { ExpectedUamiRid }));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                ArmSdkTestFakes.WebSiteBodyWithIdentity(
                    SubscriptionId, ResourceGroupName, AppServiceName,
                    keyVaultReferenceIdentity: ExpectedUamiRid,
                    userAssignedIdentityRids: new[] { ExpectedUamiRid }));
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(TrapKind.T1KeyVaultReferenceIdentity);
        failed.Diagnostic.Should().Contain("SYSTEM-ASSIGNED").And.Contain("staging");
    }

    // ---------- T4 production ORPHAN REFERENCE ----------

    [Fact]
    public async Task ProbeAsync_ProductionOrphanReference_ReturnsFailedWithOrphanDiagnostic()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/slots/staging"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    ArmSdkTestFakes.WebSiteBodyWithIdentity(
                        SubscriptionId, ResourceGroupName, AppServiceName + "/staging",
                        keyVaultReferenceIdentity: ExpectedUamiRid,
                        userAssignedIdentityRids: new[] { ExpectedUamiRid }));
            }
            // Prod slot: kv-ref points at OtherUamiRid, but only ExpectedUamiRid is attached.
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                ArmSdkTestFakes.WebSiteBodyWithIdentity(
                    SubscriptionId, ResourceGroupName, AppServiceName,
                    keyVaultReferenceIdentity: OtherUamiRid,
                    userAssignedIdentityRids: new[] { ExpectedUamiRid }));
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Kind.Should().Be(TrapKind.T1KeyVaultReferenceIdentity);
        failed.Diagnostic.Should().Contain("ORPHAN REFERENCE").And.Contain("production");
    }

    // ---------- T5 both slots inconsistent ----------

    [Fact]
    public async Task ProbeAsync_BothSlotsMissingPatch_ReturnsFailedMentioningBoth()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var siteName = path.EndsWith("/slots/staging") ? AppServiceName + "/staging" : AppServiceName;
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                ArmSdkTestFakes.WebSiteBodyWithIdentity(
                    SubscriptionId, ResourceGroupName, siteName,
                    keyVaultReferenceIdentity: string.Empty,
                    userAssignedIdentityRids: new[] { ExpectedUamiRid }));
        });
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var failed = outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Subject;
        failed.Diagnostic.Should()
            .Contain("MISSING PATCH")
            .And.Contain("production")
            .And.Contain("staging");
    }

    // ---------- T6 ARM 403 InfraFault ----------

    [Fact]
    public async Task ProbeAsync_ArmReadForbidden_ReturnsInfraFault()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden,
                ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed",
                    "The client does not have authorization to perform action.")));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T1KeyVaultReferenceIdentity);
        infra.Diagnostic.Should().Contain("verdict deferred")
            .And.Contain("RequestFailedException")
            .And.Contain(AppServiceName);
    }

    // ---------- T7-T9 Input guards ----------

    [Fact]
    public async Task ProbeAsync_MissingSubscriptionId_ReturnsInfraFaultWithoutArmCall()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ => throw new InvalidOperationException("must not call ARM"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(subscriptionId: string.Empty), CancellationToken.None);

        var infra = outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(TrapKind.T1KeyVaultReferenceIdentity);
        infra.Diagnostic.Should().Contain("SubscriptionId").And.Contain("empty");
        handler.RequestedUris.Should().BeEmpty();
    }

    [Fact]
    public async Task ProbeAsync_MissingResourceGroupName_ReturnsInfraFaultWithoutArmCall()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ => throw new InvalidOperationException("must not call ARM"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(resourceGroupName: string.Empty), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("ResourceGroupName").And.Contain("empty");
        handler.RequestedUris.Should().BeEmpty();
    }

    [Fact]
    public async Task ProbeAsync_MissingAppServiceName_ReturnsInfraFaultWithoutArmCall()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ => throw new InvalidOperationException("must not call ARM"));
        var probe = BuildProbe(handler);

        var outcome = await probe.ProbeAsync(BuildRequest(appServiceName: string.Empty), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>()
            .Which.Diagnostic.Should().Contain("AppServiceName").And.Contain("empty");
        handler.RequestedUris.Should().BeEmpty();
    }

    // ---------- T10 Cancellation propagates ----------

    [Fact]
    public async Task ProbeAsync_Cancellation_PropagatesOperationCanceled()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            throw new OperationCanceledException("simulated cancellation from ARM"));
        var probe = BuildProbe(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await probe.ProbeAsync(BuildRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---------- T11 Kind constant ----------

    [Fact]
    public void Kind_IsT1KeyVaultReferenceIdentity()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ => throw new InvalidOperationException("Kind property must not call ARM"));
        var probe = BuildProbe(handler);

        probe.Kind.Should().Be(TrapKind.T1KeyVaultReferenceIdentity);
    }

    // ---------- T12 IsSlotConsistent classifier ----------

    [Theory]
    [InlineData(null, "urn:uami:a", false)]
    [InlineData("", "urn:uami:a", false)]
    [InlineData("SystemAssigned", "urn:uami:a", false)]
    [InlineData("systemassigned", "urn:uami:a", false)]                 // case-insensitive sentinel
    [InlineData("urn:uami:a", "urn:uami:a", true)]                       // exact match
    [InlineData("URN:UAMI:A", "urn:uami:a", true)]                       // case-insensitive rid match
    [InlineData("urn:uami:b", "urn:uami:a", false)]                      // orphan reference
    public void IsSlotConsistent_ClassifiesCorrectly(string? kvRef, string attachedRid, bool expected)
    {
        var snap = new KeyVaultReferenceIdentityT1Probe.SlotKeyVaultRefSnapshot(
            SlotLabel: "prod",
            KeyVaultReferenceIdentity: kvRef,
            UserAssignedIdentityRids: string.IsNullOrEmpty(attachedRid)
                ? Array.Empty<string>()
                : new[] { attachedRid });

        KeyVaultReferenceIdentityT1Probe.IsSlotConsistent(snap).Should().Be(expected);
    }

    [Fact]
    public void IsSlotConsistent_EmptyAttachedList_ReturnsFalseEvenIfKvRefLooksValid()
    {
        var snap = new KeyVaultReferenceIdentityT1Probe.SlotKeyVaultRefSnapshot(
            SlotLabel: "prod",
            KeyVaultReferenceIdentity: ExpectedUamiRid,
            UserAssignedIdentityRids: Array.Empty<string>());

        KeyVaultReferenceIdentityT1Probe.IsSlotConsistent(snap).Should().BeFalse(
            "if the slot has zero attached UAMIs, ANY non-null kv-ref is an orphan");
    }

    // ---------- T13 ClassifyFailureShape classifier ----------

    [Theory]
    [InlineData(null, "urn:uami:a", KeyVaultReferenceIdentityT1Probe.SlotFailureShape.MissingPatch)]
    [InlineData("", "urn:uami:a", KeyVaultReferenceIdentityT1Probe.SlotFailureShape.MissingPatch)]
    [InlineData("SystemAssigned", "urn:uami:a", KeyVaultReferenceIdentityT1Probe.SlotFailureShape.SystemAssigned)]
    [InlineData("SYSTEMASSIGNED", "urn:uami:a", KeyVaultReferenceIdentityT1Probe.SlotFailureShape.SystemAssigned)]
    [InlineData("urn:uami:b", "urn:uami:a", KeyVaultReferenceIdentityT1Probe.SlotFailureShape.OrphanReference)]
    [InlineData("urn:uami:a", "urn:uami:a", KeyVaultReferenceIdentityT1Probe.SlotFailureShape.Ok)]
    public void ClassifyFailureShape_PinsMapping(
        string? kvRef, string attachedRid, KeyVaultReferenceIdentityT1Probe.SlotFailureShape expected)
    {
        var snap = new KeyVaultReferenceIdentityT1Probe.SlotKeyVaultRefSnapshot(
            SlotLabel: "prod",
            KeyVaultReferenceIdentity: kvRef,
            UserAssignedIdentityRids: string.IsNullOrEmpty(attachedRid)
                ? Array.Empty<string>()
                : new[] { attachedRid });

        KeyVaultReferenceIdentityT1Probe.ClassifyFailureShape(snap).Should().Be(expected);
    }

    // ---------- T14 ExtractUserAssignedIdentityRids ----------

    [Fact]
    public void ExtractUserAssignedIdentityRids_NullIdentity_ReturnsEmpty()
    {
        var result = KeyVaultReferenceIdentityT1Probe.ExtractUserAssignedIdentityRids(identity: null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractUserAssignedIdentityRids_IdentityWithoutUais_ReturnsEmpty()
    {
        // SAMI-only identity: SystemAssigned type, no user-assigned entries.
        var identity = new ManagedServiceIdentity(ManagedServiceIdentityType.SystemAssigned);

        var result = KeyVaultReferenceIdentityT1Probe.ExtractUserAssignedIdentityRids(identity);

        result.Should().BeEmpty();
    }

    // ---------- T15 NormalizeKeyVaultRef ----------

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("/subscriptions/x/…/uami-A", "/subscriptions/x/…/uami-A")]
    public void NormalizeKeyVaultRef_TreatsWhitespaceAsNull(string? input, string? expected)
    {
        KeyVaultReferenceIdentityT1Probe.NormalizeKeyVaultRef(input).Should().Be(expected);
    }
}

/// <summary>
/// Task 171 (Wave G-7 Batch G-7A2.1) extension of the shared
/// <see cref="ArmSdkTestFakes"/> partial class -- WebSite/WebSiteSlot response
/// body builder that INCLUDES the <c>identity.userAssignedIdentities</c> block
/// alongside <c>keyVaultReferenceIdentity</c>. The base
/// <see cref="ArmSdkTestFakes.WebSiteBody"/> (task 123) omits the identity
/// block because task 123's probe only reads keyVaultReferenceIdentity; T1's
/// structural-consistency check needs BOTH properties on the same response.
/// Kept in this test file (not the shared fakes file) per the sibling-probe
/// convention -- each probe test file extends the partial with the
/// probe-specific body shape it needs (parity with
/// ArmKeyVaultRefProbeTests.cs's own WebSiteBody extension).
/// </summary>
internal static partial class ArmSdkTestFakes
{
    /// <summary>
    /// Returns a WebSite (or WebSiteSlot) response body carrying both
    /// <c>keyVaultReferenceIdentity</c> AND the
    /// <c>identity.userAssignedIdentities</c> dictionary.
    /// </summary>
    /// <param name="keyVaultReferenceIdentity">
    /// Value for <c>properties.keyVaultReferenceIdentity</c>. Pass <c>null</c>
    /// to omit the property entirely (simulates "T1 not patched -- default state");
    /// pass <c>string.Empty</c> to emit an empty string (ARM's actual "unset" behavior);
    /// pass a UAMI resource id string or the "SystemAssigned" sentinel for other T1
    /// scenarios.
    /// </param>
    /// <param name="userAssignedIdentityRids">
    /// The UAMI resource ids to embed as keys under <c>identity.userAssignedIdentities</c>.
    /// Each entry gets an empty-object value. Pass an empty enumerable to
    /// omit the identity block entirely (SAMI-only or no-identity slot).
    /// </param>
    public static string WebSiteBodyWithIdentity(
        string subscriptionId,
        string resourceGroupName,
        string siteAndSlotName,
        string? keyVaultReferenceIdentity,
        IEnumerable<string> userAssignedIdentityRids)
    {
        var uais = userAssignedIdentityRids?.ToList() ?? new List<string>();

        string kvRefBlock;
        if (keyVaultReferenceIdentity is null)
        {
            kvRefBlock = string.Empty; // Omit property entirely.
        }
        else
        {
            kvRefBlock = $"\"keyVaultReferenceIdentity\": \"{keyVaultReferenceIdentity}\"";
        }

        string identityBlock;
        if (uais.Count == 0)
        {
            identityBlock = string.Empty;
        }
        else
        {
            var entries = string.Join(",\n            ",
                uais.Select(r => $"\"{r}\": {{ \"principalId\": \"11111111-1111-1111-1111-111111111111\", \"clientId\": \"22222222-2222-2222-2222-222222222222\" }}"));
            identityBlock =
                $$"""
                "identity": {
                    "type": "UserAssigned",
                    "userAssignedIdentities": {
                        {{entries}}
                    }
                }
                """;
        }

        // Concatenate blocks with proper comma handling.
        var propertiesBody = kvRefBlock.Length == 0 ? "{}" : "{ " + kvRefBlock + " }";
        var topBlocks = new List<string>
        {
            $"\"id\": \"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Web/sites/{siteAndSlotName}\"",
            $"\"name\": \"{siteAndSlotName}\"",
            "\"location\": \"westus2\"",
            $"\"properties\": {propertiesBody}",
        };
        if (identityBlock.Length > 0)
        {
            topBlocks.Add(identityBlock);
        }
        return "{\n  " + string.Join(",\n  ", topBlocks) + "\n}";
    }
}
