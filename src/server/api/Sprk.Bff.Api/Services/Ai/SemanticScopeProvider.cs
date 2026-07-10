using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Default <see cref="ISemanticScopeProvider"/> — WRAPS the existing <see cref="IRagService"/> (Azure AI
/// Search hybrid search + SPE). Task AIR2-061 (FR-B-12). See <see cref="ISemanticScopeProvider"/> for the
/// full ACL-preservation + placement rationale.
/// </summary>
/// <remarks>
/// <b>DI note</b>: this class has no conditional dependencies of its own — it depends only on
/// <see cref="IRagService"/>, which is ALWAYS registered (either the real <c>RagService</c> or the
/// <c>NullRagService</c> P3 Fail-Fast Null-Object, depending on the AI/RAG kill-switch state). Because
/// this provider only ever calls through <see cref="IRagService.SearchAsync(string, RagSearchOptions, CancellationToken)"/>,
/// it automatically inherits whichever behavior is wired underneath — no separate Null-Object peer is
/// needed for this class (CLAUDE.md §11 default-to-reuse: wrap, don't duplicate the Null-Object pattern).
/// </remarks>
public sealed class SemanticScopeProvider : ISemanticScopeProvider
{
    private const string ProvenanceClass = "semantic_retrieval";

    private readonly IRagService _ragService;
    private readonly ILogger<SemanticScopeProvider> _logger;

    public SemanticScopeProvider(IRagService ragService, ILogger<SemanticScopeProvider> logger)
    {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SemanticScopeResult> GetSemanticContextAsync(
        SemanticScopeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.TenantId);
        ArgumentException.ThrowIfNullOrEmpty(request.Query);

        // WRAP, not fork (AIPU2-027): the ONLY way this provider reaches Azure AI Search is through
        // the existing IRagService.SearchAsync overload, which unconditionally applies the
        // PrivilegeFilterBuilder ACL inside RagService.BuildSearchOptions. This provider never
        // constructs a SearchClient/SearchOptions itself and never calls Azure.Search.Documents
        // directly — there is no path here that could bypass the filter. CallerPrincipal is forwarded
        // UNCHANGED: null stays null (RagService treats that as public-only, fail-closed), never
        // substituted or elevated.
        var options = new RagSearchOptions
        {
            TenantId = request.TenantId,
            TopK = request.TopK,
            CallerPrincipal = request.CallerPrincipal,
            ParentEntityType = request.ParentEntityType,
            ParentEntityId = request.ParentEntityId,
        };

        var response = await _ragService
            .SearchAsync(request.Query, options, cancellationToken)
            .ConfigureAwait(false);

        var references = response.Results
            .Select(r => new RetrievalReference
            {
                DocumentRef = r.DocumentId ?? r.Id,
                IndexId = r.KnowledgeSourceName,
                ProvenanceClass = ProvenanceClass,
            })
            .ToList();

        _logger.LogDebug(
            "SemanticScopeProvider resolved {Count} retrieval reference(s) for tenant {TenantId} " +
            "(privilege-filtered via IRagService; semantic source is Azure AI Search + SPE, not Dataverse).",
            references.Count, request.TenantId);

        return new SemanticScopeResult { Retrieval = references };
    }
}
