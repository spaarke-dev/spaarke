namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// String constants for playbook capability values.
///
/// Each constant corresponds to a value in the Dataverse <c>sprk_playbookcapabilities</c>
/// global multi-select choice on the <c>sprk_analysisplaybook</c> entity. Using constants
/// avoids magic strings when filtering tools and actions by capability.
///
/// Dataverse option set integer codes:
///   search            = 100000000
///   analyze           = 100000001
///   write_back        = 100000002
///   reanalyze         = 100000003
///   selection_revise  = 100000004
///   web_search        = 100000005
///   summarize         = 100000006
///   legal_research    = 100000007
///   code_interpreter  = 100000008
///
/// The <see cref="PlaybookService"/> reads this field via OData Web API and maps the
/// integer option values to these string constants via <c>ParseCapabilities</c>.
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
    /// LegalResearchTools — Bing Grounding-backed legal topic research and case citation lookup.
    /// Only available when the playbook explicitly enables legal research to ensure legal queries
    /// are reviewed through appropriate data-governance controls (ADR-015).
    /// Dataverse option: 100000007.
    /// </summary>
    public const string LegalResearch = "legal_research";

    /// <summary>
    /// CodeInterpreterTools — run data analysis and chart generation via Azure AI Foundry
    /// Code Interpreter sandbox. Gated here so only playbooks that explicitly declare this
    /// capability can execute sandbox code (ADR-015: data governance; ADR-018: kill switch).
    /// Dataverse option set integer code: 100000008.
    /// </summary>
    public const string CodeInterpreter = "code_interpreter";

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
        Summarize,
        LegalResearch,
        CodeInterpreter
    ];

    /// <summary>
    /// Core capabilities available in standalone/generic chat mode (no playbook).
    /// Includes only tools that have no external dependency configuration requirements.
    /// Excludes LegalResearch (needs Bing Grounding), CodeInterpreter (needs Foundry agent),
    /// WebSearch (needs Bing API key), WriteBack (needs analysis context),
    /// and Reanalyze (needs analysis context).
    /// </summary>
    public static readonly IReadOnlyList<string> CoreCapabilities =
    [
        Search,
        Analyze,
        SelectionRevise,
        Summarize
    ];
}
