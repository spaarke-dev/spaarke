using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Channels;

/// <summary>
/// Channel-extensibility seam for the "send" leg of a communication (ADR-045 rule 4 / NFR-04).
/// One implementation per <see cref="CommunicationType"/>; <see cref="EmailChannelSender"/> is the
/// sole R4 implementation (wraps the Microsoft Graph send path). A future channel (Teams/Slack/SMS)
/// is added by registering a new implementation keyed to its own <see cref="SupportedType"/> — the
/// dispatcher, <see cref="CommunicationService"/>, the Association Engine, the enrichment service, the
/// regarding model, and the review UI do NOT change to add a channel.
/// </summary>
public interface ICommunicationChannelSender
{
    /// <summary>The <see cref="CommunicationType"/> this sender handles (dispatch key).</summary>
    CommunicationType SupportedType { get; }

    /// <summary>
    /// Transmits the communication over the channel provider. Implementations own ALL provider-specific
    /// wire concerns (message construction, transport auth, provider error mapping) and MUST NOT leak
    /// provider types across the seam. Provider transmit failures surface as
    /// <see cref="Infrastructure.Exceptions.SdapProblemException"/> (code <c>GRAPH_SEND_FAILED</c> for email).
    /// </summary>
    Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Channel-neutral input to <see cref="ICommunicationChannelSender.SendAsync"/>. Carries the app-level
/// request DTO plus the resolved sender identity and pre-materialized attachments, so no channel provider
/// type appears in the seam signature.
/// </summary>
public sealed record ChannelSendRequest
{
    /// <summary>The originating app-level send request (recipients, subject, body, body format).</summary>
    public required SendCommunicationRequest Communication { get; init; }

    /// <summary>Resolved sender address (approved mailbox for shared mode; user's mailbox for user mode).</summary>
    public required string FromAddress { get; init; }

    /// <summary>Optional display name for the sender (shared-mailbox mode only).</summary>
    public string? FromDisplayName { get; init; }

    /// <summary>
    /// True when sending on-behalf-of the authenticated user (OBO / <c>/me</c>); false for the app-only
    /// shared-mailbox path.
    /// </summary>
    public required bool UserMode { get; init; }

    /// <summary>HttpContext for OBO token resolution — required when <see cref="UserMode"/> is true.</summary>
    public HttpContext? HttpContext { get; init; }

    /// <summary>Materialized attachments (already downloaded + size/count validated), or null when none.</summary>
    public IReadOnlyList<ChannelAttachment>? Attachments { get; init; }

    /// <summary>Correlation id for tracing.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// Messaging channel only: the sending participant (a <c>systemuser</c> or <c>contact</c>) whose ACS
    /// identity is resolved (task 010) and whose server-minted <c>chat</c> token is used to transmit. Email
    /// addresses by mailbox, so this is null on the email path. Populated by the outbound persist-on-send
    /// wiring (task 051); required by <see cref="MessagingChannelSender"/> (a null value fails the send).
    /// Optional + additive — no email caller sets it, so the field defaults to null and the email path is
    /// byte-unchanged (NFR-04).
    /// </summary>
    public ParticipantReference? SenderParticipant { get; init; }

    /// <summary>
    /// Messaging channel only: the ACS <c>ChatThreadId</c> this message is sent into, resolved upstream by
    /// the thread resolver (task 040). When null, <see cref="MessagingChannelSender"/> creates a thread via
    /// the ACS thread plane (task 011) seeded with the sender. Ignored on the email path (null there).
    /// Optional + additive (see <see cref="SenderParticipant"/>).
    /// </summary>
    public string? AcsThreadId { get; init; }
}

/// <summary>Channel-neutral attachment payload for a send.</summary>
public sealed record ChannelAttachment
{
    public required string Name { get; init; }
    public required byte[] Content { get; init; }
    public string? ContentType { get; init; }
}

/// <summary>Channel-neutral result of a transmit.</summary>
public sealed record ChannelSendResult
{
    /// <summary>The address the communication was sent from.</summary>
    public required string FromAddress { get; init; }

    /// <summary>
    /// Provider-assigned message id captured post-send when available (email: the RFC-2822
    /// Internet-Message-Id read back from Sent Items for reply-thread continuity, FR-06; messaging: the ACS
    /// <c>SendChatMessageResult.Id</c> — the echo-dedup key, FR-04). Null when the provider returns none or
    /// capture was best-effort-skipped.
    /// </summary>
    public string? ProviderMessageId { get; init; }

    /// <summary>
    /// Provider-assigned thread/conversation id when the channel is thread-based (messaging: the ACS
    /// <c>ChatThreadId</c> the message landed in — persisted by task 051 as <c>sprk_acsthreadid</c> and the
    /// <c>sprk_communicationchannelref.sprk_externalref</c>). Null for channels with no thread concept (email).
    /// </summary>
    public string? ProviderThreadId { get; init; }
}
