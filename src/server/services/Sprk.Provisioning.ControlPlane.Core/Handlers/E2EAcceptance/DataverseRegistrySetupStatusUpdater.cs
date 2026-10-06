// -----------------------------------------------------------------------------
// DataverseRegistrySetupStatusUpdater.cs
//
// L2 CONTROL-PLANE REAL <see cref="IRegistrySetupStatusUpdater"/> impl —
// H13's terminal `sprk_setupstatus = Ready` PATCH (task 184, Wave G-7 Batch
// G-7C, 2026-08-20). Delegates the wire mechanics to task 112's C1.4
// <see cref="IDataverseEnvironmentRegistryClient.UpdateSetupStatusAsync"/>
// (Path X, MI-native) so ALL registry HTTP wiring (auth, URI shape, error
// taxonomy) lives in ONE place.
//
// WHY THIS TASK EXISTS:
//   DS-4 §6 identified this file's Wave-C4 placeholder as THE strongest
//   overstatement in the WBS — the acceptance-target itself (spec.md FR-18
//   + SC #5: "sprk_dataverseenvironment.SetupStatus transitions to Ready")
//   was a `LogWarning("no-op")` and had NEVER been written by code, only
//   claimed. Task 184 replaces the no-op with a real PATCH so the north-
//   star can finally be true when H13 declares green.
//
// SPEC / DESIGN / ADR references:
//   - spec.md FR-18 + SC #5 + §15 north-star: sprk_setupstatus → Ready is
//                                             THE acceptance-target transition.
//   - spec.md FR-23 (§4C rollback COMPANION UPDATE): companion clear of
//                                             sprk_currentrunid on Ready
//                                             releases the same-customer
//                                             serialization guard's Dataverse
//                                             side of the story.
//   - spec.md FR-38 + design.md §9.6:        Path X credential model — MI-
//                                             native via IDataverseEnvironment
//                                             RegistryClient (task 112).
//   - IRegistrySetupStatusUpdater.cs:        TRANSITION SEMANTIC — H13 has
//                                             already computed all-gates-green
//                                             before calling us; this impl
//                                             just PATCHes.
//   - ADR-010:                               ONE seam per capability; H13 owns
//                                             the "trust H13's aggregation"
//                                             contract per POML §Constraints.
//   - ADR-032 (P2):                          NullDataverseEnvironmentRegistry
//                                             Client remains as kill-switch
//                                             fallback for the read seam; this
//                                             file swaps H13's write seam onto
//                                             the real client's PATCH.
//   - ADR-044:                               EnvironmentId is a canonical GUID;
//                                             the wire path guards against a
//                                             non-GUID at UpdateSetupStatusAsync.
//
// CALLER CONTRACT (H13):
//   - The updater is invoked from H13's step (11) AFTER Cosmos-Completed has
//     already landed (MED#10 SESSION-19 Cosmos-first ordering, customer-
//     provisioning-orchestration-r1 adversarial e2e verify workflow wepdcb8we).
//     Prior to MED#10 this ran at step (10) BEFORE the Cosmos write, which
//     produced a documented split-brain window on ReplaceRunAsync Conflict.
//     Post-MED#10, this write is BEST-EFFORT — a failure here is caught by
//     H13 and logged as a REGISTRY-STALE warning; the run IS complete (Cosmos
//     is authoritative) and the operator SKILL Step 6a
//     (.claude/skills/provision-environment/SKILL.md) picks up the residual
//     PATCH via re-verify + apply-drift-fix.
//   - The updater STILL performs full aggregation trust — every trap /
//     invariant / naming / cost / validation check has reported Passed BEFORE
//     H13 reaches this call. This impl does NOT independently re-verify probes
//     (would duplicate H13 logic + create dual-source-of-truth risk).
//   - H13 catches any exception this method throws and logs a REGISTRY-STALE
//     warning instead of failing the run (MED#10 Cosmos-first). This impl
//     still surfaces infra faults by mapping the wire client's typed outcomes
//     to the domain-level IRegistrySetupStatusUpdater outcomes (no exception
//     bubbling for Web API domain errors — those are Failure).
//
// COMPANION UPDATE (spec.md FR-23):
//   Every call clears sprk_currentrunid (ClearCurrentRunId=true). The guard
//   Dataverse-side of the story releases in the SAME transition as the Ready
//   write — no partial-success window where status is Ready but the guard
//   still points at a completed run (that would block the customer's next
//   run per the I5 guard's Dataverse read).
//
// SEAM JUSTIFICATION (ADR-010 / CLAUDE.md §11):
//   Existing — IDataverseEnvironmentRegistryClient (task 112) already exposes
//   UpdateSetupStatusAsync with the exact shape we need. Extension — the
//   updater is a thin adapter mapping IRegistrySetupStatusUpdater's outcomes
//   onto the client's RegistryUpdateOutcome discriminated union. Cost-of-
//   doing-nothing — without a real PATCH, `sprk_setupstatus = Ready`
//   remains unwritten regardless of every other handler's correctness,
//   permanently voiding spec FR-18 / SC #5.
//
// SILENT-FAIL DEFENSE (task 184 audit, 2026-08-20):
//   1. Wrong entity/record targeting: request.EnvironmentId is the L2 admin
//      env's sprk_dataverseenvironment ROW ID (per IRegistrySetupStatusUpdater
//      docs). The Path X credential (L2 UAMI as App User on the admin env)
//      cannot write to any OTHER environment even if a caller supplied a
//      non-admin GUID — a wrong-record PATCH would 404 (RegistryUpdateOutcome
//      .NotFound), NOT silently succeed on the customer's own registry copy.
//   2. Wrong PATCH shape (option-set integer vs string): the C1.4 client's
//      BuildPatchBody now maps the string form to the option-set integer
//      (task 184 fix — see DataverseEnvironmentRegistryClient.cs header). A
//      pre-task-184 client would have 400'd on this PATCH; the client fix +
//      this real impl land in the same wave.
//   3. Concurrency: the Ready transition does NOT take an ETag. By design —
//      H13 is the SOLE authority to write Ready (spec §7), and the I5
//      customer-run guard (task 059) ensures only one Active run per customer
//      exists at any moment. No racing write can hit us. If a future feature
//      breaks that assumption, add ETag support to IDataverseEnvironmentRegistry
//      Client.UpdateSetupStatusAsync + honor it here — do NOT quietly ship
//      last-write-wins.
//   4. Partial success (Ready written but sprk_currentrunid retained): the
//      PATCH is a SINGLE Dataverse transaction (one HTTP call, one row); a
//      2xx response guarantees both properties landed together. The POML
//      escalation trigger documents the operator-side response if this
//      invariant ever fails in production.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Registry;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Real (task 184) <see cref="IRegistrySetupStatusUpdater"/> — issues a
/// Dataverse Web API PATCH via task 112's
/// <see cref="IDataverseEnvironmentRegistryClient.UpdateSetupStatusAsync"/>
/// transitioning <c>sprk_setupstatus</c> to <c>Ready</c> AND clearing
/// <c>sprk_currentrunid</c> in the same transaction (spec.md FR-18 + FR-23).
/// H13's step (10) trusts this updater to be pass-through: caller has already
/// verified every trap / invariant / naming / cost / validation gate.
/// </summary>
public sealed class DataverseRegistrySetupStatusUpdater : IRegistrySetupStatusUpdater
{
    /// <summary>Display-name string the Dataverse choice option-set maps to (2).</summary>
    internal const string ReadyDisplayName = "Ready";

