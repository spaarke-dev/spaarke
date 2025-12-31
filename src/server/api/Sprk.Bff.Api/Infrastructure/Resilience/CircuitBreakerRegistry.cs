using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Polly.CircuitBreaker;

namespace Sprk.Bff.Api.Infrastructure.Resilience;

/// <summary>
/// Circuit breaker state for monitoring purposes.
/// </summary>
public enum CircuitState
{
    /// <summary>Circuit is closed - requests flow normally.</summary>
    Closed,
    /// <summary>Circuit is open - requests are rejected.</summary>
    Open,
    /// <summary>Circuit is half-open - testing if service recovered.</summary>
    HalfOpen,
    /// <summary>State is unknown or circuit not registered.</summary>
    Unknown
}

/// <summary>
/// Information about a registered circuit breaker.
/// </summary>
public record CircuitBreakerInfo
{
    /// <summary>Unique identifier for the circuit breaker.</summary>
    public required string ServiceName { get; init; }

    /// <summary>Current state of the circuit.</summary>
    public CircuitState State { get; init; }

    /// <summary>When the circuit last changed state.</summary>
    public DateTimeOffset? LastStateChange { get; init; }

    /// <summary>Number of consecutive failures (if tracking).</summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>When the circuit will transition from Open to Half-Open.</summary>
    public DateTimeOffset? OpenUntil { get; init; }

    /// <summary>Whether the service is currently available.</summary>
    public bool IsAvailable => State != CircuitState.Open;
}

/// <summary>
/// Centralized registry for tracking circuit breaker states across all services.
/// Provides monitoring endpoint data and telemetry integration.
/// </summary>
/// <remarks>
/// Services registered:
/// - AzureOpenAI: Chat completions, embeddings
/// - AzureAISearch: RAG queries, indexing
/// - MicrosoftGraph: SPE operations
/// </remarks>
public class CircuitBreakerRegistry : ICircuitBreakerRegistry
{
    private readonly ConcurrentDictionary<string, CircuitBreakerState> _circuits = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<CircuitBreakerRegistry> _logger;
    private readonly Meter _meter;

    // Metrics
    private readonly Counter<long> _stateTransitions;
    private readonly ObservableGauge<int> _openCircuits;

    // Well-known service names
    public const string AzureOpenAI = "AzureOpenAI";
    public const string AzureAISearch = "AzureAISearch";
    public const string MicrosoftGraph = "MicrosoftGraph";

    public CircuitBreakerRegistry(ILogger<CircuitBreakerRegistry> logger)
    {
        _logger = logger;

        // Create meter for circuit breaker metrics
        _meter = new Meter("Sprk.Bff.Api.CircuitBreaker", "1.0.0");

        _stateTransitions = _meter.CreateCounter<long>(
            name: "circuit_breaker.state_transitions",
            unit: "{transition}",
            description: "Number of circuit breaker state transitions");

        _openCircuits = _meter.CreateObservableGauge(
            name: "circuit_breaker.open_count",
            observeValue: () => _circuits.Values.Count(c => c.State == CircuitState.Open),
            unit: "{circuit}",
            description: "Number of currently open circuit breakers");
    }

    /// <inheritdoc />
    public void RegisterCircuit(string serviceName)
    {
        _circuits.TryAdd(serviceName, new CircuitBreakerState
        {
            ServiceName = serviceName,
            State = CircuitState.Closed,
            LastStateChange = DateTimeOffset.UtcNow
        });

        _logger.LogInformation("Registered circuit breaker for {ServiceName}", serviceName);
    }

    /// <inheritdoc />
    public void RecordStateChange(string serviceName, CircuitState newState, TimeSpan? breakDuration = null)
    {
        var now = DateTimeOffset.UtcNow;
        var openUntil = newState == CircuitState.Open && breakDuration.HasValue
            ? now.Add(breakDuration.Value)
            : (DateTimeOffset?)null;

        _circuits.AddOrUpdate(
            serviceName,
            _ => new CircuitBreakerState
            {
                ServiceName = serviceName,
                State = newState,
                LastStateChange = now,
                OpenUntil = openUntil
            },
            (_, existing) =>
            {
                var oldState = existing.State;
                existing.State = newState;
                existing.LastStateChange = now;
                existing.OpenUntil = openUntil;

                // Reset consecutive failures on close
                if (newState == CircuitState.Closed)
                {
                    existing.ConsecutiveFailures = 0;
                }

                return existing;
            });

        // Record metric
        _stateTransitions.Add(1,
            new KeyValuePair<string, object?>("service", serviceName),
            new KeyValuePair<string, object?>("state", newState.ToString().ToLowerInvariant()));

        // Log state change
        LogStateChange(serviceName, newState, breakDuration);
    }

