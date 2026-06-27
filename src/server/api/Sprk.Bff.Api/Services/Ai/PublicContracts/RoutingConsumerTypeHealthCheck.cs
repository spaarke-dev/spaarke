using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Startup health check that compares the set of consumer-type strings on
/// the BFF side (<see cref="ConsumerTypes.All"/>) against the distinct
/// <c>sprk_consumertype</c> values present in Dataverse, and emits an
/// operator WARN for any mismatches.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1R suggestion S-5C</b> (per 2026-06-24 code review of task 028a).
/// The 2026-06-24 UAT-2 root cause was a Power Apps form typo
/// (<c>matter-pre-fil</c> instead of <c>matter-pre-fill</c>) that bypassed
/// the BFF entirely — there's no compile-time check on the Dataverse side
/// of the contract. This health check catches that class at DEPLOY time, not
/// at first failed request:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dataverse type NOT in <see cref="ConsumerTypes.All"/></b>: probable
///     admin typo (or a stale row from a removed consumer). Emit WARN.
///   </item>
///   <item>
///     <b>BFF constant NOT in Dataverse</b>: missing routing record. Emit WARN.
///     This is the more important signal for a new environment seed gap.
///   </item>
/// </list>
/// <para>
/// <b>ADR-015 tier-1 safe</b>: log only the consumer-type STRINGS (already
/// deterministic identifiers); never log GUID values, user data, or any
/// content.
/// </para>
/// <para>
/// <b>Fail-soft</b>: when Dataverse is unavailable at startup (transient
/// network blip, MI not yet propagated), the check logs a single info-level
/// message and silently returns. It is NOT a startup blocker — Phase 1R
/// routing has graceful-degrade on Dataverse errors via task 028a's
/// catch-and-return-null behaviour.
/// </para>
/// </remarks>
public sealed class RoutingConsumerTypeHealthCheck : IHostedService
{
    private const string EntityLogicalName = "sprk_playbookconsumer";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RoutingConsumerTypeHealthCheck> _logger;

    public RoutingConsumerTypeHealthCheck(
        IServiceProvider serviceProvider,
        ILogger<RoutingConsumerTypeHealthCheck> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var entityService = scope.ServiceProvider.GetService<IGenericEntityService>();
            if (entityService is null)
            {
                _logger.LogInformation(
                    "RoutingConsumerTypeHealthCheck skipped: IGenericEntityService not registered (likely a test host or feature-flagged-off environment).");
                return;
            }

            var query = new QueryExpression(EntityLogicalName)
            {
                ColumnSet = new ColumnSet("sprk_consumertype", "sprk_enabled"),
                NoLock = true,
            };

            var result = await entityService
                .RetrieveMultipleAsync(query, cancellationToken)
                .ConfigureAwait(false);

            if (result?.Entities is null || result.Entities.Count == 0)
            {
                _logger.LogWarning(
                    "RoutingConsumerTypeHealthCheck: sprk_playbookconsumer table is empty. Seed via scripts/dataverse/Seed-PlaybookConsumers.ps1 (FR-1R-07) or via Power Apps.");
                return;
            }

            var dataverseTypes = result.Entities
                .Select(e => e.GetAttributeValue<string>("sprk_consumertype"))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var bffSet = new HashSet<string>(ConsumerTypes.All, StringComparer.OrdinalIgnoreCase);
            var dvSet = new HashSet<string>(dataverseTypes, StringComparer.OrdinalIgnoreCase);

            var onlyInDataverse = dvSet.Except(bffSet, StringComparer.OrdinalIgnoreCase).ToList();
            var onlyInBff = bffSet.Except(dvSet, StringComparer.OrdinalIgnoreCase).ToList();

            if (onlyInDataverse.Count == 0 && onlyInBff.Count == 0)
            {
                _logger.LogInformation(
                    "RoutingConsumerTypeHealthCheck: {ConsumerTypeCount} consumer types match between Dataverse and ConsumerTypes.All. Routing surface healthy.",
                    dvSet.Count);
                return;
            }

            if (onlyInDataverse.Count > 0)
            {
                _logger.LogWarning(
                    "RoutingConsumerTypeHealthCheck: Dataverse has consumer types NOT in ConsumerTypes.All (probable admin typo or stale row): {Types}",
                    string.Join(", ", onlyInDataverse));
            }

            if (onlyInBff.Count > 0)
            {
                _logger.LogWarning(
                    "RoutingConsumerTypeHealthCheck: ConsumerTypes.All has types NOT in Dataverse (missing routing record): {Types}",
                    string.Join(", ", onlyInBff));
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown during startup — propagate.
            throw;
        }
        catch (Exception ex)
        {
            // Fail-soft: never block startup on a transient health check.
            // ADR-015: log identifiers + outcome only (the exception is fine —
            // it's a Dataverse / .NET diagnostic, not user content).
            _logger.LogInformation(
                ex,
                "RoutingConsumerTypeHealthCheck skipped due to transient error (Dataverse unreachable, MI propagation lag, or similar). Routing continues normally — this is a deploy-time diagnostic, not a runtime dependency.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
