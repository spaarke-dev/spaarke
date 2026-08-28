// -----------------------------------------------------------------------------
// DispatchIdempotencyService.cs
//
// L2 CONTROL-PLANE Level-2 idempotency guard -- Redis-backed implementation
// (task 105, Phase C'' Wave G-1). Replaces task 102's
// <see cref="NoOpDispatchIdempotencyService"/> as the DI-registered
// <see cref="IDispatchIdempotencyService"/> (see
// <see cref="DispatchModule.AddDispatchModule"/>).
//
// PURPOSE (see IDispatchIdempotencyService.cs for the full contract + the
// three-level idempotency table):
//   Per-message processed-marker + in-flight lock in a shared cache (Redis
//   via IDistributedCache), gating the dispatcher's dequeue path so a
//   duplicate Service Bus delivery whose L1 window has expired never fires
//   the handler twice, and two dispatcher instances never both invoke the
//   same handler for the same envelope concurrently.
//
// INTENTIONAL ~100-LINE COPY (CLAUDE.md §11 justification):
//   Existing    -- BFF's Services/Jobs/IdempotencyService.cs already
//                  implements exactly this shape against IDistributedCache
//                  (processed-marker + acquire/release lock, fail-open on
//                  cache outage). L2 CANNOT reference the BFF assembly --
//                  same isolation rule as IProvisioningHandler.cs:8-13 (L2
//                  is a PEER service, not a BFF extension; ADR-010 + project
//                  MUST rule).
//   Extension   -- no L2 interface currently owns a per-message dedup gate;
//                  this is a wholly new concern at the L2 dequeue path
//                  (distinct call site from the BFF's per-handler-body use).
//   Cost-of-doing-nothing -- without a real Level-2 gate, the dispatcher's
//                  Redis-outage-independent duplicate-execution window
//                  (lock-loss-under-renewal-failure, per DS-2 §2.6) has NO
//                  guard beyond L1 (SB dedup -- inert until task 108) + L3
//                  (per-handler CompletedPhases scan). Task 102 shipped the
//                  NoOp placeholder specifically to unblock the dispatcher's
//                  boot; this class closes that gap.
//
// KEY FORMAT + TTL (DS-2 §4-L2 verbatim):
//   Processed key: "provisioning:idempotency:processed:{messageId}"  TTL 24h.
//   Lock key:      "provisioning:idempotency:lock:{messageId}"       TTL =
//                  the caller-supplied <c>ttl</c> parameter (in production,
//                  DispatcherOptions.MaxHandlerDuration -- NOT the BFF's
//                  5-min default). A 30-60 min handler must hold the lock
//                  for its whole runtime since IDistributedCache offers no
//                  mid-flight lock renewal.
//   {messageId} = ServiceBusHandlerEnqueuer.ComputeMessageId(envelope),
//   recomputed on the receive side (see ProvisioningHandlerDispatcher.
//   DispatchCoreAsync) so the gate is effective even while L1 SB dedup
//   remains inert (Wave-C4 pre-queue-recreate posture).
//
// FAIL-OPEN POSTURE (mirror of BFF IdempotencyService.cs:39-44,92-97 --
// canonical reference cited in this task's POML):
//   Every method swallows cache exceptions and degrades to the PERMISSIVE
//   outcome: IsProcessedAsync -> false (not a duplicate), TryAcquireLockAsync
//   -> true (lock granted), MarkProcessedAsync / ReleaseLockAsync -> no-op.
//   L1 (SB MessageId dedup) + L3 (handler-body CompletedPhases scan) backstop
//   correctness when Redis is unreachable -- provisioning MUST NOT hard-
//   depend on cache availability (DS-2 §4-L2).
//
// NON-ATOMIC LOCK ACQUISITION (accepted, mirrors BFF exactly):
//   TryAcquireLockAsync is a GET-then-SET, not an atomic Redis SETNX. This
//   is the SAME non-atomic shape as BFF IdempotencyService.
//   TryAcquireProcessingLockAsync -- IDistributedCache's abstraction does
//   not expose a conditional-set primitive, and introducing a
//   StackExchange.Redis-specific SETNX call here would break the
//   IDistributedCache-only dependency this class is deliberately scoped to
//   (keeps the class provider-agnostic + testable against
//   AddDistributedMemoryCache, matching DispatchIdempotencyServiceTests).
//   The narrow race window this leaves (two dispatchers both GET a miss
//   before either SETs) is bounded by L1 dedup + the dispatcher's own
//   session-serialization (MaxConcurrentCallsPerSession=1 -- two
//   dispatchers only race here across DIFFERENT customer sessions on
//   different instances, which is the exact cross-instance scenario this
//   guard exists for, not a same-session race).
// -----------------------------------------------------------------------------

