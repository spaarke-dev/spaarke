using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// The single point that names the AI Search index every indexing path writes and stamps: the content type's index
/// (<see cref="AiSearchOptions.KnowledgeIndexName"/>, the files index for documents).
/// </summary>
/// <remarks>
/// <para>
/// field-mapping-server-write-path-r1 task 041 (FR-40, #2502): "the search index follows the content type, never the
/// user". The per-record chain this type used to run (document → parent → the parent's business unit, over the
/// <c>sprk_ai_search_index</c> lookup and the legacy <c>sprk_searchindexname</c> text column) is gone: no indexing path
/// reads a record's or a business unit's index columns, so a value a user or a unit holds can never route a file.
/// Search-time selection (a request-named index checked against the allow-list) is unchanged and does not pass through
/// here.
/// </para>
/// </remarks>
public interface ISearchIndexNameResolver
{
    /// <summary>
    /// Returns the single canonical AI Search index name used for BOTH reads and writes.
    /// This is the consolidation point for FAILURE-MODES G-9: the read path
    /// (<c>RagService</c>) and every write/stamp path (indexing job handler, RAG endpoints,
    /// <c>KnowledgeDeploymentService</c> default deployment) resolve the default from this ONE
    /// setting (<see cref="AiSearchOptions.KnowledgeIndexName"/>), so read and write can no longer
    /// silently target different indexes. The deprecated <c>Analysis:SharedIndexName</c> is no
    /// longer consulted by the write path (FR-26).
    /// </summary>
    string GetDefaultIndexName();
}

/// <inheritdoc cref="ISearchIndexNameResolver"/>
public sealed class SearchIndexNameResolver : ISearchIndexNameResolver
{
    // Source-entity columns. Internal (field-mapping-server-write-path-r1 tasks 015 and 041): the unified create drops a
    // client's value for these columns (RecordTableRules), by these names; nothing on the server writes them.
    internal const string LookupColumn = "sprk_ai_search_index";              // lookup on source entities (Matter/Project/Doc/...)
    internal const string LegacyTextColumn = "sprk_searchindexname";          // deprecated text column on source entities

    private readonly AiSearchOptions _aiSearchOptions;

    public SearchIndexNameResolver(IOptions<AiSearchOptions> aiSearchOptions)
    {
        _aiSearchOptions = aiSearchOptions?.Value ?? throw new ArgumentNullException(nameof(aiSearchOptions));
    }

    /// <inheritdoc />
    public string GetDefaultIndexName() => _aiSearchOptions.KnowledgeIndexName;
}
