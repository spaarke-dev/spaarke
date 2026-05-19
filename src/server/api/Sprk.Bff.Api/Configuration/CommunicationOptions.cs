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

    /// <summary>
    /// Public URL that Microsoft Graph calls back to for webhook notifications.
    /// Must be HTTPS and publicly accessible. Environment-specific.
    /// Example: https://{app-service}.azurewebsites.net/api/communications/incoming-webhook
    /// </summary>
    [Required(ErrorMessage = "Communication:WebhookNotificationUrl is required for Graph subscriptions.")]
    public string WebhookNotificationUrl { get; set; } = string.Empty;

    /// <summary>
    /// Shared secret carried inside the Graph notification body's <c>clientState</c> field.
    /// Validated in constant time on every incoming notification. Must be stored in
    /// Key Vault and referenced via <c>@Microsoft.KeyVault(...)</c>.
    /// </summary>
    [Required(ErrorMessage = "Communication:WebhookClientState is required for Graph webhook validation.")]
    public string WebhookClientState { get; set; } = string.Empty;

    /// <summary>
    /// HMAC-SHA256 signing key used by <see cref="Api.Filters.WebhookSignatureFilter"/>
    /// to validate the <c>X-Hub-Signature-256</c> header on incoming webhook requests.
    /// <para>
    /// Microsoft Graph itself does not sign notification bodies, so this key is
    /// applied when a signing relay (Logic App, Function, API Management) sits in
    /// front of the endpoint and adds the signature on Graph's behalf. Together
    /// with the body-level <see cref="WebhookClientState"/> check, this enforces
    /// defense-in-depth: a leaked endpoint URL alone cannot forge notifications.
    /// </para>
    /// <para>
    /// MUST be stored in Key Vault and referenced via <c>@Microsoft.KeyVault(...)</c>.
    /// MUST be rotated on incident or on a calendar cadence (e.g., 90 days).
    /// </para>
    /// </summary>
    [Required(ErrorMessage = "Communication:WebhookSigningKey is required (HMAC-SHA256 webhook validation). See Key Vault secret 'communication-webhook-signing-key'.")]
    public string WebhookSigningKey { get; set; } = string.Empty;
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
