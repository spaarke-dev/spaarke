// ---------------------------------------------------------------------------
// TenancyModel.cs — one shared discriminator for the two Spaarke deployment
// models (D-12) + a parse-at-the-edge parser that rejects rather than defaults.
//
// TASK 223 (D-12 remediation, INCOMING-D12-D13-REMEDIATION.md §5 Item 2) —
// introduced 2026-09-29. Replaces the ad-hoc string-comparison branches at 8
// production sites (B1–B8 per D-12-code-branch-inventory.md; B9 was Task 222's
// H3 shared-app-reg branch deletion) + the 4 silent defaults (D1–D4). H1's
// TryParse pattern (InvalidTenancyModel rejection code) generalises here.
//
// DESIGN INVARIANTS (BINDING; do NOT relax):
//
// 1. Enum member names round-trip byte-for-byte to the historical string
//    literals: `TenancyModel.Model1Shared.ToString() == "Model1Shared"` and
//    `TenancyModel.Model2Dedicated.ToString() == "Model2Dedicated"`. H12c's
//    idempotency key format `h12c-{customerId}-{tenancyModel}-{endpointHash}`
//    embeds the tenancy string verbatim (see H12cRuntimeReferencesHandler.
//    BuildIdempotencyKey). ANY completed H12c phase becomes invalidated if
//    the string changes. Item 3 (Task 224) owns any actual string value
//    change (fanning value 1 by Profile); Item 2 must NOT precipitate that.
//
// 2. No [JsonConverter] on this enum. The enum is a computed helper for
//    handler branching; the on-disk / on-wire representation for
//    ProvisioningRun.TenancyModel stays a `string` field (Cosmos wire-format
//    preservation). Handlers call TenancyModelParser.TryParse at entry.
//
// 3. Explicit integer values (Model1Shared = 0, Model2Dedicated = 1) match
//    the current sprk_tenancymodel Dataverse option-set integers
//    (Extend-DataverseEnvironmentSchema-v3.3.ps1). Reserved numeric ordering
//    enables future Item 3 fan-by-Profile without renumbering.
//
// 4. Parser is CASE-SENSITIVE. H1 + H3 (the two fail-loud sites before
//    Task 223) required exact case-sensitive match. ArmCostEnvelopeChecker's
//    B8 was case-sensitive by accident but happens to align; the rewrite
//    forces this alignment intentionally. Callers passing lowercase / mixed
//    case get a TryParse=false and the surrounding handler's rejection code.
//
// 5. No `ParseOrDefault` overload. No `ParseIgnoreCase` overload. The
//    absence is load-bearing — silent defaulting is the pre-Item-2 failure
//    mode this file's existence retires.
//
// PARALLEL ENUM NOTE (documented, non-blocking): a name-compatible
// TenancyModel enum exists at
// `src/server/api/Sprk.Bff.Api/Services/Registration/DataverseEnvironmentRecord.cs`
// in the Sprk.Bff.Api.Services.Registration namespace (task 023 v3 addition).
// The BFF enum is internal to BFF's Registration layer; this ControlPlane
// enum is used by the L2 handlers. The two live in separate assemblies with
// no cross-reference. Both share the identical shape
// (Model1Shared = 0, Model2Dedicated = 1), which guarantees any Dataverse
// row's tenancyModel string round-trips identically whichever service reads
// it. Future consolidation into a shared Contracts assembly is out of scope
// for Task 223.
// ---------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Core.Models;

/// <summary>
/// The Spaarke deployment tenancy discriminator (per D-12; INCOMING-D12-D13-REMEDIATION.md §5 Item 2).
/// Member names are the exact string values embedded in the H12c idempotency-key format and stored in
/// Dataverse's <c>sprk_tenancymodel</c> option set + Cosmos <c>ProvisioningRun.TenancyModel</c> string
/// field — do NOT rename. Integer values match the Dataverse option-set integers
/// (see Extend-DataverseEnvironmentSchema-v3.3.ps1).
/// </summary>
public enum TenancyModel
{
    /// <summary>
    /// Shared Spaarke-tenant platform tier. Currently the trial/SMB profile lives here; post-D-12
    /// this collapses to a single Spaarke-hosted profile pending Item 3 (Task 224). Numeric value 0
    /// matches <c>sprk_tenancymodel</c>.
    /// </summary>
    Model1Shared = 0,

