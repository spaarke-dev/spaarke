using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Validates <see cref="WorkspaceOptions"/> at startup and emits a deprecation
/// warning when any of the legacy <c>Workspace__*PlaybookId</c> environment
/// variables is non-null.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1R FR-1R-06</b>: With consumers migrated to
/// <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.IConsumerRoutingService"/>
/// (tasks 028c + 028d), the <c>Workspace__*PlaybookId</c> environment variables
/// are no longer the routing source of truth — <c>sprk_playbookconsumer</c>
/// Dataverse records are. This validator is the operator-facing early-warning
/// system: if any of the 6 legacy env vars is still set on a deployed
/// environment, a single startup WARN surfaces the deprecation so the operator
/// can clean them up.
/// </para>
/// <para>
/// <b>ADR-015 tier-1 binding</b>: the WARN log includes ONLY the env-var key
/// NAMES that are populated — NEVER the GUID value. The 2026-06-24 UAT-2
/// failure (env var set under a legacy key) motivated this whole phase; the
/// telemetry hygiene here makes sure we don't replace one log-leak class with
/// another.
/// </para>
/// <para>
/// <b>Deprecation, not failure</b>: <see cref="Validate"/> always returns
/// <see cref="ValidateOptionsResult.Success"/>. Env-var presence does not
/// fail startup — the consumers in 028c/028d retain the env-var values as
/// graceful-degrade fallbacks during the deprecation window. The validator's
/// only side effect is the WARN log.
/// </para>
/// </remarks>
public class WorkspaceOptionsValidator : IValidateOptions<WorkspaceOptions>
{
    /// <summary>
    /// Configuration key names of the 6 deprecated playbook-id env vars,
    /// keyed by the <see cref="WorkspaceOptions"/> property name they bind from.
    /// Centralised so the WARN log and any future runtime fallback telemetry
    /// emit the SAME key strings without drift.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> DeprecatedKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(WorkspaceOptions.PreFillPlaybookId)] = "Workspace__PreFillPlaybookId",
            [nameof(WorkspaceOptions.MatterPreFillPlaybookId)] = "Workspace__MatterPreFillPlaybookId",
            [nameof(WorkspaceOptions.ProjectPreFillPlaybookId)] = "Workspace__ProjectPreFillPlaybookId",
            [nameof(WorkspaceOptions.AiSummaryPlaybookId)] = "Workspace__AiSummaryPlaybookId",
            [nameof(WorkspaceOptions.SummarizePlaybookId)] = "Workspace__SummarizePlaybookId",
            [nameof(WorkspaceOptions.ChatSummarizePlaybookId)] = "Workspace__ChatSummarizePlaybookId",
        };

    private readonly ILogger<WorkspaceOptionsValidator> _logger;

    public WorkspaceOptionsValidator(ILogger<WorkspaceOptionsValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValidateOptionsResult Validate(string? name, WorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var populatedKeys = new List<string>(DeprecatedKeys.Count);

        if (!string.IsNullOrWhiteSpace(options.PreFillPlaybookId))
            populatedKeys.Add(DeprecatedKeys[nameof(WorkspaceOptions.PreFillPlaybookId)]);

        if (!string.IsNullOrWhiteSpace(options.MatterPreFillPlaybookId))
            populatedKeys.Add(DeprecatedKeys[nameof(WorkspaceOptions.MatterPreFillPlaybookId)]);

        if (!string.IsNullOrWhiteSpace(options.ProjectPreFillPlaybookId))
            populatedKeys.Add(DeprecatedKeys[nameof(WorkspaceOptions.ProjectPreFillPlaybookId)]);

        if (!string.IsNullOrWhiteSpace(options.AiSummaryPlaybookId))
            populatedKeys.Add(DeprecatedKeys[nameof(WorkspaceOptions.AiSummaryPlaybookId)]);

        if (!string.IsNullOrWhiteSpace(options.SummarizePlaybookId))
            populatedKeys.Add(DeprecatedKeys[nameof(WorkspaceOptions.SummarizePlaybookId)]);

        if (!string.IsNullOrWhiteSpace(options.ChatSummarizePlaybookId))
            populatedKeys.Add(DeprecatedKeys[nameof(WorkspaceOptions.ChatSummarizePlaybookId)]);

        if (populatedKeys.Count > 0)
        {
            // ADR-015 tier-1 safe: log ONLY the key names (already deterministic
            // identifiers); NEVER the GUID values.
            _logger.LogWarning(
                "Workspace__*PlaybookId env vars are deprecated; configure via sprk_playbookconsumer Dataverse table. Populated keys: {DeprecatedKeys}",
                string.Join(", ", populatedKeys));
        }

        // Deprecation does NOT fail startup. Consumers retain env-var fallback
        // per FR-1R-06 deprecation window; operator clean-up is signalled by
        // the log, not by a hard error.
        return ValidateOptionsResult.Success;
    }
}
