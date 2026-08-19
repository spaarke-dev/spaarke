// -----------------------------------------------------------------------------
// ArmSlotIdentityRoleGranterTests.cs
//
// L2 CONTROL-PLANE unit tests for ArmSlotIdentityRoleGranter (task 125, Wave
// G-2). Proves the REAL Azure.ResourceManager.AppService (slot-identity read)
// + Azure.ResourceManager.Authorization (role-assignment create) SDK call
// paths via a fake HttpClientTransport — parity with the sibling H2a/H4 ARM
// collaborator test files. ADR-038 path #1.
//
// GROUND-TRUTHED SDK SHAPES (verified via a throwaway scratch console app
// against the installed Azure.ResourceManager.Authorization 1.1.4 package
// BEFORE writing this file, not guessed):
//   - WebSiteData.Identity / WebSiteSlotData.Identity is a nullable
//     Azure.ResourceManager.Models.ManagedServiceIdentity with `Guid? PrincipalId`.
//   - ArmClient.GetRoleAssignments(ResourceIdentifier scope) -> RoleAssignmentCollection
//     (a LOCAL reference — no network call, parity with GetWebSiteResource).
//   - RoleAssignmentCollection.CreateOrUpdateAsync(WaitUntil.Completed, name,
//     content, ct) -> PUT {scope}/providers/Microsoft.Authorization/
//     roleAssignments/{name}; a single 201 response with a terminal body
//     completes WaitUntil.Completed WITHOUT extra polling.
//   - A 409 response with error code "RoleAssignmentExists" surfaces as
//     RequestFailedException with Status=409, ErrorCode="RoleAssignmentExists".
//
// COVERAGE (maps to this project's T5 acceptance criterion — "verified
// against a fake with the correct roleDefinitionId + principalId + scope"):
//   T1  Both slots have a System-Assigned MI -> BOTH principals granted; the
//       PUT request body is captured + asserted to carry the correct
//       roleDefinitionId, principalId (Guid), AND the PUT URI's scope prefix
//       matches VaultResourceId — not a hard-coded Granted.
//   T2  Neither slot has a System-Assigned MI (post-Phase-C UAMI steady
//       state) -> NoSlotSystemAssignedIdentity, NO role-assignment PUT call.
//   T3  409 RoleAssignmentExists -> treated as idempotent success (Granted),
//       not a Failure.
//   T4  Genuine grant failure (403) -> Failure.
//   T5  Only prod slot has an identity -> only prod is granted; staging PUT
//       never attempted.
// -----------------------------------------------------------------------------

