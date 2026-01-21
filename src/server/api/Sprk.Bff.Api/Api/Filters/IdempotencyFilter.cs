using System.Buffers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding IdempotencyFilter to endpoints.
/// Follows ADR-008 pattern of using endpoint filters for cross-cutting concerns.
/// </summary>
public static class IdempotencyFilterExtensions
{
    /// <summary>
    /// Adds idempotency support to an endpoint.
    /// Uses SHA256 hash of canonical request payload + user ID as idempotency key.
    /// Caches successful responses in Redis for 24 hours.
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static RouteHandlerBuilder AddIdempotencyFilter(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var cache = context.HttpContext.RequestServices.GetRequiredService<IDistributedCache>();
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<IdempotencyFilter>>();
            var filter = new IdempotencyFilter(cache, logger);
            return await filter.InvokeAsync(context, next);
        });
    }

    /// <summary>
    /// Adds idempotency support with custom TTL.
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="ttl">Custom time-to-live for cached responses.</param>
    /// <returns>The builder for chaining.</returns>
    public static RouteHandlerBuilder AddIdempotencyFilter(this RouteHandlerBuilder builder, TimeSpan ttl)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var cache = context.HttpContext.RequestServices.GetRequiredService<IDistributedCache>();
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<IdempotencyFilter>>();
            var filter = new IdempotencyFilter(cache, logger, ttl);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Endpoint filter that provides idempotency for POST endpoints.
