namespace Sprk.Bff.Api.Services.Todo.Placeholder;

/// <summary>
/// Placeholder implementation of <see cref="ITodoGraphSyncHandler"/> registered for the
/// <c>Spaarke:Graph:TodoSync:Enabled = true</c> branch until Phase 7 (task 061) lands the
/// real Graph-backed handler. Every method throws <see cref="NotImplementedException"/>
/// so a misconfigured "flag on but no real impl" environment fails loudly at the call site
/// rather than silently succeeding.
/// </summary>
/// <remarks>
/// This class is NOT a Null-Object — it intentionally throws. ADR-032 §"Anti-patterns"
/// forbids using Null-Object pattern to mask broken DI configuration; the placeholder
/// covers the inverse: a real-impl seat that is not yet filled.
/// </remarks>
internal sealed class NotImplementedTodoGraphSyncHandler : ITodoGraphSyncHandler
{
    public Task SyncAsync(Guid todoId, SyncOp op, CancellationToken ct)
        => throw new NotImplementedException(
            "Real TodoGraphSyncHandler will be added in Phase 7 (task 061). "
                + "Set Spaarke:Graph:TodoSync:Enabled=false to use the Null-Object path until then.");
}
