using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095c (option B, notes/095c-from-email-firm-optional.md): how far the tracking-scope schema has reached in this environment.
/// <c>scripts/ebilling/Deploy-EbillingSubmissionTrackingScopeSchema.ps1</c> moves an environment forward through these, in order; a stamp
/// provisioned from SpaarkeMaster reaches <see cref="P2ScopeKeyActive"/> once its key index is Active.
/// </summary>
public enum TrackingScopePhase
{
    /// <summary>P0: <c>sprk_trackingscope</c> does not exist. Today's behaviour: counted per firm, no scope written, a firm is required.</summary>
    P0NoScopeColumn = 0,

    /// <summary>P1: the column exists but the scope key is not Active. The scope is written on every row; counted per firm under the firm
    /// key; a firm is still required.</summary>
    P1ScopeColumn = 1,

    /// <summary>P2: the scope key on (<c>sprk_trackingnumber</c>, <c>sprk_trackingscope</c>) is Active. Counted per scope; a submission with no
    /// firm is numbered in the <c>none</c> scope.</summary>
    P2ScopeKeyActive = 2,
}

/// <summary>
/// ebilling-r1 task 095c: reads the <see cref="TrackingScopePhase"/> from the <c>sprk_invoicesubmission</c> entity metadata, through the same
/// <see cref="RetrieveEntityRequest"/> path as <c>ComposeIdentityKeyHealthCheck</c> (the BFF's ready <c>ServiceClient</c>, unwrapped best-effort
/// by <see cref="DataverseServiceClientExtensions.TryUnwrapServiceClient"/>). No new interface and no new registration (CLAUDE.md section 11):
/// the allocator owns the only caller.
/// </summary>
internal static class TrackingScopePhaseProbe
{
    /// <summary>The scope column: the firm id in D form, or <see cref="TrackingNumberAllocator.NoFirmScope"/>.</summary>
    internal const string ScopeColumn = TrackingNumberAllocator.ScopeColumn;

    /// <summary>The alternate key the allocator relies on from P2: (<c>sprk_trackingnumber</c>, <c>sprk_trackingscope</c>).</summary>
    internal const string ScopeKeyName = TrackingNumberAllocator.ScopeKeyName;

    /// <summary>The key's <c>EntityKeyIndexStatus</c> once its index is built, compared as text as <c>ComposeIdentityKeyHealthCheck</c> does.</summary>
    private const string ActiveStatus = "Active";

    /// <summary>
    /// The phase a metadata read shows: no scope column is P0; the column with no Active scope key over exactly
    /// (<c>sprk_trackingnumber</c>, <c>sprk_trackingscope</c>) is P1; that key Active is P2. A key that is Pending, InProgress or Failed, or
    /// one of that name over other columns, is not relied on (P1).
    /// </summary>
    internal static TrackingScopePhase PhaseOf(
        IEnumerable<string?>? attributeNames,
        IEnumerable<(string? LogicalName, IReadOnlyCollection<string>? Columns, string? Status)>? keys)
    {
        if (attributeNames is null || !attributeNames.Any(a => string.Equals(a, ScopeColumn, StringComparison.OrdinalIgnoreCase)))
        {
            return TrackingScopePhase.P0NoScopeColumn;
        }

        var active = (keys ?? []).Any(k =>
            string.Equals(k.LogicalName, ScopeKeyName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(k.Status, ActiveStatus, StringComparison.OrdinalIgnoreCase)
            && k.Columns is { Count: 2 } columns
            && columns.Contains(TrackingNumberAllocator.TrackingNumberColumn, StringComparer.OrdinalIgnoreCase)
            && columns.Contains(ScopeColumn, StringComparer.OrdinalIgnoreCase));
        return active ? TrackingScopePhase.P2ScopeKeyActive : TrackingScopePhase.P1ScopeColumn;
    }

    /// <summary>
    /// Reads the phase from Dataverse metadata (published, not as-if-published: the write path sees the published schema). Null when it
    /// cannot be read (no ready <c>ServiceClient</c>, or a fault): an unreadable phase is never a move forward.
    /// </summary>
    internal static async Task<TrackingScopePhase?> ReadAsync(IDataverseService dataverse, ILogger logger, CancellationToken ct)
    {
        try
        {
            var client = dataverse.TryUnwrapServiceClient(nameof(TrackingScopePhaseProbe), logger);
            if (client is null)
            {
                return null;
            }

            var response = (RetrieveEntityResponse)await client.ExecuteAsync(
                new RetrieveEntityRequest
                {
                    LogicalName = TrackingNumberAllocator.SubmissionEntity,
                    EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
                    RetrieveAsIfPublished = false,
                },
                ct).ConfigureAwait(false);
            var metadata = response.EntityMetadata;
            return PhaseOf(
                metadata?.Attributes?.Select(a => a.LogicalName),
                metadata?.Keys?.Select(k => ((string?)k.LogicalName, (IReadOnlyCollection<string>?)k.KeyAttributes, (string?)k.EntityKeyIndexStatus.ToString())));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Not a move forward: the cached phase stands and the next read is after the refresh interval (or when a write forces one).
            logger.LogWarning(ex, "The tracking-scope phase of {Table} could not be read; the cached phase stands", TrackingNumberAllocator.SubmissionEntity);
            return null;
        }
    }
}

/// <summary>
/// ebilling-r1 task 095c: the process-wide cache of the <see cref="TrackingScopePhase"/>. The phase only moves forward; it is re-read at most
/// every <see cref="RefreshInterval"/> until it reaches <see cref="TrackingScopePhase.P2ScopeKeyActive"/>, and never after. A write that shows
/// the cached phase is stale (the scope column refused a null) forces a re-read.
/// </summary>
/// <remarks>
/// The allocator is scoped, so its cache is a static (<see cref="Shared"/>): no new registration. Tests construct their own instance with a
/// fake clock.
/// </remarks>
internal sealed class TrackingScopePhaseCache
{
    /// <summary>The longest a cached phase below P2 is used. The schema script waits longer than this between adding the column and
    /// backfilling it (step 3), so every instance writes the scope before the backfill runs.</summary>
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>The cache every allocator in this process uses.</summary>
    internal static TrackingScopePhaseCache Shared { get; } = new(TimeProvider.System);

    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _phase = (int)TrackingScopePhase.P0NoScopeColumn;
    private long _readAtUtcTicks; // 0: never read
    private long _readsStarted;
    private int _previousSequenceMissingNoted;

