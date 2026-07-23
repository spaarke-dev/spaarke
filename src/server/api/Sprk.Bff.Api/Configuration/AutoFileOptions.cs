using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Auto-file kill-switch + threshold configuration for the Association Engine (R4 FR-11, ADR-018).
/// Bound from the <c>Communication:AutoFile</c> section and consumed via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
/// so a change to <see cref="Enabled"/> / <see cref="Threshold"/> (App Service config, Azure App
/// Configuration, or Key Vault reference) flips the engine to suggest-only WITHOUT a redeploy — the
/// ADR-018 guarantee.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR Tension Path A (owner override of design DEC-4):</b> auto-file is ON at launch for
/// DETERMINISTIC (rungs 0–3) matches at or above <see cref="Threshold"/>. AI rungs (4–5) NEVER auto-file
/// regardless of this flag. The kill-switch is the per-tenant escape hatch; misfile = re-file (audited),
/// never delete (R-1).
/// </para>
/// <para>
/// <b>Per-tenant:</b> a deployment that fronts multiple tenants can override the global default per
/// tenant via <see cref="Tenants"/> (keyed by an opaque tenant key the caller supplies). When no tenant
/// key is supplied, or no override exists for it, the global <see cref="Enabled"/>/<see cref="Threshold"/>
/// apply. Most single-org deployments never populate <see cref="Tenants"/> and simply flip the global flag.
/// </para>
/// </remarks>
public class AutoFileOptions
{
    public const string SectionName = "Communication:AutoFile";

    /// <summary>
    /// Global auto-file kill-switch. <c>true</c> (default) = deterministic ≥ <see cref="Threshold"/>
    /// matches auto-file to <c>Resolved</c>. <c>false</c> = the engine downgrades those same matches to
    /// <c>Suggested</c> (suggest-only) with no code change or redeploy.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Confidence at or above which a DETERMINISTIC match auto-files (default 0.85). Tunable up (more
    /// conservative) or down (more aggressive auto-routing) per the owner's accuracy/coverage trade-off.
    /// </summary>
    [Range(0.0, 1.0, ErrorMessage = "Communication:AutoFile:Threshold must be in [0.0, 1.0].")]
    public double Threshold { get; set; } = 0.85;

    /// <summary>
    /// Optional per-tenant overrides, keyed by an opaque tenant key. A present override replaces the
    /// global <see cref="Enabled"/> and/or <see cref="Threshold"/> for that tenant only.
    /// </summary>
    public Dictionary<string, AutoFileTenantOverride> Tenants { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Per-tenant override of the global <see cref="AutoFileOptions"/>. Each field is independently optional:
/// a null field falls back to the global value.
/// </summary>
public class AutoFileTenantOverride
{
    /// <summary>Per-tenant kill-switch override; null = inherit global <see cref="AutoFileOptions.Enabled"/>.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Per-tenant threshold override; null = inherit global <see cref="AutoFileOptions.Threshold"/>.</summary>
    [Range(0.0, 1.0, ErrorMessage = "Communication:AutoFile:Tenants:*:Threshold must be in [0.0, 1.0].")]
    public double? Threshold { get; set; }
}
