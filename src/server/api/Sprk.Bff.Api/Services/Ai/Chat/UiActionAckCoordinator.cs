using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// In-process implementation of <see cref="IUiActionAckCoordinator"/> (D-F3 / FR-A1-08 / task
/// AIR2-037). Registered as a singleton (see <c>AiChatModule.AddAiChatModule</c>) so a pending
/// wait started by one scoped request (the tool-call handler) can be resolved by a LATER,
/// separate scoped request (the client's ack POST).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why in-process, not Redis/Cosmos</b>: the wait lifetime is seconds (one ack round-trip),
/// scoped to a single BFF instance handling both the SSE stream and the ack POST for the SAME
/// chat turn (App Service sticky-session / single-instance-per-request-affinity is not required
/// here — both calls land within the same short window; a cross-instance loss just falls
/// through to the honest timeout path, which is the CORRECT degrade, never a fabricated
/// success). Promoting this to a distributed store is unwarranted engineering for a sub-10s
/// coordination window — see CLAUDE.md §11 cost-of-doing-nothing test.
/// </para>
/// <para>
/// <b>Uses <see cref="TimeProvider"/></b> (not <c>Task.Delay</c> directly) so unit tests can
/// deterministically trigger the timeout branch via <c>FakeTimeProvider.Advance</c> (per
/// tests/CLAUDE.md TimeProvider-over-Stopwatch rule) instead of a real wall-clock wait.
/// </para>
/// </remarks>
public sealed class UiActionAckCoordinator : IUiActionAckCoordinator
{
    private readonly ConcurrentDictionary<(string SessionId, string FrameId), TaskCompletionSource<bool>> _pending = new();
    private readonly ILogger<UiActionAckCoordinator> _logger;
    private readonly TimeProvider _timeProvider;

    public UiActionAckCoordinator(ILogger<UiActionAckCoordinator> logger, TimeProvider timeProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<UiActionAckOutcome> WaitForAckAsync(
        string sessionId,
        string frameId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(frameId);

        var key = (sessionId, frameId);
        // RunContinuationsAsynchronously: TryAcknowledge (called from a DIFFERENT request's
        // thread) must not run the waiter's continuation synchronously on the ack endpoint's
        // thread.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pending.TryAdd(key, tcs))
        {
            // Extremely unlikely — frame ids are per-tool-call GUIDs — but fail loudly rather
            // than silently clobbering the ALREADY-PENDING waiter's completion source. Awaiting
            // our own local `tcs` here would be pointless: TryAcknowledge resolves the dictionary
            // entry (the OTHER waiter's tcs), never this local one, so this call could only ever
            // resolve via its own timeout. Short-circuit to an honest failure immediately instead
            // of waiting out the full timeout for a wait that can never succeed.
            _logger.LogWarning(
                "UiActionAckCoordinator: duplicate WaitForAckAsync registration for session={SessionId} frame={FrameId} — failing this wait immediately (honest failure; FR-A1-08 never fabricates success)",
                sessionId, frameId);
            return UiActionAckOutcome.TimedOut;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delayTask = Task.Delay(timeout, _timeProvider, timeoutCts.Token);

            var completed = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);

            if (completed == tcs.Task)
            {
                // Stop the pending delay timer — it would otherwise fire uselessly later.
                timeoutCts.Cancel();
                return UiActionAckOutcome.Acknowledged;
            }

            // No ack arrived. Either the timeout genuinely elapsed, OR the caller's
            // cancellationToken fired (e.g. client disconnect) — both are honest-failure
            // outcomes per FR-A1-08 (never a fabricated success), so both return TimedOut here.
            // The log distinguishes them for operator diagnostics only; the returned outcome
            // (and therefore the tool-call's honest-failure behavior) is unaffected.
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "UiActionAckCoordinator: ack wait CANCELLED (caller token) session={SessionId} frame={FrameId}",
                    sessionId, frameId);
            }
            else
            {
                _logger.LogInformation(
                    "UiActionAckCoordinator: ack wait TIMED OUT session={SessionId} frame={FrameId} timeoutMs={TimeoutMs}",
                    sessionId, frameId, timeout.TotalMilliseconds);
            }
            return UiActionAckOutcome.TimedOut;
        }
        finally
        {
            _pending.TryRemove(key, out _);
        }
    }

    /// <inheritdoc />
    public bool TryAcknowledge(string sessionId, string frameId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(frameId))
        {
            return false;
        }

        if (_pending.TryGetValue((sessionId, frameId), out var tcs) && tcs.TrySetResult(true))
        {
            _logger.LogInformation(
                "UiActionAckCoordinator: ack RECEIVED session={SessionId} frame={FrameId}",
                sessionId, frameId);
            return true;
        }

        // No pending waiter — the ack arrived after the timeout already fired, is a duplicate,
        // or references an unknown frame id. Benign: the ack endpoint returns 200 either way
        // (the client does not need to branch on this).
        _logger.LogInformation(
            "UiActionAckCoordinator: ack received with NO pending waiter session={SessionId} frame={FrameId}",
            sessionId, frameId);
        return false;
    }
}
