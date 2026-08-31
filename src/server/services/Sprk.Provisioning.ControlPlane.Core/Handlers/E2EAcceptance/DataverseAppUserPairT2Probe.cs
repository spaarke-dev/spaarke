// -----------------------------------------------------------------------------
// DataverseAppUserPairT2Probe.cs
//
// Task 177 (Phase C'' Wave G-7 Batch G-7A1) — REAL T2 probe replacing the
// single-outcome InfraFault branch in PlaceholderTrapVerifier for
// TrapKind.T2DataverseAppUser. Per DS-4 §6 T2 row: "DV-REST
// systemusers?$filter=applicationid eq ... x2 (UAMI + BFF app-reg). Blocked
// by: H10 (done) + C5.8 grants."
//
// STANDALONE CLASS (deliberately no shared interface dependency):
//   6 sibling Wave-G-7 trap tasks (171 T1, 177 T2 [this task], 178 T3, 180 T4,
//   172 T5, 175 T6) each author a per-probe class to disjoint files. Assembly
//   task 185 is the terminal owner that decides the composition surface —
//   whether a shared ITrapProbe seam or a monolithic swap of
//   PlaceholderTrapVerifier — after ground-truthing what all 6 sibling probes
//   look like. Deliberately no `: ITrapProbe` here so task 185 has
//   architectural flexibility (matches sibling task 175's original standalone
//   posture — file header states "The aggregate IE2ETrapVerifier surface is
//   out of scope for this task; task 185 assembly composes this probe + 5
//   sibling probes").
//
// PROBE CONTRACT:
//   - Passed          : BOTH the customer UAMI (InterStepState.miClientId)
//                       AND the BFF app-registration (InterStepState.bffAppRegId)
//                       resolve as EXACTLY-ONE Dataverse App User systemusers
//                       row on the target customer environment — the T2 pair
//                       invariant per spec.md FR-33.
//   - Failed          : Either half returned count != 1 (0 = registration
//                       silently did not persist; >1 = duplicate rows from a
//                       prior partial run). SILENT-FAIL ACTUALLY MANIFESTED —
//                       H13 QuarantineRequired per §4B.
//   - InfraFault      : Probe could not verdict — H13 Resumable. Cases:
//                         * Missing tenantId/dataverseUrl/bffAppRegId/
//                           uamiClientId from the request (upstream handler
//                           has not run; defense-in-depth over H13's own
//                           parameter guards).
//                         * IDataverseAppUserVerifier THREW (the seam's
//                           contract is to return CountMismatch(0) on
//                           internal failures rather than throw — a throw
//                           means the verifier itself is failing, not a T2
//                           trigger).
//
// SEAM REUSE (per POML explicit direction: "Reuse task 143's
// DataverseWebApiAppUserVerifier seam"):
//   Injects <see cref="IDataverseAppUserVerifier"/> — the SAME seam H10's
//   handler uses for its own T2 half-check. Production impl is
//   <see cref="DataverseAppUserGraphParity.DataverseWebApiAppUserVerifier"/>
//   (registered globally in Worker/Program.cs via
//   AddHttpClient&lt;IDataverseAppUserVerifier, DataverseWebApiAppUserVerifier&gt;()).
//   Unit tests substitute a fake — no HttpClient duplication in this probe file,
//   and the systemusers?$filter=applicationid query idiom has EXACTLY ONE
//   implementation to change if the Dataverse API surface ever shifts.
//
// WHY BOTH HALVES ARE INDEPENDENTLY RE-QUERIED:
//   H10's own handler body already verifies the UAMI half only (H10Handler.cs
//   §11). H13 re-verifies BOTH halves as the acceptance floor — trusting H10's
//   in-memory result would defeat T2's whole purpose (the trap catches the
//   "creator reported success but Dataverse silently only persisted ONE half"
//   shape). Concrete failure mode this probe catches: post-provisioning, every
//   BFF or L2 call for the missing/duplicated half 403s at Dataverse (spec.md
//   FR-33 — the exact shape T2 exists to catch).
//
// TESTABILITY (fake-transport pattern per POML briefing):
//   Injecting IDataverseAppUserVerifier lets tests substitute the H10 seam
//   with a fake returning canned Verified / CountMismatch outcomes — the
//   probe's decision logic (both-Verified → Passed; either-mismatch → Failed;
//   missing-input or throw → InfraFault) is exercised in isolation. The real
//   HttpClient + DefaultAzureCredential surface belongs to task 143's H10
//   smoke tests, not this probe's unit tests (ADR-038 path #1).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §11):
//   Existing: <see cref="IDataverseAppUserVerifier"/> already exists (task 143 /
//     r3 H10 seams) — this probe REUSES it verbatim.
//   Extension: yes — extends the H13 acceptance-gate surface with the T2
//     probe implementation. ONE class, ONE T2 branch.
//   Cost-of-doing-nothing: without this probe, H13's T2 branch returns
//     InfraFault UNCONDITIONALLY (Resumable) — a half-registered pair silently
//     ships to Ready. Concrete failure mode: post-provisioning, every BFF call
//     for that customer 403s at Dataverse (spec.md FR-33). H13's acceptance
//     gate cannot go green for T2 without this probe (DS-4 §6 verbatim).
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Real T2 (Dataverse App User pair) probe per DS-4 §6. Produces exactly one
/// <see cref="TrapVerificationOutcome"/> for
/// <see cref="TrapKind.T2DataverseAppUser"/>. Assembly task 185 composes this
/// + 5 sibling probes into the aggregate <see cref="IE2ETrapVerifier"/>
/// implementation that replaces <see cref="PlaceholderTrapVerifier"/>'s DI
/// registration.
/// </summary>
public sealed class DataverseAppUserPairT2Probe : ITrapProbe
{
    /// <summary>Which §4B trap this probe verdicts. Exposed for task 185's aggregate composition to route by kind.</summary>
    public TrapKind Kind => TrapKind.T2DataverseAppUser;

