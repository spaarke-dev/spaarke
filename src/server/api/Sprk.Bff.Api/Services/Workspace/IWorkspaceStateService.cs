using Sprk.Bff.Api.Models.Workspace;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Q4 hybrid persistence for R6 Pillar 6a workspace tabs.
///
/// <para>
/// Storage tiers:
/// <list type="bullet">
///   <item><b>Redis hot tier</b> — 24h TTL. Key <c>workspace:{tenantId}:{sessionId}</c>
///   (ADR-014 + NFR-16 binding). Every active-session tab is mirrored here.</item>
///   <item><b>Cosmos durable tier</b> — container <c>memory</c>, partition key
///   <c>/tenantId</c>, document <c>id = workspace-tab_{tenantId}_{tabId}</c>,
///   <c>documentType = "workspace-tab"</c>. Populated on
///   <see cref="PinTabAsync"/> (matter-attach + pin promotion).</item>
/// </list>
/// </para>
///
/// <para>
/// Operation semantics:
/// <list type="bullet">
///   <item><see cref="GetTabsAsync"/> — returns the MERGED set: hot-tier rows for the
///   (tenant, session) tuple UNION durable-tier rows whose matterId is referenced by the
///   hot-tier rows. Hot-tier rows take precedence (same id) — most-recent wins.</item>
///   <item><see cref="UpsertTabAsync"/> — Redis-only write (24h TTL).
///   Does not touch Cosmos.</item>
///   <item><see cref="PinTabAsync"/> — sets <see cref="WorkspaceTab.IsPinned"/> = true and
///   writes through to Cosmos durable. Redis row is preserved.</item>
///   <item><see cref="CloseTabAsync"/> — removes the row from Redis ONLY. Does not delete
///   Cosmos durable rows (pinned-to-matter rows persist).</item>
/// </list>
/// </para>
///
/// <para>
/// Tenant isolation (binding, NFR-16): tenantId appears in every Redis key and in the
/// Cosmos partition key. Cross-tenant reads are structurally impossible.
/// </para>
///
/// <para>
/// Placement (CLAUDE.md §10 / ADR-013): workspace-state plumbing, NOT AI capability.
/// MUST NOT inject <c>IOpenAiClient</c>, <c>IPlaybookService</c>, or other AI-internal
/// types into this service.
/// </para>
/// </summary>
public interface IWorkspaceStateService
{
    /// <summary>
    /// Returns the merged tab list for a (tenant, session) tuple:
    /// hot-tier (Redis) rows UNION durable-tier (Cosmos) rows. Hot-tier rows for the same
    /// <see cref="WorkspaceTab.Id"/> override durable-tier rows.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="sessionId">Chat session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Workspace tabs for this session (may be empty).</returns>
    Task<IReadOnlyList<WorkspaceTab>> GetTabsAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Upserts a tab into the Redis hot tier with 24h TTL. The tab is identified by
    /// (<paramref name="tenantId"/>, <paramref name="sessionId"/>, <c>tab.Id</c>).
    /// Does NOT touch the Cosmos durable tier.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="sessionId">Chat session identifier.</param>
    /// <param name="tab">Tab to upsert.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpsertTabAsync(
        string tenantId,
        string sessionId,
        WorkspaceTab tab,
        CancellationToken ct = default);

    /// <summary>
    /// Promotes a tab from hot tier to Cosmos durable tier:
    /// sets <see cref="WorkspaceTab.IsPinned"/> = true, updates
    /// <see cref="WorkspaceTab.MatterContext"/>.MatterId to <paramref name="matterId"/>,
    /// writes through to BOTH Redis (24h TTL refresh) AND Cosmos. The Redis row is
    /// preserved (still served from hot tier until TTL expiry).
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="sessionId">Chat session identifier.</param>
    /// <param name="tabId">Tab identifier (<see cref="WorkspaceTab.Id"/>).</param>
    /// <param name="matterId">Dataverse matter id to attach.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PinTabAsync(
        string tenantId,
        string sessionId,
        string tabId,
        string matterId,
        CancellationToken ct = default);

    /// <summary>
    /// Removes the tab from the Redis hot tier. Does NOT touch Cosmos durable rows —
    /// a pinned tab survives close.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="sessionId">Chat session identifier.</param>
    /// <param name="tabId">Tab identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CloseTabAsync(
        string tenantId,
        string sessionId,
        string tabId,
        CancellationToken ct = default);
}
