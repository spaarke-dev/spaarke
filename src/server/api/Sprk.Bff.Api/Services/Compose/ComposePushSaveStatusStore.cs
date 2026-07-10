using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-28 (task 055) — cross-request persistence for the push/save pipeline's per-step
/// <see cref="JobAwareCompletionState"/> (ADR-009: <see cref="IDistributedCache"/> / Redis,
/// never <c>IMemoryCache</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why cross-request state for a synchronous pipeline</b>: <see cref="ComposeService.PushAnnotationsAsync"/>
/// runs push (native OOXML render) → save (SPE write) → version (confirm) entirely within ONE
/// HTTP request — there is no background job to poll here. But the core Phase A0 job-aware
/// <c>OutcomeCard</c> (HANDOFF-core-r2-A0-contract-requirements.md §2/§3) renders completion
/// evidence in the transcript, which may be read AFTER the initiating request has already
/// returned (a remount, an SSE reconnect, a Context-pane audit read — design.md §2.4). Persisting
/// the projected state here means that future consumer has real state to read from day one,
/// without this task guessing its poll contract — the clean seam the POML calls for.
/// </para>
/// <para>
/// <b>Key convention</b> mirrors <see cref="AnnotationReanchorService"/> /
/// <see cref="SpeSyncOrchestrator"/>: <c>sdap:compose:pushsave:{documentSpeId}</c>, 24h TTL (a
/// push/save status has no useful life past a work day; a fresh push overwrites the prior one).
/// </para>
/// <para>
/// <b>ADR-013 facade boundary</b>: no AI-internal type, no <c>Microsoft.Graph</c> type (ADR-007) —
/// this class only serializes/deserializes the existing <see cref="JobAwareCompletionState"/>
/// contract against <see cref="IDistributedCache"/>.
/// </para>
/// </remarks>
public sealed class ComposePushSaveStatusStore
{
    private const string KeyPrefix = "sdap:compose:pushsave:";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;

    public ComposePushSaveStatusStore(IDistributedCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <summary>
    /// Persists the projected push/save completion state for a document, overwriting any prior
    /// entry. This method itself propagates cache failures (it does not swallow them) — the
    /// CALLER decides whether a Redis outage should be treated as best-effort (see
    /// <see cref="ComposeService.PushAnnotationsAsync"/>, which never lets a status-persistence
    /// failure mask or roll back a document write/failure that already happened on its own terms).
    /// </summary>
    public Task SaveAsync(string documentSpeId, JobAwareCompletionState state, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentSpeId);
        ArgumentNullException.ThrowIfNull(state);

        var json = JsonSerializer.Serialize(state, JsonOptions);
        return _cache.SetStringAsync(
            KeyPrefix + documentSpeId,
            json,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
            cancellationToken);
    }

    /// <summary>
    /// Reads back the last-persisted push/save completion state for a document, or <c>null</c> if
    /// none has been recorded yet (no push has run, or the TTL expired). This is the seam a future
    /// OutcomeCard / status-poll endpoint attaches to — not built by this task (clean seam; the
    /// gate-dialog + OutcomeCard rendering halves wait on core Phase A0).
    /// </summary>
    public async Task<JobAwareCompletionState?> GetAsync(string documentSpeId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentSpeId);

        var json = await _cache.GetStringAsync(KeyPrefix + documentSpeId, cancellationToken).ConfigureAwait(false);
        return json is null ? null : JsonSerializer.Deserialize<JobAwareCompletionState>(json, JsonOptions);
    }
}