    private readonly IDataverseAppUserVerifier _verifier;
    private readonly ILogger<DataverseAppUserPairT2Probe> _logger;

    public DataverseAppUserPairT2Probe(
        IDataverseAppUserVerifier verifier,
        ILogger<DataverseAppUserPairT2Probe> logger)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(logger);
        _verifier = verifier;
        _logger = logger;
    }

    /// <summary>
    /// Runs the T2 probe: two independent DV-REST re-queries against
    /// <c>systemusers?$filter=applicationid eq {appId}</c> — one for the BFF
    /// app-reg, one for the UAMI. Never throws for probe-level fault modes —
    /// returns <see cref="TrapVerificationOutcome.InfraFault"/> instead so H13
    /// can classify Resumable per its §4C rollback table.
    /// </summary>
    public async Task<TrapVerificationOutcome> ProbeAsync(
        TrapVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // (1) Input guards — H13's own parameter guard catches missing
        //     tenantId + dataverseUrl earlier; defense-in-depth here surfaces
        //     an InfraFault (Resumable) rather than throwing.
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T2 probe: request.TenantId is empty. §4D I1 forbids default-tenant fallback — " +
                "upstream handler (H0.5 for Model 2, L2 endpoint for Model 1) MUST populate before H13.");
        }
        if (string.IsNullOrWhiteSpace(request.DataverseUrl))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T2 probe: request.DataverseUrl is empty. H5/H6 must complete before H13 (the T2 " +
                "probe has no target Dataverse env to re-query).");
        }
        if (string.IsNullOrWhiteSpace(request.BffAppRegId))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T2 probe: request.BffAppRegId is empty. InterStepState.bffAppRegId is populated " +
                "by H3 (Entra app-reg) — that upstream handler must complete before H13 can verify " +
                "the T2 pair.");
        }
        if (string.IsNullOrWhiteSpace(request.UamiClientId))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T2 probe: request.UamiClientId is empty. InterStepState.miClientId is populated " +
                "by H2a (uami.bicep) — that upstream handler must complete before H13 can verify " +
                "the T2 pair.");
        }

        // (2) BFF app-reg half — INDEPENDENT re-query even though H10's own body
        //     already verified the UAMI half; H13's job is to re-verify BOTH
        //     halves without trusting upstream handler telemetry (R7 EFFECT).
        DataverseAppUserVerificationResult bffResult;
        try
        {
            bffResult = await _verifier.VerifyAsync(
                request.DataverseUrl, request.TenantId, request.BffAppRegId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Caller-driven cancellation propagates.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "T2 probe: BFF-half verifier threw {ExType} for appId={AppId} on env={Env}",
                ex.GetType().Name, request.BffAppRegId, request.DataverseUrl);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T2 probe: BFF-half re-query threw {ex.GetType().Name}: {ex.Message}. " +
                "Verifier is expected to return CountMismatch(0) on internal exceptions rather than throw — " +
                "throw here means the verifier itself is failing (network / auth / other) rather than a T2 trigger.");
        }

        // (3) UAMI half — INDEPENDENT re-query (spec.md FR-33 pair invariant).
        DataverseAppUserVerificationResult uamiResult;
        try
        {
            uamiResult = await _verifier.VerifyAsync(
                request.DataverseUrl, request.TenantId, request.UamiClientId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "T2 probe: UAMI-half verifier threw {ExType} for appId={AppId} on env={Env}",
                ex.GetType().Name, request.UamiClientId, request.DataverseUrl);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T2 probe: UAMI-half re-query threw {ex.GetType().Name}: {ex.Message}. " +
                "Verifier is expected to return CountMismatch(0) on internal exceptions rather than throw.");
        }

        // (4) Verdict — BOTH halves must resolve as exactly-one App User row.
        var bffOk = bffResult is DataverseAppUserVerificationResult.Verified;
        var uamiOk = uamiResult is DataverseAppUserVerificationResult.Verified;

        if (bffOk && uamiOk)
        {
            _logger.LogInformation(
                "T2 probe PASSED: both BFF app-reg + UAMI resolve as Dataverse App Users on env={Env} " +
                "(tenant={TenantId}, bffAppId={BffAppRegId}, uamiClientId={UamiClientId}).",
                request.DataverseUrl, request.TenantId, request.BffAppRegId, request.UamiClientId);
            return new TrapVerificationOutcome.Passed(Kind);
        }

        // (5) At least one half missing/duplicated — Failed (QuarantineRequired per §4B).
        //     Diagnostic MUST cite BOTH observed + expected state per the
        //     TrapVerificationOutcome.Failed contract's doc comment.
        var diagnostic = BuildPairMismatchDiagnostic(request, bffResult, uamiResult);
        _logger.LogWarning("T2 probe FAILED: {Diagnostic}", diagnostic);
        return new TrapVerificationOutcome.Failed(Kind, diagnostic);
    }

    private static string BuildPairMismatchDiagnostic(
        TrapVerificationRequest request,
        DataverseAppUserVerificationResult bffResult,
        DataverseAppUserVerificationResult uamiResult)
    {
        var bffPart = bffResult switch
        {
            DataverseAppUserVerificationResult.Verified => "OK (count=1)",
            DataverseAppUserVerificationResult.CountMismatch cm => $"COUNT={cm.ObservedCount} (expected 1)",
            _ => "UNKNOWN",
        };
        var uamiPart = uamiResult switch
        {
            DataverseAppUserVerificationResult.Verified => "OK (count=1)",
            DataverseAppUserVerificationResult.CountMismatch cm => $"COUNT={cm.ObservedCount} (expected 1)",
            _ => "UNKNOWN",
        };

        return
            $"T2 pair invariant VIOLATED on env={request.DataverseUrl} (tenant={request.TenantId}): " +
            $"BFF app-reg (appId={request.BffAppRegId}) systemusers?$filter=applicationid → {bffPart}; " +
            $"UAMI (appId={request.UamiClientId}) systemusers?$filter=applicationid → {uamiPart}. " +
            "H10 reported success but the independent re-query disagrees for at least one half. Downstream: " +
            "every BFF or L2 call for the missing/duplicated half will fail 403 at Dataverse until resolved. " +
            "Operator: inspect the target Dataverse environment's systemusers table directly and reconcile " +
            "(count=0 → registration silently did not persist; count>1 → duplicate rows from a prior partial run).";
    }
}
