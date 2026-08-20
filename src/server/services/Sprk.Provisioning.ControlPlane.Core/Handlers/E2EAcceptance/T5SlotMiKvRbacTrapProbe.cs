// -----------------------------------------------------------------------------
// T5SlotMiKvRbacTrapProbe.cs
//
// Task 172 (Phase C'' Wave G-7 -- pipelined with H4 / task 125) -- REAL T5
// probe replacing the single-outcome InfraFault branch in PlaceholderTrapVerifier
// for TrapKind.T5SlotMiKvRbac. Per DS-4 s6 T5 row: "ARM.Authorization
// role-assignment list at KV scope, or structural UAMI check post-Phase-C".
//
// STANDALONE CLASS (deliberately no shared interface dependency):
//   6 sibling Wave-G-7 trap tasks (171 T1, 177 T2, 178 T3, 180 T4, 172 T5
//   [this task], 175 T6) each author a per-probe class to disjoint files.
//   Assembly task 185 is the terminal owner that decides the composition
//   surface -- whether a shared ITrapProbe seam or a monolithic swap of
//   PlaceholderTrapVerifier -- after ground-truthing what all 6 sibling
//   probes look like. Deliberately no `: ITrapProbe` here so task 185 has
//   architectural flexibility (parity with T2 / T6 sibling probes' original
//   standalone posture -- see DataverseAppUserPairT2Probe.cs +
//   T6SpeConfidentialClientTrapProbe.cs file headers).
//
// PROBE CONTRACT:
//   T5 has TWO valid Passed shapes -- the probe must distinguish between:
//     (a) STRUCTURAL PASSED (post-Phase-C UAMI steady state):
//         Neither slot has a System-Assigned MI (`.Data.Identity` null OR
//         `.PrincipalId` null/empty on BOTH slots). This is the SAME
//         post-condition H4's ArmSlotIdentityRoleGranter surfaces as
//         SlotIdentityRoleGrantResult.NoSlotSystemAssignedIdentity and H4
//         treats as SUCCESS (design.md s4B T5 + KvSecretsPopulation
//         ISlotIdentityRoleGranter.cs "T5 STRUCTURAL NOTE"). Post-Phase-C
//         (tasks 028/029/030 shipped in project seed) T5 is STRUCTURALLY
//         IMPOSSIBLE -- there is nothing to grant against, so nothing can
//         silently fail. Return Passed.
//     (b) LIVE-GRANTED PASSED (pre-Phase-C rolling deploy or a hypothetical
//         customer stamp with SAMI): at least one slot has a SAMI, and
//         EVERY SAMI present holds the KV Secrets User role assignment
//         (roleDefinitionId ends with the well-known GUID
//         '4633458b-17de-408a-b874-0445c86b69e6' -- centralized in
//         KvSecretsPopulationOptions.KvSecretsUserRoleId; H4 owns the write,
//         this probe owns the read-back).
//
//   FAILED shapes -- the silent-fail traps this probe catches (per POML +
//   task briefing "task 143 lesson: wrong role ID GUID would silently 403"):
//     * MISSING GRANT      -- SAMI exists on a slot but no role assignment
//                             at the KV scope references it. H4's grant
//                             never landed (dropped ARM PUT, wrong scope,
//                             wrong assignment name reuse). Downstream: BFF
//                             reads @Microsoft.KeyVault(...) references
//                             using the SAMI and 403s on every KV secret
//                             fetch after slot-swap.
//     * WRONG ROLE ID      -- SAMI exists AND has role assignment(s) at KV
//                             scope BUT none of them reference the KV
//                             Secrets User role definition (H4's granter
//                             mis-configured -- e.g. granted "Reader" or
//                             "KV Contributor" instead). The task-143 shape:
//                             a wrong GUID literal silently succeeds the
//                             PUT but produces a 403 at runtime. This probe
//                             asserts the specific role definition ID by
//                             GUID suffix, not "any role assignment exists".
//     * WRONG PRINCIPAL    -- The KV has assignments for the expected role
//                             ID, but none of the principalIds match the
//                             observed SAMI principal ID. H4's granter
//                             passed the wrong principal (e.g. UAMI object
//                             id instead of slot SAMI principal id).
//
//   InfraFault -- probe could not verdict (H13 Resumable):
//     * Missing SubscriptionId / ResourceGroupName / AppServiceName /
//       KeyVaultName from the request (upstream H4 parameter guards catch
//       this earlier; defense-in-depth surfacing here as InfraFault instead
//       of throwing).
//     * ARM SDK throws RequestFailedException / other exception while
//       reading either slot's identity OR listing role assignments at the
//       KV scope.
//
// SEAM REUSE + PATTERN PARITY (per POML + task-125 precedent):
//   Injects <see cref="ArmClient"/> directly -- the SAME shape H4's
//   ArmSlotIdentityRoleGranter (task 125) + H2a's ArmKeyVaultRefProbe
//   (task 123) already use. Production DI reuses the shared UAMI-pinned
//   ArmClient factory pattern; unit tests inject an ArmClient built against
//   a fake HttpClientTransport (ArmSdkTestFakes.NewArmClient) so ARM SDK's
//   real request/response marshaling runs unmodified against canned HTTP
//   responses (ADR-038 path #1).
//
// GROUND-TRUTHED SDK SHAPES (parity with the granter file's ground-truth
// note; verified against the same Azure.ResourceManager.Authorization
// 1.1.4 package pin the granter uses):
//   - WebSiteData.Identity is Azure.ResourceManager.Models.ManagedServiceIdentity?
//     with `Guid? PrincipalId` (SAME as ArmSlotIdentityRoleGranter reads).
//   - WebSiteSlotData.Identity has the SAME shape.
//   - ArmClient.GetRoleAssignments(ResourceIdentifier scope) ->
//     RoleAssignmentCollection (an ArmClient extension method from
//     Azure.ResourceManager.Authorization -- SAME as the granter uses for
//     CreateOrUpdate; here we use GetAllAsync).
//   - RoleAssignmentCollection.GetAllAsync(string? filter, string? tenantId,
//     CancellationToken) -> AsyncPageable<RoleAssignmentResource>. Each
//     resource's .Data is a RoleAssignmentData with:
//       * .RoleDefinitionId -> ResourceIdentifier (full RID; suffix is the
//         role definition GUID we compare against).
//       * .PrincipalId -> Guid (principal id, NOT string).
//
// WHY WE ASSERT ROLE-DEFINITION-ID BY GUID SUFFIX (not by full RID equality):
//   The role definition GUID is the stable well-known identifier. The full
//   RID includes the subscription id in the path ("/subscriptions/{sub}/
//   providers/Microsoft.Authorization/roleDefinitionsGuid}") -- subscription
//   id may differ from the KV scope's subscription in Lighthouse-delegated
//   scenarios. GUID-suffix match is the ARM canonical assertion.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md s11):
//   Existing -- IE2ETrapVerifier / TrapVerificationOutcome / TrapKind
//     (task 055) + PlaceholderTrapVerifier's InfraFault-per-kind branch for
//     T5 + KvSecretsPopulationOptions.KvSecretsUserRoleId (task 125). This
//     probe REUSES the H2a/H4 ArmClient DI pattern verbatim -- no new
//     shared abstractions.
//   Extension -- ONE per-probe class covering ONE T5 branch; sibling probes
//     cover their own kinds; assembly task 185 composes.
//   Cost-of-doing-nothing -- H13's T5 branch returns InfraFault
//     UNCONDITIONALLY (Resumable) without this probe -- H13's acceptance
//     gate cannot go green for T5 (DS-4 s6 verbatim); the acceptance-target
//     transition (task 184/185/186) is blocked on EVERY probe individually
//     landing. Concrete downstream failure this probe catches: post-
//     provisioning, every BFF slot post-swap read of a KV-reference-backed
//     appsetting 403s at KV until an operator manually grants the missing
//     RBAC (spec.md FR-33 T5 / design.md s4B T5).
// -----------------------------------------------------------------------------

