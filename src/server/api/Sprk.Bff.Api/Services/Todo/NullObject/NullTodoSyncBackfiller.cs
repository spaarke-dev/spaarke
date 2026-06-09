using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Todo.NullObject;

/// <summary>
/// Null-Object implementation of <see cref="ITodoSyncBackfiller"/> bound when
/// <c>Spaarke:Graph:TodoSync:Enabled = false</c>. No-op, but logs once per invocation
/// per ADR-032 P2 (observability matters for backfill — an operator manually triggering a
/// backfill against a flag-off BFF should see a log entry confirming the no-op rather than
/// silent success).
/// </summary>
internal sealed class NullTodoSyncBackfiller : ITodoSyncBackfiller
{
    private readonly ILogger<NullTodoSyncBackfiller> _logger;

    public NullTodoSyncBackfiller(ILogger<NullTodoSyncBackfiller> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task BackfillAsync(Guid userId, CancellationToken ct)
    {
        // P2 logging: per-call (not per-process) — the cost is one log line per backfill
        // request and the value is immediate operator visibility into the flag-off no-op.
        _logger.LogInformation(
            "TodoSyncBackfiller no-op (Spaarke:Graph:TodoSync:Enabled=false) for userId={UserId}. "
                + "Real backfill will run in Phase 7 (task 065) once the flag is enabled.",
            userId);
        return Task.CompletedTask;
    }
}
