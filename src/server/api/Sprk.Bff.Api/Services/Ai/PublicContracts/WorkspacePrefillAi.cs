using Microsoft.AspNetCore.Http;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IWorkspacePrefillAi"/>: a thin wrapper around
/// <see cref="IPlaybookOrchestrationService.ExecuteAsync"/>.
/// </summary>
public sealed class WorkspacePrefillAi : IWorkspacePrefillAi
{
    private readonly IPlaybookOrchestrationService _orchestrator;

    public WorkspacePrefillAi(IPlaybookOrchestrationService orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<PlaybookStreamEvent> ExecutePreFillPlaybookAsync(
        PlaybookRunRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(httpContext);

        return _orchestrator.ExecuteAsync(request, httpContext, cancellationToken);
    }
}