using System.Net;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArmSlotIdentityRoleGranterTests
{
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";
    private const string AppServiceName = "sprk-acme-prod-bff";
    private const string StagingSlotName = "staging";
    private const string RoleDefinitionId = "4633458b-17de-408a-b874-0445c86b69e6"; // Key Vault Secrets User
    private const string VaultResourceId =
        "/subscriptions/22222222-3333-4444-5555-666666666666/resourceGroups/rg-spaarke-acme-prod/" +
        "providers/Microsoft.KeyVault/vaults/sprk-acme-prod-kv";

    private static readonly string ProdPrincipalId = Guid.NewGuid().ToString();
    private static readonly string StagingPrincipalId = Guid.NewGuid().ToString();

    private static SlotIdentityRoleGrantInput NewInput() => new(
        SubscriptionId: SubscriptionId,
        ResourceGroupName: ResourceGroupName,
        AppServiceName: AppServiceName,
        StagingSlotName: StagingSlotName,
        VaultResourceId: VaultResourceId,
        RoleDefinitionId: RoleDefinitionId);

    private static string SiteBodyWithIdentity(string siteAndSlotName, string? principalId) =>
        principalId is null
            ? $$"""
                {
                  "id": "/subscriptions/{{SubscriptionId}}/resourceGroups/{{ResourceGroupName}}/providers/Microsoft.Web/sites/{{siteAndSlotName}}",
                  "name": "{{siteAndSlotName}}",
                  "location": "westus2",
                  "properties": { "keyVaultReferenceIdentity": "" }
                }
                """
            : $$"""
                {
                  "id": "/subscriptions/{{SubscriptionId}}/resourceGroups/{{ResourceGroupName}}/providers/Microsoft.Web/sites/{{siteAndSlotName}}",
                  "name": "{{siteAndSlotName}}",
                  "location": "westus2",
                  "identity": { "type": "SystemAssigned", "principalId": "{{principalId}}", "tenantId": "11111111-2222-3333-4444-555555555555" },
                  "properties": { "keyVaultReferenceIdentity": "" }
                }
                """;

    private static string RoleAssignmentBody(string roleAssignmentName, string principalId) => $$"""
        {
          "id": "{{VaultResourceId}}/providers/Microsoft.Authorization/roleAssignments/{{roleAssignmentName}}",
          "name": "{{roleAssignmentName}}",
          "type": "Microsoft.Authorization/roleAssignments",
          "properties": {
            "roleDefinitionId": "/subscriptions/{{SubscriptionId}}/providers/Microsoft.Authorization/roleDefinitions/{{RoleDefinitionId}}",
            "principalId": "{{principalId}}",
            "principalType": "ServicePrincipal",
            "scope": "{{VaultResourceId}}"
          }
        }
        """;

    // ---------- T1 both slots granted; body verified ----------

    [Fact]
    public async Task GrantAsync_BothSlotsHaveIdentity_GrantsBothWithCorrectRoleDefinitionPrincipalAndScope()
    {
        var putBodies = new List<string>();
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.Contains("Microsoft.Authorization/roleAssignments"))
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                putBodies.Add(body);
                var raName = path.Split('/').Last();
                var principalId = body.Contains(ProdPrincipalId) ? ProdPrincipalId : StagingPrincipalId;
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Created, RoleAssignmentBody(raName, principalId));
            }
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                    SiteBodyWithIdentity(AppServiceName + "/" + StagingSlotName, StagingPrincipalId));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK,
                SiteBodyWithIdentity(AppServiceName, ProdPrincipalId));
        });

        var granter = new ArmSlotIdentityRoleGranter(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmSlotIdentityRoleGranter>.Instance);

        var result = await granter.GrantAsync(NewInput(), CancellationToken.None);

        var granted = result.Should().BeOfType<SlotIdentityRoleGrantResult.Granted>().Subject;
        granted.ProdPrincipalId.Should().Be(ProdPrincipalId);
        granted.StagingPrincipalId.Should().Be(StagingPrincipalId);

        putBodies.Should().HaveCount(2, "one PUT per slot's grant");
        putBodies.Should().OnlyContain(b => b.Contains(
            $"/subscriptions/{SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{RoleDefinitionId}"),
            "every PUT body must carry the correct roleDefinitionId");
        putBodies.Should().Contain(b => b.Contains(ProdPrincipalId), "the prod principal must appear in one of the PUT bodies");
        putBodies.Should().Contain(b => b.Contains(StagingPrincipalId), "the staging principal must appear in one of the PUT bodies");
        handler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.StartsWith(VaultResourceId, StringComparison.Ordinal),
            "the role-assignment PUT scope must be the KV vault resource id (VaultResourceId), not some other scope");
    }

    // ---------- T2 no System-Assigned identity on either slot ----------

    [Fact]
    public async Task GrantAsync_NoSystemAssignedIdentityOnEitherSlot_ReturnsNoSlotSystemAssignedIdentityWithoutAnyGrantCall()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("Microsoft.Authorization/roleAssignments"))
            {
                throw new InvalidOperationException("must not attempt a grant when neither slot has an identity");
            }
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SiteBodyWithIdentity(AppServiceName + "/" + StagingSlotName, null));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SiteBodyWithIdentity(AppServiceName, null));
        });

        var granter = new ArmSlotIdentityRoleGranter(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmSlotIdentityRoleGranter>.Instance);

        var result = await granter.GrantAsync(NewInput(), CancellationToken.None);

        result.Should().BeOfType<SlotIdentityRoleGrantResult.NoSlotSystemAssignedIdentity>();
    }

    // ---------- T3 idempotent duplicate ----------

    [Fact]
    public async Task GrantAsync_RoleAssignmentAlreadyExists_TreatedAsIdempotentSuccess()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.Contains("Microsoft.Authorization/roleAssignments"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Conflict,
                    ArmSdkTestFakes.ArmErrorBody("RoleAssignmentExists", "The role assignment already exists."));
            }
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SiteBodyWithIdentity(AppServiceName + "/" + StagingSlotName, null));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SiteBodyWithIdentity(AppServiceName, ProdPrincipalId));
        });

        var granter = new ArmSlotIdentityRoleGranter(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmSlotIdentityRoleGranter>.Instance);

        var result = await granter.GrantAsync(NewInput(), CancellationToken.None);

        var granted = result.Should().BeOfType<SlotIdentityRoleGrantResult.Granted>().Subject;
        granted.ProdPrincipalId.Should().Be(ProdPrincipalId);
    }

    // ---------- T4 genuine failure ----------

    [Fact]
    public async Task GrantAsync_GrantForbidden_ReturnsFailure()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.Contains("Microsoft.Authorization/roleAssignments"))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Forbidden,
                    ArmSdkTestFakes.ArmErrorBody("AuthorizationFailed", "denied"));
            }
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SiteBodyWithIdentity(AppServiceName + "/" + StagingSlotName, null));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SiteBodyWithIdentity(AppServiceName, ProdPrincipalId));
        });

        var granter = new ArmSlotIdentityRoleGranter(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmSlotIdentityRoleGranter>.Instance);

        var result = await granter.GrantAsync(NewInput(), CancellationToken.None);

        result.Should().BeOfType<SlotIdentityRoleGrantResult.Failure>();
    }

    // ---------- T5 only prod has identity ----------

    [Fact]
    public async Task GrantAsync_OnlyProdHasIdentity_GrantsOnlyProdAndNeverCallsStagingGrant()
    {
        var grantedPrincipals = new List<string>();
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.Contains("Microsoft.Authorization/roleAssignments"))
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                grantedPrincipals.Add(body);
                var raName = path.Split('/').Last();
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.Created, RoleAssignmentBody(raName, ProdPrincipalId));
            }
            if (path.EndsWith("/slots/" + StagingSlotName))
            {
                return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SiteBodyWithIdentity(AppServiceName + "/" + StagingSlotName, null));
            }
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, SiteBodyWithIdentity(AppServiceName, ProdPrincipalId));
        });

        var granter = new ArmSlotIdentityRoleGranter(ArmSdkTestFakes.NewArmClient(handler), NullLogger<ArmSlotIdentityRoleGranter>.Instance);

        var result = await granter.GrantAsync(NewInput(), CancellationToken.None);

        var granted = result.Should().BeOfType<SlotIdentityRoleGrantResult.Granted>().Subject;
        granted.ProdPrincipalId.Should().Be(ProdPrincipalId);
        granted.StagingPrincipalId.Should().BeEmpty();
        grantedPrincipals.Should().ContainSingle("only the prod slot has an identity to grant against");
    }
}
