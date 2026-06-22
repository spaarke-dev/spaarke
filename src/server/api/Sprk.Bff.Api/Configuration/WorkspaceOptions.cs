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
    /// (Pattern A code-based resolution planned; see ADR-018).
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
    /// Default playbook ID for the "Summarize New File(s)" playbook used by
    /// <see cref="Sprk.Bff.Api.Api.Workspace.WorkspaceFileEndpoints"/> (POST
    /// <c>/api/workspace/files/summarize</c>).
    /// </summary>
    /// <remarks>
    /// Bound from <c>Workspace:SummarizePlaybookId</c> config key.
    /// This property replaces the prior raw <c>IConfiguration["Workspace:SummarizePlaybookId"]</c>
    /// indexer read at <c>WorkspaceFileEndpoints.cs:30,254</c> (ADR-018 violation fix
    /// — see chat-routing-redesign-r1 task 012 / spec FR-04).
    /// When unset, the endpoint falls back to the hardcoded default
    /// <c>4a72f99c-a119-f111-8343-7ced8d1dc988</c>.
    /// Preserved alongside <see cref="SummarizePlaybookCode"/> for backward-compat
    /// until task 019 migrates the consumer to stable-code resolution.
    /// </remarks>
    public string? SummarizePlaybookId { get; set; }

    /// <summary>
    /// Stable playbook code for the "Summarize New File(s)" playbook.
    /// </summary>
    /// <remarks>
    /// Bound from <c>Workspace:SummarizePlaybookCode</c> config key (expected value:
    /// <c>summarize-document-workspace</c>). Introduced by chat-routing-redesign-r1
    /// task 012 / spec FR-04 to enable the Pattern A stable-code resolution migration
    /// performed by task 019 (which resolves the code → GUID at runtime via
    /// <c>IPlaybookLookupService.GetByCodeAsync</c>). Until task 019 lands, this
    /// property exists but is unconsumed; the endpoint continues to use
    /// <see cref="SummarizePlaybookId"/> for backward-compat.
    /// </remarks>
    public string SummarizePlaybookCode { get; set; } = string.Empty;
}
