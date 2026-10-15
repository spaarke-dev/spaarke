using System.Globalization;
using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Signals.Actions;

/// <summary>
/// How the Inquiry turned out (<c>sprk_servicerequest.sprk_disposition</c>, spec FR-37). NOT the Decision Record's
/// <c>sprk_decisionoutcome</c> (what the human decided at the gate): two vocabularies, deliberately. Values are the live
/// option values (spaarkedev1 describe, 2026-10-09).
/// </summary>
public enum InquiryDisposition
{
    WriteOff = 100000000,
    BudgetRevised = 100000001,
    ScopeApproved = 100000002,
    NoAction = 100000003,
}

/// <summary>How recording an Inquiry's disposition ended.</summary>
public enum InquiryResolutionOutcome
{
    /// <summary>The Inquiry now carries the disposition and is closed.</summary>
    Resolved,
    /// <summary>Nothing written: the reply or the service request is missing, or the caller cannot read it (one answer for both).</summary>
    NotFound,
    /// <summary>Nothing written: the communication is not an inbound (Incoming) reply.</summary>
    NotAReply,
    /// <summary>Nothing written: the reply is filed against a different service request than the one named.</summary>
    ReplyNotForInquiry,
    /// <summary>Nothing written: the service request is not an outbound inquiry.</summary>
    NotAnInquiry,
    /// <summary>Nothing written: the Inquiry already has a disposition or is closed (the first one wins).</summary>
    AlreadyResolved,
    /// <summary>Nothing written: the request is malformed (empty id, or a disposition that is not one of the four).</summary>
    Invalid,
    /// <summary>Dataverse refused the caller's write (they lack Write on the service request).</summary>
    Denied,
    /// <summary>Dataverse failed, or the row's version could not be read; the Inquiry is unchanged.</summary>
    Failed,
}

/// <param name="Outcome">How it ended.</param>
/// <param name="ServiceRequestId">The Inquiry, when one was identified.</param>
/// <param name="Error">A caller-safe reason for any outcome but Resolved.</param>
/// <param name="TodoCompleted">The "Record the outcome" To Do was found and completed (false when there was none, or it could not be completed; the log says which).</param>
public sealed record InquiryResolutionResult(
    InquiryResolutionOutcome Outcome, Guid? ServiceRequestId = null, string? Error = null, bool TodoCompleted = false)
{
    public bool Succeeded => Outcome == InquiryResolutionOutcome.Resolved;
}

/// <summary>How the for-todo lookup ended (task 154).</summary>
public enum InquiryLookupOutcome
{
    /// <summary>The inquiry and its readable replies.</summary>
    Found,
    /// <summary>The To Do or the inquiry is missing or not readable by the caller, or no inquiry matches (one answer for all).</summary>
    NotFound,
    /// <summary>A Dataverse fault: 503, never a 404 and never a shorter list.</summary>
    Unavailable,
}

/// <summary>One incoming reply to an inquiry, as the caller reads it.</summary>
public sealed record InquiryReply(Guid CommunicationId, string? Subject, DateTimeOffset? ReceivedOn);

/// <param name="Outcome">How it ended.</param>
/// <param name="ServiceRequestId">The inquiry, when found.</param>
/// <param name="Replies">Its incoming replies the caller reads, newest first (undated last), when found.</param>
/// <param name="Reason">A short machine reason for <see cref="InquiryLookupOutcome.Unavailable"/>.</param>
public sealed record InquiryLookupResult(
    InquiryLookupOutcome Outcome, Guid? ServiceRequestId = null, IReadOnlyList<InquiryReply>? Replies = null, string? Reason = null)
{
    public static readonly InquiryLookupResult NotFound = new(InquiryLookupOutcome.NotFound);

    public static InquiryLookupResult Unavailable(string reason) => new(InquiryLookupOutcome.Unavailable, Reason: reason);
}

