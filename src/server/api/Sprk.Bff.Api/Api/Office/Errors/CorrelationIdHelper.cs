namespace Sprk.Bff.Api.Api.Office.Errors;

/// <summary>
/// Helper for extracting or generating correlation IDs for request tracing.
/// </summary>
/// <remarks>
/// Correlation IDs flow from client through API to workers, enabling
/// end-to-end tracing for troubleshooting. The priority is:
/// 1. X-Correlation-Id header from client
/// 2. TraceIdentifier from HttpContext
/// 3. Newly generated GUID as fallback
/// </remarks>
public static class CorrelationIdHelper
{
    /// <summary>
    /// Header name for correlation ID.
    /// </summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";

    /// <summary>
    /// Header name for request ID (alternative).
    /// </summary>
    public const string RequestIdHeader = "X-Request-Id";

    /// <summary>
    /// Gets or generates a correlation ID from the HTTP context.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The correlation ID (from header, trace identifier, or newly generated).</returns>
    public static string GetOrGenerate(HttpContext context)
    {
        // 1. Check for explicit correlation ID header
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationId) &&
            !string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId.ToString();
        }

        // 2. Check for request ID header (alternative)
        if (context.Request.Headers.TryGetValue(RequestIdHeader, out var requestId) &&
            !string.IsNullOrWhiteSpace(requestId))
        {
            return requestId.ToString();
        }

        // 3. Use the ASP.NET Core trace identifier (already set for the request)
        if (!string.IsNullOrWhiteSpace(context.TraceIdentifier))
        {
            return context.TraceIdentifier;
        }

        // 4. Generate a new one as fallback
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Gets the correlation ID from context items (set earlier in the pipeline).
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The stored correlation ID, or generates a new one if not found.</returns>
    public static string GetFromContext(HttpContext context)
    {
        if (context.Items.TryGetValue("CorrelationId", out var storedId) &&
            storedId is string id &&
            !string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        // Fallback to extraction/generation
        var correlationId = GetOrGenerate(context);
        context.Items["CorrelationId"] = correlationId;
        return correlationId;
    }

    /// <summary>
    /// Sets the correlation ID in the response headers.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="correlationId">The correlation ID to set.</param>
    public static void SetResponseHeader(HttpContext context, string correlationId)
    {
        if (!context.Response.Headers.ContainsKey(CorrelationIdHeader))
        {
            context.Response.Headers.Append(CorrelationIdHeader, correlationId);
        }
    }

    /// <summary>
    /// Ensures the correlation ID is available in context items and response headers.
    /// Call this early in the request pipeline to establish the correlation ID.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The correlation ID.</returns>
    public static string EnsureCorrelationId(HttpContext context)
    {
        var correlationId = GetFromContext(context);
        SetResponseHeader(context, correlationId);
        return correlationId;
    }
}
