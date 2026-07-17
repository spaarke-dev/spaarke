namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// A single attachment reference on a thread message (task 050 thread-read). Points at the governed
/// <c>sprk_document</c> + the <c>sprk_communicationattachment</c> intersection — NEVER the binary (SPE is the
/// store; the binary is never on the message, per task 070). Populated from ONE bulk impersonated query over the
/// page's messages, so no per-row fan-out (NFR-07).
/// </summary>
public sealed record ThreadAttachmentRef(
    Guid CommunicationAttachmentId,
    Guid? DocumentId,
    string? FileName,
    int? AttachmentType);

/// <summary>
/// One <c>sprk_communication</c> row projected for the timeline (task 060). Body + channel + sender + timestamp +
/// reply pointer + attachment references — the columns the timeline renders. <see cref="Privilege"/> is composed
/// metadata carried from the access-filter decision (it NEVER gated the read — ADR-015 / owner decision 2026-07-16);
/// the timeline may surface it as a badge. Only messages the caller may read are ever projected here.
/// </summary>
public sealed record ThreadMessageDto(
    Guid MessageId,
    string? Body,
    int? BodyFormat,
    int? CommunicationType,
    string? From,
    DateTimeOffset? SentAt,
    DateTimeOffset? CreatedOn,
    string? InReplyTo,
    int Privilege,
    IReadOnlyList<ThreadAttachmentRef> Attachments);

/// <summary>
/// Thread-read endpoint result: the access-filtered, ordered message list for a thread (task 050 / FR-11).
/// <see cref="Count"/> == <c>Messages.Count</c> (the readable subset returned on this page).
/// </summary>
public sealed record ThreadReadResult(
    Guid ThreadId,
    IReadOnlyList<ThreadMessageDto> Messages,
    int Count);

/// <summary>
/// Unread-count endpoint result: the count of READABLE messages in the thread newer than the caller's
/// <see cref="Since"/> last-seen marker (task 050 / FR-11). The count reflects the SAME internal-only + privilege
/// filter as thread-read — a message the caller cannot read is never counted (NFR-06).
/// </summary>
public sealed record UnreadCountResult(
    Guid ThreadId,
    DateTimeOffset? Since,
    int UnreadCount);
