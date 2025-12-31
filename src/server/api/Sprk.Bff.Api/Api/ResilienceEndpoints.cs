using Sprk.Bff.Api.Infrastructure.Resilience;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// API endpoints for monitoring resilience patterns (circuit breakers, etc.).
/// These endpoints are useful for operations dashboards and health checks.
/// </summary>
public static class ResilienceEndpoints
{
    /// <summary>
    /// Map resilience monitoring endpoints.
    /// Note: These endpoints require authentication to prevent information disclosure.
    /// Consider restricting to admin roles in production if needed.
    /// </summary>
    public static IEndpointRouteBuilder MapResilienceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/resilience")
            .RequireAuthorization()
            .WithTags("Resilience");

        // GET /api/resilience/circuits - Get all circuit breaker states
        group.MapGet("/circuits", GetAllCircuitBreakers)
            .WithName("GetAllCircuitBreakers")
            .WithSummary("Get status of all circuit breakers")
            .WithDescription("Returns the current state of all registered circuit breakers for monitoring purposes.")
            .Produces<CircuitBreakerStatusResponse>(StatusCodes.Status200OK);

        // GET /api/resilience/circuits/{serviceName} - Get specific circuit breaker state
        group.MapGet("/circuits/{serviceName}", GetCircuitBreaker)
            .WithName("GetCircuitBreaker")
            .WithSummary("Get status of a specific circuit breaker")
            .WithDescription("Returns the current state of a specific circuit breaker by service name.")
            .Produces<CircuitBreakerInfo>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        // GET /api/resilience/health - Health check for external services
        group.MapGet("/health", GetResilienceHealth)
            .WithName("GetResilienceHealth")
            .WithSummary("Check health of external service connections")
            .WithDescription("Returns overall health status based on circuit breaker states. Returns 503 if any circuit is open.")
            .Produces<ResilienceHealthResponse>(StatusCodes.Status200OK)
            .Produces<ResilienceHealthResponse>(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static IResult GetAllCircuitBreakers(ICircuitBreakerRegistry registry)
    {
        var circuits = registry.GetAllCircuits();
        var response = new CircuitBreakerStatusResponse
        {
            Circuits = circuits,
            Timestamp = DateTimeOffset.UtcNow,
            OpenCount = circuits.Count(c => c.State == CircuitState.Open),
            HalfOpenCount = circuits.Count(c => c.State == CircuitState.HalfOpen),
            ClosedCount = circuits.Count(c => c.State == CircuitState.Closed)
        };
        return Results.Ok(response);
    }

    private static IResult GetCircuitBreaker(string serviceName, ICircuitBreakerRegistry registry)
    {
        var info = registry.GetCircuitInfo(serviceName);
        if (info.State == CircuitState.Unknown)
        {
            return Results.NotFound(new { message = $"Circuit breaker '{serviceName}' not found" });
        }
        return Results.Ok(info);
    }

    private static IResult GetResilienceHealth(ICircuitBreakerRegistry registry, ILogger<ICircuitBreakerRegistry> logger)
    {
        var circuits = registry.GetAllCircuits();
        var openCircuits = circuits.Where(c => c.State == CircuitState.Open).ToList();
        var isHealthy = openCircuits.Count == 0;

        var response = new ResilienceHealthResponse
        {
            Status = isHealthy ? "Healthy" : "Degraded",
            Timestamp = DateTimeOffset.UtcNow,
            Services = circuits.ToDictionary(
                c => c.ServiceName,
                c => new ServiceHealthInfo
                {
                    State = c.State.ToString(),
                    IsAvailable = c.IsAvailable,
                    LastStateChange = c.LastStateChange,
                    RetryAfter = c.OpenUntil.HasValue
                        ? (int?)Math.Max(0, (c.OpenUntil.Value - DateTimeOffset.UtcNow).TotalSeconds)
                        : null
                })
        };

        if (!isHealthy)
        {
            logger.LogWarning(
                "Resilience health check degraded. Open circuits: {OpenCircuits}",
                string.Join(", ", openCircuits.Select(c => c.ServiceName)));
            return Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(response);
    }
}

/// <summary>
/// Response containing all circuit breaker states.
/// </summary>
public record CircuitBreakerStatusResponse
{
    public required IReadOnlyList<CircuitBreakerInfo> Circuits { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public int OpenCount { get; init; }
    public int HalfOpenCount { get; init; }
    public int ClosedCount { get; init; }
}

/// <summary>
/// Response for resilience health check.
/// </summary>
public record ResilienceHealthResponse
{
    public required string Status { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public required Dictionary<string, ServiceHealthInfo> Services { get; init; }
}

/// <summary>
/// Health information for a specific service.
/// </summary>
public record ServiceHealthInfo
{
    public required string State { get; init; }
    public bool IsAvailable { get; init; }
    public DateTimeOffset? LastStateChange { get; init; }
    public int? RetryAfter { get; init; }
}
