using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Api.Events;

/// <summary>
/// Body of <c>PATCH /api/v1/events/{id}/due-assignee</c> (ontology platform R1 task 044; spec FR-52, FR-61; D-27): the
/// event's due date and/or its assignee, and nothing else. At least one is required.
/// </summary>
/// <param name="DueDate">The new <c>sprk_duedate</c> (Date Only). NEVER <c>sprk_finalduedate</c> (D-27/D-63).</param>
/// <param name="AssigneeContactId">The contact the event is reassigned to: <c>sprk_assignedto</c> (a contact lookup),
/// <c>statuscode</c> Reassigned and <c>sprk_reassignedby</c>.</param>
public sealed record UpdateEventDueAssigneeRequest(DateOnly? DueDate, Guid? AssigneeContactId);

/// <summary>What the route returns: what was written, no more.</summary>
public sealed record UpdateEventDueAssigneeResponse(Guid Id, DateOnly? DueDate, Guid? AssigneeContactId, bool Reassigned);

/// <summary>How <see cref="EventDueAssigneeWrite.ApplyAsync"/> ended.</summary>
internal enum EventDueAssigneeOutcome
{
    Written,

    /// <summary>The caller cannot read the event (Dataverse said 403/404 on the caller's own read).</summary>
    NotFound,

    /// <summary>The event is not open work, so it is neither rescheduled nor reassigned (same gate as complete).</summary>
    InvalidState,

    /// <summary>The caller's own PATCH was refused by Dataverse (a missing right or a field-secured column).</summary>
    Denied,

    /// <summary>Dataverse or the network failed for a reason that is not the caller's rights.</summary>
    Failed,
}

internal sealed record EventDueAssigneeResult(EventDueAssigneeOutcome Outcome, int? DataverseStatus = null, string? Detail = null)
{
    public bool IsWritten => Outcome == EventDueAssigneeOutcome.Written;
}

/// <summary>
/// THE one narrow event write for a due date and an assignee (task 044; #1312's one route per table; #29). It runs AS THE
/// CALLER (<see cref="IDataverseUserClient"/>: their rights, their audit) because no BFF route wrote an event's due date or
/// assignee once #1312 deleted the general <c>PUT /api/v1/events/{id}</c>. The route and the Do-lane executors
/// (<c>reschedule</c>, <c>reassign</c>) both call <see cref="ApplyAsync"/>, so there is exactly one implementation.
/// </summary>
/// <remarks>
/// <para><b>Writes exactly:</b> reschedule — <c>sprk_duedate</c>, <c>sprk_rescheduleddate</c>, <c>sprk_rescheduledby</c>;
/// reassign — <c>sprk_assignedto</c> (contact), <c>statuscode</c> = Reassigned (659490007, statecode stays Active),
/// <c>sprk_reassigneddate</c>, <c>sprk_reassignedby</c>. It never names <c>sprk_finalduedate</c>.</para>
/// <para><b>Gate.</b> Only open work (<see cref="EventStatusCode.IsOpenWork"/>, owner decision A: Draft, Open, On Hold,
/// Reassigned) can be rescheduled or reassigned: the predicate the complete route already uses. The read that checks it runs
/// as the caller, so an event the caller cannot read is a not-found, never a disclosure of its status.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <c>RefileEventAsync</c> changes only filing columns
/// and the child-records PATCH does not list <c>sprk_event</c>, so nothing writes an event's due date or assignee. Extension: a
/// second filing-style route would still not write these columns; the events family is where #1312's one-route-per-table
/// rule puts an event write. Cost of doing nothing: the Do lane's Reschedule and Reassign on an event cannot be recorded
/// through a decision.</para>
/// </remarks>
internal static class EventDueAssigneeWrite
{
    private const string EventSet = "sprk_events";

    internal static string? ShapeProblem(UpdateEventDueAssigneeRequest? request)
    {
        if (request is null)
        {
            return "A request body is required.";
        }

        if (request.DueDate is null && request.AssigneeContactId is null)
        {
            return "Name a new due date, a new assignee, or both.";
        }

        if (request.AssigneeContactId is { } assignee && assignee == Guid.Empty)
        {
            return "The assignee must be a contact id.";
        }

        return null;
    }

