// ---------------------------------------------------------------------------
// TenancyModel.cs — one shared discriminator for the two Spaarke deployment
// models (D-12) + a parse-at-the-edge parser that rejects rather than defaults.
//
// TASK 223 (2026-09-29) introduced the enum + parser to migrate 8 branch sites
// off ad-hoc string comparisons (B1–B8 per D-12-code-branch-inventory.md) +
// the 4 silent defaults (D1–D4). Members were initially named `Model1Shared`
// and `Model2Dedicated` to preserve H12c's byte-for-byte idempotency-key format
// during T223's parse-or-reject migration.
//
// TASK 224 (2026-09-29 EOD; INCOMING-D12-D13-REMEDIATION.md §5 Item 3 rename)
// renamed the members to `Model1` and `Model2` per owner-decision Q1 (simplest
// labels — D-12 retires the "-Shared" tier entirely; the "-Dedicated" qualifier
// on Model2 was redundant once Model1 also became a dedicated stamp). T223's
// H12c byte-for-byte preservation invariant was RETIRED at T224 per owner-
// decision Q2 (greenfield state — Task 186 first live E2E dispatch has not
// fired, so production Cosmos ProvisioningRun.CompletedPhases[phase='H12c']
// is empty across all customers; no completed phases exist to preserve).
//
// DESIGN INVARIANTS (BINDING; do NOT relax):
//
// 1. Enum member names round-trip byte-for-byte to their string literals:
//    `TenancyModel.Model1.ToString() == "Model1"` and
//    `TenancyModel.Model2.ToString() == "Model2"`. H12c's idempotency key
//    format `h12c-{customerId}-{tenancyModel}-{endpointHash}` embeds the
//    tenancy string verbatim (see H12cRuntimeReferencesHandler.
//    BuildIdempotencyKey). Post-T224 keys use `Model1` / `Model2` literally.
//    Task 225 (Item 4) inherits this format contract but does not alter it.
//
// 2. No [JsonConverter] on this enum. The enum is a computed helper for
//    handler branching; the on-disk / on-wire representation for
//    ProvisioningRun.TenancyModel stays a `string` field (Cosmos wire-format
//    preservation). Handlers call TenancyModelParser.TryParse at entry.
//
// 3. Explicit integer values (Model1 = 0, Model2 = 1) match the current
//    sprk_tenancymodel Dataverse option-set integers
//    (Extend-DataverseEnvironmentSchema-v3.3.ps1; the option-set LABELS were
//    renamed to "Model1" / "Model2" alongside the enum in T224 — the numeric
//    values are unchanged for schema-integer alignment).
//
// 4. Parser is CASE-SENSITIVE. H1 + H3 (the two fail-loud sites before
//    Task 223) required exact case-sensitive match. The rewrite forces this
//    alignment intentionally. Callers passing lowercase / mixed case get a
//    TryParse=false and the surrounding handler's rejection code.
//
// 5. No `ParseOrDefault` overload. No `ParseIgnoreCase` overload. The
//    absence is load-bearing — silent defaulting is the pre-Item-2 failure
//    mode this file's existence retires.
//
// PARALLEL ENUM NOTE (documented, non-blocking): a name-INcompatible
// TenancyModel enum exists at
// `src/server/api/Sprk.Bff.Api/Services/Registration/DataverseEnvironmentRecord.cs`
// in the Sprk.Bff.Api.Services.Registration namespace (task 023 v3 addition).
// That enum's members are still the pre-T224 names (`Model1Shared` /
// `Model2Dedicated`). The two enums live in separate assemblies with no
// cross-reference; both serve internal handler branching in their own layer.
// The BFF's own consumers of that enum need a matching rename (out of scope
// for T224 per constraint on scope creep — file a follow-up if the BFF
// starts reading `sprk_tenancymodel` values written post-T224). Until then
// the BFF layer will read the new Dataverse label values as its enum's
// (name-mismatched) values and fail to parse them — acceptable while no
// customers exist to trigger the read.
// ---------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Core.Models;

/// <summary>
/// The Spaarke deployment tenancy discriminator (per D-12; INCOMING-D12-D13-REMEDIATION.md §5 Item 3
/// rename). Member names are the exact string values embedded in the H12c idempotency-key format and
/// stored in Dataverse's <c>sprk_tenancymodel</c> option set + Cosmos <c>ProvisioningRun.TenancyModel</c>
/// string field — do NOT rename. Integer values match the Dataverse option-set integers
/// (see Extend-DataverseEnvironmentSchema-v3.3.ps1).
/// </summary>
public enum TenancyModel
{
    /// <summary>
    /// Spaarke-hosted dedicated stamp (customer environment lives in a Spaarke-owned Azure tenant +
    /// subscription). Fan-target for Profile <c>spaarke-hosted-model2</c> per INCOMING §5 Item 3.
    /// Numeric value 0 matches <c>sprk_tenancymodel</c>. Pre-T224 this member was named
    /// <c>Model1Shared</c> under a semantic (shared trial/SMB tier) that D-12 has retired.
    /// </summary>
    Model1 = 0,

    /// <summary>
    /// Customer-hosted dedicated stamp (customer environment lives in a customer-owned Azure tenant +
    /// subscription). Fan-target for Profile <c>customer-owned-model2</c> per INCOMING §5 Item 3.
    /// Numeric value 1 matches <c>sprk_tenancymodel</c>. Pre-T224 this member was named
    /// <c>Model2Dedicated</c>; the <c>-Dedicated</c> qualifier became redundant post-D-12 when Model1
    /// also became a dedicated stamp.
    /// </summary>
    Model2 = 1
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
    /// <c>true</c> only for exact case-sensitive match of <c>"Model1"</c> or <c>"Model2"</c>. Any
    /// other input (null, empty, whitespace, wrong-case, unknown value) returns <c>false</c>.
    /// Callers reject on <c>false</c> with a handler-specific rejection code.
    /// </summary>
    public static bool TryParse(string? value, out TenancyModel model)
    {
        // Task 223 (D-12): direct-name-list match, deliberately NOT Enum.TryParse.
        // Enum.TryParse has two behaviours we do NOT want at this edge:
        //   (a) it accepts numeric strings ("0" → Model1, "1" → Model2), which would
        //       silently accept a legacy integer-serialised value — Cosmos stores
        //       tenancyModel as a string, and H12c embeds the STRING literal in its
        //       idempotency key, so a numeric-string acceptance would produce a distinct
        //       key for the same customer/model pair.
        //   (b) it trims leading/trailing whitespace before matching, so " Model2" would
        //       round-trip as Model2 but then fail an equality check against the original
        //       string field. H12c's key format embeds byte-for-byte; padded input must
        //       fail here so callers correct their source, not swallow the pad.
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
    /// in rejection diagnostics. Format: <c>"Model1, Model2"</c>. Uses the enum member names directly
    /// so it stays in lock-step with new enum values.
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