    internal TrackingScopePhaseCache(TimeProvider clock) => _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// Task 095d-1a (K4): true the first time it is called on this cache, false after. The allocator logs "<c>sprk_previoussequence</c> does not
    /// exist yet" when it first meets that, once per process (this cache is the allocator's one piece of process-wide state; a test's own cache
    /// is its own process).
    /// </summary>
    internal bool TryMarkPreviousSequenceMissing() => Interlocked.Exchange(ref _previousSequenceMissingNoted, 1) == 0;

    /// <summary>
    /// How many reads have been started. A caller that the platform refused takes this at the refusal and passes it as
    /// <c>refusedAtRead</c>: a read that started after that point already shows the schema the refusal came from.
    /// </summary>
    internal long ReadsStarted => Volatile.Read(ref _readsStarted);

    /// <summary>The phase now: the cached one while it is fresh (or P2), else <paramref name="read"/>'s answer, never lower than the cached one.
    /// With <paramref name="force"/> the read is made even while the cached phase is fresh (still never once P2 is reached), unless
    /// <paramref name="refusedAtRead"/> is given and a read started after it: callers queued behind one forced read then share its answer
    /// (one metadata read, not one per caller).</summary>
    internal async Task<TrackingScopePhase> GetAsync(
        Func<CancellationToken, Task<TrackingScopePhase?>> read, bool force, CancellationToken ct, long? refusedAtRead = null)
    {
        if (Current == TrackingScopePhase.P2ScopeKeyActive || (!force && IsFresh()))
        {
            return Current;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Current == TrackingScopePhase.P2ScopeKeyActive || (!force && IsFresh()))
            {
                return Current;
            }

            if (force && refusedAtRead is { } at && Volatile.Read(ref _readsStarted) > at)
            {
                // Reads are serial (this gate), so none is in flight: a read that started after this caller's refusal has completed.
                return Current;
            }

            Interlocked.Increment(ref _readsStarted);
            if (await read(ct).ConfigureAwait(false) is { } seen && seen > Current)
            {
                Volatile.Write(ref _phase, (int)seen);
            }

            Volatile.Write(ref _readAtUtcTicks, _clock.GetUtcNow().UtcTicks);
            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    private TrackingScopePhase Current => (TrackingScopePhase)Volatile.Read(ref _phase);

    private bool IsFresh()
    {
        var at = Volatile.Read(ref _readAtUtcTicks);
        return at != 0 && _clock.GetUtcNow().UtcTicks - at < RefreshInterval.Ticks;
    }
}
