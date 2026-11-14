using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using Sprk.Bff.Api.Api.Signals;

namespace Sprk.Bff.Api.Api.Mcp;

/// <summary>
/// Turns a decision-slice failure into an MCP tool result (spaarkeai-mcp-bff-api-r1 FR-09, design §8 B-2). A domain failure is a
/// tool result with <c>isError: true</c>, never a transport error: the model and the widget read it and can correct the call,
/// while an HTTP or JSON-RPC error would be shown as "the connector failed".
/// </summary>
public static class McpToolResults
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// <c>content</c> is the error's <see cref="DecisionToolError.Message"/> as text (how to fix the call; it never carries record
    /// text, ADR-015). <c>structuredContent</c> is <c>{ reasonCode, message, status, correlationId?, partial? }</c>: the REST route's
    /// <c>reasonCode</c> vocabulary, the HTTP <see cref="DecisionToolError.Status"/> the REST route answers for the same failure (the
    /// widget's failure display reads it), <paramref name="correlationId"/> as the support reference (the request's
    /// <c>TraceIdentifier</c>, the value the REST ProblemDetails carry as <c>correlationId</c>; absent when there is no request context),
    /// and <c>partial</c> for a failed commit (the D-17 facts: which writes landed and whether a retry resumes). Neither is a transport
    /// status: the JSON-RPC and HTTP answer is still a successful tool result.
    /// </summary>
    public static CallToolResult FromError(DecisionToolError error, string? correlationId)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = error.Message }],
            StructuredContent = JsonSerializer.SerializeToElement(
                new ErrorBody(error.ReasonCode, error.Message, error.Status, correlationId, error.Partial), Json),
        };
    }

    private sealed record ErrorBody(string ReasonCode, string Message, int Status, string? CorrelationId, DecisionToolPartial? Partial);
}
