namespace Sprk.Bff.Api.Services.Insights.Graph;

/// <summary>
/// Typed, directional edge between two vertices in the Insight Graph
/// (e.g. <c>(Matter)-[INVOLVED_PARTY {role:"opposing"}]-&gt;(Party)</c>).
/// See design.md §4.2.4 for the canonical edge type catalog.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1 ships POCO + interface (D-P17) only; storage-side adjacency-list
/// emission (incoming/outgoing edge collections embedded in vertex documents)
/// is the first Phase 1.5 deliverable per SPEC §3.3.
/// </para>
/// <para>
/// Edge attribute payload is intentionally a free-form dictionary to keep this
/// abstraction storage-agnostic — Cosmos NoSQL adjacency-list embeds edges in
/// vertex documents (design.md §4.2.3) and Cosmos Gremlin would emit them as
/// first-class edges. Both serialize the same way from the caller's perspective.
/// </para>
/// </remarks>
public sealed record InsightEdge
{
    /// <summary>
    /// Vertex id at the tail (source) of the directional edge.
    /// </summary>
    public required string FromVertexId { get; init; }

    /// <summary>
    /// Edge label — e.g. <c>INVOLVED_PARTY</c>, <c>REPRESENTED</c>, <c>WORKED_ON</c>,
    /// <c>BELONGS_TO</c>, <c>INVOLVED_ISSUE</c>, <c>VENUE</c>. See design.md §4.2.4.
    /// </summary>
    public required string EdgeType { get; init; }

    /// <summary>
    /// Vertex id at the head (target) of the directional edge.
    /// </summary>
    public required string ToVertexId { get; init; }

    /// <summary>
    /// Tenant boundary. Mirrors <see cref="InsightVertex.TenantId"/> for partition
    /// consistency; an edge is always within a single tenant in Phase 1 (per D-52).
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Edge attributes keyed by name (e.g. <c>role: "opposing"</c>,
    /// <c>role: "lead-counsel"</c>). Schema is edge-type-specific.
    /// </summary>
    public IReadOnlyDictionary<string, object?> EdgeProperties { get; init; }
        = new Dictionary<string, object?>();
}
