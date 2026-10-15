// -----------------------------------------------------------------------------
// OpenAiMonthlyLimitRule.cs
//
// Task 254 (owner G37 — "no cap, but allow for a per customer spend limit if
// desired"). The rule for the OPTIONAL intake value openAiMonthlyLimitUsd, in
// ONE place: POST /api/runs applies it (400 before anything is written), and
// scripts/Set-AiSpendLimit.ps1 mirrors it for later changes. H4b writes the
// validated string verbatim as AiSpendLimit__MonthlyLimitUsd; the BFF reads a
// value it cannot bind as no limit, so a bad value must be refused here.
//
// §11 justification — existing: CostEnvelopeIntake (H0's cost inputs, same
// decimal shape). Extension rejected: that file lives in H0's folder, so the
// run-context contract (RunContextContractTests) would record H0 as reading a
// key H0 never reads. Cost of doing nothing: a typo reaches every BFF instance
// and silently means "no limit".
// -----------------------------------------------------------------------------

using System.Globalization;

namespace Sprk.Provisioning.ControlPlane.Core.Models;

/// <summary>The rule for the optional <c>openAiMonthlyLimitUsd</c> intake value (task 254).</summary>
public static class OpenAiMonthlyLimitRule
{
    /// <summary>Rejection code: present but not a plain decimal in (0, <see cref="MaxLimitUsd"/>].</summary>
    public const string InvalidRejectionCode = "quota-openai-monthly-limit-invalid";

    /// <summary>Largest accepted limit — far above any stamp's spend, so a typo of extra digits is caught.</summary>
    public const decimal MaxLimitUsd = 1_000_000m;

    private const int MaxEchoedLength = 64;

    /// <summary>
    /// Absent or empty = no limit. Otherwise digits with an optional <c>.</c> between digits (no sign, separator,
    /// exponent or whitespace — the shape of <c>estimatedMonthlyUsd</c>), greater than zero (zero would read as "no
    /// limit" in the BFF — omit it instead) and at most <see cref="MaxLimitUsd"/>.
    /// </summary>
    public static OpenAiMonthlyLimitOutcome Validate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return new OpenAiMonthlyLimitOutcome.Valid(null);
        }
        if (value.StartsWith('.') || value.EndsWith('.')
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var limit)
            || limit <= 0m || limit > MaxLimitUsd)
        {
            var echo = value.Length > MaxEchoedLength ? value[..MaxEchoedLength] + "…" : value;
            return new OpenAiMonthlyLimitOutcome.Invalid(InvalidRejectionCode,
                $"'{Sprk.Provisioning.ControlPlane.Models.IntakeParameterCatalog.OpenAiMonthlyLimitUsd}' value '{echo}' is not a plain decimal greater than 0 and at " +
                $"most {MaxLimitUsd.ToString("0", CultureInfo.InvariantCulture)} (digits with an optional '.', no separators — " +
                "e.g. '500'). Omit it for no limit.");
        }
        return new OpenAiMonthlyLimitOutcome.Valid(limit);
    }
}

/// <summary>Result of <see cref="OpenAiMonthlyLimitRule.Validate"/>.</summary>
public abstract record OpenAiMonthlyLimitOutcome
{
    private OpenAiMonthlyLimitOutcome()
    {
    }

    /// <summary>Usable: <paramref name="MonthlyLimitUsd"/> is the limit, or null for none.</summary>
    public sealed record Valid(decimal? MonthlyLimitUsd) : OpenAiMonthlyLimitOutcome;

    /// <summary>Present but unusable.</summary>
    public sealed record Invalid(string RejectionCode, string Diagnostic) : OpenAiMonthlyLimitOutcome;
}
