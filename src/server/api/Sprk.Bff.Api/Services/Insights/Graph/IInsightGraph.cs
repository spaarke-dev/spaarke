namespace Sprk.Bff.Api.Services.Insights.Graph;

/// <summary>
/// Read/write abstraction over the Insight Graph — typed in terms of named
/// traversal patterns, deliberately storage-agnostic. See design.md §4.2.2.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1 surface (D-P17)</b>: interface + DTOs + <see cref="StubInsightGraph"/>
/// stub only. Concrete Cosmos NoSQL adjacency-list implementation
/// (<c>CosmosNoSqlInsightGraph</c>) is the first Phase 1.5 deliverable per
/// SPEC §3.3 — shipping the interface in Phase 1 preserves the swap path at
/// trivial cost and lets synthesis playbook authors reason against the
/// abstraction now.
/// </para>
/// <para>
/// <b>Named traversal discipline (D-09)</b>: every traversal must be a named
/// pattern method (<see cref="FindMattersInvolvingPartyAsync"/>,
/// <see cref="FindConnectedEntitiesAsync"/>) — the interface MUST NOT leak
/// Gremlin or Cosmos SQL fragments. This is what keeps the
/// NoSQL ↔ Gremlin implementation swap a contained refactor
/// (design.md §4.2 risk discussion).
/// </para>
/// <para>
/// <b>Boundary placement</b>: this lives in Zone B per SPEC §3.5 deliverable
/// placement table (<c>Services/Insights/Graph/</c>) — it is a domain
/// abstraction, not an AI internal. Zone A code (<c>Services/Ai/</c>) may
/// consume it via the future <c>GraphTraverseNode</c> (D-P12 extension, Phase 1.5).
/// </para>
/// </remarks>
public interface IInsightGraph
{
    // ─────────────────────────────────────────────────────────────────────
    // Vertex operations
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Insert-or-update a vertex by <see cref="InsightVertex.Id"/>.
    /// Returns the persisted vertex (server may stamp metadata).
    /// </summary>
    Task<InsightVertex> UpsertVertexAsync(InsightVertex vertex, CancellationToken ct);

    /// <summary>
    /// Fetch a single vertex by id. Returns <c>null</c> when not found.
    /// </summary>
    Task<InsightVertex?> GetVertexAsync(string vertexId, CancellationToken ct);

    /// <summary>
    /// Delete a vertex by id. Implementations SHOULD also remove its incident
    /// edges (Cosmos NoSQL adjacency-list embeds them; Gremlin would cascade).
    /// </summary>
    Task DeleteVertexAsync(string vertexId, CancellationToken ct);

    // ─────────────────────────────────────────────────────────────────────
    // Edge operations
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Insert-or-update an edge. Implementations SHOULD maintain the symmetric
    /// incoming-edge collection on the target vertex (per design.md §4.2.3
    /// adjacency-list shape) so reverse lookups are O(1).
    /// </summary>
    Task UpsertEdgeAsync(InsightEdge edge, CancellationToken ct);

    /// <summary>
    /// Remove an edge identified by its <c>(source, type, target)</c> triple.
    /// </summary>
    Task DeleteEdgeAsync(
        string fromVertexId,
        string edgeType,
        string toVertexId,
        CancellationToken ct);

    // ─────────────────────────────────────────────────────────────────────
    // Named traversals (NO Gremlin/SQL leaks per D-09)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks outward from each seed vertex in <see cref="GraphTraversalSpec.SeedVertexIds"/>
    /// for up to <see cref="GraphTraversalSpec.MaxHops"/> hops, filtering by
    /// <see cref="GraphTraversalSpec.EdgeTypeFilter"/> and
    /// <see cref="GraphTraversalSpec.TargetVertexTypeFilter"/>.
    /// Returns the unique connected vertices (excluding seeds).
    /// </summary>
    Task<IReadOnlyList<InsightVertex>> FindConnectedEntitiesAsync(
        GraphTraversalSpec spec,
        CancellationToken ct);

    /// <summary>
    /// Returns matters where the given party participates in any role within
    /// <paramref name="scope"/>. Canonical example of a typed named traversal —
    /// callers do not see edge types or hop counts; the implementation chooses
    /// the optimal path (e.g. follow <c>INVOLVED_PARTY</c> incoming edges on
    /// the party vertex).
    /// </summary>
    Task<IReadOnlyList<MatterRef>> FindMattersInvolvingPartyAsync(
        string partyId,
        GraphTraversalScope scope,
        CancellationToken ct);
}
