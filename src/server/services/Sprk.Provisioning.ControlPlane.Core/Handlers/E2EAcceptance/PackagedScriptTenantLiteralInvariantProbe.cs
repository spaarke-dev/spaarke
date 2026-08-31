// -----------------------------------------------------------------------------
// PackagedScriptTenantLiteralInvariantProbe.cs
//
// I1 adapter (task 173, Wave G-7 Batch G-7A1) that exposes task-170's real
// I1 packaged-scripts on-disk grep as an <see cref="IInvariantProbe"/>. Wraps
// the existing internal-static
// <see cref="PackagedScriptTenantLiteralInvariantVerifier.ProbeI1(string)"/>
// verbatim — this class is a thin composability adapter, NOT a re-authoring
// of task-170's scan logic (which stays in
// <see cref="PackagedScriptTenantLiteralInvariantVerifier"/> unchanged and
// remains directly unit-testable there).
//
// WHY THIS EXISTS:
//   Task 173 (this task) swaps <see cref="E2EAcceptanceModule"/>'s DI
//   registration from <see cref="PackagedScriptTenantLiteralInvariantVerifier"/>
//   (a full <see cref="IE2EInvariantVerifier"/> impl handling I1 real + I2-I5
//   InfraFault) to <see cref="CompositeInvariantVerifier"/> (composes per-kind
//   <see cref="IInvariantProbe"/>s + falls back to InfraFault for un-wired
//   kinds). Without an I1 <see cref="IInvariantProbe"/>, the composite would
//   REGRESS I1 back to InfraFault — reverting task-170's shipped real check.
//   This adapter preserves task-170's I1 semantics under the composite pattern:
//   the composite invokes THIS class for I1, which delegates to task-170's
//   static, which returns the same Pass / Fail / InfraFault outcomes task-170
//   already covered with unit tests.
//
// AUTHORSHIP NOTE:
//   The scan logic (regex + Param-block extraction + line-number + relativize)
//   is task 170's IP entirely — this class does not reproduce any of it and
//   defers to the static as the single source of truth. If task 170's
//   internal-static signature changes, this adapter changes too (mechanical).
//
// SEAM JUSTIFICATION (ADR-010 / CLAUDE.md §11 extension test):
//   Existing: <see cref="PackagedScriptTenantLiteralInvariantVerifier.ProbeI1(string)"/>
//   is a real live-executable I1 check.
//   Extension: this adapter is the minimum surface (Kind + one-line delegate)
//   needed to make it composable under the new <see cref="IInvariantProbe"/>
//   seam. Zero rewriting; zero test duplication; zero risk of drift.
//   Cost-of-doing-nothing: composite swap would regress I1 to InfraFault =
//   direct regression of task-170's Wave G-7 landing.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// <see cref="IInvariantProbe"/> for <see cref="InvariantKind.I1NoHardcodedTenant"/> —
/// thin adapter around <see cref="PackagedScriptTenantLiteralInvariantVerifier.ProbeI1(string)"/>.
/// </summary>
public sealed class PackagedScriptTenantLiteralInvariantProbe : IInvariantProbe
{
    private readonly ILogger<PackagedScriptTenantLiteralInvariantProbe> _logger;

    /// <summary>Constructs the I1 probe adapter.</summary>
    public PackagedScriptTenantLiteralInvariantProbe(
        ILogger<PackagedScriptTenantLiteralInvariantProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public InvariantKind Kind => InvariantKind.I1NoHardcodedTenant;

    /// <inheritdoc/>
    public Task<InvariantVerificationOutcome> ProbeAsync(
        InvariantVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Task-170's static reads no other request field, so the cancellation
        // token has nothing to observe inside the sync scan. We still respect
        // the token by throwing if already cancelled before invoking the scan.
        cancellationToken.ThrowIfCancellationRequested();

        var outcome = PackagedScriptTenantLiteralInvariantVerifier.ProbeI1(request.ProvisioningScriptsDirectory);
        _logger.LogInformation(
            "H13 I1 packaged-scripts probe (adapter): customerId={CustomerId} runId={RunId} scriptsDir={ScriptsDir} outcome={Outcome}",
            request.CustomerId, request.RunId, request.ProvisioningScriptsDirectory, outcome.GetType().Name);
        return Task.FromResult(outcome);
    }
}
