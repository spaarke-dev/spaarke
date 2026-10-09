using System.Data;
using System.Globalization;
using System.Text.Json;
using Spaarke.Dataverse;
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

/// <summary>How a reply's attempt to resolve its Inquiry ended.</summary>
public enum InquiryResolutionOutcome
{
    /// <summary>The Inquiry now carries the disposition and is closed.</summary>
    Resolved,
    /// <summary>Nothing written: the communication is missing, is not inbound, or is not filed against a service request.</summary>
    NotAReply,
    /// <summary>Nothing written: the service request is missing or is not an Outbound inquiry.</summary>
    NotAnInquiry,
    /// <summary>Nothing written: the Inquiry already has a disposition (the first reply wins; a later reply never overwrites it).</summary>
    AlreadyResolved,
    /// <summary>Nothing written: the request is malformed (empty id, or a disposition that is not one of the four).</summary>
    Invalid,
    /// <summary>Dataverse failed; the Inquiry is unchanged.</summary>
    Failed,
}

public sealed record InquiryResolutionResult(InquiryResolutionOutcome Outcome, Guid? ServiceRequestId = null, string? Error = null)
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
/// Task 071 (spec FR-37, criterion 10): a reply resolves its Inquiry with a typed disposition, and the dispositions are
/// queryable per matter and per outside firm. Both are derived from existing relationships on
/// <c>sprk_servicerequest</c> (<c>sprk_regardingmatter</c>, <c>sprk_regardingorganization</c>, written by the Inquiry
/// executor, task 070): no denormalised column was added.
/// </summary>
/// <remarks>
/// <para><b>Resolution is an application write</b> (an inbound email has no calling user): it is update-only and, when the
/// row's <c>versionnumber</c> is known, conditional on it, so two replies racing for the same Inquiry cannot both win.
/// The reply is tied to the Inquiry through the association ladder: the Inquiry email's primary association is the
/// service request (task 070), so a reply carries <c>sprk_communication.sprk_regardingservicerequest</c>.</para>
/// <para><b>The disposition value is an input.</b> Nothing in R1 reads a reply's text to choose it (ADR-013: no AI in the
/// executor path); the caller (the reviewing human, or a later classifier) supplies it.</para>
/// <para><b>Queries run as the caller</b> (<see cref="IDataverseUserClient"/>): a user sees the tallies of the Inquiries
/// they may read, nothing more. They are server-side aggregates, so there is no paging to truncate.</para>
/// </remarks>
public sealed class InquiryDispositionService(
    IFieldMappingDataverseService appOnly,
    IDataverseUserClient user,
    ILogger<InquiryDispositionService> logger)
{
    internal const string ServiceRequest = "sprk_servicerequest";
    internal const string Communication = "sprk_communication";
    /// <summary>sprk_communication.sprk_direction: Incoming.</summary>
    internal const int CommunicationIncoming = 100000000;
    internal const int StateInactive = 1;
    internal const int StatusInactive = 2;

    /// <summary>
    /// The inbound reply <paramref name="replyCommunicationId"/> resolves the Inquiry it is filed against with
    /// <paramref name="disposition"/>: sets <c>sprk_disposition</c> and closes the service request.
    /// </summary>
    public async Task<InquiryResolutionResult> ResolveFromReplyAsync(
        Guid replyCommunicationId, InquiryDisposition disposition, CancellationToken ct)
    {
        if (replyCommunicationId == Guid.Empty || !Enum.IsDefined(disposition))
            return new(InquiryResolutionOutcome.Invalid, Error: "A reply and one of the four dispositions are required.");

        try
        {
            var reply = await appOnly.RetrieveRecordFieldsAsync(
                Communication, replyCommunicationId, ["sprk_direction", "_sprk_regardingservicerequest_value"], ct).ConfigureAwait(false);

            if (reply.Count == 0 || AsLong(reply, "sprk_direction") != CommunicationIncoming
                || AsGuid(reply, "_sprk_regardingservicerequest_value") is not { } serviceRequestId)
                return new(InquiryResolutionOutcome.NotAReply, Error: "The communication is not an inbound reply to a service request.");

            var inquiry = await appOnly.RetrieveRecordFieldsAsync(
                ServiceRequest, serviceRequestId, ["sprk_direction", "sprk_disposition", "versionnumber"], ct).ConfigureAwait(false);

            if (inquiry.Count == 0 || AsLong(inquiry, "sprk_direction") != BudgetInquiryExecutor.DirectionOutbound)
                return new(InquiryResolutionOutcome.NotAnInquiry, serviceRequestId, "The service request is not an outbound inquiry.");

            if (AsLong(inquiry, "sprk_disposition") is not null)
                return new(InquiryResolutionOutcome.AlreadyResolved, serviceRequestId, "The inquiry already has a disposition.");

            var fields = new Dictionary<string, object?>
            {
                ["sprk_disposition"] = (int)disposition,
                ["statecode"] = StateInactive,
                ["statuscode"] = StatusInactive,
            };

            if (AsLong(inquiry, "versionnumber") is { } version)
                await appOnly.UpdateRecordFieldsIfUnchangedAsync(ServiceRequest, serviceRequestId, fields, version, ct).ConfigureAwait(false);
            else
                await appOnly.UpdateExistingRecordFieldsAsync(ServiceRequest, serviceRequestId, fields, ct).ConfigureAwait(false);

            return new(InquiryResolutionOutcome.Resolved, serviceRequestId);
        }
        catch (DBConcurrencyException)
        {
            return new(InquiryResolutionOutcome.AlreadyResolved, Error: "Another reply resolved the inquiry first.");
        }
        catch (KeyNotFoundException)
        {
            return new(InquiryResolutionOutcome.NotAnInquiry, Error: "The service request no longer exists.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Inquiry not resolved | ReplyCommunicationId: {ReplyId}", replyCommunicationId);
            return new(InquiryResolutionOutcome.Failed, Error: "The inquiry could not be resolved.");
        }
    }

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

    private static long? AsLong(Dictionary<string, object?> fields, string name) =>
        fields.TryGetValue(name, out var v) && v is not null ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : null;

    private static Guid? AsGuid(Dictionary<string, object?> fields, string name) =>
        fields.TryGetValue(name, out var v) && v is string s && Guid.TryParse(s, out var g) ? g : null;
}
