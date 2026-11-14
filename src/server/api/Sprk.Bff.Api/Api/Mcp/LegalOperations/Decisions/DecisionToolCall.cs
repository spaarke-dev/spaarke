using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;

// COMPONENT JUSTIFICATION (CLAUDE.md section 11), task 022.
//   Existing: McpToolResults.FromError (Api/Mcp) shapes an error result; nothing reads a tool's raw arguments against a closed set or
//     records per-call telemetry for a tool.
//   Extension: it calls McpToolResults and DecisionResultMapper and adds only the closed-argument check and the one telemetry line.
//   Cost of doing nothing: an invented argument (April issue 2) would be ignored silently instead of refused with a fix, and NFR-05's
//     per-call telemetry (tool, latency, rows, outcome, correlation id) would not exist.

/// <summary>
/// What the decision tools of this folder share: the closed-argument check, the result shapes and the one telemetry line per call
/// (spec NFR-05: tool name, latency, row count, outcome or reason code, correlation id; never an argument, a token, a claim or record text).
/// </summary>
internal static class DecisionToolCall
{
    /// <summary>The <c>outcome</c> of a call that returned a result.</summary>
    public const string Ok = "ok";

    /// <summary>The <c>outcome</c> of a call that ended in an exception (the SDK answers it as a generic tool error).</summary>
    public const string Exception = "exception";

    /// <summary>Web defaults (camelCase); null members are not written, so the size bound holds whatever the host's options.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The value of the one argument <paramref name="name"/> the tool takes (null when absent or JSON null), or the
    /// <c>ontology.decision.invalid_parameter</c> error when the call carries any other argument. The schema says
    /// <c>additionalProperties: false</c>; this is what makes it true, so an invented argument is refused with a fix, never ignored.
    /// </summary>
    public static bool TryReadSingleArgument(
        RequestContext<CallToolRequestParams> request, string toolName, string name, out JsonElement? value, out CallToolResult? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        value = null;
        error = null;
        var arguments = request.Params?.Arguments;
        if (arguments is null)
        {
            return true;
        }

        if (arguments.Keys.Any(k => !string.Equals(k, name, StringComparison.Ordinal)))
        {
            error = InvalidArgument($"{toolName} takes one optional argument, '{name}', and nothing else. Send the call again with only '{name}', or with no arguments.");
            return false;
        }

        if (arguments.TryGetValue(name, out var element) && element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            value = element;
        }

        return true;
    }

    /// <summary>An argument error: the decision slice's <c>ontology.decision.invalid_parameter</c> (400) with a fix-the-call message of
    /// fixed text (it names the argument and its valid values only; never record text).</summary>
    public static CallToolResult InvalidArgument(string message) =>
        McpToolResults.FromError(DecisionResultMapper.ToToolError(DecisionCommitReasons.InvalidParameter, StatusCodes.Status400BadRequest) with
        {
            Message = message,
        });

    /// <summary>A successful result: <paramref name="text"/> as <c>content</c>, <paramref name="structured"/> as <c>structuredContent</c>.</summary>
    public static CallToolResult Success(string text, object structured) =>
        new()
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(structured, Json),
        };

    /// <summary>The <c>outcome</c> recorded for <paramref name="result"/>: <see cref="Ok"/>, or the error's <c>reasonCode</c>.</summary>
    public static string OutcomeOf(CallToolResult result) =>
        result.IsError == true
        && result.StructuredContent is { ValueKind: JsonValueKind.Object } body
        && body.TryGetProperty("reasonCode", out var code)
        && code.ValueKind == JsonValueKind.String
            ? code.GetString()!
            : Ok;

    /// <summary>
    /// The one telemetry line of a call (NFR-05): identifiers, sizes, timings and outcome only. <paramref name="correlationId"/> is the
    /// request's <c>TraceIdentifier</c> (the <c>correlationId</c> REST ProblemDetails carry).
    /// </summary>
    public static void LogCompleted(ILogger logger, string toolName, string outcome, int rowCount, TimeSpan elapsed, string? correlationId) =>
        logger.LogInformation(
            "MCP tool {ToolName} completed: outcome {Outcome}, {RowCount} rows, {ElapsedMs} ms, correlation {CorrelationId}.",
            toolName, outcome, rowCount, (long)elapsed.TotalMilliseconds, correlationId);
}
