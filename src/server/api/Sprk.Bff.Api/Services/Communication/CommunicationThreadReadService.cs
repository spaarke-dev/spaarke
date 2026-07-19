using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Engine;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The BFF read model for the polling timeline (task 050 / FR-11): thread-read (a thread's messages) and
/// unread-count (readable messages since a caller's last-seen marker). Both are the ~5s poll surface for every
/// open form, so both are lightweight by construction — projected columns, paged, NO ACS call (Dataverse is the
/// record), and bounded to two impersonated queries per read regardless of message count (NFR-07).
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

    // sprk_communicationthread columns (R2 tasks 010/002).
    private const string ThreadPkField = "sprk_communicationthreadid";
    private const string ThreadNameField = "sprk_name";

    // sprk_communication columns (as-built, notes/messaging-schema-spec.md).
    private const string ThreadLookupValue = "_sprk_communicationthread_value"; // message → thread lookup
    private const string PkField = "sprk_communicationid";
    private const string BodyField = "sprk_body";
    private const string BodyFormatField = "sprk_bodyformat";
    private const string TypeField = "sprk_communicationtype";
    private const string FromField = "sprk_from";
    private const string SentAtField = "sprk_sentat";
    private const string CreatedOnField = "createdon";
    private const string InReplyToField = "sprk_inreplyto";
    private const string InternalOnlyField = "sprk_isinternalonly";
    private const string PrivilegeField = "sprk_privilegeclassification";

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
    private const int MaxRegardingScan = 500;   // bound the by-regarding message scan across threads (R2 task 010)
    private const int MaxQueryScan = 200;       // bound the filtered-query message scan (R2 task 011)

    private readonly IImpersonatedCommunicationQuery _query;
    private readonly ICommunicationAccessFilter _accessFilter;
    private readonly ICallerSystemUserResolver _callerResolver;
    private readonly ILogger<CommunicationThreadReadService> _logger;

    public CommunicationThreadReadService(
        IImpersonatedCommunicationQuery query,
        ICommunicationAccessFilter accessFilter,
        ICallerSystemUserResolver callerResolver,
        ILogger<CommunicationThreadReadService> logger)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _accessFilter = accessFilter ?? throw new ArgumentNullException(nameof(accessFilter));
        _callerResolver = callerResolver ?? throw new ArgumentNullException(nameof(callerResolver));
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
            PkField, BodyField, BodyFormatField, TypeField, FromField,
            SentAtField, CreatedOnField, InReplyToField, InternalOnlyField, PrivilegeField,
        });
        var filter = new StringBuilder($"{ThreadLookupValue} eq {threadId}");
        if (since is { } s)
            filter.Append($" and {CreatedOnField} gt {FormatOData(s)}");

        var odata = $"$select={select}&$filter={filter}&$orderby={CreatedOnField} asc&$top={pageSize}";
        var rows = await _query.QueryAsync(CommunicationSet, odata, callerSystemUserId, ct);

        // 2) Apply the SHARED internal-only + privilege filter on top of the already-scoped rows.
        var parsed = rows.Select(ParseMessageRow).ToList();
        var context = new CommunicationAccessContext(CallerSystemUserId: callerSystemUserId, IsInternalUser: true);
        var filtered = _accessFilter.FilterMessages(context, parsed.Select(p => p.Entity).ToList());

        var byId = parsed.ToDictionary(p => p.MessageId);
        var visible = filtered.Decisions
            .Where(d => d.Decision.IsVisible)
            .Select(d => (Parsed: byId[d.Message.Id], d.Decision))
            .ToList();

        // 3) Attachments — ONE bulk impersonated query for the visible page (no per-row fan-out).
        var attachmentsByMessage = await LoadAttachmentsAsync(
            visible.Select(v => v.Parsed.MessageId).ToList(), callerSystemUserId, ct);

        var messages = visible.Select(v => new ThreadMessageDto(
            MessageId: v.Parsed.MessageId,
            Body: v.Parsed.Body,
            BodyFormat: v.Parsed.BodyFormat,
            CommunicationType: v.Parsed.CommunicationType,
            From: v.Parsed.From,
            SentAt: v.Parsed.SentAt,
            CreatedOn: v.Parsed.CreatedOn,
            InReplyTo: v.Parsed.InReplyTo,
            Privilege: (int)v.Decision.Privilege,
            Attachments: attachmentsByMessage.TryGetValue(v.Parsed.MessageId, out var atts)
                ? atts
                : Array.Empty<ThreadAttachmentRef>())).ToList();

        _logger.LogDebug(
            "[THREAD-READ] thread={ThreadId} caller={Caller} returned={Returned} (impersonated={Impersonated}, hidden-internal-only={Hidden})",
            threadId, callerSystemUserId, messages.Count, rows.Count, rows.Count - messages.Count);

        return new ThreadReadResult(threadId, messages, messages.Count);
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
        var filter = new StringBuilder($"{ThreadLookupValue} eq {threadId}");
        if (since is { } s)
            filter.Append($" and {CreatedOnField} gt {FormatOData(s)}");

        var odata = $"$select={select}&$filter={filter}&$top={MaxUnreadScan}";
        var rows = await _query.QueryAsync(CommunicationSet, odata, callerSystemUserId, ct);

        var context = new CommunicationAccessContext(CallerSystemUserId: callerSystemUserId, IsInternalUser: true);
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
        var threadSelect = string.Join(',', new[] { ThreadPkField, ThreadNameField });
        var threadOData =
            $"$select={threadSelect}&$filter=_{regardingField}_value eq {recordId}" +
            $"&$orderby={CreatedOnField} asc&$top={MaxThreads}";
        var threadRows = await _query.QueryAsync(ThreadSet, threadOData, callerSystemUserId, ct);

        var threads = threadRows
            .Select(r => (Id: TryGuid(r, ThreadPkField) ?? Guid.Empty, Name: TryString(r, ThreadNameField)))
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
                return new ThreadReadResult(t.Id, msgs, msgs.Count);
            })
            .ToList();

        var messageCount = threadResults.Sum(t => t.Count);
        _logger.LogDebug(
            "[BY-REGARDING] {EntityType}={RecordId} caller={Caller} threads={Threads} messages={Messages}",
            entityType, recordId, callerSystemUserId, threadResults.Count, messageCount);

        return new RegardingReadResult(entityType, recordId, threadResults, threadResults.Count, messageCount);
    }

    // ── filtered query (R2 task 011 / FR-02) ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a flat, access-filtered communication list matching the thread/regarding/channel/date facets, in the
    /// R1 <see cref="ThreadMessageDto"/> shape. Every facet composes onto the SAME impersonation read path +
    /// <see cref="ICommunicationAccessFilter"/> as <see cref="ReadByRegardingAsync"/> — no second filter, no
    /// membership-union (NFR-03). Facet → column: <c>thread</c> → <c>_sprk_communicationthread_value</c>;
    /// <c>regarding</c> (<c>{entityType}:{guid}</c>) → the typed <see cref="RegardingFieldMap"/> lookup;
    /// <c>channel</c> → <c>sprk_communicationtype</c>; <c>from</c>/<c>to</c> → <c>sprk_sentat</c> range.
    /// <para><b><c>participant</c> is STUBBED here (R2 task 011).</b> It is wired in task 051 once the
    /// <c>sprk_communicationparticipant</c> junction (003) + participant-index write (050) land. Supplying it returns
    /// a 501 not-yet-supported ProblemDetails (ADR-019) — it is NEVER satisfied by a text-LIKE fallback over the
    /// <c>;</c>-joined address text (spec §11 forbids it), so a caller can never mistake an unfiltered/mis-filtered
    /// result for a participant-scoped one. Task 051 replaces this stub with a junction join behind the same route.</para>
    /// Graceful degradation (ADR-019): a malformed thread/regarding/channel/date, an unknown regarding entity type,
    /// or no facet at all is a 400 ProblemDetails — never a 500 and never an unfiltered dump.
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
        // participant= is inert until task 051 — reject explicitly rather than silently ignore or text-LIKE (NFR-03).
        if (!string.IsNullOrWhiteSpace(participant))
            throw new SdapProblemException(
                code: "PARTICIPANT_FILTER_NOT_SUPPORTED",
                title: "Participant Filter Not Yet Supported",
                detail: "The 'participant' facet is not yet supported; it is wired in task 051 once the participant "
                        + "junction lands. Filter by thread, regarding, channel, from, or to instead.",
                statusCode: 501);

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
        if (clauses.Count == 0)
            throw BadRequest("At least one filter is required (thread, regarding, channel, from, or to).");

        var callerSystemUserId = await ResolveCallerOrThrowAsync(caller, ct);

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
            PkField, BodyField, BodyFormatField, TypeField, FromField,
            SentAtField, CreatedOnField, InReplyToField, InternalOnlyField, PrivilegeField, ThreadLookupValue,
        });
        var odata = $"$select={select}&$filter={odataFilter}&$orderby={CreatedOnField} asc&$top={top}";
        var rows = await _query.QueryAsync(CommunicationSet, odata, callerSystemUserId, ct);

        var parsed = rows.Select(ParseMessageRow).ToList();
        var context = new CommunicationAccessContext(CallerSystemUserId: callerSystemUserId, IsInternalUser: true);
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
            SentAt: parsed.SentAt,
            CreatedOn: parsed.CreatedOn,
            InReplyTo: parsed.InReplyTo,
            Privilege: (int)decision.Privilege,
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
        var privilege = TryInt(row, PrivilegeField);

        var entity = new Entity("sprk_communication", messageId);
        if (isInternalOnly.HasValue)
            entity[InternalOnlyField] = isInternalOnly.Value;
        if (privilege.HasValue)
            entity[PrivilegeField] = new OptionSetValue(privilege.Value);

        return new ParsedMessage
        {
            MessageId = messageId,
            ThreadId = TryGuid(row, ThreadLookupValue) ?? Guid.Empty,
            Body = TryString(row, BodyField),
            BodyFormat = TryInt(row, BodyFormatField),
            CommunicationType = TryInt(row, TypeField),
            From = TryString(row, FromField),
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
        public string? Body { get; init; }
        public int? BodyFormat { get; init; }
        public int? CommunicationType { get; init; }
        public string? From { get; init; }
        public DateTimeOffset? SentAt { get; init; }
        public DateTimeOffset? CreatedOn { get; init; }
        public string? InReplyTo { get; init; }
        public required Entity Entity { get; init; }
    }

    // ── OData / JSON helpers ────────────────────────────────────────────────────────────────────────

    private static int NormalizePageSize(int? top)
        => top is null or <= 0 ? DefaultPageSize : Math.Min(top.Value, MaxPageSize);

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
}
