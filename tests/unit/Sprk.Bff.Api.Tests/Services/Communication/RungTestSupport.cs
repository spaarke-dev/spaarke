using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Shared test doubles for the W3 AI-rung (rung 4/5) + telemetry tests (task 032):
/// <list type="bullet">
///   <item><see cref="CapturingLogger{T}"/> — a spy <see cref="ILogger{TCategoryName}"/> that records
///   emitted entries (level, event id, formatted message, structured state) so telemetry assertions can be
///   made against the real BFF logging sink boundary.</item>
///   <item><see cref="FakeRung"/> — a configurable <see cref="IAssociationRung"/> that returns pre-seeded
///   matches and records how many times it was evaluated (a spy for short-circuit assertions).</item>
///   <item>Scope-factory + record-search builders — wire a mocked AI facade into the rung's
///   <see cref="IServiceScopeFactory"/> resolution and build search responses/classification inputs.</item>
/// </list>
/// The rungs resolve their facade from a per-eval <see cref="IServiceScopeFactory"/> scope, so a boundary
/// mock is registered in a real <see cref="ServiceCollection"/> — the REAL rung logic then runs over the
/// mocked AI boundary (ADR-038 boundary mock, not a DI-registration test).
/// </summary>
internal static class RungTestSupport
{
    /// <summary>
    /// Build an <see cref="IServiceScopeFactory"/> whose per-scope provider resolves <typeparamref name="T"/>
    /// to <paramref name="instance"/> — the exact wiring the AI rungs use to resolve their scoped facade.
    /// </summary>
    public static IServiceScopeFactory ScopeFactoryFor<T>(T instance) where T : class
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => instance);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    public static RecordSearchResult Hit(
        string recordType, Guid id, double score, string? name = null, params string[] reasons) =>
        new()
        {
            RecordId = id.ToString(),
            RecordType = recordType,
            RecordName = name ?? "Record",
            ConfidenceScore = score,
            MatchReasons = reasons.Length > 0 ? reasons : null,
        };

    public static RecordSearchResponse SearchResponse(params RecordSearchResult[] results) =>
        new()
        {
            Results = results,
            Metadata = new RecordSearchMetadata
            {
                HybridMode = RecordHybridSearchMode.Rrf,
                TotalCount = results.Length,
                SearchTime = 1.0,
            },
        };

    public static NormalizedMessage Envelope(
        string? subject = "Subject text",
        string? bodyText = "Body text",
        CommunicationDirection direction = CommunicationDirection.Incoming) =>
        new()
        {
            Direction = direction,
            Subject = subject,
            BodyText = bodyText,
            From = "sender@external.com",
        };
}

/// <summary>
/// A configurable <see cref="IAssociationRung"/> test double. Returns a fixed set of matches and records the
/// number of times it was evaluated (so ladder tests can assert a rung was — or was NOT — invoked).
/// </summary>
internal sealed class FakeRung : IAssociationRung
{
    private readonly IReadOnlyList<RungMatch> _matches;

    public FakeRung(RungKind kind, int order, params RungMatch[] matches)
    {
        Kind = kind;
        Order = order;
        _matches = matches;
    }

    public RungKind Kind { get; }
    public int Order { get; }
    public int EvaluationCount { get; private set; }

    public Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        EvaluationCount++;
        return Task.FromResult(_matches);
    }
}

/// <summary>
/// A spy <see cref="ILogger{TCategoryName}"/> that captures every emitted entry — level, event id, the
/// formatted message, and the structured key/value state — so telemetry emission can be asserted against the
/// real logging boundary (the "existing sink" per §11; no new telemetry framework).
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var fields = new Dictionary<string, object?>();
        if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
        {
            foreach (var kvp in kvps)
                fields[kvp.Key] = kvp.Value;
        }

        Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception, fields));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

/// <summary>A single captured log entry.</summary>
internal sealed record LogEntry(
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Fields)
{
    /// <summary>Get a structured field value by its message-template name (e.g. <c>Outcome</c>).</summary>
    public object? Field(string name) => Fields.TryGetValue(name, out var v) ? v : null;
}
