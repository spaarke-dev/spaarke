// -----------------------------------------------------------------------------
// NullDataverseEnvironmentRegistryClient.cs
//
// L2 CONTROL-PLANE ADR-032 P2 (quiet no-op) placeholder impl of
// IDataverseEnvironmentRegistryClient. Original scaffold: task 042 H0.5 seam.
// Extended (task 112) to satisfy the new UpdateSetupStatusAsync WRITE-path
// contract added by the C1.4 registry client. Stays as the kill-switch
// fallback per POML constraint — this task ADDS the real
// <see cref="DataverseEnvironmentRegistryClient"/>; it does NOT delete this
// Null-Object.
//
// SEMANTICS:
//   LookupByTenantIdAsync — returns null (H0.5 treats null as "no existing
//     environment; first consent; fresh H0 enqueue"). SAFE default: a stale
//     null results in a fresh H0 enqueue whose Service Bus MessageId dedup
//     (level-1 idempotency) drops duplicates within the dedup window.
//
//   UpdateSetupStatusAsync — returns Success WITHOUT issuing any Dataverse
//     write. Unlike the READ path (where null-object safely returns null),
//     the H13 caller path treats Success as "the registry write landed" — so
//     a Null-Object active in production would silently NOT persist the
//     acceptance-gate transition. WARN log fires on every call so operators
//     see the "no real registry writer wired" state before task 122/184
//     land the DI swap. This is INTENTIONAL for the C4 scaffold window: the
//     H13 handler must exercise its full green-path unit-test surface, and a
//     null-registered updater would break the DI graph. Real impl swaps via
//     DI registration change only (task 122 for H0.5 read; task 184 for H13
//     write) — handlers + tests unchanged.
//
// LOGGING:
//   Every method emits a single Warning-level log per invocation so scaffold
//   environments surface the "no real registry client" state before the real
//   impl swap. Suppressed to Debug once the real impl ships (bicep app-setting
//   `Registry:UseNullFallback = false`, i.e. real client is registered).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Registry;

/// <summary>
/// Placeholder <see cref="IDataverseEnvironmentRegistryClient"/> that returns
/// null for reads and Success (with WARN log) for writes. Kill-switch fallback
/// per ADR-032 P2. Real impl: <see cref="DataverseEnvironmentRegistryClient"/>
/// (task 112) swaps via DI change (tasks 122 + 184).
/// </summary>
public sealed class NullDataverseEnvironmentRegistryClient : IDataverseEnvironmentRegistryClient
{
    private readonly ILogger<NullDataverseEnvironmentRegistryClient> _logger;

    /// <summary>
    /// Initializes the placeholder. Logger emits a single Warning per call
    /// so scaffold environments surface the "no real registry yet" state.
    /// </summary>
    public NullDataverseEnvironmentRegistryClient(
        ILogger<NullDataverseEnvironmentRegistryClient> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<DataverseEnvironmentRegistrySnapshot?> LookupByTenantIdAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        _logger.LogWarning(
            "NullDataverseEnvironmentRegistryClient in use — LookupByTenantIdAsync returning null " +
            "for tenantId={TenantId}. Task 122 must swap this Null-Object for the real " +
            "DataverseEnvironmentRegistryClient before H0.5 can honor the re-consent no-op contract " +
            "for existing environments.",
            tenantId);

        return Task.FromResult<DataverseEnvironmentRegistrySnapshot?>(null);
    }

    /// <inheritdoc/>
    public Task<RegistryUpdateOutcome> UpdateSetupStatusAsync(
        RegistrySetupStatusUpdate update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        _logger.LogWarning(
            "NullDataverseEnvironmentRegistryClient in use — UpdateSetupStatusAsync returning Success " +
            "WITHOUT issuing a real Dataverse PATCH. environmentId={EnvironmentId} setupStatus={SetupStatus} " +
            "clearCurrentRunId={ClearCurrentRunId} customerId={CustomerId} runId={RunId}. " +
            "Task 184 must swap this Null-Object for the real DataverseEnvironmentRegistryClient " +
            "before H13's Ready-writer transition can persist to sprk_dataverseenvironment.sprk_setupstatus.",
            update.EnvironmentId, update.SetupStatus, update.ClearCurrentRunId,
            update.CustomerIdForLog, update.RunIdForLog);

        return Task.FromResult<RegistryUpdateOutcome>(new RegistryUpdateOutcome.Success());
    }

    /// <inheritdoc/>
    public Task<RegistryUpdateOutcome> UpdateCredentialModeAsync(
        RegistryCredentialModeUpdate update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        // ADR-032 P2 quiet no-op + WARN — parity with UpdateSetupStatusAsync
        // above. Row A38a: a Null-Object active while an environment is
        // secret-free means the sprk_credentialmode marker is NOT persisted;
        // the WARN makes that visible before the real client swap.
        _logger.LogWarning(
            "NullDataverseEnvironmentRegistryClient in use — UpdateCredentialModeAsync returning Success " +
            "WITHOUT issuing a real Dataverse PATCH. environmentId={EnvironmentId} credentialMode={CredentialMode} " +
            "customerId={CustomerId} runId={RunId}. The A38a sprk_credentialmode marker is NOT persisted " +
            "until the real DataverseEnvironmentRegistryClient is the active registration.",
            update.EnvironmentId, update.CredentialMode, update.CustomerIdForLog, update.RunIdForLog);

        return Task.FromResult<RegistryUpdateOutcome>(new RegistryUpdateOutcome.Success());
    }

    /// <inheritdoc/>
    public Task<DataverseEnvironmentRegistrySnapshot?> LookupByEnvironmentIdAsync(
        string environmentId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);

        _logger.LogWarning(
            "NullDataverseEnvironmentRegistryClient in use — LookupByEnvironmentIdAsync returning null " +
            "for environmentId={EnvironmentId}. REG-07 (2026-08-27): callers use this to sanity-check " +
            "CreateRun-supplied environmentId against the registry; a null response short-circuits the check " +
            "and lets the run proceed on trust — swap to the real DataverseEnvironmentRegistryClient in production.",
            environmentId);

        return Task.FromResult<DataverseEnvironmentRegistrySnapshot?>(null);
    }

    /// <inheritdoc/>
    public Task<RegistryUpdateOutcome> UpdateColumnsAsync(
        string environmentId,
        IReadOnlyDictionary<string, object?> columns,
        string customerIdForLog,
        string runIdForLog,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentNullException.ThrowIfNull(columns);

        // ADR-032 P2 quiet no-op + WARN — parity with UpdateSetupStatusAsync +
        // UpdateCredentialModeAsync. REG-01 (2026-08-27): a Null-Object active
        // while H13 tries to promote columns means sprk_provisionedon /
        // sprk_bffversion / etc. are NOT persisted. WARN makes that visible so
        // operators can wire the real client before H13 fires in production.
        _logger.LogWarning(
            "NullDataverseEnvironmentRegistryClient in use — UpdateColumnsAsync returning Success " +
            "WITHOUT issuing a real Dataverse PATCH. environmentId={EnvironmentId} columnCount={ColumnCount} " +
            "columnNames={ColumnNames} customerId={CustomerId} runId={RunId}. The REG-01 promoted-columns " +
            "are NOT persisted until the real DataverseEnvironmentRegistryClient is the active registration.",
            environmentId, columns.Count, string.Join(",", columns.Keys), customerIdForLog, runIdForLog);

        return Task.FromResult<RegistryUpdateOutcome>(new RegistryUpdateOutcome.Success());
    }
}
