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
/// <para>
/// <b>Sender-identity enrichment (R3 task 002 / FR-18/FR-02):</b> <see cref="Direction"/>, <see cref="SentBy"/>, and
/// <see cref="SentByName"/> are PROJECTED METADATA read from the SAME already-impersonated, already-access-filtered
/// row as every other field here — they are NOT a second query, a directory lookup, or an access gate. The R3
/// Teams-style bubble UI derives mine-right/others-left alignment from the <see cref="SentBy"/> systemuserid (not
/// from email-string matching) and renders <see cref="Direction"/> + <see cref="SentByName"/>. Because these fields
/// ride the visible-row projection, a row the caller may not see (excluded by impersonation or dropped by the shared
/// filter) contributes NONE of them to the output (no over-disclosure — NFR-01).
/// </para>
/// </summary>
/// <param name="Direction"><c>sprk_direction</c> choice: Incoming=100000000, Outgoing=100000001; null when unset.</param>
/// <param name="SentBy">The sender's Dataverse <c>systemuserid</c> from <c>_sprk_sentby_value</c>; null when unset.</param>
/// <param name="SentByName">The sender's display name from <c>sprk_sentbyname</c>; null when unset.</param>
/// <param name="Subject">The communication subject from <c>sprk_subject</c> (R3 task 021 / FR-04); null when unset. Projected metadata on the same access-filtered row (no new query/gate).</param>
/// <param name="To">The email "To" header recipients from <c>sprk_to</c> (R3 task 021 / FR-04), semicolon-split; empty when unset. The authoritative recipient field of a row the caller is already permitted to read — NOT a fabricated/derived list, and NEVER BCC (BCC is stored separately and is never projected). Projected metadata on the same access-filtered row.</param>
/// <param name="Privilege"><c>sprk_privilegeclassification</c> (FR-21): None=100000000 / PotentiallyPrivileged=100000001 / Privileged=100000002. Composed from the access-filter decision — it NEVER gated this read (ADR-015 / owner 2026-07-16); a display/review label only.</param>
/// <param name="IsInternalOnly">
/// <c>sprk_isinternalonly</c> (FR-21) — the D-05 internal-only marker. <b>Never over-discloses:</b> the shared
/// <c>CommunicationAccessFilter</c> DROPS an internal-only row for a non-internal caller (Rule 1), so this flag is
/// <c>true</c> here ONLY on a row the (permitted, internal) caller already sees. It rides the same access-filtered
/// row as every other field — a row excluded upstream contributes nothing (no over-disclosure, NFR-01). Defaults to
/// <c>false</c> when the column is unset on a visible row (display default; the access gate, not this label, is the
/// fail-closed path).
/// </param>
/// <param name="IsPrivate">
/// <c>sprk_isprivate</c> (FR-21) — the message-level privacy marker. Pure DISPLAY metadata: it NEVER gates a read.
/// Private-thread visibility is enforced by Dataverse impersonation (base ownership/scoping per the access-model
/// decision 2026-07-16), NOT by this flag — so surfacing it on a row the caller is already permitted to read cannot
/// imply access anyone lacks. Defaults to <c>false</c> when unset.
/// </param>
public sealed record ThreadMessageDto(
    Guid MessageId,
    string? Body,
    int? BodyFormat,
    int? CommunicationType,
    string? From,
    string? Subject,
    IReadOnlyList<string> To,
    int? Direction,
    Guid? SentBy,
    string? SentByName,
    DateTimeOffset? SentAt,
    DateTimeOffset? CreatedOn,
    string? InReplyTo,
    int Privilege,
    bool IsInternalOnly,
    bool IsPrivate,
    IReadOnlyList<ThreadAttachmentRef> Attachments);

