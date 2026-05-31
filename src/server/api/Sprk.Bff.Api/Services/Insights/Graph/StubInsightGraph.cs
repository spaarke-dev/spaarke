namespace Sprk.Bff.Api.Services.Insights.Graph;

/// <summary>
/// Phase 1 placeholder implementation of <see cref="IInsightGraph"/> — every method
/// throws <see cref="NotImplementedException"/> with a clear "Phase 1.5" message.
/// </summary>
/// <remarks>
/// <para>
/// The Cosmos NoSQL adjacency-list implementation (<c>CosmosNoSqlInsightGraph</c>)
/// is the <b>first Phase 1.5 deliverable</b> per SPEC §3.3. This stub ships now
/// so consumers can reason against the abstraction (D-P17 swap-path preservation)
/// without depending on Cosmos infrastructure that isn't yet provisioned.
/// </para>
/// <para>
/// Behavior is deliberately fail-fast: any call site that accidentally exercises
/// graph traversal in Phase 1 — including misrouted synthesis playbooks or test
/// fixtures — will fail loudly with a message pointing at the deferral decision.
/// This prevents silent "works in dev because the graph is empty" failure modes.
/// </para>
/// <para>
/// Registered in DI by <c>InsightsModule.AddInsightsModule</c>. Replace this
/// registration when Phase 1.5 lands <c>CosmosNoSqlInsightGraph</c>.
/// </para>
/// </remarks>
internal sealed class StubInsightGraph : IInsightGraph
{
    private const string DeferralMessage =
        "Cosmos NoSqlInsightGraph deferred to Phase 1.5 first deliverable per SPEC §3.3. " +
        "The Phase 1 stub exists only to preserve the swap path (D-P17) and to let " +
        "synthesis playbook authors reason against IInsightGraph without depending on " +
        "Cosmos infrastructure that has not yet been provisioned.";

    public Task<InsightVertex> UpsertVertexAsync(InsightVertex vertex, CancellationToken ct)
        => throw new NotImplementedException(DeferralMessage);

    public Task<InsightVertex?> GetVertexAsync(string vertexId, CancellationToken ct)
        => throw new NotImplementedException(DeferralMessage);

    public Task DeleteVertexAsync(string vertexId, CancellationToken ct)
        => throw new NotImplementedException(DeferralMessage);

    public Task UpsertEdgeAsync(InsightEdge edge, CancellationToken ct)
        => throw new NotImplementedException(DeferralMessage);

    public Task DeleteEdgeAsync(
        string fromVertexId,
        string edgeType,
        string toVertexId,
        CancellationToken ct)
        => throw new NotImplementedException(DeferralMessage);

    public Task<IReadOnlyList<InsightVertex>> FindConnectedEntitiesAsync(
        GraphTraversalSpec spec,
        CancellationToken ct)
        => throw new NotImplementedException(DeferralMessage);

    public Task<IReadOnlyList<MatterRef>> FindMattersInvolvingPartyAsync(
        string partyId,
        GraphTraversalScope scope,
        CancellationToken ct)
        => throw new NotImplementedException(DeferralMessage);
}
