using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using Sprk.Bff.Api.Api.Events.Dtos;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Events;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Tasks;

// COMPONENT JUSTIFICATION (CLAUDE.md section 11), task 025.
//   Existing: McpToolResults.FromError / McpToolError (the generalised error result), DecisionToolCall (success result, telemetry line),
//     DecisionsTextSummary.ConsoleBaseUrl (the validated https base of Dataverse:EnvironmentUrl), WorklistItemSummary.Clip (JSON-byte
//     clip), ProblemDetailsHelper.RecordUnavailableReasonCode (the REST uniform not-found) and EventCapabilityResult.ReasonCode.
//   Extension: this class calls all of them (and GetTaskTool reuses DocumentToolSupport.TryReadStringArguments, the closed
//     required-string argument check, as it is) and adds only what no one has: the row projection of an event, the one not-found,
//     the capability-outcome to tool-error mapping, and the open link. DocumentToolSupport.NotFound is documented as the document
//     tools' own, so the events not-found is declared here (the same REST uniform answer, written once for the two task tools).
//   Cost of doing nothing: the two task tools would each carry a copy of the row projection and the outcome mapping, and a list and a
//     get could drift to different answers for the same event (the FR-07 / FR-15 negative cases).

/// <summary>
/// What <see cref="ListTasksTool"/> and <see cref="GetTaskTool"/> share (spaarkeai-mcp-bff-api-r2 task 025, spec FR-07 read half, FR-15,
/// NFR-02, NFR-05, NFR-07, ADR-015). A "task" is a Spaarke event (<c>sprk_event</c>).
/// </summary>
internal static class TaskToolSupport
{
    /// <summary>Spec NFR-02: every result under 64 KiB.</summary>
    internal const int ResultLimitBytes = 64 * 1024;

    /// <summary>
    /// What the rows of a list may cost together (each row's JSON plus its text line), as escaped JSON bytes. The 4 KiB left to the
    /// 64 KiB limit hold the summary line and the body's other members (about 1 KiB) with room to spare.
    /// </summary>
    internal const int RowBudgetBytes = ResultLimitBytes - 4 * 1024;

    /// <summary>The longest subject a list row carries, as JSON bytes (a subject is free text; the default encoder writes a non-ASCII character as up to six bytes).</summary>
    internal const int ListSubjectBytes = 120;

    /// <summary>The longest regarding record name a list row carries, as JSON bytes.</summary>
    internal const int ListRegardingNameBytes = 80;

    /// <summary>The longest subject a get result carries, as JSON bytes.</summary>
    internal const int GetSubjectBytes = 400;

    /// <summary>The longest regarding record name a get result carries, as JSON bytes.</summary>
    internal const int GetRegardingNameBytes = 200;

    /// <summary>The <c>reasonCode</c> of a call whose arguments are wrong: this host's own, the one the document tools use.</summary>
    internal const string InvalidArgumentReasonCode = "mcp.invalid_argument";

    /// <summary>The <c>reasonCode</c> of a list the capability could not answer (REST: a bare 500).</summary>
    internal const string ListFailedReasonCode = "events.list_failed";

    /// <summary>The <c>reasonCode</c> of a get the capability could not answer (REST: a bare 500).</summary>
    internal const string GetFailedReasonCode = "events.get_failed";

    /// <summary>The REST uniform not-found's detail (<c>ProblemDetailsHelper.UniformRecordNotFound</c>), word for word.</summary>
    internal const string NotFoundMessage = "The requested record was not found.";

    /// <summary>
    /// THE not-found of the task tools: the REST uniform 404's <c>reasonCode</c>, message and status, for an event that does not exist and
    /// for one the caller may not read, so nothing here tells them apart (FR-15, NFR-07: no existence oracle). It names no id.
    /// </summary>
    internal static McpToolError NotFound { get; } =
        new(ProblemDetailsHelper.RecordUnavailableReasonCode, NotFoundMessage, StatusCodes.Status404NotFound);

    /// <summary>No signed-in caller: nothing was read and nothing falls back to an app-only client (ADR-028 A10, FR-15).</summary>
    internal static McpToolError UserContextRequired { get; } =
        new(DataverseUserClientErrorCodes.UserContextRequired, "The request has no signed-in caller.", StatusCodes.Status401Unauthorized);

    /// <summary>An <c>isError</c> result for <paramref name="error"/>, with the request's correlation id.</summary>
    internal static CallToolResult Failure(McpToolError error, HttpContext? http) =>
        McpToolResults.FromError(error, http?.TraceIdentifier);

    /// <summary>A wrong-arguments error whose <paramref name="message"/> says how to fix the call (names arguments and values only).</summary>
    internal static CallToolResult InvalidArgument(string message, HttpContext? http) =>
        Failure(new McpToolError(InvalidArgumentReasonCode, message, StatusCodes.Status400BadRequest), http);

