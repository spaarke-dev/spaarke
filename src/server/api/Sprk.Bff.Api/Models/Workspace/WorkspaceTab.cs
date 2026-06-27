using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Workspace;

/// <summary>
/// Canonical workspace-tab record (R6 Pillar 6a) — C# mirror of the TypeScript
/// <c>WorkspaceTab</c> contract in
/// <c>src/client/shared/Spaarke.AI.Widgets/src/types/WorkspaceTab.ts</c>.
///
/// <para>
/// Persistence semantics (Q4 hybrid):
/// <list type="bullet">
///   <item><b>Redis hot tier</b> — 24h TTL on key <c>workspace:{tenantId}:{sessionId}</c>;
///   every active-session tab.</item>
///   <item><b>Cosmos durable tier</b> — written through when <see cref="IsPinned"/> becomes
///   true (or matter-attach via <see cref="IWorkspaceStateService.PinTabAsync"/>); container
///   <c>memory</c>, partition key <c>/tenantId</c>, document discriminator
///   <c>"workspace-tab"</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// Discriminated union semantics:
/// <list type="bullet">
///   <item><see cref="WidgetType"/> is the closed 4-variant string discriminator
///   (Summary | DocumentViewer | Dashboard | Table) per Pillar 9.</item>
///   <item><see cref="WidgetData"/>'s <c>kind</c> MUST equal <see cref="WidgetType"/>.
///   Mismatches throw at deserialization.</item>
///   <item><see cref="WorkspaceTabWidgetData"/> uses System.Text.Json polymorphism
///   (<c>[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]</c>).</item>
/// </list>
/// </para>
///
/// <para>
/// Tenant isolation (ADR-014, NFR-16): <see cref="TenantId"/> appears in every Redis key
/// and as the Cosmos partition key. This DTO never leaves the BFF except as a serialized
/// JSON value owned by the originating tenant.
/// </para>
///
/// <para>
/// Provenance + ADR-015: <see cref="WorkspaceTabSourceProvenance.CreatedBy"/> MUST be a
/// deterministic ID (userId GUID, agentId, playbookId) — NEVER raw user message text.
/// </para>
/// </summary>
public sealed class WorkspaceTab
{
    /// <summary>Stable tab identity (client-generated; preserved across persist/restore).</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Pillar 9 visibility category — discriminator for <see cref="WidgetData"/>. Closed union
    /// of 4 variants (<c>Summary | DocumentViewer | Dashboard | Table</c>).
    /// </summary>
    [JsonPropertyName("widgetType")]
    public required string WidgetType { get; init; }

    /// <summary>
    /// Per-variant payload. Narrowed via System.Text.Json polymorphism on the <c>kind</c>
    /// discriminator. MUST satisfy <c>WidgetData.Kind == WidgetType</c>.
    /// </summary>
    [JsonPropertyName("widgetData")]
    public required WorkspaceTabWidgetData WidgetData { get; init; }

    /// <summary>Chat session identity. Scopes Redis hot-tier persistence.</summary>
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    /// <summary>
    /// Tenant identifier. Drives Redis key + Cosmos partition key. Required by NFR-16
    /// per-tenant isolation.
    /// </summary>
    [JsonPropertyName("tenantId")]
    public required string TenantId { get; init; }

    /// <summary>
    /// Pillar 9 visibility flag — when true, the tab's getAgentVisibleState() participates
    /// in the per-turn system-prompt snapshot.
    /// </summary>
    [JsonPropertyName("visibleToAssistant")]
    public required bool VisibleToAssistant { get; init; }

    /// <summary>Provenance (source role + creator id + creation timestamp).</summary>
    [JsonPropertyName("sourceProvenance")]
    public required WorkspaceTabSourceProvenance SourceProvenance { get; init; }

    /// <summary>Matter context anchoring this tab (matterId + matterName).</summary>
    [JsonPropertyName("matterContext")]
    public required WorkspaceTabMatterContext MatterContext { get; init; }

    /// <summary>
    /// True when the tab is pinned. Flips persistence from Redis-only (hot) to Cosmos
    /// durable tier (Q4 hybrid). Mutate via
    /// <see cref="IWorkspaceStateService.PinTabAsync"/>.
    /// </summary>
    [JsonPropertyName("isPinned")]
    public required bool IsPinned { get; init; }

    /// <summary>
    /// True when the tab has user-editing affordances enabled. Pillar 6b's
    /// <c>update_workspace_tab</c> tool MUST refuse mutation when this is false.
    /// </summary>
    [JsonPropertyName("canEdit")]
    public required bool CanEdit { get; init; }

    /// <summary>
    /// ISO-8601 timestamp of the most recent USER edit (not agent edit). Central to Q8
    /// user-wins conflict resolution — Pillar 6b's <c>update_workspace_tab</c> tool
    /// MUST refuse on stale write when this is later than the tool's read timestamp.
    /// Undefined for brand-new tabs.
    /// </summary>
    [JsonPropertyName("lastUserEditAt")]
    public string? LastUserEditAt { get; init; }

    /// <summary>ISO-8601 timestamp of tab creation. Mirrors SourceProvenance.CreatedAt.</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    /// <summary>ISO-8601 timestamp of the most recent mutation (user OR agent).</summary>
    [JsonPropertyName("updatedAt")]
    public required string UpdatedAt { get; init; }
}

/// <summary>
/// Where a workspace tab came from. <see cref="CreatedBy"/> MUST be a deterministic ID
/// (userId GUID, agentId, playbookId) — NEVER raw user message text (ADR-015 binding).
/// </summary>
public sealed class WorkspaceTabSourceProvenance
{
    /// <summary>Origin role: <c>"user"</c> | <c>"agent"</c> | <c>"playbook"</c>.</summary>
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    /// <summary>Deterministic creator id (userId GUID / agentId / playbookId).</summary>
    [JsonPropertyName("createdBy")]
    public required string CreatedBy { get; init; }

    /// <summary>ISO-8601 timestamp of creation.</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }
}

/// <summary>
/// Matter context anchoring a workspace tab. Pillar 7 memory composition uses this for
/// matter-scoped recall; Pillar 6b's "Pin to Matter" affordance reads
/// <see cref="MatterId"/> when promoting to Cosmos durable persistence.
/// </summary>
public sealed class WorkspaceTabMatterContext
{
    /// <summary>Dataverse <c>sprk_matter</c> GUID.</summary>
    [JsonPropertyName("matterId")]
    public required string MatterId { get; init; }

    /// <summary>Human-readable matter name (for tooltips + Pillar 9 agent context).</summary>
    [JsonPropertyName("matterName")]
    public required string MatterName { get; init; }
}
