// -----------------------------------------------------------------------------
// H8SpeContainerHandler.cs
//
// L2 CONTROL-PLANE H8 SPE-container-CREATION handler (H8-B semantics per
// task 214, 2026-08-30). SUPERSEDES H8SpeContainerTypeHandler (deleted).
//
// PURPOSE (POST-REWRITE):
//   Creates ONE SPE container per customer inside a PRE-EXISTING container-type
//   whose GUID comes from spaarke-constants.yaml (populated once per env by the
//   operator per docs/guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md steps 3+7).
//   The container-type itself is NEVER created at customer dispatch time —
//   per topology doc §R5 that operation requires a delegated token and returns
//   HTTP 403 accessDenied under L2's app-only runtime credential (verified 3×,
//   most recently 2026-08-30 — runs/h8-live-test-2026-08-30.md).
//
// FLOW (per topology doc §6):
//   1. Read tenantId + containerTypeId from run parameters; owning app from SpeContainerOptions
//   2. Idempotency check (spe-{customerId}) — durable no-op if already done
//   3. Call ISpeContainerProvisioner.ProvisionAsync (CREATE + ACTIVATE)
//   4. Call ISpeContainerVerifier.VerifyAsync (app-only GET) — 404 signals
//      24h SPE replication lag → RunStatus.WaitingOnGate
//   5. Persist container GUID to InterStepState.SpeContainerId — H7 reads this
//      to write Dataverse env-var sprk_SharePointEmbeddedContainerId
//   6. Mark H8 CompletedPhase + Verified gate + Running (reconciler observes)
//
// DELETED FROM H8-A (pre-rewrite):
//   - Container-TYPE creation (retired to operator prereq per §R5)
//   - Container-type registration + owning-app permission grant (also §R5)
//   - KV write of SPE-ContainerTypeId per customer (containerTypeId now comes
//     from constants, not per-customer KV; H4 no longer pre-creates that slot)
//   - sharePointDomain + subscriptionId + upgradeMode parameter guards (not
//     needed by container CREATION). The owning app is
//     SpeContainerOptions.ContainerTypeOwners — L2 configuration keyed by
//     containerTypeId (task 245b); L2 signs in as it with the Worker UAMI's
//     federated identity credential (task 248 — no certificate).
//   - T6 trap detection (task 214.4 Option A — H13 owns T6 acceptance gate)
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-11 + FR-33
//   - docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md §R1, §R5, §6
//   - docs/guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md (the operator prereq
//     that replaces H8-A's old container-type creation scope)
//   - runs/h8-live-test-2026-08-30.md (empirical §R5 verification driving §6.5
//     Path C pivot-to-comply on H8's scope)
//   - .claude/adr/ADR-004: single IJobHandler-shaped impl registered in L2 DI.
//   - .claude/adr/ADR-028 A4: the owning-app token is an MI-FIC client
//     assertion (SpeConfidentialClientGraphFactory, task 248) — A4's default
//     credential; no certificate or secret is stored for the owning app.
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌───────────────────────────────────────────┬──────────────────────────┐
//   │ Failure mode                               │ §4C class                │
//   ├───────────────────────────────────────────┼──────────────────────────┤
//   │ 24h SPE replication lag (verify GET 404    │ NOT a §4C failure class  │
//   │ on a just-created container)               │ — RunStatus.WaitingOnGate│
//   │                                             │ (session-free run-level  │
//   │                                             │ pause; DS-4 §2)          │
//   │ Missing tenantId / containerTypeId, or no  │ Resumable                │
//   │ owner configured for the container type    │ (external precondition — │
//   │                                             │ operator fixes + resumes)│
//   │ Run not found in Cosmos partition          │ Resumable                │
//   │ Provisioner CreateFailure                  │ Resumable                │
//   │ Provisioner infra fault (no side effect)   │ Resumable                │
//   │ Provisioner outputs incomplete             │ Resumable                │
//   │ Provisioner ActivateFailure                │ QuarantineRequired       │
//   │                                            │ (container exists but is │
//   │                                            │ not activated/usable)    │
//   │ Verification NotVerified                   │ QuarantineRequired       │
//   │                                            │ (created + activated,    │
//   │                                            │ unverifiable)            │
//   │ Verifier infra fault                       │ QuarantineRequired       │
//   │ Concurrent Cosmos writer conflict          │ Resumable                │
//   │ Run row deleted mid-flight                 │ Resumable                │
//   └───────────────────────────────────────────┴──────────────────────────┘
//
// IDEMPOTENCY (unchanged from H8-A): key is <c>spe-{customerId}</c>. Level-3
// (handler-body durable dedup): scans ProvisioningRun.CompletedPhases for
// (Phase=="H8", IdempotencyKey==<key>). Match → Success no-op BEFORE any
// external side effect. Enforces "one container per customer, never re-create"
// (topology doc §6: containers are cheap but the customer's container = data).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   H8 lives in L2 (not BFF) per spec §5.2 / D3 / D8 / D12; consumes NO
//   AI-internal types. Uses IProvisioningRunRepository + two dedicated seams
//   (ISpeContainerProvisioner, ISpeContainerVerifier); no BFF-facade
//   dependencies.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H8SpeContainerHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H8;

    /// <summary>Non-secret parameter key carrying the customer Entra tenant id (§4D I1/I5).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>
    /// Non-secret parameter key carrying the PRE-EXISTING container-type GUID
    /// (from <c>spaarke-constants.yaml per_env_constants.&lt;env&gt;.containerTypeId</c>).
    /// Populated by SKILL Step 4.0 payload construction.
    /// </summary>
    public const string ContainerTypeIdParameterKey = "containerTypeId";

    /// <summary>Non-secret parameter key carrying the container display name. Optional — defaults to <see cref="SpeContainerOptions.DefaultDisplayNamePrefix"/> + " - {customerId}".</summary>
    public const string DisplayNameParameterKey = "speContainerDisplayName";

    private readonly IProvisioningRunRepository _repository;
    private readonly ISpeContainerProvisioner _provisioner;
    private readonly ISpeContainerVerifier _verifier;
    private readonly SpeContainerOptions _options;
    private readonly ILogger<H8SpeContainerHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H8 SPE container handler (H8-B semantics). All
    /// collaborators are interface-abstracted so unit tests can substitute
    /// stubs for each seam.
    /// </summary>
    public H8SpeContainerHandler(
        IProvisioningRunRepository repository,
        ISpeContainerProvisioner provisioner,
        ISpeContainerVerifier verifier,
        IOptions<SpeContainerOptions> options,
        ILogger<H8SpeContainerHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(provisioner);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _provisioner = provisioner;
        _verifier = verifier;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<HandlerResult> HandleAsync(
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.CustomerId);

        if (!string.Equals(envelope.HandlerId, HandlerIdentifier, StringComparison.Ordinal))
        {
            // Defensive: the reconciler routes by HandlerId string match. A
            // mismatch means a dispatch bug — fail loud rather than silently
            // mis-executing (parity with sibling handlers).
            throw new InvalidOperationException(
                $"H8SpeContainerHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H8-B SPE container CREATION starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H8-B aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SpeContainerRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;

        // (2) Parameter guards — every field H8-B needs must be non-empty
        //     BEFORE any external side effect (§4C Resumable classification).
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerRejectionCodes.MissingTenantId,
                "Run parameter 'tenantId' is required by H8 (§4D I1/I5 no-hardcoded-tenant). " +
                "Upstream (SKILL Step 4.0 / intake) MUST populate this before H8 dispatches.",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, ContainerTypeIdParameterKey, out var containerTypeId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerRejectionCodes.MissingContainerTypeId,
                "Run parameter 'containerTypeId' is required by H8 (topology doc §R1: container-type is a " +
                "pre-existing operator prereq). Populated by SKILL Step 4.0 from " +
                "spaarke-constants.yaml per_env_constants.<env>.containerTypeId — if missing, the operator " +
                "has not completed SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md OR SKILL payload construction was bypassed.",
                cancellationToken).ConfigureAwait(false);
        }
        // T226: H4 writes the trimmed value to the vault (BuildIntakeValues); Graph gets the same id.
        containerTypeId = containerTypeId.Trim();
        // (3) Owning app (task 245b) — L2 configuration keyed by the container type: the owning app the
        //     container type is bound to (topology R1). Not the customer BFF app: the BFF app is a
        //     separate identity (topology §3A). L2 signs in as the owning app via MI-FIC (task 248).
        if (!_options.TryGetOwner(containerTypeId, out var owner))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerRejectionCodes.ContainerTypeOwnerNotConfigured,
                $"No SpeContainerOptions:ContainerTypeOwners entry for container type '{containerTypeId}'. H8 creates " +
                "containers as the container type's owning app, whose client id is Worker configuration — add the " +
                "entry (controlplane-worker-app-service.bicep speContainerTypeOwners) after the topology runbook " +
                "(SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md) has created the container type and its owning app, then resume.",
                cancellationToken).ConfigureAwait(false);
        }

        var displayName = TryGetNonEmpty(parameters, DisplayNameParameterKey, out var displayNameRaw)
            ? displayNameRaw
            : $"{_options.DefaultDisplayNamePrefix} - {envelope.CustomerId}";
        var description = $"SPE container for customer {envelope.CustomerId} — created by L2 H8 handler.";

        // (4) Idempotency key — customerId-only (version-independent; one
        //     container per customer, never re-created).
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId);

        // (5) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H8-B idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (6) Invoke the provisioner (CREATE + ACTIVATE per topology doc §6).
        //     Infra faults (thrown) are Resumable (no confirmed external side
        //     effect); CreateFailure is Resumable; ActivateFailure is
        //     QuarantineRequired (container exists but not activated).
        SpeContainerProvisionOutcome provisionOutcome;
        try
        {
            var provisionRequest = new SpeContainerProvisionRequest(
                CustomerId: envelope.CustomerId,
                TenantId: tenantId,
                ContainerTypeId: containerTypeId,
                OwningAppId: owner.OwnerAppId,
                DisplayName: displayName,
                Description: description);
            provisionOutcome = await _provisioner.ProvisionAsync(provisionRequest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H8-B provisioner infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerRejectionCodes.ProvisioningInfraFault,
                $"SPE container provisioner infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "No confirmed external side effect — Resumable.",
                cancellationToken).ConfigureAwait(false);
        }

        if (provisionOutcome is SpeContainerProvisionOutcome.CreateFailure createFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerRejectionCodes.ProvisioningFailed, createFailure.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        if (provisionOutcome is SpeContainerProvisionOutcome.ActivateFailure activateFailure)
        {
            // Container was created but activation failed — QuarantineRequired.
            // Persist the created-but-not-activated containerId to InterStepState
            // for audit/cleanup visibility.
            run.InterStepState.SpeContainerId = activateFailure.ContainerId;
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SpeContainerRejectionCodes.ContainerActivationFailed, activateFailure.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        var outputs = ((SpeContainerProvisionOutcome.Success)provisionOutcome).Outputs;
        if (string.IsNullOrWhiteSpace(outputs.ContainerId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerRejectionCodes.ProvisioningOutputsIncomplete,
                "SPE container provisioner returned incomplete outputs — ContainerId is blank.",
                cancellationToken).ConfigureAwait(false);
        }

        // (7) Post-condition: verify the container is readable via a FRESH
        //     app-only token. Container now EXISTS + is ACTIVATED — any
        //     non-transient failure past this point is QuarantineRequired.
        SpeContainerVerificationResult verifyResult;
        try
        {
            var verifyRequest = new SpeContainerVerificationRequest(
                ContainerId: outputs.ContainerId,
                OwningAppId: owner.OwnerAppId,
                TenantId: tenantId);
            verifyResult = await _verifier.VerifyAsync(verifyRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H8-B verifier infrastructure fault: runId={RunId} customerId={CustomerId} containerId={ContainerId}",
                envelope.RunId, envelope.CustomerId, outputs.ContainerId);
            // Persist the created container-id so a later resume doesn't lose it.
            run.InterStepState.SpeContainerId = outputs.ContainerId;
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SpeContainerRejectionCodes.VerificationInfraFault,
                $"Post-creation app-only GET verification infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                $"Container '{outputs.ContainerId}' was created + activated but its readability via app-only " +
                "token could not be confirmed — QuarantineRequired.",
                cancellationToken).ConfigureAwait(false);
        }

        if (verifyResult is SpeContainerVerificationResult.NotVerified notVerified)
        {
            run.InterStepState.SpeContainerId = outputs.ContainerId;
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SpeContainerRejectionCodes.ContainerGetVerificationFailed, notVerified.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (7b) DS-4 §2 / this project's CLAUDE.md MUST rules: the up-to-24h
        // SPE container-type replication window is a RUN-LEVEL external
        // blocker, not a handler defect. The container DOES exist + is
        // activated (real, durable side effects) — persist its ID so a later
        // resume does not need to re-derive it — but do NOT record a
        // CompletedPhase (H8 has not finished; a later resume re-runs
        // HandleAsync in full).
        if (verifyResult is SpeContainerVerificationResult.ReplicationPending pending)
        {
            return await MarkWaitingOnGateAsync(run, etag, outputs, pending.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        var verified = (SpeContainerVerificationResult.Verified)verifyResult;

        // (8) Advance Cosmos state — write InterStepState.SpeContainerId (the
        //     durable handoff H7 will read to materialize the real Dataverse
        //     env-var), the Verified gate, and the CompletedPhase entry.
        stopwatch.Stop();
        _logger.LogInformation(
            "H8-B SPE container CREATION succeeded: runId={RunId} customerId={CustomerId} " +
            "containerId={ContainerId} containerTypeId={ContainerTypeId} verifiedStatus={Status} " +
            "durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, outputs.ContainerId, containerTypeId,
            verified.Status, stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(run, etag, idempotencyKey, outputs, verified, envelope, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H8 idempotency key: <c>spe-{customerId}</c>.
    /// Exposed internal so unit tests can construct expected keys without
    /// duplicating the format. Key shape unchanged from H8-A (customerId-only,
    /// version-independent).
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        return $"spe-{customerId}";
    }

    private static bool TryGetNonEmpty(
        IDictionary<string, string> parameters,
        string key,
        out string value)
    {
        if (parameters.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private async Task<HandlerResult> FailAsync(
        ProvisioningRun run,
        string etag,
        FailureClass failureClass,
        string rejectionCode,
        string diagnostic,
        CancellationToken cancellationToken)
    {
        run.Status = failureClass == FailureClass.QuarantineRequired
            ? RunStatus.Quarantined
            : RunStatus.Failed;
        run.CurrentPhase = HandlerIdentifier;
        run.ErrorDetail = $"[{rejectionCode}] {diagnostic}";
        if (failureClass == FailureClass.QuarantineRequired)
        {
            run.Quarantine = new QuarantineInfo
            {
                State = QuarantineState.Quarantined,
                Reason = diagnostic,
                QuarantinedByHandler = HandlerIdentifier,
                QuarantinedAt = DateTimeOffset.UtcNow,
            };
        }

        run.GateStates[$"h8-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H8-B failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H8-B failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    /// <summary>
    /// Records the 24h SPE replication-lag pause. Sets
    /// <see cref="RunStatus.WaitingOnGate"/> (never Resumable/QuarantineRequired
    /// per this project's CLAUDE.md MUST rules), persists the already-created
    /// container id, and marks the T6Verified gate Pending (with evidence)
    /// rather than Verified. Does NOT append a CompletedPhase — H8 has not
    /// finished; a subsequent resume re-executes HandleAsync from the top,
    /// re-attempting verification.
    /// </summary>
    private async Task<HandlerResult> MarkWaitingOnGateAsync(
        ProvisioningRun run,
        string etag,
        SpeContainerProvisionOutputs outputs,
        string diagnostic,
        CancellationToken cancellationToken)
    {
        run.InterStepState.SpeContainerId = outputs.ContainerId;
        run.GateStates[SpeContainerGates.T6Verified] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
            Evidence = BuildEvidence(outputs.ContainerId, "replication-pending", verifiedViaAppOnlyToken: false),
        };

        run.Status = RunStatus.WaitingOnGate;
        run.CurrentPhase = HandlerIdentifier;
        run.ErrorDetail = null; // Not an error — an expected external wait.

        _logger.LogInformation(
            "H8-B SPE container verification WaitingOnGate (24h replication lag): runId={RunId} " +
            "customerId={CustomerId} containerId={ContainerId} diagnostic={Diagnostic}",
            run.RunId, run.CustomerId, outputs.ContainerId, diagnostic);

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H8-B WaitingOnGate state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H8-B WaitingOnGate state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        // Success: H8 correctly identified + recorded the external wait —
        // this is not an operator-actionable failure. HandlerOutcomeApplier
        // does NOT overwrite run.Status on the Success path, so the
        // WaitingOnGate write above is preserved.
        return new HandlerResult.Success(BuildIdempotencyKey(run.CustomerId));
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        SpeContainerProvisionOutputs outputs,
        SpeContainerVerificationResult.Verified verified,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        // H7 (task 050, already landed) reads SpeContainerId as the source
        // value for Dataverse env-var sprk_SharePointEmbeddedContainerId.
        run.InterStepState.SpeContainerId = outputs.ContainerId;
        run.GateStates[SpeContainerGates.T6Verified] = new GateEntry
        {
            Status = GateState.Verified,
            VerifiedAt = completedAt,
            VerifierHandler = HandlerIdentifier,
            Evidence = BuildEvidence(outputs.ContainerId, verified.Status, verifiedViaAppOnlyToken: true),
        };

        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier; // Reconciler observes + fans out.
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        run.ErrorDetail = null;

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H8-B success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SpeContainerRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H8 read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H8.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H8-B success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SpeContainerRejectionCodes.RunDeletedDuringProvisioning,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H8 was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }

    /// <summary>
    /// Builds the gate evidence JSON. <paramref name="verifiedViaAppOnlyToken"/>
    /// is an explicit parameter so the WaitingOnGate/replication-pending case
    /// produces truthful evidence (verification has NOT happened yet), not a
    /// misleading hardcoded true.
    /// </summary>
    private static System.Text.Json.JsonElement BuildEvidence(
        string containerId, string verifiedStatus, bool verifiedViaAppOnlyToken)
    {
        var doc = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            containerId,
            verifiedStatus,
            verifiedViaAppOnlyToken,
        });
        return doc;
    }
}
