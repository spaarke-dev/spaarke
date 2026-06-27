namespace Sprk.Bff.Api.Configuration;

public class AiSearchOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string ApiKeySecretName { get; init; } = string.Empty;
    public string KnowledgeIndexName { get; init; } = "spaarke-files-index";

    /// <summary>
    /// Name of the discovery-tier RAG index (1024-token chunks, 2× larger than
    /// <see cref="KnowledgeIndexName"/>'s 512-token chunks) populated by
    /// <see cref="Sprk.Bff.Api.Services.Ai.RagIndexingPipeline"/> in parallel with the
    /// knowledge index. Used for broader-context retrieval scenarios (full-document
    /// semantic search) where knowledge-index's finer-grained chunks would miss
    /// surrounding context.
    /// <para>FR-14 (`spaarke-ai-azure-setup-dev-r1`): renamed from the historical
    /// short name `discovery-index` to the canonical `spaarke-discovery-index` per
    /// the two-tier naming policy (NFR-03 / NFR-10); reactivated as a first-class
    /// catalog index after the 2026-06-25 audit incorrectly marked it retired —
    /// runtime audit confirmed the dual-index code path in
    /// <c>RagIndexingPipeline</c> + <c>RagService.GetIndexHealthAsync</c> +
    /// <c>EnsureKnownIndex</c> is live.</para>
    /// </summary>
    public string DiscoveryIndexName { get; init; } = "spaarke-discovery-index";
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
    /// R5 spec §4.2 / FR-09 — name of the session-scoped Azure AI Search index that holds
    /// chat-session-uploaded file chunks. Documents in this index carry both
    /// <c>tenantId</c> AND <c>sessionId</c> per ADR-014 tenant-isolation invariant; the
    /// <see cref="Sprk.Bff.Api.Services.Ai.RagService"/> routes session-scoped queries here
    /// (when <c>RagSearchOptions.SessionId</c> is set) and applies a
    /// <c>tenantId eq '...' and sessionId eq '...'</c> filter so cross-tenant session leaks
    /// are impossible. Admin-overridable for non-default environments; mirrors the existing
    /// <see cref="KnowledgeIndexName"/> / <see cref="FilesIndexName"/> /
    /// <see cref="RagReferencesIndexName"/> pattern. Default matches the schema deployed by
    /// R5 task 001 (<c>infrastructure/ai-search/spaarke-session-files.json</c>).
    /// </summary>
    public string SessionFilesIndexName { get; init; } = "spaarke-session-files";

    /// <summary>
    /// R5 spec §4.2 / FR-09 — semantic-ranking configuration name for the session-files
    /// index. The session-files index defines its own semantic config (distinct from the
    /// knowledge index's <c>knowledge-semantic-config</c>) so the routing branch in
    /// <see cref="Sprk.Bff.Api.Services.Ai.RagService"/> can apply semantic ranking against
    /// the correct config when <c>RagSearchOptions.SessionId</c> is set. Admin-overridable.
    /// Default matches the schema deployed by R5 task 001.
    /// </summary>
    public string SessionFilesSemanticConfigName { get; init; } = "session-files-semantic-config";

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

    /// <summary>
    /// Multi-container-multi-index-r1 (Phase B, FR-BFF-02 / FR-BFF-06) — allow-list of physical
    /// AI Search index names that callers may target via the per-record / per-request
    /// <c>searchIndexName</c> parameter on <see cref="Sprk.Bff.Api.Services.Ai.IKnowledgeDeploymentService.GetSearchClientAsync"/>.
    /// When a request specifies an <c>indexName</c> NOT in this list, the resolver MUST surface
    /// <c>ProblemDetails 400</c> with stable error code <c>INDEX_NOT_ALLOWED</c> (ADR-019 + NFR-08).
    /// When the request omits <c>indexName</c>, this list is NOT consulted — the resolver falls
    /// through to the existing 2-tier chain (<c>sprk_aiknowledgedeployment</c> Dataverse entity →
    /// <see cref="KnowledgeIndexName"/> appsettings default), preserving NFR-02 backward-compat.
    /// <para>Default value is empty array (no caller-supplied indexes accepted). Operator MUST
    /// populate this list per environment (task 012 adds the canonical baseline values).
    /// Owner clarification: static-appsettings allow-list, not a dynamic config entity (spec.md
    /// §Owner Clarifications row 2 — tighter blast radius, simpler ops).</para>
    /// </summary>
    public string[] AllowedIndexes { get; init; } = Array.Empty<string>();
}
