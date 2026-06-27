namespace Sprk.Bff.Api.Services.Todo.Placeholder;

/// <summary>
/// Placeholder implementation of <see cref="ITodoSyncBackfiller"/> for the flag-on
/// branch. Throws until Phase 7 (task 065) lands the real Graph-backed impl.
/// </summary>
internal sealed class NotImplementedTodoSyncBackfiller : ITodoSyncBackfiller
{
    public Task BackfillAsync(Guid userId, CancellationToken ct)
        => throw new NotImplementedException(
            "Real TodoSyncBackfiller will be added in Phase 7 (task 065). "
                + "Set Spaarke:Graph:TodoSync:Enabled=false to use the Null-Object path until then.");
}
