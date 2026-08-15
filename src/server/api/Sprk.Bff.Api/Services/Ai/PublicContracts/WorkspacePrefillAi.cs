using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IWorkspacePrefillAi"/>: a thin wrapper around
/// <see cref="IPlaybookOrchestrationService.ExecuteAsync"/> (playbook path) and the Linear AI
/// Consumer primitives <see cref="IActionResolver"/> / <see cref="IActionRunner"/> (the
/// <see cref="RunPrefillActionAsync"/> linear path). This facade is the sanctioned
/// <c>PublicContracts</c> boundary (ADR-013 / BFF §10 bullet 3): it may legally inject the
/// AI-internal primitives so workspace CRUD consumers never do.
/// </summary>
public sealed class WorkspacePrefillAi : IWorkspacePrefillAi
{
    private readonly IPlaybookOrchestrationService _orchestrator;
    private readonly IActionResolver _actionResolver;
    private readonly IActionRunner _actionRunner;

    public WorkspacePrefillAi(
        IPlaybookOrchestrationService orchestrator,
        IActionResolver actionResolver,
        IActionRunner actionRunner)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _actionResolver = actionResolver ?? throw new ArgumentNullException(nameof(actionResolver));
        _actionRunner = actionRunner ?? throw new ArgumentNullException(nameof(actionRunner));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<PlaybookStreamEvent> ExecutePlaybookAsync(
        PlaybookRunRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(httpContext);

        return _orchestrator.ExecuteAsync(request, httpContext, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<JsonElement> RunPrefillActionAsync(
        string consumerType,
        string extractedText,
        string fileName,
        string? tenantId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        // Resolve + run the linear Action — identical behavior to the former inline
        // IActionResolver.ResolveAsync + IActionRunner.RunAsync pair in the pre-fill services.
        var action = await _actionResolver.ResolveAsync(consumerType, cancellationToken);

        var docText = new DocumentText
        {
            DocumentId = null,
            FileName = fileName,
            ExtractedText = extractedText,
        };
        var runContext = new LinearRunContext
        {
            ConsumerType = consumerType,
            CorrelationId = correlationId,
            TenantId = tenantId,
        };

        return await _actionRunner.RunAsync(action, docText, runContext, cancellationToken);
    }
}