/// <summary>
/// Thread-read endpoint result: the access-filtered, ordered message list for a thread (task 050 / FR-11).
/// <see cref="Count"/> == <c>Messages.Count</c> (the readable subset returned on this page).
/// <see cref="Name"/> (the thread's <c>sprk_name</c>) is populated by the by-regarding read (R2 task 010/020,
/// FR-01/FR-03 — the record-level grouped view needs a label per collapsible group) AND, since R3 task 002 / FR-18,
/// by the R1 per-thread read (<c>ReadThreadAsync</c>) as well — the R3 conversation surface renders the thread label
/// inline rather than relying on the host record header. The name is read via a single IMPERSONATED projection on
/// <c>sprk_communicationthread</c>, so a caller who cannot see the thread record gets <c>null</c> (fail closed — no
/// existence leak).
/// </summary>
/// <param name="IsPinned">
/// <c>sprk_ispinned</c> (R3 task 041 / FR-24). Populated ONLY on the by-regarding path (record-mode thread list —
/// see <see cref="CommunicationThreadReadService.ReadByRegardingAsync"/>); defaults to <c>false</c> on the R1
/// per-thread read (<c>ReadThreadAsync</c>), which is not consumed by the thread-list pane. A pre-existing thread
/// row (created before task 040's schema addition) reads back <c>null</c> for <c>sprk_ispinned</c> — this is
/// normalized to <c>false</c> here (never surfaced as a three-state value), so "not pinned" is the only falsy
/// reading a caller ever observes (task 040 notes: <c>DefaultValue</c> does not backfill existing rows).
/// </param>
public sealed record ThreadReadResult(
    Guid ThreadId,
    string? Name,
    IReadOnlyList<ThreadMessageDto> Messages,
    int Count,
    bool IsPinned = false);

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
/// One thread summary row for the left-pane thread list (R3 task 003 / FR-16). Minimal by construction — the list
/// pane renders a label + type; opening a thread fetches its messages via the existing thread-read. Every item here
/// was returned by the IMPERSONATED <c>sprk_communicationthreads</c> query, so the caller may see the thread record
/// (Dataverse row-level security decided it — ownership, role depth, BU, teams, sharing, hierarchy). RECORD-LESS
/// (Direct / non-record-anchored) threads are included exactly like record-anchored ones because that query is NOT
/// scoped to any <c>sprk_regarding{type}</c> lookup. <see cref="ThreadType"/> is <c>sprk_threadtype</c>
/// (Record-Anchored=100000000, Direct 1:1=100000001; null when unset); <see cref="CreatedOn"/> is the deterministic
/// ordering key that also backs the opaque paging cursor. <see cref="IsPinned"/> is <c>sprk_ispinned</c> (R3 task
/// 041 / FR-24), normalized from Dataverse's <c>null</c>-on-pre-existing-rows reading to <c>false</c> — the
/// thread-list pane sorts/marks pinned threads on this field, never observing a three-state value.
/// </summary>
public sealed record ThreadListItem(
    Guid ThreadId,
    string? Name,
    int? ThreadType,
    DateTimeOffset? CreatedOn,
    bool IsPinned,
    // round-8.4 UAT item 3: the thread's associated ("regarding") record, resolved from the typed ADR-024 lookups
    // (RegardingFieldMap). Lets the message-pane "open associated record" affordance navigate to it. Both null for a
    // record-less Direct thread.
    string? RegardingEntityType = null,
    Guid? RegardingId = null);

/// <summary>
/// List-all-threads result (R3 task 003 / FR-16 / Success Criterion 5): a paged, name-searchable list of ALL threads
/// the caller may see, INCLUDING record-less Direct threads. The set is EXACTLY what the IMPERSONATED
/// <c>sprk_communicationthreads</c> query returns for the caller (access parity — Dataverse row-level security is the
/// only gate; NO post-hoc regarding scoping, NO hand-computed membership-union — retired 2026-07-16). A thread the
/// caller cannot see is simply absent (no over-disclosure — NFR-01). Ordering is <c>createdon desc</c> (deterministic)
/// so <see cref="NextPageToken"/> yields stable, non-overlapping pages: it is an opaque keyset cursor (Dataverse Web
/// API has no <c>$skip</c>) that the caller passes back as <c>?pageToken=</c> to fetch the next page;
/// <see cref="HasMore"/> is true iff a further page exists (<see cref="NextPageToken"/> is then non-null).
/// </summary>
public sealed record ThreadListResult(
    IReadOnlyList<ThreadListItem> Threads,
    int Count,
    string? NextPageToken,
    bool HasMore);

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
