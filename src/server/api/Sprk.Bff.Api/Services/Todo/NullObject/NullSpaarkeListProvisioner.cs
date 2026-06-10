namespace Sprk.Bff.Api.Services.Todo.NullObject;

/// <summary>
/// Null-Object implementation of <see cref="ISpaarkeListProvisioner"/> bound when
/// <c>Spaarke:Graph:TodoSync:Enabled = false</c>. Returns <see cref="string.Empty"/> per
/// ADR-032 P2 (quiet, fire-and-forget). Callers MUST check the result before passing to
/// downstream Graph calls.
/// </summary>
internal sealed class NullSpaarkeListProvisioner : ISpaarkeListProvisioner
{
    public Task<string> EnsureListAsync(Guid userId, CancellationToken ct)
        => Task.FromResult(string.Empty);
}
