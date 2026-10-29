// -----------------------------------------------------------------------------
// H7cCustomerOwnerTeamsHandler.cs
//
// L2 CONTROL-PLANE H7c — the customer owner teams (#2429; unified-access-control-r2 task 194b R4b-2/R4b-3b; field-mapping
// FR-39, design §13.3a). Owner direction (2026-10-29): whatever a customer setup requires is documented AND in the
// provisioning process — this was the manual deployment-guide step "Phase 7a".
//
// PURPOSE:
//   In the customer's TOP business unit (H10's InterStepState.CustomerBusinessUnitId, a direct child of the root and a
//   sibling of Secure Record), two memberless Owner teams, each holding the customer unit's copy of its own role:
//     - "Spaarke Restricted Content Owners" / "Restricted Content Owner" (config/restricted-content-owner-role.json): the
//       BFF owns ordinary hard-deny rows to it and refuses those writes while it is missing, ambiguous or has a member
//       (record_owner_restricted_content_team_unresolved);
//     - "Spaarke Customer Configuration Owners" / "Customer Configuration Owner" (config/customer-configuration-owner-role.json):
//       H12b's seeders own the customer configuration rows (field mapping, sprk_aisearchindex) to it (FR-39).
//   Dataverse refuses to assign a row to a principal whose roles lack Read on the table, so each role holds Read at Basic on
//   exactly its file's tables. Both team ids go to InterStepState (RestrictedContentOwnerTeamId,
//   CustomerConfigurationOwnerTeamId) on success only.
//
//   The steps live in CustomerOwnerTeamsProcedure, a port of scripts/Set-CustomerOwnerTeams.ps1 (the reference; the
//   script stays the tool for EXISTING stamps, -Apply / -Verify). This class owns what every handler owns: the run row,
//   its upstream guards, Level-3 idempotency, the §4C mapping and the Cosmos state write — modelled on H7b.
//
// DAG: [H7c] = { H7b } (H7b ← H6 ← H10: the package's tables, the Secure Record unit the procedure refuses as a customer
//   unit, and H10's customer unit). H9 waits for it (the BFF serves the customer only after the teams exist); H13
//   re-verifies both teams read-only (CustomerOwnerTeamsVerifier). TenancyModel-agnostic, as H7b (Model 1 and Model 2 run it).
//
// AUTH: H7b's — the BFF app registration via the FR-39 chain (EnvVarValuesOptions, WorkerDataverseCredentialFactory),
//   through H7b's seam ISecureRecordSetupDataverse. No new identity, option, app setting or secret.
//
// NO DRY RUN: H7b's dry run stops the run at H7b, so H7c (which follows it) never runs during one.
//
// IDEMPOTENCY: Level 3 key customer-owner-teams-{customerId}-{setHash}; setHash = SHA-256 over the procedure version and
//   both files' teams, roles and sorted privilege names. A second run in a NEW run row re-executes every step, which then
//   reads and writes nothing (pinned by tests).
//
// ROLLBACK CLASSIFICATION (§4C): Resumable everywhere except the owner-decision refusals listed in
//   CustomerOwnerTeamsRejectionCodes (QuarantineRequired). Every write is guarded by a read.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;
using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.CustomerOwnerTeams;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H7cCustomerOwnerTeamsHandler : IProvisioningHandler
{
    /// <summary>Handler identifier.</summary>
    public const string HandlerIdentifier = HandlerIds.H7c;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Verified gate: both customer owner teams are in the declared state.</summary>
    public const string SetupGateId = "h7c-customer-owner-teams";

    /// <summary>Version of the procedure, part of the idempotency hash.</summary>
    /// <remarks>1 = #2429: the port of scripts/Set-CustomerOwnerTeams.ps1 (UAC-r2 194b, 2cd0f62e2c).</remarks>
    internal const string ProcedureVersion = "customer-owner-teams-procedure=1";

    private readonly IProvisioningRunRepository _repository;
    private readonly ISecureRecordSetupDataverse _dataverse;
    private readonly EnvVarValuesOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<H7cCustomerOwnerTeamsHandler> _logger;
    private readonly Func<CustomerOwnerRoleSet> _roleSet;
    private readonly Func<SecureRecordOwnerRoleSet> _secureRoleSet;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>Constructs the handler (production: the embedded declared files).</summary>
    public H7cCustomerOwnerTeamsHandler(
        IProvisioningRunRepository repository,
        ISecureRecordSetupDataverse dataverse,
        IOptions<EnvVarValuesOptions> options,
        TimeProvider timeProvider,
        ILogger<H7cCustomerOwnerTeamsHandler> logger)
        : this(repository, dataverse, options, timeProvider, logger,
            () => CustomerOwnerRoleSet.Embedded, () => SecureRecordOwnerRoleSet.Embedded)
    {
    }

    /// <summary>Test seam: the declared roles and the Secure Record set come from the delegates.</summary>
    internal H7cCustomerOwnerTeamsHandler(
        IProvisioningRunRepository repository,
        ISecureRecordSetupDataverse dataverse,
        IOptions<EnvVarValuesOptions> options,
        TimeProvider timeProvider,
        ILogger<H7cCustomerOwnerTeamsHandler> logger,
        Func<CustomerOwnerRoleSet> roleSet,
        Func<SecureRecordOwnerRoleSet> secureRoleSet)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(roleSet);
        ArgumentNullException.ThrowIfNull(secureRoleSet);
        _repository = repository;
        _dataverse = dataverse;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _roleSet = roleSet;
        _secureRoleSet = secureRoleSet;
    }

    /// <inheritdoc/>
    public async Task<HandlerResult> HandleAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.CustomerId);
        if (!string.Equals(envelope.HandlerId, HandlerIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"H7cCustomerOwnerTeamsHandler invoked with mismatched HandlerId '{envelope.HandlerId}' (expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("H7c customer owner teams starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) The run row (§4D I3: the repository requires the partition key).
        var read = await _repository.ReadRunAsync(envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            return new HandlerResult.Failure(FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.RunNotFound,
                $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }
        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;
        var state = run.InterStepState;

        // (2) Upstream guards — each fires before any Dataverse call. Never a fallback to the root unit or the caller's unit.
        if (!parameters.TryGetValue(TenantIdParameterKey, out var tenantId) || string.IsNullOrWhiteSpace(tenantId))
        {
            return await FailMissingAsync(run, etag, "tenantId",
                "Run parameter 'tenantId' is required (§4D I1 — H7c never guesses the tenant).", cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(state.DataverseEnvUrl))
        {
            return await FailMissingAsync(run, etag, "dataverseEnvUrl",
                "InterStepState.dataverseEnvUrl is absent: H5 adopts the environment H7c configures.", cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(state.BffAppRegId))
        {
            return await FailMissingAsync(run, etag, "bffAppRegId",
                "InterStepState.bffAppRegId is absent: H3's BFF app registration is the identity H7c signs in as (H7b's).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!Guid.TryParse(state.CustomerBusinessUnitId, out var customerUnitId) || customerUnitId == Guid.Empty)
        {
            return await FailMissingAsync(run, etag, "customerBusinessUnitId",
                "InterStepState.customerBusinessUnitId (H10's customer business unit) must be a GUID: the owner teams live in " +
                "the customer's top unit, never the root.", cancellationToken).ConfigureAwait(false);
        }
        if (_options.Credentials.ClientSecretIsRequiredFirst(EnvVarValuesOptions.SectionName)
            && string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.MissingClientSecret,
                "EnvVarValuesOptions:ClientSecret is empty and the FR-39 chain's primary is ClientSecret. H7c signs in exactly " +
                "as H7b does; secret-free Workers configure EnvVarValues:Credentials:Order:0=ManagedIdentityFederated. Nothing " +
                "was called.", null, cancellationToken).ConfigureAwait(false);
        }

        CustomerOwnerRoleSet roleSet;
        string secureUnitName;
        try
        {
            roleSet = _roleSet();
            secureUnitName = _secureRoleSet().BusinessUnitName;
        }
        catch (InvalidOperationException ex)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.RoleSetInvalid,
                ex.Message, null, cancellationToken).ConfigureAwait(false);
        }

        // (3) Level-3 idempotency.
        var setHash = ComputeSetHash(roleSet);
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, setHash);
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation("H7c idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}", envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (4) The procedure.
        var request = new CustomerOwnerTeamsRequest(
            new SecureRecordSetupTarget(state.DataverseEnvUrl!, tenantId, state.BffAppRegId!),
            roleSet,
            secureUnitName,
            customerUnitId);
        CustomerOwnerTeamsOutcome outcome;
        try
        {
            outcome = await new CustomerOwnerTeamsProcedure(_dataverse, _logger).RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SecureRecordSetupDataverseException ex)
        {
            // FAIL CLOSED: a read that fails is a failure, never "absent".
            _logger.LogWarning(ex, "H7c Dataverse fault: runId={RunId} kind={Kind}", envelope.RunId, ex.Kind);
            var code = ex.Kind switch
            {
                SecureRecordSetupFaultKind.Auth => CustomerOwnerTeamsRejectionCodes.DataverseAuthFailure,
                SecureRecordSetupFaultKind.RateLimited => CustomerOwnerTeamsRejectionCodes.RateLimited,
                _ => CustomerOwnerTeamsRejectionCodes.DataverseInvocationFailed,
            };
            return await FailAsync(run, etag, FailureClass.Resumable, code,
                $"{ex.Message} Every step reads before it writes — resuming re-runs H7c safely.", null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "H7c infrastructure fault: runId={RunId}", envelope.RunId);
            return await FailAsync(run, etag, FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.DataverseInvocationFailed,
                $"Customer owner teams infrastructure error: {ex.GetType().Name}: {ex.Message}. Resuming re-runs H7c safely.",
                null, cancellationToken).ConfigureAwait(false);
        }

        switch (outcome)
        {
            case CustomerOwnerTeamsOutcome.Refused refused:
                return await FailAsync(run, etag, refused.Class, refused.RejectionCode, refused.Diagnostic,
                    new { setHash, writesBeforeRefusal = refused.Actions }, cancellationToken).ConfigureAwait(false);

            case CustomerOwnerTeamsOutcome.Applied applied:
                var restricted = applied.Teams.SingleOrDefault(x => x.File == roleSet.RestrictedContent.File);
                var configuration = applied.Teams.SingleOrDefault(x => x.File == roleSet.CustomerConfiguration.File);
                if (restricted is null || configuration is null)
                {
                    // Defence in depth: the procedure verified every declared team, so this is a defect, never a pass.
                    return await FailAsync(run, etag, FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.VerifyFailed,
                        "The verified state does not name both declared teams; nothing was recorded.", null, cancellationToken)
                        .ConfigureAwait(false);
                }
                stopwatch.Stop();
                _logger.LogInformation(
                    "H7c customer owner teams succeeded: runId={RunId} writes={Writes} durationMs={DurationMs}",
                    envelope.RunId, applied.Actions.Count, stopwatch.ElapsedMilliseconds);
                return await MarkCompleteAsync(run, etag, idempotencyKey, envelope, restricted, configuration, new
                {
                    setHash,
                    customerBusinessUnitId = customerUnitId,
                    teams = applied.Teams,
                    notes = applied.Notes,
                    writes = applied.Actions,
                }, cancellationToken).ConfigureAwait(false);

            default:
                throw new InvalidOperationException($"Unknown customer owner teams outcome {outcome.GetType().Name}.");
        }
    }

    /// <summary><c>customer-owner-teams-{customerId}-{setHash}</c>.</summary>
    internal static string BuildIdempotencyKey(string customerId, string setHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(setHash);
        return $"customer-owner-teams-{customerId}-{setHash}";
    }

    /// <summary>The set's hash, salted with <see cref="ProcedureVersion"/>.</summary>
    internal static string ComputeSetHash(CustomerOwnerRoleSet roleSet)
    {
        ArgumentNullException.ThrowIfNull(roleSet);
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(ProcedureVersion + "\n" + roleSet.SetHash()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private Task<HandlerResult> FailMissingAsync(ProvisioningRun run, string etag, string missing, string detail, CancellationToken ct)
        => FailAsync(run, etag, FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.MissingUpstreamState,
            $"Missing required upstream value '{missing}'. {detail}", null, ct);

    private async Task<HandlerResult> FailAsync(
        ProvisioningRun run,
        string etag,
        FailureClass failureClass,
        string rejectionCode,
        string diagnostic,
        object? evidence,
        CancellationToken cancellationToken)
    {
        run.Status = failureClass == FailureClass.QuarantineRequired ? RunStatus.Quarantined : RunStatus.Failed;
        run.CurrentPhase = HandlerIdentifier;
        run.ErrorDetail = $"[{rejectionCode}] {diagnostic}";
        if (failureClass == FailureClass.QuarantineRequired)
        {
            run.Quarantine = new QuarantineInfo
            {
                State = QuarantineState.Quarantined,
                Reason = diagnostic,
                QuarantinedByHandler = HandlerIdentifier,
                QuarantinedAt = _timeProvider.GetUtcNow(),
            };
        }

        run.GateStates[$"h7c-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
            Evidence = evidence is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(evidence)).RootElement.Clone(),
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning("H7c failure state write LOST optimistic-concurrency race: runId={RunId} winningStatus={Status}",
                run.RunId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning("H7c failure state write raced with row delete: runId={RunId}", run.RunId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        HandlerEnvelope envelope,
        CustomerOwnerTeamState restricted,
        CustomerOwnerTeamState configuration,
        object evidence,
        CancellationToken cancellationToken)
    {
        var completedAt = _timeProvider.GetUtcNow();
        run.MarkPhaseSucceeded(HandlerIdentifier);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = completedAt - TimeSpan.FromMilliseconds(1),
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        run.ErrorDetail = null;
        // #2429 (H7c): recorded only on a verified success — H12b binds configuration-row owners to the second (FR-39).
        run.InterStepState.RestrictedContentOwnerTeamId = restricted.TeamId.ToString("D");
        run.InterStepState.CustomerConfigurationOwnerTeamId = configuration.TeamId.ToString("D");
        run.GateStates[SetupGateId] = new GateEntry
        {
            Status = GateState.Verified,
            VerifierHandler = HandlerIdentifier,
            VerifiedAt = completedAt,
            Evidence = JsonDocument.Parse(JsonSerializer.Serialize(evidence)).RootElement.Clone(),
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning("H7c success state write LOST optimistic-concurrency race: runId={RunId} winningStatus={Status}",
                run.RunId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.ConcurrentWriteConflict,
                $"Concurrent write advanced run '{run.RunId}' between H7c read and write. Winning status: " +
                $"{conflict.Current.Run.Status}. Resume re-runs H7c, which then reads and writes nothing.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            return new HandlerResult.Failure(FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.RunDeletedDuringWrite,
                $"ProvisioningRun '{run.RunId}' was deleted while H7c was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
