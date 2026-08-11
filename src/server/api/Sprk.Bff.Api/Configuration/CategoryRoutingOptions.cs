namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Category→team reconciliation-routing configuration (FR-E7, task 057, ADR-018). Bound from the
/// <c>Communication:CategoryRouting</c> section and consumed via
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> so an operator can add/remove a
/// mapping — or flip routing off — via App Service config / Azure App Configuration / a Key Vault reference
/// WITHOUT a redeploy (the ADR-018 guarantee, same pattern as <see cref="AutoFileOptions"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>What it does (ADR-045 "extend, not fork"):</b> at triage time, a captured email whose resolved
/// <c>sprk_triagecategory</c> NAME matches a key in <see cref="CategoryToTeam"/> is ASSIGNED to the mapped
/// team (its <c>ownerid</c> is set to that team). No <c>sprk_reconciliation</c>-style entity is introduced —
/// the <c>sprk_communication</c> stays the reconciliation unit; routing is an ownership set + per-team
/// filtered grid views (the DataGrid <c>behavior.membershipFilter</c>). An unmapped category is left
/// unassigned (default view; never a forced mis-assignment).
/// </para>
/// <para>
/// <b>Opt-in:</b> <see cref="Enabled"/> defaults to <c>false</c> — routing does nothing until an operator
/// turns it on and populates the map. <b>Per-tenant:</b> a multi-tenant deployment can override the global
/// map / on-off per tenant via <see cref="Tenants"/>; a single-org deployment never populates it.
/// </para>
/// <para>
/// <b>Best-effort (NFR-04):</b> assignment rides the SAME additive triage <c>UpdateAsync</c> and is wrapped
/// so a routing failure (unknown team, resolution error) NEVER drops the triage fields or fails capture.
/// </para>
/// </remarks>
public class CategoryRoutingOptions
{
    public const string SectionName = "Communication:CategoryRouting";

    /// <summary>
    /// Global routing kill-switch. <c>false</c> (default) = no assignment happens (opt-in). <c>true</c> =
    /// a captured email whose triage category is mapped in <see cref="CategoryToTeam"/> is assigned to the
    /// mapped team at triage time.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Map of triage-category NAME (the resolved <c>sprk_triagecategory</c> name, e.g. <c>"Court / Filing"</c>)
    /// → owning TEAM name (the Dataverse <c>team.name</c>). Case-insensitive keys. An email of a mapped
    /// category is assigned to the mapped team; an unmapped category is left unassigned. Operator-managed —
    /// never hard-coded.
    /// </summary>
    public Dictionary<string, string> CategoryToTeam { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional per-tenant overrides, keyed by an opaque tenant key. A present override replaces the global
    /// <see cref="Enabled"/> and/or <see cref="CategoryToTeam"/> for that tenant only.
    /// </summary>
    public Dictionary<string, CategoryRoutingTenantOverride> Tenants { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Per-tenant override of the global <see cref="CategoryRoutingOptions"/>. Each field is independently
/// optional: a null field falls back to the global value.
/// </summary>
public class CategoryRoutingTenantOverride
{
    /// <summary>Per-tenant kill-switch override; null = inherit global <see cref="CategoryRoutingOptions.Enabled"/>.</summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Per-tenant category→team map override; null = inherit global
    /// <see cref="CategoryRoutingOptions.CategoryToTeam"/>. A present map REPLACES the global map for that
    /// tenant (it is not merged).
    /// </summary>
    public Dictionary<string, string>? CategoryToTeam { get; set; }
}
