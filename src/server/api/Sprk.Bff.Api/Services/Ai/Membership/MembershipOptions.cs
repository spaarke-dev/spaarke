// R3 Part 1 — User-Record Membership Resolution (config binding skeleton)
// Task 012 (2026-06-21): Options class + binding only. Full discovery logic
// arrives in task 030 (MembershipFieldDiscoveryService). Defaults are
// intentionally empty/conservative so production deployments which never opt
// in to the discovery service can still resolve IOptions<MembershipOptions>
// without error. Operators populate values via the "Membership" appsettings
// section. Reference: projects/spaarke-platform-foundations-r3/design.md
// Part 1 § Configuration shape; ADR-034 (forthcoming).

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Configuration for user-record membership resolution (R3 Part 1).
/// Drives <c>MembershipFieldDiscoveryService</c> + <c>MembershipResolverService</c>
/// (both arrive in later P4 tasks). Binds from the <c>"Membership"</c>
/// appsettings section via the Options pattern (ADR-010).
/// </summary>
public sealed class MembershipOptions
{
    /// <summary>
    /// Configuration section name used by <c>IConfiguration.GetSection(...)</c>.
    /// </summary>
    public const string SectionName = "Membership";

    /// <summary>
    /// Identity tables (lookup targets) that count as "person/group" identities
    /// during membership discovery. Lookups pointing to other tables are
    /// ignored by the discovery service. Defaults to an empty list at the
    /// property level — the canonical defaults are seeded by
    /// <see cref="MembershipOptionsDefaults"/> via
    /// <c>IPostConfigureOptions&lt;MembershipOptions&gt;</c> ONLY when the
    /// bound list is empty, so operator-provided configuration overrides
    /// cleanly (IConfiguration.Bind APPENDS to List&lt;T&gt; properties — a
    /// property-level default would double-up the canonical entries when the
    /// operator binds the same names).
    /// </summary>
    public List<IdentityTableConfig> IncludedIdentityTables { get; set; } = new();

    /// <summary>
    /// Logical names of lookup fields that are always excluded from membership
    /// resolution regardless of entity. Typical entries: <c>createdby</c>,
    /// <c>modifiedby</c>, <c>createdonbehalfby</c>, <c>modifiedonbehalfby</c>.
    /// Defaults seeded by <see cref="MembershipOptionsDefaults"/> only when the
    /// bound list is empty (see <see cref="IncludedIdentityTables"/> rationale).
    /// </summary>
    public List<string> GlobalFieldExclusions { get; set; } = new();

    /// <summary>
    /// Strategy used to derive role names from lookup field logical names.
    /// Current supported value: <c>"CamelCase"</c>
    /// (e.g., <c>sprk_AssignedAttorney1</c> → <c>assignedAttorney</c>).
    /// Additional strategies may be added in later tasks (per design.md Part 1).
    /// </summary>
    public string RoleNameStrategy { get; set; } = "CamelCase";

    /// <summary>
    /// TTL for the entity-metadata discovery cache (per entity type) in minutes.
    /// Discovery results are cached to avoid repeated Dataverse
    /// <c>EntityDefinitions</c> calls. Default: 60 minutes.
    /// </summary>
    public int MetadataCacheTtlMinutes { get; set; } = 60;

    /// <summary>
    /// Per-entity overrides applied AFTER auto-discovery. Keyed by the entity's
    /// logical name (e.g., <c>sprk_matter</c>). Use to exclude specific fields,
    /// force-include normally-excluded fields, or override role names for
    /// disambiguation (e.g., <c>sprk_assignedlawfirm1</c> +
    /// <c>sprk_assignedlawfirm2</c> → <c>"assignedLawFirm"</c>).
    /// </summary>
    public Dictionary<string, EntityOverride> EntityOverrides { get; set; } = new();

    /// <summary>
    /// Configuration for resolving the user → <c>sprk_organization</c> mapping
    /// per <see cref="IOrganizationMembershipResolver"/> (task 032). When
    /// <see cref="OrganizationLookupOptions.UserLookupField"/> is empty the
    /// resolver returns an empty list (fail-soft) — meaning a user simply has
    /// no organizational affiliations. See
    /// <c>notes/sprk-organization-mapping-decision.md</c> for the mechanism
    /// decision (Option b — configurable Lookup field on sprk_organization
    /// pointing to systemuser).
    /// </summary>
    public OrganizationLookupOptions OrganizationLookup { get; set; } = new();
}

/// <summary>
/// Configuration for the user → <c>sprk_organization</c> mapping mechanism
/// (task 032 — Option b). Operators configure a single Lookup field on
/// <c>sprk_organization</c> that points to <c>systemuser</c>; the resolver
/// queries organizations whose configured field equals the systemuser GUID.
/// </summary>
public sealed class OrganizationLookupOptions
{
    /// <summary>
    /// Logical name of the Lookup field on <c>sprk_organization</c> that
    /// points to <c>systemuser</c>. Common operator choices:
    /// <c>sprk_owneruser</c>, <c>sprk_relationshipowner</c>, or any custom
    /// field added for this purpose. The field MUST target the
    /// <c>systemuser</c> entity. Empty string (default) means no mapping is
    /// configured — the resolver returns an empty list and logs an info
    /// message at startup-first-use.
    /// </summary>
    public string UserLookupField { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of organization GUIDs returned per user. Hard cap to
    /// protect against runaway queries on misconfigured environments. Default
    /// 1000 (well above any plausible per-user count).
    /// </summary>
    public int MaxOrganizationsPerUser { get; set; } = 1000;
}

/// <summary>
/// Maps a Dataverse table name to a logical identity type. Used by
/// <c>MembershipFieldDiscoveryService</c> to decide which lookup targets count
/// as person/group identities. <c>IdentityType</c> is the public-facing label
/// the API returns to callers (per design.md Part 1 § Identity normalization).
/// </summary>
public sealed class IdentityTableConfig
{
    /// <summary>Dataverse logical table name (lowercase), e.g., <c>systemuser</c>.</summary>
    public string Table { get; set; } = string.Empty;

