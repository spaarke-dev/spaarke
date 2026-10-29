using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
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
    // External-plane status allow-list (task 033 review F1). A message reaches an external reader ONLY when its Status
    // Reason says it was actually sent or received: Send (659490002) and Delivered (659490003 — also what every inbound
    // ingest path stamps). Draft/Queued/Failed/Bounded/Deleted/Recalled never qualify. An ALLOW-list, so a status added
    // later is hidden from the external plane until it is consciously admitted here. Applies to the app-only read only.
    private const string StatusCodeAttribute = "statuscode";
    private const int StatusSend = (int)CommunicationStatus.Send;
    private const int StatusDelivered = (int)CommunicationStatus.Delivered;
    private static readonly string ExternalStatusClause =
        $"({StatusCodeAttribute} eq {StatusSend} or {StatusCodeAttribute} eq {StatusDelivered})";
    // External-plane privilege rule (owner decision 2026-10-11, task 033 follow-up): an external caller NEVER receives a
    // message classified Privileged OR Potentially Privileged; None and null are shown. THIS list is the one place the
    // hidden set is named - relaxing the rule to "Privileged only" is deleting PotentiallyPrivileged from it. The OData
    // clause the external query adds and the per-row re-check are both derived from it. Applies to the app-only external
    // read only: the impersonated internal reads still treat privilege as display metadata (ADR-015, owner decision
    // 2026-07-16) and are unchanged.

    /// <summary>
    /// The privilege classifications an external caller never receives. Hide-only, and a classification not in the
    /// defined set is hidden too (<see cref="IsHiddenFromExternal"/>): fail closed.
    /// </summary>
    public static readonly IReadOnlyList<CommunicationPrivilegeClassification> ExternallyHiddenPrivilege =
        new[] { CommunicationPrivilegeClassification.PotentiallyPrivileged, CommunicationPrivilegeClassification.Privileged };

    /// <summary>
    /// The <c>$filter</c> clause every EXTERNAL message query must carry, so the privilege rule is applied by Dataverse
    /// BEFORE paging (a page cut after filtering would otherwise be short): a null classification, or one outside
    /// <see cref="ExternallyHiddenPrivilege"/>. Derived from that list. Any other external read of
    /// <c>sprk_communication</c> (a notification feed, a count, a "latest") must add it.
    /// </summary>
    public static readonly string ExternalPrivilegeFilterClause =
        $"({PrivilegeField} eq null or ("
        + string.Join(" and ", ExternallyHiddenPrivilege.Select(p => $"{PrivilegeField} ne {(int)p}"))
        + "))";

    /// <summary>
    /// Whether an external caller must not receive a message with this raw <c>sprk_privilegeclassification</c> value:
    /// it is in <see cref="ExternallyHiddenPrivilege"/>, or it is a value this code does not define (an unreadable
    /// classification is hidden, not shown). Null and None are shown.
    /// </summary>
    public static bool IsHiddenFromExternal(int? classification) =>
        classification is { } value
        && (ExternallyHiddenPrivilege.Contains((CommunicationPrivilegeClassification)value)
            || !Enum.IsDefined(typeof(CommunicationPrivilegeClassification), value));

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
    // spaarke-SPA-external-access-platform-r3 task 034 (FR-11/FR-25): a CONTACT's external thread post has no sprk_sentby
    // (a systemuser lookup); its author is the sprk_createdbycontact stamp. Selected ONLY by the app-only external read
    // (ReadRegardingAppOnlyAsync) — the column is task 020's, so the internal reads do not depend on it. Its FormattedValue
    // (the contact's name) is the sender name when sprk_sentby is unset.
    private const string CreatedByContactValue = "_sprk_createdbycontact_value";
    private const string CreatedByContactFormattedValue = "_sprk_createdbycontact_value@OData.Community.Display.V1.FormattedValue";
    // spaarke-SPA-external-access-platform-r3 task 062 (owner check-2 issue 4): a message created in the model-driven app has
    // neither sprk_sentby nor a creator contact, so the sender name was null ("Unknown sender", bell "Someone"). The row's
    // createdby (the creating systemuser) FormattedValue is the last fallback: sprk_sentby name -> creator contact name ->
    // createdby name -> null. Only the display name is projected (the annotation); the id is selected solely so Dataverse
    // returns the annotation and is never mapped to a DTO field.
    // Person-only (owner decision 2026-10-13): the BFF creates communication rows through the APP identity, so for a BFF-sent
    // message createdby is the BFF's application user and its name must never be shown as the sender. The createdby name is
    // used only when createdby is a real person: the same read expands createdby to its applicationid + accessmode (no extra
    // round trip) and CreatedByIsAPerson requires no applicationid and an interactive access mode. Anything that cannot be
    // classified is NOT a person (null name). Same rule as RecordContainerResolver.ReadRowCreatorAsync (person = no
    // applicationid) and SecureBuRoleDepthCensusBuilder.IsHumanPrincipal (access mode 3/4 are not people).
    private const string CreatedByValue = "_createdby_value";
    private const string CreatedByFormattedValue = "_createdby_value@OData.Community.Display.V1.FormattedValue";
    private const string CreatedByNavigation = "createdby";
    private const string CreatedByApplicationIdColumn = "applicationid";
    private const string CreatedByAccessModeColumn = "accessmode";
    private const string CreatedByExpand =
        "$expand=" + CreatedByNavigation + "(" + "$select=" + CreatedByApplicationIdColumn + "," + CreatedByAccessModeColumn + ")";
    private const int AccessModeSupportUser = 3;
    private const int AccessModeNonInteractive = 4;

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
    private readonly IAppOnlyCommunicationQuery? _appOnlyQuery;
    private readonly ICommunicationAccessFilter _accessFilter;
    private readonly ICallerSystemUserResolver _callerResolver;
    private readonly ISystemUserIdentityResolver _identityResolver;
    private readonly ILogger<CommunicationThreadReadService> _logger;

    public CommunicationThreadReadService(
        IImpersonatedCommunicationQuery query,
        ICommunicationAccessFilter accessFilter,
        ICallerSystemUserResolver callerResolver,
        ISystemUserIdentityResolver identityResolver,
        ILogger<CommunicationThreadReadService> logger,
        IAppOnlyCommunicationQuery? appOnlyQuery = null)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        // Optional so the impersonated-only callers and their tests construct the service unchanged. Only
        // ReadRegardingAppOnlyAsync uses it, and it refuses (throws) when the seam is absent — never an impersonated or
        // unscoped substitute. CommunicationModule registers it, so DI always supplies it.
        _appOnlyQuery = appOnlyQuery;
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

        // #675 / ISS-006: the internal-vs-external bit is the AUTHORITATIVE per-caller value (systemuser.sprk_isexternal
        // via the shared resolver), NOT a hardcoded `true`. IsExternalAsync fails closed (external) on an unresolvable id.
        var isExternal = await _identityResolver.IsExternalAsync(callerSystemUserId, ct);

        // D7 (task 065 fix round 5): a partner sees nothing in a thread that is not explicitly Open at read time. Checked
        // before the message read; the answer is the same empty result as "no visible messages" (no existence leak).
        if (isExternal && !await IsThreadOpenForExternalAsync(threadId, callerSystemUserId, ct))
            return new ThreadReadResult(ThreadId: threadId, Name: null, Messages: Array.Empty<ThreadMessageDto>(), Count: 0);

        // 1) Impersonated message read — Dataverse row-level security applies natively.
        var select = string.Join(',', new[]
        {
            PkField, BodyField, BodyFormatField, TypeField, FromField, SubjectField, ToField,
            // sprk_sentbyname is NOT selected — it is broken in the env (IsValidODataAttribute=false) and 400s the
            // whole read. The sender display name instead comes from the _sprk_sentby_value lookup's FormattedValue
            // annotation (the impersonated query requests annotations; ParseMessageRow reads it) — messaging-r3 2026-07-22.
            DirectionField, SentByValue, CreatedByValue,
            SentAtField, CreatedOnField, InReplyToField, InternalOnlyField, PrivilegeField, IsPrivateField,
        });
        var filter = new StringBuilder($"{ThreadLookupValue} eq {threadId} and {ActiveOnlyClause}");
        if (since is { } s)
            filter.Append($" and {CreatedOnField} gt {FormatOData(s)}");

        var odata = $"$select={select}&{CreatedByExpand}&$filter={filter}&$orderby={CreatedOnField} asc&$top={pageSize}";
        var rows = await _query.QueryAsync(CommunicationSet, odata, callerSystemUserId, ct);

        // 2) Apply the SHARED internal-only + privilege filter on top of the already-scoped rows. Hardcoding internal made
        // CommunicationAccessFilter treat every caller as internal, so an external-licensed systemuser could read
        // internal-only (D-05) messages (over-disclosure); the per-caller bit above is what prevents that.
        var parsed = rows.Select(ParseMessageRow).ToList();
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

        // #675 / ISS-006: authoritative per-caller internal/external bit (fail-closed external) — an unread scan must
        // NOT count internal-only messages for an external-licensed caller (mirrors the thread-read filter).
        var isExternal = await _identityResolver.IsExternalAsync(callerSystemUserId, ct);

        // D7 (task 065 fix round 5): a count never includes an item the list hides. A thread that is not explicitly Open
        // is hidden from a partner entirely, so its unread count is zero.
        if (isExternal && !await IsThreadOpenForExternalAsync(threadId, callerSystemUserId, ct))
            return new UnreadCountResult(threadId, since, 0);

        var select = string.Join(',', new[] { PkField, InternalOnlyField, PrivilegeField });
        var filter = new StringBuilder($"{ThreadLookupValue} eq {threadId} and {ActiveOnlyClause}");
        if (since is { } s)
            filter.Append($" and {CreatedOnField} gt {FormatOData(s)}");

        var odata = $"$select={select}&$filter={filter}&$top={MaxUnreadScan}";
        var rows = await _query.QueryAsync(CommunicationSet, odata, callerSystemUserId, ct);

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
        // Fix round 6 (F1): an external caller (a contact, or a systemuser flagged sprk_isexternal) lists only EXPLICITLY Open
        // threads, in the query itself, so a Private thread's name never leaves Dataverse. Internal callers are unchanged.
        var openOnly = await _identityResolver.IsExternalAsync(callerSystemUserId, ct)
            ? $" and {CommunicationOpenThreadGate.OpenOnlyThreadFilterClause}"
            : string.Empty;
        var threadOData =
            $"$select={threadSelect}&$filter=_{regardingField}_value eq {recordId} and {ActiveOnlyClause}{openOnly}" +
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

    // ── app-only by-regarding read for the EXTERNAL plane (spaarke-SPA-external-access-platform-r3 task 033 / FR-04) ──

    /// <summary>
    /// The messages filed regarding one record, read APP-ONLY for the external plane, oldest→newest (newest last).
    /// Same row parsing, the same shared <see cref="ICommunicationAccessFilter"/> and the same DTO as the impersonated
    /// reads above; only the query identity differs, because a contact caller has no <c>systemuser</c> to impersonate
    /// and the external plane forbids OBO (ADR-028, NFR-01).
    /// </summary>
    /// <remarks>
    /// <para><b>Record access is the caller's job, and must already be done.</b> This method makes NO record-level
    /// decision: the external endpoint (<c>ExternalMessageEndpoints</c>) checks <paramref name="recordId"/> against the
    /// caller's composed accessible set first, and the query here is keyed on that one record through the typed
    /// <see cref="RegardingFieldMap"/> lookup on <c>sprk_communication</c> (the same column the filtered query's
    /// <c>regarding=</c> facet uses). Every returned row is re-checked: a row whose regarding value is not
    /// <paramref name="recordId"/> is dropped, so a query that drifted cannot widen the result.</para>
    /// <para><b>Internal-only.</b> Shown only to an internal <c>systemuser</c>: <paramref name="callerSystemUserId"/> is
    /// set AND <see cref="ISystemUserIdentityResolver.IsExternalAsync"/> says internal (the same per-caller bit the
    /// impersonated reads use, #675). A contact caller (CIAM or workforce contact) passes <c>null</c> and never receives
    /// an internal-only row; an unreadable flag hides the row (the filter fails closed).</para>
    /// <para><b>Columns.</b> An explicit projection — no attachments, no recipient addresses — so nothing beyond what
    /// the external DTO needs is read. Bounded to <see cref="MaxRegardingScan"/> rows; <c>Truncated</c> says when the
    /// bound was reached with more rows remaining.</para>
    /// </remarks>
    /// <param name="entityType">The regarding entity logical name (an ADR-024 family member, e.g. <c>sprk_matter</c>).</param>
    /// <param name="recordId">The record the caller has already been authorized to read.</param>
    /// <param name="callerSystemUserId">The caller's <c>systemuserid</c> when the caller is a systemuser; <c>null</c> for a contact.</param>
    /// <param name="rootsReadable">
    /// REQUIRED (no default, so a caller cannot forget it): whether the caller may see a message carrying these core-root
    /// stamps. A communication filed under an invoice of secure matter M and project P is stamped with both, so the
    /// keyed lookup alone would list it on P's thread to a reader of P only. Called once with the stamps of every row that
    /// passed the other re-checks; a row it refuses is dropped, not counted as truncation.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<AppOnlyRegardingReadResult> ReadRegardingAppOnlyAsync(
        string entityType,
        Guid recordId,
        Guid? callerSystemUserId,
        MessageRootsFilter rootsReadable,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rootsReadable);
        if (string.IsNullOrWhiteSpace(entityType))
            throw BadRequest("A regarding entity type is required.");

        var regardingField = RegardingFieldMap.FieldFor(entityType);
        if (regardingField is null)
            throw BadRequest($"'{entityType}' is not a supported regarding entity type (ADR-024 family).");

        if (recordId == Guid.Empty)
            throw BadRequest("A regarding record id is required.");

        var appOnlyQuery = _appOnlyQuery
            ?? throw new InvalidOperationException(
                "IAppOnlyCommunicationQuery is not registered; the external-plane message read cannot run (fail closed).");

        var regardingValue = $"_{regardingField}_value";
        var select = ExternalSelect(new[] { regardingValue }, includeContent: true);
        // Newest first with one over-fetched row (detects a cut-short list without a COUNT query): when the record has
        // more rows than the bound, the NEWEST ones are kept and the oldest dropped; the page is reversed to oldest→newest
        // below. Active AND sent/received only (ExternalStatusClause) — a Draft/Queued/Failed/Bounded/Recalled row is
        // never read for the external plane.
        var odata =
            $"$select={select}&{CreatedByExpand}&$filter={regardingValue} eq {recordId} and {ActiveOnlyClause} and {ExternalStatusClause}" +
            $" and {ExternalPrivilegeFilterClause}" +
            $"&$orderby={CreatedOnField} desc,{PkField} desc&$top={MaxRegardingScan + 1}";
        var rows = await appOnlyQuery.QueryAsync(CommunicationSet, odata, ct);

        var truncated = rows.Count > MaxRegardingScan;
        var bounded = rows.Take(MaxRegardingScan).Reverse().ToList();
        // Re-check every row (defence in depth — the query is not the only gate): it must regard THIS record and carry a
        // sent/received status. A row with a missing or unreadable status is dropped (fail closed).
        var candidates = bounded
            .Where(r => TryGuid(r, regardingValue) == recordId && IsExternallyVisibleRow(r))
            .ToList();
        // The multi-root rule, prepared once for every candidate's stamps (one batched secure-flag read, not one per row).
        var readable = await rootsReadable(candidates.Select(RootsOf).ToList(), ct);
        var scoped = candidates.Where(r => readable(RootsOf(r))).ToList();

        var (messages, isInternal) = await FilterExternalMessagesAsync(scoped, callerSystemUserId, ct);

        _logger.LogDebug(
            "[BY-REGARDING-APPONLY] {EntityType}={RecordId} internalCaller={Internal} returned={Returned} " +
            "(read={Read}, dropped-regarding-or-status={Dropped}, truncated={Truncated})",
            entityType, recordId, isInternal, messages.Count, rows.Count, bounded.Count - scoped.Count, truncated);

        return new AppOnlyRegardingReadResult(messages, truncated);
    }

    private const string ProjectStampColumn = "_sprk_regardingproject_value";
    private const string MatterStampColumn = "_sprk_regardingmatter_value";
    private const string WorkAssignmentStampColumn = "_sprk_regardingworkassignment_value";

    private static readonly string[] RootStampColumns = { ProjectStampColumn, MatterStampColumn, WorkAssignmentStampColumn };

    /// <summary>The core-root stamps a row carries (raw lookup values; a column the row lacks, or holds as JSON null, reads
    /// as not filed).</summary>
    private static MessageRootLookups RootsOf(Dictionary<string, JsonElement> row) => new(
        RootStamp(row, ProjectStampColumn), RootStamp(row, MatterStampColumn), RootStamp(row, WorkAssignmentStampColumn),
        // Task 187: the row itself, whose other parents (thread, invoice, event ...) the rule derives and takes in LIVE.
        row);

    /// <summary>The table the external reads select from.</summary>
    private const string CommunicationTable = "sprk_communication";

    /// <summary>Stands for a stamp that is present but not a string; the multi-root rule reads it as an unreadable root.</summary>
    private const string MalformedRootStamp = "malformed";

    /// <summary>
    /// One stamp, fail CLOSED (fix round for r3 task 039, K3): absent or JSON null is "not filed", a string is the lookup, and
    /// any other JSON value (a number, object, array, boolean) is malformed, so the row is withheld, as
    /// <c>DocumentRootLinks</c> and the delete-target parse read the same case.
    /// </summary>
    private static string? RootStamp(Dictionary<string, JsonElement> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : MalformedRootStamp;
    }

    /// <summary>True only for a Status Reason that means the message was actually sent or received (see the allow-list).</summary>
    private static bool IsExternallyReadableStatus(int? statusCode)
        => statusCode is StatusSend or StatusDelivered;

    /// <summary>
    /// The per-row re-check every EXTERNAL read applies after its query (defence in depth — the query is not the only
    /// gate): a sent/received status (a missing or unreadable one is dropped) and a privilege classification an external
    /// caller may receive (<see cref="IsHiddenFromExternal"/>).
    /// </summary>
    private static bool IsExternallyVisibleRow(Dictionary<string, JsonElement> row)
        => IsExternallyReadableStatus(TryInt(row, StatusCodeAttribute)) && !IsHiddenFromExternal(TryInt(row, PrivilegeField));

    /// <summary>
    /// The explicit external projection (no attachments, no recipient addresses) plus the regarding lookups read.
    /// <paramref name="includeContent"/> adds the body, its format and the subject: the task 033 thread read returns them;
    /// the notification feed never uses them, so it projects without (task 037 review K3, least data).
    /// </summary>
    private static string ExternalSelect(IEnumerable<string> regardingValues, bool includeContent)
    {
        // The three core-root stamps are ALWAYS read, whichever record the query is keyed on: they are what lets a row also
        // filed to a root the caller cannot read be withheld (RootsOf). A read that dropped one would list such a row.
        // Task 187 (owner round 99, D-123): and the row's other parent lookups, whose LIVE roots the rule takes in (RootsOf).
        regardingValues = regardingValues.Concat(RootStampColumns)
            .Concat(Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalMultiRootRule.LiveParentSelect(CommunicationTable))
            .Distinct();
        var columns = new List<string> { PkField };
        if (includeContent)
        {
            columns.AddRange(new[] { BodyField, BodyFormatField, SubjectField });
        }

        columns.AddRange(new[]
        {
            DirectionField, SentByValue, CreatedByContactValue, CreatedByValue, CreatedOnField, InternalOnlyField, PrivilegeField,
            StatusCodeAttribute,
            // D7 (task 065 fix round 5): the thread each row sits in, so the open-thread gate can read the distinct threads.
            ThreadLookupValue,
        });
        // Distinct over the whole list: the live-parent lookups include the thread lookup the D7 gate already selects.
        return string.Join(',', columns.Concat(regardingValues).Distinct());
    }

    /// <summary>
    /// The shared tail of every EXTERNAL read: the internal-only decision (an internal systemuser only — the same
    /// <see cref="ISystemUserIdentityResolver.IsExternalAsync"/> bit the impersonated reads use, #675), the shared
    /// <see cref="ICommunicationAccessFilter"/>, the D7 open-thread gate for a non-internal caller (the thread must read
    /// explicitly Open now), and the shared DTO builder. Returns the visible messages in input order.
    /// </summary>
    private async Task<(List<ThreadMessageDto> Messages, bool IsInternal)> FilterExternalMessagesAsync(
        IReadOnlyList<Dictionary<string, JsonElement>> rows, Guid? callerSystemUserId, CancellationToken ct)
    {
        var isInternal = callerSystemUserId is { } systemUserId
            && systemUserId != Guid.Empty
            && !await _identityResolver.IsExternalAsync(systemUserId, ct);
        var context = new CommunicationAccessContext(
            CallerSystemUserId: callerSystemUserId ?? Guid.Empty, IsInternalUser: isInternal);

        var parsed = rows.Select(ParseMessageRow).Where(p => p.MessageId != Guid.Empty).ToList();
        var filtered = _accessFilter.FilterMessages(context, parsed.Select(p => p.Entity).ToList());
        var byId = parsed.ToDictionary(p => p.MessageId);
        var visible = filtered.Decisions
            .Where(d => d.Decision.IsVisible)
            .Select(d => (Parsed: byId[d.Message.Id], d.Decision))
            .ToList();

        // D7 (task 065 fix round 5, ISS-009): a non-internal caller sees an item only when its thread is EXPLICITLY Open at
        // read time, on top of the flag rule above. One batched read of the page's distinct threads; a hidden row is
        // filtered exactly like one the flag hid (it never counts toward truncation). Internal callers are unchanged.
        if (!isInternal && visible.Count > 0)
        {
            var open = await ReadOpenThreadIdsAppOnlyAsync(visible.Select(v => v.Parsed.ThreadId), ct);
            visible = visible.Where(v => open.Contains(v.Parsed.ThreadId)).ToList();
        }

        var messages = visible.Select(v => BuildDto(v.Parsed, v.Decision, EmptyAttachments)).ToList();
        return (messages, isInternal);
    }

    // ── app-only RECENT messages across many records, for the EXTERNAL notification feed (r3 task 037 / FR-08) ──

    /// <summary>Records per feed query: one OR-filter over this many regarding ids keeps the GET URL well inside the limit.</summary>
    internal const int RecentRecordChunkSize = 50;

    /// <summary>
    /// The most records one feed read scans (<see cref="RecentRecordChunkSize"/> × 20 queries). A caller with more is
    /// answered from the first this-many and the result says <c>Truncated</c> — a cap is never silent.
    /// </summary>
    internal const int MaxRecentRecords = 1000;

    /// <summary>
    /// The newest messages filed regarding ANY of <paramref name="records"/> since <paramref name="since"/>, read APP-ONLY
    /// for the external notification feed, newest first, at most <paramref name="max"/>. Every row passes the SAME external
    /// rules as <see cref="ReadRegardingAppOnlyAsync"/>: active and sent/received only, the privilege rule
    /// (<see cref="ExternalPrivilegeFilterClause"/> in the query, <see cref="IsHiddenFromExternal"/> per row), and the
    /// shared internal-only filter (an internal systemuser only).
    /// </summary>
    /// <remarks>
    /// <para><b>Record access is the caller's job, and must already be done.</b> <paramref name="records"/> must be the
    /// caller's accessible set (the feed endpoint passes the principal's Read-bearing roots and its own service requests).
    /// The query is keyed on those records through the typed <see cref="RegardingFieldMap"/> lookups, and every returned row
    /// is re-checked: it is kept only when one of its regarding values IS one of <paramref name="records"/>, and that record
    /// (the first in <paramref name="records"/> order) is the one reported. A row that matches none is dropped.</para>
    /// <para><b>Bounded.</b> At most <see cref="MaxRecentRecords"/> records, queried <see cref="RecentRecordChunkSize"/> at
    /// a time, each query <c>$top</c> = <paramref name="max"/> + 1 newest; merged, newest first, cut to
    /// <paramref name="max"/>. <c>Truncated</c> says a bound cut the answer (more records, or more messages, than read).</para>
    /// </remarks>
    /// <param name="records">The caller's accessible records as (entity logical name, id); unsupported types are ignored.</param>
    /// <param name="since">The window start; only messages created at or after it are read.</param>
    /// <param name="callerSystemUserId">The caller's <c>systemuserid</c> when the caller is a systemuser; <c>null</c> for a contact.</param>
    /// <param name="max">The most messages to return (newest first).</param>
    /// <param name="rootsReadable">
    /// REQUIRED: whether the caller may see a message carrying these core-root stamps (see
    /// <see cref="ReadRegardingAppOnlyAsync"/>). A message filed to one accessible record and also to a root the caller
    /// cannot read is not reported; it is dropped, not counted as truncation.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<AppOnlyRecentReadResult> ReadRecentForRecordsAppOnlyAsync(
        IReadOnlyList<(string EntityType, Guid RecordId)> records,
        DateTimeOffset since,
        Guid? callerSystemUserId,
        int max,
        MessageRootsFilter rootsReadable,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(rootsReadable);
        if (max <= 0)
            throw new ArgumentOutOfRangeException(nameof(max), "The feed read needs a positive bound.");

        var appOnlyQuery = _appOnlyQuery
            ?? throw new InvalidOperationException(
                "IAppOnlyCommunicationQuery is not registered; the external-plane message read cannot run (fail closed).");

        // Supported, non-empty, distinct records, in the caller's order (that order picks the reported record for a row
        // filed under two of them).
        var keyed = records
            .Where(r => r.RecordId != Guid.Empty && RegardingFieldMap.FieldFor(r.EntityType) is not null)
            .Select(r => (Entity: r.EntityType, r.RecordId, Value: $"_{RegardingFieldMap.FieldFor(r.EntityType)}_value"))
            .DistinctBy(r => (r.Value, r.RecordId))
            .ToList();
        var truncated = keyed.Count > MaxRecentRecords;
        if (truncated)
            keyed = keyed.Take(MaxRecentRecords).ToList();

        if (keyed.Count == 0)
            return new AppOnlyRecentReadResult(Array.Empty<RecentRecordMessage>(), truncated);

        var rows = new List<Dictionary<string, JsonElement>>();
        foreach (var chunk in keyed.Chunk(RecentRecordChunkSize))
        {
            var regardingValues = chunk.Select(r => r.Value).Distinct().ToList();
            var idFilter = string.Join(" or ", chunk.Select(r => $"{r.Value} eq {r.RecordId}"));
            var odata =
                $"$select={ExternalSelect(regardingValues, includeContent: false)}&{CreatedByExpand}" +
                $"&$filter=({idFilter}) and {ActiveOnlyClause} and {ExternalStatusClause} and {ExternalPrivilegeFilterClause}" +
                $" and {CreatedOnField} ge {FormatOData(since)}" +
                $"&$orderby={CreatedOnField} desc,{PkField} desc&$top={max + 1}";
            var chunkRows = await appOnlyQuery.QueryAsync(CommunicationSet, odata, ct);
            truncated |= chunkRows.Count > max;
            rows.AddRange(chunkRows.Take(max));
        }

        // Re-check every row: the window, the external status/privilege rules, and that it regards one of THE records.
        var matched = new Dictionary<Guid, (string Entity, Guid RecordId)>();
        var candidates = new List<Dictionary<string, JsonElement>>();
        foreach (var row in rows)
        {
            if (TryGuid(row, PkField) is not { } messageId || messageId == Guid.Empty || matched.ContainsKey(messageId))
                continue;
            if (TryDateTimeOffset(row, CreatedOnField) is not { } createdOn || createdOn < since || !IsExternallyVisibleRow(row))
                continue;

            var match = keyed.FirstOrDefault(r => TryGuid(row, r.Value) == r.RecordId);
            if (match.Value is null)
                continue;
            matched[messageId] = (match.Entity, match.RecordId);
            candidates.Add(row);
        }

        // The multi-root rule, prepared once for every candidate's stamps (one batched secure-flag read, not one per row):
        // a message also filed to a root the caller may not see is not reported.
        var readable = await rootsReadable(candidates.Select(RootsOf).ToList(), ct);
        var scoped = candidates.Where(r => readable(RootsOf(r))).ToList();

        var (messages, isInternal) = await FilterExternalMessagesAsync(scoped, callerSystemUserId, ct);
        var ordered = messages
            .OrderByDescending(m => m.CreatedOn)
            .ThenByDescending(m => m.MessageId)
            .ToList();
        truncated |= ordered.Count > max;
        var result = ordered
            .Take(max)
            .Select(m => new RecentRecordMessage(m, matched[m.MessageId].Entity, matched[m.MessageId].RecordId))
            .ToList();

        _logger.LogDebug(
            "[RECENT-APPONLY] records={Records} since={Since:o} internalCaller={Internal} returned={Returned} " +
            "(read={Read}, kept-after-recheck={Kept}, truncated={Truncated})",
            keyed.Count, since, isInternal, result.Count, rows.Count, scoped.Count, truncated);

        return new AppOnlyRecentReadResult(result, truncated);
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
        // Fix round 6 (F1): an external caller lists only EXPLICITLY Open threads. The clause is part of the query's
        // $filter, so $top, the over-fetch row and the keyset next-page token are all computed over visible threads.
        if (await _identityResolver.IsExternalAsync(callerSystemUserId, ct))
            clauses.Add(CommunicationOpenThreadGate.OpenOnlyThreadFilterClause);
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
            DirectionField, SentByValue, CreatedByValue,
            SentAtField, CreatedOnField, InReplyToField, InternalOnlyField, PrivilegeField, IsPrivateField, ThreadLookupValue,
        });
        // Active-only (round-8): deactivated (soft-deleted) messages must drop out of every by-regarding / filtered
        // read that composes onto this shared pipeline. Wrap the caller filter so the AND binds correctly.
        var odata = $"$select={select}&{CreatedByExpand}&$filter=({odataFilter}) and {ActiveOnlyClause}&$orderby={CreatedOnField} asc&$top={top}";
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

        // D7 (task 065 fix round 5, ISS-009): an external caller sees an item only when its thread is EXPLICITLY Open at
        // read time. One batched read of the page's distinct threads (impersonated, like the rows); a thread the caller
        // cannot read is absent from the answer and so hides its rows. Internal callers are unchanged.
        if (isExternal && visible.Count > 0)
        {
            var open = await ReadOpenThreadIdsImpersonatedAsync(visible.Select(v => v.Parsed.ThreadId), callerSystemUserId, ct);
            visible = visible.Where(v => open.Contains(v.Parsed.ThreadId)).ToList();
        }

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
                : Array.Empty<ThreadAttachmentRef>(),
            CreatedByContactId: parsed.CreatedByContactId);

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

    // ── D7 open-thread gate (task 065 fix round 5, ISS-009) ─────────────────────────────────────────

    /// <summary>The distinct threads' open set read APP-ONLY (the external plane's identity). The seam being absent is a
    /// refusal, never "show everything".</summary>
    private Task<HashSet<Guid>> ReadOpenThreadIdsAppOnlyAsync(IEnumerable<Guid> threadIds, CancellationToken ct)
    {
        var appOnlyQuery = _appOnlyQuery
            ?? throw new InvalidOperationException(
                "IAppOnlyCommunicationQuery is not registered; the external-plane thread privacy read cannot run (fail closed).");
        return CommunicationOpenThreadGate.ReadOpenThreadIdsAsync(
            threadIds, (odata, c) => appOnlyQuery.QueryAsync(ThreadSet, odata, c), ct);
    }

    /// <summary>The distinct threads' open set read IMPERSONATED as the caller (same identity as the impersonated message
    /// read: a thread the caller cannot read is absent, which hides it).</summary>
    private Task<HashSet<Guid>> ReadOpenThreadIdsImpersonatedAsync(
        IEnumerable<Guid> threadIds, Guid callerSystemUserId, CancellationToken ct)
        => CommunicationOpenThreadGate.ReadOpenThreadIdsAsync(
            threadIds, (odata, c) => _query.QueryAsync(ThreadSet, odata, callerSystemUserId, c), ct);

    private async Task<bool> IsThreadOpenForExternalAsync(Guid threadId, Guid callerSystemUserId, CancellationToken ct)
        => (await ReadOpenThreadIdsImpersonatedAsync(new[] { threadId }, callerSystemUserId, ct)).Contains(threadId);

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
    /// <para><b>D9 (task 065 fix round 6, owner decision 2026-10-14):</b> an EXTERNAL caller (a systemuser flagged
    /// <c>sprk_isexternal</c>) can see, and so rename, pin or deactivate, only a thread that reads EXPLICITLY Open. The
    /// clause is part of the same one query, so a Private thread answers exactly like a missing one (no existence leak).
    /// Internal callers are unchanged.</para>
    /// </summary>
    public async Task<bool> CanCallerSeeThreadAsync(Guid threadId, ClaimsPrincipal? caller, CancellationToken ct)
    {
        var callerSystemUserId = await ResolveCallerOrThrowAsync(caller, ct);
        var openOnly = await _identityResolver.IsExternalAsync(callerSystemUserId, ct)
            ? $" and {CommunicationOpenThreadGate.OpenOnlyThreadFilterClause}"
            : string.Empty;
        var odata = $"$select={ThreadPkField}&$filter={ThreadPkField} eq {threadId}{openOnly}&$top=1";
        var rows = await _query.QueryAsync(ThreadSet, odata, callerSystemUserId, ct);
        return rows.Count > 0;
    }

    /// <summary>The record a thread is filed against: its first populated typed regarding lookup (ADR-024 priority).</summary>
    /// <param name="EntityType">The record's entity logical name; null when the thread has no typed regarding.</param>
    /// <param name="RecordId">The record's id; null when the thread has no typed regarding.</param>
    public sealed record ThreadRegardingTarget(string? EntityType, Guid? RecordId);

    /// <summary>
    /// Task 065 fix round 3 (F3): the typed regarding record of a thread the caller can SEE, from one IMPERSONATED
    /// projection (the same Dataverse row-level security as <see cref="CanCallerSeeThreadAsync"/>). The send route's
    /// authorization filter uses it to run AppendTo on the record a send into the thread is filed against (the send
    /// inherits the thread's regarding). The anchor is chosen in <c>RegardingFieldMap</c> priority order, the same order
    /// the send reads it in. Returns <c>null</c> when the thread is not visible to the caller or does not exist; an
    /// unresolved caller throws 403 and a query fault propagates (the caller denies, fail closed).
    /// </summary>
    public async Task<ThreadRegardingTarget?> ReadVisibleThreadRegardingAsync(
        Guid threadId, ClaimsPrincipal? caller, CancellationToken ct)
    {
        var callerSystemUserId = await ResolveCallerOrThrowAsync(caller, ct);
        if (threadId == Guid.Empty)
            return null;

        var select = string.Join(',', new[] { ThreadPkField }.Concat(ThreadRegardingFields.Select(x => $"_{x.RegardingField}_value")));
        var odata = $"$select={select}&$filter={ThreadPkField} eq {threadId}&$top=1";
        var rows = await _query.QueryAsync(ThreadSet, odata, callerSystemUserId, ct);
        if (rows.Count == 0)
            return null;

        var (entityType, recordId) = ResolveRegardingFromRow(rows[0]);
        return new ThreadRegardingTarget(entityType, recordId);
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

    /// <summary>
    /// COMMUNICATION VISIBILITY for the per-record route gate (unified-access-control-r2 task 161): the caller's
    /// impersonated top-1 read of <c>sprk_communication</c> <paramref name="communicationId"/>, THEN the shared
    /// <see cref="ICommunicationAccessFilter"/> (internal-only + privilege rules) — the exact sequence
    /// <see cref="GetUnreadCountAsync"/> applies to every row it counts. Returns the visible row (so a caller can
    /// read <paramref name="additionalColumns"/> from the SAME impersonated answer, e.g. the typed regarding
    /// lookups), or <c>null</c> when the row does not exist, is not readable by the caller, or the access filter
    /// hides it — the three are deliberately indistinguishable.
    /// </summary>
    /// <remarks>
    /// <para>Distinct from <see cref="CanCallerSeeMessageAsync"/>, which runs the impersonated read only and is
    /// left unchanged (the message-deactivate route depends on it). This one adds the access filter, so an
    /// internal-only message is not visible to an external caller here either.</para>
    /// <para>Fail closed: an unresolved caller throws the 403 <c>THREAD_READ_FORBIDDEN</c>; an impersonated-query
    /// fault propagates. There is no app-only fallback.</para>
    /// </remarks>
    /// <param name="communicationId">The <c>sprk_communication</c> id the route acts on.</param>
    /// <param name="caller">The request principal.</param>
    /// <param name="additionalColumns">Extra columns to select on the same read (OData names, e.g. <c>_sprk_regardingmatter_value</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ReadVisibleCommunicationAsync(
        Guid communicationId,
        ClaimsPrincipal? caller,
        IReadOnlyCollection<string>? additionalColumns,
        CancellationToken ct)
    {
        var callerSystemUserId = await ResolveCallerOrThrowAsync(caller, ct);
        if (communicationId == Guid.Empty)
            return null;

        // An external caller's answer also depends on the row's thread (D7), so the thread lookup is selected for them
        // only; an internal caller's query is unchanged.
        var isExternal = await _identityResolver.IsExternalAsync(callerSystemUserId, ct);
        var columns = new List<string> { PkField, InternalOnlyField, PrivilegeField };
        if (isExternal)
            columns.Add(ThreadLookupValue);
        if (additionalColumns is not null)
        {
            columns.AddRange(additionalColumns.Where(c => !string.IsNullOrWhiteSpace(c) && !columns.Contains(c, StringComparer.Ordinal)));
        }

        var odata = $"$select={string.Join(',', columns)}&$filter={PkField} eq {communicationId}&$top=1";
        var rows = await _query.QueryAsync(CommunicationSet, odata, callerSystemUserId, ct);
        if (rows.Count == 0)
            return null;

        var row = rows[0];
        var context = new CommunicationAccessContext(CallerSystemUserId: callerSystemUserId, IsInternalUser: !isExternal);
        var decision = _accessFilter.EvaluateMessage(context, ParseMessageRow(row).Entity);
        if (!decision.IsVisible)
            return null;

        // D7: an external caller sees the row only when its thread is explicitly Open at read time.
        if (isExternal)
        {
            var threadId = TryGuid(row, ThreadLookupValue) ?? Guid.Empty;
            var open = await ReadOpenThreadIdsImpersonatedAsync(new[] { threadId }, callerSystemUserId, ct);
            if (!open.Contains(threadId))
                return null;
        }

        return row;
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
            CreatedByContactId = TryGuid(row, CreatedByContactValue),
            // The systemuser sender's name; else (task 034) the contact author's name — selected only by the external read;
            // else (task 062) the creating user's name (an MDA-sent message stamps neither) — ONLY when createdby is a person,
            // never the BFF's application user; else null.
            SentByName = TryString(row, SentByFormattedValue)
                ?? TryString(row, CreatedByContactFormattedValue)
                ?? (CreatedByIsAPerson(row) ? TryString(row, CreatedByFormattedValue) : null),
            SentAt = TryDateTimeOffset(row, SentAtField),
            CreatedOn = TryDateTimeOffset(row, CreatedOnField),
            InReplyTo = TryString(row, InReplyToField),
            Entity = entity,
        };
    }

    /// <summary>
    /// True only when the row's expanded <c>createdby</c> is positively a person: the nested object is present, its
    /// <c>applicationid</c> is present and JSON null (an application user has one), and its <c>accessmode</c> is a number that
    /// is not Support User (3) or Non-interactive (4). A missing expand, a missing/odd member, or any other shape is "not a
    /// person" (fail toward a null sender name — the application user's name is never shown).
    /// </summary>
    private static bool CreatedByIsAPerson(Dictionary<string, JsonElement> row)
    {
        if (!row.TryGetValue(CreatedByNavigation, out var createdBy) || createdBy.ValueKind != JsonValueKind.Object)
            return false;
        if (!createdBy.TryGetProperty(CreatedByApplicationIdColumn, out var applicationId) || applicationId.ValueKind != JsonValueKind.Null)
            return false;
        return createdBy.TryGetProperty(CreatedByAccessModeColumn, out var accessMode)
            && accessMode.ValueKind == JsonValueKind.Number
            && accessMode.TryGetInt32(out var mode)
            && mode is not (AccessModeSupportUser or AccessModeNonInteractive);
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
        public Guid? CreatedByContactId { get; init; }
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
