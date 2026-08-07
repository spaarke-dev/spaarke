using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for the Association Engine's affinity / deterministic-learning rung
/// (<see cref="Sprk.Bff.Api.Services.Communication.Engine.RungKind.Affinity"/>, FR-A4). Bound from the
/// <c>Communication:Affinity</c> section and consumed via
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> so the kill-switch flips WITHOUT a
/// redeploy (ADR-018), mirroring <see cref="AutoFileOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// The rung reads the per-tenant <c>sprk_affinity</c> store (human-confirmation frequencies) and surfaces the
/// highest-frequency record for an untagged message's signal set (sender / sender-domain / subject-keyword /
/// participant-set) as an explainable, SUGGEST-ONLY candidate citing the confirmation count. It NEVER
/// auto-files — structurally guaranteed by its exclusion from the mapper's auto-file / deterministic-write
/// eligibility sets — so these values only tune WHICH suggestion surfaces, never whether one is auto-filed.
/// </para>
/// <para>
/// <b>Per-tenant:</b> a deployment fronting multiple tenants can override the global <see cref="Enabled"/>
/// per tenant via <see cref="Tenants"/> (keyed by the opaque tenant key the caller supplies). When no key is
/// supplied, or no override exists for it, the global <see cref="Enabled"/> applies. Most single-org
/// deployments never populate <see cref="Tenants"/> and just flip the global flag.
/// </para>
/// </remarks>
public class AffinityOptions
{
    public const string SectionName = "Communication:Affinity";

    /// <summary>
    /// Global kill-switch. <c>true</c> (default) runs the affinity rung; <c>false</c> makes it a no-op
    /// without a redeploy (ADR-018). Registered unconditionally (ADR-010); this flag gates behavior.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum confirmation count an affinity row must have before it is surfaced as a suggestion. A single
    /// confirmation is too weak a signal to suggest on; require a repeated pattern. Default 3 (per FR-A4
    /// "after N confirmed associations …"). Keep ≥ 1.
    /// </summary>
    [Range(1, 1000)]
    public int MinConfirmations { get; set; } = 3;

    /// <summary>
    /// Fixed confidence emitted for an affinity match — deliberately in the Suggested band: below the 0.85
    /// auto-file threshold and at/above the mapper's 0.50 suggest/write floor, so the match always surfaces as
    /// a review candidate and NEVER auto-files. (Auto-file is additionally structurally impossible — the rung
    /// is excluded from the mapper's auto-file-eligible set — so this value only affects ranking/surfacing.)
    /// Default 0.60.
    /// </summary>
    [Range(0.50, 0.84)]
    public double SuggestConfidence { get; set; } = 0.60;

    /// <summary>
    /// Max distinct subject keywords extracted from a message as affinity signals (bounds the OR-filter width
    /// of the single affinity lookup). Default 5.
    /// </summary>
    [Range(0, 25)]
    public int MaxSubjectKeywords { get; set; } = 5;

    /// <summary>
    /// Minimum length (chars) a subject token must have to be a keyword signal — drops short/noise tokens
    /// ("re", "fw", "the"). Default 4.
    /// </summary>
    [Range(2, 50)]
    public int MinKeywordLength { get; set; } = 4;

    /// <summary>
    /// Optional per-tenant overrides of <see cref="Enabled"/>, keyed by the opaque tenant key. A present entry
    /// replaces the global <see cref="Enabled"/> for that tenant only; absent = inherit global.
    /// </summary>
    public Dictionary<string, bool> Tenants { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the effective enabled state for <paramref name="tenantKey"/>: a per-tenant override in
    /// <see cref="Tenants"/> wins; otherwise the global <see cref="Enabled"/>. A null/blank key uses the global.
    /// </summary>
    public bool IsEnabledFor(string? tenantKey) =>
        !string.IsNullOrWhiteSpace(tenantKey) && Tenants.TryGetValue(tenantKey, out var t) ? t : Enabled;
}
