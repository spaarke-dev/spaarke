namespace Sprk.Bff.Api.Services.Todo;

/// <summary>
/// Reconciles a single <c>sprk_todo</c> record to its mirror task in Microsoft Graph
/// (<c>/me/todo/lists/{listId}/tasks</c>). One-way (Dataverse → Graph) per smart-todo-decoupling-r3
/// design §6; the inbound path lives behind <see cref="ITodoSubscriptionManager"/> webhooks.
/// </summary>
/// <remarks>
/// <para>Feature-gated by <c>Spaarke:Graph:TodoSync:Enabled</c>. When false, the Null-Object
/// implementation (<c>NullObject.NullTodoGraphSyncHandler</c>) is bound and every call is a no-op
/// per ADR-032 P2 (quiet, fire-and-forget semantics — absence of Graph mirror equals "sync deferred").</para>
/// <para>Real implementation arrives in Phase 7 (task 061). Until then, the placeholder
/// <c>NotImplementedTodoGraphSyncHandler</c> throws if the flag is on but the impl is missing.</para>
/// </remarks>
public interface ITodoGraphSyncHandler
{
    /// <summary>
    /// Reconciles the <c>sprk_todo</c> identified by <paramref name="todoId"/> with its
    /// Graph mirror per <paramref name="op"/>.
    /// </summary>
    /// <param name="todoId">Dataverse primary key of the <c>sprk_todo</c> record.</param>
    /// <param name="op">Operation kind (Create / Update / Delete).</param>
    /// <param name="ct">Cancellation token.</param>
    Task SyncAsync(Guid todoId, SyncOp op, CancellationToken ct);
}
