// -----------------------------------------------------------------------------
// H8SpeContainerTypeHandler.cs
//
// L2 CONTROL-PLANE H8 SPE container-type + root-container handler (task 051,
// wave C4 Batch 3E).
//
// PURPOSE:
//   Creates the customer's SPE container type + a root container within it,
//   using EXCLUSIVELY confidential-client (app-only) cert-based auth (T6 fix,
//   spec.md FR-33 — delegated tokens 403 "public client not allowed" against
//   Microsoft Graph SPE APIs). As of task 131 (Wave G-3), the three
//   collaborator seams are Microsoft.Graph 6.5.0 SDK implementations
//   (GraphContainerTypeProvisioner / GraphAppOnlyContainerVerifier /
//   SecretClientSpeContainerIdKvWriter — see those files' headers) rather
//   than the task-051 shell-out scripts; the retired script-based
//   collaborators remain on disk, unregistered, per this project's
//   established retirement pattern. This handler's own orchestration logic
//   (parameter guards, idempotency, §4C classification) is UNCHANGED by the
//   port — the seam interfaces (ISpeContainerTypeProvisioner /
//   ISpeContainerVerifier / ISpeContainerIdKvWriter) are stable across the
//   swap, plus ONE addition: ISpeContainerVerifier now has a third outcome,
//   SpeContainerVerificationResult.ReplicationPending, for the documented
//   24h SPE replication-lag case (see MarkWaitingOnGateAsync + the ROLLBACK
//   CLASSIFICATION table below).
//   Persists the real container-type id to the customer's Key Vault under the
//   canonical `SPE-ContainerTypeId` slot H4 pre-created (StaticKvSecretManifest).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-11 + FR-33 (T6)
//     + §4D I4 (tenant-scoped container-id storage, no fallback default) +
//     §4D I5 (per-tenant Graph token scoping) + NFR-09 (Graph v6/Kiota 2.0
//     error type — N/A here: H8 shells out to PS, no direct Graph SDK C# call,
//     same design choice as H3's IEntraAppRegProvisioner).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H8 row +
//     §4B T6 + §4C rollback taxonomy + line 1158 (KV secret + Dataverse
//     env-var destinations; "Never rotate; container = data").
//   - .claude/adr/ADR-004: single IJobHandler-shaped impl registered in L2 DI.
//   - .claude/adr/ADR-010: register in L2, NOT BFF.
//   - .claude/adr/ADR-028: auth ceremony — confidential-client cert loaded
//     from KV is the T6-specific MUST; cleartext secrets never traverse
//     handler code (cert bootstrap + JWT signing happen entirely inside the
//     PS script's process boundary, same as H3/H4's KV-secret handling).
//   - .claude/adr/ADR-036: reuse background-job infrastructure; fire-and-forget.
//   - .claude/adr/ADR-044: containerTypeId written to Cosmos interStepState
//     follows GUID canonicalization (script already emits canonical-form
//     GUIDs from Graph responses).
//
// BUSINESS-UNIT STAMP (unified-access-control-r2 task 165, owner round 35 item 1,
// 2026-10-04 — supersedes the "H8 runs before H5/H6" premise of deviation (1)
// below for the DAG): every SPE container is stamped with its owning business
// unit at creation, and the BFF's admin plane reaches NO unbound container. H8
// now depends on H3 AND H5 (DagAdvancer.HandlerDependencies), reads the new
// environment's ROOT business unit before creating anything
// (IDataverseRootBusinessUnitReader; missing/unreadable -> Resumable, no side
// effect), and after verification binds the root container to it
// (ISpeContainerTypeProvisioner.BindRootContainerAsync: stamp, read back,
// DELETE on failure). A bind failure is QuarantineRequired
// (ContainerBindingFailed / ...NotRemoved / ContainerBindingInfraFault) and the
// container id is never handed to H7. Recorded for this project in
// projects/customer-provisioning-orchestration-r1/notes/uac-r2-165-h8-container-stamp.md.
//
// RESUME (task 165, owner rounds 41 + 49 — every creation path stamps or
// removes, the replication-pending path included): H8 RECORDS what it created
// immediately (6b) in TYPED fields of the run — InterStepState.ContainerTypeId
// + InterStepState.SpeContainerCreation (the root container, further containers
// to bind, creations in doubt). Round 41 kept the root container in the T6
// gate's JsonElement evidence, which the Cosmos SDK's Newtonsoft serializer
// wrote as {"valueKind":1}: every re-entry then created a second root container
// (owner round 49 item 2). A re-entry that finds the record (5c,
// ReadRecordedCreation) RESUMES at verification with that container: it never
// calls ProvisionAsync for a recorded root container. When only the type is
// recorded, the provisioner lists it and ADOPTS a container already in it (a
// creation whose answer was lost) before creating one, and creates none while
// an unanswered root-container POST may still appear (WaitingOnGate). A
// container-type POST with no answer is QuarantineRequired — a type may exist
// unnamed and cannot be found app-only or deleted. The H7 hand-off
// (InterStepState.SpeContainerId) is written ONLY by MarkCompleteAsync, after
// the bind (of the root and every adopted container) and the KV write, and H7
// depends on H8 in the DAG — so H7 never consumes an unbound container. The L2
// tests persist runs through the production serializer
// (CosmosModule.BuildCosmosClient), so a record that does not survive Cosmos
// fails them.
//
// DEVIATION FROM POML LITERAL WORDING (documented per CLAUDE.md §6.5 — Path C
// pivot-to-comply, discovered during implementation; see
// projects/customer-provisioning-orchestration-r1/notes/task-051-h8-deviations.md
// for the full writeup):
//   (1) "Root container" is created via Create-NewContainerType.ps1's own
//       -CreateTestContainer switch (Graph-only, no Dataverse dependency)
//       rather than New-BusinessUnitContainer.ps1 (which requires an EXISTING
//       Dataverse business-unit row — design.md's handler DAG places H8
//       BEFORE H5/H6, so no customer Dataverse environment exists yet).
//   (2) "Persist to Dataverse env-var sprk_SharePointEmbeddedContainerId" is
//       satisfied via Cosmos InterStepState.SpeContainerId (a controlled
//       schema extension landed by sibling task 050/H7 specifically for this
//       handoff — see InterStepState.cs's SpeContainerId doc comment) rather
//       than a direct Dataverse write. H7DataverseEnvVarValuesHandler.cs
//       (task 050, already landed) reads InterStepState.SpeContainerId as the
//       source value for sprk_SharePointEmbeddedContainerId and fails
//       Resumable/MissingUpstreamState if it is absent — H8 MUST populate it.
//       H8 cannot write the live Dataverse environmentvariablevalue record
//       directly — the target Dataverse environment does not exist yet at
//       H8's point in the DAG (H8 runs before H5/H6 per the handler DAG).
//   (3) The KV secret name used is the §7.9 CANONICAL `SPE-ContainerTypeId`
//       (matching the slot H4's KvSecretsPopulation manifest already
//       pre-creates — StaticKvSecretManifest.cs), not the POML's literal
//       `customer-{customerId}-spe-container-id` — design.md itself carries
//       both names (line 741 §7.7 vs line 1158/StaticKvSecretManifest §7.9);
//       the more recently-reconciled §7.9 canonical name is authoritative and
//       reuses an already-established slot rather than creating a duplicate.
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌───────────────────────────────────────────┬──────────────────────────┐
//   │ Failure mode                               │ §4C class                │
//   ├───────────────────────────────────────────┼──────────────────────────┤
//   │ 24h SPE replication lag (verify GET 404    │ NOT a §4C failure class  │
//   │ on a just-created container; task 131)     │ — RunStatus.WaitingOnGate│
//   │                                             │ (session-free run-level  │
//   │                                             │ pause; DS-4 §2 / this    │
//   │                                             │ project's CLAUDE.md MUST │
//   │                                             │ rule)                    │
//   │ Missing tenantId / keyVaultName /          │ Resumable                │
//   │ subscriptionId / sharePointDomain /        │ (external precondition — │
//   │ owningAppId (H3 not yet complete)          │ operator fixes + resumes)│
//   │ Run not found in Cosmos partition          │ Resumable                │
//   │ Provisioner hard failure (non-T6)          │ Resumable                │
//   │ Provisioner infra fault (no side effect)   │ Resumable                │
//   │ Provisioner outputs incomplete             │ Resumable                │
//   │ T6 trap detected (creation OR verification)│ QuarantineRequired       │
//   │                                            │ (the exact failure T6    │
//   │                                            │ exists to catch)         │
//   │ Verification NotVerified (non-T6)          │ QuarantineRequired       │
//   │                                            │ (created, unverified)    │
//   │ Verifier infra fault                       │ QuarantineRequired       │
//   │ KV write Failure                           │ QuarantineRequired       │
//   │                                            │ (created+verified, not   │
//   │                                            │ persisted)               │
//   │ KV writer infra fault                      │ QuarantineRequired       │
//   │ Concurrent Cosmos writer conflict          │ Resumable                │
//   │ Run row deleted mid-flight                 │ Resumable                │
//   │ Recorded root container without a type     │ QuarantineRequired       │
//   │ (task 165 round 41; nothing created)       │                          │
//   │ Creation not recordable (run deleted /     │ QuarantineRequired       │
//   │ merges lost; ids in the diagnostic)        │                          │
//   │ Container-type POST with no answer (the    │ QuarantineRequired       │
//   │ type may exist unnamed; round 49)          │ (until an operator clears│
//   │                                            │ after checking)          │
//   │ Root-container POST with no answer         │ Resumable (recorded; the │
//   │                                            │ resume lists + adopts)   │
//   │ Root creation in doubt, type lists none,   │ NOT a §4C failure class  │
//   │ within the replication window              │ — RunStatus.WaitingOnGate│
//   └───────────────────────────────────────────┴──────────────────────────┘
//
// IDEMPOTENCY (3-level per ADR-004 / design.md §4.1):
//   Level 1 (Service Bus MessageId dedup): reconciler-owned, deterministic
//           MessageId per (HandlerId, RunId, CustomerId, paramHash).
//   Level 2 (Redis IdempotencyService): NOT YET IMPLEMENTED in L2 (parity
//           with sibling wave-C4 handlers).
//   Level 3 (handler body durable dedup): scans
//           ProvisioningRun.CompletedPhases for (Phase=="H8",
//           IdempotencyKey=="spe-{customerId}") — per POML constraint the key
//           is customerId-only (container-type is per-customer + VERSION-
//           INDEPENDENT, unlike H4's secretsVer-suffixed key). Match ⇒
//           Success no-op BEFORE any external side effect — this is also the
//           mechanism that enforces "never re-create a container-type for a
//           customer that already has one" (design.md line 1158 "container =
//           data").
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   H8 lives in L2 (not BFF) per spec §5.2 / D3 / D8 / D12; consumes NO
//   AI-internal types. Uses IProvisioningRunRepository (task 037) + three
//   dedicated seams (ISpeContainerTypeProvisioner, ISpeContainerVerifier,
//   ISpeContainerIdKvWriter); no BFF-facade dependencies.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H8SpeContainerTypeHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H8;

    /// <summary>Non-secret parameter key carrying the customer Entra tenant id (§4D I1/I5).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the target Key Vault name (§4D I4 tenant-scoped vault).</summary>
    public const string KeyVaultNameParameterKey = "keyVaultName";

    /// <summary>Non-secret parameter key carrying the target subscription id (`az` CLI scoping on the KV write).</summary>
    public const string SubscriptionIdParameterKey = "subscriptionId";

    /// <summary>Non-secret parameter key carrying the customer SharePoint domain (e.g. <c>acme.sharepoint.com</c>).</summary>
    public const string SharePointDomainParameterKey = "sharePointDomain";

    /// <summary>Non-secret parameter key carrying the KV secret name holding the base64 PFX SPE owner cert. Optional — defaults to <see cref="SpeContainerTypeOptions.DefaultCertSecretName"/>.</summary>
    public const string CertSecretNameParameterKey = "speCertSecretName";

    /// <summary>Non-secret parameter key carrying the container-type display name. Optional — defaults to <see cref="SpeContainerTypeOptions.DefaultDisplayName"/>.</summary>
    public const string DisplayNameParameterKey = "speContainerTypeDisplayName";

    /// <summary>
    /// Non-secret parameter key carrying an ISO-8601 timestamp for
    /// <c>sprk_dataverseenvironment.sprk_provisionedon</c>. When present +
    /// non-empty, H8's KV write runs in upgrade mode (never-rotate; container
    /// = data). Parity with H4's <c>ProvisionedOnParameterKey</c>.
    /// </summary>
    public const string ProvisionedOnParameterKey = "provisionedOn";

    private readonly IProvisioningRunRepository _repository;
    private readonly ISpeContainerTypeProvisioner _provisioner;
    private readonly ISpeContainerVerifier _verifier;
    private readonly ISpeContainerIdKvWriter _kvWriter;
    private readonly IDataverseRootBusinessUnitReader _rootBusinessUnitReader;
    private readonly SpeContainerTypeOptions _options;
    private readonly ILogger<H8SpeContainerTypeHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H8 SPE container-type handler. All collaborators are
    /// interface-abstracted so unit tests can substitute stubs for each seam.
    /// </summary>
    public H8SpeContainerTypeHandler(
        IProvisioningRunRepository repository,
        ISpeContainerTypeProvisioner provisioner,
        ISpeContainerVerifier verifier,
        ISpeContainerIdKvWriter kvWriter,
        IDataverseRootBusinessUnitReader rootBusinessUnitReader,
        IOptions<SpeContainerTypeOptions> options,
        ILogger<H8SpeContainerTypeHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(provisioner);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(kvWriter);
        ArgumentNullException.ThrowIfNull(rootBusinessUnitReader);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _provisioner = provisioner;
        _verifier = verifier;
        _kvWriter = kvWriter;
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
                $"H8SpeContainerTypeHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H8 SPE container-type provisioning starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H8 aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SpeContainerTypeRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;

        // (2) Parameter guards — every field H8 needs must be non-empty
        //     BEFORE any external side effect (§4C Resumable classification).
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerTypeRejectionCodes.MissingTenantId,
                "Run parameter 'tenantId' is required by H8 (§4D I1/I5 no-hardcoded-tenant). " +
                "Upstream handler MUST populate this before H8 dispatches.",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, KeyVaultNameParameterKey, out var keyVaultName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerTypeRejectionCodes.MissingKeyVaultName,
                "Run parameter 'keyVaultName' is required by H8 (§4D I4 tenant-scoped KV target for " +
                "cert bootstrap + the SPE-ContainerTypeId secret).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, SubscriptionIdParameterKey, out var subscriptionId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerTypeRejectionCodes.MissingSubscriptionId,
                "Run parameter 'subscriptionId' is required by H8 (`az` CLI --subscription scoping on the KV write).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, SharePointDomainParameterKey, out var sharePointDomain))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerTypeRejectionCodes.MissingSharePointDomain,
                "Run parameter 'sharePointDomain' is required by H8 (Create-NewContainerType.ps1's " +
                "mandatory -SharePointDomain for the SharePoint-side application-permission registration call).",
                cancellationToken).ConfigureAwait(false);
        }

        // (3) H3 prerequisite guard — the owning app id comes from H3's
        //     InterStepState output, not a run parameter. H8 owns NO fallback
        //     path to create/discover the app registration itself.
        var owningAppId = run.InterStepState.BffAppRegId;
        if (string.IsNullOrWhiteSpace(owningAppId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerTypeRejectionCodes.MissingOwningAppId,
                "InterStepState.BffAppRegId is empty — H3 (Entra app-reg) MUST complete before H8 " +
                "dispatches (design.md §4.1 DAG: H4 -> H3 -> { H8, H9 }).",
                cancellationToken).ConfigureAwait(false);
        }

        var certSecretName = TryGetNonEmpty(parameters, CertSecretNameParameterKey, out var certSecretRaw)
            ? certSecretRaw
            : _options.DefaultCertSecretName;
        var displayName = TryGetNonEmpty(parameters, DisplayNameParameterKey, out var displayNameRaw)
            ? displayNameRaw
            : _options.DefaultDisplayName;
        var upgradeMode = TryGetNonEmpty(parameters, ProvisionedOnParameterKey, out var provisionedOnRaw)
            && !string.IsNullOrWhiteSpace(provisionedOnRaw);

        // (4) Idempotency key — per POML constraint: customerId-only (version-
        //     independent; a container-type is durable customer data, never
        //     re-created for a repeat/upgrade run).
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId);

        // (5) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H8 idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (5a) InterStepState.SpeContainerId is H7's hand-off and is written ONLY by MarkCompleteAsync, after the bind (owner
        //      round 41 item 1). H8 is not complete (5), so a value here predates the bind: the pre-round-41
        //      replication-pending path wrote the UNBOUND root container's id there. That path ALSO named it in the T6 gate's
        //      evidence, but evidence never survived Cosmos' Newtonsoft serializer ({"valueKind":1} — owner round 49 item
        //      2), so this field is the only place such a run records its root container. It is MOVED into H8's typed
        //      creation record (InterStepState.SpeContainerCreation) and withdrawn before ANY write this entry makes — so no
        //      persisted state of an incomplete H8 hands a container to H7, and the container is resumed, not orphaned.
        AdoptPreRound41HandOff(run);

        // (5b) The root container's owner (unified-access-control-r2 task 165, owner round 35 item 1): every SPE
        //      container is stamped with its owning business unit at creation — here the ROOT business unit of the
        //      customer's Dataverse environment (H5 output; under D-12 the environment is the customer's own). Resolved
        //      BEFORE any external side effect: H8 never creates a container it could not bind. H8 runs after H5
        //      (DagAdvancer.HandlerDependencies), so a missing URL is an upstream defect — Resumable.
        var dataverseEnvUrl = run.InterStepState.DataverseEnvUrl;
        if (string.IsNullOrWhiteSpace(dataverseEnvUrl))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerTypeRejectionCodes.MissingDataverseEnvUrl,
                "InterStepState.DataverseEnvUrl is empty — H5 (Dataverse environment) MUST complete before H8: the root " +
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
                    SpeContainerTypeRejectionCodes.RootBusinessUnitUnresolved,
                    $"Dataverse environment '{dataverseEnvUrl}' reports no root business unit — the root container " +
                    "would have no owner, so none is created.",
                    cancellationToken).ConfigureAwait(false);
            }

            rootBusinessUnitId = resolved;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H8 could not read the root business unit: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerTypeRejectionCodes.RootBusinessUnitUnresolved,
                $"Reading the root business unit of '{dataverseEnvUrl}' failed: {ex.GetType().Name}: {ex.Message}. " +
                "No container was created — Resumable.",
                cancellationToken).ConfigureAwait(false);
        }

        // (5c) What THIS run's H8 already created (unified-access-control-r2 task 165, owner rounds 41 + 49) — read from the
        //      run's TYPED creation record, never from gate evidence. H8 is not complete (5), so a recorded container type /
        //      root container is one H8 made on an earlier entry that stopped before completing: the 24h replication wait
        //      (7b), a quarantined verification, bind or KV step, a fault with no answer, or a crash. A re-entry RESUMES
        //      with it — it never calls ProvisionAsync for a recorded root container (that created a second container type
        //      and root container on every resume and orphaned the first one UNBOUND).
        var recorded = ReadRecordedCreation(run);
        if (recorded.Inconsistent)
        {
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SpeContainerTypeRejectionCodes.CreationRecordInconsistent,
                "The run's H8 creation record (interStepState.speContainerCreation) names root container(s) " +
                $"'{string.Join("', '", recorded.AllContainerIds)}' or an unanswered root-container creation, but no container " +
                "type (interStepState.containerTypeId) — H8 never writes that state. Nothing was created: an operator must " +
                "establish which container type they belong to (or remove them) and correct the run before H8 resumes.",
                cancellationToken).ConfigureAwait(false);
        }

        // A container type that is now known withdraws an earlier "type in doubt" (an operator recorded it).
        if (recorded.ContainerTypeId is not null && run.InterStepState.SpeContainerCreation is { ContainerTypeInDoubtSince: not null } known)
        {
            known.ContainerTypeInDoubtSince = null;
        }

        // (5d) A container-type POST of this run got no authoritative answer (owner round 49 item 2): a type may exist that
        //      no one names, and a container type can neither be deleted nor be found app-only. H8 creates NO type until an
        //      operator has checked with a delegated SharePoint Embedded admin token and cleared the quarantine below.
        if (recorded.ContainerTypeId is null && recorded.ContainerTypeInDoubtSince is { } typeInDoubtSince)
        {
            if (!QuarantineClearedSince(run, typeInDoubtSince))
            {
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    SpeContainerTypeRejectionCodes.ContainerTypeCreationInDoubt,
                    ContainerTypeInDoubtDiagnostic(envelope, owningAppId, typeInDoubtSince),
                    cancellationToken).ConfigureAwait(false);
            }

            // The operator cleared H8's in-doubt quarantine after checking (a type they found would now be recorded in
            // interStepState.containerTypeId). The marker is withdrawn and a type is created below.
            _logger.LogWarning(
                "H8 container-type creation in doubt since {Since} was cleared by an operator — creating the type: runId={RunId}",
                typeInDoubtSince, envelope.RunId);
            run.InterStepState.SpeContainerCreation!.ContainerTypeInDoubtSince = null;
        }

        SpeContainerTypeProvisionOutputs outputs;
        if (recorded.RootContainerId is { } recordedRoot)
        {
            outputs = new SpeContainerTypeProvisionOutputs(recorded.ContainerTypeId!, recordedRoot, recorded.AdditionalContainerIds);
            _logger.LogInformation(
                "H8 resumes with the container type {ContainerTypeId} and root container {RootContainerId} it already " +
                "created — nothing is created: runId={RunId} customerId={CustomerId}",
                outputs.ContainerTypeId, outputs.RootContainerId, envelope.RunId, envelope.CustomerId);
        }
        else
        {
            // (6) Invoke the provisioner (container-type + root container, T6
            //     confidential-client cert-based) — or, when this run already
            //     created the container type, ONLY a root container in it: one
            //     the type already holds is ADOPTED (owner round 49 item 2), and
            //     none is created while an unanswered root-container POST may
            //     still appear (the replication window). The provisioner returns
            //     every fault after a Graph write as a Failure saying what may
            //     exist; it throws only before any Graph call (cert load).
            var rootContainerInDoubt = recorded.RootContainerInDoubtSince is { } rootInDoubtSince
                                       && DateTimeOffset.UtcNow - rootInDoubtSince < RootContainerInDoubtWindow;
            SpeContainerTypeProvisionOutcome provisionOutcome;
            try
            {
                var provisionRequest = new SpeContainerTypeProvisionRequest(
                    CustomerId: envelope.CustomerId,
                    TenantId: tenantId,
                    OwningAppId: owningAppId,
                    SharePointDomain: sharePointDomain,
                    VaultName: keyVaultName,
                    CertSecretName: certSecretName,
                    DisplayName: displayName,
                    ExistingContainerTypeId: recorded.ContainerTypeId,
                    RootContainerCreationInDoubt: rootContainerInDoubt);
                provisionOutcome = await _provisioner.ProvisionAsync(provisionRequest, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "H8 provisioner infrastructure fault: runId={RunId} customerId={CustomerId}",
                    envelope.RunId, envelope.CustomerId);
                return await FailAsync(run, etag, FailureClass.Resumable,
                    SpeContainerTypeRejectionCodes.ProvisioningInfraFault,
                    $"SPE container-type provisioner infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                    "Raised before any Graph write (the provisioner returns every later fault as a Failure) — Resumable.",
                    cancellationToken).ConfigureAwait(false);
            }

            if (provisionOutcome is SpeContainerTypeProvisionOutcome.Failure provisionFailure)
            {
                // Record what may exist BEFORE anything else (owner round 49 item 2): the type it created (a resume creates
                // only a root container in it), a type that may exist unnamed, a root container that may exist unseen.
                var createdType = string.IsNullOrWhiteSpace(provisionFailure.CreatedContainerTypeId)
                    ? recorded.ContainerTypeId
                    : provisionFailure.CreatedContainerTypeId.Trim();
                if (createdType is not null || provisionFailure.ContainerTypeInDoubt || provisionFailure.RootContainerInDoubt)
                {
                    var now = DateTimeOffset.UtcNow;
                    var failureRecord = await RecordCreationAsync(run, etag, new CreationUpdate(
                            ContainerTypeId: createdType,
                            RootContainerId: null,
                            AdditionalContainerIds: null,
                            ContainerTypeInDoubtSince: provisionFailure.ContainerTypeInDoubt ? now : null,
                            RootContainerInDoubtSince: provisionFailure.RootContainerInDoubt ? now : recorded.RootContainerInDoubtSince,
                            Status: provisionFailure.ContainerTypeInDoubt
                                ? SpeContainerCreationRecord.StatusContainerTypeInDoubt
                                : provisionFailure.RootContainerInDoubt
                                    ? SpeContainerCreationRecord.StatusRootContainerInDoubt
                                    : SpeContainerCreationRecord.StatusTypeOnly),
                        expectedRootContainerId: null, envelope)
                        .ConfigureAwait(false);
                    if (failureRecord.Refusal is { } failureRefusal)
                    {
                        return failureRefusal;
                    }

                    (run, etag) = (failureRecord.Run!, failureRecord.ETag!);
                }

                if (provisionFailure.ContainerTypeInDoubt)
                {
                    return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                        SpeContainerTypeRejectionCodes.ContainerTypeCreationInDoubt,
                        provisionFailure.Diagnostic + " " +
                        ContainerTypeInDoubtDiagnostic(envelope, owningAppId, run.InterStepState.SpeContainerCreation?.ContainerTypeInDoubtSince),
                        cancellationToken).ConfigureAwait(false);
                }

                if (provisionFailure.IsDelegatedTokenTrap)
                {
                    return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                        SpeContainerTypeRejectionCodes.TrapT6DelegatedTokenDetected, provisionFailure.Diagnostic,
                        cancellationToken).ConfigureAwait(false);
                }
                return await FailAsync(run, etag, FailureClass.Resumable,
                    SpeContainerTypeRejectionCodes.ProvisioningFailed,
                    provisionFailure.Diagnostic + (provisionFailure.RootContainerInDoubt
                        ? " Recorded: the resume lists the container type and adopts the container if it was created."
                        : string.Empty),
                    cancellationToken).ConfigureAwait(false);
            }

            if (provisionOutcome is SpeContainerTypeProvisionOutcome.RootContainerNotYetVisible notYetVisible)
            {
                // An earlier root-container POST got no answer and its type lists no container yet: creating another now
                // could leave the first one unbound when it appears. Wait (a run-level pause, like the replication wait);
                // the next entry lists again, and creates only once the window has passed.
                return await MarkWaitingOnGateAsync(run, etag, new CreationUpdate(
                        ContainerTypeId: notYetVisible.ContainerTypeId,
                        RootContainerId: null,
                        AdditionalContainerIds: null,
                        ContainerTypeInDoubtSince: null,
                        RootContainerInDoubtSince: recorded.RootContainerInDoubtSince,
                        Status: SpeContainerCreationRecord.StatusRootContainerInDoubt),
                    notYetVisible.Diagnostic, cancellationToken).ConfigureAwait(false);
            }

            outputs = ((SpeContainerTypeProvisionOutcome.Success)provisionOutcome).Outputs;
            if (string.IsNullOrWhiteSpace(outputs.ContainerTypeId) || string.IsNullOrWhiteSpace(outputs.RootContainerId))
            {
                return await FailAsync(run, etag, FailureClass.Resumable,
                    SpeContainerTypeRejectionCodes.ProvisioningOutputsIncomplete,
                    "SPE container-type provisioner returned incomplete outputs — one of ContainerTypeId / " +
                    "RootContainerId is blank.",
                    cancellationToken).ConfigureAwait(false);
            }

            // (6b) RECORD the creation in the run NOW — before verification, the bind or anything else — so no later
            //      failure, wait, crash or lost write can make a re-entry create a second one (owner rounds 41 + 49). The
            //      record is H8's own and TYPED: InterStepState.ContainerTypeId + InterStepState.SpeContainerCreation
            //      (the root container, and any further containers adopted with it). It is NOT the H7 hand-off
            //      (SpeContainerId), which only a bound container ever reaches.
            var record = await RecordCreationAsync(run, etag, new CreationUpdate(
                    ContainerTypeId: outputs.ContainerTypeId,
                    RootContainerId: outputs.RootContainerId,
                    AdditionalContainerIds: outputs.AdditionalContainerIds,
                    ContainerTypeInDoubtSince: null,
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

        // (7) T6 post-condition: verify the root container is readable via a
        //     FRESH app-only token. Container now EXISTS — any failure past
        //     this point is QuarantineRequired (external resource created,
        //     not fully confirmed/persisted).
        SpeContainerVerificationResult verifyResult;
        try
        {
            var verifyRequest = new SpeContainerVerificationRequest(
                ContainerId: outputs.RootContainerId,
                OwningAppId: owningAppId,
                TenantId: tenantId,
                VaultName: keyVaultName,
                CertSecretName: certSecretName);
            verifyResult = await _verifier.VerifyAsync(verifyRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H8 verifier infrastructure fault: runId={RunId} customerId={CustomerId} rootContainerId={RootContainerId}",
                envelope.RunId, envelope.CustomerId, outputs.RootContainerId);
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SpeContainerTypeRejectionCodes.VerificationInfraFault,
                $"Post-creation app-only GET verification infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                $"Container '{outputs.RootContainerId}' was created but its readability via app-only token " +
                "could not be confirmed — QuarantineRequired.",
                cancellationToken).ConfigureAwait(false);
        }

        if (verifyResult is SpeContainerVerificationResult.NotVerified notVerified)
        {
            var rejectionCode = notVerified.IsDelegatedTokenTrap
                ? SpeContainerTypeRejectionCodes.TrapT6DelegatedTokenDetected
                : SpeContainerTypeRejectionCodes.ContainerGetVerificationFailed;
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                rejectionCode, notVerified.Diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (7b) DS-4 §2 / this project's CLAUDE.md MUST rules: the up-to-24h
        // SPE container-type replication window is a RUN-LEVEL external
        // blocker, not a handler defect. The container-type + root container
        // DO exist (real, durable side effects) and are already RECORDED (6b),
        // so the later re-entry RESUMES with them at (7) — it creates nothing
        // (owner round 41 item 1). Do NOT write the KV secret yet (that still
        // waits on Verified, unchanged ordering), do NOT bind (the container
        // may be unaddressable), do NOT hand the container to H7, and do NOT
        // record a CompletedPhase. RunStatus.WaitingOnGate is a session-free
        // pause; H7 depends on H8 (DagAdvancer.HandlerDependencies), so it waits
        // for the bound container — H9 and the other branches still advance.
        if (verifyResult is SpeContainerVerificationResult.ReplicationPending pending)
        {
            return await MarkWaitingOnGateAsync(run, etag, new CreationUpdate(
                    ContainerTypeId: outputs.ContainerTypeId,
                    RootContainerId: outputs.RootContainerId,
                    AdditionalContainerIds: outputs.AdditionalContainerIds,
                    ContainerTypeInDoubtSince: null,
                    RootContainerInDoubtSince: null,
                    Status: SpeContainerCreationRecord.StatusReplicationPending),
                pending.Diagnostic, cancellationToken).ConfigureAwait(false);
        }

        var verified = (SpeContainerVerificationResult.Verified)verifyResult;

        // (7c) Bind the verified root container to its owning business unit (task 165, owner round 35 item 1): stamp,
        //      read back, and REMOVE the container when the stamp did not land — no unbound container is handed to H7
        //      or left behind. Done after verification because an SPE container may be unaddressable for up to 24h
        //      after creation (7b): binding earlier would remove healthy containers during that documented window.
        SpeContainerBindOutcome bindOutcome;
        try
        {
            bindOutcome = await _provisioner.BindRootContainerAsync(
                new SpeContainerBindRequest(
                    CustomerId: envelope.CustomerId,
                    TenantId: tenantId,
                    OwningAppId: owningAppId,
                    VaultName: keyVaultName,
                    CertSecretName: certSecretName,
                    ContainerId: outputs.RootContainerId,
                    BusinessUnitId: rootBusinessUnitId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H8 bind infrastructure fault: runId={RunId} customerId={CustomerId} rootContainerId={RootContainerId}",
                envelope.RunId, envelope.CustomerId, outputs.RootContainerId);
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SpeContainerTypeRejectionCodes.ContainerBindingInfraFault,
                $"Binding root container '{outputs.RootContainerId}' to business unit '{rootBusinessUnitId}' failed before " +
                $"any Graph call: {ex.GetType().Name}: {ex.Message}. The container exists UNBOUND (no SPE admin route reaches " +
                "it) — bind it with Backfill-SpeContainerBusinessUnitStamp.ps1 -Bind, or remove it — QuarantineRequired.",
                cancellationToken).ConfigureAwait(false);
        }

        if (bindOutcome is SpeContainerBindOutcome.NotBound notBound)
        {
            if (notBound.Removed)
            {
                // The recorded root container no longer exists. Keep the (undeletable) container type on record and
                // drop the container, so a resume never verifies a deleted container (a 404 reads as the replication wait)
                // and never makes a second type: it lists the type and adopts a container still in it (an additional one),
                // or creates ONLY a new root container. Persisted with the merge-safe record write — a lost write here would
                // leave the deleted container on record.
                var removal = await RecordCreationAsync(run, etag, new CreationUpdate(
                        ContainerTypeId: outputs.ContainerTypeId,
                        RootContainerId: null,
                        AdditionalContainerIds: outputs.AdditionalContainerIds,
                        ContainerTypeInDoubtSince: null,
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
                    ? SpeContainerTypeRejectionCodes.ContainerBindingFailed
                    : SpeContainerTypeRejectionCodes.ContainerBindingFailedNotRemoved,
                notBound.Diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (7d) Further containers adopted with the root (a repeated creation in the run's own type — owner round 49 item 2):
        //      each is bound to the same business unit, or removed, before anything durable consumes the root — none is
        //      left unbound. One that is neither bound nor removed stays on record and the run is quarantined naming it.
        var additionalRefusal = await BindAdditionalContainersAsync(
                run, etag, outputs, envelope, tenantId, owningAppId, keyVaultName, certSecretName, rootBusinessUnitId,
                cancellationToken)
            .ConfigureAwait(false);
        if (additionalRefusal is not null)
        {
            return additionalRefusal;
        }

        // (8) Persist the real container-type id to the customer KV (the slot
        //     H4 pre-created with a placeholder — StaticKvSecretManifest.cs).
        SpeContainerIdKvWriteResult kvResult;
        try
        {
            var kvRequest = new SpeContainerIdKvWriteRequest(
                CustomerId: envelope.CustomerId,
                TargetKeyVaultName: keyVaultName,
                SubscriptionId: subscriptionId,
                ContainerTypeId: outputs.ContainerTypeId,
                UpgradeMode: upgradeMode);
            kvResult = await _kvWriter.WriteAsync(kvRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H8 KV writer infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SpeContainerTypeRejectionCodes.KvWriteInfraFault,
                $"KV writer infrastructure error persisting SPE-ContainerTypeId: {ex.GetType().Name}: {ex.Message}. " +
                "Container was created + verified but not persisted to KV — QuarantineRequired.",
                cancellationToken).ConfigureAwait(false);
        }

        if (kvResult is SpeContainerIdKvWriteResult.Failure kvFailure)
        {
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SpeContainerTypeRejectionCodes.KvWriteFailed, kvFailure.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }
        // Wrote OR SkippedAlreadyPresent both proceed to Success.

        // (9) Advance Cosmos state — write InterStepState.ContainerTypeId (the
        //     durable handoff H7 will read to materialize the real Dataverse
        //     env-var, per design.md §10.3 "Set by H7" + deviation note above),
        //     the T6-verified gate, and the CompletedPhase entry.
        stopwatch.Stop();
        _logger.LogInformation(
            "H8 SPE container-type provisioning succeeded: runId={RunId} customerId={CustomerId} " +
            "containerTypeId={ContainerTypeId} rootContainerId={RootContainerId} verifiedStatus={Status} " +
            "kvAction={KvAction} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, outputs.ContainerTypeId, outputs.RootContainerId,
            verified.Status, kvResult.GetType().Name, stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(run, etag, idempotencyKey, outputs, verified, rootBusinessUnitId, envelope, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H8 idempotency key: <c>spe-{customerId}</c>.
    /// Exposed internal so unit tests can construct expected keys without
    /// duplicating the format.
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
                "H8 failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H8 failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    /// <summary>
    /// Records a run-level external wait: the 24h SPE replication lag (verification answered 404), or a root-container
    /// creation in doubt whose container is not listed yet. Sets <see cref="RunStatus.WaitingOnGate"/> (never
    /// Resumable/QuarantineRequired per this project's CLAUDE.md MUST rules), keeps everything H8 created ON RECORD in the
    /// typed creation record (<paramref name="update"/> — InterStepState.ContainerTypeId + SpeContainerCreation), and
    /// marks the T6 gate Pending rather than Verified. Does NOT append a CompletedPhase and does NOT hand the container to
    /// H7 (InterStepState.SpeContainerId stays empty — an unbound container is never handed off; owner round 41 item 1).
    /// A subsequent re-entry finds the record (ReadRecordedCreation) and RESUMES with it: a recorded root container is
    /// verified again (then bound, the KV write, completion); a type with a creation in doubt is listed again.
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
            "H8 SPE container-type provisioning WaitingOnGate ({Status}): runId={RunId} " +
            "customerId={CustomerId} containerTypeId={ContainerTypeId} rootContainerId={RootContainerId} " +
            "diagnostic={Diagnostic}",
            update.Status, run.RunId, run.CustomerId, update.ContainerTypeId, update.RootContainerId ?? "(none)", diagnostic);

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H8 WaitingOnGate state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H8 WaitingOnGate state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        // Success: H8 correctly identified + recorded the external wait — this
        // is not an operator-actionable failure. HandlerOutcomeApplier does
        // NOT overwrite run.Status on the Success path (it reads run.Status
        // as-is — see HandlerOutcomeApplier.cs's "Success path" comment), so
        // the WaitingOnGate write above is preserved.
        return new HandlerResult.Success(BuildIdempotencyKey(run.CustomerId));
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        SpeContainerTypeProvisionOutputs outputs,
        SpeContainerVerificationResult.Verified verified,
        Guid owningBusinessUnitId,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        run.InterStepState.ContainerTypeId = outputs.ContainerTypeId;
        // H7 (task 050, already landed) reads SpeContainerId as the source
        // value for Dataverse env-var sprk_SharePointEmbeddedContainerId
        // (H7DataverseEnvVarValuesHandler.cs guard at "speContainerId not
        // present") — this is the deviation-(2) handoff mechanism documented
        // in this file's header.
        run.InterStepState.SpeContainerId = outputs.RootContainerId;
        // The typed record says what H8 finished with (live gate (d) reads it): the root container, bound to this unit.
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord
        {
            RootContainerId = outputs.RootContainerId,
            OwningBusinessUnitId = owningBusinessUnitId.ToString("D"),
            Status = SpeContainerCreationRecord.StatusBound,
            UpdatedAt = completedAt,
        };
        run.GateStates[SpeContainerTypeGates.T6Verified] = new GateEntry
        {
            Status = GateState.Verified,
            VerifiedAt = completedAt,
            VerifierHandler = HandlerIdentifier,
            Evidence = BuildEvidence(outputs.RootContainerId, verified.Status, verifiedViaAppOnlyToken: true, owningBusinessUnitId),
        };

        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier; // Reconciler observes + fans out (e.g. H9 in parallel per DAG).
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
                "H8 success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SpeContainerTypeRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H8 read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H8.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H8 success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SpeContainerTypeRejectionCodes.RunDeletedDuringProvisioning,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H8 was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }

    /// <summary>
    /// How long an unanswered root-container POST may still produce a container its type does not list yet — the SPE
    /// replication window (design.md §4.1 H8 row: up to 24h). Within it H8 creates no further root container while its
    /// type lists none (owner round 49 item 2).
    /// </summary>
    internal static readonly TimeSpan RootContainerInDoubtWindow = TimeSpan.FromHours(24);

    /// <summary>Conflicting writes a creation record is merged over before H8 gives up (operator writes only — dispatch is session-serialized per customer).</summary>
    private const int RecordMergeAttempts = 5;

    /// <summary>
    /// What this run's H8 already created and has not completed (unified-access-control-r2 task 165, owner rounds 41 +
    /// 49), read ONLY from typed fields — never from gate evidence: <see cref="InterStepState.ContainerTypeId"/> (H8 is its
    /// only writer) and <see cref="InterStepState.SpeContainerCreation"/>. A pre-round-41 run's root container is moved
    /// into the record by <see cref="AdoptPreRound41HandOff"/> first.
    /// </summary>
    internal static RecordedCreation ReadRecordedCreation(ProvisioningRun run)
    {
        var creation = run.InterStepState.SpeContainerCreation;
        var additional = (creation?.AdditionalContainerIds ?? Array.Empty<string>())
            .Select(Trimmed)
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var root = Trimmed(creation?.RootContainerId);
        return new RecordedCreation(
            ContainerTypeId: Trimmed(run.InterStepState.ContainerTypeId),
            RootContainerId: root,
            AdditionalContainerIds: additional.Where(id => !string.Equals(id, root, StringComparison.Ordinal)).ToList(),
            ContainerTypeInDoubtSince: creation?.ContainerTypeInDoubtSince,
            RootContainerInDoubtSince: creation?.RootContainerInDoubtSince);
    }

    /// <summary>
    /// The pre-round-41 replication-pending path wrote the UNBOUND root container into
    /// <see cref="InterStepState.SpeContainerId"/> (H7's hand-off). It is moved into the typed creation record — as the
    /// root container, or as a further container to bind when the record already names a different root — and the
    /// hand-off is withdrawn. Called before any write of an entry of an incomplete H8.
    /// </summary>
    internal static void AdoptPreRound41HandOff(ProvisioningRun run)
    {
        var handOff = Trimmed(run.InterStepState.SpeContainerId);
        run.InterStepState.SpeContainerId = null;
        if (handOff is null)
        {
            return;
        }

        var creation = run.InterStepState.SpeContainerCreation ??= new SpeContainerCreationRecord
        {
            Status = SpeContainerCreationRecord.StatusCreated,
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
    /// "container type in doubt" quarantine asks for (its diagnostic is the procedure).
    /// </summary>
    private static bool QuarantineClearedSince(ProvisioningRun run, DateTimeOffset since) =>
        run.Quarantine is { State: QuarantineState.Cleared, ClearedAt: { } clearedAt } quarantine
        && string.Equals(quarantine.QuarantinedByHandler, HandlerIdentifier, StringComparison.Ordinal)
        && clearedAt >= since;

    /// <summary>THE operator procedure for a container type in doubt (owner round 49 item 2).</summary>
    private static string ContainerTypeInDoubtDiagnostic(HandlerEnvelope envelope, string owningAppId, DateTimeOffset? since) =>
        $"A container-type creation for run '{envelope.RunId}' got no authoritative answer" +
        (since is { } s ? $" (at {s:O})" : string.Empty) +
        ": a container type owned by app '" + owningAppId + "' may exist that the run does not name. A container type " +
        "cannot be deleted and is capped per tenant, so H8 creates NO type until an operator checks. With a DELEGATED " +
        "SharePoint Embedded admin token (app-only answers 403): GET https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes " +
        $"and look for owningAppId '{owningAppId}'. If one exists, set the run document's interStepState.containerTypeId to " +
        "its id (H8 then creates only a root container in it). Then clear this quarantine (POST /api/runs/{id}/clear-quarantine) " +
        "and resume: clearing it is the confirmation — without a recorded type H8 then creates one.";

    /// <summary>
    /// Binds every further container adopted with the root (<see cref="SpeContainerTypeProvisionOutputs.AdditionalContainerIds"/>)
    /// to the root's business unit, or removes it (the provisioner's bind-or-remove). A bound or removed one leaves the
    /// record; one that is neither stays on it and the run is quarantined naming it. Null to continue.
    /// </summary>
    private async Task<HandlerResult?> BindAdditionalContainersAsync(
        ProvisioningRun run,
        string etag,
        SpeContainerTypeProvisionOutputs outputs,
        HandlerEnvelope envelope,
        string tenantId,
        string owningAppId,
        string keyVaultName,
        string certSecretName,
        Guid rootBusinessUnitId,
        CancellationToken cancellationToken)
    {
        var pending = (outputs.AdditionalContainerIds ?? Array.Empty<string>())
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
                        VaultName: keyVaultName,
                        CertSecretName: certSecretName,
                        ContainerId: containerId,
                        BusinessUnitId: rootBusinessUnitId),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                KeepOnRecord(pending);
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    SpeContainerTypeRejectionCodes.ContainerBindingInfraFault,
                    $"Binding further container '{containerId}' (found in the run's own container type with root container " +
                    $"'{outputs.RootContainerId}') to business unit '{rootBusinessUnitId}' failed before any Graph call: " +
                    $"{ex.GetType().Name}: {ex.Message}. It exists UNBOUND — bind it with Backfill-SpeContainerBusinessUnitStamp.ps1 " +
                    "-Bind, or remove it — QuarantineRequired.",
                    cancellationToken).ConfigureAwait(false);
            }

            if (outcome is SpeContainerBindOutcome.NotBound { Removed: false } notBound)
            {
                KeepOnRecord(pending);
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    SpeContainerTypeRejectionCodes.ContainerBindingFailedNotRemoved,
                    $"Further container '{containerId}' in the run's own container type: {notBound.Diagnostic}",
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
    /// Persists the creation record (<paramref name="update"/> — <see cref="InterStepState.ContainerTypeId"/> and the typed
    /// <see cref="InterStepState.SpeContainerCreation"/>) and withdraws any H7 hand-off, with the CancellationToken.None —
    /// something was created, so the record is written even when the caller is cancelled. On a concurrent write the
    /// record is MERGED over the current document and retried (it touches only H8's own fields) — never over another
    /// creation's record: a current document naming a different type, or a root container that is neither the one this
    /// entry started from (<paramref name="expectedRootContainerId"/>) nor the one it writes, is left alone. When it cannot
    /// be persisted (the run is gone, or the merges keep losing), the refusal is QuarantineRequired and names the ids: the
    /// root container is UNBOUND and unrecorded, so an operator binds or removes it.
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
                        "H8 recorded its creation ({Status}): runId={RunId} containerTypeId={ContainerTypeId} " +
                        "rootContainerId={RootContainerId}",
                        update.Status, run.RunId, update.ContainerTypeId ?? "(in doubt)", update.RootContainerId ?? "(none)");
                    return (run, success.ETag, null);

                case ReplaceRunResult.Conflict conflict:
                    var current = conflict.Current.Run;
                    if (current.CompletedPhases.Any(cp => string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)))
                    {
                        return (null, null, NotPersisted("a concurrent write completed H8"));
                    }

                    AdoptPreRound41HandOff(current);
                    var currentRecord = ReadRecordedCreation(current);
                    if (currentRecord.ContainerTypeId is { } currentType
                        && !string.Equals(currentType, update.ContainerTypeId, StringComparison.OrdinalIgnoreCase))
                    {
                        // Never overwrite another creation's record — that would orphan IT.
                        return (null, null, NotPersisted($"the run already records container type '{currentType}'"));
                    }

                    if (currentRecord.RootContainerId is { } currentRoot
                        && !string.Equals(currentRoot, update.RootContainerId, StringComparison.Ordinal)
                        && !string.Equals(currentRoot, expectedRootContainerId, StringComparison.Ordinal))
                    {
                        return (null, null, NotPersisted($"the run already records root container '{currentRoot}'"));
                    }

                    _logger.LogWarning(
                        "H8 creation record lost a concurrent write (attempt {Attempt}) — merging over the current run: runId={RunId}",
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
            var created = update.ContainerTypeId is null
                ? "may have created a container type it cannot name"
                : $"created container type '{update.ContainerTypeId}'";
            var containers = new[] { update.RootContainerId }
                .Concat(update.AdditionalContainerIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
            var diagnostic =
                $"H8 {created}" +
                (containers.Count == 0 ? string.Empty : $" and container(s) '{string.Join("', '", containers)}'") +
                (update.RootContainerInDoubtSince is not null ? " (a root-container creation in it got no answer)" : string.Empty) +
                $" but could not record that in run '{envelope.RunId}' ({reason}). A root container is UNBOUND — no SPE " +
                "admin route reaches it: bind it with Backfill-SpeContainerBusinessUnitStamp.ps1 -Bind " +
                "<containerId>=<rootBusinessUnitId>, or remove it, before this run is resumed.";
            _logger.LogCritical(
                "H8 creation NOT recorded: runId={RunId} customerId={CustomerId} containerTypeId={ContainerTypeId} " +
                "containers={Containers} reason={Reason}",
                envelope.RunId, envelope.CustomerId, update.ContainerTypeId ?? "(in doubt)", string.Join(",", containers), reason);
            return new HandlerResult.Failure(
                FailureClass.QuarantineRequired, SpeContainerTypeRejectionCodes.CreationRecordNotPersisted, diagnostic);
        }
    }

    /// <summary>
    /// Writes <paramref name="update"/> into the run: <see cref="InterStepState.ContainerTypeId"/> (when known), the typed
    /// <see cref="InterStepState.SpeContainerCreation"/>, the T6 gate (Pending; its evidence is the operator's view, never
    /// read back), and withdraws the H7 hand-off.
    /// </summary>
    private static void ApplyCreationRecord(ProvisioningRun run, CreationUpdate update)
    {
        if (update.ContainerTypeId is not null)
        {
            run.InterStepState.ContainerTypeId = update.ContainerTypeId;
        }

        run.InterStepState.SpeContainerId = null;
        run.InterStepState.SpeContainerCreation = new SpeContainerCreationRecord
        {
            RootContainerId = update.RootContainerId,
            AdditionalContainerIds = update.AdditionalContainerIds is { Count: > 0 } additional
                ? additional.Where(id => !string.Equals(id, update.RootContainerId, StringComparison.Ordinal)).ToList()
                : null,
            ContainerTypeInDoubtSince = update.ContainerTypeInDoubtSince,
            RootContainerInDoubtSince = update.RootContainerInDoubtSince,
            Status = update.Status,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        if (run.InterStepState.SpeContainerCreation.AdditionalContainerIds is { Count: 0 })
        {
            run.InterStepState.SpeContainerCreation.AdditionalContainerIds = null;
        }

        run.GateStates[SpeContainerTypeGates.T6Verified] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
            Evidence = BuildEvidence(update.RootContainerId, update.Status, verifiedViaAppOnlyToken: false),
        };
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>What H8 has recorded (typed fields only — <see cref="ReadRecordedCreation"/>).</summary>
    internal sealed record RecordedCreation(
        string? ContainerTypeId,
        string? RootContainerId,
        IReadOnlyList<string> AdditionalContainerIds,
        DateTimeOffset? ContainerTypeInDoubtSince,
        DateTimeOffset? RootContainerInDoubtSince)
    {
        /// <summary>Every container the record names (root first).</summary>
        public IEnumerable<string> AllContainerIds =>
            (RootContainerId is null ? Array.Empty<string>() : new[] { RootContainerId }).Concat(AdditionalContainerIds);

        /// <summary>
        /// Containers, or a root-container creation in doubt, without a container type — a state H8 never writes (a
        /// container lives in a type). Quarantined; nothing is created.
        /// </summary>
        public bool Inconsistent =>
            ContainerTypeId is null && (RootContainerId is not null || AdditionalContainerIds.Count > 0 || RootContainerInDoubtSince is not null);
    }

    /// <summary>One write of H8's creation record (<see cref="ApplyCreationRecord"/>).</summary>
    private sealed record CreationUpdate(
        string? ContainerTypeId,
        string? RootContainerId,
        IReadOnlyList<string>? AdditionalContainerIds,
        DateTimeOffset? ContainerTypeInDoubtSince,
        DateTimeOffset? RootContainerInDoubtSince,
        string Status);

    /// <summary>
    /// Builds the gate evidence JSON. <paramref name="verifiedViaAppOnlyToken"/>
    /// is now an explicit parameter (task 131 fix — previously hardcoded
    /// <c>true</c> unconditionally, which would have produced misleading
    /// evidence for the WaitingOnGate/replication-pending case, where
    /// verification has explicitly NOT happened yet).
    /// </summary>
    private static System.Text.Json.JsonElement BuildEvidence(
        string? rootContainerId, string verifiedStatus, bool verifiedViaAppOnlyToken, Guid? owningBusinessUnitId = null)
    {
        // owningBusinessUnitId: the business unit the root container is stamped with (task 165, round 35 item 1);
        // null while the container is not yet bound (the replication-pending wait).
        var doc = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            rootContainerId,
            verifiedStatus,
            verifiedViaAppOnlyToken,
            owningBusinessUnitId = owningBusinessUnitId?.ToString("D"),
        });
        return doc;
    }
}