/// <summary>Whether a To Do is an OPEN inquiry's "Record the outcome" To Do (task 154, adversarial pass 1).</summary>
public enum InquiryTodoMatch
{
    /// <summary>No open outbound inquiry the caller can read derives the To Do's id: an ordinary To Do.</summary>
    None,
    /// <summary>The To Do records the outcome of <see cref="InquiryTodoMatchResult.ServiceRequestId"/>, which is open and outbound.</summary>
    OpenInquiry,
    /// <summary>A Dataverse fault: the answer is unknown (fail closed: 503, never "ordinary").</summary>
    Unavailable,
}

/// <param name="Match">How it ended.</param>
/// <param name="ServiceRequestId">The open inquiry, for <see cref="InquiryTodoMatch.OpenInquiry"/>.</param>
/// <param name="Reason">A short machine reason for <see cref="InquiryTodoMatch.Unavailable"/>.</param>
public sealed record InquiryTodoMatchResult(InquiryTodoMatch Match, Guid? ServiceRequestId = null, string? Reason = null)
{
    public static readonly InquiryTodoMatchResult None = new(InquiryTodoMatch.None);

    public static InquiryTodoMatchResult Unavailable(string reason) => new(InquiryTodoMatch.Unavailable, Reason: reason);
}

/// <summary>Resolved Inquiries by disposition. <see cref="Total"/> is the sum.</summary>
public sealed record DispositionTally(int WriteOff, int BudgetRevised, int ScopeApproved, int NoAction)
{
    public int Total => WriteOff + BudgetRevised + ScopeApproved + NoAction;

    public static DispositionTally From(IEnumerable<(InquiryDisposition Disposition, int Count)> rows)
    {
        int w = 0, b = 0, s = 0, n = 0;
        foreach (var (d, c) in rows)
        {
            switch (d)
            {
                case InquiryDisposition.WriteOff: w += c; break;
                case InquiryDisposition.BudgetRevised: b += c; break;
                case InquiryDisposition.ScopeApproved: s += c; break;
                case InquiryDisposition.NoAction: n += c; break;
            }
        }
        return new(w, b, s, n);
    }
}

/// <summary>One outside firm's resolved Inquiries. <see cref="FirmId"/> is null for Inquiries sent without a firm.</summary>
public sealed record FirmDispositionTally(Guid? FirmId, DispositionTally Dispositions);

