// -----------------------------------------------------------------------------
// CostEnvelopeIntake.cs
//
// Task 229 (G4 — cost model for dedicated stamps). H0's cost-envelope inputs, in
// ONE place: POST /api/runs applies these rules at intake (400 before any
// registry lookup, Cosmos write or enqueue) and H0 applies them again before it
// compares the estimate with the tier ceiling (defence in depth) — the
// UserProvisioningIntake pattern (task 245c).
//
// Since D-12 every run deploys a full dedicated stamp (customer.bicep) into the
// customer's own subscription — Model 1 paid by Spaarke, Model 2 by the
// customer — so both inputs are required for every model. Before T229 they were
// optional, H0 skipped the gate when they were absent unless the run was Model 2,
// and Model 1 could waive an overrun with `costEnvelopePolicy = warnAndProceed`
// ("shared-trial ONLY"); the shared tier is retired and the waiver went with it.
//
// §11 justification — existing: H0PreflightHandler's inline missing / unknown /
// unparseable branches. Extension: those rules moved here, H0 calls this; a
// second copy in RunsEndpoints would drift (.claude/constraints/provisioning.md
// "validated at POST /api/runs with the same rules"). Cost of doing nothing: a
// run without a usable estimate is accepted at intake and refused by H0 only
// after it is enqueued — or, before T229, accepted with no cost check at all.
// -----------------------------------------------------------------------------

using System.Globalization;

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <summary>H0's cost-envelope intake rules (<c>tier</c> + <c>estimatedMonthlyUsd</c>), shared by <c>POST /api/runs</c> and H0 (task 229).</summary>
public static class CostEnvelopeIntake
{
    /// <summary>Non-secret parameter: the cost tier whose monthly ceiling the estimate is compared with.</summary>
    public const string TierParameterKey = "tier";

    /// <summary>Non-secret parameter: the operator's projected monthly Azure spend for the stamp, an invariant-culture decimal string (e.g. <c>"425"</c>).</summary>
    public const string EstimatedMonthlyUsdParameterKey = "estimatedMonthlyUsd";

    /// <summary>Rejection code: <c>tier</c> or <c>estimatedMonthlyUsd</c> is absent.</summary>
    public const string MissingRejectionCode = "quota-cost-envelope-required-missing";

    /// <summary>Rejection code: <c>tier</c> is not one of <see cref="Tiers"/>.</summary>
    public const string UnknownTierRejectionCode = "quota-cost-envelope-unknown-tier";

    /// <summary>Rejection code: <c>estimatedMonthlyUsd</c> is not a plain non-negative decimal (digits, optional <c>.</c>).</summary>
    public const string InvalidEstimateRejectionCode = "quota-cost-envelope-unparseable-estimate";

    private const int MaxEchoedLength = 64;

    /// <summary>
    /// The accepted tiers (ordinal — exact case): the keys of <see cref="H0Options.DefaultCeilingsUsd"/>, which
    /// intake.schema.json's <c>tier</c> enum mirrors (IntakeSchemaProfileParityTests).
    /// </summary>
    public static IReadOnlyList<string> Tiers { get; } = [.. H0Options.DefaultCeilingsUsd.Keys];

    /// <summary>Validates the run's <c>tier</c> and <c>estimatedMonthlyUsd</c> values.</summary>
    public static CostEnvelopeIntakeOutcome Validate(string? tier, string? estimatedMonthlyUsd)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            return new CostEnvelopeIntakeOutcome.Invalid(MissingRejectionCode,
                $"'{TierParameterKey}' is required — one of {string.Join(", ", Tiers.Select(t => $"'{t}'"))}.");
        }
        if (!Tiers.Contains(tier, StringComparer.Ordinal))
        {
            return new CostEnvelopeIntakeOutcome.Invalid(UnknownTierRejectionCode,
                $"'{TierParameterKey}' value '{Echo(tier)}' is not one of {string.Join(", ", Tiers.Select(t => $"'{t}'"))} " +
                "(exact case). Every stamp is dedicated since D-12; the shared-trial tier is retired.");
        }
        if (string.IsNullOrWhiteSpace(estimatedMonthlyUsd))
        {
            return new CostEnvelopeIntakeOutcome.Invalid(MissingRejectionCode,
                $"'{EstimatedMonthlyUsdParameterKey}' is required — the projected monthly Azure spend of the stamp in USD " +
                "(an empty stamp costs about $340/month before usage; see the deployment guide).");
        }
        // Digits with an optional decimal point only — no sign (so never negative), no thousands separator, no
        // exponent: the schema value is a JSON number the skill sends as an invariant string.
        if (!decimal.TryParse(estimatedMonthlyUsd, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var estimate))
        {
            return new CostEnvelopeIntakeOutcome.Invalid(InvalidEstimateRejectionCode,
                $"'{EstimatedMonthlyUsdParameterKey}' value '{Echo(estimatedMonthlyUsd)}' is not a plain non-negative " +
                "decimal — digits with an optional '.', no separators (e.g. '425' or '1200.50').");
        }
        return new CostEnvelopeIntakeOutcome.Valid(tier, estimate);
    }

    private static string Echo(string value)
        => value.Length > MaxEchoedLength ? value[..MaxEchoedLength] + "…" : value;
}

/// <summary>Result of <see cref="CostEnvelopeIntake.Validate"/>.</summary>
public abstract record CostEnvelopeIntakeOutcome
{
    private CostEnvelopeIntakeOutcome()
    {
    }

    /// <summary>Both values are usable.</summary>
    public sealed record Valid(string Tier, decimal EstimatedMonthlyUsd) : CostEnvelopeIntakeOutcome;

    /// <summary>A value is absent or unusable; <paramref name="RejectionCode"/> is one of the <see cref="CostEnvelopeIntake"/> codes.</summary>
    public sealed record Invalid(string RejectionCode, string Diagnostic) : CostEnvelopeIntakeOutcome;
}
