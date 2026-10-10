// -----------------------------------------------------------------------------
// CustomerBusinessUnitIntake.cs
//
// T259 (ISS-010 / #1486, owner decision 2026-10-09) — the ONE rule for the intake value H10 names the customer's
// business unit with: IntakeParameterCatalog.DisplayName (the intake file's displayName, T237 — the customer's full
// name, also the registry row's sprk_name). POST /api/runs applies it (400 before anything is created) and H10 applies it
// again (defence in depth) — the run-context contract: an intake value a handler has rules for is validated at intake
// with the same code and refused with the handler's own rejection code (provisioning.md "Run-context contract").
//
// RULES: present; 1–MaxLength characters (Dataverse businessunit.name holds 160); no control characters; no leading or
// trailing whitespace (Dataverse would find "Acme " and "Acme" as different names on a later run's find-by-name, and a
// second unit would be created); never the Secure Record unit's name, in any casing (Dataverse compares names without
// case — H10 would otherwise adopt the Secure Record unit as the customer's and put the BFF's users in it, the exact
// exposure the owner decision forbids).
//
// §11 — existing: SecureRecordSetupIntake (H7b's intake rule) and OpenAiMonthlyLimitRule are the same pattern for other
// keys. Extension: neither can carry another key's rule without making the run-context contract record a handler as
// reading a key it never reads. Cost of doing nothing: a blank, oversized or "Secure Record" name reaches H10 after
// H0–H5 have built the stamp, or H10 places the BFF's users in the Secure Record unit.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;
using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <summary>Intake rule for <see cref="IntakeParameterCatalog.DisplayName"/> — the customer business unit's name.</summary>
public static class CustomerBusinessUnitIntake
{
    /// <summary>Longest accepted name — Dataverse <c>businessunit.name</c> MaxLength.</summary>
    public const int MaxLength = 160;

    private const int MaxEchoedLength = 64;

    /// <summary>
    /// Reads and checks the customer's display name. Returns the name, or a rejection (<see cref="H10Rejections"/> code
    /// plus diagnostic) when it is absent or unusable.
    /// </summary>
    /// <param name="nonSecretParameters">The run's intake values.</param>
    /// <param name="secureRecordBusinessUnitName">The Secure Record unit's name (config/secure-record-owner-role.json).</param>
    public static CustomerBusinessUnitIntakeOutcome Validate(
        IDictionary<string, string> nonSecretParameters, string secureRecordBusinessUnitName)
    {
        ArgumentNullException.ThrowIfNull(nonSecretParameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(secureRecordBusinessUnitName);

        var key = IntakeParameterCatalog.DisplayName;
        if (!nonSecretParameters.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return new CustomerBusinessUnitIntakeOutcome.Invalid(H10Rejections.CustomerDisplayNameRequired,
                $"'{key}' is required: the customer's full name, which names the customer's own business unit (created " +
                "directly under the Dataverse root; the BFF's application users and every guest are placed in it).");
        }

        string? problem = null;
        if (value.Length > MaxLength)
        {
            problem = $"is {value.Length} characters (at most {MaxLength} — Dataverse businessunit.name)";
        }
        else if (value.Any(char.IsControl))
        {
            problem = "contains a control character";
        }
        else if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            problem = "has leading or trailing whitespace";
        }
        else if (string.Equals(value, secureRecordBusinessUnitName, StringComparison.OrdinalIgnoreCase))
        {
            problem = $"is the Secure Record business unit's name ('{secureRecordBusinessUnitName}') — the customer's unit " +
                      "must be a different unit: no user may be placed in the Secure Record unit";
        }

        if (problem is not null)
        {
            var echo = value.Length > MaxEchoedLength ? value[..MaxEchoedLength] + "…" : value;
            echo = new string(echo.Select(c => char.IsControl(c) ? '?' : c).ToArray());
            return new CustomerBusinessUnitIntakeOutcome.Invalid(H10Rejections.CustomerDisplayNameInvalid,
                $"'{key}' value '{echo}' {problem}.");
        }

        return new CustomerBusinessUnitIntakeOutcome.Valid(value);
    }
}

/// <summary>Result of <see cref="CustomerBusinessUnitIntake.Validate"/>.</summary>
public abstract record CustomerBusinessUnitIntakeOutcome
{
    private CustomerBusinessUnitIntakeOutcome()
    {
    }

    /// <summary>Usable: <paramref name="Name"/> is the business unit's name.</summary>
    public sealed record Valid(string Name) : CustomerBusinessUnitIntakeOutcome;

    /// <summary>Absent or unusable.</summary>
    public sealed record Invalid(string RejectionCode, string Diagnostic) : CustomerBusinessUnitIntakeOutcome;
}
