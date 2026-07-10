using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Tests.Integration.Seam.Ai.Memory;

/// <summary>
/// In-memory <see cref="IMemoryItemStore"/> test double (no Cosmos in CI) that replicates the REAL
/// upsert-by-<c>(scope, fact.Type, fact.Key)</c>-per-subject supersession semantics of the production
/// <see cref="MemoryItemStore"/> — by keying documents on the SAME deterministic id
/// (<see cref="MemoryItemStore.BuildItemId"/>, accessible via <c>InternalsVisibleTo</c>) so a repeated
/// capture for the same key collapses to one document exactly as Cosmos would. The prompt-fragment
/// serialization reuses the production <see cref="MemoryItemStore.BuildPromptFragment"/>, so the
/// recall path a memory.write test asserts is the real one.
/// </summary>
internal sealed class FakeMemoryItemStore : IMemoryItemStore
{
    private readonly Dictionary<string, MemoryItem> _docs = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public FakeMemoryItemStore(TimeProvider? timeProvider = null) => _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Every item ever upserted, in insertion/replacement order (test observation).</summary>
    public IReadOnlyCollection<MemoryItem> All => _docs.Values;

    public Task<MemoryItem> UpsertAsync(MemoryItem item, string tenantId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        MemoryItemContract.EnsureValidMemoryScope(item.Scope);
        if (string.Equals(item.Scope, MemoryScope.Record, StringComparison.Ordinal))
        {
            DataverseFieldMirrorGuard.EnsureNotDataverseFieldMirror(item.Fact);
        }

        var subjectKey = item.PartitionKey;
        if (string.IsNullOrWhiteSpace(subjectKey))
        {
            throw new ArgumentException("Memory item is missing its subject keying (subjectId / userId).", nameof(item));
        }

        var id = MemoryItemStore.BuildItemId(item.Scope, item.Fact.Type, item.Fact.Key);
        var docKey = subjectKey + "|" + id;

        MemoryItem stored;
        if (_docs.TryGetValue(docKey, out var existing))
        {
            // Supersession: same (scope, Type, Key) for the same subject REPLACES the fact, preserving
            // the original creation stamps and marking the update instant (mirrors the real store).
            stored = item with
            {
                Id = id,
                CreatedAt = existing.CreatedAt,
                CreatedBy = existing.CreatedBy ?? item.CreatedBy,
                UpdatedAt = _timeProvider.GetUtcNow(),
            };
        }
        else
        {
            stored = item with { Id = id };
        }

        _docs[docKey] = stored;
        return Task.FromResult(stored);
    }

    public Task<IReadOnlyList<MemoryItem>> GetForRecordAsync(string subjectType, string subjectId, CancellationToken ct = default)
    {
        var results = _docs.Values
            .Where(i => string.Equals(i.Scope, MemoryScope.Record, StringComparison.Ordinal)
                        && string.Equals(i.SubjectType, subjectType, StringComparison.Ordinal)
                        && string.Equals(i.SubjectId, subjectId, StringComparison.Ordinal))
            .ToList();
        return Task.FromResult<IReadOnlyList<MemoryItem>>(results);
    }

    public Task<IReadOnlyList<MemoryItem>> GetForUserAsync(string userId, CancellationToken ct = default)
    {
        var results = _docs.Values
            .Where(i => string.Equals(i.Scope, MemoryScope.User, StringComparison.Ordinal)
                        && string.Equals(i.UserId, userId, StringComparison.Ordinal))
            .ToList();
        return Task.FromResult<IReadOnlyList<MemoryItem>>(results);
    }

    public Task DeleteItemAsync(string subjectKey, string itemId, CancellationToken ct = default)
    {
        _docs.Remove(subjectKey + "|" + itemId);
        return Task.CompletedTask;
    }

    public Task EraseSubjectAsync(string subjectKey, CancellationToken ct = default)
    {
        var prefix = subjectKey + "|";
        foreach (var key in _docs.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _docs.Remove(key);
        }
        return Task.CompletedTask;
    }

    public async Task<string> ToRecordPromptFragmentAsync(string subjectType, string subjectId, CancellationToken ct = default)
    {
        var items = await GetForRecordAsync(subjectType, subjectId, ct).ConfigureAwait(false);
        return items.Count == 0
            ? string.Empty
            : MemoryItemStore.BuildPromptFragment(items.Select(i => i.Fact).ToList(), subjectType);
    }
}
