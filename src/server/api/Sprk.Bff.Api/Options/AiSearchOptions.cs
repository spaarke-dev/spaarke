namespace Sprk.Bff.Api.Configuration;

public class AiSearchOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string ApiKeySecretName { get; init; } = string.Empty;
    public string KnowledgeIndexName { get; init; } = "spaarke-knowledge-index-v2";
    public string DiscoveryIndexName { get; init; } = "discovery-index";
    public string RagReferencesIndexName { get; init; } = "spaarke-rag-references";
    public string SemanticConfigName { get; init; } = "semantic-config";

    /// <summary>
    /// Name of the SPE files index that holds chunked document content. Read by the D-P7
    /// universal ingest source (<c>FilesIndexIngestDocumentSource</c>) to fetch document
    /// text + chunks WITHOUT re-fetching from SPE. Default matches SPEC §4.3 naming
    /// (<c>spaarke-files-index</c>). Admin-overridable via config in case the index name
    /// differs per environment.
    /// </summary>
    public string FilesIndexName { get; init; } = "spaarke-files-index";

    /// <summary>
    /// Name of the SPEC §3.4 derived intelligence index that holds Observations and
    /// Precedents. The D-P7 universal ingest pipeline writes emitted Observations here
    /// via <c>ReferenceIndexingService.IndexIntoAsync</c> + an Observation schema mapper.
    /// Default matches SPEC §3.4 / D-P2 naming (<c>spaarke-insights-index</c>).
    /// </summary>
    public string InsightsIndexName { get; init; } = "spaarke-insights-index";

    /// <summary>
    /// Wave D6 (task 035) — hybrid backward-compat dual-write toggle per design-a6 §4.4 + §5.3.
    /// When true (default), the writer populates <c>scope.matterId</c> for matter-subject
    /// Observations in addition to <c>scope.entityType</c> + <c>scope.entityId</c>, preserving
    /// NFR-08 backward-compat for any consumer that filters by <c>scope/matterId eq '…'</c>.
    /// Set to false to disable dual-write (matter Observations would only carry the canonical
    /// <c>scope.entityType</c>/<c>scope.entityId</c> fields). The reader (IndexRetrieveNode) uses
    /// OR-filter logic per design-a6 §4.5 regardless of this flag, so flipping it does NOT
    /// break reads — it only stops emitting the legacy field on NEW writes.
    /// </summary>
    public bool DualWriteScopeMatterId { get; init; } = true;
}
