namespace Sprk.Bff.Api.Services.Insights.Graph;

/// <summary>
/// Inputs to <see cref="IInsightGraph.FindConnectedEntitiesAsync"/> — describes a
/// bounded outward walk from one or more seed vertices.
/// </summary>
/// <remarks>
/// Deliberately abstract over the underlying graph engine — no Gremlin step syntax,
/// no Cosmos SQL fragments (constraint D-09). Implementations translate this
/// description into their native traversal idiom.
/// </remarks>
public sealed record GraphTraversalSpec
{
    /// <summary>
    /// Vertices to start walking from. At least one is required.
    /// </summary>
    public required IReadOnlyList<string> SeedVertexIds { get; init; }

    /// <summary>
    /// Tenant boundary for the traversal. All visited vertices must share this tenant.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Maximum hop distance from any seed (1 = immediate neighbors).
    /// Implementations SHOULD reject values that would make the traversal
    /// unbounded in practice (Phase 1.5 will define a hard cap based on
    /// Cosmos RU budget observation).
    /// </summary>
    public int MaxHops { get; init; } = 1;

    /// <summary>
    /// If non-empty, only edges whose <see cref="InsightEdge.EdgeType"/> appears
    /// in this set are followed (e.g. <c>["INVOLVED_PARTY", "WORKED_ON"]</c>).
    /// Empty means "any edge type".
    /// </summary>
    public IReadOnlyList<string> EdgeTypeFilter { get; init; } = Array.Empty<string>();

    /// <summary>
    /// If non-empty, only vertices whose <see cref="InsightVertex.VertexType"/>
    /// appears in this set are returned (e.g. <c>["Matter", "Party"]</c>).
    /// Empty means "any vertex type".
    /// </summary>
    public IReadOnlyList<string> TargetVertexTypeFilter { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Scope filters for <see cref="IInsightGraph.FindMattersInvolvingPartyAsync"/>.
/// Lets callers narrow the matter cohort without rewriting traversal syntax.
/// </summary>
public sealed record GraphTraversalScope
{
    /// <summary>
    /// Tenant boundary. Required so the traversal stays within a single tenant
    /// (per D-52 single-tenant Phase 1 + future federation safety).
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Inclusive lower bound on matter opening date. <c>null</c> = no lower bound.
    /// </summary>
    public DateTimeOffset? OpenedOnOrAfter { get; init; }

    /// <summary>
    /// Inclusive upper bound on matter opening date. <c>null</c> = no upper bound.
    /// </summary>
    public DateTimeOffset? OpenedOnOrBefore { get; init; }

    /// <summary>
    /// If non-empty, restricts results to matters whose practice area is in this set
    /// (e.g. <c>["ip-licensing", "commercial-litigation"]</c>).
    /// </summary>
    public IReadOnlyList<string> PracticeAreaFilter { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Maximum matters returned. Implementations MUST cap at a sane upper bound
    /// (Phase 1.5 will define based on Cosmos RU budget).
    /// </summary>
    public int? MaxResults { get; init; }
}

/// <summary>
/// Lightweight matter reference returned by graph traversals. Keeps the
/// abstraction lean — callers that need full matter detail re-fetch from Dataverse
/// or <c>spaarke-insights-index</c> via the matter id.
/// </summary>
public sealed record MatterRef
{
    /// <summary>
    /// Stable matter id (e.g. <c>"M-2024-0341"</c>). Equivalent to the vertex id
    /// minus the <c>"matter:"</c> prefix.
    /// </summary>
    public required string MatterId { get; init; }

    /// <summary>
    /// Tenant boundary. All returned matter refs share the traversal's tenant.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Optional display name copied from the matter vertex's properties for
    /// convenience (e.g. logging, audit). NOT authoritative — fetch full data
    /// from Dataverse when displaying to users.
    /// </summary>
    public string? DisplayName { get; init; }
}
