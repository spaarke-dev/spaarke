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
///   verify_citations  = 100000009
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
    /// VerifyCitationsTool — explicit LLM-invokable citation verification against authoritative
    /// legal databases. Exposed as the "verify_citations" AI function. Gated so only playbooks
    /// that deal with legal documents include this tool in the LLM's tool schema.
    /// The automatic post-LLM citation check (CitationSafetyCheck) runs unconditionally
    /// regardless of this capability.
    /// Dataverse option set integer code: 100000009.
    /// </summary>
    public const string VerifyCitations = "verify_citations";

    /// <summary>
    /// InvokeInsightsQueryTool — entity-scoped analytical questions (matter/project/invoice)
    /// answered via the Insights Engine Assistant endpoint
    /// (<c>POST /api/insights/assistant/query</c>). Exposed as the <c>insights.query</c> AI
    /// function. R5 task 024 / D2-14 — R5 is a Zone B HTTP consumer of the Insights contract
    /// per refined ADR-013 §3.5; the tool never injects Insights internals.
    ///
    /// <para>
    /// Dataverse option set integer code: PROVISIONAL (not yet assigned by the Insights team's
    /// capability-manifest backfill — Insights r3 work item F-4). For now this string constant
    /// is added to the local <see cref="CoreCapabilities"/> set so the tool is discoverable in
    /// standalone chat mode; when the Dataverse capability row lands, this constant will map
    /// to that row's <c>sprk_playbookcapabilities</c> integer code and the manifest's
    /// <c>ToolNames</c> allow-list will gate Layer-2 routing per AIPU2-061.
    /// </para>
    /// </summary>
    public const string InsightsQuery = "insights_query";

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
        CodeInterpreter,
        VerifyCitations,
        InsightsQuery
    ];

    /// <summary>
    /// Core capabilities available in standalone/generic chat mode (no playbook).
    /// Includes only tools that have no external dependency configuration requirements.
    /// Excludes LegalResearch (needs Bing Grounding), CodeInterpreter (needs Foundry agent),
    /// WebSearch (needs Bing API key), WriteBack (needs analysis context),
    /// and Reanalyze (needs analysis context).
    ///
    /// <para>
    /// R5 D2-14 (task 024) — <see cref="InsightsQuery"/> is included so the
    /// <c>insights.query</c> tool is discoverable in standalone chat mode. The Insights
    /// endpoint enforces its own kill-switches (returns 503 for
    /// <c>ai.insights.disabled</c> / <c>ai.rag.disabled</c> /
    /// <c>ai.intent-classification.disabled</c>) — surfaced to the renderer per the
    /// contract v1.0 §5.1 error matrix.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> CoreCapabilities =
    [
        Search,
        Analyze,
        SelectionRevise,
        Summarize,
        InsightsQuery
    ];
}
