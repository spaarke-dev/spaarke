using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace Sprk.Bff.Api.Api.Mcp;

// COMPONENT JUSTIFICATION (CLAUDE.md section 11), spaarkeai-mcp-bff-api-r2 task 020.
//   Existing: McpToolResults shapes an error result; the legacy agent's start/poll pair (AgentEndpoints.cs:461-481 and
//     PlaybookStatusEndpoints.cs at 37f0864afe, deleted by task 001) was the only start/status shape, and it was REST, not MCP.
//   Extension: McpToolResults builds errors, not successes, and no live code builds an MCP start or status result to extend; this is a
//     static builder of the two FR-14 shapes beside it, with no DI registration, package, state or background work.
//   Cost of doing nothing: each long-running tool (tasks 030, 032, 034) would invent its own start/status shape, failing FR-14's
//     "the pattern is shared code" acceptance, and the 30 s rule would have no single home.

/// <summary>The state of a long-running tool run, as an MCP status result names it (FR-14): <c>queued</c>, <c>running</c>,
/// <c>completed</c> or <c>failed</c>.</summary>
public enum McpRunState
{
    /// <summary>Accepted, not yet started.</summary>
    Queued,

    /// <summary>Started, not yet finished.</summary>
    Running,

    /// <summary>Finished; a status result may carry the result.</summary>
    Completed,

    /// <summary>Finished without a result.</summary>
    Failed,
}

/// <summary>
/// The shared start-then-status pattern of spec FR-14 (owner Q3): every MCP tool answers within <see cref="Budget"/>; a tool whose work
/// can take longer starts it, answers at once with <see cref="Started"/>, and a status tool answers each poll with <see cref="Status"/>.
/// </summary>
/// <remarks>
/// <para><b>Only result shapes.</b> The helper builds two <see cref="CallToolResult"/>s. It starts nothing, polls nothing, stores nothing
/// and schedules nothing (ADR-052): the run belongs to the service the tool calls as the caller, and its status to that service's run
/// record (FR-13). The model or the widget polls by calling the status tool again.</para>
/// <para><b>The console link.</b> <c>consoleLink</c> is never a URL the tool writes by hand. A tool may not inject a type from
/// <c>Services/Ai/**</c> outside <c>PublicContracts</c> (ADR-013; the MCP ledger guard fails it), so it cannot use
/// <c>HandoffUrlBuilder</c> directly, and <c>IDeepLinkBuilder</c> builds only <c>sprk_todo</c> links. No public-contract member returns a
/// run link yet: the first long-running tool task (030) adds one, and later tools reuse it. The helper refuses anything but an absolute https URL with no user
/// information.</para>
/// <para><b>Errors.</b> A refusal to start, or a status read the caller may not make, is not a run state: it goes through
/// <see cref="McpToolResults.FromError(McpToolError, string?)"/> with the REST route's <c>reasonCode</c> (ADR-019). A run that
/// <see cref="McpRunState.Failed"/> is a successful status call reporting that state, so its result is not <c>isError</c>.</para>
/// </remarks>
public static class McpLongRunning
{
    /// <summary>The longest any MCP tool call may take, in seconds (FR-14, owner Q3).</summary>
    public const int BudgetSeconds = 30;

    /// <summary>The longest any MCP tool call may take (FR-14, owner Q3). Work that can take longer uses <see cref="Started"/>.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(BudgetSeconds);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// The start result: <c>structuredContent</c> <c>{ runId, status, consoleLink }</c> and <paramref name="summary"/> as the one-line
    /// <c>content</c> text (what was started and that the status tool reports it; no record text, ADR-015).
    /// </summary>
    /// <param name="runId">The run's id, the one the status tool takes.</param>
    /// <param name="status"><see cref="McpRunState.Queued"/> or <see cref="McpRunState.Running"/>; a run that already finished is
    /// answered with its result, not with a start.</param>
    /// <param name="consoleLink">Where the user follows the run in Spaarke, built by an existing deep-link builder (see remarks).</param>
    /// <param name="summary">One line of text for the model.</param>
    /// <exception cref="ArgumentException">A blank id or summary, a summary of more than one line, or a link that is not an absolute
    /// https URL with no user information.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is a finished state.</exception>
    public static CallToolResult Started(string runId, McpRunState status, string consoleLink, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (status is not (McpRunState.Queued or McpRunState.Running))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A start result is queued or running; answer a finished run with its result.");
        }

        if (!Uri.TryCreate(consoleLink, UriKind.Absolute, out var link) || link.Scheme != Uri.UriSchemeHttps || link.UserInfo.Length > 0)
        {
            throw new ArgumentException("The console link must be an absolute https URL with no user information, built by a deep-link builder.", nameof(consoleLink));
        }

        return Result(OneLine(summary, nameof(summary)), new StartBody(runId, status, consoleLink));
    }

    /// <summary>
    /// The status result: <c>structuredContent</c> <c>{ runId, status, progress?, result? }</c>. <c>content</c> is
    /// <paramref name="summary"/> when given (for a completed run, the model-visible answer), else a fixed line naming the state.
    /// </summary>
    /// <param name="runId">The run's id.</param>
    /// <param name="status">Where the run is.</param>
    /// <param name="progress">Percent done, 0 to 100, when the run reports it.</param>
    /// <param name="result">The run's result, serialized as its runtime type (model-visible: slim and with no privileged field, NFR-07).</param>
    /// <param name="summary">One line of text for the model; null for the fixed line.</param>
    /// <exception cref="ArgumentException">A blank id, or a summary that is blank or more than one line.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="progress"/> outside 0 to 100.</exception>
    public static CallToolResult Status(string runId, McpRunState status, int? progress = null, object? result = null, string? summary = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (progress is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(progress), progress, "Progress is a percentage from 0 to 100.");
        }

        var text = summary is null ? DefaultStatusLine(status, progress) : OneLine(summary, nameof(summary));
        return Result(text, new StatusBody(runId, status, progress, result));
    }

    private static string DefaultStatusLine(McpRunState status, int? progress) => status switch
    {
        McpRunState.Queued => "The run is queued. Call the status tool again shortly.",
        McpRunState.Running when progress is { } p => $"The run is running ({p}% done). Call the status tool again shortly.",
        McpRunState.Running => "The run is running. Call the status tool again shortly.",
        McpRunState.Completed => "The run has completed.",
        _ => "The run has failed.",
    };

    private static string OneLine(string text, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, name);
        if (text.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new ArgumentException("The text summary is one line.", name);
        }

        return text;
    }

    private static CallToolResult Result(string text, object structured) =>
        new()
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(structured, Json),
        };

    private sealed record StartBody(string RunId, McpRunState Status, string ConsoleLink);

    private sealed record StatusBody(string RunId, McpRunState Status, int? Progress, object? Result);
}
