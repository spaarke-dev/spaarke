using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Constants for the "Spaarke AI General" default playbook.
///
/// This playbook is the system-wide baseline that the SpaarkeAi control loads automatically
/// when no context playbook is supplied by the host form (standalone mode). It provides the
/// six core AI tools that are always available regardless of matter or document context.
///
/// The corresponding Dataverse record is seeded by <c>scripts/Create-DefaultPlaybook.ps1</c>
/// and is identified by the alternate key <see cref="PlaybookCode"/> (<c>sprk_playbookcode</c>).
///
/// <para>
/// Usage in <see cref="StandaloneChatContextProvider"/> and <see cref="SprkChatAgentFactory"/>:
/// when the chat session has no playbook ID bound to a host form, the agent factory resolves
/// this playbook by name via <c>IPlaybookService.GetByNameAsync</c> and activates only the
/// capabilities listed in <see cref="DefaultCapabilities"/>.
/// </para>
///
/// <para>
/// Capabilities map to <see cref="PlaybookCapabilities"/> string constants, which in turn map
/// to Dataverse option values on <c>sprk_playbookcapabilities</c>:
///   search           = 100000000
///   analyze          = 100000001
///   selection_revise = 100000004
///   summarize        = 100000006
/// </para>
/// </summary>
public static class DefaultPlaybookConstants
{
    /// <summary>
    /// Exact display name of the default playbook record in Dataverse.
    /// Used by <c>IPlaybookService.GetByNameAsync</c> for name-based lookup.
    /// Must match the <c>sprk_name</c> field on the seed record.
    /// </summary>
    public const string DefaultPlaybookName = "Spaarke AI General";

    /// <summary>
    /// Portable alternate key (<c>sprk_playbookcode</c> — admin-facing descriptive slug) for the
    /// default playbook. Survives solution imports across environments.
    /// </summary>
    /// <remarks>
    /// Per Q&amp;A 2026-06-22 Q1 this code is admin-facing only and is NOT used for runtime lookup.
    /// The runtime lookup column is <c>sprk_playbookid</c> (see
    /// <c>IPlaybookLookupService.GetByIdAsync</c>). This constant is retained for documentation /
    /// seed-script use; consumers needing the playbook should resolve via
    /// <c>IPlaybookService.GetByNameAsync</c> with <see cref="DefaultPlaybookName"/>.
    /// </remarks>
    public const string PlaybookCode = "PB-DEFAULT-GENERAL";

    /// <summary>
    /// Ordered list of AI tool names enabled for the default standalone playbook.
    ///
    /// These correspond to the handler names registered in the AI tool pipeline
    /// (<c>IAnalysisToolHandler</c> implementations) and to the capability routing
    /// logic in <c>SprkChatAgentFactory</c>. The order is informational — tool
    /// availability is determined by the capability flags on the playbook record,
    /// not this array index.
    ///
    /// Tool → Capability mapping:
    ///   SearchDocuments  → <see cref="PlaybookCapabilities.Search"/>
    ///   SearchDiscovery  → <see cref="PlaybookCapabilities.Search"/>
    ///   GetKnowledgeSource → <see cref="PlaybookCapabilities.Summarize"/>
    ///   RefineText       → <see cref="PlaybookCapabilities.SelectionRevise"/>
    ///   GenerateSummary  → <see cref="PlaybookCapabilities.Summarize"/>
    ///   QueryEntities    → <see cref="PlaybookCapabilities.Analyze"/>
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultPlaybookTools =
    [
        "SearchDocuments",
        "SearchDiscovery",
        "GetKnowledgeSource",
        "RefineText",
        "GenerateSummary",
        "QueryEntities",
    ];

    /// <summary>
    /// <see cref="PlaybookCapabilities"/> strings that are active for the default playbook.
    ///
    /// These mirror the <c>sprk_playbookcapabilities</c> option values set on the seed record
    /// by <c>scripts/Create-DefaultPlaybook.ps1</c>:
    ///   100000000 = search          (SearchDocuments, SearchDiscovery)
    ///   100000001 = analyze         (QueryEntities, GetKnowledgeSource)
    ///   100000004 = selection_revise (RefineText)
    ///   100000006 = summarize        (GenerateSummary, GetKnowledgeSource)
    ///
    /// Excluded capabilities (and why):
    ///   write_back     — requires an active analysis output record
    ///   reanalyze      — requires an active analysis output record
    ///   web_search     — requires Bing Grounding API key configuration
    ///   legal_research — gated by ADR-015 data governance; requires explicit playbook
    ///   code_interpreter — requires Azure AI Foundry sandbox; requires explicit playbook
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultCapabilities =
    [
        PlaybookCapabilities.Search,
        PlaybookCapabilities.Analyze,
        PlaybookCapabilities.SelectionRevise,
        PlaybookCapabilities.Summarize,
    ];
}
