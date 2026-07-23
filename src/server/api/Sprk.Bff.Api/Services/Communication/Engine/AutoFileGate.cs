using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// The ADR-018 per-tenant auto-file kill-switch (R4 FR-11). Resolves the effective auto-file
/// <see cref="AutoFileSettings.Enabled"/> flag + <see cref="AutoFileSettings.Threshold"/> for a given
/// tenant, re-reading <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> on every call so a config
/// flip takes effect WITHOUT a redeploy.
/// </summary>
/// <remarks>
/// The gate ONLY governs whether a deterministic match auto-files; it never bypasses the confidence
/// ladder and never applies to AI rungs (which never auto-file regardless — enforced in
/// <see cref="AssociationStatusMapper"/>). Registered unconditionally (ADR-010); it is pure config
/// resolution with no external dependency, so no Null-Object peer is required.
/// </remarks>
public sealed class AutoFileGate
{
    private readonly IOptionsMonitor<AutoFileOptions> _options;

    public AutoFileGate(IOptionsMonitor<AutoFileOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// Resolve the effective auto-file settings for <paramref name="tenantKey"/>. A per-tenant override
    /// (when present) replaces the global default field-by-field; a null/unknown tenant key uses the
    /// global default. Read fresh from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> each call.
    /// </summary>
    public AutoFileSettings Resolve(string? tenantKey)
    {
        var o = _options.CurrentValue;
        var enabled = o.Enabled;
        var threshold = o.Threshold;

        if (!string.IsNullOrWhiteSpace(tenantKey) &&
            o.Tenants.TryGetValue(tenantKey, out var overrideEntry) &&
            overrideEntry is not null)
        {
            if (overrideEntry.Enabled.HasValue) enabled = overrideEntry.Enabled.Value;
            if (overrideEntry.Threshold.HasValue) threshold = overrideEntry.Threshold.Value;
        }

        return new AutoFileSettings(enabled, threshold);
    }
}

/// <summary>Effective auto-file policy for one decision.</summary>
/// <param name="Enabled">Whether deterministic ≥ <paramref name="Threshold"/> matches auto-file.</param>
/// <param name="Threshold">Confidence at or above which a deterministic match auto-files.</param>
public readonly record struct AutoFileSettings(bool Enabled, double Threshold);
