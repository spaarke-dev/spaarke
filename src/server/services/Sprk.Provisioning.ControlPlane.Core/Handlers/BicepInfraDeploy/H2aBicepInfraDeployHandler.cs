// -----------------------------------------------------------------------------
// H2aBicepInfraDeployHandler.cs
//
// L2 CONTROL-PLANE H2a Bicep infra-deploy handler (task 044, wave C4).
//
// PURPOSE:
//   Provisions per-customer Azure resources (Cosmos + OpenAI + AI Search +
//   Doc Intel + App Insights + optional SignalR) by wrapping the hardened
//   scripts/Provision-Customer.ps1 (post-Phase B). Redis is EXPLICITLY NOT
//   provisioned per Q-E FR-12 (per-env via scripts/Deploy-RedisCache.ps1).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-04 (H2a):
//       15 resources per §7.2 deploy to expected names per §7.1; drift
//       detection surfaces manual edits BEFORE apply.
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-33 (T1):
//       App Service keyVaultReferenceIdentity == UAMI resource id on BOTH
//       prod + staging slots. Handler asserts post-deploy; fails with
//       distinct rejection code (trap-T1-keyvault-reference-identity-mismatch)
//       otherwise.
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-34 (upgrade
//       mode): if run parameter `provisionedOn` is non-null, run `az deployment
//       group what-if` first; default REJECT on drift + write drift report at
//       runNotes/drift-{customerId}-{timestamp}.md.
//   - projects/customer-provisioning-orchestration-r1/spec.md §MUST rules:
//       Redis MUST NOT be provisioned per-customer; H2a asserts template
//       structural absence of a Redis module before deploy.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1a Model 1
//       vs Model 2: TenancyModel drives stack selection.
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback:
//       Bicep partial-fail is Quarantine-required (orphaned resources).
//   - ADR-020: openai.bicep model deployments MUST be pinned to specific
//       versions; H2a asserts template structural absence of `latest`.
//   - ADR-027: subscription-per-customer isolation; H2a deploys to the target
//       subscription only.
//   - ADR-032: SignalR feature-gated via the `SignalREnabled` parameter — the
//       resource deploys only when true; both code paths compile + register.
//   - ADR-036: handler is fire-and-forget via Service Bus (10–20 min Bicep
//       runs off-thread per spec.md FR-22 / R20).
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌─────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                        │ §4C class                 │
//   ├─────────────────────────────────────┼───────────────────────────┤
//   │ Missing tenantId / subscriptionId / │ Resumable                 │
//   │ bicepVer (§4D I1 + structural)      │ (external precondition —  │
//   │                                     │ operator fixes params +   │
//   │                                     │ resumes)                  │
//   │ Run not found in Cosmos partition   │ Resumable                 │
//   │ Template contains Redis (spec MUST) │ QuarantineRequired        │
//   │                                     │ (template drift — must    │
//   │                                     │ not silently proceed)     │
//   │ Model deployment unpinned (ADR-020) │ QuarantineRequired        │
//   │ Upgrade-mode drift detected         │ QuarantineRequired        │
//   │ (§14A.5 default Option B)           │ (accept-and-apply would   │
//   │                                     │ overwrite intended state) │
//   │ Bicep deploy failed (partial state) │ QuarantineRequired        │
//   │ (design.md §4C example — 12 of 16   │ (orphaned resources —     │
//   │ resources deployed then fail)       │ operator repair or        │
//   │                                     │ Decommission-Customer.ps1)│
//   │ T1 trap post-condition mismatch     │ QuarantineRequired        │
//   │ (keyVaultReferenceIdentity ≠ UAMI)  │ (partial deploy — Bicep   │
//   │                                     │ succeeded but PATCH step  │
//   │                                     │ missing → downstream BFF  │
//   │                                     │ boots with null KV refs)  │
//   │ Concurrent Cosmos writer conflict   │ Resumable                 │
//   │ Run row deleted mid-flight          │ Resumable                 │
//   └─────────────────────────────────────┴───────────────────────────┘
//
// IDEMPOTENCY (3-level per ADR-004 / design.md §4.1):
//   Level 1 (Service Bus MessageId dedup): the H0 chain / future reconciler
//           computes deterministic MessageId per (HandlerId, RunId, CustomerId,
//           paramHash); SB duplicate-detection collapses re-enqueues.
//   Level 2 (Redis IdempotencyService): NOT YET IMPLEMENTED in L2 (design.md
//           §4.1 preamble; parity with H0 / H0.5).
//   Level 3 (handler body durable dedup): this handler scans
//           ProvisioningRun.CompletedPhases for (Phase=="H2a",
//           IdempotencyKey==infra-{customerId}-{bicepVer}). Match ⇒ Success
//           no-op. bicepVer is the git SHA of infrastructure/bicep/ (POML
//           constraint) — NOT an attempt counter.
//
// DOWNSTREAM ENQUEUE (Wave C4 note):
//   H2a does NOT enqueue a specific successor (unlike H0 which explicitly
//   chains H0.5). The downstream DAG per design.md §4.1 branches 3-way from
//   H2a (H2b indexes / H4 KV / H5 dv-env). The wave-C5 reconciler owns the
//   fan-out; for wave C4 this handler mutates Cosmos state (CurrentPhase +
//   CompletedPhases + InterStepState) and returns Success, letting the
//   reconciler decide which handler(s) to dispatch next based on the
//   Cosmos state.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H2aBicepInfraDeployHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md § 4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H2a;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the target subscription id (ADR-027 D4).</summary>
    public const string SubscriptionIdParameterKey = "subscriptionId";

    /// <summary>Non-secret parameter key carrying the bicep-repo git SHA (feeds idempotency key).</summary>
    public const string BicepVersionParameterKey = "bicepVer";

    /// <summary>Non-secret parameter key carrying the target environment name (dev/staging/prod). Defaults to <c>prod</c> when absent.</summary>
    public const string EnvironmentNameParameterKey = "environmentName";

    /// <summary>Non-secret parameter key carrying the target Azure region. Defaults to <c>westus2</c> when absent.</summary>
    public const string LocationParameterKey = "location";

    /// <summary>Non-secret parameter key carrying the SignalR feature flag (ADR-032). Defaults to <c>false</c> when absent.</summary>
    public const string SignalREnabledParameterKey = "signalrEnabled";

    /// <summary>Non-secret parameter key carrying the auth-v4 §9.1 secret-free gate (customer-provisioning-orchestration-r1 punch row A38b). Defaults to <c>false</c> when absent (pre-migration/backwards-compat behavior).</summary>
    public const string RequireSecretFreeIdentityParameterKey = "requireSecretFreeIdentity";

    /// <summary>
    /// Non-secret parameter key carrying an ISO-8601 timestamp for
    /// <c>sprk_dataverseenvironment.sprk_provisionedon</c>. When present +
    /// non-empty, H2a runs in upgrade mode (spec.md FR-34 / design.md §14A.5).
    /// The upstream handler (or L2 endpoint) is responsible for populating
    /// this from the registry lookup — H2a stays Dataverse-client-free for
    /// wave C4.
    /// </summary>
    public const string ProvisionedOnParameterKey = "provisionedOn";

    /// <summary>Default target environment when the parameter is absent.</summary>
    private const string DefaultEnvironmentName = "prod";

    /// <summary>Default target Azure region when the parameter is absent (parity with <c>customer.bicep</c>).</summary>
    private const string DefaultLocation = "westus2";

    private readonly IProvisioningRunRepository _repository;
    private readonly IBicepDeployRunner _runner;
    private readonly IArmKeyVaultRefProbe _armProbe;
    private readonly IUpgradeDriftDetector _driftDetector;
    private readonly IBicepTemplateInspector _templateInspector;
    private readonly IResourceNameAvailabilityProbe _nameAvailabilityProbe;
    private readonly BicepInfraDeployOptions _options;
    private readonly ILogger<H2aBicepInfraDeployHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H2a Bicep-infra-deploy handler. All collaborators are
    /// interface-abstracted so unit tests can substitute stubs for each seam.
    /// </summary>
    public H2aBicepInfraDeployHandler(
        IProvisioningRunRepository repository,
        IBicepDeployRunner runner,
        IArmKeyVaultRefProbe armProbe,
        IUpgradeDriftDetector driftDetector,
        IBicepTemplateInspector templateInspector,
        IResourceNameAvailabilityProbe nameAvailabilityProbe,
        IOptions<BicepInfraDeployOptions> options,
        ILogger<H2aBicepInfraDeployHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(armProbe);
        ArgumentNullException.ThrowIfNull(driftDetector);
        ArgumentNullException.ThrowIfNull(templateInspector);
        ArgumentNullException.ThrowIfNull(nameAvailabilityProbe);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _runner = runner;
        _armProbe = armProbe;
        _driftDetector = driftDetector;
        _templateInspector = templateInspector;
        _nameAvailabilityProbe = nameAvailabilityProbe;
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
            // mismatch here means a dispatch bug — fail loud rather than
            // silently mis-executing (parity with H0PreflightHandler).
            throw new InvalidOperationException(
                $"H2aBicepInfraDeployHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H2a Bicep infra-deploy starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H2a aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: BicepDeployRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;

        // (2) §4D I1 tenant guard — H2a MUST NOT fall back to a default tenant.
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            var diagnostic =
                "Run parameter 'tenantId' is required by H2a (§4D I1 no-hardcoded-tenant). " +
                "Upstream handler (H0.5 for Model 2, L2 endpoint for Model 1) MUST populate this before H2a dispatches.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                BicepDeployRejectionCodes.MissingTenantId, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (3) Subscription-id guard (ADR-027 D4 — deploy to customer sub, never platform).
        if (!TryGetNonEmpty(parameters, SubscriptionIdParameterKey, out var subscriptionId))
        {
            var diagnostic =
                "Run parameter 'subscriptionId' is required by H2a (ADR-027 D4 — subscription-per-customer). " +
                "H1 subscription-readiness handler MUST populate this before H2a dispatches.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                BicepDeployRejectionCodes.MissingSubscriptionId, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (4) Bicep-version guard — feeds the idempotency key. Absence means
        // level-3 dedup would collide across upgrades → refuse.
        if (!TryGetNonEmpty(parameters, BicepVersionParameterKey, out var bicepVer))
        {
            var diagnostic =
                "Run parameter 'bicepVer' is required by H2a (idempotency key: infra-{customerId}-{bicepVer}). " +
                "bicepVer MUST be the git SHA of infrastructure/bicep/ per POML constraint / design.md §4.1 preamble.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                BicepDeployRejectionCodes.MissingBicepVersion, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (4.5) EXEC-04 (pre-dispatch audit 2026-08-27, Wave 2 remediation):
        //       fail fast on blank TenancyModel. A silent fallback to
        //       "Model2Dedicated" here would deploy an entire per-customer
        //       stack (~$400/mo baseline) for a Model 1 shared trial customer —
        //       cost blow-up + tenancy-invariant violation (§4D I1 no-silent-
        //       default) detectable only at H13 (E13 cost-envelope checker),
        //       AFTER Bicep succeeded. Fail here, before any Bicep parameter
        //       assembly, so the operator gets a specific rejection code + no
        //       Azure API call happens with a defaulted tenancy model.
        if (string.IsNullOrWhiteSpace(run.TenancyModel))
        {
            var diagnostic =
                $"ProvisioningRun.TenancyModel is blank / whitespace for customerId '{envelope.CustomerId}'. " +
                "H2a MUST NOT silently default to 'Model2Dedicated' — that would deploy an entire per-customer " +
                "stack for a Model 1 shared trial (~$400/mo cost blow-up + tenancy invariant violation, only " +
                "detectable at H13 E13-cost-envelope). Upstream (intake schema + L2 CreateRun endpoint) MUST " +
                "populate TenancyModel with a valid value ('Model1Shared' or 'Model2Dedicated') before H2a dispatches.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                BicepDeployRejectionCodes.MissingTenancyModel, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, bicepVer);

        // (5) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H2a idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (6) Assemble the deploy request from run parameters + tenancy model.
        var environmentName = TryGetNonEmpty(parameters, EnvironmentNameParameterKey, out var env)
            ? env
            : DefaultEnvironmentName;
        var location = TryGetNonEmpty(parameters, LocationParameterKey, out var loc)
            ? loc
            : DefaultLocation;
        var signalrEnabled = TryGetNonEmpty(parameters, SignalREnabledParameterKey, out var signalRaw)
            && bool.TryParse(signalRaw, out var signalParsed)
            && signalParsed;
        var requireSecretFreeIdentity = TryGetNonEmpty(parameters, RequireSecretFreeIdentityParameterKey, out var secretFreeRaw)
            && bool.TryParse(secretFreeRaw, out var secretFreeParsed)
            && secretFreeParsed;

        var request = new BicepDeployRequest(
            CustomerId: envelope.CustomerId,
            TenantId: tenantId,
            SubscriptionId: subscriptionId,
            // EXEC-04: guarded above (step 4.5) — TenancyModel is non-empty here.
            TenancyModel: run.TenancyModel,
            BicepVersion: bicepVer,
            EnvironmentName: environmentName,
            Location: location,
            SignalREnabled: signalrEnabled,
            RequireSecretFreeIdentity: requireSecretFreeIdentity);

        // (7) Structural pre-flight — Redis presence + model-version pin.
        //     These are cheap file reads; fail fast before ARM traffic.
        try
        {
            var inspection = await _templateInspector.InspectAsync(request, cancellationToken).ConfigureAwait(false);
            if (inspection.ContainsRedisResource)
            {
                var diagnostic =
                    $"Bicep template contains a Redis resource — violates spec MUST rule + Q-E FR-12 " +
                    $"(Redis is per-environment via scripts/Deploy-RedisCache.ps1, NOT per-customer). " +
                    $"Reference: {inspection.RedisReference}";
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    BicepDeployRejectionCodes.RedisProvisioningForbidden, diagnostic, cancellationToken).ConfigureAwait(false);
            }
            if (inspection.HasUnpinnedModelDeployment)
            {
                var diagnostic =
                    $"Bicep template contains an unpinned OpenAI model deployment — violates ADR-020 " +
                    $"(pinned versions required: gpt-4o 2024-08-06, gpt-4o-mini 2024-07-18, text-embedding-3-large 1). " +
                    $"Reference: {inspection.UnpinnedModelReference}";
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    BicepDeployRejectionCodes.ModelVersionNotPinned, diagnostic, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H2a template-inspector infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"Template inspector infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Verify BicepInfraDeploy:BicepDirectory resolves to the infrastructure/bicep tree in the L2 publish output.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                BicepDeployRejectionCodes.BicepDeployFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (7.5) HANDLER-05 (Wave 2 pre-dispatch remediation 2026-08-27) — F10:
        //       check globally-namespaced resource names for availability
        //       BEFORE the ~20 min Bicep deploy tries to create them and
        //       fails 90-180s in on a global collision (F10 verbatim: burned
        //       16m35s on the SESSION 2 first deploy because a Service Bus
        //       `-sb` suffix was already reserved globally). Runs AFTER the
        //       inspector (per punchlist: "wire into H2aBicepInfraDeployHandler
        //       after inspector but before runner") and BEFORE the upgrade-
        //       drift branch since an upgrade run against existing resources
        //       will NOT collide (the customer's own resources will report as
        //       "unavailable — already owned by you", which the probe
        //       correctly treats as a domain conflict; skip the check on
        //       upgrade runs to avoid a false positive).
        if (!TryGetNonEmpty(parameters, ProvisionedOnParameterKey, out _))
        {
            try
            {
                var nameCheckRequest = new ResourceNameAvailabilityRequest(
                    SubscriptionId: subscriptionId,
                    Names: BuildGloballyNamespacedNameChecks(envelope.CustomerId, environmentName));
                var nameResult = await _nameAvailabilityProbe
                    .CheckAvailabilityAsync(nameCheckRequest, cancellationToken).ConfigureAwait(false);
                if (nameResult is ResourceNameAvailabilityResult.Conflict conflict)
                {
                    var diagnostic =
                        $"Globally-namespaced resource name collision: {conflict.Kind} name '{conflict.ConflictingName}' " +
                        $"is unavailable ({conflict.Reason}). H2a fails fast per HANDLER-05 (F10 remediation) — " +
                        "operator must rename the resource in the Bicep template (or wait for the current owner " +
                        "to release the name) before re-running H2a. Sparing the 20 min deploy window a global-name " +
                        "collision would otherwise burn.";
                    return await FailAsync(run, etag, FailureClass.Resumable,
                        BicepDeployRejectionCodes.ResourceNameTaken, diagnostic, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Infra fault (ARM SDK connection drop, etc.) — do NOT block
                // the deploy on a probe-side outage; log + proceed. The Bicep
                // deploy itself will surface real collisions if any exist.
                _logger.LogWarning(ex,
                    "H2a resource-name availability probe infra fault (proceeding): " +
                    "runId={RunId} customerId={CustomerId}",
                    envelope.RunId, envelope.CustomerId);
            }
        }

        // (8) Upgrade-mode branch: if `provisionedOn` is populated, run
        //     what-if FIRST + REJECT on drift.
        if (TryGetNonEmpty(parameters, ProvisionedOnParameterKey, out var provisionedOnRaw))
        {
            _logger.LogInformation(
                "H2a upgrade mode detected: runId={RunId} customerId={CustomerId} provisionedOn={ProvisionedOn}",
                envelope.RunId, envelope.CustomerId, provisionedOnRaw);
            try
            {
                var driftResult = await _driftDetector.DetectDriftAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                if (driftResult is UpgradeDriftDetectionResult.DriftDetected drift)
                {
                    var driftReportPath = await WriteDriftReportAsync(
                        envelope.CustomerId, drift.DriftReport, cancellationToken).ConfigureAwait(false);
                    var diagnostic =
                        $"Upgrade-mode drift detected for '{envelope.CustomerId}' — default REJECT per §14A.5 Option B. " +
                        $"Drift report: {driftReportPath}. Operator must reconcile drift before re-running H2a " +
                        $"(accept-and-apply would silently overwrite operator-intended state).";
                    return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                        BicepDeployRejectionCodes.UpgradeModeDrift, diagnostic, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "H2a upgrade drift-detector infrastructure fault: runId={RunId} customerId={CustomerId}",
                    envelope.RunId, envelope.CustomerId);
                var diagnostic =
                    $"Upgrade drift detector infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                    "Verify az CLI is on PATH + the operator has 'Reader' RBAC on the target subscription.";
                return await FailAsync(run, etag, FailureClass.Resumable,
                    BicepDeployRejectionCodes.UpgradeModeDrift, diagnostic, cancellationToken).ConfigureAwait(false);
            }
        }

        // (9) Invoke the deploy runner. This is the long-running (10–20 min)
        //     step per FR-22 / R20 — the reconciler owns keeping the ambient
        //     Service Bus lock alive.
        BicepDeployOutcome outcome;
        try
        {
            outcome = await _runner.DeployAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H2a Bicep-deploy infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"Bicep deploy infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Partial resource state may exist — treated as Quarantine-required per §4C.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                BicepDeployRejectionCodes.BicepDeployFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        if (outcome is BicepDeployOutcome.Failure runnerFailure)
        {
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                BicepDeployRejectionCodes.BicepDeployFailed, runnerFailure.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        var outputs = ((BicepDeployOutcome.Success)outcome).Outputs;
        if (!AreOutputsComplete(outputs))
        {
            var diagnostic =
                $"Bicep deploy for '{envelope.CustomerId}' completed but returned incomplete outputs " +
                "(one or more required fields blank: UAMI resource id / object id / client id / " +
                "AppService name+staging slot / OpenAI+AISearch+Cosmos endpoints). " +
                "Verify infrastructure/bicep outputs surface all fields required by BicepDeployOutputs.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                BicepDeployRejectionCodes.BicepDeployOutputsIncomplete, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (10) T1 post-condition — verify keyVaultReferenceIdentity == UAMI on
        //      BOTH slots. This is the entire reason H2a exists as a handler
        //      wrapping the script rather than the script alone (POML
        //      "This handler is the source of truth for T1 clearance.").
        ArmKeyVaultRefProbeResult probeResult;
        try
        {
            var probeInput = new ArmKeyVaultRefProbeInput(
                SubscriptionId: subscriptionId,
                ResourceGroupName: outputs.ResourceGroupName,
                AppServiceName: outputs.AppServiceName,
                StagingSlotName: outputs.AppServiceStagingSlotName,
                ExpectedUserAssignedIdentityResourceId: outputs.UserAssignedIdentityResourceId);
            probeResult = await _armProbe.VerifyKeyVaultReferenceIdentityAsync(probeInput, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H2a T1 probe infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"T1 probe infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Bicep deploy succeeded but T1 verification could not run — treat as Quarantine-required " +
                "so the operator inspects App Service ARM state before downstream handlers depend on KV refs.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                BicepDeployRejectionCodes.TrapT1KeyVaultReferenceIdentityMismatch, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        if (probeResult is ArmKeyVaultRefProbeResult.Mismatch mismatch)
        {
            var diagnostic =
                $"T1 trap NOT cleared for App Service '{outputs.AppServiceName}': expected " +
                $"keyVaultReferenceIdentity == '{outputs.UserAssignedIdentityResourceId}' on both slots; " +
                $"observed prod='{mismatch.ObservedProductionSlotIdentity ?? "(null)"}', " +
                $"staging='{mismatch.ObservedStagingSlotIdentity ?? "(null)"}'. " +
                "Downstream BFF boot will fail resolving @Microsoft.KeyVault(...) refs — Bicep must PATCH " +
                "the identity on both slots before H2a can report success.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                BicepDeployRejectionCodes.TrapT1KeyVaultReferenceIdentityMismatch, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (11) All post-conditions cleared — write outputs to interStepState +
        //      advance Cosmos state. Downstream reconciler (wave C5) fans out
        //      to H2b / H4 / H5.
        stopwatch.Stop();
        _logger.LogInformation(
            "H2a Bicep infra-deploy succeeded: runId={RunId} customerId={CustomerId} durationMs={DurationMs} " +
            "tenancyModel={TenancyModel} signalRDeployed={SignalRDeployed}",
            envelope.RunId, envelope.CustomerId, stopwatch.ElapsedMilliseconds,
            request.TenancyModel, outputs.SignalRDeployed);

        return await MarkCompleteAsync(run, etag, idempotencyKey, outputs, envelope, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// HANDLER-05 (Wave 2 pre-dispatch remediation 2026-08-27): builds the
    /// list of (kind, name) tuples the resource-name availability probe
    /// checks BEFORE the Bicep deploy fires. Names MUST mirror the
    /// customer.bicep naming convention verbatim (any drift = false
    /// positives / negatives):
    ///   - Storage: <c>take(toLower(replace('sprk{customerId}{env}sa', '-', '')), 24)</c>
    ///     (customer.bicep line 141)
    ///   - Service Bus namespace: <c>sprk-{customerId}-{env}-sb</c>
    ///     (F10 verbatim mention: "Service Bus `-sb` suffix was already reserved
    ///     globally"; matches customer.bicep's serviceBusNamespaceName pattern)
    /// Key Vault is omitted for now (see <see cref="ResourceNameKind.KeyVault"/>
    /// enum comment — Azure.ResourceManager.KeyVault not currently a project
    /// dependency). Exposed <c>internal</c> so unit tests can validate the
    /// naming logic without invoking the handler.
    /// </summary>
    internal static IReadOnlyList<ResourceNameCheckEntry> BuildGloballyNamespacedNameChecks(
        string customerId,
        string environmentName)
    {
        var baseName = $"sprk{customerId}{environmentName}";
        // Storage: lowercase + no hyphens + 24-char cap (customer.bicep line 141).
        var storageName = TruncateTo(
            $"{baseName}sa".ToLowerInvariant().Replace("-", string.Empty, StringComparison.Ordinal),
            24);
        // Service Bus namespace: keep hyphens for readability (SB name rules
        // allow hyphens); 50-char cap per ARM (well within customer id +
        // env-name budget). F10 verbatim reference.
        var sbName = $"sprk-{customerId}-{environmentName}-sb";
        return new[]
        {
            new ResourceNameCheckEntry(ResourceNameKind.StorageAccount, storageName),
            new ResourceNameCheckEntry(ResourceNameKind.ServiceBusNamespace, sbName),
        };
    }

    private static string TruncateTo(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max);

    /// <summary>
    /// Computes the deterministic H2a idempotency key:
    /// <c>infra-{customerId}-{bicepVer}</c>. Exposed internal so unit tests
    /// can construct expected keys without duplicating the format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, string bicepVer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bicepVer);
        return $"infra-{customerId}-{bicepVer}";
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

    private static bool AreOutputsComplete(BicepDeployOutputs outputs)
        => !string.IsNullOrWhiteSpace(outputs.ResourceGroupName)
        && !string.IsNullOrWhiteSpace(outputs.UserAssignedIdentityResourceId)
        && !string.IsNullOrWhiteSpace(outputs.UserAssignedIdentityObjectId)
        && !string.IsNullOrWhiteSpace(outputs.UserAssignedIdentityClientId)
        && !string.IsNullOrWhiteSpace(outputs.AppServiceName)
        && !string.IsNullOrWhiteSpace(outputs.AppServiceStagingSlotName)
        && !string.IsNullOrWhiteSpace(outputs.OpenAiEndpoint)
        && !string.IsNullOrWhiteSpace(outputs.AiSearchEndpoint)
        && !string.IsNullOrWhiteSpace(outputs.CosmosEndpoint);

    private async Task<string> WriteDriftReportAsync(
        string customerId,
        string driftReport,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.RunNotesDirectory);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(_options.RunNotesDirectory, $"drift-{customerId}-{timestamp}.md");
        var body =
            $"# H2a upgrade-mode drift report\n\n" +
            $"- **Customer**: `{customerId}`\n" +
            $"- **Timestamp (UTC)**: {DateTimeOffset.UtcNow:O}\n" +
            $"- **Decision**: REJECT (§14A.5 default Option B)\n\n" +
            $"## Raw `az deployment sub what-if` output\n\n```\n{driftReport}\n```\n";
        await File.WriteAllTextAsync(reportPath, body, cancellationToken).ConfigureAwait(false);
        return reportPath;
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

        run.GateStates[$"h2a-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H2a failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H2a failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        BicepDeployOutputs outputs,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier; // Reconciler observes + fans out to H2b/H4/H5.
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        run.ErrorDetail = null;

        // Populate interStepState (design.md §6.2) — one write per key.
        run.InterStepState.OpenAiEndpoint = outputs.OpenAiEndpoint;
        run.InterStepState.AiSearchEndpoint = outputs.AiSearchEndpoint;
        run.InterStepState.CosmosEndpoint = outputs.CosmosEndpoint;
        run.InterStepState.MiObjectId = outputs.UserAssignedIdentityObjectId;
        run.InterStepState.MiClientId = outputs.UserAssignedIdentityClientId;

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H2a success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: BicepDeployRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H2a read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H2a.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H2a success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: BicepDeployRejectionCodes.RunDeletedDuringDeploy,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H2a was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
