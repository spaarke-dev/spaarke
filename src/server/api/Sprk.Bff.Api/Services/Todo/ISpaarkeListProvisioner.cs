namespace Sprk.Bff.Api.Services.Todo;

/// <summary>
/// Ensures a per-user "Spaarke" todo list exists in the user's Microsoft To Do account
/// (Graph <c>/me/todo/lists</c>). Idempotent: returns the existing list id if already provisioned,
/// otherwise creates it and returns the new id.
/// </summary>
/// <remarks>
/// <para>Feature-gated by <c>Spaarke:Graph:TodoSync:Enabled</c>. When false, the Null-Object
/// implementation returns <see cref="string.Empty"/> per ADR-032 P2 quiet semantics — callers
/// MUST check the result before using it for downstream Graph calls.</para>
/// <para>Real implementation arrives in Phase 7 (task 062).</para>
/// </remarks>
public interface ISpaarkeListProvisioner
{
    /// <summary>
    /// Returns the Graph todoList id of the Spaarke list for the given user, creating
    /// the list if it does not exist.
    /// </summary>
    /// <param name="userId">Entra ID object id of the user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Graph todoList id, or <see cref="string.Empty"/> when the feature flag is off.</returns>
    Task<string> EnsureListAsync(Guid userId, CancellationToken ct);
}
