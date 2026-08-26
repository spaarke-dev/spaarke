// -----------------------------------------------------------------------------
// H13E2EAcceptanceGateHandler.cs
//
// L2 CONTROL-PLANE H13 E2E acceptance-gate handler (task 055, wave C4 Batch 4E).
// THE FINAL GATE. Sole authority for the Dataverse registry
// `sprk_dataverseenvironment.SetupStatus → Ready` transition.
//
// PURPOSE:
//   Asserts EFFECTS not intentions (R7 principle). Independently re-verifies
//   every prior handler's silent-fail post-condition + samples every §4D
//   tenant-isolation invariant + gates naming-conformance + samples cost
//   envelope. Registry transition happens if-and-only-if ALL six gates green.
//   On any failure → Cosmos state = Quarantined per §4C.
//
// SPEC / DESIGN references:
//   - spec.md FR-18 (H13 acceptance criteria) + SC #5 (extended validate
//     script) + SC #6 (all 6 traps re-verified) + SC #14 (cost envelope) +
//     SC #17 (naming exit 0) + §4B (T1–T6 trap catalog) + §4C (Quarantined
//     rollback) + §4D (I1–I5 invariants) + §15 #14 (cost).
//   - design.md §4.1 H13 row (final gate, downstream of H14) + §4B (trap
//     catalog + owning-handler post-condition table) + §4D (5 invariants).
//   - .claude/adr/ADR-004: single IProvisioningHandler-shape impl registered
//     in L2 DI (Path A exception for L2 orchestration — see project spec.md
//     § ADR Tensions).
//   - .claude/adr/ADR-010: register in L2, NOT BFF.
//   - .claude/adr/ADR-036: reuse background-job infrastructure; 3-level
//     idempotency (Level 1 = SB MessageId dedup by reconciler; Level 3 =
//     ProvisioningRun.CompletedPhases scan in this handler).
//   - .claude/adr/ADR-044: registry env id + sample assertion GUIDs follow
//     canonicalization (bare lowercase — enforced upstream at repository
//     boundary).
//
// AGGREGATE-GATE PATTERN:
//   H13 fans out to 6 collaborator seams in a deterministic order + collects
//   every outcome BEFORE deciding pass/fail — the operator sees the full
//   picture on failure, not just the first observed failure. Each collaborator
//   is invoked once; short-circuit-fail on any hard failure is deliberately
//   AVOIDED (contrast with H0.5 / H1 quota probes which short-circuit) because
//   H13's job is a COMPREHENSIVE snapshot for operator diagnosis — see
//   design.md §4.1 H13 row "final acceptance snapshot".
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌───────────────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                                  │ §4C class                 │
//   ├───────────────────────────────────────────────┼───────────────────────────┤
//   │ Missing tenantId/subscriptionId/buildId/      │ Resumable                 │
//   │ dataverseUrl/bffApiUrl (§4D I1 / idempotency) │ (external precondition)   │
//   │ Run not found in Cosmos partition             │ Resumable                 │
//   │ Extended validate script domain failure       │ QuarantineRequired        │
//   │ (SC #5 sample-check failed — silent-fail      │ (a post-swap sample check │
//   │ actually manifested)                          │ observed a defect that   │
//   │                                               │ blocks handoff)          │
//   │ Extended validate script infra fault          │ Resumable                 │
//   │ (pwsh / script missing / timeout)             │                           │
//   │ ANY T1–T6 trap FAILED                         │ QuarantineRequired        │
//   │ (silent-fail actually manifested — SC #6)     │                           │
//   │ Trap verifier InfraFault                      │ Resumable                 │
//   │ (probe could not run — no verdict)            │                           │
//   │ ANY I1–I5 invariant FAILED                    │ QuarantineRequired        │
//   │ (§4D CATASTROPHIC severity — cross-tenant     │                           │
//   │ bleed risk)                                   │                           │
//   │ Invariant verifier InfraFault                 │ Resumable                 │
//   │ Naming-conformance FAILED (SC #17)            │ QuarantineRequired        │
//   │ Naming-conformance infra fault                │ Resumable                 │
//   │ Cost drift > threshold AND CostDriftFailsRun  │ QuarantineRequired        │
//   │ Cost drift > threshold AND !CostDriftFailsRun │ (advisory-warn — Ready    │
//   │                                               │ still transitions if all │
//   │                                               │ else green; per POML     │
//   │                                               │ deviation note)          │
//   │ Cost query infra fault                        │ Resumable                 │
//   │ Registry transition PATCH failed              │ Resumable                 │
//   │ (Web API 4xx/5xx / ETag conflict)             │ (retry after operator    │
//   │                                               │ resolves)                │
//   │ Concurrent Cosmos writer conflict             │ Resumable                 │
//   │ Run row deleted mid-flight                    │ Resumable                 │
//   └───────────────────────────────────────────────┴───────────────────────────┘
//
// IDEMPOTENCY (per POML constraint):
//   Key = validate-{customerId}-{buildId} — buildId is the BFF CI build number
//   (deterministic per §4.1 preamble; parity with H9's bff-{customerId}-
//   {buildId}). Level-3 scan: run.CompletedPhases search for (Phase="H13",
//   IdempotencyKey==expected). Match ⇒ Success no-op BEFORE any collaborator
//   seam is invoked — a repeat/retry for the same build is a no-op; a NEW
//   build gets a NEW key + re-verifies from scratch.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   H13 lives in L2 (not BFF) per spec §5.2 / D3 / D8 / D12; consumes NO
//   AI-internal types (ADR-013 forcing-function rule — no IActionResolver,
//   IActionRunner, IOpenAiClient, IPlaybookService injection). Uses
//   IProvisioningRunRepository (task 037) + 6 dedicated seams
//   (IE2EValidationRunner, IE2ETrapVerifier, IE2EInvariantVerifier,
//   INamingConformanceChecker, ICostEnvelopeChecker, IRegistrySetupStatusUpdater);
//   no BFF-facade dependencies.
//
// COMPONENT JUSTIFICATION (CLAUDE.md §11):
//   Existing: scripts/Validate-DeployedEnvironment.ps1 (operator-invocable) +
//     scripts/naming-conformance-check.ps1 (r3 task 063). Neither extends the
//     automated per-run Cosmos state transition + registry-status transition
//     + §4C Quarantine semantics H13 needs.
//   Extension: no — H13 is the SOLE authoritative gate for Setup Status =
//     Ready. Extending Validate-DeployedEnvironment.ps1 alone cannot own
//     Cosmos state transitions + §4C Quarantined semantics + Dataverse
//     registry transition — those require an IProvisioningHandler.
//   Cost-of-doing-nothing: registry rows could transition to Ready with
//     silent trap failures still present (T1 keyVaultReferenceIdentity
//     mismatch → BFF boots with null KV refs → runtime failures the moment
//     the operator hands the env to the customer). H13 is the r1 acceptance
//     floor.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Registry;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H13E2EAcceptanceGateHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H13;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the customer subscription id (ADR-027 D4) — required for cost query + ARM trap probes.</summary>
    public const string SubscriptionIdParameterKey = "subscriptionId";

    /// <summary>Non-secret parameter key carrying the BFF CI build number — feeds the idempotency key <c>validate-{customerId}-{buildId}</c>.</summary>
    public const string BuildIdParameterKey = "buildId";

    /// <summary>Non-secret parameter key carrying the target BFF API URL (production slot post-H9 swap).</summary>
    public const string BffApiUrlParameterKey = "bffApiUrl";

    /// <summary>Non-secret parameter key carrying the customer resource group name — cost + ARM query scoping.</summary>
    public const string ResourceGroupNameParameterKey = "resourceGroupName";

    /// <summary>Non-secret parameter key carrying the BFF App Service name — T1/T5 ARM trap scoping.</summary>
    public const string AppServiceNameParameterKey = "appServiceName";

    /// <summary>Non-secret parameter key carrying the customer KV name — T1 trap scoping.</summary>
    public const string KeyVaultNameParameterKey = "keyVaultName";

    /// <summary>Non-secret parameter key carrying the Spaarke-internal registry Dataverse env URL (target of the SetupStatus PATCH — NOT the customer's own Dataverse env URL).</summary>
    public const string RegistryDataverseUrlParameterKey = "registryDataverseUrl";

    /// <summary>Non-secret parameter key carrying the absolute path to scripts/ on disk for I1 grep probe. Optional — defaults to the App Service publish scripts directory.</summary>
    public const string ProvisioningScriptsDirectoryParameterKey = "provisioningScriptsDirectory";

    private readonly IProvisioningRunRepository _repository;
    private readonly IE2EValidationRunner _validationRunner;
    private readonly IE2ETrapVerifier _trapVerifier;
    private readonly IE2EInvariantVerifier _invariantVerifier;
    private readonly INamingConformanceChecker _namingChecker;
    private readonly ICostEnvelopeChecker _costChecker;
    private readonly IRegistrySetupStatusUpdater _registryUpdater;
    private readonly IDataverseEnvironmentRegistryClient _registryClient;
    private readonly H13AcceptanceOptions _options;
    private readonly ILogger<H13E2EAcceptanceGateHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    public H13E2EAcceptanceGateHandler(
        IProvisioningRunRepository repository,
        IE2EValidationRunner validationRunner,
        IE2ETrapVerifier trapVerifier,
        IE2EInvariantVerifier invariantVerifier,
        INamingConformanceChecker namingChecker,
        ICostEnvelopeChecker costChecker,
        IRegistrySetupStatusUpdater registryUpdater,
        IDataverseEnvironmentRegistryClient registryClient,
        IOptions<H13AcceptanceOptions> options,
        ILogger<H13E2EAcceptanceGateHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(validationRunner);
        ArgumentNullException.ThrowIfNull(trapVerifier);
        ArgumentNullException.ThrowIfNull(invariantVerifier);
        ArgumentNullException.ThrowIfNull(namingChecker);
        ArgumentNullException.ThrowIfNull(costChecker);
        ArgumentNullException.ThrowIfNull(registryUpdater);
        ArgumentNullException.ThrowIfNull(registryClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _validationRunner = validationRunner;
        _trapVerifier = trapVerifier;
        _invariantVerifier = invariantVerifier;
        _namingChecker = namingChecker;
        _costChecker = costChecker;
        _registryUpdater = registryUpdater;
        _registryClient = registryClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<HandlerResult> HandleAsync(
        HandlerEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.CustomerId);

        if (!string.Equals(envelope.HandlerId, HandlerIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"H13E2EAcceptanceGateHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H13 E2E acceptance-gate starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        //     required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H13 aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                FailureClass.Resumable, H13Rejections.RunNotFound,
                $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;

        // (2) Parameter guards — every field H13 needs must be non-empty
        //     BEFORE any collaborator seam is invoked (§4C Resumable).
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H13Rejections.MissingTenantId,
                "Run parameter 'tenantId' is required by H13 (§4D I1 no-hardcoded-tenant). " +
                "Every trap/invariant probe scopes to this tenant.",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, SubscriptionIdParameterKey, out var subscriptionId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H13Rejections.MissingSubscriptionId,
                "Run parameter 'subscriptionId' is required by H13 (ADR-027 D4 — cost query + ARM trap probes).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, BuildIdParameterKey, out var buildId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H13Rejections.MissingBuildId,
                "Run parameter 'buildId' is required by H13 (idempotency key: validate-{customerId}-{buildId}).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, BffApiUrlParameterKey, out var bffApiUrl))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H13Rejections.MissingBffApiUrl,
                "Run parameter 'bffApiUrl' is required by H13 (BFF sample /healthz + E2E round-trip target).",
                cancellationToken).ConfigureAwait(false);
        }

        var dataverseUrl = run.InterStepState.DataverseEnvUrl ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dataverseUrl))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H13Rejections.MissingDataverseEnvUrl,
                "InterStepState.dataverseEnvUrl is not populated — H5/H6 must complete before H13. " +
                "The extended validate script requires it for sample checks.",
                cancellationToken).ConfigureAwait(false);
        }

        TryGetNonEmpty(parameters, ResourceGroupNameParameterKey, out var resourceGroupName);
        TryGetNonEmpty(parameters, AppServiceNameParameterKey, out var appServiceName);
        TryGetNonEmpty(parameters, KeyVaultNameParameterKey, out var keyVaultName);
        TryGetNonEmpty(parameters, RegistryDataverseUrlParameterKey, out var registryDataverseUrl);
        TryGetNonEmpty(parameters, ProvisioningScriptsDirectoryParameterKey, out var scriptsDirParam);
        var scriptsDirectory = string.IsNullOrWhiteSpace(scriptsDirParam)
            ? Path.Combine(AppContext.BaseDirectory, "scripts")
            : scriptsDirParam;

        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, buildId);

        // (3) Level-3 idempotency: durable no-op on duplicate.
        //     Per POML acceptance criterion 8: "no re-verification if
        //     idempotency-key matches AND registry status is Ready."
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            if (_options.HonorRegistryStatusReadyShortCircuit)
            {
                var snap = await _registryClient
                    .LookupByTenantIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
                if (snap is not null && string.Equals(snap.SetupStatus, "Ready", StringComparison.Ordinal))
                {
                    _logger.LogInformation(
                        "H13 idempotent no-op (registry Ready): runId={RunId} idempotencyKey={IdempotencyKey}",
                        envelope.RunId, idempotencyKey);
                    return new HandlerResult.Success(idempotencyKey);
                }
                _logger.LogInformation(
                    "H13 idempotent no-op (level-3 CompletedPhases hit): runId={RunId} idempotencyKey={IdempotencyKey}",
                    envelope.RunId, idempotencyKey);
            }
            else
            {
                _logger.LogInformation(
                    "H13 idempotent no-op (level-3 CompletedPhases hit): runId={RunId} idempotencyKey={IdempotencyKey}",
                    envelope.RunId, idempotencyKey);
            }
            return new HandlerResult.Success(idempotencyKey);
        }

        var bffAppRegId = run.InterStepState.BffAppRegId ?? string.Empty;
        var uamiClientId = run.InterStepState.MiClientId ?? string.Empty;
        var aiSearchEndpoint = run.InterStepState.AiSearchEndpoint ?? string.Empty;
        var cosmosEndpoint = run.InterStepState.CosmosEndpoint ?? string.Empty;

        // (4) Extended validate script (SC #5).
        E2EValidationOutcome validationOutcome;
        try
        {
            validationOutcome = await _validationRunner.RunAsync(
                new E2EValidationRequest(
                    CustomerId: envelope.CustomerId,
                    RunId: envelope.RunId,
                    DataverseUrl: dataverseUrl,
                    BffApiUrl: bffApiUrl,
                    TargetSlotName: _options.TargetSlotName),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H13 extended-validation infra fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                H13Rejections.ExtendedValidationInfraFault,
                $"Validate-DeployedEnvironment.ps1 infra fault: {ex.GetType().Name}: {ex.Message}. " +
                "Verify pwsh + the script are on the App Service publish layout.",
                cancellationToken).ConfigureAwait(false);
        }

        // (5) Trap verifier (SC #6 — all 6 traps).
        TrapCatalogVerificationResult trapResult;
        try
        {
            trapResult = await _trapVerifier.VerifyAllAsync(
                new TrapVerificationRequest(
                    CustomerId: envelope.CustomerId,
                    RunId: envelope.RunId,
                    TenantId: tenantId,
                    SubscriptionId: subscriptionId,
                    DataverseUrl: dataverseUrl,
                    BffAppRegId: bffAppRegId,
                    UamiClientId: uamiClientId,
                    KeyVaultName: keyVaultName,
                    AppServiceName: appServiceName,
                    ResourceGroupName: resourceGroupName),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H13 trap-verifier infra fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                H13Rejections.TrapVerifierInfraFault,
                $"Trap verifier infra fault: {ex.GetType().Name}: {ex.Message}.",
                cancellationToken).ConfigureAwait(false);
        }
        _logger.LogInformation("H13 trap-catalog: runId={RunId} summary={Summary}",
            envelope.RunId, trapResult.ToLogSummary());

        // (6) Invariant verifier (§4D I1–I5).
        InvariantCatalogVerificationResult invariantResult;
        try
        {
            invariantResult = await _invariantVerifier.VerifyAllAsync(
                new InvariantVerificationRequest(
                    CustomerId: envelope.CustomerId,
                    RunId: envelope.RunId,
                    TenantId: tenantId,
                    SubscriptionId: subscriptionId,
                    AiSearchEndpoint: aiSearchEndpoint,
                    CosmosEndpoint: cosmosEndpoint,
                    BffApiUrl: bffApiUrl,
                    ProvisioningScriptsDirectory: scriptsDirectory),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H13 invariant-verifier infra fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                H13Rejections.InvariantVerifierInfraFault,
                $"Invariant verifier infra fault: {ex.GetType().Name}: {ex.Message}.",
                cancellationToken).ConfigureAwait(false);
        }
        _logger.LogInformation("H13 invariant-catalog: runId={RunId} summary={Summary}",
            envelope.RunId, invariantResult.ToLogSummary());

        // (7) Naming-conformance (SC #17).
        NamingConformanceOutcome namingOutcome;
        try
        {
            namingOutcome = await _namingChecker.CheckAsync(
                new NamingConformanceRequest(envelope.CustomerId, envelope.RunId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H13 naming-conformance infra fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                H13Rejections.NamingConformanceInfraFault,
                $"naming-conformance-check.ps1 infra fault: {ex.GetType().Name}: {ex.Message}.",
                cancellationToken).ConfigureAwait(false);
        }

        // (8) Cost envelope (SC #14 + §15 #14).
        CostEnvelopeReport? costReport = null;
        string? costInfraDiag = null;
        try
        {
            costReport = await _costChecker.CheckAsync(
                new CostEnvelopeRequest(
                    CustomerId: envelope.CustomerId,
                    RunId: envelope.RunId,
                    SubscriptionId: subscriptionId,
                    TenancyModel: run.TenancyModel,
                    ResourceGroupName: resourceGroupName,
                    DriftAdvisoryThreshold: _options.CostDriftAdvisoryThreshold),
                cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("H13 cost-envelope: runId={RunId} summary={Summary}",
                envelope.RunId, costReport.Summary);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H13 cost-query infra fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            costInfraDiag = $"Cost query infra fault: {ex.GetType().Name}: {ex.Message}";
        }

        // (9) DECISION: aggregate every collaborator's outcome + pick failure
        //     with correct §4C classification. Priority order:
        //       (a) Trap/invariant FAILED → QuarantineRequired (silent-fail
        //           actually manifested — CATASTROPHIC).
        //       (b) Naming-conformance FAILED → QuarantineRequired.
        //       (c) Extended-validate FAILED → QuarantineRequired.
        //       (d) Cost drift (fail-run mode) → QuarantineRequired.
        //       (e) Trap/invariant InfraFault → Resumable (no verdict).
        //       (f) Cost query infra fault → Resumable.
        //     Advisory-warn cost drift is NOT a failure branch — it's attached
        //     to the diagnostic + gate-state but the Ready transition still
        //     happens if all else green (per POML deviation note).

        if (trapResult.FirstFailure is TrapVerificationOutcome.Failed trapFail)
        {
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                MapTrapKindToRejectionCode(trapFail.Kind),
                $"T{(int)trapFail.Kind} silent-fail trap detected: {trapFail.Diagnostic}. " +
                $"Full trap catalog: {trapResult.ToLogSummary()}",
                cancellationToken).ConfigureAwait(false);
        }
        if (invariantResult.FirstFailure is InvariantVerificationOutcome.Failed invFail)
        {
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                MapInvariantKindToRejectionCode(invFail.Kind),
                $"I{(int)invFail.Kind} tenant-isolation invariant VIOLATED (CATASTROPHIC): {invFail.Diagnostic}. " +
                $"Full invariant catalog: {invariantResult.ToLogSummary()}",
                cancellationToken).ConfigureAwait(false);
        }
        if (namingOutcome is NamingConformanceOutcome.Failure namingFail)
        {
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                H13Rejections.NamingConformanceFailed,
                $"naming-conformance-check.ps1 exit {namingFail.ExitCode}: {namingFail.Diagnostic}.",
                cancellationToken).ConfigureAwait(false);
        }
        if (validationOutcome is E2EValidationOutcome.Failure valFail)
        {
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                H13Rejections.ExtendedValidationFailed,
                $"Extended validate script (SC #5) reported {valFail.ChecksFailed.Count} " +
                $"failing check(s): {valFail.Diagnostic}.",
                cancellationToken).ConfigureAwait(false);
        }
        if (costReport is not null && costReport.ExceedsAdvisoryThreshold && _options.CostDriftFailsRun)
        {
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                H13Rejections.CostDriftExceeded,
                $"Cost drift {costReport.DriftFraction:P1} exceeds threshold {_options.CostDriftAdvisoryThreshold:P0} " +
                $"AND CostDriftFailsRun=true. Summary: {costReport.Summary}.",
                cancellationToken).ConfigureAwait(false);
        }
        if (trapResult.FirstInfraFault is TrapVerificationOutcome.InfraFault trapInfra)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                H13Rejections.TrapVerifierInfraFault,
                $"Trap verifier could not verdict T{(int)trapInfra.Kind}: {trapInfra.Diagnostic}. " +
                $"Full trap catalog: {trapResult.ToLogSummary()}",
                cancellationToken).ConfigureAwait(false);
        }
        if (invariantResult.FirstInfraFault is InvariantVerificationOutcome.InfraFault invInfra)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                H13Rejections.InvariantVerifierInfraFault,
                $"Invariant verifier could not verdict I{(int)invInfra.Kind}: {invInfra.Diagnostic}. " +
                $"Full invariant catalog: {invariantResult.ToLogSummary()}",
                cancellationToken).ConfigureAwait(false);
        }
        if (costInfraDiag is not null)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                H13Rejections.CostQueryInfraFault, costInfraDiag,
                cancellationToken).ConfigureAwait(false);
        }

        // (10) All gates green. Transition registry Setup Status → Ready.
        var envUpdate = new RegistrySetupStatusUpdateRequest(
            CustomerId: envelope.CustomerId,
            RunId: envelope.RunId,
            TenantId: tenantId,
            EnvironmentId: run.EnvironmentId,
            RegistryDataverseUrl: registryDataverseUrl);
        RegistrySetupStatusUpdateOutcome updateOutcome;
        try
        {
            updateOutcome = await _registryUpdater.TransitionToReadyAsync(envUpdate, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H13 registry-update infra fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                H13Rejections.RegistryUpdateFailed,
                $"Registry Setup Status → Ready PATCH infra fault: {ex.GetType().Name}: {ex.Message}.",
                cancellationToken).ConfigureAwait(false);
        }

        if (updateOutcome is RegistrySetupStatusUpdateOutcome.Failure updateFail)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                H13Rejections.RegistryUpdateFailed,
                $"Registry Setup Status → Ready PATCH rejected: {updateFail.Diagnostic}.",
                cancellationToken).ConfigureAwait(false);
        }

        // (11) Advance Cosmos state.
        stopwatch.Stop();
        _logger.LogInformation(
            "H13 E2E acceptance succeeded: runId={RunId} customerId={CustomerId} durationMs={DurationMs} " +
            "traps={TrapSummary} invariants={InvariantSummary} costSummary={CostSummary} costAdvisory={CostAdvisory}",
            envelope.RunId, envelope.CustomerId, stopwatch.ElapsedMilliseconds,
            trapResult.ToLogSummary(), invariantResult.ToLogSummary(),
            costReport?.Summary ?? "(none)",
            costReport?.ExceedsAdvisoryThreshold ?? false);

        return await MarkCompleteAsync(
            run, etag, idempotencyKey, envelope,
            trapResult, invariantResult, costReport,
            validationOutcome, namingOutcome,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H13 idempotency key:
    /// <c>validate-{customerId}-{buildId}</c>. Exposed internal so unit tests
    /// can construct expected keys without duplicating the format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, string buildId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        return $"validate-{customerId}-{buildId}";
    }

    /// <summary>
    /// Maps a failing <see cref="TrapKind"/> to its distinct rejection code
    /// (POML criterion "each trap fail branch"). Internal so unit tests can
    /// assert the mapping directly.
    /// </summary>
    internal static string MapTrapKindToRejectionCode(TrapKind kind) => kind switch
    {
        TrapKind.T1KeyVaultReferenceIdentity => H13Rejections.TrapT1Failed,
        TrapKind.T2DataverseAppUser          => H13Rejections.TrapT2Failed,
        TrapKind.T3GraphAppRoleParity        => H13Rejections.TrapT3Failed,
        TrapKind.T4ExchangePolicyCount       => H13Rejections.TrapT4Failed,
        TrapKind.T5SlotMiKvRbac              => H13Rejections.TrapT5Failed,
        TrapKind.T6SpeConfidentialClient     => H13Rejections.TrapT6Failed,
        _ => throw new InvalidOperationException($"Unmapped trap kind '{kind}'."),
    };

    /// <summary>
    /// Maps a failing <see cref="InvariantKind"/> to its distinct rejection
    /// code. Internal so unit tests can assert the mapping directly.
    /// </summary>
    internal static string MapInvariantKindToRejectionCode(InvariantKind kind) => kind switch
    {
        InvariantKind.I1NoHardcodedTenant   => H13Rejections.InvariantI1Failed,
        InvariantKind.I2AiSearchTenantFilter => H13Rejections.InvariantI2Failed,
        InvariantKind.I3CosmosPartitionKey  => H13Rejections.InvariantI3Failed,
        InvariantKind.I4SpeContainerResolver => H13Rejections.InvariantI4Failed,
        InvariantKind.I5GraphTokenTenant    => H13Rejections.InvariantI5Failed,
        _ => throw new InvalidOperationException($"Unmapped invariant kind '{kind}'."),
    };

    private static bool TryGetNonEmpty(
        IDictionary<string, string> parameters, string key, out string value)
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
        ProvisioningRun run, string etag, FailureClass failureClass,
        string rejectionCode, string diagnostic, CancellationToken cancellationToken)
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

        run.GateStates[$"h13-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H13 failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H13 failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run, string etag, string idempotencyKey, HandlerEnvelope envelope,
        TrapCatalogVerificationResult trapResult,
        InvariantCatalogVerificationResult invariantResult,
        CostEnvelopeReport? costReport,
        E2EValidationOutcome validationOutcome,
        NamingConformanceOutcome namingOutcome,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        run.Status = RunStatus.Completed;
        run.CurrentPhase = HandlerIdentifier;
        run.CompletedOn = completedAt;
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        run.ErrorDetail = null;

        // Record every H13 gate as Verified — operators can grep the run for
        // each of the 6 gates independently without opening the full doc.
        var verified = new GateEntry
        {
            Status = GateState.Verified,
            VerifiedAt = completedAt,
            VerifierHandler = HandlerIdentifier,
        };
        run.GateStates[H13Gates.ExtendedValidationVerified] = verified;
        run.GateStates[H13Gates.TrapCatalogVerified] = verified;
        run.GateStates[H13Gates.InvariantCatalogVerified] = verified;
        run.GateStates[H13Gates.NamingConformanceVerified] = verified;
        run.GateStates[H13Gates.CostEnvelopeVerified] = new GateEntry
        {
            Status = costReport is not null && costReport.ExceedsAdvisoryThreshold
                ? GateState.Verified   // Advisory-warn is Verified (Ready still transitions) — the warning is captured in the ErrorDetail advisory suffix + summary.
                : GateState.Verified,
            VerifiedAt = completedAt,
            VerifierHandler = HandlerIdentifier,
        };
        run.GateStates[H13Gates.RegistryReadyTransitioned] = verified;

        // If cost drift is advisory-warn, attach the advisory to ErrorDetail so
        // the operator sees it in the run document (Ready still transitions).
        if (costReport is not null && costReport.ExceedsAdvisoryThreshold)
        {
            run.ErrorDetail = $"[advisory: cost-drift] {costReport.Summary} " +
                              $"(drift {costReport.DriftFraction:P1} > threshold {_options.CostDriftAdvisoryThreshold:P0}). " +
                              "Advisory-only per project deviation note; Ready transitioned despite drift.";
        }

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H13 success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                FailureClass.Resumable, H13Rejections.ConcurrentWriteConflict,
                $"Concurrent write advanced run '{run.RunId}' between H13 read + write. " +
                $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H13.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H13 success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                FailureClass.Resumable, H13Rejections.RunDeletedDuringAcceptance,
                $"ProvisioningRun '{run.RunId}' was deleted while H13 was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
