namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// String constants for playbook capability values.
///
/// Each constant corresponds to a value in the Dataverse <c>sprk_playbookcapabilities</c>
/// multi-select option set on the Playbook entity. Using constants avoids magic strings
/// when filtering tools and actions by capability.
///
/// Dataverse option set integer codes:
///   search          = 100000000
///   analyze         = 100000001
///   write_back      = 100000002
///   reanalyze       = 100000003
///   selection_revise = 100000004
///   web_search      = 100000005
///   summarize       = 100000006
///
/// NOTE: The Dataverse schema update (sprk_capabilities field on sprk_playbook entity)
/// must be deployed via solution import. See task R2-047 for Dataverse-backed lookup.
/// </summary>
public static class PlaybookCapabilities
{
    /// <summary>DocumentSearchTools — semantic search across indexed documents.</summary>
    public const string Search = "search";

    /// <summary>AnalysisQueryTools — retrieve and query analysis results.</summary>
    public const string Analyze = "analyze";

    /// <summary>WorkingDocumentTools — write back edits to the active document.</summary>
    public const string WriteBack = "write_back";

    /// <summary>AnalysisExecutionTools — trigger re-analysis of documents.</summary>
    public const string Reanalyze = "reanalyze";

    /// <summary>TextRefinementTools — refine, rephrase, or improve selected text.</summary>
    public const string SelectionRevise = "selection_revise";

    /// <summary>WebSearchTools — search the web for external information.</summary>
    public const string WebSearch = "web_search";

    /// <summary>KnowledgeRetrievalTools — summarize and retrieve knowledge base content.</summary>
    public const string Summarize = "summarize";

    /// <summary>
    /// All defined capability values. Useful for validation and iteration.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        Search,
        Analyze,
        WriteBack,
        Reanalyze,
        SelectionRevise,
        WebSearch,
        Summarize
    ];
}