    /// <summary>
    /// Logical identity type label returned to API callers, e.g., <c>SystemUser</c>,
    /// <c>Contact</c>, <c>Team</c>, <c>BusinessUnit</c>, <c>Account</c>, <c>Organization</c>.
    /// </summary>
    public string IdentityType { get; set; } = string.Empty;
}

/// <summary>
/// Per-entity override applied AFTER auto-discovery for a single entity.
/// All collections default to empty (no override).
/// </summary>
public sealed class EntityOverride
{
    /// <summary>
    /// Logical names of lookup fields to exclude from membership resolution
    /// for THIS entity (in addition to <see cref="MembershipOptions.GlobalFieldExclusions"/>).
    /// </summary>
    public List<string> ExcludedFields { get; set; } = new();

    /// <summary>
    /// Logical names of lookup fields to force-include even if they would
    /// otherwise be globally excluded. Rare; use for entity-specific
    /// exceptions to global exclusions.
    /// </summary>
    public List<string> IncludedFields { get; set; } = new();

    /// <summary>
    /// Field-to-role-name overrides for THIS entity. Used when the auto-derived
    /// role name needs disambiguation. Example: <c>sprk_assignedlawfirm1</c> +
    /// <c>sprk_assignedlawfirm2</c> both mapping to <c>"assignedLawFirm"</c>.
    /// Key = lookup field logical name; value = role name.
    /// </summary>
    public Dictionary<string, string> FieldRoleOverrides { get; set; } = new();
}

/// <summary>
/// Post-configuration seeding for <see cref="MembershipOptions"/>. Populates
/// the canonical Spaarke identity tables + audit-field exclusions ONLY when the
/// bound configuration left them empty.
/// </summary>
/// <remarks>
/// <para><b>R7 W12 task 130 (2026-06-30) — root-cause fix for "0 memberships for
/// every user in spaarkedev1":</b></para>
/// <para>The <see cref="MembershipFieldDiscoveryService"/> classifies a lookup
/// as "discovered" only when its target table appears in
/// <see cref="MembershipOptions.IncludedIdentityTables"/>. Prior to this seeding
/// step, the property defaulted to an empty list and the <c>"Membership"</c>
/// appsettings section was not present in any deployed environment
/// (<c>appsettings.template.json</c>, <c>appsettings.Production.json.template</c>,
/// and the Bicep <c>appSettings</c> all omitted it — only the GITIGNORED
/// <c>appsettings.Development.json.template</c> defined it). The net result:
/// every membership-bearing lookup on every entity was classified as
/// <c>target-table-not-in-identity-list</c>, the downstream resolver returned
/// zero descriptors, and <see cref="MembershipResolverService.ResolveAsync"/>
/// emitted an empty <see cref="MembershipResponse"/> for every user.</para>
/// <para><b>Why post-configure rather than property-initializer defaults:</b>
/// <c>IConfiguration.Bind</c> APPENDS to <see cref="List{T}"/> properties — it
/// does not replace. Defaulting at the property initializer would cause every
/// operator-supplied entry to duplicate the canonical defaults. The
/// post-configure pattern seeds defaults ONLY when the bound list is empty, so
/// operator config replaces cleanly.</para>
/// <para>Registered by <c>MembershipModule.AddMembership</c> via
/// <c>ConfigureOptions&lt;MembershipOptionsDefaults&gt;</c> AFTER
/// <c>Configure&lt;MembershipOptions&gt;</c>.</para>
/// </remarks>
public sealed class MembershipOptionsDefaults : Microsoft.Extensions.Options.IPostConfigureOptions<MembershipOptions>
{
    /// <summary>The canonical Spaarke identity tables (6 entries).</summary>
    public static IReadOnlyList<IdentityTableConfig> CanonicalIdentityTables { get; } = new[]
    {
        new IdentityTableConfig { Table = "systemuser",        IdentityType = "SystemUser" },
        new IdentityTableConfig { Table = "contact",           IdentityType = "Contact" },
        new IdentityTableConfig { Table = "team",              IdentityType = "Team" },
        new IdentityTableConfig { Table = "businessunit",      IdentityType = "BusinessUnit" },
        new IdentityTableConfig { Table = "account",           IdentityType = "Account" },
        new IdentityTableConfig { Table = "sprk_organization", IdentityType = "Organization" },
    };

    /// <summary>
    /// The standard Dataverse audit lookups (4 entries). These point to
    /// <c>systemuser</c> but never represent membership in a business sense
    /// (they record who created/modified a row, not who is assigned to it).
    /// </summary>
    public static IReadOnlyList<string> CanonicalAuditFieldExclusions { get; } = new[]
    {
        "createdby",
        "modifiedby",
        "createdonbehalfby",
        "modifiedonbehalfby",
    };

    /// <inheritdoc/>
    public void PostConfigure(string? name, MembershipOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.IncludedIdentityTables.Count == 0)
        {
            options.IncludedIdentityTables.AddRange(CanonicalIdentityTables);
        }
        if (options.GlobalFieldExclusions.Count == 0)
        {
            options.GlobalFieldExclusions.AddRange(CanonicalAuditFieldExclusions);
        }
    }
}
