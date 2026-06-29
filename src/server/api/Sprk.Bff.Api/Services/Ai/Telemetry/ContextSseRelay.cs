namespace Sprk.Bff.Api.Services.Ai.Telemetry;

/// <summary>
/// R6 DEF-001 / task 095 Phase 3 — Production implementation of
/// <see cref="IContextSseRelay"/>. Holds a per-request writer delegate and serializes
/// concurrent <see cref="TryWriteAsync"/> invocations via a <see cref="SemaphoreSlim"/>
/// so SSE frames never interleave.
///
/// <para>
/// <b>Failure mode</b>: every exception thrown by the writer (e.g., client disconnect,
/// response disposed, cancellation token tripped) is swallowed — the trace
/// side-channel is best-effort. The emitter call site (singleton
/// <see cref="ContextEventEmitter"/>) MUST NOT observe relay failures.
/// </para>
/// </summary>
public sealed class ContextSseRelay : IContextSseRelay, IDisposable
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ILogger<ContextSseRelay> _logger;

    public ContextSseRelay(ILogger<ContextSseRelay> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Func<ContextSseEventDto, CancellationToken, Task>? Writer { get; set; }

    /// <inheritdoc />
    public async Task TryWriteAsync(ContextSseEventDto dto, CancellationToken cancellationToken)
    {
        var writer = Writer;
        if (writer is null) return;

        // Best-effort, swallow-all — never throw back into the singleton emitter caller.
        try
        {
            if (cancellationToken.IsCancellationRequested) return;

            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Re-check Writer inside the gate (it may have been cleared by the
                // SSE endpoint's finally between our nullcheck and our acquire).
                var current = Writer;
                if (current is null) return;
                await current(dto, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected mid-emission — expected, no log noise.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "[ContextSseRelay] context_event write failed (suppressed) — type={ContextEventType}",
                dto.ContextEventType);
        }
    }

    public void Dispose()
    {
        Writer = null;
        _writeGate.Dispose();
    }
}
