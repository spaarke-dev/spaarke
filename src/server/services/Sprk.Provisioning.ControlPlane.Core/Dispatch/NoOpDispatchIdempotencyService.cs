// -----------------------------------------------------------------------------
// NoOpDispatchIdempotencyService.cs
//
// L2 CONTROL-PLANE placeholder Level-2 idempotency service (task 102, Phase C''
// Wave G-1). REPLACED by task 105's Redis-backed DispatchIdempotencyService in
// the same wave.
//
// PURPOSE:
//   Ships the DispatchModule default so the dispatcher (task 102) can boot
//   and dispatch handlers TODAY, before task 105's Redis wiring lands. This
//   is the SAME Wave-C4 "Level-2 gate is currently a no-op" posture the
//   handler set already ships under -- see IProvisioningHandler.cs:38-40
//   preamble ("L2 does not yet have a Redis dependency; the level-2 gate
//   is currently a no-op for this project's Wave-C4 handler set").
//
// SEMANTICS (see IDispatchIdempotencyService for the operative contract):
//   IsProcessedAsync    -> Task.FromResult(false)   -- always report "not processed"
//   TryAcquireLockAsync -> Task.FromResult(true)    -- always grant the lock
//   MarkProcessedAsync  -> Task.CompletedTask       -- no-op
//   ReleaseLockAsync    -> Task.CompletedTask       -- no-op
//
//   Net effect: L1 (SB MessageId dedup, once queue recreated with sessions +
//   dedup per task 108) + L3 (per-handler CompletedPhases scan + idempotency
//   key) do the idempotency work. This is the same guarantee level the
//   Wave-C4 handler suite already expects.
//
// SWAP-OUT PATH (task 105):
//   DispatchModule.AddDispatchModule() default registration line changes
//   from:
//     services.AddSingleton<IDispatchIdempotencyService, NoOpDispatchIdempotencyService>();
//   to:
//     services.AddSingleton<IDispatchIdempotencyService, DispatchIdempotencyService>();
//   plus the Redis:ConnectionString app-setting binding. The dispatcher's
//   consumption of IDispatchIdempotencyService is UNCHANGED.
//
// NOT DEAD CODE AFTER SWAP:
//   This class remains useful for unit tests where a table-driven
//   DispatchCoreAsync test wants a permissive default WITHOUT wiring a
//   mocking framework -- construct the dispatcher with this placeholder and
//   only override the two specific behaviors the test cares about.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Dispatch;

/// <summary>
/// Fail-open no-op implementation of <see cref="IDispatchIdempotencyService"/>.
/// Wave-C4 default that lets the dispatcher boot before task 105's Redis
/// impl lands. See file header for the full swap-out plan.
/// </summary>
public sealed class NoOpDispatchIdempotencyService : IDispatchIdempotencyService
{
    /// <inheritdoc/>
    public Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task<bool> TryAcquireLockAsync(string messageId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task MarkProcessedAsync(string messageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ReleaseLockAsync(string messageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        return Task.CompletedTask;
    }
}
