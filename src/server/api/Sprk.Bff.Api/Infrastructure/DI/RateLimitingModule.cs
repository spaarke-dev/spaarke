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

            // 8. AI Batch - Moderate limit for background summarization enqueue
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

            // ProblemDetails JSON response for rate limit rejections
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/problem+json";

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

                await context.HttpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

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
