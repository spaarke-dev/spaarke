using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Routing-table-driven <see cref="IActionResolver"/>: resolves consumer-type
/// to an Action GUID via <see cref="IConsumerRoutingService.ResolveActionAsync"/>
/// (reads <c>sprk_playbookconsumer.sprk_action</c> lookup), then loads the row
/// via <see cref="IScopeResolverService.GetActionAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// R7 Wave 12.3 (2026-07-02) — replaces the earlier
/// <see cref="LinearConsumersOptions.ActionIds"/> config-map lookup. The routing
/// table is the single canonical routing surface for every consumer (Linear +
/// legacy playbook); config-based routing is retired to eliminate per-env drift,
/// silent fallback on missing App Settings, and dual routing surfaces.
/// </para>
/// <para>
/// <b>Adding a new Linear consumer</b>: seed one <c>sprk_playbookconsumer</c>
/// row with <c>sprk_consumertype = &lt;yourType&gt;</c>, <c>sprk_action</c>
/// pointing at the target Action row, and <c>sprk_enabled = true</c>. No code
/// change or App Settings change is required — routing takes effect within the
/// 5-minute cache TTL.
/// </para>
/// </remarks>
public sealed class ActionResolver : IActionResolver
{
    private readonly IConsumerRoutingService _consumerRouting;
    private readonly IScopeResolverService _scopeResolver;
    private readonly ILogger<ActionResolver> _logger;

    public ActionResolver(
        IConsumerRoutingService consumerRouting,
        IScopeResolverService scopeResolver,
        ILogger<ActionResolver> logger)
    {
        _consumerRouting = consumerRouting;
        _scopeResolver = scopeResolver;
        _logger = logger;
    }

    public async Task<AnalysisAction> ResolveAsync(string consumerType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(consumerType))
        {
            throw new ArgumentException("consumerType is required", nameof(consumerType));
        }

        var actionId = await _consumerRouting
            .ResolveActionAsync(consumerType, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (actionId is null || actionId.Value == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Linear consumer '{consumerType}' has no Action routed. " +
                $"Populate sprk_action on the sprk_playbookconsumer row where " +
                $"sprk_consumertype='{consumerType}' AND sprk_enabled=true.");
        }

        var action = await _scopeResolver.GetActionAsync(actionId.Value, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Linear consumer '{consumerType}' Action {actionId.Value} not found in Dataverse.");

        _logger.LogDebug(
            "Resolved linear consumer {ConsumerType} → Action {ActionId} ({ActionName}) via routing table",
            consumerType, action.Id, action.Name);

        return action;
    }
}
