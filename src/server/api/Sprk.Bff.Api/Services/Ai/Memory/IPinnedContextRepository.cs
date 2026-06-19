using Sprk.Bff.Api.Models.Memory;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// R6 Pillar 7 / task 065 (D-C-18) — repository contract for the user-curated
/// <see cref="PinnedContextItem"/> "memory anchor" entity.
///
/// <para>
/// Pinned-context items are persistent, user-curated content the LLM is instructed to
/// ALWAYS keep in system-prompt context (per spec FR-42). Storage: Cosmos container
/// <c>memory</c>, partition key <c>/tenantId</c>, document discriminator
/// <c>documentType = "pinned-context"</c>. Co-exists with matter-memory and
/// workspace-tab documents on the same partition without id collision (the
/// <c>pinned-context_</c> id prefix is the disambiguator).
/// </para>
///
/// <para>
/// Tenant isolation (NFR-16 / ADR-014): every method takes a <c>tenantId</c> argument
/// that scopes the underlying Cosmos partition key. Cross-tenant reads are structurally
/// impossible.
/// </para>
///
/// <para>
/// Placement (CLAUDE.md §10 / ADR-013): memory plumbing only. The repository does NOT
/// inject AI-internal types (<c>IOpenAiClient</c>, <c>IPlaybookService</c>, etc.).
/// AI-internal callers consume this repository directly per the 2026-05-20 refined
/// ADR-013 boundary rule (no PublicContracts facade for AI-internal collaborators).
/// </para>
///
/// <para>
/// Consumer plan: task 067 (hierarchical memory composition) reads from this repository
/// at chat-turn build time to inject the user's pinned items into the system prompt.
/// The Q7 Pinned Memory UI (task 070, R7) is the write-side surface.
/// </para>
/// </summary>
public interface IPinnedContextRepository
{
    /// <summary>
    /// Creates a new pinned-context document. The caller MUST have set
    /// <see cref="PinnedContextItem.Id"/>, <see cref="PinnedContextItem.TenantId"/>,
    /// and <see cref="PinnedContextItem.UserId"/> consistent with the deterministic
    /// id format <c>pinned-context_{tenantId}_{userId}_{pinId}</c>.
    /// </summary>
    /// <param name="pin">Item to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CreateAsync(PinnedContextItem pin, CancellationToken ct = default);

    /// <summary>
    /// Returns all pinned items the LLM should consider when the chat session is anchored
    /// to <paramref name="matterId"/>. Per FR-42 / pin-type semantics, this includes items
    /// with <c>PinType = MatterFact</c> whose <see cref="PinnedContextItem.MatterId"/>
    /// equals <paramref name="matterId"/>; <c>SystemRule</c> items that apply broadly are
    /// resolved through <see cref="GetByUserAsync"/> at composition time.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (partition key).</param>
    /// <param name="matterId">Dataverse <c>sprk_matter</c> id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<PinnedContextItem>> GetByMatterAsync(
        string tenantId,
        string matterId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all pinned items owned by <paramref name="userId"/> within the tenant.
    /// Used by task 067 to fold a user's <c>UserPreference</c> + <c>SystemRule</c> pins
    /// into the system-prompt context for every chat session the user starts.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (partition key).</param>
    /// <param name="userId">Owning user identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<PinnedContextItem>> GetByUserAsync(
        string tenantId,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Hard-deletes a pin document. Idempotent: deleting a missing pin returns without
    /// error so the Q7 UI does not crash on a stale-handle race.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (partition key).</param>
    /// <param name="pinId">Stable pin identifier — the <c>{pinId}</c> portion of the
    /// Cosmos document id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(string tenantId, string pinId, CancellationToken ct = default);

    /// <summary>
    /// Returns a single pin by its stable identifier, or <c>null</c> when the pin is not
    /// found in the supplied tenant partition. Used by the Q7 UI endpoint pair (R6 task 070)
    /// to fetch a single pin for ownership validation before update/delete operations.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (partition key).</param>
    /// <param name="pinId">Stable pin identifier (the <c>{pinId}</c> portion of the
    /// Cosmos document id).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PinnedContextItem?> GetByIdAsync(string tenantId, string pinId, CancellationToken ct = default);

    /// <summary>
    /// Replaces an existing pin document with the supplied <paramref name="pin"/>. Caller MUST
    /// have already validated that <paramref name="pin"/>'s <see cref="PinnedContextItem.Id"/>
    /// resolves to an existing document the caller owns; this contract does NOT perform an
    /// ownership check (the calling endpoint does it via <see cref="GetByIdAsync"/> first).
    /// </summary>
    /// <param name="pin">Item to replace.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(PinnedContextItem pin, CancellationToken ct = default);
}
