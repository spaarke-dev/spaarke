namespace Sprk.Bff.Api.Services.Insights.Graph;

/// <summary>
/// Vertex (node) in the Insight Graph — represents a typed domain entity
/// (Matter, Party, Person, Firm, Document, Issue, Jurisdiction, Outcome, Playbook).
/// See design.md §4.2.2 for the abstraction and §4.2.4 for the schema.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1 ships POCO + interface (D-P17) only; Cosmos NoSQL adjacency-list
/// implementation is the first Phase 1.5 deliverable per SPEC §3.3.
/// </para>
/// <para>
/// Designed deliberately to NOT leak Gremlin or Cosmos-specific syntax — consumers
/// stay typed in terms of named traversal patterns on <see cref="IInsightGraph"/>.
/// This preserves the swap path between Cosmos NoSQL and Cosmos Gremlin implementations
/// (per design.md §4.2 decision rationale; constraint D-09).
/// </para>
/// </remarks>
public sealed record InsightVertex
{
    /// <summary>
    /// Stable identifier — typed namespace + scoped id (e.g. <c>"matter:M-1234"</c>, <c>"party:acme"</c>).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Tenant boundary. Per D-52, used as the Cosmos partition key in Phase 1.5
    /// to support per-tenant cost attribution and physical isolation.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Vertex kind — one of: <c>Matter</c>, <c>Party</c>, <c>Person</c>, <c>Firm</c>,
    /// <c>Document</c>, <c>Issue</c>, <c>Claim</c>, <c>Jurisdiction</c>,
    /// <c>Outcome</c>, <c>Playbook</c>. See design.md §4.2.4.
    /// </summary>
    public required string VertexType { get; init; }

    /// <summary>
    /// Free-form key/value bag of vertex attributes (e.g. <c>displayName</c>,
    /// <c>practiceArea</c>, <c>jurisdiction</c>, <c>openedDate</c>, <c>status</c>).
    /// Schema is vertex-type-specific and intentionally not statically typed here
    /// — keeps the abstraction storage-agnostic.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Properties { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>
    /// References to associated <c>InsightArtifact</c>s (Observations, Precedents)
    /// keyed by URI per design.md §4.2.3 example (e.g.
    /// <c>"insight-matter://M-1234#closure-summary"</c>). Allows surfaces to drill
    /// from a graph vertex to its evidentiary artifacts.
    /// </summary>
    public IReadOnlyList<string> ArtifactRefs { get; init; } = Array.Empty<string>();
}
