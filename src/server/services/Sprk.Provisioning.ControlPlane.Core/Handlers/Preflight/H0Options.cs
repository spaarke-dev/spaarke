// -----------------------------------------------------------------------------
// H0Options.cs
//
// COMP-10 (customer-provisioning-orchestration-r1 SESSION 17 pre-dispatch
// remediation, 2026-08-27): configuration for the H0 preflight cost-envelope
// gate.
//
// PURPOSE:
//   The SKILL.md Step 2 client-side cost-envelope check (BAT-10, Wave 6
//   punchlist landing commit dc77381f8) reads `intake.costEnvelopePolicy` +
//   `intake.tier` to fail-fast BEFORE POST /api/runs. That's a good first
//   line of defense — but a rogue direct-API caller (retry script bypassing
//   the skill, ad-hoc curl, a future non-skill orchestrator) can enqueue a
//   run without ever exercising the client check. Without a server-side
//   equivalent the cost-envelope invariant is client-only enforcement — an
//   NFR-violation shape the operator memory `feedback_fix_drift_at_discovery`
//   warns about explicitly ("BINDING pre-check protection at time of
//   discovery, not later").
//
//   H0 is the natural place: it already fails-fast Resumable BEFORE any
//   side-effecting handler dispatches (see H0PreflightHandler file-header
//   §ROLLBACK CLASSIFICATION), it already reads nonSecret parameters, and
//   its Failure record cleanly propagates through the reconciler + endpoint
//   without any special-casing.
//
// DEFAULT BEHAVIOR:
//   `CostEnvelopeAbortsPreflight = true` — the gate is ON by default. A
//   deployment that wants to explicitly disable (e.g., a fixed-budget
//   internal-test env where the operator has confirmed cost analytically
//   before dispatch) sets `false` in configuration:
//       "H0": { "CostEnvelopeAbortsPreflight": false }
//
// TIER CEILINGS:
//   Mirror the SKILL.md Step 2 BAT-10 defaults (shared-trial=430,
//   smb=700, enterprise=2500, dedicated=5000 USD/month). Operators can
//   override via configuration:
//       "H0": { "TierMonthlyCostCeilingsUsd": { "shared-trial": 500, ... } }
//   Any tier missing from the map falls back to the built-in default table
//   below via `GetCeilingUsd(tier)`.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §11):
//   New class — no existing options bag covers preflight cost gating (H13
//   AcceptanceOptions gates POST-provisioning drift, not preflight). The
//   H0 handler already carries preflight-scope options concerns implicitly
//   (via IConfiguration probes reading Preflight:VersionCompatMatrixPath);
//   this consolidates cost-gate config in a typed record so its shape is
//   test-injectable via IOptions<H0Options>. Cost of new class = 1 file
//   (~80 lines); cost of extending H13AcceptanceOptions instead = leaking
//   H0 concerns into a downstream-handler options bag + confusing readers
//   ("is CostEnvelopeAbortsPreflight a preflight or acceptance concern?").
//
// ADR references:
//   - ADR-010: registered UNCONDITIONALLY via HandlersModule.AddProvisioningHandlers.
//              No feature-gate DI branch. When CostEnvelopeAbortsPreflight = false
//              the gate is a documented no-op inside the handler, not a
//              missing dependency.
//   - ADR-032: no Null-Object kill-switch needed — the options record itself
//              is the switch (Boolean flag). Feature-gating via config is
//              the correct primitive here.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <summary>
/// Configuration for <see cref="H0PreflightHandler"/> — cost-envelope gate
/// (COMP-10) + future H0-scope options.
/// </summary>
public sealed class H0Options
{
    /// <summary>Configuration section name bound in <c>HandlersModule.AddProvisioningHandlers</c>.</summary>
    public const string SectionName = "H0";

    /// <summary>
    /// When <c>true</c> (default), H0 fails Resumable with rejection code
    /// <c>quota-cost-overrun</c> if the run's estimated monthly cost exceeds
    /// the tier ceiling AND the run's <c>costEnvelopePolicy</c> is not
    /// <c>warnAndProceed</c>. When <c>false</c>, the entire gate is
    /// skipped (log-only advisory). Default <c>true</c> per COMP-10 binding.
    /// </summary>
    public bool CostEnvelopeAbortsPreflight { get; set; } = true;

    /// <summary>
    /// Per-tier monthly cost ceilings in USD. Missing tiers fall back to
    /// <see cref="DefaultCeilingsUsd"/> via <see cref="GetCeilingUsd"/>.
    /// Matches SKILL.md Step 2 BAT-10 default table.
    /// </summary>
    public IDictionary<string, decimal> TierMonthlyCostCeilingsUsd { get; set; } =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Built-in default per-tier ceilings — used when
    /// <see cref="TierMonthlyCostCeilingsUsd"/> omits a tier. Mirrors
    /// SKILL.md Step 2 BAT-10 (shared-trial=430 / smb=700 / enterprise=2500 /
    /// dedicated=5000). Kept immutable — operators override via
    /// <see cref="TierMonthlyCostCeilingsUsd"/> config binding, not by
    /// mutating this table.
    /// </summary>
    public static IReadOnlyDictionary<string, decimal> DefaultCeilingsUsd { get; } =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["shared-trial"] = 430m,
            ["smb"] = 700m,
            ["enterprise"] = 2500m,
            ["dedicated"] = 5000m,
        };

    /// <summary>
    /// Resolves the monthly cost ceiling (USD) for a given tier — configured
    /// override wins over built-in default. Returns null when the tier is
    /// unknown to BOTH the operator override AND the built-in table (H0
    /// treats unknown tier as "skip gate + WARN in log"; a strict-reject
    /// posture would require adding the tier to the enum in intake.schema.json).
    /// </summary>
    public decimal? GetCeilingUsd(string tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            return null;
        }
        if (TierMonthlyCostCeilingsUsd.TryGetValue(tier, out var configured))
        {
            return configured;
        }
        if (DefaultCeilingsUsd.TryGetValue(tier, out var builtin))
        {
            return builtin;
        }
        return null;
    }
}