    /// <summary>
    /// The payload the caller's PATCH carries; separate so tests pin the exact column set. The <c>@odata.bind</c> names are the
    /// lookups' NAVIGATION PROPERTIES, which the Web API matches case-sensitively (live, spaarkedev1, 2026-10-09:
    /// <c>$expand=sprk_AssignedTo</c> 200, <c>$expand=sprk_assignedto</c> 400): <c>sprk_AssignedTo</c>,
    /// <c>sprk_ReassignedBy</c>, <c>sprk_RescheduledBy</c>. Plain column names stay lower case.
    /// </summary>
    internal static string BuildPatchJson(UpdateEventDueAssigneeRequest request, Guid? callerContactId, DateTimeOffset now)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (request.DueDate is { } due)
        {
            body["sprk_duedate"] = due.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            body["sprk_rescheduleddate"] = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            if (callerContactId is { } rescheduler && rescheduler != Guid.Empty)
            {
                body["sprk_RescheduledBy@odata.bind"] = $"/contacts({rescheduler:D})";
            }
        }

        if (request.AssigneeContactId is { } assignee)
        {
            body["sprk_AssignedTo@odata.bind"] = $"/contacts({assignee:D})";
            body["statuscode"] = EventStatusCode.Reassigned;
            body["sprk_reassigneddate"] = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            if (callerContactId is { } reassigner && reassigner != Guid.Empty)
            {
                body["sprk_ReassignedBy@odata.bind"] = $"/contacts({reassigner:D})";
            }
        }

        return JsonSerializer.Serialize(body);
    }

    /// <summary>
    /// The event's <c>statuscode</c>, read AS THE CALLER, with the open-work gate applied: the refusal to return when the event
    /// cannot be read (not found) or is not open work (invalid state), else null with the status. No write.
    /// </summary>
    internal static async Task<(EventDueAssigneeResult? Refusal, int Status)> ReadStatusAsync(
        IDataverseUserClient user, Guid eventId, CancellationToken ct)
    {
        // The caller's own read: a row they cannot read is a not-found, and the gate below never runs for it.
        var current = await user.GetAsync($"{EventSet}({eventId:D})?$select=statuscode", ct).ConfigureAwait(false);
        if (!current.IsSuccess)
        {
            return (current.StatusCode is 403 or 404
                ? new EventDueAssigneeResult(EventDueAssigneeOutcome.NotFound, current.StatusCode)
                : new EventDueAssigneeResult(EventDueAssigneeOutcome.Failed, current.StatusCode, current.ErrorMessage), 0);
        }

        var statusCode = current.Body is { ValueKind: JsonValueKind.Object } row
            && row.TryGetProperty("statuscode", out var sc) && sc.ValueKind == JsonValueKind.Number && sc.TryGetInt32(out var parsed)
                ? parsed
                : (int?)null;
        if (statusCode is not { } status || !EventStatusCode.IsOpenWork(status))
        {
            return (new EventDueAssigneeResult(EventDueAssigneeOutcome.InvalidState, null,
                "Only an event that is open work (Draft, Open, On Hold or Reassigned) can be rescheduled or reassigned."), 0);
        }

        return (null, status);
    }

    internal static async Task<EventDueAssigneeResult> ApplyAsync(
        IDataverseUserClient user,
        Guid eventId,
        UpdateEventDueAssigneeRequest request,
        Guid? callerContactId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var (read, _) = await ReadStatusAsync(user, eventId, ct).ConfigureAwait(false);
        if (read is not null)
        {
            return read;
        }

        var patch = await user.PatchAsync($"{EventSet}({eventId:D})", BuildPatchJson(request, callerContactId, now), ct)
            .ConfigureAwait(false);
        if (patch.IsSuccess)
        {
            return new EventDueAssigneeResult(EventDueAssigneeOutcome.Written);
        }

        return patch.StatusCode is 401 or 403
            ? new EventDueAssigneeResult(EventDueAssigneeOutcome.Denied, patch.StatusCode, patch.ErrorMessage)
            : patch.StatusCode == 404
                ? new EventDueAssigneeResult(EventDueAssigneeOutcome.NotFound, patch.StatusCode)
                : new EventDueAssigneeResult(EventDueAssigneeOutcome.Failed, patch.StatusCode, patch.ErrorMessage);
    }
}
