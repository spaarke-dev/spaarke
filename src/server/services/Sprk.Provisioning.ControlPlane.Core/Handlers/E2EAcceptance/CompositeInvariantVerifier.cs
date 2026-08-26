// -----------------------------------------------------------------------------
// CompositeInvariantVerifier.cs
//
// Production <see cref="IE2EInvariantVerifier"/> — composes the set of DI-
// registered <see cref="IInvariantProbe"/> implementations, one per
// <see cref="InvariantKind"/>, into the aggregate H13 acceptance-gate consumes.
// REPLACES <see cref="PlaceholderInvariantVerifier"/>'s DI registration
// (task 174, Wave G-7 Batch G-7A1).
//
// SEMANTICS:
//   For each canonical <see cref="InvariantKind"/> (I1..I5):
//     - If exactly ONE <see cref="IInvariantProbe"/> is registered for that
//       kind → invoke it, forward its outcome verbatim.
//     - If ZERO probes are registered → return
//       <see cref="InvariantVerificationOutcome.InfraFault"/> with
//       <see cref="InvariantProbeDeferralMessages.DeferralDiagnostic"/> —
//       preserves the Wave-C4 "InfraFault-so-Resumable" semantics for un-wired
//       kinds until their own probe tasks land.
//     - If TWO OR MORE probes register for the same kind → throw
//       <see cref="InvalidOperationException"/> at composition time (fail
//       loud; silently picking a winner would mask which probe answers the
//       H13 acceptance gate).
//
// PROBE ORDER + FAULT ISOLATION:
//   Probes are invoked in <see cref="InvariantKind"/> enum-declaration order
//   (I1..I5) — parity with the retired PlaceholderInvariantVerifier's outcomes-
//   array order so H13's aggregate log summary reads identically pre/post-
//   migration. Each probe runs independently — a Failed / InfraFault from one
//   probe does NOT short-circuit the remaining probes (H13 decides pass/fail
//   from the aggregate). A probe that THROWS (contract violation — probes
//   MUST return outcomes, not throw) is caught and surfaced as InfraFault so
//   one broken probe doesn't tear down the whole aggregate run.
//
// PLACEMENT + JUSTIFICATION:
//   Sprk.Provisioning.ControlPlane.Core (L2, not BFF — CLAUDE.md §10). No
//   AI-internal types (ADR-013). No BFF-facade dependencies. Zero shell-out.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Aggregates the registered <see cref="IInvariantProbe"/> set into the
/// <see cref="IE2EInvariantVerifier"/> H13 consumes.
/// </summary>
public sealed class CompositeInvariantVerifier : IE2EInvariantVerifier
{
    private static readonly ImmutableArray<InvariantKind> AllKindsInEnumOrder =
        ImmutableArray.Create(
            InvariantKind.I1NoHardcodedTenant,
            InvariantKind.I2AiSearchTenantFilter,
            InvariantKind.I3CosmosPartitionKey,
            InvariantKind.I4SpeContainerResolver,
            InvariantKind.I5GraphTokenTenant);

    private readonly ImmutableDictionary<InvariantKind, IInvariantProbe> _probesByKind;
    private readonly ILogger<CompositeInvariantVerifier> _logger;

    /// <exception cref="InvalidOperationException">
    /// Thrown when two or more probes register for the same <see cref="InvariantKind"/>.
    /// </exception>
    public CompositeInvariantVerifier(
        IEnumerable<IInvariantProbe> probes,
        ILogger<CompositeInvariantVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(logger);

        var builder = ImmutableDictionary.CreateBuilder<InvariantKind, IInvariantProbe>();
        foreach (var probe in probes)
        {
            if (probe is null)
            {
                continue;
            }
            if (builder.ContainsKey(probe.Kind))
            {
                throw new InvalidOperationException(
                    $"CompositeInvariantVerifier: duplicate IInvariantProbe registration for kind '{probe.Kind}' " +
                    $"(existing: {builder[probe.Kind].GetType().FullName}; new: {probe.GetType().FullName}). " +
                    "Register exactly one probe per InvariantKind; a duplicate would mask which probe answers the " +
                    "H13 acceptance gate and is a fail-loud DI-config bug.");
            }
            builder.Add(probe.Kind, probe);
        }
        _probesByKind = builder.ToImmutable();
        _logger = logger;

        _logger.LogInformation(
            "CompositeInvariantVerifier composed: {WiredCount}/{TotalCount} InvariantKinds have real probes registered. " +
            "Wired: [{Wired}]",
            _probesByKind.Count, AllKindsInEnumOrder.Length, string.Join(", ", _probesByKind.Keys));
    }

    /// <inheritdoc/>
    public async Task<InvariantCatalogVerificationResult> VerifyAllAsync(
        InvariantVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcomes = new InvariantVerificationOutcome[AllKindsInEnumOrder.Length];
        for (var i = 0; i < AllKindsInEnumOrder.Length; i++)
        {
            var kind = AllKindsInEnumOrder[i];
            outcomes[i] = await ProbeSingleAsync(kind, request, cancellationToken).ConfigureAwait(false);
        }

        return new InvariantCatalogVerificationResult(outcomes);
    }

    private async Task<InvariantVerificationOutcome> ProbeSingleAsync(
        InvariantKind kind, InvariantVerificationRequest request, CancellationToken cancellationToken)
    {
        if (!_probesByKind.TryGetValue(kind, out var probe))
        {
            return new InvariantVerificationOutcome.InfraFault(
                kind, InvariantProbeDeferralMessages.DeferralDiagnostic);
        }

        try
        {
            var outcome = await probe.ProbeAsync(request, cancellationToken).ConfigureAwait(false);
            if (outcome is null)
            {
                return new InvariantVerificationOutcome.InfraFault(
                    kind,
                    $"IInvariantProbe '{probe.GetType().FullName}' returned null. Treated as InfraFault so H13 stays Resumable.");
            }
            var reportedKind = outcome switch
            {
                InvariantVerificationOutcome.Passed p => p.Kind,
                InvariantVerificationOutcome.Failed f => f.Kind,
                InvariantVerificationOutcome.InfraFault i => i.Kind,
                _ => kind,
            };
            if (reportedKind != kind)
            {
                return new InvariantVerificationOutcome.InfraFault(
                    kind,
                    $"IInvariantProbe '{probe.GetType().FullName}' declared Kind={kind} but returned outcome for " +
                    $"Kind={reportedKind}. Contract violation — treated as InfraFault.");
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
                "IInvariantProbe '{ProbeType}' for kind {Kind} threw. Converting to InfraFault to preserve aggregate.",
                probe.GetType().FullName, kind);
            return new InvariantVerificationOutcome.InfraFault(
                kind,
                $"Probe '{probe.GetType().FullName}' threw {ex.GetType().Name}: {ex.Message}. " +
                "Probes MUST return outcomes (never throw); this was caught to preserve the aggregate contract.");
        }
    }
}