using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.Authorization;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Real T5 (slot-MI KV RBAC / UAMI structural) probe per DS-4 s6. Produces
/// exactly one <see cref="TrapVerificationOutcome"/> for
/// <see cref="TrapKind.T5SlotMiKvRbac"/>. Assembly task 185 composes this +
/// 5 sibling probes into the aggregate <see cref="IE2ETrapVerifier"/>
/// implementation that replaces <see cref="PlaceholderTrapVerifier"/>'s DI
/// registration.
/// </summary>
public sealed class T5SlotMiKvRbacTrapProbe
{
    /// <summary>
    /// KV Secrets User role definition GUID -- well-known public-cloud
    /// constant, identical to <c>KvSecretsPopulationOptions.KvSecretsUserRoleId</c>
    /// (H4's granter uses the SAME constant for the PUT; this probe asserts
    /// the read-back matches). Duplicated here (not injected via options)
    /// deliberately -- the probe's whole purpose is to catch a WRONG GUID
    /// silent-fail (task 143 shape), so the assertion literal MUST be
    /// visible in the probe file for hand review. If a sovereign cloud
    /// requires a different GUID, both KvSecretsPopulationOptions and this
    /// constant must be updated together (a lockstep operator action).
    /// </summary>
    internal const string ExpectedKvSecretsUserRoleDefinitionGuid =
        "4633458b-17de-408a-b874-0445c86b69e6";

