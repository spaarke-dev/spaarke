using System.Threading.RateLimiting;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for rate limiting policies (ADR-009, ADR-010).
/// Defines per-user/per-IP traffic control policies for Graph, Dataverse, upload, AI, and anonymous access.
/// </summary>
public static class RateLimitingModule
{
    /// <summary>
    /// Adds rate limiting services with 8 named policies and ProblemDetails rejection handler.
    /// </summary>
    public static IServiceCollection AddRateLimitingModule(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // 1. Graph Read Operations - High volume, sliding window
            options.AddPolicy("graph-read", context =>
            {
                var userId = GetUserId(context);
                return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 100,
                    QueueLimit = 10,
                    SegmentsPerWindow = 6
                });
            });

            // 2. Graph Write Operations - Lower volume, token bucket for burst
            options.AddPolicy("graph-write", context =>
            {
                var userId = GetUserId(context);
                return RateLimitPartition.GetTokenBucketLimiter(userId, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 20,
                    TokensPerPeriod = 10,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 5
                });
            });

            // 3. Dataverse Query Operations - Moderate volume, sliding window
            options.AddPolicy("dataverse-query", context =>
            {
                var userId = GetUserId(context);
                return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 50,
                    QueueLimit = 5,
                    SegmentsPerWindow = 4
                });
            });

            // 3b. Metadata Query Operations - Very high volume with L1 cache
            options.AddPolicy("metadata-query", context =>
            {
                var userId = GetUserId(context);
                return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 200,
                    QueueLimit = 10,
                    SegmentsPerWindow = 6
                });
            });

            // 4. Heavy Operations - File uploads, strict concurrency
            options.AddPolicy("upload-heavy", context =>
            {
                var userId = GetUserId(context);
                return RateLimitPartition.GetConcurrencyLimiter(userId, _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = 5,
                    QueueLimit = 10
                });
            });

            // 5. Job Submission - Rate-sensitive, fixed window
            options.AddPolicy("job-submission", context =>
            {
                var userId = GetUserId(context);
                return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 10,
                    QueueLimit = 2
                });
            });

            // 6. Anonymous/Unauthenticated - Very restrictive, fixed window
            options.AddPolicy("anonymous", context =>
            {
                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(ipAddress, _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 10,
                    QueueLimit = 0
                });
            });

            // 7. AI Streaming - Strict limit for costly AI operations
            options.AddPolicy("ai-stream", context =>
            {
                var userId = GetUserId(context);
                return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 10,
                    QueueLimit = 2,
                    SegmentsPerWindow = 6
                });
            });

            // 8. AI Upload - Strict limit for document uploads (ADR-016: 5 uploads/minute/user)
            options.AddPolicy("ai-upload", context =>
            {
                var userId = GetUserId(context);
                return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 5,
                    QueueLimit = 0
                });
            });

            // 9. AI Batch - Moderate limit for background summarization enqueue
            options.AddPolicy("ai-batch", context =>
            {
                var userId = GetUserId(context);
                return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 20,
                    QueueLimit = 5,
                    SegmentsPerWindow = 4
                });
            });

            // 10. AI Persist - SPE persistence operations (ADR-016: 20 req/min/user)
            options.AddPolicy("ai-persist", context =>
            {
                var userId = GetUserId(context);
                return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 20,
                    QueueLimit = 2
                });
            });

            // 11. AI Export - Word/PDF export operations (ADR-016: 10 exports/min/user)
            options.AddPolicy("ai-export", context =>
            {
                var userId = GetUserId(context);
                return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 10,
                    QueueLimit = 2
                });
            });

            // 12. AI Indexing - Playbook embedding indexing (ADR-016: 30 req/min, tenant-scoped)
            options.AddPolicy("ai-indexing", context =>
            {
                var tenantId = context.User?.FindFirst("tid")?.Value
                    ?? context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                    ?? GetUserId(context);
                return RateLimitPartition.GetFixedWindowLimiter(tenantId, _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 30,
                    QueueLimit = 5
                });
            });

            // 13. AI Context - Read-heavy context resolution endpoints (ADR-016: 60 req/min/user)
            options.AddPolicy("ai-context", context =>
            {
                var userId = GetUserId(context);
                return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 60,
                    QueueLimit = 5,
                    SegmentsPerWindow = 6
                });
            });

            // ProblemDetails JSON response for rate limit rejections
            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
                    ? retryAfterValue.TotalSeconds
                    : 60;

                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();

                var problemDetails = new
                {
                    type = "https://tools.ietf.org/html/rfc6585#section-4",
                    title = "Too Many Requests",
                    status = 429,
                    detail = "Rate limit exceeded. Please retry after the specified duration.",
                    instance = context.HttpContext.Request.Path.Value,
                    retryAfter = $"{retryAfter} seconds"
                };

                // ADR-019: Response MUST use Content-Type: application/problem+json.
                // Serialize manually instead of using WriteAsJsonAsync, which would
                // override the content type to application/json.
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/problem+json; charset=utf-8";
                var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(problemDetails);
                await context.HttpContext.Response.Body.WriteAsync(json, cancellationToken);

                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogWarning(
                    "Rate limit exceeded for {Path} by {User} (IP: {IP}). Retry after {RetryAfter}s",
                    context.HttpContext.Request.Path,
                    context.HttpContext.User?.Identity?.Name ?? "anonymous",
                    context.HttpContext.Connection.RemoteIpAddress,
                    retryAfter);
            };
        });

        return services;
    }

    private static string GetUserId(HttpContext context)
    {
        return context.User?.FindFirst("oid")?.Value
               ?? context.User?.FindFirst("sub")?.Value
               ?? context.Connection.RemoteIpAddress?.ToString()
               ?? "unknown";
    }
}
