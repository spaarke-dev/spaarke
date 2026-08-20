// -----------------------------------------------------------------------------
// KeyVaultReferenceIdentityT1Probe.cs
//
// Task 171 (Phase C'' Wave G-7 -- pipelined with H2a / task 123 + H4 / task 125)
// -- REAL T1 probe replacing the single-outcome InfraFault branch in
// PlaceholderTrapVerifier for TrapKind.T1KeyVaultReferenceIdentity. Per DS-4 s6
// T1 row: "ARM.AppService read Data.KeyVaultReferenceIdentity on site + slot;
// compare to UAMI resource id".
//
// STANDALONE CLASS (deliberately no shared interface dependency):
//   6 sibling Wave-G-7 trap tasks (171 T1 [this task], 177 T2, 178 T3, 180 T4,
//   172 T5, 175 T6) each author a per-probe class to disjoint files. Assembly
//   task 185 is the terminal owner that decides the composition surface --
//   whether a shared ITrapProbe seam or a monolithic swap of
//   PlaceholderTrapVerifier -- after ground-truthing what all 6 sibling probes
//   look like. Deliberately no `: ITrapProbe` here so task 185 has
//   architectural flexibility (parity with sibling probes' original standalone
//   posture -- see DataverseAppUserPairT2Probe.cs / T5SlotMiKvRbacTrapProbe.cs /
//   T6SpeConfidentialClientTrapProbe.cs file headers).
//
// SIBLING-COORDINATION NOTE (parent dispatch 2026-08-20):
//   Parallel siblings are simultaneously authoring per-probe classes for
//   T5 (task 172) and I4 (task 176). All tasks write to disjoint per-probe .cs
//   files to avoid guaranteed merge conflicts on the shared PlaceholderTrapVerifier.cs
//   / E2EAcceptanceModule.cs. The DI wire-up that swaps this real probe into
//   the aggregate verifier is DEFERRED to assembly task 185. Zero shared-file
//   coord touched by this task (per T2/T5/T6 sibling precedent).
//
// PROBE CONTRACT:
//   T1 has ONE valid Passed shape -- both slots must (a) have a NON-EMPTY
//   Data.KeyVaultReferenceIdentity and (b) that value must appear as a UAMI
//   resource-id key in that slot's Data.Identity.UserAssignedIdentities
//   dictionary. This is the R7 "assert EFFECTS not intentions" principle:
//   proving the runtime configuration is INTERNALLY consistent (KV-ref points
//   to a UAMI that is genuinely attached to the slot -- which is what actually
//   makes @Microsoft.KeyVault(...) references resolve at runtime), not just
//   trusting H4's patcher self-report.
//
//   FAILED shapes -- the silent-fail traps this probe catches (per POML +
//   spec.md FR-33 T1 + design.md s4B T1):
//     * MISSING PATCH      -- Data.KeyVaultReferenceIdentity is null / empty
//                             on either slot. Bicep's PATCH never landed, OR
//                             H4's ArmAppServiceIdentityPatcher's fallback
//                             failed silently. Downstream: every
//                             @Microsoft.KeyVault(...) app-setting resolves
//                             as null at runtime -- BFF fails silently on
//                             first Dataverse call (design.md s4B T1
//                             verbatim).
//     * SYSTEM-ASSIGNED    -- Data.KeyVaultReferenceIdentity == "SystemAssigned"
//                             literal on either slot (App Service's default
//                             behavior when identity type is set but no UAMI
//                             is bound to KV refs). Same runtime symptom as
//                             MISSING PATCH -- the slot doesn't route KV
//                             requests through the UAMI.
//     * ORPHAN REFERENCE   -- Data.KeyVaultReferenceIdentity is a UAMI
//                             resource id BUT that UAMI is NOT in the slot's
//                             Data.Identity.UserAssignedIdentities dictionary.
//                             The KV reference points to an identity that the
//                             slot doesn't actually have attached -- runtime
//                             failure at the first KV request (403 or
//                             DPoP-shaped resolution failure). This is the
//                             subtle T1 shape: patcher wrote a plausible-
//                             looking value that isn't actually usable.
//
//   InfraFault -- probe could not verdict (H13 Resumable):
//     * Missing SubscriptionId / ResourceGroupName / AppServiceName from the
//       request (upstream H4/H13 parameter guards catch this earlier;
//       defense-in-depth surfacing here as InfraFault instead of throwing).
//     * ARM SDK throws RequestFailedException / other exception while reading
//       either slot's Data (e.g. 403 missing Reader RBAC on the App Service).
//
// SEAM REUSE + PATTERN PARITY (per POML "REUSES the exact read pattern task 123's
// T1 KV-ref probe port and task 125's post-PATCH verify already implement"):
//   Injects <see cref="ArmClient"/> directly -- the SAME shape task 123's
//   ArmKeyVaultRefProbe and task 172's T5SlotMiKvRbacTrapProbe (sibling) use.
//   Uses <see cref="WebSiteResource.GetAsync"/> + <see cref="WebSiteResource.GetWebSiteSlotAsync"/>
//   -- the IDENTICAL SDK read pattern task 123 established for the H2a T1
//   post-condition + task 125 established for the H4 post-PATCH verify. Not
//   literally invoking IArmKeyVaultRefProbe because that seam's contract
//   requires the caller to already know the ExpectedUserAssignedIdentityResourceId
//   (a value H2a knows from its own Bicep outputs but H13 does not have
//   plumbed into TrapVerificationRequest -- and modifying that shared record
//   is a coordinating change with sibling T5 task 172); the SDK primitives
//   underneath are what "reuse" points at, per the POML's "underlying read
//   logic" phrase.
//
// STRUCTURAL-CONSISTENCY CHECK (why we compare against the slot's OWN
// UserAssignedIdentities and not an externally-supplied expected id):
//   The T1 silent-fail is "keyVaultReferenceIdentity is not set to a UAMI
//   this slot actually has attached." The RUNTIME failure mode is that the
//   App Service's KV-reference resolver cannot obtain a token because the
//   referenced identity isn't bound to the slot. So the CORRECT value for
//   KeyVaultReferenceIdentity is "any UAMI resource id that appears in this
//   slot's UserAssignedIdentities dictionary." This is what makes KV refs
//   ACTUALLY WORK. A structural check against the slot's own identity set
//   catches:
//     - null / empty / "SystemAssigned" (T1 not patched)
//     - a resource id that doesn't match any attached UAMI (orphan reference)
//   without requiring H13 to know the "canonical" UAMI resource id
//   independently. This is the same R7 "assert EFFECTS" principle: we're
//   verifying that the deployed configuration actually functions, not that
//   it matches an intent we happen to remember from H2a's outputs.
//
// GROUND-TRUTHED SDK SHAPES (parity with T5 sibling's ground-truth note;
// verified against the same Azure.ResourceManager.AppService package pin
// task 123's ArmKeyVaultRefProbe.cs + task 125's ArmAppServiceIdentityPatcher.cs
// already use):
//   - WebSiteData.KeyVaultReferenceIdentity is `string?` (ARM returns empty
//     string when unset -- normalize to null for the comparison + diagnostic).
//   - WebSiteSlotData.KeyVaultReferenceIdentity has the SAME shape.
//   - WebSiteData.Identity is `Azure.ResourceManager.Models.ManagedServiceIdentity?`
//     with `IDictionary&lt;ResourceIdentifier, UserAssignedIdentity&gt; UserAssignedIdentities`
//     (case-insensitive key comparison for the resource-id membership check).
//   - WebSiteSlotData.Identity has the SAME shape.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md s11):
//   Existing -- IE2ETrapVerifier / TrapVerificationOutcome / TrapKind
//     (task 055) + PlaceholderTrapVerifier's InfraFault-per-kind branch for
//     T1 + task 123's WebSiteResource read pattern + task 172's ArmClient DI
//     pattern (sibling probe). This probe REUSES the H2a/H4/T5-sibling
//     ArmClient DI pattern verbatim -- no new shared abstractions.
//   Extension -- ONE per-probe class covering ONE T1 branch; sibling probes
//     cover their own kinds; assembly task 185 composes.
//   Cost-of-doing-nothing -- H13's T1 branch returns InfraFault
//     UNCONDITIONALLY (Resumable) without this probe -- H13's acceptance
//     gate cannot go green for T1 (DS-4 s6 verbatim); the acceptance-target
//     transition (task 184/185/186) is blocked on EVERY probe individually
//     landing. Concrete downstream failure this probe catches: BFF boot with
//     null @Microsoft.KeyVault(...) refs -> runtime failure the moment the
//     operator hands the env to the customer (design.md s4B T1 + spec.md
//     FR-33 exact silent-fail T1 catches).
// -----------------------------------------------------------------------------

