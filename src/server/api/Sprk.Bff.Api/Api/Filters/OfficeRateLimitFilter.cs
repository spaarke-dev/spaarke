using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Defines the category of Office endpoint for rate limiting purposes.
/// </summary>
public enum OfficeRateLimitCategory
{
    /// <summary>POST /office/save - 10 requests/minute/user</summary>
    Save,

    /// <summary>POST /office/quickcreate/* - 5 requests/minute/user</summary>
    QuickCreate,

    /// <summary>GET /office/search/* - 30 requests/minute/user</summary>
    Search,

    /// <summary>GET /office/jobs/* - 60 requests/minute/user</summary>
    Jobs,

    /// <summary>POST /office/share/* - 20 requests/minute/user</summary>
    Share,

    /// <summary>GET /office/recent - 30 requests/minute/user</summary>
    Recent
}

/// <summary>
/// Extension methods for adding OfficeRateLimitFilter to endpoints.
/// </summary>
public static class OfficeRateLimitFilterExtensions
{
    /// <summary>
    /// Adds rate limiting filter with the specified category.
    /// Returns 429 Too Many Requests with OFFICE_015 error code when limit exceeded.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="category">The rate limit category determining the limit.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddOfficeRateLimitFilter<TBuilder>(
        this TBuilder builder,
        OfficeRateLimitCategory category) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var rateLimitService = context.HttpContext.RequestServices
                .GetRequiredService<IOfficeRateLimitService>();
            var logger = context.HttpContext.RequestServices
                .GetService<ILogger<OfficeRateLimitFilter>>();

            var filter = new OfficeRateLimitFilter(rateLimitService, category, logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Endpoint filter that enforces per-user rate limits for Office endpoints.
/// Uses sliding window algorithm with distributed state storage via Redis.
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-008: Use endpoint filters for request validation (including rate limiting).
/// This is applied per-endpoint rather than as global middleware to allow
/// different limits for different endpoint categories.
/// </para>
/// <para>
/// Rate limits per spec.md:
/// - Save: 10 requests/minute
/// - QuickCreate: 5 requests/minute
/// - Search: 30 requests/minute
/// - Jobs: 60 requests/minute
/// - Share: 20 requests/minute
/// </para>
/// </remarks>
public class OfficeRateLimitFilter : IEndpointFilter
{
    private readonly IOfficeRateLimitService _rateLimitService;
    private readonly OfficeRateLimitCategory _category;
    private readonly ILogger<OfficeRateLimitFilter>? _logger;

    public OfficeRateLimitFilter(
        IOfficeRateLimitService rateLimitService,
        OfficeRateLimitCategory category,
        ILogger<OfficeRateLimitFilter>? logger = null)
    {
        _rateLimitService = rateLimitService;
        _category = category;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var traceId = httpContext.TraceIdentifier;

        // Extract user ID for rate limiting partition
        var userId = ExtractUserId(httpContext.User);
        if (string.IsNullOrEmpty(userId))
        {
            // Fall back to IP address for unauthenticated requests (shouldn't happen for Office endpoints)
            userId = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        // Check and increment rate limit
        var result = await _rateLimitService.CheckAndIncrementAsync(userId, _category);

        // Add rate limit headers to response
        httpContext.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
        httpContext.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
        httpContext.Response.Headers["X-RateLimit-Reset"] = result.ResetTimestamp.ToUnixTimeSeconds().ToString();

        if (!result.IsAllowed)
        {
            _logger?.LogWarning(
                "Rate limit exceeded for user {UserId} on {Category} endpoint. " +
                "Limit: {Limit}, Used: {Used}, RetryAfter: {RetryAfter}s. CorrelationId: {CorrelationId}",
                userId,
                _category,
                result.Limit,
                result.Limit - result.Remaining,
                result.RetryAfterSeconds,
                traceId);

            // Add Retry-After header (required per spec)
            httpContext.Response.Headers["Retry-After"] = result.RetryAfterSeconds.ToString();

            return ProblemDetailsHelper.OfficeRateLimitExceeded(
                result.Limit,
                result.RetryAfterSeconds,
                traceId);
        }

        _logger?.LogDebug(
            "Rate limit check passed for user {UserId} on {Category} endpoint. " +
            "Remaining: {Remaining}/{Limit}. CorrelationId: {CorrelationId}",
            userId,
            _category,
            result.Remaining,
            result.Limit,
            traceId);

        return await next(context);
    }

    /// <summary>
    /// Extracts user ID from claims, checking Azure AD claims first.
    /// </summary>
    private static string? ExtractUserId(ClaimsPrincipal user)
    {
        // Try Azure AD object identifier (OID) first
        var oid = user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

        if (!string.IsNullOrWhiteSpace(oid))
        {
            return oid;
        }

        // Fallback to standard NameIdentifier
        var nameId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(nameId))
        {
            return nameId;
        }

        // Fallback to OIDC 'sub' claim
        return user.FindFirst("sub")?.Value;
    }
}

/// <summary>
/// Result of a rate limit check.
/// </summary>
public class RateLimitResult
{
    /// <summary>
    /// Whether the request is allowed (under the limit).
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// Maximum requests allowed per window.
    /// </summary>
    public int Limit { get; init; }

    /// <summary>
    /// Remaining requests allowed in the current window.
    /// </summary>
    public int Remaining { get; init; }

    /// <summary>
    /// Timestamp when the rate limit window resets.
    /// </summary>
    public DateTimeOffset ResetTimestamp { get; init; }

    /// <summary>
    /// Seconds until the client should retry (when rate limited).
    /// </summary>
    public int RetryAfterSeconds { get; init; }
}

/// <summary>
/// Service interface for rate limit operations.
/// </summary>
public interface IOfficeRateLimitService
{
    /// <summary>
    /// Checks the rate limit for a user and category, and increments the counter if allowed.
    /// </summary>
    /// <param name="userId">The user identifier for rate limit partitioning.</param>
    /// <param name="category">The endpoint category for limit determination.</param>
    /// <returns>The rate limit result including whether the request is allowed.</returns>
    Task<RateLimitResult> CheckAndIncrementAsync(string userId, OfficeRateLimitCategory category);
}

/// <summary>
/// Redis-backed rate limit service using sliding window algorithm.
/// Falls back to in-memory when Redis is unavailable.
/// </summary>
public class OfficeRateLimitService : IOfficeRateLimitService
{
    private readonly IDistributedCache _cache;
    private readonly OfficeRateLimitOptions _options;
    private readonly ILogger<OfficeRateLimitService> _logger;

    public OfficeRateLimitService(
        IDistributedCache cache,
        IOptions<OfficeRateLimitOptions> options,
        ILogger<OfficeRateLimitService> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RateLimitResult> CheckAndIncrementAsync(string userId, OfficeRateLimitCategory category)
    {
        if (!_options.Enabled)
        {
            // Rate limiting disabled, allow all requests
            return new RateLimitResult
            {
                IsAllowed = true,
                Limit = int.MaxValue,
                Remaining = int.MaxValue,
                ResetTimestamp = DateTimeOffset.UtcNow.AddMinutes(1),
                RetryAfterSeconds = 0
            };
        }

        var limit = GetLimitForCategory(category);
        var windowSeconds = _options.WindowSizeSeconds;
        var segmentSeconds = windowSeconds / _options.SegmentsPerWindow;
        var now = DateTimeOffset.UtcNow;
        var currentSegment = now.ToUnixTimeSeconds() / segmentSeconds;

        // Build cache key for this user, category, and segment
        var cacheKey = $"{_options.KeyPrefix}{userId}:{category}";

        try
        {
            // Get current window state from cache
            var windowState = await GetWindowStateAsync(cacheKey);

            // Calculate total count across the sliding window
            var windowStartSegment = currentSegment - _options.SegmentsPerWindow + 1;
            var totalCount = 0;

            foreach (var (segment, count) in windowState.SegmentCounts)
            {
                if (segment >= windowStartSegment)
                {
                    totalCount += count;
                }
            }

            // Check if under limit
            var isAllowed = totalCount < limit;
            var remaining = Math.Max(0, limit - totalCount - (isAllowed ? 1 : 0));

            if (isAllowed)
            {
                // Increment the current segment counter
                if (windowState.SegmentCounts.ContainsKey(currentSegment))
                {
                    windowState.SegmentCounts[currentSegment]++;
                }
                else
                {
                    windowState.SegmentCounts[currentSegment] = 1;
                }

                // Prune old segments
                var segmentsToRemove = windowState.SegmentCounts
                    .Where(kvp => kvp.Key < windowStartSegment)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var segment in segmentsToRemove)
                {
                    windowState.SegmentCounts.Remove(segment);
                }

                // Save updated state
                await SetWindowStateAsync(cacheKey, windowState, TimeSpan.FromSeconds(windowSeconds * 2));
            }

            // Calculate reset timestamp (end of current window)
            var windowEndSegment = currentSegment + _options.SegmentsPerWindow;
            var resetTimestamp = DateTimeOffset.FromUnixTimeSeconds(windowEndSegment * segmentSeconds);

            // Calculate retry-after (time until oldest segment expires)
            var oldestSegment = windowState.SegmentCounts.Keys.DefaultIfEmpty(currentSegment).Min();
            var oldestSegmentExpiry = DateTimeOffset.FromUnixTimeSeconds((oldestSegment + _options.SegmentsPerWindow) * segmentSeconds);
            var retryAfterSeconds = Math.Max(1, (int)(oldestSegmentExpiry - now).TotalSeconds);

            return new RateLimitResult
            {
                IsAllowed = isAllowed,
                Limit = limit,
                Remaining = remaining,
                ResetTimestamp = resetTimestamp,
                RetryAfterSeconds = retryAfterSeconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Error checking rate limit for user {UserId} category {Category}. Allowing request (fail-open).",
                userId,
                category);

            // Fail-open: allow request if rate limit check fails
            return new RateLimitResult
            {
                IsAllowed = true,
                Limit = limit,
                Remaining = limit - 1,
                ResetTimestamp = now.AddSeconds(windowSeconds),
                RetryAfterSeconds = 0
            };
        }
    }

    private int GetLimitForCategory(OfficeRateLimitCategory category)
    {
        return category switch
        {
            OfficeRateLimitCategory.Save => _options.Limits.SaveRequestsPerMinute,
            OfficeRateLimitCategory.QuickCreate => _options.Limits.QuickCreateRequestsPerMinute,
            OfficeRateLimitCategory.Search => _options.Limits.SearchRequestsPerMinute,
            OfficeRateLimitCategory.Jobs => _options.Limits.JobsRequestsPerMinute,
            OfficeRateLimitCategory.Share => _options.Limits.ShareRequestsPerMinute,
            OfficeRateLimitCategory.Recent => _options.Limits.RecentRequestsPerMinute,
            _ => 30 // Default fallback
        };
    }

    private async Task<SlidingWindowState> GetWindowStateAsync(string cacheKey)
    {
        var cached = await _cache.GetStringAsync(cacheKey);
        if (string.IsNullOrEmpty(cached))
        {
            return new SlidingWindowState();
        }

        try
        {
            return JsonSerializer.Deserialize<SlidingWindowState>(cached) ?? new SlidingWindowState();
        }
        catch
        {
            return new SlidingWindowState();
        }
    }

    private async Task SetWindowStateAsync(string cacheKey, SlidingWindowState state, TimeSpan expiry)
    {
        var json = JsonSerializer.Serialize(state);
        await _cache.SetStringAsync(
            cacheKey,
            json,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry
            });
    }

    /// <summary>
    /// Internal state for sliding window rate limiting.
    /// Stores request counts per time segment.
    /// </summary>
    private class SlidingWindowState
    {
        /// <summary>
        /// Map of segment timestamp (Unix seconds / segment size) to request count.
        /// </summary>
        public Dictionary<long, int> SegmentCounts { get; set; } = new();
    }
}