using System.Text;
using Microsoft.Extensions.Caching.Distributed;

namespace Sprk.Provisioning.ControlPlane.Dispatch;

/// <summary>
/// Redis-backed (via <see cref="IDistributedCache"/>) implementation of
/// <see cref="IDispatchIdempotencyService"/>. See file header for the key
/// format, TTL contract, and fail-open posture.
/// </summary>
public sealed class DispatchIdempotencyService : IDispatchIdempotencyService
{
    private const string ProcessedKeyPrefix = "provisioning:idempotency:processed:";
    private const string LockKeyPrefix = "provisioning:idempotency:lock:";

    /// <summary>
    /// TTL for the processed-marker -- 24h per DS-2 §4-L2 (independent of
    /// <see cref="DispatcherOptions.MaxHandlerDuration"/>; this bounds how
    /// long a completed dispatch is remembered as "already processed", not
    /// how long a handler may run).
    /// </summary>
    private static readonly TimeSpan ProcessedMarkerTtl = TimeSpan.FromHours(24);

    private static readonly byte[] ProcessedValue = Encoding.UTF8.GetBytes("processed");
    private static readonly byte[] LockedValue = Encoding.UTF8.GetBytes("locked");

    private readonly IDistributedCache _cache;
    private readonly ILogger<DispatchIdempotencyService> _logger;

    /// <summary>
    /// Constructs the service. <paramref name="cache"/> is resolved from DI
    /// (Redis via <c>AddStackExchangeRedisCache</c>, or an in-memory fallback
    /// for hosts without a configured Redis connection -- see
    /// <see cref="DispatchModule.AddDispatchModule"/>).
    /// </summary>
    public DispatchIdempotencyService(IDistributedCache cache, ILogger<DispatchIdempotencyService> logger)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        try
        {
            var value = await _cache.GetAsync(ProcessedKey(messageId), cancellationToken).ConfigureAwait(false);
            var processed = value is not null;

            if (processed)
            {
                _logger.LogInformation(
                    "Level-2 processed-marker hit for MessageId={MessageId}.", messageId);
            }

            return processed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to check Level-2 processed-marker for MessageId={MessageId} -- " +
                "failing OPEN (treating as NOT processed). L1 + L3 backstop correctness.",
                messageId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryAcquireLockAsync(string messageId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        try
        {
            var key = LockKey(messageId);
            var existing = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Level-2 lock already held for MessageId={MessageId} -- peer dispatcher in flight.",
                    messageId);
                return false;
            }

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl,
            };
            await _cache.SetAsync(key, LockedValue, options, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(
                "Level-2 lock acquired for MessageId={MessageId} (TtlMinutes={TtlMinutes}).",
                messageId, (int)ttl.TotalMinutes);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to acquire Level-2 lock for MessageId={MessageId} -- failing OPEN " +
                "(granting the lock). L1 + L3 backstop correctness; provisioning must not " +
                "hard-depend on cache availability.",
                messageId);
            return true;
        }
    }

    /// <inheritdoc/>
    public async Task MarkProcessedAsync(string messageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        try
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ProcessedMarkerTtl,
            };
            await _cache.SetAsync(ProcessedKey(messageId), ProcessedValue, options, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug("Level-2 processed-marker set for MessageId={MessageId}.", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to set Level-2 processed-marker for MessageId={MessageId} -- swallowed. " +
                "The Cosmos transition already landed; a redelivery would be caught by L3 " +
                "handler-body dedup.",
                messageId);
        }
    }

    /// <inheritdoc/>
    public async Task ReleaseLockAsync(string messageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        try
        {
            await _cache.RemoveAsync(LockKey(messageId), cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Level-2 lock released for MessageId={MessageId}.", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to release Level-2 lock for MessageId={MessageId} -- swallowed; " +
                "the lock will self-expire at its TTL.",
                messageId);
        }
    }

    private static string ProcessedKey(string messageId) => ProcessedKeyPrefix + messageId;

    private static string LockKey(string messageId) => LockKeyPrefix + messageId;
}