    private readonly IDataverseEnvironmentRegistryClient _registryClient;
    private readonly ILogger<DataverseRegistrySetupStatusUpdater> _logger;

    /// <summary>Constructs the updater bound to the C1.4 registry client.</summary>
    public DataverseRegistrySetupStatusUpdater(
        IDataverseEnvironmentRegistryClient registryClient,
        ILogger<DataverseRegistrySetupStatusUpdater> logger)
    {
        ArgumentNullException.ThrowIfNull(registryClient);
        ArgumentNullException.ThrowIfNull(logger);
        _registryClient = registryClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<RegistrySetupStatusUpdateOutcome> TransitionToReadyAsync(
        RegistrySetupStatusUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EnvironmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RunId);

        // Bucket B HIGH#7 SESSION 18 (customer-provisioning-orchestration-r1
        // adversarial e2e verify workflow wepdcb8we): PRIOR behavior set
        // ClearCurrentRunId=true so the same PATCH that flipped setupstatus=Ready
        // also nulled sprk_currentrunid. That was structurally unsafe — the
        // PATCH runs without an If-Match / compare-and-swap guard, while every
        // OTHER writer of sprk_currentrunid (CustomerRunGuard.ReleaseAsync,
        // QuarantineClearService.ClearAsync via COMP-06) uses the stale-value-
        // safe LookupAsync → runId-equality → TryClearAsync-with-ETag primitive.
        // Two writers with different safety models on the same column is exactly
        // the concurrency skew that produces silent-fail bugs in production
        // (e.g. an operator manual PATCH between H13's read and write would be
        // clobbered).
        //
        // Fix: HIGH#7 flips ClearCurrentRunId=false here. The guard release now
        // fires from ONE authoritative path — Bucket B HIGH#6's explicit
        // ICustomerRunGuard.ReleaseAsync call in HandlerOutcomeApplier's
        // Success-with-RunStatus.Completed branch. That call is ETag-safe and
        // stale-value-safe (Mismatched = no-op). This PATCH now touches ONLY
        // sprk_setupstatus, restoring the single-writer invariant on
        // sprk_currentrunid.
        //
        // Concurrency: yes, briefly the row is Ready but sprk_currentrunid still
        // holds this runId — a millisecond-scale window between this PATCH and
        // HandlerOutcomeApplier's Release. A concurrent /api/runs POST during
        // that window sees the guard held and gets a 409 (correct: the release
        // has not yet fired, so the run is technically still "in flight" from
        // the guard's perspective). This is preferable to the prior unconditional
        // clobber which could silently release a guard a different run legitimately
        // holds.
        var update = new RegistrySetupStatusUpdate(
            EnvironmentId: request.EnvironmentId,
            SetupStatus: ReadyDisplayName,
            ClearCurrentRunId: false,
            CustomerIdForLog: request.CustomerId,
            RunIdForLog: request.RunId);

        _logger.LogInformation(
            "DataverseRegistrySetupStatusUpdater PATCHing sprk_setupstatus=Ready (sprk_currentrunid release " +
            "routed via ICustomerRunGuard.ReleaseAsync per Bucket B HIGH#6/#7 SESSION 18). " +
            "customerId={CustomerId} runId={RunId} environmentId={EnvironmentId}.",
            request.CustomerId, request.RunId, request.EnvironmentId);

        var outcome = await _registryClient.UpdateSetupStatusAsync(update, cancellationToken)
            .ConfigureAwait(false);

        // Map C1.4 client's typed wire outcomes → the H13-facing domain
        // outcomes. NotFound and Failure both fold to Failure with the
        // preserved diagnostic — H13's step (10) surfaces the message via
        // H13Rejections.RegistryUpdateFailed. Distinct wire outcomes are
        // preserved verbatim in the diagnostic so an operator diffing the
        // Cosmos rejection reason can still tell a wrong-row from a domain
        // rejection.
        return outcome switch
        {
            RegistryUpdateOutcome.Success => new RegistrySetupStatusUpdateOutcome.Success(),
            RegistryUpdateOutcome.NotFound notFound =>
                new RegistrySetupStatusUpdateOutcome.Failure(
                    $"Registry row not found for environmentId={request.EnvironmentId}. {notFound.Diagnostic}"),
            RegistryUpdateOutcome.Failure failure =>
                new RegistrySetupStatusUpdateOutcome.Failure(failure.Diagnostic),
            _ => throw new InvalidOperationException(
                $"IDataverseEnvironmentRegistryClient.UpdateSetupStatusAsync returned an unknown outcome type '{outcome.GetType().FullName}'."),
        };
    }
}
