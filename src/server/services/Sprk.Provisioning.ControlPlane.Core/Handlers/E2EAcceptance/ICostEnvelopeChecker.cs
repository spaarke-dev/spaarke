// -----------------------------------------------------------------------------
// ICostEnvelopeChecker.cs
//
// L2 abstraction over the Azure Cost Management query H13 issues to compare
// the customer stamp's observed month-to-date cost against the expected
// envelope per spec.md §15 #14. Production impl shells out to `az
// costmanagement query`; test impls return canned reports.
//
// COST-DRIFT SEMANTIC (per POML mandatory pre-work note):
//   Cost drift > 20% is currently AMBIGUOUS between fail-run and advisory-warn
//   (spec.md Unresolved Questions). This task implements the ADVISORY-WARN
//   default per POML notes — the handler collects the drift + attaches it to
//   the run's diagnostic + gate-state but does NOT fail H13 on cost alone,
//   UNLESS <see cref="H13AcceptanceOptions.CostDriftFailsRun"/> is
//   opted-in true. Full deviation reasoning in projects/customer-provisioning-
//   orchestration-r1/notes/task-055-deviations.md.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 impls (production az CLI cost query + test fakes per unit test).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Queries Azure Cost Management for the customer stamp's month-to-date cost
/// + returns a typed report vs the expected envelope per <see cref="H13AcceptanceOptions"/>.
/// Production impl shells out to `az costmanagement query`; test impls return
/// canned reports.
/// </summary>
public interface ICostEnvelopeChecker
{
    /// <summary>
    /// Issues the cost query + returns a typed report. Infrastructure faults
    /// (az CLI not on PATH, timeout, ARM 5xx) throw — the handler catches +
    /// classifies Resumable.
    /// </summary>
    Task<CostEnvelopeReport> CheckAsync(CostEnvelopeRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single cost-envelope check. Immutable record.
/// </summary>
/// <param name="CustomerId">Customer id — flows into log lines.</param>
/// <param name="RunId">RunId — for log correlation.</param>
/// <param name="SubscriptionId">Customer subscription id (ADR-027 D4) — cost query scopes to this subscription.</param>
/// <param name="TenancyModel">Tenancy model (<c>Model1Shared</c> / <c>Model2Dedicated</c>) — drives the expected-envelope selection.</param>
/// <param name="ResourceGroupName">Customer resource group name — optional scoping for finer-grained cost queries.</param>
/// <param name="DriftAdvisoryThreshold">Fraction above which drift is advisory-warn (§15 #14 default 0.20).</param>
public sealed record CostEnvelopeRequest(
    string CustomerId,
    string RunId,
    string SubscriptionId,
    string TenancyModel,
    string ResourceGroupName,
    decimal DriftAdvisoryThreshold);

/// <summary>
/// Report of a single cost-envelope check. Always returned (there is no
/// domain-Failure branch — the handler decides advisory-warn vs fail-run based
/// on <see cref="H13AcceptanceOptions.CostDriftFailsRun"/>).
/// </summary>
/// <param name="ObservedMonthlyUsd">Month-to-date cost extrapolated to a full month in whole USD.</param>
/// <param name="ExpectedMonthlyUsd">Expected envelope per §15 #14 for this tenancy model.</param>
/// <param name="DriftFraction">(<see cref="ObservedMonthlyUsd"/> − <see cref="ExpectedMonthlyUsd"/>) ÷ <see cref="ExpectedMonthlyUsd"/> — positive = over-budget.</param>
/// <param name="ExceedsAdvisoryThreshold">True iff <see cref="DriftFraction"/> exceeds the request's <see cref="CostEnvelopeRequest.DriftAdvisoryThreshold"/>.</param>
/// <param name="Summary">Human-readable summary suitable for logging + diagnostic surfaces.</param>
public sealed record CostEnvelopeReport(
    decimal ObservedMonthlyUsd,
    decimal ExpectedMonthlyUsd,
    decimal DriftFraction,
    bool ExceedsAdvisoryThreshold,
    string Summary);
