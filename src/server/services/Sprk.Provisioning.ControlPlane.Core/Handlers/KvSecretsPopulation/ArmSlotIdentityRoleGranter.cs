// -----------------------------------------------------------------------------
// ArmSlotIdentityRoleGranter.cs
//
// Production <see cref="ISlotIdentityRoleGranter"/> — task 125 (Wave G-2,
// Option D hybrid) SDK port of <see cref="AzCliSlotIdentityRoleGranter"/>
// (shelled out to `az webapp identity show` / `az webapp deployment slot show`
// + `az role assignment create`; RETIRED, kept on disk unregistered — see
// that file's retirement banner). Reads both slots' System-Assigned MI
// principals via Azure.ResourceManager.AppService (WebSiteResource /
// WebSiteSlotResource `.Data.Identity`) and grants the KV Secrets User role
// via Azure.ResourceManager.Authorization RoleAssignmentCollection — the T5
// interim silent-fail-trap mitigation H4 owns (design.md §4B T5 + spec.md
// FR-33 T5).
//
// GROUND-TRUTHED SDK SHAPES (verified via reflection against a scratch
// project referencing Azure.ResourceManager.Authorization 1.1.4 alongside
// this project's existing Azure.ResourceManager.AppService 1.5.0 +
// Azure.ResourceManager 1.14.0 pins — BEFORE writing this file, not guessed):
//   - WebSiteData.Identity / WebSiteSlotData.Identity is a nullable
//     Azure.ResourceManager.Models.ManagedServiceIdentity with a
//     `Guid? PrincipalId` — the SAME shape ArmKeyVaultRefProbe.cs (task 123)
//     already reads `.Data.KeyVaultReferenceIdentity` from on the SAME
//     resource type; this collaborator reads `.Data.Identity` instead.
//   - ArmClient.GetRoleAssignments(ResourceIdentifier scope) -> RoleAssignmentCollection
//     (Azure.ResourceManager.Authorization.AuthorizationExtensions — an
//     ArmClient extension method, parity with GetWebSiteResource/
//     GetSubscriptionResource's non-network resource-reference construction).
//   - RoleAssignmentCollection.CreateOrUpdateAsync(WaitUntil, string
//     roleAssignmentName, RoleAssignmentCreateOrUpdateContent, CancellationToken)
//     -> Task&lt;ArmOperation&lt;RoleAssignmentResource&gt;&gt;. A single 200/201
//     PUT response with a terminal body completes WaitUntil.Completed without
//     extra polling — parity with task 123's ArmDeploymentRunnerTests.cs
//     ground-truthing note for other ARM CreateOrUpdate LRO calls.
//   - RoleAssignmentCreateOrUpdateContent(ResourceIdentifier roleDefinitionId,
//     Guid principalId) — PrincipalId is a Guid (NOT a string, unlike the
//     retired az CLI's `--assignee-object-id` string argument).
//   - RoleManagementPrincipalType.ServicePrincipal is an extensible-enum
//     static property (matches the retired collaborator's
//     `--assignee-principal-type ServicePrincipal`).
//
// IDEMPOTENCY REDESIGN (improves on the retired collaborator's parsed-stderr
// "RoleAssignmentExists" string match — the exact silent-fail-fragile class
// DS-2b's OCC analysis + this project's whole silent-fail-trap catalog exist
// to eliminate): the role-assignment NAME is now a deterministic hash of
// (scope, principalId, roleDefinitionId) rather than a fresh random GUID per
// attempt. A retry targets the SAME assignment name, so ARM's idempotent PUT
// semantics do the heavy lifting; the RequestFailedException
// "RoleAssignmentExists" catch is retained ONLY as defense-in-depth for the
// case where a differently-named assignment already covers the same
// scope+principal+role triple (e.g. a manual grant).
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.Authorization;
using Azure.ResourceManager.Authorization.Models;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Reads both slots' System-Assigned MI principal ids via
/// <see cref="WebSiteResource"/> / <see cref="WebSiteSlotResource"/> and
/// grants the KV Secrets User role via
/// <see cref="RoleAssignmentCollection.CreateOrUpdateAsync(WaitUntil, string, RoleAssignmentCreateOrUpdateContent, CancellationToken)"/>.
/// Constructed with an <see cref="ArmClient"/> so tests inject one built
/// against a fake HTTP transport — parity with the other H2a/H4 ARM
/// collaborators (task 121/123/125).
/// </summary>
public sealed class ArmSlotIdentityRoleGranter : ISlotIdentityRoleGranter
{
    private readonly ArmClient _armClient;
    private readonly ILogger<ArmSlotIdentityRoleGranter> _logger;

