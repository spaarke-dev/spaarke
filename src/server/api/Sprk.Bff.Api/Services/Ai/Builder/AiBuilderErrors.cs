using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Sprk.Bff.Api.Services.Ai.Builder;

/// <summary>
/// Provides standardized error handling for AI Builder operations.
/// Uses ProblemDetails format for API responses per ADR-001.
/// </summary>
/// <remarks>
/// <para>
/// Error Categories:
/// - Validation errors (4xx): Invalid node configuration, missing scopes
/// - Execution errors (5xx): AI service failures, timeout
/// - Streaming errors: SSE-specific error events
/// </para>
/// <para>
/// Task 050: Comprehensive error handling with ProblemDetails
/// </para>
/// </remarks>
public static class AiBuilderErrors
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Error Codes
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Error code for validation failures.</summary>
    public const string ValidationError = "AI_BUILDER_VALIDATION";

    /// <summary>Error code for node configuration issues.</summary>
    public const string NodeConfigurationError = "AI_BUILDER_NODE_CONFIG";

    /// <summary>Error code for scope resolution failures.</summary>
    public const string ScopeResolutionError = "AI_BUILDER_SCOPE_RESOLUTION";

    /// <summary>Error code for execution timeouts.</summary>
    public const string ExecutionTimeoutError = "AI_BUILDER_TIMEOUT";

    /// <summary>Error code for AI service failures.</summary>
    public const string AiServiceError = "AI_BUILDER_SERVICE_FAILURE";

    /// <summary>Error code for SSE streaming failures.</summary>
    public const string StreamingError = "AI_BUILDER_STREAMING";

    /// <summary>Error code for playbook not found.</summary>
    public const string PlaybookNotFoundError = "AI_BUILDER_PLAYBOOK_NOT_FOUND";

    /// <summary>Error code for ownership validation failures.</summary>
    public const string OwnershipError = "AI_BUILDER_OWNERSHIP";

    // ─────────────────────────────────────────────────────────────────────────────
    // ProblemDetails Factory Methods
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a validation error ProblemDetails response.
    /// </summary>
    public static ProblemDetails ValidationFailed(
        string detail,
        string? correlationId = null,
        IDictionary<string, object?>? extensions = null)
    {
        return CreateProblemDetails(
            StatusCodes.Status400BadRequest,
            "Validation Failed",
            ValidationError,
            detail,
            correlationId,
            extensions);
    }

    /// <summary>
    /// Create a node configuration error ProblemDetails response.
    /// </summary>
    public static ProblemDetails NodeConfigurationInvalid(
        Guid nodeId,
        string issue,
        string? correlationId = null)
    {
        return CreateProblemDetails(
            StatusCodes.Status400BadRequest,
            "Node Configuration Invalid",
            NodeConfigurationError,
            $"Node {nodeId} has invalid configuration: {issue}",
            correlationId,
            new Dictionary<string, object?> { ["nodeId"] = nodeId });
    }

    /// <summary>
    /// Create a scope resolution error ProblemDetails response.
    /// </summary>
    public static ProblemDetails ScopeResolutionFailed(
        Guid[] scopeIds,
        string reason,
        string? correlationId = null)
    {
        return CreateProblemDetails(
            StatusCodes.Status400BadRequest,
            "Scope Resolution Failed",
            ScopeResolutionError,
            $"Failed to resolve scopes: {reason}",
            correlationId,
            new Dictionary<string, object?> { ["scopeIds"] = scopeIds });
    }

    /// <summary>
    /// Create a playbook not found error ProblemDetails response.
    /// </summary>
    public static ProblemDetails PlaybookNotFound(
        Guid playbookId,
        string? correlationId = null)
    {
        return CreateProblemDetails(
            StatusCodes.Status404NotFound,
            "Playbook Not Found",
            PlaybookNotFoundError,
            $"Playbook {playbookId} was not found or access is denied.",
            correlationId,
            new Dictionary<string, object?> { ["playbookId"] = playbookId });
    }

    /// <summary>
    /// Create an ownership error ProblemDetails response.
    /// </summary>
    public static ProblemDetails OwnershipViolation(
        string scopeName,
        string operation,
        string? correlationId = null)
    {
        return CreateProblemDetails(
            StatusCodes.Status403Forbidden,
            "Ownership Violation",
            OwnershipError,
            $"Cannot {operation} system scope '{scopeName}'. System scopes (SYS-) are immutable.",
            correlationId,
            new Dictionary<string, object?> { ["scopeName"] = scopeName, ["operation"] = operation });
    }

    /// <summary>
    /// Create an execution timeout error ProblemDetails response.
    /// </summary>
    public static ProblemDetails ExecutionTimeout(
        TimeSpan timeout,
        string operation,
        string? correlationId = null)
    {
        return CreateProblemDetails(
            StatusCodes.Status504GatewayTimeout,
            "Execution Timeout",
            ExecutionTimeoutError,
            $"Operation '{operation}' exceeded the timeout of {timeout.TotalSeconds} seconds.",
            correlationId,
            new Dictionary<string, object?>
            {
                ["timeoutSeconds"] = timeout.TotalSeconds,
                ["operation"] = operation
            });
    }

    /// <summary>
    /// Create an AI service error ProblemDetails response.
    /// </summary>
    public static ProblemDetails AiServiceFailed(
        string serviceName,
        string reason,
        string? correlationId = null)
    {
        return CreateProblemDetails(
            StatusCodes.Status502BadGateway,
            "AI Service Failure",
            AiServiceError,
            $"AI service '{serviceName}' failed: {reason}",
            correlationId,
            new Dictionary<string, object?> { ["serviceName"] = serviceName });
    }

    /// <summary>
    /// Create an internal server error ProblemDetails response.
    /// </summary>
    public static ProblemDetails InternalError(
        string detail,
        string? correlationId = null)
    {
        return CreateProblemDetails(
            StatusCodes.Status500InternalServerError,
            "Internal Server Error",
            "AI_BUILDER_INTERNAL",
            detail,
            correlationId);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // SSE Error Events
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Create an SSE error event for streaming responses.
    /// </summary>
    public static SseErrorEvent CreateSseError(
        string errorCode,
        string message,
        bool isRetryable = false,
        string? correlationId = null)
    {
        return new SseErrorEvent
        {
            EventType = "error",
            ErrorCode = errorCode,
            Message = message,
            IsRetryable = isRetryable,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Serialize an SSE error event to JSON for streaming.
    /// </summary>
    public static string SerializeSseError(SseErrorEvent error)
    {
        return JsonSerializer.Serialize(error, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helper Methods
    // ─────────────────────────────────────────────────────────────────────────────

    private static ProblemDetails CreateProblemDetails(
        int status,
        string title,
        string errorCode,
        string detail,
        string? correlationId,
        IDictionary<string, object?>? extensions = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://spaarke.dev/errors/{errorCode.ToLowerInvariant().Replace('_', '-')}"
        };

        // Add correlation ID
        problemDetails.Extensions["correlationId"] = correlationId ?? Guid.NewGuid().ToString();
        problemDetails.Extensions["errorCode"] = errorCode;
        problemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;

        // Add any additional extensions
        if (extensions != null)
        {
            foreach (var (key, value) in extensions)
            {
                problemDetails.Extensions[key] = value;
            }
        }

        return problemDetails;
    }
}

/// <summary>
/// SSE error event structure for streaming error responses.
/// </summary>
public record SseErrorEvent
{
    /// <summary>Event type (always "error" for error events).</summary>
    public string EventType { get; init; } = "error";

    /// <summary>Error code for client handling.</summary>
    public string ErrorCode { get; init; } = string.Empty;

    /// <summary>Human-readable error message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Whether the client should retry the operation.</summary>
    public bool IsRetryable { get; init; }

    /// <summary>Correlation ID for support/debugging.</summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>When the error occurred.</summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Result type for operations that may fail with detailed errors.
/// </summary>
/// <typeparam name="T">The success value type.</typeparam>
public record AiBuilderResult<T>
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>The result value (if successful).</summary>
    public T? Value { get; init; }

    /// <summary>The error details (if failed).</summary>
    public ProblemDetails? Error { get; init; }

    /// <summary>Create a successful result.</summary>
    public static AiBuilderResult<T> Ok(T value) => new() { Success = true, Value = value };

    /// <summary>Create a failed result with ProblemDetails.</summary>
    public static AiBuilderResult<T> Fail(ProblemDetails error) => new() { Success = false, Error = error };
}
