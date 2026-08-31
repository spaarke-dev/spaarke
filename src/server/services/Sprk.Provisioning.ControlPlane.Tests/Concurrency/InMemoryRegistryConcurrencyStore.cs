// -----------------------------------------------------------------------------
// InMemoryRegistryConcurrencyStore.cs
//
// Test-only IRegistryConcurrencyStore impl used by CustomerRunGuardTests.
//
// FAITHFUL to production semantics:
//   - Lookup returns Found + a synthetic ETag; the ETag increments on every
//     successful write so a stale-ETag PATCH surfaces as PreconditionFailed.
//   - TrySetIfNullAsync respects the null->value contract: if the current
//     value is NOT null it does NOT throw (it's the guard's job to check
//     via Lookup first); it simply performs the requested PATCH (the ETag
//     is what protects consistency).
//   - Simulated-failure hooks let tests force a TransientFailure for
//     LookupAsync (per-call and until-cleared).
//
// SIZE: kept short — this is a test double, not a Cosmos-quality store.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Concurrency;

namespace Sprk.Provisioning.ControlPlane.Tests.Concurrency;

/// <summary>
/// Test-only <see cref="IRegistryConcurrencyStore"/>. Backs the guard unit
/// tests without any HTTP / mock-HTTP surface (satisfies ADR-038 §5).
/// </summary>
public sealed class InMemoryRegistryConcurrencyStore : IRegistryConcurrencyStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, RowState> _rowsByCustomerId = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _customerIdByRowId = new();

    public string? ForceLookupFailure { get; set; }
    public string? ForceWriteFailure { get; set; }

    /// <summary>
    /// Seeds a registry row for <paramref name="customerId"/> with initial
    /// <paramref name="currentRunId"/> (may be null). Returns the row id
    /// (which tests can also read via LookupAsync).
    /// </summary>
    public Guid Seed(string customerId, string? currentRunId = null)
    {
        var rowId = Guid.NewGuid();
        lock (_lock)
        {
            _rowsByCustomerId[customerId] = new RowState(rowId, currentRunId, EtagCounter: 1);
            _customerIdByRowId[rowId] = customerId;
        }
        return rowId;
    }

    public string? PeekCurrentRunId(string customerId)
    {
        lock (_lock)
        {
            return _rowsByCustomerId.TryGetValue(customerId, out var row) ? row.CurrentRunId : null;
        }
    }

    public Task<LookupOutcome> LookupAsync(string customerId, CancellationToken cancellationToken)
    {
        if (ForceLookupFailure is { } lookupErr)
        {
            return Task.FromResult<LookupOutcome>(new LookupOutcome.TransientFailure(customerId, lookupErr));
        }
        lock (_lock)
        {
            if (!_rowsByCustomerId.TryGetValue(customerId, out var row))
            {
                return Task.FromResult<LookupOutcome>(new LookupOutcome.NotFound(customerId));
            }
            return Task.FromResult<LookupOutcome>(
                new LookupOutcome.Found(row.RowId, row.CurrentRunId, EtagOf(row.EtagCounter)));
        }
    }

    public Task<WriteOutcome> TrySetIfNullAsync(
        Guid environmentRowId,
        string newRunId,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        if (ForceWriteFailure is { } writeErr)
        {
            return Task.FromResult<WriteOutcome>(new WriteOutcome.TransientFailure(writeErr));
        }
        lock (_lock)
        {
            if (!_customerIdByRowId.TryGetValue(environmentRowId, out var customerId))
            {
                return Task.FromResult<WriteOutcome>(WriteOutcome.NotFound.Instance);
            }
            var row = _rowsByCustomerId[customerId];
            if (EtagOf(row.EtagCounter) != ifMatchEtag)
            {
                return Task.FromResult<WriteOutcome>(WriteOutcome.PreconditionFailed.Instance);
            }
            _rowsByCustomerId[customerId] = row with
            {
                CurrentRunId = newRunId,
                EtagCounter = row.EtagCounter + 1,
            };
            return Task.FromResult<WriteOutcome>(WriteOutcome.Success.Instance);
        }
    }

    public Task<WriteOutcome> TryClearAsync(
        Guid environmentRowId,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        if (ForceWriteFailure is { } writeErr)
        {
            return Task.FromResult<WriteOutcome>(new WriteOutcome.TransientFailure(writeErr));
        }
        lock (_lock)
        {
            if (!_customerIdByRowId.TryGetValue(environmentRowId, out var customerId))
            {
                return Task.FromResult<WriteOutcome>(WriteOutcome.NotFound.Instance);
            }
            var row = _rowsByCustomerId[customerId];
            if (EtagOf(row.EtagCounter) != ifMatchEtag)
            {
                return Task.FromResult<WriteOutcome>(WriteOutcome.PreconditionFailed.Instance);
            }
            _rowsByCustomerId[customerId] = row with
            {
                CurrentRunId = null,
                EtagCounter = row.EtagCounter + 1,
            };
            return Task.FromResult<WriteOutcome>(WriteOutcome.Success.Instance);
        }
    }

    /// <summary>
    /// Simulates an out-of-band writer stamping <paramref name="newRunId"/>
    /// (or null) onto the row for <paramref name="customerId"/>. Used to
    /// drive the ETag-race scenario deterministically in a single-threaded test.
    /// </summary>
    public void ExternalWrite(string customerId, string? newRunId)
    {
        lock (_lock)
        {
            var row = _rowsByCustomerId[customerId];
            _rowsByCustomerId[customerId] = row with
            {
                CurrentRunId = newRunId,
                EtagCounter = row.EtagCounter + 1,
            };
        }
    }

    private static string EtagOf(int counter) => $"W/\"{counter}\"";

    private sealed record RowState(Guid RowId, string? CurrentRunId, int EtagCounter);
}
