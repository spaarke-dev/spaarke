using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Config-driven <see cref="IActionResolver"/>: reads the ActionId map from
/// <see cref="LinearConsumersOptions"/> and delegates to
/// <see cref="IScopeResolverService.GetActionAsync"/> to load the row.
/// </summary>
/// <remarks>
/// Registered as Singleton per <see cref="Sprk.Bff.Api.Services.Ai.IScopeResolverService"/>
/// lifecycle; safe because it holds no mutable per-request state and
/// <see cref="IOptions{TOptions}"/> is itself Singleton.
/// </remarks>
public sealed class ActionResolver : IActionResolver
{
    private readonly IOptions<LinearConsumersOptions> _options;
    private readonly IScopeResolverService _scopeResolver;
    private readonly ILogger<ActionResolver> _logger;

    public ActionResolver(
        IOptions<LinearConsumersOptions> options,
        IScopeResolverService scopeResolver,
        ILogger<ActionResolver> logger)
    {
        _options = options;
        _scopeResolver = scopeResolver;
        _logger = logger;
    }

    public async Task<AnalysisAction> ResolveAsync(string consumerType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(consumerType))
        {
            throw new ArgumentException("consumerType is required", nameof(consumerType));
        }

        if (!_options.Value.TryGetActionId(consumerType, out var actionId))
        {
            throw new InvalidOperationException(
                $"Linear consumer '{consumerType}' has no ActionId configured. " +
                $"Add a LinearConsumers:ActionIds:{consumerType} entry to appsettings " +
                $"(or LinearConsumers__ActionIds__{consumerType.Replace('-', '_')} in App Service settings).");
        }

        var action = await _scopeResolver.GetActionAsync(actionId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Linear consumer '{consumerType}' ActionId {actionId} not found in Dataverse.");

        _logger.LogDebug(
            "Resolved linear consumer {ConsumerType} → Action {ActionId} ({ActionName})",
            consumerType, action.Id, action.Name);

        return action;
    }
}
