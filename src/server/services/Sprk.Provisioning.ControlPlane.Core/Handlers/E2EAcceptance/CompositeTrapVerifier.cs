// -----------------------------------------------------------------------------
// CompositeTrapVerifier.cs
//
// Task 185 (Phase C'' Wave G-7 Batch G-7D) — TERMINAL aggregation. Production
// <see cref="IE2ETrapVerifier"/> that composes the 6 real per-trap probes
// (tasks 171/172/175/177/178/180) into the aggregate H13 acceptance-gate
// consumes. REPLACES <see cref="PlaceholderTrapVerifier"/>'s DI registration.
//
// DIRECT PARITY with <see cref="CompositeInvariantVerifier"/> (Batch G-7A1 /
// task 174). Every semantics rule below mirrors that sibling verifier so H13's
// aggregate log summary + failure-mode taxonomy reads identically for traps
// and invariants — the only substantive difference is <see cref="TrapKind"/>
// vs <see cref="InvariantKind"/>.
//
// SEMANTICS:
//   For each canonical <see cref="TrapKind"/> (T1–T6):
//     - If exactly ONE <see cref="ITrapProbe"/> is registered for that kind →
//       invoke it, forward its outcome verbatim.
//     - If ZERO probes are registered → return
//       <see cref="TrapVerificationOutcome.InfraFault"/> with
//       <see cref="TrapProbeDeferralMessages.DeferralDiagnostic"/> — preserves
//       the Wave-C4 "InfraFault-so-Resumable" semantics for un-wired kinds
//       until their own probe tasks land (this is defense-in-depth; task 185's
//       module wires all 6, so this branch is not expected to hit in
//       production).
//     - If TWO OR MORE probes register for the same kind → throw
//       <see cref="InvalidOperationException"/> at composition time (fail
//       loud; silently picking a winner would mask which probe answers the
//       H13 acceptance gate).
//
// PROBE ORDER + FAULT ISOLATION:
//   Probes are invoked in <see cref="TrapKind"/> enum-declaration order (T1–T6)
//   — parity with the retired <see cref="PlaceholderTrapVerifier"/>'s outcomes-
//   array order so H13's aggregate log summary reads identically pre/post-
//   migration. Each probe runs independently — a Failed / InfraFault from one
//   probe does NOT short-circuit the remaining probes (H13 decides
//   pass/fail from the aggregate; specifically, H13 collects the FULL trap
//   catalog so the operator sees every trap's verdict on failure, not just
//   the first one that fired). A probe that THROWS (contract violation —
//   probes MUST return outcomes, not throw) is caught and surfaced as
//   InfraFault so one broken probe doesn't tear down the whole aggregate run.
//   <see cref="OperationCanceledException"/> propagates.
//
// CONTRACT VALIDATION:
//   If a probe returns an outcome whose Kind does NOT match its declared
//   <see cref="ITrapProbe.Kind"/> (e.g., a probe with Kind=T3 returns a
//   TrapVerificationOutcome.Failed(T4, "..."))), the composite converts the
//   outcome to InfraFault. Silently forwarding a mis-kinded outcome would let
//   H13's MapTrapKindToRejectionCode map the WRONG rejection code, producing
//   a misleading operator diagnostic that names the wrong trap.
//
// SILENT-FAIL AUDIT (per POML rigor):
//   * Empty probe set: caller.Count==0 → every kind returns InfraFault → H13
//     classifies Resumable → aggregate Ready transition BLOCKED. Correct.
//     (Contrast: hypothetical "empty set == vacuously Passed" bug would silently
//     Green the gate.)
//   * All-InfraFault vs mixed InfraFault+Passed: H13 uses trapResult.FirstFailure
//     (Failed only) then trapResult.FirstInfraFault. If NO Failed and ≥1
//     InfraFault → Resumable. If ALL Passed → the trap portion of H13's
//     decision passes (invariants + naming + validate + cost still gate).
//   * Duplicate registration: throws at composition (constructor) — CANNOT
//     silently reach a state where two probes disagree on a kind.
//   * Missing kind in AllKindsInEnumOrder: adding a new TrapKind to the enum
//     WITHOUT updating AllKindsInEnumOrder would silently omit that kind from
//     the aggregate → the assertion in the constructor logs the observed
//     wired count vs the AllKinds count, but does NOT fail construction (the
//     enum author's responsibility). A defense-in-depth ArchTest could pin
//     this — deferred to a future god-class-ratchet or wave.
//
// PLACEMENT + JUSTIFICATION (CLAUDE.md §10 / §11):
//   Existing — <see cref="PlaceholderTrapVerifier"/> stub + 6 real per-trap
//     probe classes (each `: ITrapProbe` after task 185's two-token annotation).
//   Extension — REUSES <see cref="CompositeInvariantVerifier"/>'s composition
//     pattern verbatim; no new abstractions beyond <see cref="ITrapProbe"/>.
//   Cost-of-doing-nothing — H13's trap surface would remain wired to
//     PlaceholderTrapVerifier → every T1–T6 verdict returns InfraFault
//     UNCONDITIONALLY (Resumable), meaning EVERY future customer provision
//     would be blocked at the trap gate. This is the terminal wiring that
//     unblocks r1's Ready transition per spec.md FR-18 / SC #6.
//   Sprk.Provisioning.ControlPlane.Core (L2, not BFF — CLAUDE.md §10). No
//   AI-internal types (ADR-013). No BFF-facade dependencies. Zero shell-out.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Aggregates the registered <see cref="ITrapProbe"/> set into the
/// <see cref="IE2ETrapVerifier"/> H13 consumes.
/// </summary>
public sealed class CompositeTrapVerifier : IE2ETrapVerifier
{
    private static readonly ImmutableArray<TrapKind> AllKindsInEnumOrder =
        ImmutableArray.Create(
            TrapKind.T1KeyVaultReferenceIdentity,
            TrapKind.T2DataverseAppUser,
            TrapKind.T3GraphAppRoleParity,
            TrapKind.T4ExchangePolicyCount,
            TrapKind.T5SlotMiKvRbac,
            TrapKind.T6SpeConfidentialClient);

