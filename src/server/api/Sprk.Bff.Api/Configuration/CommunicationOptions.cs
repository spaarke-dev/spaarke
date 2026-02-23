using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for the Communication Service.
/// Bound from appsettings.json section "Communication".
/// </summary>
public class CommunicationOptions
{
    public const string SectionName = "Communication";

    /// <summary>
    /// List of approved sender mailbox configurations.
    /// Phase 1: sole source of approved senders.
    /// Phase 2+: merged with Dataverse sprk_approvedsender entity.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one approved sender must be configured.")]
    public ApprovedSenderConfig[] ApprovedSenders { get; set; } = Array.Empty<ApprovedSenderConfig>();

    /// <summary>
    /// Default mailbox address used when no fromMailbox is specified and no sender is marked IsDefault.
    /// Falls back to first approved sender if not set.
    /// </summary>
    public string? DefaultMailbox { get; set; }

    /// <summary>
    /// SPE container/drive ID used for archiving .eml files.
    /// Required when ArchiveToSpe=true is used in send requests.
    /// </summary>
    public string? ArchiveContainerId { get; set; }
}

/// <summary>
/// Configuration for an approved email sender.
/// </summary>
public class ApprovedSenderConfig
{
    /// <summary>
    /// Email address of the approved sender (shared mailbox or user mailbox).
    /// </summary>
    [Required]
    public required string Email { get; set; }

    /// <summary>
    /// Display name shown in the "From" field of sent emails.
    /// </summary>
    [Required]
    public required string DisplayName { get; set; }

    /// <summary>
    /// Whether this sender is the default when no fromMailbox is specified in the request.
    /// Only one sender should be marked as default.
    /// </summary>
    public bool IsDefault { get; set; }
}