    /// <summary>
    /// Dedicated per-customer stamp in a customer-owned Azure tenant + subscription. Numeric value 1
    /// matches <c>sprk_tenancymodel</c>. Post-D-12 this is the only long-term profile shape; Item 3
    /// (Task 224) fans value 1 by <c>Profile</c> so a Spaarke-hosted Model 2 becomes distinguishable
    /// from a customer-hosted Model 2.
    /// </summary>
    Model2Dedicated = 1
}

/// <summary>
/// Parse-at-the-edge parser for <see cref="TenancyModel"/>. Case-sensitive; rejects null / whitespace /
/// unknown values. Callers at HTTP + handler entry points invoke <see cref="TryParse"/> and emit their
/// own rejection code on failure (H1's <c>InvalidTenancyModel</c> is the reference pattern). The
/// deliberate absence of a defaulting overload is what closes D-12's four silent defaults (D1–D4).
/// </summary>
public static class TenancyModelParser
{
    /// <summary>
    /// Attempts to parse a string tenancy value into a <see cref="TenancyModel"/> enum. Returns
    /// <c>true</c> only for exact case-sensitive match of <c>"Model1Shared"</c> or
    /// <c>"Model2Dedicated"</c>. Any other input (null, empty, whitespace, wrong-case, unknown value)
    /// returns <c>false</c>. Callers reject on <c>false</c> with a handler-specific rejection code.
    /// </summary>
    public static bool TryParse(string? value, out TenancyModel model)
    {
        // Task 223 (D-12): direct-name-list match, deliberately NOT Enum.TryParse.
        // Enum.TryParse has two behaviours we do NOT want at this edge:
        //   (a) it accepts numeric strings ("0" → Model1Shared, "1" → Model2Dedicated),
        //       which would silently accept a legacy integer-serialised value — Cosmos
        //       stores tenancyModel as a string, and H12c embeds the STRING literal in
        //       its idempotency key, so a numeric-string acceptance would produce a
        //       distinct key for the same customer/model pair.
        //   (b) it trims leading/trailing whitespace before matching, so " Model2Dedicated"
        //       would round-trip as Model2Dedicated but then fail an equality check against
        //       the original string field. H12c's key format embeds byte-for-byte; padded
        //       input must fail here so callers correct their source, not swallow the pad.
        // A direct string-equality scan against Enum.GetNames avoids both — no numeric
        // acceptance, no auto-trim. `Ordinal` because the case-sensitivity is load-bearing.
        if (!string.IsNullOrEmpty(value))
        {
            foreach (var name in Enum.GetNames<TenancyModel>())
            {
                if (string.Equals(value, name, StringComparison.Ordinal))
                {
                    model = Enum.Parse<TenancyModel>(name);
                    return true;
                }
            }
        }

        model = default;
        return false;
    }

    /// <summary>
    /// Parses a string tenancy value into a <see cref="TenancyModel"/> enum or throws
    /// <see cref="TenancyModelParseException"/> on failure. Reserved for callers that KNOW the value
    /// has been pre-validated (typically because <see cref="TryParse"/> was already called earlier in
    /// the request lifecycle). Handlers on the failing path should prefer <see cref="TryParse"/> so
    /// they can emit a structured rejection code instead of throwing.
    /// </summary>
    public static TenancyModel Parse(string? value)
    {
        if (!TryParse(value, out var model))
        {
            throw new TenancyModelParseException(value);
        }
        return model;
    }

    /// <summary>
    /// Returns a human-readable list of the accepted <see cref="TenancyModel"/> string values for use
    /// in rejection diagnostics. Format: <c>"Model1Shared, Model2Dedicated"</c>. Uses the enum member
    /// names directly so it stays in lock-step with new enum values (Item 3 will add more).
    /// </summary>
    public static string FormatExpectedValues()
        => string.Join(", ", Enum.GetNames<TenancyModel>());
}

/// <summary>
/// Thrown by <see cref="TenancyModelParser.Parse"/> when the input string cannot be parsed as a
/// <see cref="TenancyModel"/>. Handler entry-points should prefer <see cref="TenancyModelParser.TryParse"/>
/// so they can emit a structured rejection code instead of catching this.
/// </summary>
public sealed class TenancyModelParseException : Exception
{
    /// <summary>The offending input string (may be null / empty / whitespace).</summary>
    public string? OffendingValue { get; }

    public TenancyModelParseException(string? offendingValue)
        : base($"'{offendingValue ?? "(null)"}' is not a recognized TenancyModel. Expected: {TenancyModelParser.FormatExpectedValues()}.")
    {
        OffendingValue = offendingValue;
    }
}
