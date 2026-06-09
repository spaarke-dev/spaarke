namespace Sprk.Bff.Api.Services.Todo.NullObject;

/// <summary>
/// Null-Object implementation of <see cref="ITodoSubscriptionManager"/> bound when
/// <c>Spaarke:Graph:TodoSync:Enabled = false</c>. Returns <see cref="string.Empty"/> per
/// ADR-032 P2 (quiet, fire-and-forget) — no subscription is created and inbound webhooks
/// are not received.
/// </summary>
internal sealed class NullTodoSubscriptionManager : ITodoSubscriptionManager
{
    public Task<string> EnsureSubscriptionAsync(Guid userId, string listId, CancellationToken ct)
        => Task.FromResult(string.Empty);
}
