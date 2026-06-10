namespace Sprk.Bff.Api.Services.Todo;

/// <summary>
/// Ensures a Graph change-notification subscription exists for the user's Spaarke todo list
/// (resource <c>/me/todo/lists/{listId}/tasks</c>). Idempotent: returns the existing
/// subscription id if active, otherwise creates a new subscription and returns its id.
/// </summary>
/// <remarks>
/// <para>Feature-gated by <c>Spaarke:Graph:TodoSync:Enabled</c>. When false, the Null-Object
/// implementation returns <see cref="string.Empty"/> per ADR-032 P2 quiet semantics — no subscription
/// is created and inbound webhooks are not received.</para>
/// <para>Real implementation arrives in Phase 7 (task 063). It will reuse the patterns established
/// by <c>GraphSubscriptionManager</c> (Communication module) but for the To Do tasks resource.</para>
/// </remarks>
public interface ITodoSubscriptionManager
{
    /// <summary>
    /// Returns the Graph subscription id for the user's Spaarke list, creating a new
    /// subscription if none is active.
    /// </summary>
    /// <param name="userId">Entra ID object id of the user.</param>
    /// <param name="listId">Graph todoList id (from <see cref="ISpaarkeListProvisioner"/>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Graph subscription id, or <see cref="string.Empty"/> when the feature flag is off.</returns>
    Task<string> EnsureSubscriptionAsync(Guid userId, string listId, CancellationToken ct);
}