using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Real T1 (App Service keyVaultReferenceIdentity binding) probe per DS-4 s6.
/// Produces exactly one <see cref="TrapVerificationOutcome"/> for
/// <see cref="TrapKind.T1KeyVaultReferenceIdentity"/>. Assembly task 185
/// composes this + 5 sibling probes into the aggregate <see cref="IE2ETrapVerifier"/>
/// implementation that replaces <see cref="PlaceholderTrapVerifier"/>'s DI
/// registration.
/// </summary>
public sealed class KeyVaultReferenceIdentityT1Probe
{
    /// <summary>Sentinel value ARM emits when keyVaultReferenceIdentity is set to the App Service's own SystemAssigned identity. NOT a UAMI resource id -- T1 manifested.</summary>
    internal const string SystemAssignedSentinel = "SystemAssigned";

    /// <summary>Default staging slot name (parity with app-service.bicep task 029 + H4KvSecretsPopulationHandler.DefaultStagingSlotName + T5SlotMiKvRbacTrapProbe.DefaultStagingSlotName).</summary>
    internal const string DefaultStagingSlotName = "staging";

    /// <summary>Which s4B trap this probe verdicts. Exposed for task 185's aggregate composition to route by kind.</summary>
    public TrapKind Kind => TrapKind.T1KeyVaultReferenceIdentity;

