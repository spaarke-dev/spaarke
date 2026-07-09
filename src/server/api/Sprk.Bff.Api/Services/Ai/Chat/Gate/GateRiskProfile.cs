using System.Text.Json;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Chat.Gate;

/// <summary>
/// The catalog-declared risk profile for a capability (Policy v2 note §1) — the DATA
/// extension of ADR-039 <c>side_effect_class</c>. Carries the declared sub-tier plus the
/// five risk factors (reversibility, external visibility, deadline impact, confidentiality/
/// privilege impact, record-of-truth impact) that justify it.
///
/// <para>
/// <b>Two framing invariants (policy-v2 note §0 / FR-A1-03):</b>
/// </para>
/// <list type="number">
///   <item>These are <b>declared properties on the catalog row</b> — authored through the
///   task-020 hoisted seed source (<c>infra/dataverse/sprk_analysistool-*-row.json</c>,
///   nested in the <c>sprk_configuration.riskProfile</c> blob so the seed UPSERT needs no
///   new live column). They are DATA, never a runtime model judgment.</item>
///   <item>A profile whose declared tier UNDER-states its factors is escalated to the
///   stricter tier by <see cref="RiskTierResolver"/> — fail-closed by construction, so an
///   authoring slip can never let a risky write execute at a weaker tier.</item>
/// </list>
///
/// <para>
/// This type is a value object with ZERO behavior beyond parsing catalog DATA. The
/// tier decision lives in <see cref="RiskTierResolver"/>; there is no code path here (or
/// anywhere in the engine) that derives a tier from a model opinion — the escalation
/// trigger in the task POML (ADR-039 one-intent boundary) is honored structurally: risk
/// is only ever read from this DATA.
/// </para>
/// </summary>
public sealed record GateRiskProfile
{
    /// <summary>The catalog-declared sub-tier (Policy v2 note §1 table).</summary>
    public required GateRiskTier DeclaredTier { get; init; }

    /// <summary>The side effect can be undone (a compensating action exists — delete, revert, unsend-window).</summary>
    public bool Reversible { get; init; }

    /// <summary>The side effect is visible outside the platform (email send, filing, external commitment).</summary>
    public bool ExternallyVisible { get; init; }

    /// <summary>The side effect sets or moves a legal deadline / obligation.</summary>
    public bool DeadlineImpact { get; init; }

    /// <summary>The side effect touches confidential / privileged material.</summary>
    public bool ConfidentialityImpact { get; init; }

    /// <summary>The side effect creates or mutates a system-of-record row (matter, project, invoice, …).</summary>
    public bool RecordOfTruthImpact { get; init; }

    /// <summary>
    /// The JSON property name (inside <c>sprk_configuration</c>) that carries the declared
    /// risk profile in the task-020 seed source. Kept as a constant so the seed authoring
    /// and the parser cannot drift.
    /// </summary>
    public const string ConfigurationKey = "riskProfile";

    /// <summary>
    /// Parses the catalog-declared risk profile from a tool row's <c>sprk_configuration</c>
    /// JSON element (the task-020 authored source). Returns null when the row declares no
    /// <c>riskProfile</c> block (legacy / non-side-effecting rows) — callers treat a null
    /// profile as "no declared side-effect risk" and gate by the existing
    /// <c>sprk_sideeffectclass</c> posture (fail-closed, unchanged).
    /// </summary>
    /// <exception cref="FormatException">The <c>riskProfile.tier</c> value is present but not a known tier token.</exception>
    public static GateRiskProfile? FromConfiguration(JsonElement configuration)
    {
        if (configuration.ValueKind != JsonValueKind.Object ||
            !configuration.TryGetProperty(ConfigurationKey, out var profile) ||
            profile.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!profile.TryGetProperty("tier", out var tierEl) || tierEl.ValueKind != JsonValueKind.String)
        {
            throw new FormatException(
                $"Catalog riskProfile is present but missing a string 'tier' — the declared tier is " +
                "required DATA (Policy v2 note §1). A model-judged tier is forbidden (ADR-039).");
        }

        return new GateRiskProfile
        {
            DeclaredTier = ParseTier(tierEl.GetString()!),
            Reversible = ReadBool(profile, "reversible"),
            ExternallyVisible = ReadBool(profile, "externallyVisible"),
            DeadlineImpact = ReadBool(profile, "deadlineImpact"),
            ConfidentialityImpact = ReadBool(profile, "confidentialityImpact"),
            RecordOfTruthImpact = ReadBool(profile, "recordOfTruthImpact"),
        };
    }

    /// <summary>
    /// Maps the seed's short tier token (<c>"0" "1" "2a" "2b" "2c" "3" "4"</c>) to the
    /// <see cref="GateRiskTier"/> contract enum. The token set is closed and case-insensitive.
    /// </summary>
    public static GateRiskTier ParseTier(string token) => token.Trim().ToLowerInvariant() switch
    {
        "0" => GateRiskTier.Tier0,
        "1" => GateRiskTier.Tier1,
        "2a" => GateRiskTier.Tier2a,
        "2b" => GateRiskTier.Tier2b,
        "2c" => GateRiskTier.Tier2c,
        "3" => GateRiskTier.Tier3,
        "4" => GateRiskTier.Tier4,
        _ => throw new FormatException(
            $"Unknown risk tier token '{token}'. Valid catalog tokens: 0, 1, 2a, 2b, 2c, 3, 4 (Policy v2 note §1)."),
    };

    private static bool ReadBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) &&
        (el.ValueKind == JsonValueKind.True ||
         (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var b) && b));
}