    /// <inheritdoc />
    public void RecordFailure(string serviceName)
    {
        _circuits.AddOrUpdate(
            serviceName,
            _ => new CircuitBreakerState
            {
                ServiceName = serviceName,
                State = CircuitState.Closed,
                LastStateChange = DateTimeOffset.UtcNow,
                ConsecutiveFailures = 1
            },
            (_, existing) =>
            {
                existing.ConsecutiveFailures++;
                return existing;
            });
    }

    /// <inheritdoc />
    public void RecordSuccess(string serviceName)
    {
        if (_circuits.TryGetValue(serviceName, out var state))
        {
            state.ConsecutiveFailures = 0;
        }
    }

    /// <inheritdoc />
    public CircuitBreakerInfo GetCircuitInfo(string serviceName)
    {
        if (_circuits.TryGetValue(serviceName, out var state))
        {
            return new CircuitBreakerInfo
            {
                ServiceName = state.ServiceName,
                State = state.State,
                LastStateChange = state.LastStateChange,
                ConsecutiveFailures = state.ConsecutiveFailures,
                OpenUntil = state.OpenUntil
            };
        }

        return new CircuitBreakerInfo
        {
            ServiceName = serviceName,
            State = CircuitState.Unknown
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<CircuitBreakerInfo> GetAllCircuits()
    {
        return _circuits.Values
            .Select(s => new CircuitBreakerInfo
            {
                ServiceName = s.ServiceName,
                State = s.State,
                LastStateChange = s.LastStateChange,
                ConsecutiveFailures = s.ConsecutiveFailures,
                OpenUntil = s.OpenUntil
            })
            .OrderBy(c => c.ServiceName)
            .ToList();
    }

    /// <inheritdoc />
    public bool IsServiceAvailable(string serviceName)
    {
        if (_circuits.TryGetValue(serviceName, out var state))
        {
            // Check if open circuit has expired
            if (state.State == CircuitState.Open && state.OpenUntil.HasValue)
            {
                if (DateTimeOffset.UtcNow >= state.OpenUntil.Value)
                {
                    // Time to transition to half-open
                    RecordStateChange(serviceName, CircuitState.HalfOpen);
                    return true; // Allow test request
                }
            }

            return state.State != CircuitState.Open;
        }

        return true; // Unknown circuits are assumed available
    }

    private void LogStateChange(string serviceName, CircuitState newState, TimeSpan? breakDuration)
    {
        switch (newState)
        {
            case CircuitState.Open:
                _logger.LogWarning(
                    "Circuit breaker OPENED for {ServiceName}. Will retry after {BreakDuration}s",
                    serviceName, breakDuration?.TotalSeconds ?? 30);
                break;

            case CircuitState.HalfOpen:
                _logger.LogInformation(
                    "Circuit breaker HALF-OPEN for {ServiceName}. Testing service availability",
                    serviceName);
                break;

            case CircuitState.Closed:
                _logger.LogInformation(
                    "Circuit breaker CLOSED for {ServiceName}. Service recovered",
                    serviceName);
                break;
        }
    }

    private class CircuitBreakerState
    {
        public required string ServiceName { get; init; }
        public CircuitState State { get; set; }
        public DateTimeOffset? LastStateChange { get; set; }
        public int ConsecutiveFailures { get; set; }
        public DateTimeOffset? OpenUntil { get; set; }
    }
}

/// <summary>
/// Interface for circuit breaker registry operations.
/// </summary>
public interface ICircuitBreakerRegistry
{
    /// <summary>Register a new circuit breaker for monitoring.</summary>
    void RegisterCircuit(string serviceName);

    /// <summary>Record a state change for a circuit breaker.</summary>
    void RecordStateChange(string serviceName, CircuitState newState, TimeSpan? breakDuration = null);

    /// <summary>Record a failure for a circuit breaker.</summary>
    void RecordFailure(string serviceName);

    /// <summary>Record a success for a circuit breaker.</summary>
    void RecordSuccess(string serviceName);

    /// <summary>Get information about a specific circuit breaker.</summary>
    CircuitBreakerInfo GetCircuitInfo(string serviceName);

    /// <summary>Get information about all registered circuit breakers.</summary>
    IReadOnlyList<CircuitBreakerInfo> GetAllCircuits();

    /// <summary>Check if a service is currently available (circuit not open).</summary>
    bool IsServiceAvailable(string serviceName);
}
