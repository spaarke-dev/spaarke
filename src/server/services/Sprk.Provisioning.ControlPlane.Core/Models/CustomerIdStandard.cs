// ---------------------------------------------------------------------------
// CustomerIdStandard.cs — the customerId rule at the provisioning intake edge.
//
// T237 (2026-09-30; owner D10 adopting unified-access-control-r2 D-14,
// INCOMING-CUSTOMERID-STANDARD.md §3.1). A customerId is 3–8 characters,
// lowercase letters and digits, starting with a letter. The derivation lives in
// docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md § "The customerId
// standard":
//   - max 8: customer.bicep's Key Vault name `sprk-{id}-{env}-kv` must fit 24
//     chars without ending in a hyphen (env 'staging' is the binding case);
//   - lowercase letters + digits: the storage-account name strips hyphens, so
//     `acme-x` and `acmex` would silently share one account. Neither ARM (no
//     @pattern) nor a Dataverse text column can enforce this, so the intake code
//     path is the ONLY place it is enforced.
//
// The same pattern string is asserted at BFF runtime by
// Sprk.Bff.Api.Configuration.CustomerIdResolver.CustomerIdPattern. The control
// plane does not reference the BFF (provisioning/BFF boundary), so the constant
// is restated here. Nothing compiles the two together: keep the literal identical
// (IntakeSchemaProfileParityTests pins this one to intake.schema.json). The BFF
// derives its id from a resource-group name, so it does not need this file's
// extra trailing-newline guard.
//
// Reserved ids: `platform`, `shared`, `byok` match the pattern but occupy the
// customerId position in NON-customer resource-group names
// (rg-spaarke-platform-{env} hosts the BFF + L2; rg-spaarke-shared-{env} is the
// retired Model 1 tier; rg-spaarke-byok-prod). A customer stamp with one of these
// ids would deploy into that group, and its own BFF would refuse to start —
// CustomerIdResolver.NonCustomerResourceGroupSegments refuses the same three.
//
// Reject, never repair: no trimming, lower-casing or abbreviation. A longer
// customer name is abbreviated by the operator at intake (northwind → nwind),
// and the full name is recorded once on the registry row (sprk_name).
// ---------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Sprk.Provisioning.ControlPlane.Core.Models;

/// <summary>
/// The customerId standard (<c>^[a-z][a-z0-9]{2,7}$</c>) enforced at provisioning intake.
/// </summary>
public static partial class CustomerIdStandard
{
    /// <summary>
    /// 3–8 lowercase letters and digits, first character a letter. Must equal
    /// <c>Sprk.Bff.Api.Configuration.CustomerIdResolver.CustomerIdPattern</c>.
    /// </summary>
    public const string Pattern = "^[a-z][a-z0-9]{2,7}$";

    /// <summary>
    /// Ids that satisfy <see cref="Pattern"/> but name non-customer resource groups. Same set as the
    /// BFF's <c>CustomerIdResolver.NonCustomerResourceGroupSegments</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedIds =
        new HashSet<string>(StringComparer.Ordinal) { "platform", "shared", "byok" };

    /// <summary>Operator-facing statement of the rule, for rejection messages.</summary>
    public const string Description =
        "3-8 lowercase letters and digits, starting with a letter (see AZURE-RESOURCE-NAMING-CONVENTION.md § \"The customerId standard\")";

    /// <summary>
    /// True when <paramref name="value"/> matches <see cref="Pattern"/> exactly. Surrounding
    /// whitespace is NOT trimmed — a padded value is rejected, not repaired.
    /// </summary>
    /// <remarks>
    /// In .NET, <c>$</c> also matches before a final <c>\n</c>, so <c>"acme\n"</c> satisfies the
    /// pattern. The length check closes that gap while keeping <see cref="Pattern"/> byte-identical
    /// to the intake schema's (ECMA-262, where <c>$</c> is end-of-input) and the BFF's.
    /// </remarks>
    public static bool IsValid(string? value) =>
        value is not null && CustomerIdRegex().Match(value) is { Success: true } match && match.Length == value.Length;

    /// <summary>True when <paramref name="value"/> is one of <see cref="ReservedIds"/>.</summary>
    public static bool IsReserved(string? value) => value is not null && ReservedIds.Contains(value);

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex CustomerIdRegex();
}