    /// <summary>Which s4B trap this probe verdicts. Exposed for task 185's aggregate composition to route by kind.</summary>
    public TrapKind Kind => TrapKind.T5SlotMiKvRbac;

    /// <summary>Default staging slot name (parity with app-service.bicep task 029 + H4's H4KvSecretsPopulationHandler.DefaultStagingSlotName).</summary>
    internal const string DefaultStagingSlotName = "staging";

    private readonly ArmClient _armClient;
    private readonly ILogger<T5SlotMiKvRbacTrapProbe> _logger;

    public T5SlotMiKvRbacTrapProbe(
        ArmClient armClient,
        ILogger<T5SlotMiKvRbacTrapProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _logger = logger;
    }

    /// <summary>
    /// Runs the T5 probe: reads both slots' System-Assigned MI principal
    /// ids via <see cref="WebSiteResource"/> / <see cref="WebSiteSlotResource"/>,
    /// then (if any SAMI present) lists role assignments at the KV scope
    /// via <see cref="RoleAssignmentCollection.GetAllAsync"/> and confirms
    /// every present SAMI principal id has an assignment for the KV Secrets
    /// User role definition. Never throws for probe-level fault modes --
    /// returns <see cref="TrapVerificationOutcome.InfraFault"/> instead so
    /// H13 can classify Resumable per its s4C rollback table.
    /// </summary>
    public async Task<TrapVerificationOutcome> ProbeAsync(
        TrapVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // (1) Input guards -- defense-in-depth over H13's own parameter
        //     guards. Missing input surfaces as InfraFault (Resumable)
        //     rather than throwing (parity with sibling T2 probe).
        if (string.IsNullOrWhiteSpace(request.SubscriptionId))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T5 probe: request.SubscriptionId is empty. H1 subscription-readiness MUST populate " +
                "before H13 can locate the App Service + KV in ARM.");
        }
        if (string.IsNullOrWhiteSpace(request.ResourceGroupName))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T5 probe: request.ResourceGroupName is empty. H2a Bicep infra deploy MUST populate " +
                "this parameter -- the probe scopes the App Service slot reads to this rg.");
        }
        if (string.IsNullOrWhiteSpace(request.AppServiceName))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T5 probe: request.AppServiceName is empty. H2a Bicep infra deploy MUST populate " +
                "this parameter -- the probe reads both slots' SAMI principal ids from this App Service.");
        }
        if (string.IsNullOrWhiteSpace(request.KeyVaultName))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T5 probe: request.KeyVaultName is empty. H2a Bicep infra deploy MUST populate " +
                "this parameter -- the probe lists role assignments at the KV vault scope to " +
                "verify the KV Secrets User grant landed.");
        }

        var stagingSlotName = DefaultStagingSlotName;

        _logger.LogInformation(
            "T5 probe starting: customerId={CustomerId} runId={RunId} appService={AppService} vault={Vault} stagingSlot={StagingSlot}",
            request.CustomerId, request.RunId, request.AppServiceName, request.KeyVaultName, stagingSlotName);

        // (2) Read both slots' SAMI principal ids. Failure to read either
        //     is InfraFault (Resumable) -- we cannot verdict T5 without
        //     both readings (a missing read could hide a genuine T5 trap
        //     on that slot).
        string? prodPrincipalId;
        string? stagingPrincipalId;
        var siteResourceId = WebSiteResource.CreateResourceIdentifier(
            request.SubscriptionId, request.ResourceGroupName, request.AppServiceName);
        var siteResource = _armClient.GetWebSiteResource(siteResourceId);

        try
        {
            var prodResponse = await siteResource.GetAsync(cancellationToken).ConfigureAwait(false);
            prodPrincipalId = NormalizePrincipalId(prodResponse.Value.Data.Identity?.PrincipalId);

            var stagingResponse = await siteResource
                .GetWebSiteSlotAsync(stagingSlotName, cancellationToken).ConfigureAwait(false);
            stagingPrincipalId = NormalizePrincipalId(stagingResponse.Value.Data.Identity?.PrincipalId);
        }
        catch (OperationCanceledException)
        {
            throw; // Caller-driven cancellation propagates.
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "T5 probe InfraFault -- ARM slot-identity read failed: customerId={CustomerId} " +
                "appService={AppService} status={Status}",
                request.CustomerId, request.AppServiceName, ex.Status);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T5 verdict deferred: ARM slot-identity read for App Service '{request.AppServiceName}' " +
                $"in resource group '{request.ResourceGroupName}' failed with RequestFailedException " +
                $"status {ex.Status}: {ex.ErrorCode ?? "(no code)"} -- {ex.Message}. Verify the L2 UAMI has " +
                $"Website Contributor (or at least Reader) RBAC on the App Service resource, and both slots " +
                $"exist. Re-run H13 after fixing.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "T5 probe InfraFault -- unexpected exception during slot-identity read: customerId={CustomerId} type={ExType}",
                request.CustomerId, ex.GetType().Name);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T5 verdict deferred: unexpected {ex.GetType().Name} reading App Service " +
                $"'{request.AppServiceName}' slot identities: {ex.Message}.");
        }

        // (3) STRUCTURAL PASSED: neither slot has a System-Assigned MI --
        //     post-Phase-C UAMI steady state (design.md s4B T5 "T5 STRUCTURAL
        //     NOTE" + ISlotIdentityRoleGranter.cs). T5 is structurally
        //     impossible -- there is no SAMI to require a KV RBAC grant.
        //     H4's granter surfaces this SAME state as
        //     NoSlotSystemAssignedIdentity = SUCCESS; the probe verdicts
        //     the SAME way.
        if (prodPrincipalId is null && stagingPrincipalId is null)
        {
            _logger.LogInformation(
                "T5 probe PASSED (structural): neither slot has a System-Assigned MI on App Service " +
                "'{AppService}' -- post-Phase-C UAMI steady state; T5 structurally impossible.",
                request.AppServiceName);
            return new TrapVerificationOutcome.Passed(Kind);
        }

        // (4) At least one slot has a SAMI -- read back all role assignments
        //     at the KV vault scope and confirm every SAMI-present slot has
        //     a KV Secrets User grant. Failure to LIST role assignments is
        //     InfraFault (Resumable) -- we cannot verdict T5 without
        //     visibility into the KV scope's grants.
        var kvScope = new ResourceIdentifier(BuildKvResourceId(
            request.SubscriptionId, request.ResourceGroupName, request.KeyVaultName));
        var roleAssignments = new List<KvScopeRoleAssignmentSnapshot>();
        try
        {
            var collection = _armClient.GetRoleAssignments(kvScope);
            await foreach (var assignment in collection.GetAllAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))
            {
                var data = assignment.Data;
                var roleDefRid = data.RoleDefinitionId?.ToString();
                var principalId = NormalizePrincipalId(data.PrincipalId);
                if (roleDefRid is null || principalId is null)
                {
                    continue;
                }
                roleAssignments.Add(new KvScopeRoleAssignmentSnapshot(
                    RoleDefinitionResourceId: roleDefRid,
                    PrincipalId: principalId));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "T5 probe InfraFault -- ARM role-assignment list at KV scope failed: customerId={CustomerId} " +
                "vault={Vault} status={Status}",
                request.CustomerId, request.KeyVaultName, ex.Status);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T5 verdict deferred: ARM role-assignment list at KV scope " +
                $"'{kvScope}' failed with RequestFailedException status {ex.Status}: " +
                $"{ex.ErrorCode ?? "(no code)"} -- {ex.Message}. Verify the L2 UAMI has " +
                $"'Microsoft.Authorization/roleAssignments/read' at the KV scope (User Access Administrator " +
                $"or Reader on the vault RG suffices). Re-run H13 after fixing.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "T5 probe InfraFault -- unexpected exception during KV role-assignment list: customerId={CustomerId} type={ExType}",
                request.CustomerId, ex.GetType().Name);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T5 verdict deferred: unexpected {ex.GetType().Name} listing role assignments at KV scope " +
                $"'{kvScope}': {ex.Message}.");
        }

        // (5) Per-slot verdict: for every slot with a SAMI, assert at least
        //     one KV-scope role assignment references BOTH the KV Secrets
        //     User role definition GUID AND the observed SAMI principal id.
        //
        //     WHY GUID-SUFFIX MATCH: the role definition RID includes the
        //     subscription id, which may differ from the KV scope's
        //     subscription in Lighthouse-delegated scenarios; the well-known
        //     role GUID is the stable canonical identifier.
        var missing = new List<string>(capacity: 2);
        if (prodPrincipalId is not null)
        {
            if (!TryFindKvSecretsUserGrant(roleAssignments, prodPrincipalId, out var observedProdRoles))
            {
                missing.Add(BuildMissingGrantEvidence("prod", prodPrincipalId, observedProdRoles));
            }
        }
        if (stagingPrincipalId is not null)
        {
            if (!TryFindKvSecretsUserGrant(roleAssignments, stagingPrincipalId, out var observedStagingRoles))
            {
                missing.Add(BuildMissingGrantEvidence(stagingSlotName, stagingPrincipalId, observedStagingRoles));
            }
        }

        if (missing.Count > 0)
        {
            var diagnostic =
                $"T5 silent-fail trap MANIFESTED on App Service '{request.AppServiceName}' + " +
                $"KV vault '{request.KeyVaultName}' (scope='{kvScope}'). Expected: EVERY slot with a " +
                $"System-Assigned MI holds a role assignment for role definition GUID " +
                $"'{ExpectedKvSecretsUserRoleDefinitionGuid}' (Key Vault Secrets User) referencing that " +
                $"SAMI principal id at the KV scope. Observed missing: {string.Join(" | ", missing)}. " +
                $"Downstream: every BFF slot post-swap read of a @Microsoft.KeyVault(...) reference " +
                $"backed by the SAMI 403s at KV until an operator manually grants the missing RBAC " +
                $"(spec.md FR-33 T5 / design.md s4B T5). H4's ArmSlotIdentityRoleGranter must be " +
                $"re-run or the grant reconciled by hand.";
            _logger.LogWarning("T5 probe FAILED: {Diagnostic}", diagnostic);
            return new TrapVerificationOutcome.Failed(Kind, diagnostic);
        }

        _logger.LogInformation(
            "T5 probe PASSED (live-granted): appService={AppService} vault={Vault} " +
            "prodSami={ProdSami} stagingSami={StagingSami} roleAssignmentsAtKvScope={AssignmentCount}",
            request.AppServiceName, request.KeyVaultName,
            prodPrincipalId ?? "(none)", stagingPrincipalId ?? "(none)", roleAssignments.Count);
        return new TrapVerificationOutcome.Passed(Kind);
    }

    private static string? NormalizePrincipalId(Guid? principalId)
        => principalId is { } pid && pid != Guid.Empty ? pid.ToString() : null;

    /// <summary>
    /// Builds the canonical KV vault resource id. Parity with
    /// <see cref="Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation.H4KvSecretsPopulationHandler.BuildKvResourceId"/>
    /// -- H4 writes the grant at this scope; the probe reads back at the
    /// SAME scope. Duplicated (not injected) to keep the probe standalone
    /// per the sibling-probe convention (task 185 assembly may consolidate).
    /// </summary>
    internal static string BuildKvResourceId(string subscriptionId, string resourceGroupName, string keyVaultName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceGroupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyVaultName);
        return $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.KeyVault/vaults/{keyVaultName}";
    }

    /// <summary>
    /// Returns true iff <paramref name="assignments"/> contains at least one
    /// entry whose principalId matches <paramref name="expectedPrincipalId"/>
    /// AND whose roleDefinitionId ends with the KV Secrets User GUID. Also
    /// collects the observed role-definition GUIDs granted to that principal
    /// (regardless of role) into <paramref name="observedRolesForPrincipal"/>
    /// so the diagnostic can distinguish wrong-role from missing-grant vs
    /// wrong-principal.
    /// </summary>
    internal static bool TryFindKvSecretsUserGrant(
        IReadOnlyList<KvScopeRoleAssignmentSnapshot> assignments,
        string expectedPrincipalId,
        out IReadOnlyList<string> observedRolesForPrincipal)
    {
        var observed = new List<string>();
        var found = false;
        foreach (var a in assignments)
        {
            if (!string.Equals(a.PrincipalId, expectedPrincipalId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var roleDefGuid = ExtractRoleDefinitionGuid(a.RoleDefinitionResourceId);
            observed.Add(roleDefGuid ?? a.RoleDefinitionResourceId);
            if (string.Equals(roleDefGuid, ExpectedKvSecretsUserRoleDefinitionGuid,
                    StringComparison.OrdinalIgnoreCase))
            {
                found = true;
            }
        }
        observedRolesForPrincipal = observed;
        return found;
    }

    /// <summary>
    /// Extracts the trailing GUID from a role-definition resource id. ARM
    /// shape: <c>/subscriptions/{sub}/providers/Microsoft.Authorization/roleDefinitions/{guid}</c>.
    /// Returns the last path segment when it parses as a GUID; null otherwise.
    /// Exposed internal so unit tests can validate the parsing across
    /// scope-prefix variations (subscription-scope vs management-group scope).
    /// </summary>
    internal static string? ExtractRoleDefinitionGuid(string? roleDefinitionResourceId)
    {
        if (string.IsNullOrWhiteSpace(roleDefinitionResourceId))
        {
            return null;
        }
        var lastSlash = roleDefinitionResourceId.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash == roleDefinitionResourceId.Length - 1)
        {
            return null;
        }
        var tail = roleDefinitionResourceId[(lastSlash + 1)..];
        return Guid.TryParse(tail, out _) ? tail : null;
    }

    private static string BuildMissingGrantEvidence(
        string slotLabel, string expectedPrincipalId, IReadOnlyList<string> observedRolesForPrincipal)
    {
        if (observedRolesForPrincipal.Count == 0)
        {
            return $"slot='{slotLabel}' principalId='{expectedPrincipalId}' has ZERO role assignments at KV scope " +
                   "(MISSING GRANT -- H4 never landed the role or granted to a different principal)";
        }
        return $"slot='{slotLabel}' principalId='{expectedPrincipalId}' holds role assignments at KV scope " +
               $"but NONE reference the KV Secrets User role definition. Observed roles: " +
               $"[{string.Join(", ", observedRolesForPrincipal)}] (WRONG ROLE ID -- H4's granter mis-configured; " +
               $"expected role definition GUID '{ExpectedKvSecretsUserRoleDefinitionGuid}')";
    }

    /// <summary>
    /// Minimal projection of a role-assignment for the probe's decision
    /// logic. Public so unit tests can construct fixtures directly against
    /// <see cref="TryFindKvSecretsUserGrant"/>.
    /// </summary>
    /// <param name="RoleDefinitionResourceId">Full role-definition RID (whichever scope it was resolved at).</param>
    /// <param name="PrincipalId">Assignment principalId (stringified GUID, lowercase preferred by ARM but comparison is case-insensitive).</param>
    public sealed record KvScopeRoleAssignmentSnapshot(
        string RoleDefinitionResourceId,
        string PrincipalId);
}
