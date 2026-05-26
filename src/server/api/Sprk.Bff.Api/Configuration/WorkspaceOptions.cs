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
    /// When unset, the service falls back to the hardcoded default
    /// <c>3f21cec1-7d19-f111-8343-7ced8d1dc988</c>.
    /// </summary>
    public string? ProjectPreFillPlaybookId { get; set; }

    /// <summary>
    /// Default playbook ID for the AI summary playbook used by
    /// <see cref="Sprk.Bff.Api.Services.Workspace.WorkspaceAiService"/>.
    /// When unset, the service falls back to the hardcoded default
    /// <c>18cf3cc8-02ec-f011-8406-7c1e520aa4df</c>.
    /// </summary>
    public string? AiSummaryPlaybookId { get; set; }
}
