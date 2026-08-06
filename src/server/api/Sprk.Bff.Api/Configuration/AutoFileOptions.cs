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
/// DETERMINISTIC matches at or above <see cref="Threshold"/>. Per the C-1 narrowing
/// (<c>email-communication-intelligence-r1</c>, ADR-045 path-A exception), only rungs 0 (ExplicitReference)
/// and 1 (ThreadContinuity) auto-file by default — rungs 2 (ParticipantCorrelation) and 3
/// (StructuralDetector) still MATCH but resolve to <c>Suggested</c> unless
/// <see cref="Rung2And3AutoFileEnabled"/> is toggled on (legacy pre-C-1 behavior, kill-switch-governed
/// per ADR-018 so the narrowing is revertible without a redeploy). AI rungs (4–5) NEVER auto-file
/// regardless of either flag. The kill-switch is the per-tenant escape hatch; misfile = re-file (audited),
/// never delete (R-1).
/// </para>
/// <para>
/// <b>Per-tenant:</b> a deployment that fronts multiple tenants can override the global default per
/// tenant via <see cref="Tenants"/> (keyed by an opaque tenant key the caller supplies). When no tenant
/// key is supplied, or no override exists for it, the global <see cref="Enabled"/>/<see cref="Threshold"/>/
/// <see cref="Rung2And3AutoFileEnabled"/> apply. Most single-org deployments never populate
/// <see cref="Tenants"/> and simply flip the global flag.
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
    /// C-1 auto-file narrowing kill-switch (ADR-045 path-A exception; ADR-018 governance).
    /// <c>false</c> (default) = C-1 narrowed behavior — only rung 0 (ExplicitReference) and rung 1
    /// (ThreadContinuity) are auto-file-eligible; rung 2 (ParticipantCorrelation) and rung 3
    /// (StructuralDetector) still match but resolve to <c>Suggested</c>. <c>true</c> = legacy pre-C-1
    /// behavior — rungs 0–3 are all auto-file-eligible. Togglable without a redeploy (ADR-018).
    /// </summary>
    public bool Rung2And3AutoFileEnabled { get; set; } = false;

    /// <summary>
    /// Core record types (Dataverse entity logical names) that may be AUTO-ASSOCIATED at capture — i.e.
    /// written to a <c>sprk_regarding*</c> lookup by the engine without human confirmation. Default:
    /// matter + project + service request. A match on ANY OTHER regarding target (contact, organization,
    /// account, invoice, work-assignment, event, budget, report-card, analysis) is surfaced as a
    /// <c>Suggested</c> review candidate the user confirms — NEVER written automatically (owner rule,
    /// 061 UAT round-3, 2026-07-31: "only auto-associate to our core records; contacts/orgs/invoices/etc.
    /// can be suggestions the user associates, but never auto-associated").
    /// <para>
    /// This is BOTH the auto-file-STATUS gate (only a core target can push a communication to
    /// <c>Resolved</c>) AND the WRITE gate (only a core field is persisted; non-core fields stay
    /// candidate-only). Tunable per ADR-018 without a redeploy — an operator can add a type here (e.g.
    /// <c>sprk_workassignment</c>) or remove one, and the engine picks it up on the next decision.
    /// </para>
    /// </summary>
    public List<string> CoreWritableEntities { get; set; } = new()
    {
        "sprk_matter",
        "sprk_project",
        "sprk_servicerequest",
    };

    /// <summary>
    /// Optional per-tenant overrides, keyed by an opaque tenant key. A present override replaces the
    /// global <see cref="Enabled"/>, <see cref="Threshold"/>, <see cref="Rung2And3AutoFileEnabled"/>,
    /// and/or <see cref="CoreWritableEntities"/> for that tenant only.
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

    /// <summary>
    /// Per-tenant C-1 narrowing override; null = inherit global
    /// <see cref="AutoFileOptions.Rung2And3AutoFileEnabled"/>.
    /// </summary>
    public bool? Rung2And3AutoFileEnabled { get; set; }

    /// <summary>
    /// Per-tenant core-writable-entities override; null = inherit global
    /// <see cref="AutoFileOptions.CoreWritableEntities"/>. A present list REPLACES the global set for
    /// that tenant (it is not merged).
    /// </summary>
    public List<string>? CoreWritableEntities { get; set; }
}
