using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Services.Ai.Insights.Extraction;

/// <summary>
/// Per-field confidence threshold values for the D-P10 gating primitive. Realizes
/// <c>D-63</c> (per-field thresholds admin-tunable; calibrated against the D-P11 review surface).
/// <para>
/// Bound to configuration section <see cref="SectionName"/> via <c>IOptionsMonitor</c> in
/// <c>InsightsExtractionModule</c>, so admins can edit appsettings (or Key Vault references)
/// without restarting the BFF. <see cref="IObservationEmitter"/> consumes the latest snapshot
/// on every emission call — there is no per-request caching of thresholds.
/// </para>
/// <para>
/// Starter values match <c>SPEC-phase-1-minimum.md §3.4</c>:
/// <list type="bullet">
///   <item><c>outcomeCategory ≥ 0.75</c></item>
///   <item><c>settlementAmount ≥ 0.85</c></item>
///   <item><c>outcomeDate ≥ 0.85</c></item>
///   <item><c>matterDurationDays ≥ 0.75</c></item>
/// </list>
/// Fields whose extracted confidence is below the configured threshold are DROPPED (not
/// persisted) and logged to App Insights. Fields whose name is not in
/// <see cref="PerField"/> fall back to <see cref="DefaultThreshold"/>.
/// </para>
/// </summary>
public sealed class ConfidenceThresholdOptions
{
    /// <summary>Configuration section name: <c>Insights:Extraction:ConfidenceThresholds</c>.</summary>
    public const string SectionName = "Insights:Extraction:ConfidenceThresholds";

    /// <summary>
    /// Fallback threshold applied to any field whose name is not listed in <see cref="PerField"/>.
    /// Defaults to <c>0.75</c>, which matches the most permissive starter value in
    /// <c>SPEC-phase-1-minimum.md §3.4</c>.
    /// </summary>
    [Range(0.0, 1.0, ErrorMessage = "DefaultThreshold must be in [0.0, 1.0]")]
    public double DefaultThreshold { get; set; } = 0.75;

    /// <summary>
    /// Per-field thresholds. Keys are field names (Observation predicates — e.g.,
    /// <c>outcomeCategory</c>, <c>settlementAmount</c>); values are minimum acceptable
    /// confidence in [0.0, 1.0].
    /// <para>
    /// Defaults match the Phase 1 starter values from <c>SPEC-phase-1-minimum.md §3.4</c>.
    /// Mutable dictionary so the options binder can populate from configuration; treat as
    /// read-only at consumption time.
    /// </para>
    /// </summary>
    public Dictionary<string, double> PerField { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["outcomeCategory"] = 0.75,
        ["settlementAmount"] = 0.85,
        ["outcomeDate"] = 0.85,
        ["matterDurationDays"] = 0.75
    };

    /// <summary>
    /// Resolves the threshold for a given field name — returns the per-field override if one
    /// is configured, otherwise <see cref="DefaultThreshold"/>. Case-insensitive lookup.
    /// </summary>
    public double GetThresholdFor(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName)) return DefaultThreshold;
        return PerField.TryGetValue(fieldName, out var t) ? t : DefaultThreshold;
    }
}
