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
    /// The deterministic id of an Inquiry's "Record the outcome" To Do: the same Inquiry always yields the same id, which
    /// makes creating it idempotent (a second create is a duplicate key) and finding it to complete it a lookup by id.
    /// </summary>
    internal static Guid TodoIdFor(Guid serviceRequestId)
    {
        var hash = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes("inquiry-outcome-todo:" + serviceRequestId.ToString("D")));
        return new Guid(hash);
    }

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
