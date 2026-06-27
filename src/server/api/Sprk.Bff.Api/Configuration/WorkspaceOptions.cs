using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for Legal Operations Workspace AI features (Matter / Project pre-fill,
/// AI summary playbooks).
/// Bound to the "Workspace" configuration section.
/// </summary>
/// <remarks>
/// Per ADR-010 / ADR-018, BFF code reads configuration via typed <see cref="IOptions{TOptions}"/>
/// rather than raw <see cref="IConfiguration"/> indexer reads. This centralizes the keys,
/// applies data-annotation validation at startup (when wired with <c>ValidateOnStart()</c>),
/// and prevents typos from silently degrading behaviour at runtime.
/// <para>
/// Q&amp;A 2026-06-22 Q1 decision: playbook-lookup options carry the IMMUTABLE
/// <c>sprk_playbookid</c> opaque GUID value (the row's <c>sprk_analysisplaybookid</c> PK,
/// mirrored into the stable-ID alternate key column). The descriptive
/// <c>sprk_playbookcode</c> slug (e.g., <c>PB-002</c>, <c>PB-008</c>) is admin-facing only and
/// is NOT consumed by BFF code. See <c>IPlaybookLookupService.GetByIdAsync</c>.
/// </para>
/// </remarks>
public class WorkspaceOptions
{
    /// <summary>
    /// Configuration section name. Used by <c>configuration.GetSection(WorkspaceOptions.SectionName)</c>.
    /// </summary>
    public const string SectionName = "Workspace";

    /// <summary>
    /// Default playbook ID for the "Create New Matter Pre-Fill" playbook (Extract Matter Fields, gpt-4o).
    /// Used by <see cref="Sprk.Bff.Api.Services.Workspace.MatterPreFillService"/>.
    /// When unset, the service falls back to the hardcoded default
    /// <c>2d660cad-d418-f111-8343-7ced8d1dc988</c>.
    /// </summary>
    public string? PreFillPlaybookId { get; set; }

    /// <summary>
    /// Default playbook ID for the "Create New Project Pre-Fill" playbook (Extract Project Fields, gpt-4o).
    /// Used by <see cref="Sprk.Bff.Api.Services.Workspace.ProjectPreFillService"/>.
    /// When unset, the service falls back to a hardcoded default — see
    /// <see cref="Sprk.Bff.Api.Services.Workspace.ProjectPreFillService"/> DefaultPreFillPlaybookId
    /// (Pattern A stable-ID resolution planned; see ADR-018).
    /// </summary>
    public string? ProjectPreFillPlaybookId { get; set; }

    /// <summary>
    /// Default playbook ID for the AI summary playbook used by
    /// <see cref="Sprk.Bff.Api.Services.Workspace.WorkspaceAiService"/>.
    /// When unset, the service falls back to the hardcoded default
    /// <c>18cf3cc8-02ec-f011-8406-7c1e520aa4df</c>.
    /// </summary>
    public string? AiSummaryPlaybookId { get; set; }

    /// <summary>
    /// Canonical stable-ID lookup value for the "Summarize New File(s)" playbook used by
    /// <see cref="Sprk.Bff.Api.Api.Workspace.WorkspaceFileEndpoints"/> (POST
    /// <c>/api/workspace/files/summarize</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bound from <c>Workspace:SummarizePlaybookId</c> config key. GUID-format opaque ID per
    /// Q&amp;A 2026-06-22 Q1 — value mirrors the row's <c>sprk_analysisplaybookid</c> PK and
    /// is looked up at runtime via the <c>sprk_playbookid</c> alternate key on
    /// <c>sprk_analysisplaybook</c> (see <c>IPlaybookLookupService.GetByIdAsync</c>).
    /// </para>
    /// <para>
    /// Replaces the prior raw <c>IConfiguration["Workspace:SummarizePlaybookId"]</c> indexer
    /// read at <c>WorkspaceFileEndpoints.cs:30,254</c> (ADR-018 violation fix — see
    /// chat-routing-redesign-r1 task 012 / spec FR-04).
    /// When unset, the endpoint falls back to the hardcoded default
    /// <c>4a72f99c-a119-f111-8343-7ced8d1dc988</c>.
    /// </para>
    /// </remarks>
    public string? SummarizePlaybookId { get; set; }

    /// <summary>
    /// Stable-ID lookup value for the chat "summarize document" path (spec FR-05).
    /// </summary>
    /// <remarks>
    /// Bound from <c>Workspace:ChatSummarizePlaybookId</c> config key. GUID-format opaque ID
    /// per Q&amp;A 2026-06-22 Q1 (value mirrors the row's <c>sprk_analysisplaybookid</c> PK).
    /// Pre-seated by chat-routing-redesign-r1 task 013 (CRIT-1 race-condition fix)
    /// so wave 1-E consumer migrations (task 016) can land in parallel without
    /// colliding on <see cref="WorkspaceOptions"/>. Resolved at runtime via
    /// <c>IPlaybookLookupService.GetByIdAsync</c> per ADR-018 typed-options +
    /// Pattern A stable-ID resolution. Default is empty string — populated per-env at deploy time.
    /// </remarks>
    public string ChatSummarizePlaybookId { get; set; } = string.Empty;

    /// <summary>
    /// Stable-ID lookup value for the "Create New Matter Pre-Fill" playbook
    /// (spec FR-02, NFR-07 pre-fill flow).
    /// </summary>
    /// <remarks>
    /// Bound from <c>Workspace:MatterPreFillPlaybookId</c> config key. GUID-format opaque ID
    /// per Q&amp;A 2026-06-22 Q1 (value mirrors the row's <c>sprk_analysisplaybookid</c> PK).
    /// Pre-seated by chat-routing-redesign-r1 task 013 (CRIT-1 race-condition fix)
    /// so wave 1-E consumer migrations (task 017) can land in parallel without
    /// colliding on <see cref="WorkspaceOptions"/>. NFR-07 contract preserved:
    /// the 45s timeout, <c>useAiPrefill</c> flag, and <c>$choices</c> envelope must
    /// remain unchanged when task 017 migrates <c>MatterPreFillService</c> to
    /// resolve this id via <c>IPlaybookLookupService.GetByIdAsync</c>.
    /// Default is empty string — populated per-env at deploy time.
    /// </remarks>
    public string MatterPreFillPlaybookId { get; set; } = string.Empty;
}
