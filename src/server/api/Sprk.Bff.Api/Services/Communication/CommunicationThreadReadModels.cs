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

/// <summary>
/// By-regarding read result (R2 task 010 / FR-01): ALL of a regarding record's threads, each carrying its own
/// access-filtered message list in the SAME per-thread DTO shape as the R1 thread-id read (<see cref="ThreadReadResult"/>
/// → <see cref="ThreadMessageDto"/>). Entity-set-agnostic across all 11 ADR-024 regarding families — the
/// <see cref="EntityType"/> only selects WHICH typed thread-regarding lookup is queried; the message fetch + access
/// filter are identical for every family. Every thread here was returned by the IMPERSONATED thread query (so the
/// caller may see the thread) and every message by the IMPERSONATED message query + the shared
/// <c>CommunicationAccessFilter</c> — private/internal-only content the caller may not see is never present (NFR-03).
/// </summary>
public sealed record RegardingReadResult(
    string EntityType,
    Guid RecordId,
    IReadOnlyList<ThreadReadResult> Threads,
    int ThreadCount,
    int MessageCount);

/// <summary>
/// Filtered communication-query result (R2 task 011 / FR-02): a flat, access-filtered communication list in the R1
/// <see cref="ThreadMessageDto"/> shape, produced by composing the thread/regarding/channel/date facets onto the
/// SAME impersonation read path + <c>CommunicationAccessFilter</c> as <see cref="RegardingReadResult"/>. The
/// <c>participant=</c> facet is STUBBED until R2 W5 (task 051) — see the endpoint/service for the not-yet-supported
/// contract. No message content bypasses the BFF filter (NFR-03).
/// </summary>
public sealed record CommunicationQueryResult(
    IReadOnlyList<ThreadMessageDto> Messages,
    int Count);
