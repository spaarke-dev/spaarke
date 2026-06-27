namespace Sprk.Bff.Api.Services.Todo.Placeholder;

/// <summary>
/// Placeholder implementation of <see cref="ITodoSubscriptionManager"/> for the flag-on
/// branch. Throws until Phase 7 (task 063) lands the real Graph-backed impl.
/// </summary>
internal sealed class NotImplementedTodoSubscriptionManager : ITodoSubscriptionManager
{
    public Task<string> EnsureSubscriptionAsync(Guid userId, string listId, CancellationToken ct)
        => throw new NotImplementedException(
            "Real TodoSubscriptionManager will be added in Phase 7 (task 063). "
                + "Set Spaarke:Graph:TodoSync:Enabled=false to use the Null-Object path until then.");
}
