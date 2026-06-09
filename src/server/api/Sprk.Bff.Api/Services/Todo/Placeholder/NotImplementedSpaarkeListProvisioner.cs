namespace Sprk.Bff.Api.Services.Todo.Placeholder;

/// <summary>
/// Placeholder implementation of <see cref="ISpaarkeListProvisioner"/> for the flag-on
/// branch. Throws until Phase 7 (task 062) lands the real Graph-backed impl.
/// </summary>
internal sealed class NotImplementedSpaarkeListProvisioner : ISpaarkeListProvisioner
{
    public Task<string> EnsureListAsync(Guid userId, CancellationToken ct)
        => throw new NotImplementedException(
            "Real SpaarkeListProvisioner will be added in Phase 7 (task 062). "
                + "Set Spaarke:Graph:TodoSync:Enabled=false to use the Null-Object path until then.");
}
