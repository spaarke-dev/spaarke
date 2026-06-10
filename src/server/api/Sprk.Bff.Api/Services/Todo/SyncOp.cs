namespace Sprk.Bff.Api.Services.Todo;

/// <summary>
/// Sync operation kind for <see cref="ITodoGraphSyncHandler.SyncAsync"/> — describes the
/// direction of a single `sprk_todo` ↔ Microsoft Graph `/me/todo` reconciliation.
/// </summary>
/// <remarks>
/// Phase 7 (task 061) introduces the real handler that maps these to Graph Tasks operations:
/// <list type="bullet">
///   <item><see cref="Create"/> → POST <c>/me/todo/lists/{listId}/tasks</c></item>
///   <item><see cref="Update"/> → PATCH <c>/me/todo/lists/{listId}/tasks/{taskId}</c></item>
///   <item><see cref="Delete"/> → DELETE <c>/me/todo/lists/{listId}/tasks/{taskId}</c></item>
/// </list>
/// While <c>Spaarke:Graph:TodoSync:Enabled = false</c>, the Null-Object impl is bound and
/// every operation is a no-op (per ADR-032 P2 quiet semantics).
/// </remarks>
public enum SyncOp
{
    /// <summary>Create the corresponding Graph task for a newly-inserted <c>sprk_todo</c>.</summary>
    Create = 0,

    /// <summary>Update the corresponding Graph task to reflect a changed <c>sprk_todo</c>.</summary>
    Update = 1,

    /// <summary>Delete the corresponding Graph task because <c>sprk_todo</c> was removed.</summary>
    Delete = 2,
}
