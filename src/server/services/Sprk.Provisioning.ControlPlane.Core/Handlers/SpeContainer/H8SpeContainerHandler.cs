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
// FLOW (per topology doc §6, with unified-access-control-r2 task 165):
//   1. Read tenantId + containerTypeId from run parameters; owning app from SpeContainerOptions
//   2. Idempotency check (spe-{customerId}) — durable no-op if already done
//   3. Resolve the container's OWNER — the customer environment's ROOT business
//      unit (H5 output DataverseEnvUrl) — BEFORE anything is created (task 165)
//   4. Read this run's typed CREATION RECORD (InterStepState.SpeContainerCreation):
//      a container this run already created is RESUMED, never created again
//   5. Otherwise call ISpeContainerProvisioner.ProvisionAsync (CREATE + ACTIVATE)
//      and RECORD the container immediately, before anything that can fail or wait
//   6. Call ISpeContainerVerifier.VerifyAsync (app-only GET) — 404 signals
//      24h SPE replication lag → RunStatus.WaitingOnGate (the container stays
//      on record; a resume verifies the same container)
//   7. BIND the verified container to its business unit (stamp, read back,
//      REMOVE on failure — ISpeContainerProvisioner.BindRootContainerAsync)
//   8. Persist the container GUID to InterStepState.SpeContainerId — H7 reads this
//      to write Dataverse env-var sprk_SharePointEmbeddedContainerId. Written ONLY
//      here, after the bind: H7 never consumes an unbound container (and H7
//      depends on H8 in the DAG)
//   9. Mark H8 CompletedPhase + Verified gate + Running (reconciler observes)
//
// BUSINESS-UNIT STAMP + RESUME (unified-access-control-r2 task 165, owner rounds
// 35 item 1, 41 item 1, 49 item 2 — re-applied to H8-B at the batch-4
// integration; projects/unified-access-control-r2/notes/task-165-admin-surfaces.md
// §11.2, §12.2, §13.1, §14.1-14.2, §16):
//   - Every SPE container is stamped with its owning business unit, read back,
//     and removed if the stamp did not land: the BFF's admin plane reaches NO
//     unbound container. H8 depends on H3 AND H5 (DagAdvancer.HandlerDependencies).
//   - H8 RECORDS what it created in TYPED fields (never gate evidence — the Cosmos
//     SDK's Newtonsoft serializer stored a JsonElement as {"valueKind":1}) and a
//     re-entry RESUMES with it: after the replication wait, a quarantine, a lost
//     write or a crash it never creates a second container and never orphans the
//     first one unbound. A container POST with NO authoritative answer is
//     recorded as in doubt and QuarantineRequired: the container type is shared
//     (other customers' containers live in it), so H8 never guesses by listing
//     it — an operator checks, records a container found, and clears.
//   - DROPPED as moot under H8-B: everything about container-TYPE creation (the
//     type in-doubt quarantine, re-using a type the run created, the type
//     registration, adopting a container by listing the run's own type).
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
//   │ Missing DataverseEnvUrl / root business    │ Resumable (nothing       │
//   │ unit unreadable, none or two (task 165)    │ created)                 │
//   │ Run not found in Cosmos partition          │ Resumable                │
//   │ Provisioner CreateFailure (Graph's answer) │ Resumable                │
//   │ Provisioner CreateFailure, NO answer       │ QuarantineRequired       │
//   │ (container in doubt — task 165)            │ (until an operator clears│
//   │                                            │ after checking)          │
//   │ Provisioner infra fault (before a request) │ Resumable                │
//   │ Provisioner outputs incomplete             │ Resumable                │
//   │ Provisioner ActivateFailure                │ QuarantineRequired       │
//   │                                            │ (container exists, on    │
//   │                                            │ record; resume activates)│
//   │ Verification NotVerified                   │ QuarantineRequired       │
//   │                                            │ (created + activated,    │
//   │                                            │ unverifiable)            │
//   │ Verifier infra fault                       │ QuarantineRequired       │
//   │ Bind failed (removed / not removed / infra)│ QuarantineRequired       │
//   │ Creation not recordable (ids in diagnostic)│ QuarantineRequired       │
//   │ Concurrent Cosmos writer conflict          │ Resumable                │
//   │ Run row deleted mid-flight                 │ Resumable                │
//   └───────────────────────────────────────────┴──────────────────────────┘
//
// IDEMPOTENCY (unchanged from H8-A): key is <c>spe-{customerId}</c>. Level-3
// (handler-body durable dedup): scans ProvisioningRun.CompletedPhases for
// (Phase=="H8", IdempotencyKey==<key>). Match → Success no-op BEFORE any
// external side effect. Enforces "one container per customer, never re-create"
// (topology doc §6: containers are cheap but the customer's container = data).
// The creation record extends this to an INCOMPLETE H8 (task 165).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   H8 lives in L2 (not BFF) per spec §5.2 / D3 / D8 / D12; consumes NO
//   AI-internal types. Uses IProvisioningRunRepository + three dedicated seams
//   (ISpeContainerProvisioner, ISpeContainerVerifier,
//   IDataverseRootBusinessUnitReader); no BFF-facade dependencies.
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
    private readonly IDataverseRootBusinessUnitReader _rootBusinessUnitReader;
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
        IDataverseRootBusinessUnitReader rootBusinessUnitReader,
        IOptions<SpeContainerOptions> options,
        ILogger<H8SpeContainerHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(provisioner);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(rootBusinessUnitReader);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _provisioner = provisioner;
        _verifier = verifier;
        _rootBusinessUnitReader = rootBusinessUnitReader;
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

        var owningAppId = owner.OwnerAppId;
        var displayName = TryGetNonEmpty(parameters, DisplayNameParameterKey, out var displayNameRaw)
            ? displayNameRaw
            : $"{_options.DefaultDisplayNamePrefix} - {envelope.CustomerId}";
        // The run id names the container to an operator checking a creation in doubt (task 165): the type is shared.
        var description = $"SPE container for customer {envelope.CustomerId} (run {envelope.RunId}) — created by L2 H8 handler.";

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

        // (5a) InterStepState.SpeContainerId is H7's hand-off and is written ONLY by MarkCompleteAsync, after the bind
        //      (task 165, owner round 41 item 1). H8 is not complete (5), so a value here predates the bind: an H8 before
        //      task 165 wrote the UNBOUND container's id there on its replication-pending and quarantine paths, and that
        //      field is such a run's only record of it. It is MOVED into H8's typed creation record and withdrawn before
        //      ANY write this entry makes — so no persisted state of an incomplete H8 hands a container to H7, and the
        //      container is resumed, not orphaned.
        AdoptUnboundHandOff(run);

        // (5b) The container's owner (task 165, owner round 35 item 1): every SPE container is stamped with its owning
        //      business unit at creation — here the ROOT business unit of the customer's Dataverse environment (H5
        //      output; under D-12 the environment is the customer's own). Resolved BEFORE any external side effect: H8
        //      never creates a container it could not bind. H8 runs after H5 (DagAdvancer.HandlerDependencies), so a
        //      missing URL is an upstream defect — Resumable.
        var dataverseEnvUrl = run.InterStepState.DataverseEnvUrl;
        if (string.IsNullOrWhiteSpace(dataverseEnvUrl))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerRejectionCodes.MissingDataverseEnvUrl,
                "InterStepState.dataverseEnvUrl is empty — H5 (Dataverse environment) MUST complete before H8: the " +
                "container is bound to the environment's root business unit (task 165, owner round 35 item 1).",
                cancellationToken).ConfigureAwait(false);
        }

        Guid rootBusinessUnitId;
        try
        {
            var root = await _rootBusinessUnitReader
                .ReadRootBusinessUnitIdAsync(dataverseEnvUrl, tenantId, cancellationToken)
                .ConfigureAwait(false);
            if (root is not { } resolved || resolved == Guid.Empty)
            {
                return await FailAsync(run, etag, FailureClass.Resumable,
                    SpeContainerRejectionCodes.RootBusinessUnitUnresolved,
                    $"Dataverse environment '{dataverseEnvUrl}' reports no root business unit — the container would " +
                    "have no owner, so none is created.",
                    cancellationToken).ConfigureAwait(false);
            }

            rootBusinessUnitId = resolved;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H8-B could not read the root business unit: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerRejectionCodes.RootBusinessUnitUnresolved,
                $"Reading the root business unit of '{dataverseEnvUrl}' failed: {ex.GetType().Name}: {ex.Message}. " +
                "No container was created — Resumable.",
                cancellationToken).ConfigureAwait(false);
        }

        // (5c) What THIS run's H8 already created (task 165, owner rounds 41 + 49) — read from the run's TYPED creation
        //      record, never from gate evidence. H8 is not complete (5), so a recorded container is one H8 made on an
        //      earlier entry that stopped before completing: the 24h replication wait, a quarantined activation,
        //      verification or bind, a lost write, or a crash. A re-entry RESUMES with it — it never calls ProvisionAsync
        //      for a recorded container (that created a second container on every resume and orphaned the first UNBOUND).
        var recorded = ReadRecordedCreation(run);

        // (5d) A container POST of this run got no authoritative answer (owner round 49 item 2, on H8-B): a container
        //      may exist, unbound, that no one names. The container type is shared, so H8 does not guess by listing it:
        //      it creates NO container until an operator has checked and cleared the quarantine below (a container they
        //      found is recorded as the root and resumed instead).
        if (recorded.RootContainerId is null && recorded.RootContainerInDoubtSince is { } inDoubtSince)
        {
            if (!QuarantineClearedSince(run, inDoubtSince))
            {
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    SpeContainerRejectionCodes.ContainerCreationInDoubt,
                    ContainerCreationInDoubtDiagnostic(envelope, containerTypeId, owningAppId, displayName, inDoubtSince),
                    cancellationToken).ConfigureAwait(false);
            }

            // The operator cleared H8's in-doubt quarantine after checking (a container they found would now be recorded
            // as the root). The marker is withdrawn and a container is created below.
            _logger.LogWarning(
                "H8-B container creation in doubt since {Since} was cleared by an operator — creating the container: runId={RunId}",
                inDoubtSince, envelope.RunId);
            run.InterStepState.SpeContainerCreation!.RootContainerInDoubtSince = null;
        }

        CreatedContainers outputs;
        if (recorded.RootContainerId is { } recordedRoot)
        {
            outputs = new CreatedContainers(recordedRoot, recorded.AdditionalContainerIds);
            _logger.LogInformation(
                "H8-B resumes with the container {RootContainerId} it already created — nothing is created: " +
                "runId={RunId} customerId={CustomerId} recordStatus={Status}",
                recordedRoot, envelope.RunId, envelope.CustomerId, recorded.Status ?? "(none)");

            // A recorded container whose /activate failed — or one an operator found after a creation in doubt, which was
            // never activated — is activated now. It is THIS container: a resume never creates another.
            if (recorded.NeedsActivation)
            {
                SpeContainerProvisionOutcome activation;
                try
                {
                    activation = await _provisioner.ActivateAsync(
                        new SpeContainerActivationRequest(envelope.CustomerId, tenantId, owningAppId, recordedRoot),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "H8-B activation infrastructure fault on resume: runId={RunId} containerId={ContainerId}",
                        envelope.RunId, recordedRoot);
                    return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                        SpeContainerRejectionCodes.ContainerActivationInfraFault,
                        $"Activating recorded container '{recordedRoot}' failed before any Graph call: {ex.GetType().Name}: " +
                        $"{ex.Message}. The container stays on the run's record (UNBOUND); a resume activates it again.",
                        cancellationToken).ConfigureAwait(false);
                }

                if (activation is SpeContainerProvisionOutcome.ActivateFailure reactivateFailure)
                {
                    return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                        reactivateFailure.NoAnswer
                            ? SpeContainerRejectionCodes.ContainerActivationInfraFault
                            : SpeContainerRejectionCodes.ContainerActivationFailed,
                        reactivateFailure.Diagnostic + " The container stays on the run's record; a resume activates it again.",
                        cancellationToken).ConfigureAwait(false);
                }

                var activated = await RecordCreationAsync(run, etag, new CreationUpdate(
                        RootContainerId: recordedRoot,
                        AdditionalContainerIds: recorded.AdditionalContainerIds,
                        RootContainerInDoubtSince: null,
                        Status: SpeContainerCreationRecord.StatusCreated),
                    expectedRootContainerId: recordedRoot, envelope)
                    .ConfigureAwait(false);
                if (activated.Refusal is { } activatedRefusal)
                {
                    return activatedRefusal;
                }

                (run, etag) = (activated.Run!, activated.ETag!);
            }
        }
        else
        {
            // (6) Invoke the provisioner (CREATE + ACTIVATE per topology doc §6). It throws only before any Graph request
            //     (the owning-app token exchange) — Resumable, nothing created. Every fault after the POST was sent is a
            //     returned outcome saying what may exist, which is RECORDED before anything else (owner round 49 item 2).
            SpeContainerProvisionOutcome provisionOutcome;
            try
            {
                var provisionRequest = new SpeContainerProvisionRequest(
                    CustomerId: envelope.CustomerId,
                    TenantId: tenantId,
                    ContainerTypeId: containerTypeId,
                    OwningAppId: owningAppId,
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
                    "Raised before any Graph request (the provisioner returns every later fault) — Resumable.",
                    cancellationToken).ConfigureAwait(false);
            }

            if (provisionOutcome is SpeContainerProvisionOutcome.CreateFailure { ContainerInDoubt: true } inDoubt)
            {
                // The POST got no answer: a container may exist, unbound. Record THAT before anything else, then quarantine
                // — a re-entry creates nothing until an operator has checked (5d).
                var now = DateTimeOffset.UtcNow;
                var doubt = await RecordCreationAsync(run, etag, new CreationUpdate(
                        RootContainerId: null,
                        AdditionalContainerIds: recorded.AdditionalContainerIds,
                        RootContainerInDoubtSince: now,
                        Status: SpeContainerCreationRecord.StatusRootContainerInDoubt),
                    expectedRootContainerId: null, envelope)
                    .ConfigureAwait(false);
                if (doubt.Refusal is { } doubtRefusal)
                {
                    return doubtRefusal;
                }

                (run, etag) = (doubt.Run!, doubt.ETag!);
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    SpeContainerRejectionCodes.ContainerCreationInDoubt,
                    inDoubt.Diagnostic + " " +
                    ContainerCreationInDoubtDiagnostic(envelope, containerTypeId, owningAppId, displayName, now),
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
                // Container was created but activation failed — QuarantineRequired. The created-but-not-activated
                // container is RECORDED (typed — not the H7 hand-off) so the resume activates THIS container.
                var failedActivation = await RecordCreationAsync(run, etag, new CreationUpdate(
                        RootContainerId: activateFailure.ContainerId,
                        AdditionalContainerIds: recorded.AdditionalContainerIds,
                        RootContainerInDoubtSince: null,
                        Status: SpeContainerCreationRecord.StatusActivationFailed),
                    expectedRootContainerId: null, envelope)
                    .ConfigureAwait(false);
                if (failedActivation.Refusal is { } activationRefusal)
                {
                    return activationRefusal;
                }

                (run, etag) = (failedActivation.Run!, failedActivation.ETag!);
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    activateFailure.NoAnswer
                        ? SpeContainerRejectionCodes.ContainerActivationInfraFault
                        : SpeContainerRejectionCodes.ContainerActivationFailed,
                    activateFailure.Diagnostic, cancellationToken)
                    .ConfigureAwait(false);
            }

            var created = ((SpeContainerProvisionOutcome.Success)provisionOutcome).Outputs;
            if (string.IsNullOrWhiteSpace(created.ContainerId))
            {
                return await FailAsync(run, etag, FailureClass.Resumable,
                    SpeContainerRejectionCodes.ProvisioningOutputsIncomplete,
                    "SPE container provisioner returned incomplete outputs — ContainerId is blank.",
                    cancellationToken).ConfigureAwait(false);
            }

            outputs = new CreatedContainers(created.ContainerId, recorded.AdditionalContainerIds);

            // (6b) RECORD the creation in the run NOW — before verification, the bind or anything else — so no later
            //      failure, wait, crash or lost write can make a re-entry create a second one (owner rounds 41 + 49). The
            //      record is H8's own and TYPED (InterStepState.SpeContainerCreation). It is NOT the H7 hand-off
            //      (SpeContainerId), which only a bound container ever reaches.
            var record = await RecordCreationAsync(run, etag, new CreationUpdate(
                    RootContainerId: outputs.RootContainerId,
                    AdditionalContainerIds: outputs.AdditionalContainerIds,
                    RootContainerInDoubtSince: null,
                    Status: SpeContainerCreationRecord.StatusCreated),
                expectedRootContainerId: null, envelope)
                .ConfigureAwait(false);
            if (record.Refusal is { } refusal)
            {
                return refusal;
            }

            (run, etag) = (record.Run!, record.ETag!);
        }

        // (7) Post-condition: verify the container is readable via a FRESH
        //     app-only token. Container now EXISTS + is ACTIVATED + is RECORDED —
        //     any non-transient failure past this point is QuarantineRequired, and
        //     the resume verifies the same container.
        SpeContainerVerificationResult verifyResult;
        try
        {
            var verifyRequest = new SpeContainerVerificationRequest(
                ContainerId: outputs.RootContainerId,
                OwningAppId: owningAppId,
                TenantId: tenantId);
            verifyResult = await _verifier.VerifyAsync(verifyRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H8-B verifier infrastructure fault: runId={RunId} customerId={CustomerId} containerId={ContainerId}",
                envelope.RunId, envelope.CustomerId, outputs.RootContainerId);
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SpeContainerRejectionCodes.VerificationInfraFault,
                $"Post-creation app-only GET verification infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                $"Container '{outputs.RootContainerId}' was created + activated but its readability via app-only " +
                "token could not be confirmed — QuarantineRequired. It stays on the run's record; a resume verifies it again.",
                cancellationToken).ConfigureAwait(false);
        }

        if (verifyResult is SpeContainerVerificationResult.NotVerified notVerified)
        {
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SpeContainerRejectionCodes.ContainerGetVerificationFailed, notVerified.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (7b) DS-4 §2 / this project's CLAUDE.md MUST rules: the up-to-24h
        // SPE container-type replication window is a RUN-LEVEL external
        // blocker, not a handler defect. The container DOES exist + is
        // activated + is RECORDED (6b), so the later re-entry RESUMES with it at
        // (7) — it creates nothing (task 165, owner round 41 item 1). Do NOT bind
        // (the container may be unaddressable), do NOT hand it to H7, and do NOT
        // record a CompletedPhase. H7 depends on H8, so it waits for the bound
        // container — the other branches still advance.
        if (verifyResult is SpeContainerVerificationResult.ReplicationPending pending)
        {
            return await MarkWaitingOnGateAsync(run, etag, new CreationUpdate(
                    RootContainerId: outputs.RootContainerId,
                    AdditionalContainerIds: outputs.AdditionalContainerIds,
                    RootContainerInDoubtSince: null,
                    Status: SpeContainerCreationRecord.StatusReplicationPending),
                pending.Diagnostic, cancellationToken).ConfigureAwait(false);
        }

        var verified = (SpeContainerVerificationResult.Verified)verifyResult;

        // (7c) Bind the verified container to its owning business unit (task 165, owner round 35 item 1): stamp, read
        //      back, and REMOVE the container when the stamp did not land — no unbound container is handed to H7 or left
        //      behind. Done after verification because an SPE container may be unaddressable for up to 24h after
        //      creation (7b): binding earlier would remove healthy containers during that documented window.
        SpeContainerBindOutcome bindOutcome;
        try
        {
            bindOutcome = await _provisioner.BindRootContainerAsync(
                new SpeContainerBindRequest(
                    CustomerId: envelope.CustomerId,
                    TenantId: tenantId,
                    OwningAppId: owningAppId,
                    ContainerId: outputs.RootContainerId,
                    BusinessUnitId: rootBusinessUnitId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H8-B bind infrastructure fault: runId={RunId} customerId={CustomerId} containerId={ContainerId}",
                envelope.RunId, envelope.CustomerId, outputs.RootContainerId);
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SpeContainerRejectionCodes.ContainerBindingInfraFault,
                $"Binding container '{outputs.RootContainerId}' to business unit '{rootBusinessUnitId}' failed before " +
                $"any Graph call: {ex.GetType().Name}: {ex.Message}. The container exists UNBOUND (no SPE admin route reaches " +
                "it) and stays on the run's record — a resume binds it; or bind it with " +
                "Backfill-SpeContainerBusinessUnitStamp.ps1 -Bind, or remove it — QuarantineRequired.",
                cancellationToken).ConfigureAwait(false);
        }

        if (bindOutcome is SpeContainerBindOutcome.NotBound notBound)
        {
            if (notBound.Removed)
            {
                // The recorded container no longer exists. Drop it from the record, so a resume never verifies a deleted
                // container (a 404 reads as the replication wait) and creates ONE new container instead. Persisted with
                // the merge-safe record write — a lost write here would leave the deleted container on record.
                var removal = await RecordCreationAsync(run, etag, new CreationUpdate(
                        RootContainerId: null,
                        AdditionalContainerIds: outputs.AdditionalContainerIds,
                        RootContainerInDoubtSince: null,
                        Status: SpeContainerCreationRecord.StatusRootContainerRemoved),
                    expectedRootContainerId: outputs.RootContainerId, envelope)
                    .ConfigureAwait(false);
                if (removal.Refusal is { } removalRefusal)
                {
                    return removalRefusal;
                }

                (run, etag) = (removal.Run!, removal.ETag!);
            }

            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                notBound.Removed
                    ? SpeContainerRejectionCodes.ContainerBindingFailed
                    : SpeContainerRejectionCodes.ContainerBindingFailedNotRemoved,
                notBound.Diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (7d) Further containers on record next to the root (an older H8's unbound hand-off — see 5a): each is bound to
        //      the same business unit, or removed, before anything durable consumes the root — none is left unbound. One
        //      that is neither bound nor removed stays on record and the run is quarantined naming it.
        var additionalRefusal = await BindAdditionalContainersAsync(
                run, etag, outputs, envelope, tenantId, owningAppId, rootBusinessUnitId, cancellationToken)
            .ConfigureAwait(false);
        if (additionalRefusal is not null)
        {
            return additionalRefusal;
        }

        // (8) Advance Cosmos state — write InterStepState.SpeContainerId (the
        //     durable handoff H7 will read to materialize the real Dataverse
        //     env-var), the Verified gate, and the CompletedPhase entry.
        stopwatch.Stop();
        _logger.LogInformation(
            "H8-B SPE container CREATION succeeded: runId={RunId} customerId={CustomerId} " +
            "containerId={ContainerId} containerTypeId={ContainerTypeId} verifiedStatus={Status} " +
            "owningBusinessUnitId={BusinessUnitId} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, outputs.RootContainerId, containerTypeId,
            verified.Status, rootBusinessUnitId, stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(run, etag, idempotencyKey, outputs, verified, rootBusinessUnitId, envelope, cancellationToken)
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
    /// per this project's CLAUDE.md MUST rules), keeps the created container ON
    /// RECORD in the typed creation record (<paramref name="update"/>), and marks
    /// the T6Verified gate Pending (with evidence) rather than Verified. Does NOT
    /// append a CompletedPhase and does NOT hand the container to H7
    /// (InterStepState.SpeContainerId stays empty — an unbound container is never
    /// handed off; task 165, owner round 41 item 1). A subsequent re-entry finds
    /// the record and RESUMES with it: the same container is verified again (then
    /// bound and completed) — nothing is created.
    /// </summary>
    private async Task<HandlerResult> MarkWaitingOnGateAsync(
        ProvisioningRun run,
        string etag,
        CreationUpdate update,
        string diagnostic,
        CancellationToken cancellationToken)
    {
        ApplyCreationRecord(run, update);

        run.Status = RunStatus.WaitingOnGate;
        run.CurrentPhase = HandlerIdentifier;
        run.ErrorDetail = null; // Not an error — an expected external wait.

        _logger.LogInformation(
            "H8-B SPE container verification WaitingOnGate (24h replication lag): runId={RunId} " +
            "customerId={CustomerId} containerId={ContainerId} diagnostic={Diagnostic}",
            run.RunId, run.CustomerId, update.RootContainerId, diagnostic);

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
        CreatedContainers outputs,
        SpeContainerVerificationResult.Verified verified,
        Guid owningBusinessUnitId,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        // H7 (task 050, already landed) reads SpeContainerId as the source
        // value for Dataverse env-var sprk_SharePointEmbeddedContainerId. The ONLY
        // writer of a container into it — after the bind (task 165).
        run.InterStepState.SpeContainerId = outputs.RootContainerId;
        // The typed record says what H8 finished with: the container, bound to this unit.
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord
        {
            RootContainerId = outputs.RootContainerId,
            OwningBusinessUnitId = owningBusinessUnitId.ToString("D"),
            Status = SpeContainerCreationRecord.StatusBound,
            UpdatedAt = completedAt,
        };
        run.GateStates[SpeContainerGates.T6Verified] = new GateEntry
        {
            Status = GateState.Verified,
            VerifiedAt = completedAt,
            VerifierHandler = HandlerIdentifier,
            Evidence = BuildEvidence(outputs.RootContainerId, verified.Status, verifiedViaAppOnlyToken: true, owningBusinessUnitId),
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
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H8 (the bound container " +
                             "stays on the run's record, so it is resumed, not re-created).");
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

    /// <summary>Conflicting writes a creation record is merged over before H8 gives up (operator writes only — dispatch is session-serialized per customer).</summary>
    private const int RecordMergeAttempts = 5;

    /// <summary>
    /// What this run's H8 already created and has not completed (unified-access-control-r2 task 165, owner rounds 41 +
    /// 49), read ONLY from the typed <see cref="InterStepState.SpeContainerCreation"/> — never from gate evidence. An
    /// older H8's unbound hand-off is moved into the record by <see cref="AdoptUnboundHandOff"/> first.
    /// </summary>
    internal static RecordedCreation ReadRecordedCreation(ProvisioningRun run)
    {
        var creation = run.InterStepState.SpeContainerCreation;
        var root = Trimmed(creation?.RootContainerId);
        var additional = (creation?.AdditionalContainerIds ?? Array.Empty<string>())
            .Select(Trimmed)
            .Where(id => id is not null && !string.Equals(id, root, StringComparison.Ordinal))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new RecordedCreation(
            RootContainerId: root,
            AdditionalContainerIds: additional,
            RootContainerInDoubtSince: creation?.RootContainerInDoubtSince,
            Status: creation?.Status);
    }

    /// <summary>
    /// An H8 before task 165 wrote the UNBOUND container into <see cref="InterStepState.SpeContainerId"/> (H7's hand-off)
    /// on its replication-pending and quarantine paths. It is moved into the typed creation record — as the root
    /// container, or as a further container to bind when the record already names a different root — and the hand-off is
    /// withdrawn. When that older H8 had quarantined on the activation, the record says so, so the resume activates it.
    /// Called before any write of an entry of an incomplete H8.
    /// </summary>
    internal static void AdoptUnboundHandOff(ProvisioningRun run)
    {
        var handOff = Trimmed(run.InterStepState.SpeContainerId);
        run.InterStepState.SpeContainerId = null;
        if (handOff is null)
        {
            return;
        }

        var activationFailed =
            run.GateStates.ContainsKey($"h8-{SpeContainerRejectionCodes.ContainerActivationFailed}")
            || run.GateStates.ContainsKey($"h8-{SpeContainerRejectionCodes.ContainerActivationInfraFault}");
        var creation = run.InterStepState.SpeContainerCreation ??= new SpeContainerCreationRecord
        {
            Status = activationFailed ? SpeContainerCreationRecord.StatusActivationFailed : SpeContainerCreationRecord.StatusCreated,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        if (Trimmed(creation.RootContainerId) is null)
        {
            creation.RootContainerId = handOff;
        }
        else if (!string.Equals(Trimmed(creation.RootContainerId), handOff, StringComparison.Ordinal))
        {
            creation.AdditionalContainerIds ??= new List<string>();
            if (!creation.AdditionalContainerIds.Contains(handOff, StringComparer.Ordinal))
            {
                creation.AdditionalContainerIds.Add(handOff);
            }
        }
    }

    /// <summary>
    /// True when an operator cleared a quarantine H8 raised, at or after <paramref name="since"/> — the acknowledgement the
    /// "container creation in doubt" quarantine asks for (its diagnostic is the procedure). A clearance older than the
    /// doubt says nothing about it (owner round 57 item 3, VL7).
    /// </summary>
    private static bool QuarantineClearedSince(ProvisioningRun run, DateTimeOffset since) =>
        run.Quarantine is { State: QuarantineState.Cleared, ClearedAt: { } clearedAt } quarantine
        && string.Equals(quarantine.QuarantinedByHandler, HandlerIdentifier, StringComparison.Ordinal)
        && clearedAt >= since;

    /// <summary>THE operator procedure for a container creation in doubt (owner round 49 item 2, on H8-B).</summary>
    private static string ContainerCreationInDoubtDiagnostic(
        HandlerEnvelope envelope, string containerTypeId, string owningAppId, string displayName, DateTimeOffset? since) =>
        $"The container creation for run '{envelope.RunId}' got no authoritative answer" +
        (since is { } s ? $" (at {s:O})" : string.Empty) +
        $": a container may exist in container type '{containerTypeId}' — created by owning app '{owningAppId}' and UNBOUND " +
        "(no SPE admin route reaches it) — that the run does not name. The container type is shared with other customers, " +
        "so H8 does not guess by listing it, and creates NO container until an operator checks. As the owning app (or with " +
        $"a SharePoint Embedded admin token), list the containers of container type '{containerTypeId}' and look for display " +
        $"name '{displayName}' with a description naming customer '{envelope.CustomerId}' and run '{envelope.RunId}'. If one " +
        "exists, set the run document's interStepState.speContainerCreation.rootContainerId to its id (H8 then activates, " +
        "verifies and binds THAT container), or remove it. Then clear this quarantine (POST /api/runs/{id}/clear-quarantine) " +
        "and resume: clearing it is the confirmation — without a recorded container H8 then creates one.";

    /// <summary>
    /// Binds every further container on record (<see cref="CreatedContainers.AdditionalContainerIds"/>) to the root's
    /// business unit, or removes it (the provisioner's bind-or-remove). A bound or removed one leaves the record; one that
    /// is neither stays on it and the run is quarantined naming it. Null to continue.
    /// </summary>
    private async Task<HandlerResult?> BindAdditionalContainersAsync(
        ProvisioningRun run,
        string etag,
        CreatedContainers outputs,
        HandlerEnvelope envelope,
        string tenantId,
        string owningAppId,
        Guid rootBusinessUnitId,
        CancellationToken cancellationToken)
    {
        var pending = outputs.AdditionalContainerIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && !string.Equals(id, outputs.RootContainerId, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var containerId in pending.ToList())
        {
            SpeContainerBindOutcome outcome;
            try
            {
                outcome = await _provisioner.BindRootContainerAsync(
                    new SpeContainerBindRequest(
                        CustomerId: envelope.CustomerId,
                        TenantId: tenantId,
                        OwningAppId: owningAppId,
                        ContainerId: containerId,
                        BusinessUnitId: rootBusinessUnitId),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                KeepOnRecord(pending);
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    SpeContainerRejectionCodes.ContainerBindingInfraFault,
                    $"Binding further container '{containerId}' (on the run's record next to container " +
                    $"'{outputs.RootContainerId}') to business unit '{rootBusinessUnitId}' failed before any Graph call: " +
                    $"{ex.GetType().Name}: {ex.Message}. It exists UNBOUND — a resume binds it; or bind it with " +
                    "Backfill-SpeContainerBusinessUnitStamp.ps1 -Bind, or remove it — QuarantineRequired.",
                    cancellationToken).ConfigureAwait(false);
            }

            if (outcome is SpeContainerBindOutcome.NotBound { Removed: false } notBound)
            {
                KeepOnRecord(pending);
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    SpeContainerRejectionCodes.ContainerBindingFailedNotRemoved,
                    $"Further container '{containerId}' on the run's record: {notBound.Diagnostic}",
                    cancellationToken).ConfigureAwait(false);
            }

            // Bound, or removed: nothing unbound remains of it.
            pending.Remove(containerId);
        }

        KeepOnRecord(pending);
        return null;

        void KeepOnRecord(List<string> remaining)
        {
            if (run.InterStepState.SpeContainerCreation is { } creation)
            {
                creation.AdditionalContainerIds = remaining.Count == 0 ? null : remaining.ToList();
            }
        }
    }

    /// <summary>
    /// Persists the creation record (<paramref name="update"/> — the typed <see cref="InterStepState.SpeContainerCreation"/>)
    /// and withdraws any H7 hand-off, with <see cref="CancellationToken.None"/> — something was created, so the record is
    /// written even when the caller is cancelled. On a concurrent write the record is MERGED over the current document and
    /// retried (it touches only H8's own fields) — never over another creation's record: a current document naming a root
    /// container that is neither the one this entry started from (<paramref name="expectedRootContainerId"/>) nor the one
    /// it writes is left alone. When it cannot be persisted (the run is gone, or the merges keep losing), the refusal is
    /// QuarantineRequired and names the container: it is UNBOUND and unrecorded, so an operator binds or removes it.
    /// </summary>
    private async Task<(ProvisioningRun? Run, string? ETag, HandlerResult? Refusal)> RecordCreationAsync(
        ProvisioningRun run,
        string etag,
        CreationUpdate update,
        string? expectedRootContainerId,
        HandlerEnvelope envelope)
    {
        for (var attempt = 1; attempt <= RecordMergeAttempts; attempt++)
        {
            ApplyCreationRecord(run, update);

            var replace = await _repository.ReplaceRunAsync(run, etag, CancellationToken.None).ConfigureAwait(false);
            switch (replace)
            {
                case ReplaceRunResult.Success success:
                    _logger.LogInformation(
                        "H8-B recorded its creation ({Status}): runId={RunId} rootContainerId={RootContainerId}",
                        update.Status, run.RunId, update.RootContainerId ?? "(none)");
                    return (run, success.ETag, null);

                case ReplaceRunResult.Conflict conflict:
                    var current = conflict.Current.Run;
                    if (current.CompletedPhases.Any(cp => string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)))
                    {
                        return (null, null, NotPersisted("a concurrent write completed H8"));
                    }

                    AdoptUnboundHandOff(current);
                    var currentRecord = ReadRecordedCreation(current);
                    if (currentRecord.RootContainerId is { } currentRoot
                        && !string.Equals(currentRoot, update.RootContainerId, StringComparison.Ordinal)
                        && !string.Equals(currentRoot, expectedRootContainerId, StringComparison.Ordinal))
                    {
                        // Never overwrite another creation's record — that would orphan IT.
                        return (null, null, NotPersisted($"the run already records container '{currentRoot}'"));
                    }

                    _logger.LogWarning(
                        "H8-B creation record lost a concurrent write (attempt {Attempt}) — merging over the current run: runId={RunId}",
                        attempt, run.RunId);
                    (run, etag) = (current, conflict.Current.ETag);
                    continue;

                default:
                    return (null, null, NotPersisted("the run was deleted"));
            }
        }

        return (null, null, NotPersisted($"{RecordMergeAttempts} merges over concurrent writes all lost"));

        HandlerResult NotPersisted(string reason)
        {
            var containers = new[] { update.RootContainerId }
                .Concat(update.AdditionalContainerIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
            var created = containers.Count == 0
                ? update.RootContainerInDoubtSince is not null
                    ? "may have created a container it cannot name (its creation got no answer)"
                    : "holds no container"
                : $"created container(s) '{string.Join("', '", containers)}'";
            var diagnostic =
                $"H8 {created} but could not record that in run '{envelope.RunId}' ({reason}). A container is UNBOUND — no " +
                "SPE admin route reaches it: bind it with Backfill-SpeContainerBusinessUnitStamp.ps1 -Bind " +
                "<containerId>=<rootBusinessUnitId>, or remove it, before this run is resumed.";
            _logger.LogCritical(
                "H8-B creation NOT recorded: runId={RunId} customerId={CustomerId} containers={Containers} reason={Reason}",
                envelope.RunId, envelope.CustomerId, string.Join(",", containers), reason);
            return new HandlerResult.Failure(
                FailureClass.QuarantineRequired, SpeContainerRejectionCodes.CreationRecordNotPersisted, diagnostic);
        }
    }

    /// <summary>
    /// Writes <paramref name="update"/> into the run: the typed <see cref="InterStepState.SpeContainerCreation"/>, the T6
    /// gate (Pending; its evidence is the operator's view, never read back), and withdraws the H7 hand-off.
    /// </summary>
    private static void ApplyCreationRecord(ProvisioningRun run, CreationUpdate update)
    {
        run.InterStepState.SpeContainerId = null;
        var additional = update.AdditionalContainerIds?
            .Where(id => !string.Equals(id, update.RootContainerId, StringComparison.Ordinal))
            .ToList();
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord
        {
            RootContainerId = update.RootContainerId,
            AdditionalContainerIds = additional is { Count: > 0 } ? additional : null,
            RootContainerInDoubtSince = update.RootContainerInDoubtSince,
            Status = update.Status,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        run.GateStates[SpeContainerGates.T6Verified] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
            Evidence = BuildEvidence(update.RootContainerId, update.Status, verifiedViaAppOnlyToken: false),
        };
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>What H8 has recorded (typed fields only — <see cref="ReadRecordedCreation"/>).</summary>
    internal sealed record RecordedCreation(
        string? RootContainerId,
        IReadOnlyList<string> AdditionalContainerIds,
        DateTimeOffset? RootContainerInDoubtSince,
        string? Status)
    {
        /// <summary>
        /// A recorded container that was never activated: its <c>/activate</c> failed, or an operator recorded it after a
        /// creation in doubt (whose activation was never sent).
        /// </summary>
        public bool NeedsActivation =>
            RootContainerId is not null
            && (string.Equals(Status, SpeContainerCreationRecord.StatusActivationFailed, StringComparison.Ordinal)
                || string.Equals(Status, SpeContainerCreationRecord.StatusRootContainerInDoubt, StringComparison.Ordinal));
    }

    /// <summary>The container H8 carries through verification, bind and completion (and any further ones on record).</summary>
    private sealed record CreatedContainers(string RootContainerId, IReadOnlyList<string> AdditionalContainerIds);

    /// <summary>One write of H8's creation record (<see cref="ApplyCreationRecord"/>).</summary>
    private sealed record CreationUpdate(
        string? RootContainerId,
        IReadOnlyList<string>? AdditionalContainerIds,
        DateTimeOffset? RootContainerInDoubtSince,
        string Status);

    /// <summary>
    /// Builds the gate evidence JSON. <paramref name="verifiedViaAppOnlyToken"/>
    /// is an explicit parameter so the WaitingOnGate/replication-pending case
    /// produces truthful evidence (verification has NOT happened yet), not a
    /// misleading hardcoded true. <paramref name="owningBusinessUnitId"/>: the
    /// business unit the container is stamped with (task 165); null while it is
    /// not yet bound. Evidence is the operator's record — nothing resumes from it.
    /// </summary>
    private static System.Text.Json.JsonElement BuildEvidence(
        string? containerId, string verifiedStatus, bool verifiedViaAppOnlyToken, Guid? owningBusinessUnitId = null)
    {
        var doc = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            containerId,
            verifiedStatus,
            verifiedViaAppOnlyToken,
            owningBusinessUnitId = owningBusinessUnitId?.ToString("D"),
        });
        return doc;
    }
}