    /// <summary>
    /// The tool's answer for a capability outcome that is not <see cref="EventCapabilityStatus.Ok"/>: the REST route's reason code and
    /// status for the same outcome, with fixed text (never a record name, id or the capability's refusal detail, ADR-015).
    /// Fail closed: an outcome a read does not produce is the fault answer.
    /// </summary>
    /// <param name="status">The capability's status.</param>
    /// <param name="reasonCode">The capability's own <c>ReasonCode</c> for it, when REST carries one.</param>
    /// <param name="invalidMessage">The capability's text for <see cref="EventCapabilityStatus.Invalid"/> (REST's 400 text: names the request member, not a value).</param>
    /// <param name="failedReasonCode">The tool's code for a fault.</param>
    /// <param name="failedMessage">The tool's text for a fault.</param>
    internal static McpToolError ErrorOf(
        EventCapabilityStatus status, string? reasonCode, string? invalidMessage, string failedReasonCode, string failedMessage) =>
        status switch
        {
            EventCapabilityStatus.UserContextRequired => UserContextRequired,
            EventCapabilityStatus.NotFound => NotFound,
            EventCapabilityStatus.Invalid => new McpToolError(
                InvalidArgumentReasonCode,
                string.IsNullOrWhiteSpace(invalidMessage) ? "A filter value is not valid. Send the call again with a valid value." : invalidMessage,
                StatusCodes.Status400BadRequest),
            EventCapabilityStatus.CallerUnresolved => new McpToolError(
                reasonCode ?? EventsCapability.CallerUnresolvedReasonCode,
                "The signed-in user could not be resolved to a Spaarke user, so their tasks cannot be listed.",
                StatusCodes.Status403Forbidden),
            EventCapabilityStatus.Forbidden => new McpToolError(
                reasonCode ?? "sdap.access.deny.insufficient_rights",
                "The signed-in user may not read this task.",
                StatusCodes.Status403Forbidden),
            _ => new McpToolError(failedReasonCode, failedMessage, StatusCodes.Status500InternalServerError),
        };

    /// <summary>The <c>yyyy-MM-dd</c> text of a calendar date; null when there is none.</summary>
    internal static string? DateText(DateOnly? date) => date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The slim, model-visible row of one event (ADR-015, NFR-07): id, subject, due date, status, priority and the regarding record's name.
    /// No description, no event type, no owner, no audit fields.
    /// </summary>
    internal static TaskRow RowOf(EventDto task, int subjectBytes, int regardingNameBytes) =>
        new(
            task.Id.ToString("D"),
            WorklistItemSummary.Clip(task.Subject, subjectBytes) ?? string.Empty,
            DateText(task.DueDate),
            task.Status,
            task.PriorityName,
            WorklistItemSummary.Clip(task.RegardingRecordName, regardingNameBytes));

    /// <summary>A bounded one-line text for the model: subject, due date, status, priority and id.</summary>
    internal static string LineOf(TaskRow row)
    {
        var parts = new List<string>(3);
        if (row.DueDate is not null)
        {
            parts.Add($"due {row.DueDate}");
        }

        if (!string.IsNullOrEmpty(row.Status))
        {
            parts.Add(McpText.SingleLine(row.Status));
        }

        if (!string.IsNullOrEmpty(row.Priority))
        {
            parts.Add(McpText.SingleLine(row.Priority));
        }

        // The subject is user-authored: on one line, so it cannot start a row or a link line of its own. Structured content keeps it as it is.
        var subject = McpText.SingleLine(row.Subject);
        return parts.Count == 0 ? $"{subject} (id {row.Id})" : $"{subject} ({string.Join(", ", parts)}; id {row.Id})";
    }

    /// <summary>
    /// The cost of one list row in the result: its JSON as <c>structuredContent</c> plus its line as <c>content</c>, both as escaped JSON bytes.
    /// </summary>
    internal static int CostOf(TaskRow row) =>
        JsonSerializer.SerializeToUtf8Bytes(row, DecisionToolCall.Json).Length
        + JsonEncodedText.Encode("- " + LineOf(row) + "\n").EncodedUtf8Bytes.Length;

    /// <summary>
    /// The link that opens an event in Spaarke: the record form on <paramref name="baseUrl"/> (<see cref="DecisionsTextSummary.ConsoleBaseUrl"/>
    /// of <c>Dataverse:EnvironmentUrl</c>), the shape the side-effect gate composes for any record. Null without a base.
    /// </summary>
    internal static string? OpenLink(string? baseUrl, Guid eventId) =>
        baseUrl is null ? null : $"{baseUrl}/main.aspx?pagetype=entityrecord&etn={EventsCapability.EventTable}&id={eventId:D}";
}

/// <summary>One task as the list tool returns it (and the get tool's core): id, subject, due date, status, priority, regarding record name.</summary>
internal sealed record TaskRow(string Id, string Subject, string? DueDate, string Status, string? Priority, string? RegardingRecordName);
