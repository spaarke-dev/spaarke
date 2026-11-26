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
// second unit would be created); never a RESERVED unit's name, in any casing (Dataverse compares names without case):
// the Secure Record unit — H10 would otherwise adopt it as the customer's and put the BFF's users in it, the exact
// exposure the owner decision forbids — and (task 267, #2618) the ontology writer's own unit, which holds that one
// identity only. This name comparison is only a FIRST FILTER for a clear 400 at intake: Dataverse
// also ignores accents, which no code-side comparison reproduces exactly. The binding checks compare business-unit IDs
// resolved by Dataverse — H10 (EnsureCustomerBusinessUnitAsync → IsReservedUnit) and H7b S1 (BusinessUnitIsCustomerUnit).
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
    /// The business units no customer unit may be: the Secure Record unit (config/secure-record-owner-role.json) and the
    /// ontology writer's unit (task 267). POST /api/runs and H10 pass this list; H10 also passes it, by id, to
    /// <see cref="IDataverseAppUserCreator.EnsureCustomerBusinessUnitAsync"/>.
    /// </summary>
    public static IReadOnlyList<string> ReservedBusinessUnitNames { get; } =
        [SecureRecordOwnerRoleSet.Embedded.BusinessUnitName, SecureRecordSetupProcedure.OntologyWriterBusinessUnitName];

    /// <summary>
    /// Reads and checks the customer's display name. Returns the name, or a rejection (<see cref="H10Rejections"/> code
    /// plus diagnostic) when it is absent or unusable.
    /// </summary>
    /// <param name="nonSecretParameters">The run's intake values.</param>
    /// <param name="reservedBusinessUnitNames">The reserved units' names (<see cref="ReservedBusinessUnitNames"/>).</param>
    public static CustomerBusinessUnitIntakeOutcome Validate(
        IDictionary<string, string> nonSecretParameters, IReadOnlyCollection<string> reservedBusinessUnitNames)
    {
        ArgumentNullException.ThrowIfNull(nonSecretParameters);
        ArgumentNullException.ThrowIfNull(reservedBusinessUnitNames);
        if (reservedBusinessUnitNames.Count == 0 || reservedBusinessUnitNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one reserved unit name is required, none blank.", nameof(reservedBusinessUnitNames));
        }

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
        else if (reservedBusinessUnitNames.FirstOrDefault(reserved =>
                     string.Equals(FoldAccents(value), FoldAccents(reserved), StringComparison.OrdinalIgnoreCase)) is { } reservedName)
        {
            problem = $"is the reserved business unit name '{reservedName}', ignoring case and accents as Dataverse does — " +
                      "the customer's unit must be a different unit: no customer user may be placed in the Secure Record " +
                      "unit or in the ontology writer's own unit";
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

    /// <summary>
    /// Canonical decomposition with combining marks dropped ("Sécure" → "Secure") — an APPROXIMATION of Dataverse's
    /// accent-insensitive name match (it misses ß/ss, æ/ae, ø and similar). Good enough for a first-filter 400; the binding
    /// checks compare unit IDs Dataverse resolves.
    /// </summary>
    private static string FoldAccents(string value)
        => new string(value.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());
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