    private readonly ArmClient _armClient;
    private readonly ILogger<KeyVaultReferenceIdentityT1Probe> _logger;

    public KeyVaultReferenceIdentityT1Probe(
        ArmClient armClient,
        ILogger<KeyVaultReferenceIdentityT1Probe> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _logger = logger;
    }

    /// <summary>
    /// Runs the T1 probe: reads both slots' Data.KeyVaultReferenceIdentity +
    /// Data.Identity.UserAssignedIdentities via <see cref="WebSiteResource"/>
    /// / <see cref="WebSiteSlotResource"/>, then verifies that each slot's
    /// KeyVaultReferenceIdentity is a NON-EMPTY value that appears as a key
    /// in that slot's UserAssignedIdentities dictionary (case-insensitive).
    /// Never throws for probe-level fault modes -- returns
    /// <see cref="TrapVerificationOutcome.InfraFault"/> instead so H13 can
    /// classify Resumable per its s4C rollback table.
    /// </summary>
    public async Task<TrapVerificationOutcome> ProbeAsync(
        TrapVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // (1) Input guards -- defense-in-depth over H13's own parameter
        //     guards. Missing input surfaces as InfraFault (Resumable)
        //     rather than throwing (parity with sibling T2 / T5 probes).
        if (string.IsNullOrWhiteSpace(request.SubscriptionId))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T1 probe: request.SubscriptionId is empty. H1 subscription-readiness MUST populate " +
                "before H13 can locate the App Service in ARM.");
        }
        if (string.IsNullOrWhiteSpace(request.ResourceGroupName))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T1 probe: request.ResourceGroupName is empty. H2a Bicep infra deploy MUST populate " +
                "this parameter -- the probe scopes the App Service slot reads to this rg.");
        }
        if (string.IsNullOrWhiteSpace(request.AppServiceName))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T1 probe: request.AppServiceName is empty. H2a Bicep infra deploy MUST populate " +
                "this parameter -- the probe reads both slots' Data.KeyVaultReferenceIdentity + " +
                "Data.Identity.UserAssignedIdentities from this App Service.");
        }

        var stagingSlotName = DefaultStagingSlotName;

        _logger.LogInformation(
            "T1 probe starting: customerId={CustomerId} runId={RunId} appService={AppService} stagingSlot={StagingSlot}",
            request.CustomerId, request.RunId, request.AppServiceName, stagingSlotName);

        // (2) Read both slots' KeyVaultReferenceIdentity + UserAssignedIdentities
        //     via WebSiteResource.GetAsync + GetWebSiteSlotAsync -- the SAME
        //     SDK primitives task 123's ArmKeyVaultRefProbe + task 125's
        //     ArmAppServiceIdentityPatcher already use. Failure to read either
        //     is InfraFault (Resumable) -- we cannot verdict T1 without both
        //     readings (a missing read could hide a genuine T1 trap on that
        //     slot).
        SlotKeyVaultRefSnapshot prodSnapshot;
        SlotKeyVaultRefSnapshot stagingSnapshot;
        var siteResourceId = WebSiteResource.CreateResourceIdentifier(
            request.SubscriptionId, request.ResourceGroupName, request.AppServiceName);
        var siteResource = _armClient.GetWebSiteResource(siteResourceId);

        try
        {
            var prodResponse = await siteResource.GetAsync(cancellationToken).ConfigureAwait(false);
            prodSnapshot = BuildSnapshot(
                slotLabel: "production",
                keyVaultReferenceIdentity: prodResponse.Value.Data.KeyVaultReferenceIdentity,
                userAssignedIdentityRids: ExtractUserAssignedIdentityRids(prodResponse.Value.Data.Identity));

            var stagingResponse = await siteResource
                .GetWebSiteSlotAsync(stagingSlotName, cancellationToken).ConfigureAwait(false);
            stagingSnapshot = BuildSnapshot(
                slotLabel: stagingSlotName,
                keyVaultReferenceIdentity: stagingResponse.Value.Data.KeyVaultReferenceIdentity,
                userAssignedIdentityRids: ExtractUserAssignedIdentityRids(stagingResponse.Value.Data.Identity));
        }
        catch (OperationCanceledException)
        {
            throw; // Caller-driven cancellation propagates.
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "T1 probe InfraFault -- ARM slot read failed: customerId={CustomerId} " +
                "appService={AppService} status={Status}",
                request.CustomerId, request.AppServiceName, ex.Status);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T1 verdict deferred: ARM slot read for App Service '{request.AppServiceName}' " +
                $"in resource group '{request.ResourceGroupName}' failed with RequestFailedException " +
                $"status {ex.Status}: {ex.ErrorCode ?? "(no code)"} -- {ex.Message}. Verify the L2 UAMI has " +
                $"Website Contributor (or at least Reader) RBAC on the App Service resource, and both slots " +
                $"exist. Re-run H13 after fixing.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "T1 probe InfraFault -- unexpected exception during slot read: customerId={CustomerId} type={ExType}",
                request.CustomerId, ex.GetType().Name);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T1 verdict deferred: unexpected {ex.GetType().Name} reading App Service " +
                $"'{request.AppServiceName}' slot data: {ex.Message}.");
        }

        // (3) Structural-consistency check per slot: KeyVaultReferenceIdentity
        //     must be non-empty AND appear as a UAMI RID in the slot's own
        //     UserAssignedIdentities dictionary.
        var prodOk = IsSlotConsistent(prodSnapshot);
        var stagingOk = IsSlotConsistent(stagingSnapshot);

        if (prodOk && stagingOk)
        {
            _logger.LogInformation(
                "T1 probe PASSED: both slots' keyVaultReferenceIdentity binds an attached UAMI. " +
                "appService={AppService} prodKvRef={ProdKvRef} stagingKvRef={StagingKvRef}",
                request.AppServiceName,
                prodSnapshot.KeyVaultReferenceIdentity ?? "(none)",
                stagingSnapshot.KeyVaultReferenceIdentity ?? "(none)");
            return new TrapVerificationOutcome.Passed(Kind);
        }

        // (4) At least one slot's KV-ref binding is broken -- Failed
        //     (QuarantineRequired per s4B). Diagnostic MUST cite BOTH observed
        //     state AND expected shape per the TrapVerificationOutcome.Failed
        //     contract's doc comment.
        var diagnostic = BuildFailureDiagnostic(request, prodSnapshot, stagingSnapshot);
        _logger.LogWarning("T1 probe FAILED: {Diagnostic}", diagnostic);
        return new TrapVerificationOutcome.Failed(Kind, diagnostic);
    }

    /// <summary>
    /// A slot is CONSISTENT iff:
    /// (a) <see cref="SlotKeyVaultRefSnapshot.KeyVaultReferenceIdentity"/> is non-empty AND not the SystemAssigned sentinel, AND
    /// (b) that value appears (case-insensitive) as a UAMI resource-id key in
    ///     <see cref="SlotKeyVaultRefSnapshot.UserAssignedIdentityRids"/>.
    /// Exposed internal so unit tests can validate the classifier directly.
    /// </summary>
    internal static bool IsSlotConsistent(SlotKeyVaultRefSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(snapshot.KeyVaultReferenceIdentity))
        {
            return false;
        }
        if (string.Equals(snapshot.KeyVaultReferenceIdentity, SystemAssignedSentinel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        foreach (var rid in snapshot.UserAssignedIdentityRids)
        {
            if (string.Equals(snapshot.KeyVaultReferenceIdentity, rid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Classifies the per-slot failure shape for the diagnostic:
    ///  * MISSING PATCH (KV-ref null/empty)
    ///  * SYSTEM-ASSIGNED (KV-ref == "SystemAssigned")
    ///  * ORPHAN REFERENCE (KV-ref set but not in UserAssignedIdentities)
    ///  * OK (consistent -- caller filters this out before diagnostic build)
    /// Exposed internal so unit tests can pin the classifier.
    /// </summary>
    internal static SlotFailureShape ClassifyFailureShape(SlotKeyVaultRefSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(snapshot.KeyVaultReferenceIdentity))
        {
            return SlotFailureShape.MissingPatch;
        }
        if (string.Equals(snapshot.KeyVaultReferenceIdentity, SystemAssignedSentinel, StringComparison.OrdinalIgnoreCase))
        {
            return SlotFailureShape.SystemAssigned;
        }
        foreach (var rid in snapshot.UserAssignedIdentityRids)
        {
            if (string.Equals(snapshot.KeyVaultReferenceIdentity, rid, StringComparison.OrdinalIgnoreCase))
            {
                return SlotFailureShape.Ok;
            }
        }
        return SlotFailureShape.OrphanReference;
    }

    private static string BuildFailureDiagnostic(
        TrapVerificationRequest request,
        SlotKeyVaultRefSnapshot prod,
        SlotKeyVaultRefSnapshot staging)
    {
        var prodPart = FormatSlotEvidence(prod);
        var stagingPart = FormatSlotEvidence(staging);
        return
            $"T1 keyVaultReferenceIdentity binding VIOLATED on App Service '{request.AppServiceName}' " +
            $"(rg='{request.ResourceGroupName}', sub='{request.SubscriptionId}'): " +
            $"expected BOTH slots to carry keyVaultReferenceIdentity == a UAMI resource id that also " +
            $"appears in that slot's Identity.UserAssignedIdentities dictionary. " +
            $"Observed: {prodPart}; {stagingPart}. " +
            "Downstream: BFF boot with null @Microsoft.KeyVault(...) refs -> runtime failure the moment " +
            "the operator hands the env to the customer. Operator: verify H2a Bicep + H4's " +
            "ArmAppServiceIdentityPatcher landed the intended UAMI resource id on both slots' " +
            "keyVaultReferenceIdentity property, and that the SAME UAMI is attached to both slots' " +
            "Identity.UserAssignedIdentities. If the reference points to a UAMI that is not attached, " +
            "H2a/H4 wrote a plausible-looking but non-functional value (ORPHAN REFERENCE class).";
    }

    private static string FormatSlotEvidence(SlotKeyVaultRefSnapshot snapshot)
    {
        var shape = ClassifyFailureShape(snapshot);
        var kvRef = snapshot.KeyVaultReferenceIdentity is null
            ? "(null)"
            : snapshot.KeyVaultReferenceIdentity.Length == 0 ? "(empty)" : snapshot.KeyVaultReferenceIdentity;
        var attached = snapshot.UserAssignedIdentityRids.Count == 0
            ? "(none)"
            : string.Join(", ", snapshot.UserAssignedIdentityRids);
        return shape switch
        {
            SlotFailureShape.MissingPatch =>
                $"slot='{snapshot.SlotLabel}' keyVaultReferenceIdentity={kvRef} " +
                "[MISSING PATCH -- H2a/H4 never PATCHed the property; default null means KV refs resolve as null at runtime]",
            SlotFailureShape.SystemAssigned =>
                $"slot='{snapshot.SlotLabel}' keyVaultReferenceIdentity={kvRef} " +
                $"[SYSTEM-ASSIGNED -- slot's UAMIs attached=[{attached}] but KV routing points at SystemAssigned]",
            SlotFailureShape.OrphanReference =>
                $"slot='{snapshot.SlotLabel}' keyVaultReferenceIdentity={kvRef} " +
                $"[ORPHAN REFERENCE -- slot's attached UAMIs are [{attached}], none match the KV-ref value]",
            SlotFailureShape.Ok =>
                $"slot='{snapshot.SlotLabel}' keyVaultReferenceIdentity={kvRef} [OK]",
            _ => $"slot='{snapshot.SlotLabel}' [unknown shape]",
        };
    }

    private static SlotKeyVaultRefSnapshot BuildSnapshot(
        string slotLabel,
        string? keyVaultReferenceIdentity,
        IReadOnlyList<string> userAssignedIdentityRids)
        => new(
            SlotLabel: slotLabel,
            KeyVaultReferenceIdentity: NormalizeKeyVaultRef(keyVaultReferenceIdentity),
            UserAssignedIdentityRids: userAssignedIdentityRids);

    /// <summary>
    /// ARM returns an empty string (not null) when <c>keyVaultReferenceIdentity</c>
    /// is unset -- normalize to null so the classifier maps to MissingPatch and
    /// the diagnostic reads "(null)" rather than a confusing "(empty)" edge case.
    /// Mirrors task 123's ArmKeyVaultRefProbe.NormalizeIdentity behavior.
    /// </summary>
    internal static string? NormalizeKeyVaultRef(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Extracts the list of UAMI resource ids attached to a slot from its
    /// <see cref="Azure.ResourceManager.Models.ManagedServiceIdentity"/>
    /// projection. Returns an empty list if Identity is null OR
    /// UserAssignedIdentities is null/empty (system-assigned-only slots +
    /// no-identity slots both surface as empty). Case is preserved from ARM;
    /// membership tests are case-insensitive.
    /// </summary>
    internal static IReadOnlyList<string> ExtractUserAssignedIdentityRids(
        Azure.ResourceManager.Models.ManagedServiceIdentity? identity)
    {
        if (identity is null)
        {
            return Array.Empty<string>();
        }
        var uais = identity.UserAssignedIdentities;
        if (uais is null || uais.Count == 0)
        {
            return Array.Empty<string>();
        }
        var rids = new List<string>(uais.Count);
        foreach (var key in uais.Keys)
        {
            var rid = key?.ToString();
            if (!string.IsNullOrWhiteSpace(rid))
            {
                rids.Add(rid);
            }
        }
        return rids;
    }

    /// <summary>
    /// Per-slot snapshot: the SlotLabel is the operator-facing name
    /// ("production" or the staging slot name), the KeyVaultReferenceIdentity
    /// is the ARM-observed value (normalized -- null when unset), and
    /// UserAssignedIdentityRids is the list of UAMI resource ids attached to
    /// the slot. Public so unit tests can construct fixtures directly against
    /// <see cref="IsSlotConsistent"/> / <see cref="ClassifyFailureShape"/>.
    /// </summary>
    public sealed record SlotKeyVaultRefSnapshot(
        string SlotLabel,
        string? KeyVaultReferenceIdentity,
        IReadOnlyList<string> UserAssignedIdentityRids);

    /// <summary>
    /// Per-slot failure classification -- drives the operator diagnostic +
    /// unit-test assertions. Public so tests can pin the classifier.
    /// </summary>
    public enum SlotFailureShape
    {
        /// <summary>Slot is internally consistent (kv-ref points at an attached UAMI).</summary>
        Ok = 0,

        /// <summary>Data.KeyVaultReferenceIdentity is null / empty -- H2a/H4 never PATCHed.</summary>
        MissingPatch = 1,

        /// <summary>Data.KeyVaultReferenceIdentity == "SystemAssigned" sentinel -- slot routes KV through SAMI, not UAMI.</summary>
        SystemAssigned = 2,

        /// <summary>Data.KeyVaultReferenceIdentity is a UAMI resource id BUT that UAMI is not in the slot's UserAssignedIdentities dictionary.</summary>
        OrphanReference = 3,
    }
}
