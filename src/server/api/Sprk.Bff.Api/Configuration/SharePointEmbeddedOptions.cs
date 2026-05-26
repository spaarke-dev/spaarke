using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for SharePoint Embedded resources (containers used for staging,
/// production document storage, etc.).
/// Bound to the "SharePointEmbedded" configuration section.
/// </summary>
/// <remarks>
/// Per ADR-010 / ADR-018, BFF code reads configuration via typed <see cref="IOptions{TOptions}"/>
/// rather than raw <see cref="IConfiguration"/> indexer reads. This centralizes the keys,
/// applies data-annotation validation at startup (when wired with <c>ValidateOnStart()</c>),
/// and prevents typos from silently degrading behaviour at runtime.
/// </remarks>
public class SharePointEmbeddedOptions
{
    /// <summary>
    /// Configuration section name. Used by <c>configuration.GetSection(SharePointEmbeddedOptions.SectionName)</c>.
    /// </summary>
    public const string SectionName = "SharePointEmbedded";

    /// <summary>
    /// Container ID used to stage uploaded files during AI pre-fill flows (matter and project).
    /// Used by <see cref="Sprk.Bff.Api.Services.Workspace.MatterPreFillService"/> and
    /// <see cref="Sprk.Bff.Api.Services.Workspace.ProjectPreFillService"/>.
    /// When unset (null or empty), pre-fill services fall back to in-memory text extraction
    /// without persisting files to SPE.
    /// </summary>
    public string? StagingContainerId { get; set; }

    /// <summary>
    /// SharePoint Embedded Container Type ID for the tenant. Required for container provisioning
    /// and selection-scoped Graph operations. Read directly by callers that need it; not used
    /// by the pre-fill services themselves.
    /// </summary>
    public string? ContainerTypeId { get; set; }
}
