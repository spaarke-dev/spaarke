using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using Sprk.Bff.Api.Api.Signals;

namespace Sprk.Bff.Api.Api.Mcp;

/// <summary>
/// A tool's domain failure, for any MCP tool (spaarkeai-mcp-bff-api-r2 task 020, spec "Tools"): the same four facts a decision tool's
/// <see cref="DecisionToolError"/> carries, with no decision type in it. <see cref="McpToolResults.FromError(McpToolError, string?)"/>
/// turns it into an <c>isError</c> tool result.
/// </summary>
/// <param name="ReasonCode">The <c>reasonCode</c> the REST route returns for the same failure (one vocabulary per slice).</param>
/// <param name="Message">How to fix the call. The reason and the fix only: never record text, party names or matter names (ADR-015).</param>
/// <param name="Status">The HTTP status the REST route answers for the same failure. Information for the host, never a transport status.</param>
/// <param name="Partial">What a failed write left behind, when the slice reports it (the decision commit's D-17 facts); null otherwise.
/// It is serialized as its runtime type, so it must hold codes, ids and flags only (ADR-015).</param>
public sealed record McpToolError(string ReasonCode, string Message, int Status, object? Partial = null);

/// <summary>
/// Turns a tool's domain failure into an MCP tool result (spaarkeai-mcp-bff-api-r1 FR-09, design §8 B-2; generalised beyond the
/// decision slice by spaarkeai-mcp-bff-api-r2 task 020). A domain failure is a tool result with <c>isError: true</c>, never a
/// transport error: the model and the widget read it and can correct the call, while an HTTP or JSON-RPC error would be shown as
/// "the connector failed" (ADR-019).
/// </summary>
public static class McpToolResults
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// <c>content</c> is the error's <see cref="McpToolError.Message"/> as text (how to fix the call; it never carries record text,
    /// ADR-015). <c>structuredContent</c> is <c>{ reasonCode, message, status, correlationId?, partial? }</c>: the REST route's
    /// <c>reasonCode</c> vocabulary, the HTTP <see cref="McpToolError.Status"/> the REST route answers for the same failure (the
    /// widget's failure display reads it), <paramref name="correlationId"/> as the support reference (the request's
    /// <c>TraceIdentifier</c>, the value the REST ProblemDetails carry as <c>correlationId</c>; absent when there is no request context),
    /// and <c>partial</c> when the slice reports what a failed write left behind. Neither is a transport status: the JSON-RPC and HTTP
    /// answer is still a successful tool result.
    /// </summary>
    public static CallToolResult FromError(McpToolError error, string? correlationId)
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

    /// <summary>
    /// A decision-slice failure: <see cref="DecisionToolError"/> maps field for field onto <see cref="McpToolError"/>, so the decision
    /// tools' result is byte-identical to r1's (a failed commit's <c>partial</c> carries the D-17 facts: which writes landed and whether
    /// a retry resumes). See <see cref="FromError(McpToolError, string?)"/> for the shape.
    /// </summary>
    public static CallToolResult FromError(DecisionToolError error, string? correlationId)
    {
        ArgumentNullException.ThrowIfNull(error);

        return FromError(new McpToolError(error.ReasonCode, error.Message, error.Status, error.Partial), correlationId);
    }

    // Partial is declared object, so System.Text.Json writes it as its runtime type (a DecisionToolPartial writes the same members it
    // wrote when this field was typed DecisionToolPartial); the member order is the r1 order.
    private sealed record ErrorBody(string ReasonCode, string Message, int Status, string? CorrelationId, object? Partial);
}
