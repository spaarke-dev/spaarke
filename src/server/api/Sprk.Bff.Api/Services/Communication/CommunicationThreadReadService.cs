using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The BFF read model for the polling timeline (task 050 / FR-11): thread-read (a thread's messages) and
/// unread-count (readable messages since a caller's last-seen marker). Both are the ~5s poll surface for every
/// open form, so both are lightweight by construction — projected columns, paged, NO ACS call (Dataverse is the
/// record), and bounded to a FIXED number of impersonated queries per read regardless of message count (NFR-07 —
/// no per-row fan-out). Thread-read issues at most three O(1) queries: the message page, one bulk attachment query
/// for the visible page, and (R3 task 002 / FR-18) one thread-name projection for the inline label.
///
/// <para><b>Access model (owner decision 2026-07-16, <c>notes/access-model-decision.md</c>):</b> record-level read
/// access is Dataverse's job — every read issues the <c>sprk_communication</c> query IMPERSONATED
/// (<c>MSCRMCallerID</c> = the caller's <c>systemuserid</c>) via <see cref="IImpersonatedCommunicationQuery"/>, so
/// Dataverse returns exactly the rows the caller may see (ownership, role depth, BU, teams, sharing, hierarchy) in
/// one query. On TOP of those already-scoped rows this service applies the SAME task-042
/// <see cref="ICommunicationAccessFilter"/> both endpoints share — internal-only (D-05) hides
/// <c>sprk_isinternalonly</c> rows from non-internal callers; privilege rides along as metadata and NEVER gates
/// (ADR-015). No second/divergent filter is introduced (FR-08 / NFR-06). Fail-closed: an unresolved caller
/// (no Dataverse <c>systemuserid</c>) is refused — there is NO app-only fallback that would widen access.</para>
///
/// <para>Scoped (not a method on the Singleton <see cref="CommunicationService"/>) because it consumes the Scoped
/// <see cref="ICallerSystemUserResolver"/>; a captive scoped dependency inside the singleton would be an
/// anti-pattern. §11: the read model is a distinct concern from send/archive.</para>
/// </summary>
public sealed class CommunicationThreadReadService
{
    // OData entity SET (collection) names — regular +s pluralization.
    private const string CommunicationSet = "sprk_communications";
    private const string CommunicationAttachmentSet = "sprk_communicationattachments";
    private const string ThreadSet = "sprk_communicationthreads"; // by-regarding thread query (R2 task 010)

    // sprk_communicationthread columns (R2 tasks 010/002; R3 task 003 adds sprk_threadtype for the list pane).
    private const string ThreadPkField = "sprk_communicationthreadid";
    private const string ThreadNameField = "sprk_name";
    private const string ThreadTypeField = "sprk_threadtype"; // Record-Anchored=100000000, Direct 1:1=100000001
    private const string PinnedField = "sprk_ispinned"; // R3 task 040/041 / FR-24 — null on pre-existing rows, normalized to false below

    // round-8.4 item 3 (fix): sprk_communicationthread carries the same typed ADR-024 regarding lookups as
    // sprk_communication EXCEPT sprk_regardingreportcard, which does NOT exist on the thread entity. Selecting a
    // non-existent column 400s the whole OData query (surfaced client-side as an HttpRequestException / blank thread
    // list), so the thread projection uses only the lookups the thread actually has. Verified against the
    // sprk_communicationthread metadata 2026-08-03.
    private static readonly IReadOnlyList<(string EntityLogicalName, string RegardingField)> ThreadRegardingFields =
        RegardingFieldMap.All.Where(x => x.RegardingField != "sprk_regardingreportcard").ToArray();

    // sprk_communication columns (as-built, notes/messaging-schema-spec.md).
    private const string ThreadLookupValue = "_sprk_communicationthread_value"; // message → thread lookup
    private const string PkField = "sprk_communicationid";
    // Round-8 UAT fix: soft-delete (deactivate) sets statecode=1 (Inactive), but Dataverse RetrieveMultiple returns
    // inactive rows unless the query excludes them — so a deactivated thread/message reappeared on the next poll
    // ("delete didn't work"). Every read below filters `statecode eq 0` (Active) so deactivated rows drop out. Both
    // sprk_communication and sprk_communicationthread carry the OOB `statecode` attribute.
    private const string StateCodeField = "statecode";
    private const string ActiveOnlyClause = "statecode eq 0";
    private const string BodyField = "sprk_body";
    private const string BodyFormatField = "sprk_bodyformat";
    private const string TypeField = "sprk_communicationtype";
    private const string FromField = "sprk_from";
    private const string SubjectField = "sprk_subject";                 // R3 task 021 / FR-04 — email-in-flow block subject
    private const string ToField = "sprk_to";                           // R3 task 021 / FR-04 — "; "-joined recipient To header
    private const string SentAtField = "sprk_sentat";
    private const string CreatedOnField = "createdon";
    private const string InReplyToField = "sprk_inreplyto";
    private const string InternalOnlyField = "sprk_isinternalonly";
    private const string PrivilegeField = "sprk_privilegeclassification";
    private const string IsPrivateField = "sprk_isprivate";             // R3 task 043 / FR-21 — message-level privacy marker (display metadata; NEVER gates a read)
    // Sender-identity enrichment (R3 task 002 / FR-18) — projected metadata over the already-visible row.
    private const string DirectionField = "sprk_direction";            // choice: Incoming=100000000, Outgoing=100000001
    private const string SentByValue = "_sprk_sentby_value";           // systemuser lookup value
    // Sender display name comes from the sprk_sentby lookup's FormattedValue annotation (the systemuser's
    // display name), which rides the already-selected _sprk_sentby_value lookup when the impersonated query
    // requests annotations (DataverseWebApiService.RetrieveMultipleImpersonatedAsync). This REPLACES the
    // denormalized sprk_sentbyname column, which is in a broken metadata state in the env
    // (IsValidODataAttribute=false → 400 on $select) and was never written by the send path anyway
    // (messaging-r3 2026-07-22). Null when sprk_sentby is unset (e.g. an inbound message with no systemuser sender).
    private const string SentByFormattedValue = "_sprk_sentby_value@OData.Community.Display.V1.FormattedValue";

    // sprk_communicationattachment columns (pre-existing intersection, task 070).
    private const string AttachmentPkField = "sprk_communicationattachmentid";
    private const string AttachmentNameField = "sprk_name";
    private const string AttachmentTypeField = "sprk_attachmenttype";
    private const string AttachmentDocumentValue = "_sprk_document_value";
    private const string AttachmentCommunicationValue = "_sprk_communication_value";

    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 200;
    private const int MaxUnreadScan = 500; // bound the unread projection scan (NFR-07)
    private const int MaxThreads = 200;         // bound the by-regarding thread fan (R2 task 010)
    private const int DefaultThreadListPage = 50;  // list-all-threads default page size (R3 task 003 / FR-16)
    private const int MaxThreadListPage = 200;     // list-all-threads page-size ceiling (mirrors MaxThreads)
    private const int MaxRegardingScan = 500;   // bound the by-regarding message scan across threads (R2 task 010)
    private const int MaxQueryScan = 200;       // bound the filtered-query message scan (R2 task 011)

    // sprk_communicationparticipant junction (R2 tasks 003/050/051) — the `participant=` facet join.
    private const string ParticipantSet = "sprk_communicationparticipants";
    private const string ParticipantCommunicationValue = "_sprk_communication_value";
    private const string ParticipantSystemUserValue = "_sprk_systemuser_value";
    private const string ParticipantContactValue = "_sprk_contact_value";
    private const string ParticipantAddressTextField = "sprk_addresstext";
    private const int MaxParticipantScan = 500;          // bound the junction candidate-id scan (R2 task 051)
    private const int MaxParticipantAddressLength = 400; // matches sprk_addresstext Text(400) field max length

    private readonly IImpersonatedCommunicationQuery _query;
    private readonly ICommunicationAccessFilter _accessFilter;
    private readonly ICallerSystemUserResolver _callerResolver;
    private readonly ISystemUserIdentityResolver _identityResolver;
    private readonly ILogger<CommunicationThreadReadService> _logger;

    public CommunicationThreadReadService(
        IImpersonatedCommunicationQuery query,
        ICommunicationAccessFilter accessFilter,
        ICallerSystemUserResolver callerResolver,
        ISystemUserIdentityResolver identityResolver,
        ILogger<CommunicationThreadReadService> logger)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _accessFilter = accessFilter ?? throw new ArgumentNullException(nameof(accessFilter));
        _callerResolver = callerResolver ?? throw new ArgumentNullException(nameof(callerResolver));
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Thread-read: the caller's readable messages in <paramref name="threadId"/>, ordered oldest→newest, with
    /// attachment references. <paramref name="since"/> (optional) returns only messages created after that instant
    /// (incremental poll). Returns an EMPTY result (never 404) when the caller can see no messages — a 404 would
    /// leak the existence of a private thread (NFR-06); "none visible" and "does not exist" are intentionally
    /// indistinguishable to the client.
    /// </summary>
    public async Task<ThreadReadResult> ReadThreadAsync(
        Guid threadId,
        ClaimsPrincipal? caller,
        DateTimeOffset? since,
        int? top,
        CancellationToken ct)
    {
        var callerSystemUserId = await ResolveCallerOrThrowAsync(caller, ct);
        var pageSize = NormalizePageSize(top);

        // 1) Impersonated message read — Dataverse row-level security applies natively.
        var select = string.Join(',', new[]
        {
            PkField, BodyField, BodyFormatField, TypeField, FromField, SubjectField, ToField,
            // sprk_sentbyname is NOT selected — it is broken in the env (IsValidODataAttribute=false) and 400s the
            // whole read. The sender display name instead comes from the _sprk_sentby_value lookup's FormattedValue
            // annotation (the impersonated query requests annotations; ParseMessageRow reads it) — messaging-r3 2026-07-22.
            DirectionField, SentByValue,
            SentAtField, CreatedOnField, InReplyToField, InternalOnlyField, PrivilegeField, IsPrivateField,
        });
        var filter = new StringBuilder($"{ThreadLookupValue} eq {threadId} and {ActiveOnlyClause}");
        if (since is { } s)
            filter.Append($" and {CreatedOnField} gt {FormatOData(s)}");

        var odata = $"$select={select}&$filter={filter}&$orderby={CreatedOnField} asc&$top={pageSize}";
        var rows = await _query.QueryAsync(CommunicationSet, odata, callerSystemUserId, ct);

        // 2) Apply the SHARED internal-only + privilege filter on top of the already-scoped rows.
        // #675 / ISS-006: the internal-vs-external bit is the AUTHORITATIVE per-caller value (systemuser.sprk_isexternal
        // via the shared resolver), NOT a hardcoded `true`. Hardcoding internal made CommunicationAccessFilter treat
        // every caller as internal, so an external-licensed systemuser could read internal-only (D-05) messages
        // (over-disclosure). IsExternalAsync fails closed (external) on an unresolvable id.
        var parsed = rows.Select(ParseMessageRow).ToList();
        var isExternal = await _identityResolver.IsExternalAsync(callerSystemUserId, ct);
        var context = new CommunicationAccessContext(CallerSystemUserId: callerSystemUserId, IsInternalUser: !isExternal);
        var filtered = _accessFilter.FilterMessages(context, parsed.Select(p => p.Entity).ToList());

        var byId = parsed.ToDictionary(p => p.MessageId);
        var visible = filtered.Decisions
            .Where(d => d.Decision.IsVisible)
            .Select(d => (Parsed: byId[d.Message.Id], d.Decision))
            .ToList();

        // 3) Attachments — ONE bulk impersonated query for the visible page (no per-row fan-out).
        var attachmentsByMessage = await LoadAttachmentsAsync(
            visible.Select(v => v.Parsed.MessageId).ToList(), callerSystemUserId, ct);

        var messages = visible.Select(v => BuildDto(v.Parsed, v.Decision, attachmentsByMessage)).ToList();

        // Thread label (R3 task 002 / FR-18): one bounded IMPERSONATED projection on sprk_communicationthread by id.
        // Impersonated, so a caller who cannot see the thread record gets null — no existence leak (fail closed).
        var name = await ReadThreadNameAsync(threadId, callerSystemUserId, ct);

        _logger.LogDebug(
            "[THREAD-READ] thread={ThreadId} caller={Caller} returned={Returned} (impersonated={Impersonated}, hidden-internal-only={Hidden})",
            threadId, callerSystemUserId, messages.Count, rows.Count, rows.Count - messages.Count);

        return new ThreadReadResult(ThreadId: threadId, Name: name, Messages: messages, Count: messages.Count);
    }

    /// <summary>
    /// Unread-count: the number of READABLE messages in <paramref name="threadId"/> newer than
    /// <paramref name="since"/> (the caller's last-seen marker; null = count all). Reflects the SAME internal-only
    /// filter as thread-read — a message the caller cannot read is never counted (NFR-06). Projected (no body) and
    /// bounded (<see cref="MaxUnreadScan"/>) to keep the poll cheap.
    /// </summary>
    public async Task<UnreadCountResult> GetUnreadCountAsync(
        Guid threadId,
        ClaimsPrincipal? caller,
        DateTimeOffset? since,
        CancellationToken ct)
    {
        var callerSystemUserId = await ResolveCallerOrThrowAsync(caller, ct);

        var select = string.Join(',', new[] { PkField, InternalOnlyField, PrivilegeField });
        var filter = new StringBuilder($"{ThreadLookupValue} eq {threadId} and {ActiveOnlyClause}");
        if (since is { } s)
            filter.Append($" and {CreatedOnField} gt {FormatOData(s)}");

        var odata = $"$select={select}&$filter={filter}&$top={MaxUnreadScan}";
        var rows = await _query.QueryAsync(CommunicationSet, odata, callerSystemUserId, ct);

        // #675 / ISS-006: authoritative per-caller internal/external bit (fail-closed external) — an unread scan must
        // NOT count internal-only messages for an external-licensed caller (mirrors the thread-read filter above).
        var isExternal = await _identityResolver.IsExternalAsync(callerSystemUserId, ct);
        var context = new CommunicationAccessContext(CallerSystemUserId: callerSystemUserId, IsInternalUser: !isExternal);
        var entities = rows.Select(r => ParseMessageRow(r).Entity).ToList();
        var filtered = _accessFilter.FilterMessages(context, entities);
        var unread = filtered.VisibleMessages.Count;

        _logger.LogDebug(
            "[UNREAD-COUNT] thread={ThreadId} caller={Caller} since={Since} unread={Unread} (scanned={Scanned})",
            threadId, callerSystemUserId, since, unread, rows.Count);

        return new UnreadCountResult(threadId, since, unread);
    }

    // ── by-regarding read (R2 task 010 / FR-01) ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns ALL of a regarding record's threads + their access-filtered messages, in the R1 per-thread DTO
    /// shape (<see cref="RegardingReadResult"/> → <see cref="ThreadReadResult"/> → <see cref="ThreadMessageDto"/>).
    /// Entity-set-agnostic across all 11 ADR-024 regarding families: <paramref name="entityType"/> selects the typed
    /// thread-regarding lookup via <see cref="RegardingFieldMap"/> (the ONLY per-family difference); the message fetch
    /// (<c>or</c>-filter on thread id) + the shared <see cref="ICommunicationAccessFilter"/> are identical for every
    /// family. Both queries run IMPERSONATED (Dataverse row-level security is native) and the messages then pass
    /// through the SAME internal-only/privilege filter the R1 thread-read composes — no second/divergent filter, no
    /// membership-union (retired 2026-07-16). A private thread the caller has no grant for is absent from the
    /// impersonated thread set; an internal-only message the caller may not see is dropped by the filter (NFR-03).
    /// A bad <paramref name="entityType"/> is a 400 ProblemDetails (ADR-019); an empty regarding yields an empty result.
    /// </summary>
    public async Task<RegardingReadResult> ReadByRegardingAsync(
        string entityType,
        Guid recordId,
        ClaimsPrincipal? caller,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            throw BadRequest("A regarding entity type is required.");

        var regardingField = RegardingFieldMap.FieldFor(entityType);
        if (regardingField is null)
            throw BadRequest($"'{entityType}' is not a supported regarding entity type (ADR-024 family).");

        var callerSystemUserId = await ResolveCallerOrThrowAsync(caller, ct);

        // 1) Impersonated thread query by the typed regarding lookup (entity-set-agnostic; task 002 lookups).
        // PinnedField added (task 041 / FR-24) so the record-mode thread list can mark/sort pinned threads too.
        var threadSelect = string.Join(',', new[] { ThreadPkField, ThreadNameField, PinnedField });
        var threadOData =
            $"$select={threadSelect}&$filter=_{regardingField}_value eq {recordId} and {ActiveOnlyClause}" +
            $"&$orderby={CreatedOnField} asc&$top={MaxThreads}";
        var threadRows = await _query.QueryAsync(ThreadSet, threadOData, callerSystemUserId, ct);

        var threads = threadRows
            .Select(r => (
                Id: TryGuid(r, ThreadPkField) ?? Guid.Empty,
                Name: TryString(r, ThreadNameField),
                IsPinned: TryBool(r, PinnedField) ?? false))
            .Where(t => t.Id != Guid.Empty)
            .ToList();

        if (threads.Count == 0)
        {
            _logger.LogDebug(
                "[BY-REGARDING] {EntityType}={RecordId} caller={Caller} threads=0 (no visible threads)",
                entityType, recordId, callerSystemUserId);
            return new RegardingReadResult(entityType, recordId, Array.Empty<ThreadReadResult>(), 0, 0);
        }

        // 2) Impersonated message query across those threads (or-filter on thread id) + shared access filter.
        var orClause = string.Join(" or ", threads.Select(t => $"{ThreadLookupValue} eq {t.Id}"));
        var visible = await QueryVisibleMessagesAsync($"({orClause})", MaxRegardingScan, callerSystemUserId, ct);

        // 3) Group the access-filtered messages by thread (query order = createdon asc is preserved).
        var byThread = visible
            .GroupBy(v => v.ThreadId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ThreadMessageDto>)g.Select(v => v.Dto).ToList());

        var threadResults = threads
            .Select(t =>
            {
                var msgs = byThread.TryGetValue(t.Id, out var list) ? list : Array.Empty<ThreadMessageDto>();
                return new ThreadReadResult(
                    ThreadId: t.Id, Name: t.Name, Messages: msgs, Count: msgs.Count, IsPinned: t.IsPinned);
            })
            .ToList();

        var messageCount = threadResults.Sum(t => t.Count);
        _logger.LogDebug(
            "[BY-REGARDING] {EntityType}={RecordId} caller={Caller} threads={Threads} messages={Messages}",
            entityType, recordId, callerSystemUserId, threadResults.Count, messageCount);

        return new RegardingReadResult(entityType, recordId, threadResults, threadResults.Count, messageCount);
    }

    // ── list all threads (R3 task 003 / FR-16) ───────────────────────────────────────────────────────

    /// <summary>
    /// Lists ALL threads the caller may see — record-anchored AND record-less (Direct) — for the R3 workspace
    /// left pane + standalone code page (FR-16 / Success Criterion 5). Paged and optionally name-searchable.
    ///
    /// <para><b>Access model (NFR-01 — the exact failure mode this method guards):</b> the thread query is issued
    /// IMPERSONATED (<c>MSCRMCallerID</c> = caller <c>systemuserid</c>) via <see cref="IImpersonatedCommunicationQuery"/>,
    /// so Dataverse row-level security is the ONLY visibility gate (ownership, role depth, BU, teams, sharing,
    /// hierarchy). The returned set is EXACTLY what impersonation returns — there is NO post-hoc regarding scoping and
    /// NO hand-computed membership-union (retired 2026-07-16, <c>../messaging-communication-app-r1/notes/access-model-decision.md</c>).
    /// A thread the caller cannot see is simply absent (no over-disclosure). Fail-closed: an unresolved caller is
    /// refused (403) — no app-only fallback that would widen access.</para>
    ///
    /// <para><b>Record-less inclusion:</b> the query is deliberately NOT scoped to any <c>sprk_regarding{type}</c>
    /// lookup (unlike <see cref="ReadByRegardingAsync"/>), so a Direct/record-less thread — which carries no regarding
    /// anchor — is returned alongside record-anchored threads. Nothing post-filters by regarding.</para>
    ///
    /// <para><b>Search + paging:</b> <paramref name="search"/> (optional) adds a <c>contains(sprk_name, …)</c> predicate
    /// with the value single-quote-escaped (OData string-literal injection safe). Ordering is <c>createdon desc</c>
    /// (deterministic), and paging is a keyset cursor on <c>createdon</c> (Dataverse Web API has no <c>$skip</c>, and
    /// the impersonated-query seam does not surface the <c>@odata.nextLink</c> skiptoken): <paramref name="pageToken"/>
    /// is the opaque base64 cursor returned by the previous page, decoded to a <c>createdon lt …</c> lower bound — so
    /// pages are stable and non-overlapping. A malformed token is a 400 (ADR-019).</para>
    /// </summary>
    public async Task<ThreadListResult> ListThreadsAsync(
        ClaimsPrincipal? caller,
        string? search,
        int? top,
        string? pageToken,
        CancellationToken ct)
    {
        var callerSystemUserId = await ResolveCallerOrThrowAsync(caller, ct);
        var pageSize = NormalizeThreadListPageSize(top);

        // Build the filter: NO regarding scoping (record-less inclusion) + optional name search + keyset cursor.
        // Active-only (round-8): deactivated (soft-deleted) threads must drop out of the list.
        var clauses = new List<string> { ActiveOnlyClause };
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Two-stage escape: (1) double single quotes so the value cannot break out of the OData string literal;
            // (2) Uri.EscapeDataString so transport-significant chars (space, & # + %) cannot break out of the query
            // string — the impersonated-query seam concatenates the value RAW into the URL (no Uri.EscapeDataString
            // there), so an un-encoded '&'/space would truncate/inject the query. Order matters: quote-double FIRST,
            // then percent-encode the whole literal.
            var searchLiteral = Uri.EscapeDataString(EscapeODataString(search.Trim()));
            clauses.Add($"contains({ThreadNameField},'{searchLiteral}')");
        }

        var cursor = DecodePageToken(pageToken);
        if (cursor is { } c)
        {
            // COMPOSITE keyset cursor over (createdon, sprk_communicationthreadid) — createdon alone is NOT unique
            // (Dataverse createdon is second-granular; bulk/seed/rapid creation ties routinely), so a createdon-only
            // `lt` cursor would silently DROP tied rows past the page cut (a user loses visibility of their own
            // threads — breaks FR-16 "list ALL"). The tuple comparison keeps paging stable AND lossless.
            var v = FormatOData(c.CreatedOn);
            clauses.Add(
                $"({CreatedOnField} lt {v} or ({CreatedOnField} eq {v} and {ThreadPkField} lt {c.ThreadId}))");
        }

        // PinnedField added (task 041 / FR-24) so the all-mode thread list can mark/sort pinned threads.
        // round-8.4 item 3: also project the typed regarding lookups (RegardingFieldMap) so each row can carry its
        // associated record for the message-pane "open record" affordance. Lookups read back as `_{field}_value`.
        var regardingValueCols = ThreadRegardingFields.Select(x => $"_{x.RegardingField}_value");
        var select = string.Join(
            ',',
            new[] { ThreadPkField, ThreadNameField, ThreadTypeField, CreatedOnField, PinnedField }.Concat(regardingValueCols));
        var filterPart = clauses.Count > 0 ? $"&$filter={string.Join(" and ", clauses)}" : string.Empty;
        // Deterministic total order on the composite key so paging is stable + non-overlapping; over-fetch one row
        // to detect whether a further page exists without a second COUNT query.
        var odata =
            $"$select={select}{filterPart}&$orderby={CreatedOnField} desc,{ThreadPkField} desc&$top={pageSize + 1}";

        var rows = await _query.QueryAsync(ThreadSet, odata, callerSystemUserId, ct);

        var items = rows
            .Select(r =>
            {
                var (regType, regId) = ResolveRegardingFromRow(r);
                return new ThreadListItem(
                    ThreadId: TryGuid(r, ThreadPkField) ?? Guid.Empty,
                    Name: TryString(r, ThreadNameField),
                    ThreadType: TryInt(r, ThreadTypeField),
                    CreatedOn: TryDateTimeOffset(r, CreatedOnField),
                    // Pre-existing rows read sprk_ispinned back as null (task 040 DefaultValue does not backfill) —
                    // TryBool returns null for that case, so the ?? false normalizes it to unpinned (task 041 caveat).
                    IsPinned: TryBool(r, PinnedField) ?? false,
                    RegardingEntityType: regType,
                    RegardingId: regId);
            })
            .Where(t => t.ThreadId != Guid.Empty)
            .ToList();

        var hasMore = items.Count > pageSize;
        if (hasMore)
            items = items.Take(pageSize).ToList();

        // A next cursor is only meaningful when there IS a further page AND the boundary row has a createdon; the
        // cursor is the COMPOSITE (createdon, threadId) of the last kept row so the next page resumes losslessly.
        var nextToken = hasMore && items.Count > 0 && items[^1].CreatedOn is { } last
            ? EncodePageToken(last, items[^1].ThreadId)
            : null;
        // If we could not mint a cursor (missing createdon), do not claim a further page the caller cannot fetch.
        hasMore = nextToken is not null;

        _logger.LogDebug(
            "[LIST-THREADS] caller={Caller} search={HasSearch} returned={Returned} hasMore={HasMore} (impersonated set)",
            callerSystemUserId, !string.IsNullOrWhiteSpace(search), items.Count, hasMore);

        return new ThreadListResult(items, items.Count, nextToken, hasMore);
    }

    private static int NormalizeThreadListPageSize(int? top)
        => top is null or <= 0 ? DefaultThreadListPage : Math.Min(top.Value, MaxThreadListPage);

    /// <summary>The composite keyset cursor for list-all-threads paging: the last kept row's ordering tuple.</summary>
    private readonly record struct ThreadPageCursor(DateTimeOffset CreatedOn, Guid ThreadId);

    /// <summary>
    /// Opaque base64 keyset cursor over a thread's ordering tuple (<c>createdon</c> round-trip-precise UTC +
    /// <c>sprk_communicationthreadid</c>). The id disambiguates rows sharing the same second-granular createdon so
    /// the next page resumes without dropping or duplicating a tied row.
    /// </summary>
    private static string EncodePageToken(DateTimeOffset createdOn, Guid threadId)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{FormatOData(createdOn)}|{threadId:D}"));

    /// <summary>
    /// Decodes an opaque paging cursor back into the composite <c>(createdon, threadId)</c> lower bound. Null/blank →
    /// null (first page). A non-decodable/malformed/incomplete token is a 400 ProblemDetails (ADR-019) — never a
    /// silent full-list dump.
    /// </summary>
    private static ThreadPageCursor? DecodePageToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var separator = decoded.IndexOf('|');
            if (separator > 0
                && DateTimeOffset.TryParse(
                    decoded[..separator], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdOn)
                && Guid.TryParse(decoded[(separator + 1)..], out var threadId)
                && threadId != Guid.Empty)
            {
                return new ThreadPageCursor(createdOn, threadId);
            }
        }
        catch (FormatException)
        {
            // fall through to the 400 below
        }

        throw BadRequest("'pageToken' is not a valid pagination cursor.");
    }

    // ── filtered query (R2 task 011 / FR-02) ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a flat, access-filtered communication list matching the thread/regarding/channel/date facets, in the
    /// R1 <see cref="ThreadMessageDto"/> shape. Every facet composes onto the SAME impersonation read path +
    /// <see cref="ICommunicationAccessFilter"/> as <see cref="ReadByRegardingAsync"/> — no second filter, no
    /// membership-union (NFR-03). Facet → column: <c>thread</c> → <c>_sprk_communicationthread_value</c>;
    /// <c>regarding</c> (<c>{entityType}:{guid}</c>) → the typed <see cref="RegardingFieldMap"/> lookup;
    /// <c>channel</c> → <c>sprk_communicationtype</c>; <c>from</c>/<c>to</c> → <c>sprk_sentat</c> range.
    /// <para><b><c>participant</c> (R2 task 051)</b> joins the <c>sprk_communicationparticipant</c> junction (003,
    /// populated at message grain by 050) to find the messages where the given person/address participates in ANY
    /// role (From/To/Cc/Bcc). A GUID value matches the junction's typed <c>sprk_systemuser</c>/<c>sprk_contact</c>
    /// lookups — exact, FK-backed, role-precise (NOT a <c>sprk_from/to/cc</c> text-LIKE scan). A non-GUID value is
    /// matched as an unresolved external address via an EXACT-equality clause on the junction's dedicated
    /// <c>sprk_addresstext</c> column (still not a LIKE scan of the message text fields). The junction match only
    /// yields CANDIDATE message ids — those ids are then run through the SAME impersonated
    /// <c>sprk_communication</c> read + <see cref="ICommunicationAccessFilter"/> as every other facet
    /// (<see cref="QueryVisibleMessagesAsync"/>), so a candidate the caller cannot see is silently absent from the
    /// final result (no second/divergent access mechanism, no leak — NFR-03). No match at all degrades to an
    /// always-false clause rather than an error, so <c>participant=</c> composes as AND with the other facets like
    /// any other facet.</para>
    /// Graceful degradation (ADR-019): a malformed thread/regarding/channel/date/participant, an unknown regarding
    /// entity type, or no facet at all is a 400 ProblemDetails — never a 500 and never an unfiltered dump.
    /// </summary>
    public async Task<CommunicationQueryResult> QueryCommunicationsAsync(
        string? thread,
        string? regarding,
        string? channel,
        string? from,
        string? to,
        string? participant,
        ClaimsPrincipal? caller,
        CancellationToken ct)
    {
        var clauses = new List<string>();

        if (!string.IsNullOrWhiteSpace(thread))
        {
            if (!Guid.TryParse(thread, out var threadId) || threadId == Guid.Empty)
                throw BadRequest("'thread' must be a communication-thread GUID.");
            clauses.Add($"{ThreadLookupValue} eq {threadId}");
        }

        if (!string.IsNullOrWhiteSpace(regarding))
            clauses.Add(BuildRegardingClause(regarding));

        if (!string.IsNullOrWhiteSpace(channel))
        {
            if (!int.TryParse(channel, NumberStyles.Integer, CultureInfo.InvariantCulture, out var channelValue))
                throw BadRequest("'channel' must be an sprk_communicationtype option-set integer.");
            clauses.Add($"{TypeField} eq {channelValue}");
        }

        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!TryParseIso(from, out var fromValue))
                throw BadRequest("'from' must be an ISO-8601 timestamp (e.g. 2026-07-19T00:00:00Z).");
            clauses.Add($"{SentAtField} ge {FormatOData(fromValue)}");
        }

        if (!string.IsNullOrWhiteSpace(to))
        {
            if (!TryParseIso(to, out var toValue))
                throw BadRequest("'to' must be an ISO-8601 timestamp (e.g. 2026-07-19T23:59:59Z).");
            clauses.Add($"{SentAtField} le {FormatOData(toValue)}");
        }

        // No facet at all → 400 (never an unfiltered dump of every communication the caller can see).
        if (clauses.Count == 0 && string.IsNullOrWhiteSpace(participant))
            throw BadRequest("At least one filter is required (thread, regarding, channel, from, to, or participant).");

        var callerSystemUserId = await ResolveCallerOrThrowAsync(caller, ct);

        if (!string.IsNullOrWhiteSpace(participant))
            clauses.Add(await BuildParticipantClauseAsync(participant, callerSystemUserId, ct));

        var filter = string.Join(" and ", clauses);
        var visible = await QueryVisibleMessagesAsync(filter, MaxQueryScan, callerSystemUserId, ct);
        var messages = visible.Select(v => v.Dto).ToList();

        _logger.LogDebug(
            "[COMM-QUERY] caller={Caller} facets=[{Facets}] returned={Returned}",
            callerSystemUserId, string.Join(',', clauses), messages.Count);

        return new CommunicationQueryResult(messages, messages.Count);
    }

    /// <summary>
    /// Parses a <c>regarding={entityType}:{guid}</c> facet into a typed <see cref="RegardingFieldMap"/> lookup clause
    /// on <c>sprk_communication</c> (entity-set-agnostic). A malformed shape or an unmapped entity type is a 400.
    /// </summary>
    private static string BuildRegardingClause(string regarding)
    {
        var separator = regarding.IndexOf(':');
        if (separator <= 0 || separator == regarding.Length - 1)
            throw BadRequest("'regarding' must be '{entityType}:{guid}' (e.g. sprk_matter:<guid>).");

        var entityType = regarding[..separator].Trim();
        var idText = regarding[(separator + 1)..].Trim();

        var regardingField = RegardingFieldMap.FieldFor(entityType);
        if (regardingField is null)
            throw BadRequest($"'{entityType}' is not a supported regarding entity type (ADR-024 family).");

        if (!Guid.TryParse(idText, out var recordId) || recordId == Guid.Empty)
            throw BadRequest("'regarding' record id must be a GUID.");

        return $"_{regardingField}_value eq {recordId}";
    }

    /// <summary>
    /// Resolves <c>participant={personId|address}</c> (R2 task 051) into an OData clause over
    /// <c>sprk_communication</c>'s primary key. Joins the <c>sprk_communicationparticipant</c> junction — using the
    /// SAME impersonated <see cref="IImpersonatedCommunicationQuery"/> seam as every other facet, so no second
    /// access mechanism is introduced — to find the candidate message ids, then builds an OR-of-ids clause over
    /// <see cref="PkField"/>. This is deliberately NOT the access gate: the junction is a thin, exact-match lookup
    /// (typed FK for a resolved person; exact-equality on <see cref="ParticipantAddressTextField"/> for an
    /// unresolved external address — never a text-LIKE scan of <c>sprk_from/to/cc</c>). The REAL gate remains
    /// <see cref="QueryVisibleMessagesAsync"/>'s impersonated <c>sprk_communication</c> read + the shared
    /// <see cref="ICommunicationAccessFilter"/> — a candidate id the caller cannot see there is silently dropped
    /// (no leak). A malformed value (not a GUID and too long to be an address) is a 400; no candidate match
    /// degrades to an always-false clause so the caller sees an empty result, never an error.
    /// </summary>
    private async Task<string> BuildParticipantClauseAsync(string participant, Guid callerSystemUserId, CancellationToken ct)
    {
        var trimmed = participant.Trim();

        string junctionFilter;
        if (Guid.TryParse(trimmed, out var personId))
        {
            if (personId == Guid.Empty)
                throw BadRequest("'participant' must be a non-empty systemuser/contact GUID, or an address.");

            // Resolved-person case: role-exact, FK-backed — match either typed lookup, in ANY role.
            junctionFilter = $"({ParticipantSystemUserValue} eq {personId} or {ParticipantContactValue} eq {personId})";
        }
        else
        {
            if (trimmed.Length > MaxParticipantAddressLength)
                throw BadRequest($"'participant' address must be at most {MaxParticipantAddressLength} characters.");

            // Unresolved-address case: EXACT equality on the junction's dedicated address column (Q-D) — never a
            // text-LIKE scan of sprk_from/to/cc on the message itself.
            junctionFilter = $"{ParticipantAddressTextField} eq '{EscapeODataString(trimmed)}'";
        }

        var odata = $"$select={ParticipantCommunicationValue}&$filter={junctionFilter}&$top={MaxParticipantScan}";
        var rows = await _query.QueryAsync(ParticipantSet, odata, callerSystemUserId, ct);

        var messageIds = rows
            .Select(r => TryGuid(r, ParticipantCommunicationValue))
            .Where(id => id is { } g && g != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (messageIds.Count == 0)
        {
            // No participant match — an always-false clause so the AND-composed result is gracefully empty
            // (never an error, never silently ignored as "no filter").
            return $"{PkField} eq {Guid.Empty}";
        }

        return "(" + string.Join(" or ", messageIds.Select(id => $"{PkField} eq {id}")) + ")";
    }

    private static string EscapeODataString(string value) => value.Replace("'", "''");

    // ── shared read pipeline (impersonated query → shared access filter → DTO, with attachments) ──────

    /// <summary>
    /// Runs <paramref name="odataFilter"/> against <c>sprk_communication</c> IMPERSONATED, applies the SHARED
    /// <see cref="ICommunicationAccessFilter"/> (internal-only + privilege), loads attachments in ONE bulk
    /// impersonated query for the visible page, and projects each visible row into a <see cref="ThreadMessageDto"/>
    /// tagged with its owning thread id. This is the single pipeline both the by-regarding read (010) and the
    /// filtered query (011) compose onto — there is exactly ONE access filter and ONE impersonation seam.
    /// </summary>
    private async Task<IReadOnlyList<VisibleMessage>> QueryVisibleMessagesAsync(
        string odataFilter,
        int top,
        Guid callerSystemUserId,
        CancellationToken ct)
    {
        var select = string.Join(',', new[]
        {
            PkField, BodyField, BodyFormatField, TypeField, FromField, SubjectField, ToField,
            // sprk_sentbyname is NOT selected — broken in the env (IsValidODataAttribute=false) → 400 → 500 on the
            // by-regarding + filtered-query paths. Sender name comes from the _sprk_sentby_value FormattedValue
            // annotation instead (see ReadThreadAsync + ParseMessageRow) — messaging-r3 2026-07-22.
            DirectionField, SentByValue,
            SentAtField, CreatedOnField, InReplyToField, InternalOnlyField, PrivilegeField, IsPrivateField, ThreadLookupValue,
        });
        // Active-only (round-8): deactivated (soft-deleted) messages must drop out of every by-regarding / filtered
        // read that composes onto this shared pipeline. Wrap the caller filter so the AND binds correctly.
        var odata = $"$select={select}&$filter=({odataFilter}) and {ActiveOnlyClause}&$orderby={CreatedOnField} asc&$top={top}";
        var rows = await _query.QueryAsync(CommunicationSet, odata, callerSystemUserId, ct);

        // #675 / ISS-006: authoritative per-caller internal/external bit (fail-closed external) on the SHARED read
        // pipeline that both the by-regarding read (010) and the filtered query (011) compose onto — so an
        // external-licensed caller cannot read internal-only (D-05) messages via ANY read path, not just thread-read.
        var parsed = rows.Select(ParseMessageRow).ToList();
        var isExternal = await _identityResolver.IsExternalAsync(callerSystemUserId, ct);
        var context = new CommunicationAccessContext(CallerSystemUserId: callerSystemUserId, IsInternalUser: !isExternal);
        var filtered = _accessFilter.FilterMessages(context, parsed.Select(p => p.Entity).ToList());

        var byId = parsed.ToDictionary(p => p.MessageId);
        var visible = filtered.Decisions
            .Where(d => d.Decision.IsVisible)
            .Select(d => (Parsed: byId[d.Message.Id], d.Decision))
            .ToList();

        var attachmentsByMessage = await LoadAttachmentsAsync(
            visible.Select(v => v.Parsed.MessageId).ToList(), callerSystemUserId, ct);

        return visible
            .Select(v => new VisibleMessage(v.Parsed.ThreadId, BuildDto(v.Parsed, v.Decision, attachmentsByMessage)))
            .ToList();
    }

    private static ThreadMessageDto BuildDto(
        ParsedMessage parsed,
        CommunicationAccessDecision decision,
        IReadOnlyDictionary<Guid, IReadOnlyList<ThreadAttachmentRef>> attachmentsByMessage)
        => new(
            MessageId: parsed.MessageId,
            Body: parsed.Body,
            BodyFormat: parsed.BodyFormat,
            CommunicationType: parsed.CommunicationType,
            From: parsed.From,
            Subject: parsed.Subject,
            To: parsed.To,
            Direction: parsed.Direction,
            SentBy: parsed.SentBy,
            SentByName: parsed.SentByName,
            SentAt: parsed.SentAt,
            CreatedOn: parsed.CreatedOn,
            InReplyTo: parsed.InReplyTo,
            Privilege: (int)decision.Privilege,
            // FR-21 markers ride the SAME impersonated + access-filtered row as every other field: a row the caller
            // may not see is absent from this projection entirely (no over-disclosure). IsInternalOnly is only ever
            // true here for a permitted (internal) caller (the filter drops it for external callers); IsPrivate is
            // display-only metadata that never gated the read.
            IsInternalOnly: parsed.IsInternalOnly,
            IsPrivate: parsed.IsPrivate,
            Attachments: attachmentsByMessage.TryGetValue(parsed.MessageId, out var atts)
                ? atts
                : Array.Empty<ThreadAttachmentRef>());

    private sealed record VisibleMessage(Guid ThreadId, ThreadMessageDto Dto);

    private static SdapProblemException BadRequest(string detail) =>
        new(code: "VALIDATION_ERROR", title: "Validation Error", detail: detail, statusCode: 400);

    private static bool TryParseIso(string value, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed);

    // ── caller resolution (fail-closed) ─────────────────────────────────────────────────────────────

    private async Task<Guid> ResolveCallerOrThrowAsync(ClaimsPrincipal? caller, CancellationToken ct)
    {
        var resolution = await _callerResolver.ResolveAsync(caller, ct);
        if (!resolution.IsResolved
            || !Guid.TryParse(resolution.SystemUserId, out var systemUserId)
            || systemUserId == Guid.Empty)
        {
            _logger.LogWarning(
                "[THREAD-READ] caller has no resolvable Dataverse systemuserid ({Reason}) — refusing the read (fail closed; no app-only fallback).",
                resolution.UnresolvedReason ?? "unresolved");
            throw new SdapProblemException(
                code: "THREAD_READ_FORBIDDEN",
                title: "Forbidden",
                detail: "The caller could not be resolved to a Dataverse user, so the messages cannot be read.",
                statusCode: 403);
        }

        return systemUserId;
    }

    // ── thread label (single bounded impersonated projection) ───────────────────────────────────────

    /// <summary>
    /// Reads the thread's <c>sprk_name</c> via a single IMPERSONATED projection on <c>sprk_communicationthread</c>
    /// by id (R3 task 002 / FR-18). One bounded O(1) query (not a per-row fan-out — NFR-07 preserved). Because it
    /// is impersonated, a caller who cannot see the thread record gets <c>null</c> back rather than the name —
    /// fail closed, no existence leak (NFR-06).
    /// </summary>
    private async Task<string?> ReadThreadNameAsync(Guid threadId, Guid callerSystemUserId, CancellationToken ct)
    {
        var odata = $"$select={ThreadNameField}&$filter={ThreadPkField} eq {threadId}&$top=1";
        var rows = await _query.QueryAsync(ThreadSet, odata, callerSystemUserId, ct);
        return rows.Count > 0 ? TryString(rows[0], ThreadNameField) : null;
    }

    // ── rename authorization (single bounded impersonated existence check) ──────────────────────────

    /// <summary>
    /// FR-17 rename authorization (task 004): returns <c>true</c> iff <paramref name="caller"/> may SEE the thread
    /// record, via a single IMPERSONATED existence projection on <c>sprk_communicationthread</c> by id (Dataverse
    /// row-level security is the ONLY gate — ownership, role depth, BU, teams, sharing, hierarchy). The rename
    /// endpoint uses this to refuse (403) a rename of a thread the caller cannot see — a caller MUST NOT rename a
    /// thread they cannot see (ADR-028 / NFR-01). Because it is impersonated, a caller with no read access simply
    /// gets zero rows (no existence leak — NFR-06). Fail-closed: an unresolved caller throws 403 (no app-only
    /// fallback that would widen access).
    /// </summary>
    public async Task<bool> CanCallerSeeThreadAsync(Guid threadId, ClaimsPrincipal? caller, CancellationToken ct)
    {
        var callerSystemUserId = await ResolveCallerOrThrowAsync(caller, ct);
        var odata = $"$select={ThreadPkField}&$filter={ThreadPkField} eq {threadId}&$top=1";
        var rows = await _query.QueryAsync(ThreadSet, odata, callerSystemUserId, ct);
        return rows.Count > 0;
    }

    /// <summary>
    /// Round-7 item 8 authorization gate: returns true only if the impersonated caller can SEE the single message
    /// (<c>sprk_communication</c>). Mirrors <see cref="CanCallerSeeThreadAsync"/> — an impersonated top-1 existence
    /// probe (MSCRMCallerID = caller). A message the caller cannot read returns zero rows → false → the delete
    /// endpoint fails closed with a 403 (NFR-01: never deactivate a message the caller cannot see).
    /// </summary>
    public async Task<bool> CanCallerSeeMessageAsync(Guid communicationId, ClaimsPrincipal? caller, CancellationToken ct)
    {
        var callerSystemUserId = await ResolveCallerOrThrowAsync(caller, ct);
        var odata = $"$select={PkField}&$filter={PkField} eq {communicationId}&$top=1";
        var rows = await _query.QueryAsync(CommunicationSet, odata, callerSystemUserId, ct);
        return rows.Count > 0;
    }

    // ── attachments (single bulk query per read) ────────────────────────────────────────────────────

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ThreadAttachmentRef>>> LoadAttachmentsAsync(
        IReadOnlyList<Guid> messageIds,
        Guid callerSystemUserId,
        CancellationToken ct)
    {
        if (messageIds.Count == 0)
            return EmptyAttachments;

        var select = string.Join(',', new[]
        {
            AttachmentPkField, AttachmentNameField, AttachmentTypeField,
            AttachmentDocumentValue, AttachmentCommunicationValue,
        });
        var orClause = string.Join(" or ", messageIds.Select(id => $"{AttachmentCommunicationValue} eq {id}"));
        var odata = $"$select={select}&$filter={orClause}";

        var rows = await _query.QueryAsync(CommunicationAttachmentSet, odata, callerSystemUserId, ct);

        var map = new Dictionary<Guid, List<ThreadAttachmentRef>>();
        foreach (var row in rows)
        {
            var messageId = TryGuid(row, AttachmentCommunicationValue);
            if (messageId is null)
                continue;

            var reference = new ThreadAttachmentRef(
                CommunicationAttachmentId: TryGuid(row, AttachmentPkField) ?? Guid.Empty,
                DocumentId: TryGuid(row, AttachmentDocumentValue),
                FileName: TryString(row, AttachmentNameField),
                AttachmentType: TryInt(row, AttachmentTypeField));

            if (!map.TryGetValue(messageId.Value, out var list))
                map[messageId.Value] = list = new List<ThreadAttachmentRef>();
            list.Add(reference);
        }

        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ThreadAttachmentRef>)kv.Value);
    }

    private static readonly IReadOnlyDictionary<Guid, IReadOnlyList<ThreadAttachmentRef>> EmptyAttachments =
        new Dictionary<Guid, IReadOnlyList<ThreadAttachmentRef>>();

    // ── row parsing ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses one impersonated OData row into the projected values AND a minimal <see cref="Entity"/> carrying only
    /// the two fields the <see cref="ICommunicationAccessFilter"/> reads (<c>sprk_isinternalonly</c> +
    /// <c>sprk_privilegeclassification</c>), so the shared filter runs unchanged over the read set.
    /// </summary>
    private static ParsedMessage ParseMessageRow(Dictionary<string, JsonElement> row)
    {
        var messageId = TryGuid(row, PkField) ?? Guid.Empty;
        var isInternalOnly = TryBool(row, InternalOnlyField);
        var isPrivate = TryBool(row, IsPrivateField);
        var privilege = TryInt(row, PrivilegeField);

        var entity = new Entity("sprk_communication", messageId);
        if (isInternalOnly.HasValue)
            entity[InternalOnlyField] = isInternalOnly.Value;
        if (privilege.HasValue)
            entity[PrivilegeField] = new OptionSetValue(privilege.Value);

        return new ParsedMessage
        {
            MessageId = messageId,
            // FR-21 marker projection. Default false when the column is unset on a VISIBLE row — a display default,
            // not an access decision (the internal-only ACCESS gate is the access filter's fail-closed job, not this
            // label). IsPrivate never gates; impersonation enforces private-thread visibility.
            IsInternalOnly = isInternalOnly ?? false,
            IsPrivate = isPrivate ?? false,
            ThreadId = TryGuid(row, ThreadLookupValue) ?? Guid.Empty,
            Body = TryString(row, BodyField),
            BodyFormat = TryInt(row, BodyFormatField),
            CommunicationType = TryInt(row, TypeField),
            From = TryString(row, FromField),
            Subject = TryString(row, SubjectField),
            To = SplitRecipients(TryString(row, ToField)),
            Direction = TryInt(row, DirectionField),
            SentBy = TryGuid(row, SentByValue),
            SentByName = TryString(row, SentByFormattedValue),
            SentAt = TryDateTimeOffset(row, SentAtField),
            CreatedOn = TryDateTimeOffset(row, CreatedOnField),
            InReplyTo = TryString(row, InReplyToField),
            Entity = entity,
        };
    }

    private sealed class ParsedMessage
    {
        public required Guid MessageId { get; init; }
        public Guid ThreadId { get; init; }
        public bool IsInternalOnly { get; init; }
        public bool IsPrivate { get; init; }
        public string? Body { get; init; }
        public int? BodyFormat { get; init; }
        public int? CommunicationType { get; init; }
        public string? From { get; init; }
        public string? Subject { get; init; }
        public IReadOnlyList<string> To { get; init; } = Array.Empty<string>();
        public int? Direction { get; init; }
        public Guid? SentBy { get; init; }
        public string? SentByName { get; init; }
        public DateTimeOffset? SentAt { get; init; }
        public DateTimeOffset? CreatedOn { get; init; }
        public string? InReplyTo { get; init; }
        public required Entity Entity { get; init; }
    }

    // ── OData / JSON helpers ────────────────────────────────────────────────────────────────────────

    private static int NormalizePageSize(int? top)
        => top is null or <= 0 ? DefaultPageSize : Math.Min(top.Value, MaxPageSize);

    /// <summary>
    /// Splits the <c>sprk_to</c> recipient field into individual addresses. The send path stores it as
    /// <c>string.Join("; ", To)</c> (see <c>CommunicationService</c>), so split on ';' and trim; empty/whitespace
    /// entries are dropped. Returns an empty list for null/blank (never null).
    /// </summary>
    private static IReadOnlyList<string> SplitRecipients(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();
        return value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    /// <summary>OData datetimeoffset literal (unquoted, UTC) for a <c>gt</c> comparison on <c>createdon</c>.</summary>
    private static string FormatOData(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string? TryString(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? TryInt(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static bool? TryBool(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static Guid? TryGuid(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) ? g : null;

    private static DateTimeOffset? TryDateTimeOffset(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var dto) ? dto : null;

    // round-8.4 item 3: resolve a thread row's associated ("regarding") record from the typed ADR-024 lookups, in
    // RegardingFieldMap priority order — the first non-empty `_{field}_value` wins. (null, null) for a record-less
    // Direct thread. Mirrors CommunicationArrivedProducer.ResolveTypedRegarding, but over the raw OData row shape.
    private static (string? EntityType, Guid? Id) ResolveRegardingFromRow(Dictionary<string, JsonElement> row)
    {
        foreach (var (entityLogicalName, field) in ThreadRegardingFields)
        {
            if (TryGuid(row, $"_{field}_value") is { } id && id != Guid.Empty)
            {
                return (entityLogicalName, id);
            }
        }

        return (null, null);
    }
}
