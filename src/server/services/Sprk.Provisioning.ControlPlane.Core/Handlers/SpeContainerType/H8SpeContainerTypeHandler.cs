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
//   Microsoft Graph SPE APIs). Wraps the task-011-hardened
//   scripts/Create-NewContainerType.ps1 (-CreateTestContainer), verifies the
//   root container is readable via a FRESH app-only token
//   (scripts/Get-SpeContainerMetadata-AppOnly.ps1 — new, task 051), and
//   persists the real container-type id to the customer's Key Vault under the
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
        IOptions<SpeContainerTypeOptions> options,
        ILogger<H8SpeContainerTypeHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(provisioner);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(kvWriter);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _provisioner = provisioner;
        _verifier = verifier;
        _kvWriter = kvWriter;
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

        // (6) Invoke the provisioner (container-type + root container, T6
        //     confidential-client cert-based). Infra faults are Resumable (no
        //     confirmed external side effect); domain Failure with
        //     IsDelegatedTokenTrap=true is the T6 trap itself.
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
                DisplayName: displayName);
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
                "No confirmed external side effect — Resumable.",
                cancellationToken).ConfigureAwait(false);
        }

        if (provisionOutcome is SpeContainerTypeProvisionOutcome.Failure provisionFailure)
        {
            if (provisionFailure.IsDelegatedTokenTrap)
            {
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    SpeContainerTypeRejectionCodes.TrapT6DelegatedTokenDetected, provisionFailure.Diagnostic,
                    cancellationToken).ConfigureAwait(false);
            }
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerTypeRejectionCodes.ProvisioningFailed, provisionFailure.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        var outputs = ((SpeContainerTypeProvisionOutcome.Success)provisionOutcome).Outputs;
        if (string.IsNullOrWhiteSpace(outputs.ContainerTypeId) || string.IsNullOrWhiteSpace(outputs.RootContainerId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SpeContainerTypeRejectionCodes.ProvisioningOutputsIncomplete,
                "SPE container-type provisioner returned incomplete outputs — one of ContainerTypeId / " +
                "RootContainerId is blank.",
                cancellationToken).ConfigureAwait(false);
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

        var verified = (SpeContainerVerificationResult.Verified)verifyResult;

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

        return await MarkCompleteAsync(run, etag, idempotencyKey, outputs, verified, envelope, cancellationToken)
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

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        SpeContainerTypeProvisionOutputs outputs,
        SpeContainerVerificationResult.Verified verified,
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
        run.GateStates[SpeContainerTypeGates.T6Verified] = new GateEntry
        {
            Status = GateState.Verified,
            VerifiedAt = completedAt,
            VerifierHandler = HandlerIdentifier,
            Evidence = BuildEvidence(outputs.RootContainerId, verified.Status),
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

    private static System.Text.Json.JsonElement BuildEvidence(string rootContainerId, string verifiedStatus)
    {
        var doc = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            rootContainerId,
            verifiedStatus,
            verifiedViaAppOnlyToken = true,
        });
        return doc;
    }
}
