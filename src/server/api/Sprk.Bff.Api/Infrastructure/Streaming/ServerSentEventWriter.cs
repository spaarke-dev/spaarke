using System.Text.Json;
using Sprk.Bff.Api.Models.Ai;
using BuildPlan = Sprk.Bff.Api.Models.Ai.BuildPlan;

namespace Sprk.Bff.Api.Infrastructure.Streaming;

/// <summary>
/// Utility for writing Server-Sent Events (SSE) to HTTP responses.
/// Follows the SSE specification with proper event formatting.
/// </summary>
/// <remarks>
/// SSE Format:
/// <code>
/// event: {eventType}
/// data: {json}
///
/// </code>
/// Each event ends with a blank line (\n\n).
/// </remarks>
public static class ServerSentEventWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Write an SSE event to the response with event type header.
    /// Format: "event: {type}\ndata: {json}\n\n"
    /// </summary>
    /// <typeparam name="T">The event type (must inherit from BuilderSseEvent).</typeparam>
    /// <param name="response">The HTTP response to write to.</param>
    /// <param name="sseEvent">The event to serialize and write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task WriteEventAsync<T>(
        HttpResponse response,
        T sseEvent,
        CancellationToken cancellationToken) where T : BuilderSseEvent
    {
        var json = JsonSerializer.Serialize(sseEvent, JsonOptions);
        var eventType = sseEvent.Type;

        // SSE format with event type: "event: {type}\ndata: {json}\n\n"
        var sseData = $"event: {eventType}\ndata: {json}\n\n";

        await response.WriteAsync(sseData, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Write a thinking event.
    /// </summary>
    public static Task WriteThinkingAsync(
        HttpResponse response,
        string content,
        string? step = null,
        CancellationToken cancellationToken = default) =>
        WriteEventAsync(response, ThinkingEvent.Create(content, step), cancellationToken);

    /// <summary>
    /// Write a Dataverse operation event.
    /// </summary>
    public static Task WriteDataverseOperationAsync(
        HttpResponse response,
        string operation,
        string entityType,
        Guid? recordId = null,
        string? description = null,
        CancellationToken cancellationToken = default) =>
        WriteEventAsync(response, DataverseOperationEvent.Create(operation, entityType, recordId, description), cancellationToken);

    /// <summary>
    /// Write a canvas patch event.
    /// </summary>
    public static Task WriteCanvasPatchAsync(
        HttpResponse response,
        Services.Ai.CanvasPatch patch,
        string? description = null,
        CancellationToken cancellationToken = default) =>
        WriteEventAsync(response, CanvasPatchEvent.Create(patch, description), cancellationToken);

    /// <summary>
    /// Write a message event.
    /// </summary>
    public static Task WriteMessageAsync(
        HttpResponse response,
        string content,
        bool isPartial = false,
        CancellationToken cancellationToken = default) =>
        WriteEventAsync(response, MessageEvent.Create(content, isPartial), cancellationToken);

    /// <summary>
    /// Write a done event.
    /// </summary>
    public static Task WriteDoneAsync(
        HttpResponse response,
        int operationCount = 0,
        string? summary = null,
        Services.Ai.SessionState? sessionState = null,
        CancellationToken cancellationToken = default) =>
        WriteEventAsync(response, DoneEvent.Create(operationCount, summary, sessionState), cancellationToken);

    /// <summary>
    /// Write an error event.
    /// </summary>
    public static Task WriteErrorAsync(
        HttpResponse response,
        string message,
        string? code = null,
        bool isRecoverable = true,
        string? suggestedAction = null,
        CancellationToken cancellationToken = default) =>
        WriteEventAsync(response, ErrorEvent.Create(message, code, isRecoverable, suggestedAction), cancellationToken);

    /// <summary>
    /// Write a clarification event.
    /// </summary>
    public static Task WriteClarificationAsync(
        HttpResponse response,
        string question,
        string[]? options = null,
        CancellationToken cancellationToken = default) =>
        WriteEventAsync(response, ClarificationEvent.Create(question, options), cancellationToken);

    /// <summary>
    /// Write a plan preview event.
    /// </summary>
    public static Task WritePlanPreviewAsync(
        HttpResponse response,
        BuildPlan plan,
        CancellationToken cancellationToken = default) =>
        WriteEventAsync(response, PlanPreviewEvent.Create(plan), cancellationToken);

    /// <summary>
    /// Set SSE headers on the response.
    /// Must be called before writing any events.
    /// </summary>
    public static void SetSseHeaders(HttpResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
    }

    /// <summary>
    /// Write an SSE event with custom event type and data object.
    /// Format: "event: {type}\ndata: {json}\n\n"
    /// </summary>
    /// <remarks>
    /// Generic version that accepts any object as data.
    /// Use for test execution events and other non-BuilderSseEvent types.
    /// </remarks>
    /// <param name="response">The HTTP response to write to.</param>
    /// <param name="eventType">The event type (e.g., "test_started", "node_complete").</param>
    /// <param name="data">The data object to serialize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task WriteEventAsync(
        HttpResponse response,
        string eventType,
        object data,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);

        // SSE format with event type: "event: {type}\ndata: {json}\n\n"
        var sseData = $"event: {eventType}\ndata: {json}\n\n";

        await response.WriteAsync(sseData, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
