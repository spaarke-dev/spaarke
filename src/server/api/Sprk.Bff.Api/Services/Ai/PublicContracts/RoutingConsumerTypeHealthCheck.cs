using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Boot reconciliation gate for the two closed AI catalogs (ADR-039 /
/// FR-P0-04, <c>spaarke-ai-architecture-redesign-r1</c> task 005). Verifies:
/// </summary>
/// <remarks>
/// <list type="number">
///   <item>
///     <b>Constants ↔ Binding rows</b>: every <see cref="ConsumerTypes.All"/>
///     constant has at least one <c>sprk_playbookconsumer</c> row and every
///     distinct <c>sprk_consumertype</c> value maps back to a constant.
///   </item>
///   <item>
///     <b>Tool row ↔ handler bijection</b>: every active
///     <c>sprk_analysistool</c> row names (via <c>sprk_handlerclass</c>, the
///     documented routing identity per
///     <c>Services/Ai/Handlers/HandlerRegistrationConventions.md</c>) exactly
///     one handler registered in <see cref="IToolHandlerRegistry"/>, and every
///     registered handler has exactly one tool row. Orphans and duplicates on
///     either side are drift.
///   </item>
/// </list>
/// <para>
/// <b>History</b>: originally the Phase 1R suggestion S-5C startup WARN
/// (per the 2026-06-24 code review of chat-routing-redesign-r1 task 028a;
/// the UAT-2 <c>matter-pre-fil</c> Power Apps typo was the motivating
/// incident). FR-P0-04 upgrades drift from an operator WARN to a startup
/// health FAILURE: the class now also implements
/// <see cref="IHealthCheck"/> and is registered with the health-check
/// pipeline (see <c>Infrastructure/DI/RoutingModule.cs</c>) so catalog drift
/// reports <see cref="HealthStatus.Unhealthy"/> at <c>/healthz</c> — a deploy
/// gate rather than a first-failed-request surprise. This is what lets P1+
/// phases trust the closed catalogs blindly (ADR-039).
/// </para>
/// <para>
/// <b>Gating (never false-fail)</b>:
/// </para>
/// <list type="bullet">
///   <item>Compound AI gate OFF (<c>Analysis:Enabled=false</c> or
///     <c>DocumentIntelligence:Enabled=false</c>): reconciliation is skipped
///     and the check reports Healthy-with-description — a disabled subsystem
///     is not drift.</item>
///   <item><c>ToolFramework:Enabled=false</c> (or
///     <see cref="IToolHandlerRegistry"/> not resolvable): only the
///     tool↔handler dimension is skipped (handlers are not discovered on that
///     branch, so comparing rows against an empty registry would be a false
///     failure); the consumer-type dimension still runs.</item>
///   <item>Transient Dataverse error: <see cref="HealthStatus.Degraded"/>
///     from the health check and fail-soft (info log) from the hosted
///     service — availability blips are not catalog drift.</item>
/// </list>
/// <para>
/// <b>ADR-015 tier-1 safe</b>: the drift report contains only deterministic
/// catalog identifiers (consumer-type strings, tool display names,
/// namespaced tool ids, handler class names) — never GUIDs, user data, or
/// content.
/// </para>
/// </remarks>
public sealed class RoutingConsumerTypeHealthCheck : IHostedService, IHealthCheck
{
    private const string BindingEntityLogicalName = "sprk_playbookconsumer";
    private const string ToolEntityLogicalName = "sprk_analysistool";

    private const string AnalysisEnabledKey = "Analysis:Enabled";
    private const string DocumentIntelligenceEnabledKey = "DocumentIntelligence:Enabled";
    private const string ToolFrameworkEnabledKey = "ToolFramework:Enabled";

    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RoutingConsumerTypeHealthCheck> _logger;

    public RoutingConsumerTypeHealthCheck(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<RoutingConsumerTypeHealthCheck> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── IHostedService: startup log surface (fail-soft) ────────────────────

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!IsAiSubsystemEnabled())
            {
                _logger.LogInformation(
                    "RoutingConsumerTypeHealthCheck skipped: AI subsystem disabled (Analysis:Enabled=false or DocumentIntelligence:Enabled=false).");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var entityService = scope.ServiceProvider.GetService<IGenericEntityService>();
            if (entityService is null)
            {
                _logger.LogInformation(
                    "RoutingConsumerTypeHealthCheck skipped: IGenericEntityService not registered (likely a test host or feature-flagged-off environment).");
                return;
            }

            var report = await ReconcileAsync(scope.ServiceProvider, entityService, cancellationToken)
                .ConfigureAwait(false);

            if (report.HasDrift)
            {
                // FR-P0-04: drift is a startup FAILURE (surfaced as Unhealthy at
                // /healthz via CheckHealthAsync). The hosted-service run logs the
                // same report at Error so the drift is visible in startup logs
                // even before the first health probe.
                _logger.LogError(
                    "RoutingConsumerTypeHealthCheck FAILED: {DriftReport}",
                    report.BuildDriftDescription());
            }
            else if (report.HasOrphanHandlers)
            {
                _logger.LogWarning(
                    "RoutingConsumerTypeHealthCheck DEGRADED: {OrphanReport}",
                    report.BuildOrphanDescription());
            }
            else
            {
                _logger.LogInformation(
                    "RoutingConsumerTypeHealthCheck: {Summary}",
                    report.BuildHealthyDescription());
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown during startup — propagate.
            throw;
        }
        catch (Exception ex)
        {
            // Fail-soft: never block startup on a transient reconciliation error.
            // ADR-015: log identifiers + outcome only (the exception is fine —
            // it's a Dataverse / .NET diagnostic, not user content).
            _logger.LogInformation(
                ex,
                "RoutingConsumerTypeHealthCheck skipped due to transient error (Dataverse unreachable, MI propagation lag, or similar). The /healthz check reports Degraded until reconciliation can run.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ── IHealthCheck: the FR-P0-04 boot reconciliation gate ────────────────

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!IsAiSubsystemEnabled())
        {
            return HealthCheckResult.Healthy(
                "AI subsystem disabled (Analysis:Enabled=false or DocumentIntelligence:Enabled=false) — catalog reconciliation skipped; a disabled subsystem is not drift.");
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var entityService = scope.ServiceProvider.GetService<IGenericEntityService>();
            if (entityService is null)
            {
                return HealthCheckResult.Healthy(
                    "IGenericEntityService not registered (test host or feature-flagged-off environment) — catalog reconciliation skipped.");
            }

            var report = await ReconcileAsync(scope.ServiceProvider, entityService, cancellationToken)
                .ConfigureAwait(false);

            if (report.HasDrift)
            {
                return HealthCheckResult.Unhealthy(report.BuildDriftDescription());
            }

            return report.HasOrphanHandlers
                ? HealthCheckResult.Degraded(report.BuildOrphanDescription())
                : HealthCheckResult.Healthy(report.BuildHealthyDescription());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Transient Dataverse error is NOT catalog drift — degrade rather
            // than fail so an availability blip cannot be mistaken for a
            // closed-catalog violation (fail-soft precedent from S-5C).
            return HealthCheckResult.Degraded(
                "Catalog reconciliation could not run (Dataverse unreachable, MI propagation lag, or similar transient error). Drift status unknown until the next probe.",
                ex);
        }
    }

    // ── Reconciliation core (shared by both surfaces) ──────────────────────

    private bool IsAiSubsystemEnabled() =>
        _configuration.GetValue(AnalysisEnabledKey, true) &&
        _configuration.GetValue(DocumentIntelligenceEnabledKey, true);

    private async Task<CatalogReconciliationReport> ReconcileAsync(
        IServiceProvider scopedProvider,
        IGenericEntityService entityService,
        CancellationToken cancellationToken)
    {
        // ── (a) ConsumerTypes constants ↔ Binding rows ──────────────────────
        var bindingQuery = new QueryExpression(BindingEntityLogicalName)
        {
            ColumnSet = new ColumnSet("sprk_consumertype", "sprk_enabled"),
            NoLock = true,
        };

        var bindingResult = await entityService
            .RetrieveMultipleAsync(bindingQuery, cancellationToken)
            .ConfigureAwait(false);

        var dataverseTypes = (bindingResult?.Entities ?? Enumerable.Empty<Entity>())
            .Select(e => e.GetAttributeValue<string>("sprk_consumertype"))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var bffSet = new HashSet<string>(ConsumerTypes.All, StringComparer.OrdinalIgnoreCase);
        var dvSet = new HashSet<string>(dataverseTypes, StringComparer.OrdinalIgnoreCase);

        var rowsWithoutConstants = dvSet
            .Except(bffSet, StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var constantsWithoutRows = bffSet
            .Except(dvSet, StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ── (b) Tool rows ↔ registered handlers (bijection) ─────────────────
        string? toolBijectionSkippedReason = null;
        var toolRowsWithoutHandlers = new List<string>();
        var toolRowsMissingHandlerClass = new List<string>();
        var handlersWithoutToolRows = new List<string>();
        var duplicateToolRowsPerHandler = new List<string>();
        var toolRowCount = 0;
        var handlerCount = 0;

        if (!_configuration.GetValue(ToolFrameworkEnabledKey, true))
        {
            // Handlers are not auto-discovered on this branch — comparing rows
            // against an empty registry would be a false failure.
            toolBijectionSkippedReason = "ToolFramework:Enabled=false";
        }
        else
        {
            var registry = scopedProvider.GetService<IToolHandlerRegistry>();
            if (registry is null)
            {
                toolBijectionSkippedReason = "IToolHandlerRegistry not registered";
            }
            else
            {
                var toolQuery = new QueryExpression(ToolEntityLogicalName)
                {
                    ColumnSet = new ColumnSet("sprk_name", "sprk_handlerclass", "sprk_toolid"),
                    NoLock = true,
                    Criteria = new FilterExpression(LogicalOperator.And),
                };
                toolQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // Active rows only.

                var toolResult = await entityService
                    .RetrieveMultipleAsync(toolQuery, cancellationToken)
                    .ConfigureAwait(false);

                var toolRows = (toolResult?.Entities ?? Enumerable.Empty<Entity>()).ToList();
                toolRowCount = toolRows.Count;

                var registeredIds = registry.GetRegisteredHandlerIds();
                handlerCount = registeredIds.Count;
                var registeredSet = new HashSet<string>(registeredIds, StringComparer.OrdinalIgnoreCase);

                var rowCountByHandler = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var rowCountByToolId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in toolRows)
                {
                    var name = row.GetAttributeValue<string>("sprk_name");
                    var toolId = row.GetAttributeValue<string>("sprk_toolid");
                    var handlerClass = row.GetAttributeValue<string>("sprk_handlerclass")?.Trim();
                    var label = string.IsNullOrWhiteSpace(toolId)
                        ? name ?? "(unnamed tool row)"
                        : $"{name ?? "(unnamed tool row)"} [{toolId}]";

                    if (string.IsNullOrWhiteSpace(handlerClass))
                    {
                        // No routing identity — the row cannot participate in the
                        // bijection at all (the legacy GenericAnalysisHandler
                        // fallback is exactly the ambiguity ADR-039 closes).
                        toolRowsMissingHandlerClass.Add(label);
                        continue;
                    }

                    rowCountByHandler[handlerClass] =
                        rowCountByHandler.TryGetValue(handlerClass, out var n) ? n + 1 : 1;

                    // Tool identity is sprk_toolid (the 8-field contract). A handler
                    // MAY legitimately serve several named tool rows (row = tool,
                    // handler = implementation — e.g. TextRefinementHandler serves
                    // Summary/KeyPoints/Refinement). What must NOT happen is two
                    // active rows claiming the SAME tool id. Legacy rows with null
                    // toolid predate the contract and are excluded from this check
                    // (they gain ids at the FR-P2-07 re-namespacing).
                    if (!string.IsNullOrWhiteSpace(toolId))
                    {
                        var key = toolId.Trim();
                        rowCountByToolId[key] =
                            rowCountByToolId.TryGetValue(key, out var t) ? t + 1 : 1;
                    }

                    if (!registeredSet.Contains(handlerClass))
                    {
                        toolRowsWithoutHandlers.Add($"{label} (sprk_handlerclass={handlerClass})");
                    }
                }

                duplicateToolRowsPerHandler.AddRange(rowCountByToolId
                    .Where(kvp => kvp.Value > 1)
                    .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kvp => $"{kvp.Key} ({kvp.Value} rows)"));

                handlersWithoutToolRows.AddRange(registeredSet
                    .Where(id => !rowCountByHandler.ContainsKey(id))
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
            }
        }

        return new CatalogReconciliationReport(
            constantsWithoutRows,
            rowsWithoutConstants,
            toolRowsWithoutHandlers,
            toolRowsMissingHandlerClass,
            handlersWithoutToolRows,
            duplicateToolRowsPerHandler,
            dvSet.Count,
            toolRowCount,
            handlerCount,
            toolBijectionSkippedReason);
    }

    /// <summary>
    /// Result of one reconciliation pass. All lists carry deterministic
    /// catalog identifiers only (ADR-015 tier-1 safe).
    /// </summary>
    private sealed record CatalogReconciliationReport(
        IReadOnlyList<string> ConstantsWithoutRows,
        IReadOnlyList<string> RowsWithoutConstants,
        IReadOnlyList<string> ToolRowsWithoutHandlers,
        IReadOnlyList<string> ToolRowsMissingHandlerClass,
        IReadOnlyList<string> HandlersWithoutToolRows,
        IReadOnlyList<string> DuplicateToolRowsPerHandler,
        int ConsumerTypeCount,
        int ToolRowCount,
        int HandlerCount,
        string? ToolBijectionSkippedReason)
    {
        public bool HasDrift =>
            ConstantsWithoutRows.Count > 0 ||
            RowsWithoutConstants.Count > 0 ||
            ToolRowsWithoutHandlers.Count > 0 ||
            ToolRowsMissingHandlerClass.Count > 0 ||
            DuplicateToolRowsPerHandler.Count > 0;

        /// <summary>
        /// Registered handlers with no catalog row. DEGRADED (not Unhealthy)
        /// until the FR-P2-01 catalog-projection cutover (task 030): today
        /// several handlers are direct-wired to chat contexts without rows,
        /// and the F-1 deletion targets (audit 2026-07-05) must NOT be given
        /// rows just to satisfy a probe. Task 030 escalates this dimension to
        /// Unhealthy when the closed catalog becomes the ONLY projection.
        /// </summary>
        public bool HasOrphanHandlers => HandlersWithoutToolRows.Count > 0;

        public string BuildDriftDescription()
        {
            var parts = new List<string>();

            if (ConstantsWithoutRows.Count > 0)
            {
                parts.Add(
                    $"ConsumerTypes constants without Binding row (missing sprk_playbookconsumer seed — scripts/dataverse/Seed-PlaybookConsumers.ps1): {string.Join(", ", ConstantsWithoutRows)}");
            }

            if (RowsWithoutConstants.Count > 0)
            {
                parts.Add(
                    $"Binding rows without ConsumerTypes constant (probable admin typo or stale row): {string.Join(", ", RowsWithoutConstants)}");
            }

            if (ToolRowsMissingHandlerClass.Count > 0)
            {
                parts.Add(
                    $"tool rows missing sprk_handlerclass (no routing identity): {string.Join(", ", ToolRowsMissingHandlerClass)}");
            }

            if (ToolRowsWithoutHandlers.Count > 0)
            {
                parts.Add(
                    $"tool rows without a registered handler: {string.Join(", ", ToolRowsWithoutHandlers)}");
            }

            if (DuplicateToolRowsPerHandler.Count > 0)
            {
                parts.Add(
                    $"duplicate sprk_toolid across active tool rows (tool identity violated): {string.Join(", ", DuplicateToolRowsPerHandler)}");
            }

            return
                "AI catalog drift detected (ADR-039 / FR-P0-04 boot reconciliation): "
                + string.Join("; ", parts) + ".";
        }

        public string BuildOrphanDescription() =>
            $"Catalog rows and constants reconcile, but {HandlersWithoutToolRows.Count} registered handlers have no sprk_analysistool row (not catalog-invocable; escalates to Unhealthy at the FR-P2-01 cutover, task 030): {string.Join(", ", HandlersWithoutToolRows)}.";

        public string BuildHealthyDescription() =>
            ToolBijectionSkippedReason is null
                ? $"Catalog reconciliation healthy: {ConsumerTypeCount} consumer types match ConsumerTypes.All; {ToolRowCount} tool rows <-> {HandlerCount} registered handlers form a bijection."
                : $"Consumer types healthy ({ConsumerTypeCount} match ConsumerTypes.All); tool<->handler bijection skipped ({ToolBijectionSkippedReason}).";
    }
}
