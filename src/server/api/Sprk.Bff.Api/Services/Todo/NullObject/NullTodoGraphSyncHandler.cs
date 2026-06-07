namespace Sprk.Bff.Api.Services.Todo.NullObject;

/// <summary>
/// Null-Object implementation of <see cref="ITodoGraphSyncHandler"/> bound when
/// <c>Spaarke:Graph:TodoSync:Enabled = false</c>. No-op per ADR-032 P2 (quiet, fire-and-forget).
/// </summary>
/// <remarks>
/// Rationale (ADR-032 §"Three Patterns"): the sync handler is a side-effecting fire-and-forget
/// service; under feature-off the absence of a mirror in Graph is the correct steady-state
/// behavior (Dataverse remains the system-of-record). A P3 fail-fast would falsely signal to
/// upstream job handlers that the sync was unrecoverable; a P2 quiet no-op preserves the
/// pre-feature behavior cleanly.
/// </remarks>
internal sealed class NullTodoGraphSyncHandler : ITodoGraphSyncHandler
{
    public Task SyncAsync(Guid todoId, SyncOp op, CancellationToken ct) => Task.CompletedTask;
}
