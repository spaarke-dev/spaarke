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
    /// ignored by the discovery service.
    /// </summary>
    public List<IdentityTableConfig> IncludedIdentityTables { get; set; } = new();

    /// <summary>
    /// Logical names of lookup fields that are always excluded from membership
    /// resolution regardless of entity. Typical entries: <c>createdby</c>,
    /// <c>modifiedby</c>, <c>createdonbehalfby</c>, <c>modifiedonbehalfby</c>.
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
