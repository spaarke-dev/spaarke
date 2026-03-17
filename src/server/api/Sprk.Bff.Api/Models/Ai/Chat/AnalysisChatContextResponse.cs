namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Resolved context for an analysis-scoped SprkChat session.
///
/// Returned by <c>GET /api/ai/chat/context-mappings/analysis/{analysisId}</c> and
/// consumed by the AnalysisWorkspace Code Page to populate QuickActionChips,
/// SlashCommandMenu, and the initial SprkChat playbook selection.
///
/// The <see cref="InlineActions"/> list is derived from the playbook's
/// <c>sprk_playbookcapabilities</c> multi-select option set via the static
/// capability-to-action mapping in <c>AnalysisChatContextResolver</c>.
/// </summary>
/// <param name="DefaultPlaybookId">Dataverse GUID string of the default playbook, or empty when unresolved.</param>
/// <param name="DefaultPlaybookName">Display name of the default playbook.</param>
/// <param name="AvailablePlaybooks">All playbooks available for this analysis context, ordered by sort order.</param>
/// <param name="InlineActions">
/// Inline AI actions derived from the playbook's capabilities. Used to render
/// QuickActionChips and slash command entries in SprkChat.
/// </param>
/// <param name="KnowledgeSources">Knowledge sources scoped to this analysis context.</param>
/// <param name="AnalysisContext">Contextual metadata about the analysis record (type, matter, practice area).</param>
public record AnalysisChatContextResponse(
    string DefaultPlaybookId,
    string DefaultPlaybookName,
    List<AnalysisPlaybookInfo> AvailablePlaybooks,
    List<InlineActionInfo> InlineActions,
    List<AnalysisKnowledgeSourceInfo> KnowledgeSources,
    AnalysisContextInfo AnalysisContext);

/// <summary>
/// Lightweight playbook descriptor in the analysis context response.
/// Parallels <see cref="ChatPlaybookInfo"/> but uses string IDs to avoid
/// Guid serialisation assumptions in the client.
/// </summary>
/// <param name="Id">Dataverse GUID string of the <c>sprk_analysisplaybook</c> record.</param>
/// <param name="Name">Display name (<c>sprk_name</c>) shown in the playbook selector.</param>
/// <param name="Description">Optional description for tooltip / help text.</param>
public record AnalysisPlaybookInfo(string Id, string Name, string? Description);

/// <summary>
/// Describes a single inline AI action surfaced in the AnalysisWorkspace toolbar
/// and SprkChat slash command menu.
///
/// The <see cref="ActionType"/> field determines how SprkChat handles the result:
/// <list type="bullet">
///   <item><c>"chat"</c> — streams a normal chat response into the message list.</item>
///   <item><c>"diff"</c> — triggers a diff preview via the existing <c>DiffReviewPanel</c>
///     (applies to <c>selection_revise</c> — capability value 100000004).</item>
/// </list>
///
/// Derived from <c>sprk_playbookcapabilities</c> integers via the static
/// <c>CapabilityToActionMap</c> dictionary in <c>AnalysisChatContextResolver</c>.
/// </summary>
/// <param name="Id">Capability string key (e.g. "search", "selection_revise"). Matches <see cref="PlaybookCapabilities"/> constants.</param>
/// <param name="Label">Human-readable label for the chip / menu item (e.g. "Search", "Revise Selection").</param>
/// <param name="ActionType">How SprkChat handles the result: <c>"chat"</c> or <c>"diff"</c>.</param>
/// <param name="Description">Optional tooltip / description text.</param>
public record InlineActionInfo(
    string Id,
    string Label,
    string ActionType,
    string? Description = null);

/// <summary>
/// A knowledge source scoped to the analysis context.
///
/// The <see cref="Type"/> field identifies the source category (e.g. <c>"rag_index"</c>,
/// <c>"inline"</c>, <c>"reference"</c>) so the client and BFF can route queries correctly.
/// </summary>
/// <param name="Type">Source category string (e.g. "rag_index", "inline", "reference").</param>
/// <param name="Id">Dataverse GUID string of the knowledge source record.</param>
/// <param name="Label">Optional display label for the knowledge source.</param>
public record AnalysisKnowledgeSourceInfo(string Type, string Id, string? Label = null);

/// <summary>
/// Contextual metadata about the analysis record resolved from Dataverse.
///
/// Fields are nullable because some may not be populated until the analysis record
/// is fully processed. The <see cref="AnalysisId"/> is always present (it is the
/// resolver input).
/// </summary>
/// <param name="AnalysisId">The analysis record identifier used to resolve this context.</param>
/// <param name="AnalysisType">Analysis type (e.g. "contract_review", "matter_summary") from the analysis record.</param>
/// <param name="MatterType">Matter type from the related matter record (e.g. "litigation", "transactional").</param>
/// <param name="PracticeArea">Practice area from the related matter record (e.g. "corporate", "real_estate").</param>
/// <param name="SourceFileId">Dataverse GUID string of the source document (<c>sprk_spefileid</c>) if present.</param>
/// <param name="SourceContainerId">SPE container ID of the source document's container if present.</param>
public record AnalysisContextInfo(
    string AnalysisId,
    string? AnalysisType,
    string? MatterType,
    string? PracticeArea,
    string? SourceFileId,
    string? SourceContainerId);