/// <summary>
/// Task 071 (spec FR-37, criterion 10; decision D-111): a human records how the Inquiry turned out, and the dispositions
/// are queryable per matter and per outside firm. Both are derived from existing relationships on
/// <c>sprk_servicerequest</c> (<c>sprk_regardingmatter</c>, <c>sprk_regardingorganization</c>, written by the Inquiry
/// executor, task 070): no denormalised column was added.
/// </summary>
/// <remarks>
/// <para><b>Recording is written AS THE CALLER</b> (<see cref="IDataverseUserClient"/>): Dataverse enforces Write on the
/// service request itself, so there is no application-identity write path. The PATCH is conditional on the row's
/// <c>versionnumber</c> (If-Match), so two people recording at once cannot both win; on a 412 the row is re-read once and
/// the write retried only if the disposition is still empty. The reply is tied to the Inquiry through the association
/// ladder: the Inquiry email's primary association is the service request (task 070), so a reply carries
/// <c>sprk_communication.sprk_regardingservicerequest</c>, which must equal the Inquiry named.</para>
/// <para>The human's choice counts as confirming a Suggested association of the reply. The "Record the outcome" To Do
/// (<see cref="InquiryReplyTodoCreator"/>) is completed in the same call.</para>
/// <para><b>Tallies run as the caller</b>: a user sees the tallies of the Inquiries they may read, nothing more. They are
/// server-side aggregates, so there is no paging to truncate.</para>
/// </remarks>
public sealed class InquiryDispositionService(
    IDataverseUserClient user,
    TimeProvider clock,
    ILogger<InquiryDispositionService> logger)
{
    internal const string ServiceRequest = "sprk_servicerequest";
    /// <summary>sprk_communication.sprk_direction: Incoming.</summary>
    internal const int CommunicationIncoming = 100000000;
    /// <summary>sprk_communication.sprk_associationstatus: Suggested / Resolved.</summary>
    internal const int AssociationSuggested = 100000003;
    internal const int AssociationResolved = 100000000;
    internal const int StateActive = 0;
    internal const int StateInactive = 1;
    internal const int StatusInactive = 2;
    /// <summary>sprk_todo.statuscode Completed (State Inactive).</summary>
    internal const int TodoStatusCompleted = 2;

    /// <summary>
    /// Records <paramref name="disposition"/> on the Inquiry <paramref name="serviceRequestId"/> because of the inbound
    /// <paramref name="replyCommunicationId"/>: sets <c>sprk_disposition</c> and closes the service request, completes the
    /// "Record the outcome" To Do and confirms a Suggested association of the reply.
    /// </summary>
    public async Task<InquiryResolutionResult> RecordDispositionAsync(
        Guid serviceRequestId, Guid replyCommunicationId, int disposition, CancellationToken ct)
    {
        if (serviceRequestId == Guid.Empty || replyCommunicationId == Guid.Empty || !Enum.IsDefined(typeof(InquiryDisposition), disposition))
            return new(InquiryResolutionOutcome.Invalid, Error: "An inquiry, its reply and one of the four dispositions are required.");

        var reply = await user.GetAsync(
            $"sprk_communications({replyCommunicationId:D})?$select=sprk_direction,_sprk_regardingservicerequest_value,sprk_associationstatus", ct)
            .ConfigureAwait(false);
        if (!reply.IsSuccess)
            return FailureOfRead(reply, "The reply could not be read.");

        if (ReadLong(reply.Body, "sprk_direction") != CommunicationIncoming)
            return new(InquiryResolutionOutcome.NotAReply, Error: "The communication is not an inbound reply.");
        if (ReadGuid(reply.Body, "_sprk_regardingservicerequest_value") != serviceRequestId)
            return new(InquiryResolutionOutcome.ReplyNotForInquiry, Error: "The reply is not filed against this inquiry.");

        // One re-read after a 412: the write is retried only if the disposition is STILL empty.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var row = await user.GetAsync(
                $"sprk_servicerequests({serviceRequestId:D})?$select=sprk_direction,sprk_disposition,statecode,versionnumber", ct)
                .ConfigureAwait(false);
            if (!row.IsSuccess)
                return FailureOfRead(row, "The inquiry could not be read.");

            if (ReadLong(row.Body, "sprk_direction") != BudgetInquiryExecutor.DirectionOutbound)
                return new(InquiryResolutionOutcome.NotAnInquiry, serviceRequestId, "The service request is not an outbound inquiry.");
            if (ReadLong(row.Body, "sprk_disposition") is not null || ReadLong(row.Body, "statecode") == StateInactive)
                return new(InquiryResolutionOutcome.AlreadyResolved, serviceRequestId, "The inquiry already has a disposition.");
            if (ReadLong(row.Body, "versionnumber") is not { } version)
            {
                logger.LogWarning("Inquiry not resolved: the row's versionnumber was not returned | ServiceRequestId: {Id}", serviceRequestId);
                return new(InquiryResolutionOutcome.Failed, serviceRequestId, "The inquiry's version could not be read; nothing was written.");
            }

            var body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["sprk_disposition"] = disposition,
                ["statecode"] = StateInactive,
                ["statuscode"] = StatusInactive,
            });
            var patch = await user.PatchAsync($"sprk_servicerequests({serviceRequestId:D})", body, version, ct).ConfigureAwait(false);

            if (patch.IsSuccess)
            {
                var todoCompleted = await CompleteTodoAsync(serviceRequestId, ct).ConfigureAwait(false);
                await ConfirmSuggestedAssociationAsync(replyCommunicationId, ReadLong(reply.Body, "sprk_associationstatus"), ct).ConfigureAwait(false);
                return new(InquiryResolutionOutcome.Resolved, serviceRequestId, TodoCompleted: todoCompleted);
            }

            if (patch.StatusCode == 412 && attempt == 0)
                continue;
            if (patch.StatusCode == 412)
                return new(InquiryResolutionOutcome.AlreadyResolved, serviceRequestId, "The inquiry changed while it was being recorded.");

            return patch.StatusCode == 403
                ? new(InquiryResolutionOutcome.Denied, serviceRequestId, "You do not have permission to record this disposition.")
                : new(InquiryResolutionOutcome.Failed, serviceRequestId, "The disposition could not be recorded.");
        }

        return new(InquiryResolutionOutcome.Failed, serviceRequestId, "The disposition could not be recorded.");
    }

    /// <summary>
    /// The namespace of the To Do ids (RFC 4122 version 5). A CONSTANT of the contract, not a secret: the client computes the
    /// same id (SHA-1 of this namespace's 16 network-order bytes followed by the UTF-8 name, version and variant bits set).
    /// Documented in notes/071-disposition-accrual.md. Never change it: every existing To Do id derives from it.
    /// </summary>
    public static readonly Guid TodoIdNamespace = new("6f1c0a52-3b7e-4d1a-9c2e-5a8d4b7e1f30");

    /// <summary>
    /// The deterministic id of an Inquiry's "Record the outcome" To Do: a version-5 UUID of the name
    /// <c>"inquiry-outcome-todo:" + serviceRequestId</c> (lower-case "D" form) in <see cref="TodoIdNamespace"/>. The same
    /// Inquiry always yields the same id, which makes creating it idempotent (a second create is a duplicate key), finding it
    /// to complete it a lookup by id, and the To Do's link back to its Inquiry recoverable by a client that can compute SHA-1
    /// even when the notes hint is gone.
    /// </summary>
    public static Guid TodoIdFor(Guid serviceRequestId)
    {
        var name = System.Text.Encoding.UTF8.GetBytes("inquiry-outcome-todo:" + serviceRequestId.ToString("D"));
        var input = new byte[16 + name.Length];
        TodoIdNamespace.ToByteArray(bigEndian: true).CopyTo(input, 0);
        name.CopyTo(input, 16);

        var hash = System.Security.Cryptography.SHA1.HashData(input);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>
    /// THE test of "does this To Do's notes hint name the inquiry it records" (task 154): every <c>serviceRequestId=</c> occurrence in
    /// its <c>sprk_notes</c>, on any line (<see cref="InquiryReplyTodoCreator.NotesKey"/>, a HINT only), is read as a GUID and accepted
    /// only when that inquiry's derived id (<see cref="TodoIdFor"/>) is <paramref name="todoId"/>. The v5 id match is the proof, so a
    /// note typed above the hint does not hide it. Returns the inquiry, or null. This test alone says nothing about whether the inquiry
    /// is still OPEN (<see cref="FindOpenInquiryOfTodoAsync"/> reads that, and falls back to the matter's open inquiries when the hint
    /// fails; the plan route, the commit route and the for-todo lookup all go through it, task 154 pass 2 F1).
    /// </summary>
    public static Guid? InquiryOfTodo(Guid todoId, string? notes)
    {
        if (todoId == Guid.Empty || string.IsNullOrEmpty(notes))
            return null;

        var key = InquiryReplyTodoCreator.NotesKey;
        for (var at = notes.IndexOf(key, StringComparison.Ordinal); at >= 0; at = notes.IndexOf(key, at + key.Length, StringComparison.Ordinal))
        {
            var rest = notes.AsSpan(at + key.Length).TrimStart(" \t");
            var end = rest.IndexOfAny(" \t\r\n");
            if (Guid.TryParse(end < 0 ? rest : rest[..end], out var serviceRequestId)
                && serviceRequestId != Guid.Empty && TodoIdFor(serviceRequestId) == todoId)
            {
                return serviceRequestId;
            }
        }

        return null;
    }

    /// <summary>
    /// THE identification of an inquiry-outcome To Do whose inquiry is still OPEN (task 154, adversarial pass 1), read AS THE CALLER
    /// through <paramref name="user"/>. The for-todo lookup, the commit route and the plan route all call it (pass 2 F1: the plan
    /// passes the notes and matter the gate's "mine" read returned). (1) A hint <see cref="InquiryOfTodo"/> accepts: one GET of that inquiry. (2) Otherwise,
    /// only when <paramref name="matterId"/> is given: one list of the matter's open outbound inquiries, and the one whose derived id is
    /// the To Do's. An inquiry that is closed, not outbound, or not readable by the caller is <see cref="InquiryTodoMatch.None"/>: the
    /// To Do is then an ordinary To Do. A fault (any status but 403/404, a throw, an unparseable answer, a further page) is
    /// <see cref="InquiryTodoMatch.Unavailable"/>, never None.
    /// </summary>
    internal static async Task<InquiryTodoMatchResult> FindOpenInquiryOfTodoAsync(
        IDataverseUserClient user, ILogger logger, Guid todoId, string? notes, Guid? matterId, CancellationToken ct)
    {
        if (InquiryOfTodo(todoId, notes) is { } hinted)
            return await ReadOpenInquiryAsync(user, logger, hinted, ct).ConfigureAwait(false);

        if (matterId is not { } matter || matter == Guid.Empty)
            return InquiryTodoMatchResult.None;

        try
        {
            // The hint is missing, malformed or names another inquiry: the matter's open outbound inquiries, as the caller.
            var open = await user.GetAsync(
                $"sprk_servicerequests?$select=sprk_servicerequestid&$filter=_sprk_regardingmatter_value eq {matter:D}"
                + $" and sprk_direction eq {BudgetInquiryExecutor.DirectionOutbound.ToString(CultureInfo.InvariantCulture)}"
                + $" and statecode eq {StateActive.ToString(CultureInfo.InvariantCulture)}", ct)
                .ConfigureAwait(false);
            if (ListRows(open, "inquiries", logger) is not { } rows)
                return InquiryTodoMatchResult.Unavailable("inquiry_list_failed");

            return rows.Select(r => ReadGuid(r, "sprk_servicerequestid")).FirstOrDefault(id => id is { } g && TodoIdFor(g) == todoId) is { } found
                ? new(InquiryTodoMatch.OpenInquiry, found)
                : InquiryTodoMatchResult.None;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Inquiry of a To Do: the matter's inquiries could not be listed ({ExceptionType}); unavailable.", ex.GetType().Name);
            return InquiryTodoMatchResult.Unavailable("inquiry_list_failed");
        }
    }

    /// <summary>
    /// Whether the inquiry <paramref name="serviceRequestId"/> is open, read AS THE CALLER: one GET of its <c>statecode</c> and
    /// <c>sprk_direction</c>. Active and outbound is <see cref="InquiryTodoMatch.OpenInquiry"/>; closed, not outbound, or 403/404 is
    /// <see cref="InquiryTodoMatch.None"/>; any other failure, an unparseable answer or a throw is <see cref="InquiryTodoMatch.Unavailable"/>.
    /// </summary>
    internal static async Task<InquiryTodoMatchResult> ReadOpenInquiryAsync(
        IDataverseUserClient user, ILogger logger, Guid serviceRequestId, CancellationToken ct)
    {
        try
        {
            var row = await user.GetAsync($"sprk_servicerequests({serviceRequestId:D})?$select=statecode,sprk_direction", ct).ConfigureAwait(false);
            if (!row.IsSuccess)
            {
                if (row.StatusCode is 403 or 404)
                    return InquiryTodoMatchResult.None;

                logger.LogWarning("Inquiry of a To Do: the inquiry read failed ({Status}, {ErrorCode}); unavailable.", row.StatusCode, row.ErrorCode);
                return InquiryTodoMatchResult.Unavailable("inquiry_read_failed");
            }

            if (row.Body is not { ValueKind: JsonValueKind.Object } body)
            {
                logger.LogWarning("Inquiry of a To Do: the inquiry read was unparseable; unavailable.");
                return InquiryTodoMatchResult.Unavailable("inquiry_read_failed");
            }

            return ReadLong(body, "statecode") == StateActive && ReadLong(body, "sprk_direction") == BudgetInquiryExecutor.DirectionOutbound
                ? new(InquiryTodoMatch.OpenInquiry, serviceRequestId)
                : InquiryTodoMatchResult.None;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Inquiry of a To Do: the inquiry read threw ({ExceptionType}); unavailable.", ex.GetType().Name);
            return InquiryTodoMatchResult.Unavailable("inquiry_read_failed");
        }
    }

    /// <summary>
    /// <c>GET /api/v1/inquiries/for-todo/{todoId}</c> (task 154, D-111): the OPEN inquiry an inquiry-outcome To Do records and its
    /// incoming replies, newest first. Every read is AS THE CALLER (<see cref="IDataverseUserClient"/>); nothing is read with the BFF's
    /// identity.
    /// </summary>
    /// <remarks>
    /// (1) The To Do: 403/404 is <see cref="InquiryLookupOutcome.NotFound"/>. (2) <see cref="FindOpenInquiryOfTodoAsync"/> with the
    /// To Do's matter: the hint when it derives the To Do's id, else the matter's open outbound inquiries. A closed, non-outbound or
    /// unreadable inquiry is NotFound; the hinted path applies the same open and outbound test as the fallback. (3) The inquiry's
    /// Incoming communications, read as the caller (no Read on the table: none). A fault, a throw (a Dataverse timeout included), a
    /// 401, an unparseable answer or a list with more pages is <see cref="InquiryLookupOutcome.Unavailable"/> (503), never a shorter
    /// list and never a 404.
    /// </remarks>
    public async Task<InquiryLookupResult> FindForTodoAsync(Guid todoId, CancellationToken ct)
    {
        if (todoId == Guid.Empty)
            return InquiryLookupResult.NotFound;

        try
        {
            var todo = await user.GetAsync($"sprk_todos({todoId:D})?$select=sprk_notes,_sprk_regardingmatter_value", ct).ConfigureAwait(false);
            if (!todo.IsSuccess)
                return LookupFailure(todo, "todo_read_failed");

            var match = await FindOpenInquiryOfTodoAsync(
                user, logger, todoId, ReadText(todo.Body, "sprk_notes"), ReadGuid(todo.Body, "_sprk_regardingmatter_value"), ct).ConfigureAwait(false);
            if (match.Match == InquiryTodoMatch.Unavailable)
                return InquiryLookupResult.Unavailable(match.Reason ?? "fault");
            if (match.ServiceRequestId is not { } serviceRequestId)
                return InquiryLookupResult.NotFound;

            var replies = await user.GetAsync(
                $"sprk_communications?$select=sprk_communicationid,sprk_subject,sprk_receiveddate&$filter=_sprk_regardingservicerequest_value eq {serviceRequestId:D}"
                + $" and sprk_direction eq {CommunicationIncoming.ToString(CultureInfo.InvariantCulture)}&$orderby=sprk_receiveddate desc", ct)
                .ConfigureAwait(false);
            if (ListRows(replies, "replies", logger) is not { } replyRows)
                return InquiryLookupResult.Unavailable("replies_read_failed");

            var list = replyRows
                .Select(r => (Id: ReadGuid(r, "sprk_communicationid"), Row: r))
                .Where(r => r.Id is not null)
                .Select(r => new InquiryReply(r.Id!.Value, ReadText(r.Row, "sprk_subject"), ReadInstant(r.Row, "sprk_receiveddate")))
                .OrderByDescending(r => r.ReceivedOn.HasValue)
                .ThenByDescending(r => r.ReceivedOn)
                .ThenBy(r => r.CommunicationId)
                .ToList();
            return new InquiryLookupResult(InquiryLookupOutcome.Found, serviceRequestId, list);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // DataverseUserClient catches only HttpRequestException, so a timeout (TaskCanceledException) reaches here: a fault, 503.
            logger.LogWarning("Inquiry lookup: a caller read threw ({ExceptionType}); unavailable.", ex.GetType().Name);
            return InquiryLookupResult.Unavailable("fault");
        }
    }

    /// <summary>A single-row read the caller could not do: 403/404 is "not found" (one answer for both); anything else, a 401
    /// included (the token failed mid-request, not a verdict), is unavailable.</summary>
    private InquiryLookupResult LookupFailure(DataverseUserResponse response, string reason)
    {
        if (response.StatusCode is 403 or 404)
            return InquiryLookupResult.NotFound;

        logger.LogWarning("Inquiry lookup: a caller read failed ({Status}, {ErrorCode}); unavailable.", response.StatusCode, response.ErrorCode);
        return InquiryLookupResult.Unavailable(reason);
    }

    /// <summary>A list read as the caller: the rows; none on a 403 (no Read on the table); null on any fault, an unparseable body
    /// or a further page (a list is never presented shorter than it is).</summary>
    private static List<JsonElement>? ListRows(DataverseUserResponse response, string what, ILogger logger)
    {
        if (!response.IsSuccess)
        {
            if (response.StatusCode == 403)
                return [];
            logger.LogWarning("Inquiry lookup: the {What} read failed ({Status}, {ErrorCode}); unavailable.", what, response.StatusCode, response.ErrorCode);
            return null;
        }

        if (response.Body is not { } body || !body.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array
            || body.TryGetProperty("@odata.nextLink", out _))
        {
            logger.LogWarning("Inquiry lookup: the {What} read was unparseable or had more pages; unavailable.", what);
            return null;
        }

        return value.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static string? ReadText(JsonElement? body, string name) =>
        body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? ReadInstant(JsonElement row, string name) =>
        ReadText(row, name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
            ? at
            : null;

    private async Task<bool> CompleteTodoAsync(Guid serviceRequestId, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["statecode"] = StateInactive,
            ["statuscode"] = TodoStatusCompleted,
            ["sprk_completedon"] = clock.GetUtcNow().UtcDateTime,
        });
        var response = await user.PatchAsync($"sprk_todos({TodoIdFor(serviceRequestId):D})", body, ct).ConfigureAwait(false);
        if (response.IsSuccess)
            return true;

        // 404 is "there is no such To Do" (no reply raised one, or it was removed): nothing to complete. Anything else is
        // reported in the log; the disposition is already recorded and stands.
        if (response.StatusCode != 404)
            logger.LogWarning("Inquiry outcome To Do not completed ({Status}) | ServiceRequestId: {Id}", response.StatusCode, serviceRequestId);
        return false;
    }

    private async Task ConfirmSuggestedAssociationAsync(Guid replyCommunicationId, long? status, CancellationToken ct)
    {
        if (status != AssociationSuggested)
            return;

        var body = JsonSerializer.Serialize(new Dictionary<string, object> { ["sprk_associationstatus"] = AssociationResolved });
        var response = await user.PatchAsync($"sprk_communications({replyCommunicationId:D})", body, ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            logger.LogWarning("Reply association not confirmed ({Status}) | CommunicationId: {Id}", response.StatusCode, replyCommunicationId);
    }

    /// <summary>A read the caller could not do: a missing row and one they may not read get the SAME answer.</summary>
    private static InquiryResolutionResult FailureOfRead(DataverseUserResponse response, string message) =>
        response.StatusCode is 403 or 404
            ? new(InquiryResolutionOutcome.NotFound, Error: "Not found.")
            : new(InquiryResolutionOutcome.Failed, Error: message);

    private static long? ReadLong(JsonElement? body, string name) =>
        body is { } b && b.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;

    private static Guid? ReadGuid(JsonElement? body, string name) =>
        body is { } b && b.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) ? g : null;

    // ---- accrual: queryable per matter and per outside firm ------------------------------------------------------

    /// <summary>Every resolved Inquiry on <paramref name="matterId"/>, by disposition.</summary>
    public async Task<DispositionTally> GetByMatterAsync(Guid matterId, CancellationToken ct)
    {
        if (matterId == Guid.Empty)
            throw new ArgumentException("A matter is required.", nameof(matterId));

        var rows = await AggregateAsync(BuildFetch(["disposition"], matterId, null), ct).ConfigureAwait(false);
        return DispositionTally.From(rows.Select(r => (Disposition: (InquiryDisposition)ReadInt(r, "disposition"), Count: ReadInt(r, "n"))));
    }

    /// <summary>Resolved Inquiries grouped by outside firm; <paramref name="firmId"/> narrows to one firm (across all matters).</summary>
    public async Task<IReadOnlyList<FirmDispositionTally>> GetByOutsideFirmAsync(Guid? firmId, CancellationToken ct)
    {
        if (firmId == Guid.Empty)
            throw new ArgumentException("A firm id cannot be empty.", nameof(firmId));

        var rows = await AggregateAsync(BuildFetch(["firm", "disposition"], null, firmId), ct).ConfigureAwait(false);
        return rows
            .GroupBy(r => AsGuid(r, "firm"))
            .Select(g => new FirmDispositionTally(
                g.Key,
                DispositionTally.From(g.Select(r => (Disposition: (InquiryDisposition)ReadInt(r, "disposition"), Count: ReadInt(r, "n"))))))
            .OrderBy(f => f.FirmId)
            .ToList();
    }

    /// <summary>
    /// The aggregate: resolved (disposition set) OUTBOUND service requests, counted per requested group. No state filter:
    /// resolving closes the row, and the closed ones are exactly what accrues.
    /// </summary>
    internal static string BuildFetch(string[] groups, Guid? matterId, Guid? firmId)
    {
        var inv = CultureInfo.InvariantCulture;
        var attrs = string.Concat(groups.Select(g => g switch
        {
            "disposition" => "<attribute name=\"sprk_disposition\" alias=\"disposition\" groupby=\"true\"/>",
            "firm" => "<attribute name=\"sprk_regardingorganization\" alias=\"firm\" groupby=\"true\"/>",
            _ => throw new ArgumentOutOfRangeException(nameof(groups), g, null),
        }));
        var filter = $"<condition attribute=\"sprk_direction\" operator=\"eq\" value=\"{BudgetInquiryExecutor.DirectionOutbound.ToString(inv)}\"/>"
            + "<condition attribute=\"sprk_disposition\" operator=\"not-null\"/>"
            + (matterId is { } m ? $"<condition attribute=\"sprk_regardingmatter\" operator=\"eq\" value=\"{m:D}\"/>" : "")
            + (firmId is { } f ? $"<condition attribute=\"sprk_regardingorganization\" operator=\"eq\" value=\"{f:D}\"/>" : "");
        return $"<fetch aggregate=\"true\"><entity name=\"{ServiceRequest}\">"
            + "<attribute name=\"sprk_servicerequestid\" alias=\"n\" aggregate=\"count\"/>"
            + attrs + $"<filter>{filter}</filter></entity></fetch>";
    }

    private async Task<List<JsonElement>> AggregateAsync(string fetchXml, CancellationToken ct)
    {
        var response = await user.GetAsync($"sprk_servicerequests?fetchXml={Uri.EscapeDataString(fetchXml)}", ct).ConfigureAwait(false);
        return ReadRows(response);
    }

    internal static List<JsonElement> ReadRows(DataverseUserResponse response)
    {
        if (!response.IsSuccess)
            throw new InvalidOperationException($"The dispositions could not be read ({response.ErrorCode}).");
        if (response.Body is not { } body || !body.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The dispositions could not be read (unexpected response).");
        return value.EnumerateArray().ToList();
    }

    internal static int ReadInt(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : throw new InvalidOperationException($"The aggregate row has no numeric '{name}'.");

    internal static Guid? AsGuid(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) ? g : null;
}