    private readonly ImmutableDictionary<TrapKind, ITrapProbe> _probesByKind;
    private readonly ILogger<CompositeTrapVerifier> _logger;

    /// <exception cref="InvalidOperationException">
    /// Thrown when two or more probes register for the same <see cref="TrapKind"/>.
    /// </exception>
    public CompositeTrapVerifier(
        IEnumerable<ITrapProbe> probes,
        ILogger<CompositeTrapVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(logger);

        var builder = ImmutableDictionary.CreateBuilder<TrapKind, ITrapProbe>();
        foreach (var probe in probes)
        {
            if (probe is null)
            {
                continue;
            }
            if (builder.ContainsKey(probe.Kind))
            {
                throw new InvalidOperationException(
                    $"CompositeTrapVerifier: duplicate ITrapProbe registration for kind '{probe.Kind}' " +
                    $"(existing: {builder[probe.Kind].GetType().FullName}; new: {probe.GetType().FullName}). " +
                    "Register exactly one probe per TrapKind; a duplicate would mask which probe answers the " +
                    "H13 acceptance gate and is a fail-loud DI-config bug.");
            }
            builder.Add(probe.Kind, probe);
        }
        _probesByKind = builder.ToImmutable();
        _logger = logger;

        _logger.LogInformation(
            "CompositeTrapVerifier composed: {WiredCount}/{TotalCount} TrapKinds have real probes registered. " +
            "Wired: [{Wired}]",
            _probesByKind.Count, AllKindsInEnumOrder.Length, string.Join(", ", _probesByKind.Keys));
    }

    /// <inheritdoc/>
    public async Task<TrapCatalogVerificationResult> VerifyAllAsync(
        TrapVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcomes = new TrapVerificationOutcome[AllKindsInEnumOrder.Length];
        for (var i = 0; i < AllKindsInEnumOrder.Length; i++)
        {
            var kind = AllKindsInEnumOrder[i];
            outcomes[i] = await ProbeSingleAsync(kind, request, cancellationToken).ConfigureAwait(false);
        }

        return new TrapCatalogVerificationResult(outcomes);
    }

    private async Task<TrapVerificationOutcome> ProbeSingleAsync(
        TrapKind kind, TrapVerificationRequest request, CancellationToken cancellationToken)
    {
        if (!_probesByKind.TryGetValue(kind, out var probe))
        {
            return new TrapVerificationOutcome.InfraFault(
                kind, TrapProbeDeferralMessages.DeferralDiagnostic);
        }

        try
        {
            var outcome = await probe.ProbeAsync(request, cancellationToken).ConfigureAwait(false);
            if (outcome is null)
            {
                return new TrapVerificationOutcome.InfraFault(
                    kind,
                    $"ITrapProbe '{probe.GetType().FullName}' returned null. Treated as InfraFault so H13 stays Resumable.");
            }
            var reportedKind = outcome switch
            {
                TrapVerificationOutcome.Passed p => p.Kind,
                TrapVerificationOutcome.Failed f => f.Kind,
                TrapVerificationOutcome.InfraFault i => i.Kind,
                _ => kind,
            };
            if (reportedKind != kind)
            {
                return new TrapVerificationOutcome.InfraFault(
                    kind,
                    $"ITrapProbe '{probe.GetType().FullName}' declared Kind={kind} but returned outcome for " +
                    $"Kind={reportedKind}. Contract violation — treated as InfraFault so H13's rejection-code " +
                    "mapping never names the wrong trap.");
            }
            return outcome;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ITrapProbe '{ProbeType}' for kind {Kind} threw. Converting to InfraFault to preserve aggregate.",
                probe.GetType().FullName, kind);
            return new TrapVerificationOutcome.InfraFault(
                kind,
                $"Probe '{probe.GetType().FullName}' threw {ex.GetType().Name}: {ex.Message}. " +
                "Probes MUST return outcomes (never throw); this was caught to preserve the aggregate contract.");
        }
    }
}