    /// <summary>Constructs the granter. Production DI reuses the shared UAMI-pinned ArmClient factory pattern.</summary>
    public ArmSlotIdentityRoleGranter(ArmClient armClient, ILogger<ArmSlotIdentityRoleGranter> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SlotIdentityRoleGrantResult> GrantAsync(
        SlotIdentityRoleGrantInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.SubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.VaultResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.RoleDefinitionId);

        var siteResourceId = WebSiteResource.CreateResourceIdentifier(
            input.SubscriptionId, input.ResourceGroupName, input.AppServiceName);
        var siteResource = _armClient.GetWebSiteResource(siteResourceId);

        string? prodPrincipal;
        string? stagingPrincipal;
        try
        {
            prodPrincipal = await ReadProductionSlotPrincipalIdAsync(siteResource, cancellationToken).ConfigureAwait(false);
            stagingPrincipal = await ReadStagingSlotPrincipalIdAsync(
                siteResource, input.StagingSlotName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SlotIdentityRoleGrantResult.Failure(
                $"T5 slot-identity read failed for App Service '{input.AppServiceName}': " +
                $"{ex.GetType().Name}: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(prodPrincipal) && string.IsNullOrWhiteSpace(stagingPrincipal))
        {
            _logger.LogInformation(
                "H4 T5 interim: no System-Assigned MI on either slot (post-Phase-C UAMI steady state) — " +
                "T5 structurally impossible; treating as SUCCESS. appService={AppService}",
                input.AppServiceName);
            return new SlotIdentityRoleGrantResult.NoSlotSystemAssignedIdentity(
                $"No System-Assigned MI on prod or staging slot of '{input.AppServiceName}' — post-Phase-C UAMI steady state.");
        }

        var scope = new ResourceIdentifier(input.VaultResourceId);
        var roleDefinitionId = new ResourceIdentifier(
            $"/subscriptions/{input.SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{input.RoleDefinitionId}");

        var failures = new List<string>(capacity: 2);
        if (!string.IsNullOrWhiteSpace(prodPrincipal))
        {
            var err = await TryGrantAsync(scope, roleDefinitionId, prodPrincipal!, "prod", cancellationToken).ConfigureAwait(false);
            if (err is not null) failures.Add($"prod slot ({prodPrincipal}): {err}");
        }
        if (!string.IsNullOrWhiteSpace(stagingPrincipal))
        {
            var err = await TryGrantAsync(scope, roleDefinitionId, stagingPrincipal!, input.StagingSlotName, cancellationToken).ConfigureAwait(false);
            if (err is not null) failures.Add($"staging slot ({stagingPrincipal}): {err}");
        }

        if (failures.Count > 0)
        {
            return new SlotIdentityRoleGrantResult.Failure(
                $"T5 KV Secrets User grant failed for App Service '{input.AppServiceName}': " +
                string.Join(" | ", failures));
        }

        return new SlotIdentityRoleGrantResult.Granted(prodPrincipal ?? "", stagingPrincipal ?? "");
    }

    private static async Task<string?> ReadProductionSlotPrincipalIdAsync(
        WebSiteResource siteResource, CancellationToken cancellationToken)
    {
        var response = await siteResource.GetAsync(cancellationToken).ConfigureAwait(false);
        return NormalizePrincipalId(response.Value.Data.Identity?.PrincipalId);
    }

    private static async Task<string?> ReadStagingSlotPrincipalIdAsync(
        WebSiteResource siteResource, string stagingSlotName, CancellationToken cancellationToken)
    {
        var response = await siteResource.GetWebSiteSlotAsync(stagingSlotName, cancellationToken).ConfigureAwait(false);
        return NormalizePrincipalId(response.Value.Data.Identity?.PrincipalId);
    }

    private static string? NormalizePrincipalId(Guid? principalId)
        => principalId is { } pid && pid != Guid.Empty ? pid.ToString() : null;

    private async Task<string?> TryGrantAsync(
        ResourceIdentifier scope, ResourceIdentifier roleDefinitionId, string principalId, string slotLabel,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "H4 T5 interim: granting KV Secrets User on {KvResourceId} to slot={Slot} principalId={PrincipalId}",
                scope, slotLabel, principalId);

            var content = new RoleAssignmentCreateOrUpdateContent(roleDefinitionId, Guid.Parse(principalId))
            {
                PrincipalType = RoleManagementPrincipalType.ServicePrincipal,
            };
            var roleAssignmentName = DeterministicRoleAssignmentName(scope, principalId, roleDefinitionId);
            var collection = _armClient.GetRoleAssignments(scope);
            await collection.CreateOrUpdateAsync(WaitUntil.Completed, roleAssignmentName, content, cancellationToken)
                .ConfigureAwait(false);
            return null;
        }
        catch (RequestFailedException ex) when (
            string.Equals(ex.ErrorCode, "RoleAssignmentExists", StringComparison.OrdinalIgnoreCase))
        {
            // Defense-in-depth: a differently-named assignment already covers
            // this exact scope+principal+role triple (e.g. a manual grant).
            // ARM enforces triple-uniqueness regardless of assignment name.
            _logger.LogInformation(
                "H4 T5 idempotent — role assignment already exists for {Slot} principalId={PrincipalId}",
                slotLabel, principalId);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>
    /// Derives a stable role-assignment name (a GUID) from
    /// (scope, principalId, roleDefinitionId) so a retry targets the SAME
    /// ARM object — ARM's idempotent PUT semantics then handle the
    /// duplicate-attempt case without needing to parse error text. Exposed
    /// internal so tests assert stability without depending on ARM call
    /// order. NOT cryptographically an RFC 4122 v5 UUID (no namespace UUID
    /// input) — sufficient for a deterministic, collision-resistant name.
    /// </summary>
    internal static string DeterministicRoleAssignmentName(
        ResourceIdentifier scope, string principalId, ResourceIdentifier roleDefinitionId)
    {
        var seed = $"{scope}|{principalId}|{roleDefinitionId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes).ToString();
    }
}
