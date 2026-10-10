// -----------------------------------------------------------------------------
// H7bSecureRecordSetupHandler.cs
//
// L2 CONTROL-PLANE H7b — the per-environment Secure Record setup (T256; unified-access-control-r2 INCOMING-145,
// owner F10 = a: a control-plane handler step, not a hand-edited runbook).
//
// PURPOSE:
//   Every environment that may hold a secure project, matter or work assignment needs things NO solution import can
//   create (INCOMING-145 §1): the "Secure Record" business unit (no users, ever), its named memberless Owner team
//   "Secure Record Owners", the "Secure Record Owner" role created INSIDE that unit (a child-unit role cannot be
//   packaged — T218e — and its containment is the point, setup guide §5.2) holding exactly the Read-at-Basic set of
//   config/secure-record-owner-role.json, that role on the named team alone, the BFF-managed field-security
//   memberships (task 150), and the contact identity-binding field-security memberships (INCOMING-141 / task 141,
//   T255: "Spaarke Identity Link Readers" ← every default team, "Spaarke Identity Link Writers" ← the BFF's application
//   users only). Without them every BFF secure path refuses (sdap.provision.secure_owner_team_not_found), and the BFF
//   can neither read nor write a contact binding (the field-secured columns are hidden from it), so contact-bound
//   sign-ins (CIAM, Type-2) fail.
//   It also checks the environment can serve the BFF at all: sprk_noaccessentry (#1364) must be readable, or the BFF's
//   fail-closed deny-list reader denies every read.
//
//   The steps live in SecureRecordSetupProcedure (read-then-write, refusals before any write, verify at the end). This
//   class owns what every handler owns: the run row, its upstream guards, Level-3 idempotency, the dry run, the
//   §4C mapping and the Cosmos state write.
//
// CUSTOMER UNIT (T259, ISS-010 / owner 2026-10-09): H7b also checks INCOMING-145 §6 T1 (the customer's unit, H10's
//   InterStepState.CustomerBusinessUnitId, is a direct child of the root) and T3 (both BFF application users are in it),
//   refusing QuarantineRequired otherwise; S2 already refuses any user in the Secure Record unit.
//
// DAG: [H7b] = { H6 } (it reads the package's tables' metadata). H9 waits for it, so no BFF is deployed to an
// environment without sprk_noaccessentry or with NULL secure flags; H13 waits for it (INCOMING-145 §2).
//
// AUTH: the BFF app registration via the FR-39 chain — the identity H6/H7 use (no new secret, ADR-028 A4). The
//   options are H7's EnvVarValuesOptions (one identity, one configuration section).
//
// DRY RUN (intake secureRecordSetupDryRun = "true"): every read, zero writes, the plan in gate h7b-secure-setup-plan,
//   and the run stops here (Resumable secure_setup.dry_run) so nothing later runs against an environment the dry run
//   left unconfigured. To apply, start a run without the flag (every handler before H7b is idempotent).
//
// IDEMPOTENCY: Level 3 key secure-setup-{customerId}-{setHash}; setHash = SHA-256 over the procedure version and the
//   file's sorted privilege names (INCOMING-145 §2.3) — extending the codified set or changing the procedure re-applies.
//   A second run in a NEW run row re-executes every step, which then reads and writes nothing (pinned by tests).
//
// ROLLBACK CLASSIFICATION (§4C): Resumable everywhere except the owner-decision refusals listed in
//   SecureRecordSetupRejectionCodes (QuarantineRequired). No RetryableWithCleanup path: every write is guarded by a read,
//   so re-running after a mid-way failure resumes where it stopped.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H7bSecureRecordSetupHandler : IProvisioningHandler
{
    /// <summary>Handler identifier.</summary>
    public const string HandlerIdentifier = HandlerIds.H7b;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Verified gate: the environment is in the codified Secure Record state.</summary>
    public const string SetupGateId = "h7b-secure-record-setup";

    /// <summary>Pending gate carrying a dry run's plan.</summary>
    public const string PlanGateId = "h7b-secure-setup-plan";

    /// <summary>
    /// Version of the procedure, part of the idempotency hash: a run that completed an older procedure does not
    /// short-circuit past a step added since.
    /// </summary>
    /// <remarks>
    /// 2 = T255: S15–S18, the contact identity-binding profiles' memberships (INCOMING-141).
    /// 3 = T259: §6 T1/T3 — the customer's business unit is a direct child of the root and holds both BFF application users.
    /// 4 = ISS-020 / #1565: S19–S21, the standing-grant profile (BFF application users as members) and the task-154 role split.
    /// </remarks>
    internal const string ProcedureVersion = "secure-setup-procedure=4";

    private readonly IProvisioningRunRepository _repository;
    private readonly ISecureRecordSetupDataverse _dataverse;
    private readonly EnvVarValuesOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<H7bSecureRecordSetupHandler> _logger;
    private readonly Func<SecureRecordOwnerRoleSet> _roleSet;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>Constructs the handler (production: the embedded codified set).</summary>
    public H7bSecureRecordSetupHandler(
        IProvisioningRunRepository repository,
        ISecureRecordSetupDataverse dataverse,
        IOptions<EnvVarValuesOptions> options,
        TimeProvider timeProvider,
        ILogger<H7bSecureRecordSetupHandler> logger)
        : this(repository, dataverse, options, timeProvider, logger, () => SecureRecordOwnerRoleSet.Embedded)
    {
    }

    /// <summary>Test seam: the codified set comes from <paramref name="roleSet"/>.</summary>
    internal H7bSecureRecordSetupHandler(
        IProvisioningRunRepository repository,
        ISecureRecordSetupDataverse dataverse,
        IOptions<EnvVarValuesOptions> options,
        TimeProvider timeProvider,
        ILogger<H7bSecureRecordSetupHandler> logger,
        Func<SecureRecordOwnerRoleSet> roleSet)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(roleSet);
        _repository = repository;
        _dataverse = dataverse;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _roleSet = roleSet;
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
                $"H7bSecureRecordSetupHandler invoked with mismatched HandlerId '{envelope.HandlerId}' (expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("H7b Secure Record setup starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) The run row (§4D I3: the repository requires the partition key).
        var read = await _repository.ReadRunAsync(envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            return new HandlerResult.Failure(FailureClass.Resumable, SecureRecordSetupRejectionCodes.RunNotFound,
                $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }
        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;
        var state = run.InterStepState;

        // (2) Upstream guards — each fires before any Dataverse call.
        if (!parameters.TryGetValue(TenantIdParameterKey, out var tenantId) || string.IsNullOrWhiteSpace(tenantId))
        {
            return await FailMissingAsync(run, etag, "tenantId",
                "Run parameter 'tenantId' is required (§4D I1 — H7b never guesses the tenant).", cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(state.DataverseEnvUrl))
        {
            return await FailMissingAsync(run, etag, "dataverseEnvUrl",
                "InterStepState.dataverseEnvUrl is absent: H5 adopts the environment H7b configures.", cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(state.BffAppRegId))
        {
            return await FailMissingAsync(run, etag, "bffAppRegId",
                "InterStepState.bffAppRegId is absent: H3's BFF app registration is the identity H7b signs in as.", cancellationToken).ConfigureAwait(false);
        }
        if (!Guid.TryParse(state.BffAppRegSystemUserId, out var bffAppUser) || bffAppUser == Guid.Empty
            || !Guid.TryParse(state.SystemUserId, out var miAppUser) || miAppUser == Guid.Empty)
        {
            return await FailMissingAsync(run, etag, "bffAppRegSystemUserId/systemUserId",
                "InterStepState.bffAppRegSystemUserId and systemUserId (H10's two Dataverse application users) must both be GUIDs: " +
                "they are the only members of the BFF writer field-security profile (S12).", cancellationToken).ConfigureAwait(false);
        }
        if (!Guid.TryParse(state.CustomerBusinessUnitId, out var customerUnitId) || customerUnitId == Guid.Empty)
        {
            return await FailMissingAsync(run, etag, "customerBusinessUnitId",
                "InterStepState.customerBusinessUnitId (H10's customer business unit) must be a GUID: H7b checks it is a direct " +
                "child of the root and holds both BFF application users (INCOMING-145 §6 T1/T3).", cancellationToken).ConfigureAwait(false);
        }
        if (!SecureRecordSetupIntake.TryReadDryRun(parameters, out var dryRun))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, SecureRecordSetupRejectionCodes.DryRunInvalid,
                $"Run parameter '{IntakeParameterCatalog.SecureRecordSetupDryRun}' must be 'true' or 'false' (POST /api/runs " +
                "refuses anything else; this is defence in depth).", null, cancellationToken).ConfigureAwait(false);
        }
        if (_options.Credentials.ClientSecretIsRequiredFirst(EnvVarValuesOptions.SectionName)
            && string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, SecureRecordSetupRejectionCodes.MissingClientSecret,
                "EnvVarValuesOptions:ClientSecret is empty and the FR-39 chain's primary is ClientSecret. H7b signs in exactly " +
                "as H7 does; secret-free Workers configure EnvVarValues:Credentials:Order:0=ManagedIdentityFederated. Nothing " +
                "was called.", null, cancellationToken).ConfigureAwait(false);
        }

        SecureRecordOwnerRoleSet roleSet;
        try
        {
            roleSet = _roleSet();
        }
        catch (InvalidOperationException ex)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, SecureRecordSetupRejectionCodes.RoleSetInvalid,
                ex.Message, null, cancellationToken).ConfigureAwait(false);
        }

        // (3) Level-3 idempotency.
        var setHash = ComputeSetHash(roleSet);
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, setHash);
        if (!dryRun && run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation("H7b idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}", envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (4) The procedure.
        var request = new SecureRecordSetupRequest(
            new SecureRecordSetupTarget(state.DataverseEnvUrl!, tenantId, state.BffAppRegId!),
            roleSet,
            [bffAppUser, miAppUser],
            customerUnitId,
            dryRun);
        SecureRecordSetupOutcome outcome;
        try
        {
            outcome = await new SecureRecordSetupProcedure(_dataverse, _logger).RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SecureRecordSetupDataverseException ex)
        {
            _logger.LogWarning(ex, "H7b Dataverse fault: runId={RunId} kind={Kind}", envelope.RunId, ex.Kind);
            var code = ex.Kind switch
            {
                SecureRecordSetupFaultKind.Auth => SecureRecordSetupRejectionCodes.DataverseAuthFailure,
                SecureRecordSetupFaultKind.RateLimited => SecureRecordSetupRejectionCodes.RateLimited,
                _ => SecureRecordSetupRejectionCodes.DataverseInvocationFailed,
            };
            return await FailAsync(run, etag, FailureClass.Resumable, code,
                $"{ex.Message} Every step reads before it writes — resuming re-runs H7b safely.", null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "H7b infrastructure fault: runId={RunId}", envelope.RunId);
            return await FailAsync(run, etag, FailureClass.Resumable, SecureRecordSetupRejectionCodes.DataverseInvocationFailed,
                $"Secure Record setup infrastructure error: {ex.GetType().Name}: {ex.Message}. Resuming re-runs H7b safely.",
                null, cancellationToken).ConfigureAwait(false);
        }

        switch (outcome)
        {
            case SecureRecordSetupOutcome.Refused refused:
                return await FailAsync(run, etag, refused.Class, refused.RejectionCode, refused.Diagnostic,
                    new { setHash, dryRun, writesBeforeRefusal = refused.Actions }, cancellationToken).ConfigureAwait(false);

            case SecureRecordSetupOutcome.Planned planned:
                _logger.LogInformation("H7b dry run: runId={RunId} plannedWrites={Count}", envelope.RunId, planned.Actions.Count);
                return await FailAsync(run, etag, FailureClass.Resumable, SecureRecordSetupRejectionCodes.DryRunComplete,
                    $"Dry run: nothing was written. {planned.Actions.Count} write(s) planned — see gate '{PlanGateId}'. " +
                    $"Start a run without '{IntakeParameterCatalog.SecureRecordSetupDryRun}' to apply.",
                    new { setHash, plan = planned.Actions }, cancellationToken, gateId: PlanGateId).ConfigureAwait(false);

            case SecureRecordSetupOutcome.Applied applied:
                stopwatch.Stop();
                _logger.LogInformation(
                    "H7b Secure Record setup succeeded: runId={RunId} writes={Writes} durationMs={DurationMs}",
                    envelope.RunId, applied.Actions.Count, stopwatch.ElapsedMilliseconds);
                return await MarkCompleteAsync(run, etag, idempotencyKey, envelope, new
                {
                    setHash,
                    businessUnitId = applied.State.BusinessUnitId,
                    customerBusinessUnitId = customerUnitId,
                    ownerTeamId = applied.State.OwnerTeamId,
                    roleId = applied.State.RoleId,
                    privilegeCount = applied.State.PrivilegeCount,
                    lockedTables = applied.State.LockedTables,
                    writes = applied.Actions,
                }, cancellationToken).ConfigureAwait(false);

            default:
                throw new InvalidOperationException($"Unknown Secure Record setup outcome {outcome.GetType().Name}.");
        }
    }

    /// <summary><c>secure-setup-{customerId}-{setHash}</c> (INCOMING-145 §2.3).</summary>
    internal static string BuildIdempotencyKey(string customerId, string setHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(setHash);
        return $"secure-setup-{customerId}-{setHash}";
    }

    /// <summary>The set's hash, salted with <see cref="ProcedureVersion"/>.</summary>
    internal static string ComputeSetHash(SecureRecordOwnerRoleSet roleSet)
    {
        ArgumentNullException.ThrowIfNull(roleSet);
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(ProcedureVersion + "\n" + roleSet.RoleName + "\n" + roleSet.BusinessUnitName + "\n" + roleSet.SetHash()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private Task<HandlerResult> FailMissingAsync(ProvisioningRun run, string etag, string missing, string detail, CancellationToken ct)
        => FailAsync(run, etag, FailureClass.Resumable, SecureRecordSetupRejectionCodes.MissingUpstreamState,
            $"Missing required upstream value '{missing}'. {detail}", null, ct);

    private async Task<HandlerResult> FailAsync(
        ProvisioningRun run,
        string etag,
        FailureClass failureClass,
        string rejectionCode,
        string diagnostic,
        object? evidence,
        CancellationToken cancellationToken,
        string? gateId = null)
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

        run.GateStates[gateId ?? $"h7b-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
            Evidence = evidence is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(evidence)).RootElement.Clone(),
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning("H7b failure state write LOST optimistic-concurrency race: runId={RunId} winningStatus={Status}",
                run.RunId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning("H7b failure state write raced with row delete: runId={RunId}", run.RunId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        HandlerEnvelope envelope,
        object evidence,
        CancellationToken cancellationToken)
    {
        var completedAt = _timeProvider.GetUtcNow();
        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier;
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = completedAt - TimeSpan.FromMilliseconds(1),
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        run.ErrorDetail = null;
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
            _logger.LogWarning("H7b success state write LOST optimistic-concurrency race: runId={RunId} winningStatus={Status}",
                run.RunId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(FailureClass.Resumable, SecureRecordSetupRejectionCodes.ConcurrentWriteConflict,
                $"Concurrent write advanced run '{run.RunId}' between H7b read and write. Winning status: " +
                $"{conflict.Current.Run.Status}. Resume re-runs H7b, which then reads and writes nothing.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            return new HandlerResult.Failure(FailureClass.Resumable, SecureRecordSetupRejectionCodes.RunDeletedDuringWrite,
                $"ProvisioningRun '{run.RunId}' was deleted while H7b was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
