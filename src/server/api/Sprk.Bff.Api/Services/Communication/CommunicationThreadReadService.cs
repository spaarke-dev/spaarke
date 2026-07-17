using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication.Access;

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