/// Implements the idempotency specification from spec.md:
/// - SHA256 hash of canonical payload + user ID
/// - Redis storage with 24-hour TTL
/// - Returns cached response for duplicate requests
/// </summary>
/// <remarks>
/// Per ADR-008, this is implemented as an endpoint filter rather than global middleware.
/// Per ADR-009, uses IDistributedCache (Redis) for cross-request caching.
/// </remarks>
public class IdempotencyFilter : IEndpointFilter
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<IdempotencyFilter> _logger;
    private readonly TimeSpan _ttl;

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);
    private const string CacheKeyPrefix = "idempotency:request:";
    private const string LockKeyPrefix = "idempotency:lock:";
    private const string IdempotencyKeyHeader = "X-Idempotency-Key";
    private const string IdempotencyStatusHeader = "X-Idempotency-Status";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public IdempotencyFilter(IDistributedCache cache, ILogger<IdempotencyFilter> logger)
        : this(cache, logger, DefaultTtl)
    {
    }

    public IdempotencyFilter(IDistributedCache cache, ILogger<IdempotencyFilter> logger, TimeSpan ttl)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ttl = ttl;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Only apply idempotency to POST requests
        if (!HttpMethods.IsPost(httpContext.Request.Method))
        {
            return await next(context);
        }

        // Extract user ID for scoping
        var userId = GetUserId(httpContext);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Idempotency check skipped: no user ID available");
            return await next(context);
        }

        // Check for client-provided idempotency key in header
        var clientProvidedKey = httpContext.Request.Headers[IdempotencyKeyHeader].FirstOrDefault();

        // Generate or use provided idempotency key
        string idempotencyKey;
        if (!string.IsNullOrEmpty(clientProvidedKey))
        {
            // Use client-provided key scoped by user ID
            idempotencyKey = $"{userId}:{clientProvidedKey}";
            _logger.LogDebug("Using client-provided idempotency key: {IdempotencyKey}", clientProvidedKey);
        }
        else
        {
            // Generate key from request body hash + user ID
            var generatedKey = await GenerateIdempotencyKeyAsync(httpContext, userId);
            if (generatedKey == null)
            {
                // Could not generate key (e.g., empty body), proceed without idempotency
                _logger.LogDebug("Idempotency check skipped: could not generate key from request body");
                return await next(context);
            }
            idempotencyKey = generatedKey;
        }

        var cacheKey = $"{CacheKeyPrefix}{idempotencyKey}";
        var lockKey = $"{LockKeyPrefix}{idempotencyKey}";
        var correlationId = httpContext.TraceIdentifier;

        try
        {
            // Check for cached response
            var cachedResponse = await _cache.GetStringAsync(cacheKey, httpContext.RequestAborted);
            if (cachedResponse != null)
            {
                _logger.LogInformation(
                    "Idempotent request detected: returning cached response for key {IdempotencyKey}, CorrelationId={CorrelationId}",
                    idempotencyKey,
                    correlationId);

                // Add header to indicate this is a cached response
                httpContext.Response.Headers[IdempotencyStatusHeader] = "cached";

                return DeserializeCachedResponse(cachedResponse);
            }

            // Try to acquire lock to prevent race conditions
            if (!await TryAcquireLockAsync(lockKey, httpContext.RequestAborted))
            {
                _logger.LogWarning(
                    "Request already being processed: idempotency key {IdempotencyKey} is locked, CorrelationId={CorrelationId}",
                    idempotencyKey,
                    correlationId);

                // Another request is processing with the same key
                // Return 409 Conflict to indicate concurrent processing
                return Results.Problem(
                    title: "Request In Progress",
                    detail: "A request with the same idempotency key is currently being processed. Please retry shortly.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "OFFICE_IDEMPOTENCY_CONFLICT",
                        ["correlationId"] = correlationId
                    });
            }

            try
            {
                // Execute the endpoint
                var result = await next(context);

                // Cache successful responses only
                if (IsSuccessResponse(result))
                {
                    await CacheResponseAsync(cacheKey, result, httpContext.RequestAborted);
                    _logger.LogDebug(
                        "Cached response for idempotency key {IdempotencyKey}, TTL={TtlHours}h",
                        idempotencyKey,
                        _ttl.TotalHours);
                }

                // Add header to indicate this is a new response
                httpContext.Response.Headers[IdempotencyStatusHeader] = "new";

                return result;
            }
            finally
            {
                // Release lock
                await ReleaseLockAsync(lockKey, httpContext.RequestAborted);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Error during idempotency check for key {IdempotencyKey}, CorrelationId={CorrelationId}",
                idempotencyKey,
                correlationId);

            // On cache failure, proceed without idempotency (fail open)
            return await next(context);
        }
    }

    /// <summary>
    /// Generates an idempotency key from the request body and user ID.
    /// Uses SHA256 hash of canonical JSON payload.
    /// </summary>
    private async Task<string?> GenerateIdempotencyKeyAsync(HttpContext httpContext, string userId)
    {
        // Enable buffering so we can read the body multiple times
        httpContext.Request.EnableBuffering();

        try
        {
            // Read request body
            httpContext.Request.Body.Position = 0;
            using var reader = new StreamReader(
                httpContext.Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

            var body = await reader.ReadToEndAsync();
            httpContext.Request.Body.Position = 0; // Reset for downstream processing

            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            // Parse and re-serialize for canonical form (sorted keys, consistent formatting)
            var canonicalBody = GetCanonicalJson(body);
            if (canonicalBody == null)
            {
                return null;
            }

            // Create hash input: userId + canonical body + endpoint path
            var hashInput = $"{userId}:{httpContext.Request.Path}:{canonicalBody}";

            // Compute SHA256 hash
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
            var hashString = Convert.ToHexString(hashBytes).ToLowerInvariant();

            return hashString;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse request body for idempotency key generation");
            return null;
        }
    }

    /// <summary>
    /// Converts JSON to canonical form (sorted keys, no whitespace).
    /// </summary>
    private static string? GetCanonicalJson(string json)
    {
        try
        {
            // Parse and re-serialize to get consistent formatting
            using var doc = JsonDocument.Parse(json);
            return SerializeCanonical(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Recursively serializes a JSON element with sorted object keys.
    /// </summary>
    private static string SerializeCanonical(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => SerializeCanonicalObject(element),
            JsonValueKind.Array => SerializeCanonicalArray(element),
            _ => element.GetRawText()
        };
    }

    private static string SerializeCanonicalObject(JsonElement element)
    {
        var properties = element.EnumerateObject()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"\"{p.Name}\":{SerializeCanonical(p.Value)}");

        return "{" + string.Join(",", properties) + "}";
    }

    private static string SerializeCanonicalArray(JsonElement element)
    {
        var items = element.EnumerateArray()
            .Select(SerializeCanonical);

        return "[" + string.Join(",", items) + "]";
    }

    private static string? GetUserId(HttpContext httpContext)
    {
        return httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("oid")?.Value;
    }

    private async Task<bool> TryAcquireLockAsync(string lockKey, CancellationToken cancellationToken)
    {
        try
        {
            var existingLock = await _cache.GetAsync(lockKey, cancellationToken);
            if (existingLock != null)
            {
                return false;
            }

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = LockDuration
            };

            await _cache.SetAsync(lockKey, Encoding.UTF8.GetBytes("locked"), options, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acquire idempotency lock for key {LockKey}", lockKey);
            // Fail open - allow request to proceed
            return true;
        }
    }

    private async Task ReleaseLockAsync(string lockKey, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.RemoveAsync(lockKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release idempotency lock for key {LockKey}", lockKey);
            // Lock will expire automatically
        }
    }

    private static bool IsSuccessResponse(object? result)
    {
        return result switch
        {
            IStatusCodeHttpResult statusCodeResult =>
                statusCodeResult.StatusCode is >= 200 and < 300,
            IResult => true, // Assume success for other IResult types
            _ => result != null
        };
    }

    private async Task CacheResponseAsync(string cacheKey, object? result, CancellationToken cancellationToken)
    {
        try
        {
            var response = SerializeResponse(result);
            if (response == null)
            {
                return;
            }

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _ttl
            };

            await _cache.SetStringAsync(cacheKey, response, options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache response for idempotency key {CacheKey}", cacheKey);
        }
    }

    private static string? SerializeResponse(object? result)
    {
        if (result == null)
        {
            return null;
        }

        var response = new CachedIdempotencyResponse
        {
            ResultType = result.GetType().FullName ?? result.GetType().Name
        };

        // Extract the value and status code from different result types
        switch (result)
        {
            case IValueHttpResult valueResult:
                response.Value = valueResult.Value;
                if (result is IStatusCodeHttpResult statusResult)
                {
                    response.StatusCode = statusResult.StatusCode ?? 200;
                }
                break;

            case IStatusCodeHttpResult statusCodeResult:
                response.StatusCode = statusCodeResult.StatusCode ?? 200;
                break;

            default:
                response.Value = result;
                break;
        }

        return JsonSerializer.Serialize(response, CanonicalJsonOptions);
    }

    private static IResult DeserializeCachedResponse(string cachedJson)
    {
        var cached = JsonSerializer.Deserialize<CachedIdempotencyResponse>(cachedJson, CanonicalJsonOptions);
        if (cached == null)
        {
            return Results.StatusCode(500);
        }

        // Determine the appropriate result type based on status code
        return cached.StatusCode switch
        {
            200 => cached.Value != null ? Results.Ok(cached.Value) : Results.Ok(),
            201 => Results.Created(string.Empty, cached.Value),
            202 => Results.Accepted(null, cached.Value),
            204 => Results.NoContent(),
            _ => cached.Value != null
                ? Results.Json(cached.Value, statusCode: cached.StatusCode)
                : Results.StatusCode(cached.StatusCode)
        };
    }

    /// <summary>
    /// Internal class for caching response data.
    /// </summary>
    private sealed class CachedIdempotencyResponse
    {
        public int StatusCode { get; set; } = 200;
        public object? Value { get; set; }
        public string? ResultType { get; set; }
    }
}
