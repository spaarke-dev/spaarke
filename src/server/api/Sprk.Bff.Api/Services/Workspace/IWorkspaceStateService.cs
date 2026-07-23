using Sprk.Bff.Api.Models.Workspace;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Q4 hybrid persistence for R6 Pillar 6a workspace tabs — READ surface.
///
/// <para>
/// Storage tiers read on <see cref="GetTabsAsync"/>:
/// <list type="bullet">
///   <item><b>Redis hot tier</b> — 24h TTL. Key
///   <c>tenant:{tenantId}:workspace-state:{sessionId}:v1</c> (ADR-014 + NFR-16 binding).</item>
///   <item><b>Cosmos durable tier</b> — container <c>memory</c>, partition key
///   <c>/tenantId</c>, document discriminator <c>documentType = "workspace-tab"</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// The tab WRITE path (upsert / pin / close) was retired by AIR2-075 together with the
/// orphaned Get/Update/Close Workspace Tab chat tools and the SendWorkspaceArtifact legacy
/// artifact variants. This service now exposes the read path only — consumed by
/// <c>GET /api/workspace/state</c> restore and the <c>SprkChatAgentFactory</c> workspace-state
/// system-prompt block. Any durable rows returned are pre-existing (pinned) tabs; nothing in
/// the BFF writes new tab state.
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
}
