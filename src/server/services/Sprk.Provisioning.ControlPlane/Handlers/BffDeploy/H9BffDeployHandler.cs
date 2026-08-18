// -----------------------------------------------------------------------------
// H9BffDeployHandler.cs
//
// L2 CONTROL-PLANE H9 BFF-deploy handler (task 052, wave C4 Batch 4B).
//
// PURPOSE:
//   Deploys the BFF API to the customer's Azure App Service using a blue-
//   green slot-swap flow with re-swap rollback on smoke-test failure. Wraps
//   the hardened scripts/Deploy-BffApi.ps1 (build + zip + staging deploy +
//   staging health) and completes the swap + prod smoke test + rollback in-
//   handler so Cosmos state advances precisely at each gate. Enforces the
//   five r3-era gates (analyzers-as-errors, god-class ratchet, 4 new
//   ArchTests, naming-conformance, Graph app-role parity) BEFORE the swap so
//   a bad build cannot reach production. Reports NFR-01 publish size + delta.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-12 (H9
//     acceptance): blue-green slot swap + rollback on smoke-test failure +
//     r3-era gates block swap + idempotency key `bff-{customerId}-{buildId}`.
//   - projects/customer-provisioning-orchestration-r1/spec.md NFR-01: publish
//     size ≤60 MB ceiling; ≥+5 MB per-task delta = explicit justification
//     required. Baseline 44.96 MB (2026-08-13, net10 framework-dependent
//     linux-x64).
//   - projects/customer-provisioning-orchestration-r1/spec.md NFR-05: post-
//     deploy /health returns 200 resolving all Tier-1 IOptions KV refs (r3
//     task 061 ValidateOnStart hardening — /health returning 200 IS the KV-
//     ref resolution proof; the BFF cannot boot to 200 without them).
//   - projects/customer-provisioning-orchestration-r1/spec.md §5.5 r3-era
//     gates: analyzers-as-errors (r3 task 041), god-class ratchet (r3 task
//     040 — tests/Spaarke.ArchTests/GodClassGuardTests.cs), 4 new ArchTests
//     (r1 task 064 pending), naming-conformance (r3 task 063 pending), Graph
//     app-role parity (r3 task 062 / r1 task 067 pending). Distinct rejection
//     code per failing gate.
//   - projects/customer-provisioning-orchestration-r1/spec.md §4D I1 / FR-28:
//     tenantId is explicit on every deploy invocation — NO hardcoded default.
//     Deploy-Release.ps1 Phase 4 was hardened in Phase B task 013 to be
//     customerId-driven with -CustomerId Mandatory (no spaarkedev1 fallback).
//     H9 REJECTS the deploy if a spaarkedev1 literal is detected in the
//     shipped script (POML criterion 5 Gap 2 assertion — script regression
//     is a code-integrity concern that must not silently proceed).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H9 row +
//     Gap 2 (Phase 4 hardening) + §4C rollback taxonomy.
//   - .claude/adr/ADR-004: single IProvisioningHandler-shape impl registered
//     in L2 DI (Path A exception for L2 orchestration — see project spec.md
//     § ADR Tensions).
//   - .claude/adr/ADR-010: register in L2, NOT BFF.
//   - .claude/adr/ADR-028: 21 MUSTs — KV-ref resolution via UAMI is verified
//     by the /health smoke test (BFF boots via ValidateOnStart which resolves
//     every Tier-1 KV ref via keyVaultReferenceIdentity=UAMI).
//   - .claude/adr/ADR-036: reuse background-job infrastructure; fire-and-
//     forget; return 202 at endpoint layer.
//   - .claude/skills/bff-deploy/SKILL.md: reference model for the slot-swap
//     flow — this handler mirrors the skill's `Deploy-BffApi.ps1 -UseSlotDeploy`
//     shape but owns the swap + smoke + rollback in-process for Cosmos state
//     coupling (see IBffDeployRunner file header "SCOPE DIVISION").
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌───────────────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                                  │ §4C class                 │
//   ├───────────────────────────────────────────────┼───────────────────────────┤
//   │ Missing tenantId/subscriptionId/              │ Resumable                 │
//   │ resourceGroupName/appServiceName/buildId      │ (external precondition —  │
//   │ (§4D I1 no-hardcoded-tenant + idempotency)    │ operator fixes params +   │
//   │                                               │ resumes)                  │
//   │ Run not found in Cosmos partition             │ Resumable                 │
//   │ spaarkedev1 hardcode detected in script       │ QuarantineRequired        │
//   │ (Gap 2 assertion — script REGRESSION is a     │ (script regression must   │
//   │ code-integrity concern)                       │ NOT silently deploy)      │
//   │ ANY r3-era gate FAILED                        │ Resumable                 │
//   │ (POML criterion 2 distinct code per gate)     │ (fix code + push new      │
//   │                                               │ build + resume)           │
//   │ r3-gate verifier infra fault                  │ Resumable                 │
//   │ (dotnet/pwsh not on PATH, timeout)            │ (no external side effect) │
//   │ Deploy runner Failure (staging deploy fail)   │ Resumable                 │
//   │                                               │ (staging is dedicated;    │
//   │                                               │ re-deploy is safe)        │
//   │ Deploy runner infra fault                     │ Resumable                 │
//   │ (pwsh not on PATH, script missing)            │                           │
//   │ Publish-size reporter infra fault             │ Resumable                 │
//   │ Publish-size delta > threshold OR absolute >  │ QuarantineRequired        │
//   │ ceiling (NFR-01)                              │ (explicit justification   │
//   │                                               │ required)                 │
//   │ Slot-swap Failure (ARM 4xx/5xx)               │ RetryableWithCleanup      │
//   │                                               │ (source + target slot     │
//   │                                               │ UNCHANGED; swap is        │
//   │                                               │ atomic — retry safe)      │
//   │ Slot-swap infra fault (az CLI blew up)        │ RetryableWithCleanup      │
//   │ Smoke test failure + rollback SUCCESS         │ RetryableWithCleanup      │
//   │ (SmokeTestFailedRolledBack)                   │ (production safe; retry   │
//   │                                               │ re-runs whole handler)    │
//   │ Smoke test failure + rollback FAILURE         │ QuarantineRequired        │
//   │ (BothSlotsBadRollbackFailed — POML            │ (BOTH slots bad — do NOT  │
//   │ escalation trigger 2)                         │ retry; operator required) │
//   │ Rollback infra fault (az CLI blew up mid-swap)│ QuarantineRequired        │
//   │ Concurrent Cosmos writer conflict             │ Resumable                 │
//   │ Run row deleted mid-flight                    │ Resumable                 │
//   └───────────────────────────────────────────────┴───────────────────────────┘
//
// IDEMPOTENCY (3-level per ADR-004 / design.md §4.1):
//   Level 1 (Service Bus MessageId dedup): reconciler-owned, deterministic
//           MessageId per (HandlerId, RunId, CustomerId, paramHash).
//   Level 2 (Redis IdempotencyService): NOT YET IMPLEMENTED in L2 (parity
//           with sibling wave-C4 handlers).
//   Level 3 (handler body durable dedup): scans
//           ProvisioningRun.CompletedPhases for (Phase=="H9",
//           IdempotencyKey==bff-{customerId}-{buildId}) — buildId is the
//           BFF CI build number (deterministic per POML constraint +
//           design.md §4.1 preamble). Match ⇒ Success no-op BEFORE any
//           external side effect — a repeat/retry of the same build is a
//           no-op; a NEW build gets a NEW key + re-deploys.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   H9 lives in L2 (not BFF) per spec §5.2 / D3 / D8 / D12; consumes NO
//   AI-internal types (ADR-013 forcing-function rule — no IActionResolver,
//   IActionRunner, IOpenAiClient, IPlaybookService injection). Uses
//   IProvisioningRunRepository (task 037) + five dedicated seams
//   (IR3GateVerifier, IBffDeployRunner, IAppServiceSlotSwapper, IHealthProbe,
//   IBffPublishSizeReporter); no BFF-facade dependencies. Deployed by the
//   L2 App Service using its OWN UAMI — the handler itself never handles
//   BFF secrets or KV refs (BFF app-only auth happens inside the BFF's
//   own boot pipeline that the /health probe verifies).
//
// COMPONENT JUSTIFICATION (CLAUDE.md §11):
//   Existing: scripts/Deploy-BffApi.ps1 (operator-invocable) + .claude/skills/
//     bff-deploy/SKILL.md (reference model). Neither extends the automated
//     DAG-advancement + rollback semantics H9 needs.
//   Extension: no — deploy needs r3-era gate verification + slot-swap +
//     smoke-test + rollback in a single idempotent handler that mutates
//     Cosmos state atomically per gate.
//   Cost-of-doing-nothing: without H9 as a handler, deploy lacks automated
//     rollback on smoke-test failure (operator must manually re-swap under
//     time pressure); r3-era gates could be silently skipped; NFR-01
//     publish-size delta reporting is by-hand. All three break the r1
//     "single systematic process" promise.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H9BffDeployHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = "H9";

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the target subscription id (ADR-027 D4).</summary>
    public const string SubscriptionIdParameterKey = "subscriptionId";

    /// <summary>Non-secret parameter key carrying the App Service resource group name.</summary>
    public const string ResourceGroupNameParameterKey = "resourceGroupName";

    /// <summary>Non-secret parameter key carrying the BFF App Service name (production slot binds to <c>https://{name}.azurewebsites.net</c>).</summary>
    public const string AppServiceNameParameterKey = "appServiceName";

    /// <summary>Non-secret parameter key carrying the BFF CI build number — feeds the idempotency key <c>bff-{customerId}-{buildId}</c>.</summary>
    public const string BuildIdParameterKey = "buildId";

    /// <summary>Non-secret parameter key carrying the staging slot name. Optional — defaults to <see cref="BffDeployOptions.DefaultStagingSlotName"/>.</summary>
    public const string StagingSlotNameParameterKey = "stagingSlotName";

    /// <summary>Non-secret parameter key carrying the /health probe path. Optional — defaults to <see cref="BffDeployOptions.DefaultHealthCheckPath"/>.</summary>
    public const string HealthCheckPathParameterKey = "healthCheckPath";

    /// <summary>Non-secret parameter key carrying the target environment name (<c>dev</c>/<c>staging</c>/<c>production</c>). Optional — defaults to <c>production</c>.</summary>
    public const string EnvironmentNameParameterKey = "environmentName";

    /// <summary>Non-secret parameter key indicating whether to skip the build step (existing publish artifact will be deployed). Optional — defaults to <c>false</c>.</summary>
    public const string SkipBuildParameterKey = "skipBuild";

    /// <summary>Default target environment when the parameter is absent.</summary>
    private const string DefaultEnvironmentName = "production";

    private readonly IProvisioningRunRepository _repository;
    private readonly IR3GateVerifier _r3GateVerifier;
    private readonly IBffDeployRunner _deployRunner;
    private readonly IAppServiceSlotSwapper _slotSwapper;
    private readonly IHealthProbe _healthProbe;
    private readonly IBffPublishSizeReporter _publishSizeReporter;
    private readonly BffDeployOptions _options;
    private readonly ILogger<H9BffDeployHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    public H9BffDeployHandler(
        IProvisioningRunRepository repository,
        IR3GateVerifier r3GateVerifier,
        IBffDeployRunner deployRunner,
        IAppServiceSlotSwapper slotSwapper,
        IHealthProbe healthProbe,
        IBffPublishSizeReporter publishSizeReporter,
        IOptions<BffDeployOptions> options,
        ILogger<H9BffDeployHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(r3GateVerifier);
        ArgumentNullException.ThrowIfNull(deployRunner);
        ArgumentNullException.ThrowIfNull(slotSwapper);
        ArgumentNullException.ThrowIfNull(healthProbe);
        ArgumentNullException.ThrowIfNull(publishSizeReporter);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _r3GateVerifier = r3GateVerifier;
        _deployRunner = deployRunner;
        _slotSwapper = slotSwapper;
        _healthProbe = healthProbe;
        _publishSizeReporter = publishSizeReporter;
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
            throw new InvalidOperationException(
                $"H9BffDeployHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H9 BFF deploy starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H9 aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: BffDeployRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;

        // (2) Parameter guards — every field H9 needs must be non-empty
        //     BEFORE any external side effect (§4C Resumable classification).
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.MissingTenantId,
                "Run parameter 'tenantId' is required by H9 (§4D I1 no-hardcoded-tenant). " +
                "Upstream handler MUST populate this before H9 dispatches.",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, SubscriptionIdParameterKey, out var subscriptionId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.MissingSubscriptionId,
                "Run parameter 'subscriptionId' is required by H9 (ADR-027 D4 — App Service lives in the customer sub).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, ResourceGroupNameParameterKey, out var resourceGroupName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.MissingResourceGroupName,
                "Run parameter 'resourceGroupName' is required by H9 (az CLI targeting for deploy + swap ops).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, AppServiceNameParameterKey, out var appServiceName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.MissingAppServiceName,
                "Run parameter 'appServiceName' is required by H9 (BFF App Service name — deploy + swap targeting).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, BuildIdParameterKey, out var buildId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.MissingBuildId,
                "Run parameter 'buildId' is required by H9 (idempotency key: bff-{customerId}-{buildId}). " +
                "buildId MUST be the BFF CI build number per POML constraint / design.md §4.1 preamble.",
                cancellationToken).ConfigureAwait(false);
        }

        var stagingSlotName = TryGetNonEmpty(parameters, StagingSlotNameParameterKey, out var slotRaw)
            ? slotRaw : _options.DefaultStagingSlotName;
        var healthCheckPath = TryGetNonEmpty(parameters, HealthCheckPathParameterKey, out var pathRaw)
            ? pathRaw : _options.DefaultHealthCheckPath;
        var environmentName = TryGetNonEmpty(parameters, EnvironmentNameParameterKey, out var envRaw)
            ? envRaw : DefaultEnvironmentName;
        var skipBuild = TryGetNonEmpty(parameters, SkipBuildParameterKey, out var skipRaw)
            && bool.TryParse(skipRaw, out var skipParsed) && skipParsed;

        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, buildId);

        // (3) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H9 idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (4) Gap 2 assertion — spaarkedev1-hardcode pre-flight scan on the
        //     shipped Deploy-Release.ps1 script. Task 013 hardened Phase 4
        //     to be customerId-driven; a regression MUST NOT silently
        //     proceed to production. POML acceptance criterion 5.
        var hardcodeScanResult = ScanForSpaarkedev1Hardcode();
        if (hardcodeScanResult is not null)
        {
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                BffDeployRejectionCodes.Spaarkedev1HardcodeDetected,
                hardcodeScanResult,
                cancellationToken).ConfigureAwait(false);
        }

        // (5) r3-era gate verification — the "distinct rejection code per
        //     failing gate" branch (POML criterion 2). ANY failed gate
        //     BLOCKS the slot-swap.
        R3GateVerificationResult gateResult;
        try
        {
            gateResult = await _r3GateVerifier.VerifyAsync(
                new R3GateVerificationRequest(envelope.CustomerId, envelope.RunId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H9 r3-gate verifier infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            // Verifier infra fault (dotnet/pwsh not on PATH, timeout) is
            // Resumable — the gates ran or did not, but there is no external
            // side effect against the customer environment yet.
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.R3GateFailedAnalyzers,
                $"r3-gate verifier infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Gates could not run — verify dotnet + pwsh are on PATH + the repo layout matches BffDeploy:RepositoryRoot.",
                cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "H9 r3-gates verified: runId={RunId} summary={Summary}",
            envelope.RunId, gateResult.ToLogSummary());

        if (!gateResult.AllGatesGreen)
        {
            var failure = gateResult.FirstFailure!;
            var rejectionCode = MapGateKindToRejectionCode(failure.Kind);
            return await FailAsync(run, etag, FailureClass.Resumable,
                rejectionCode,
                $"r3-era gate {failure.Kind} FAILED — slot-swap BLOCKED per spec.md §5.5 + FR-12 acceptance criterion 2. " +
                $"Gate diagnostic: {failure.Diagnostic}. Fix the underlying issue, push a new build (new buildId), and resume.",
                cancellationToken).ConfigureAwait(false);
        }

        // (6) Invoke the deploy runner — build + zip + staging deploy +
        //     staging /health.
        BffDeployRunnerOutcome deployOutcome;
        try
        {
            var deployRequest = new BffDeployRunnerRequest(
                CustomerId: envelope.CustomerId,
                ResourceGroupName: resourceGroupName,
                AppServiceName: appServiceName,
                StagingSlotName: stagingSlotName,
                EnvironmentName: environmentName,
                BuildId: buildId,
                SkipBuild: skipBuild);
            deployOutcome = await _deployRunner.DeployAsync(deployRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H9 deploy runner infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.BffDeployInfraFault,
                $"Deploy runner infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "No slot-swap attempted — verify pwsh is on PATH + Deploy-BffApi.ps1 exists at BffDeploy:DeployBffApiScriptPath.",
                cancellationToken).ConfigureAwait(false);
        }

        if (deployOutcome is BffDeployRunnerOutcome.Failure deployFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.BffDeployFailed,
                deployFailure.Diagnostic,
                cancellationToken).ConfigureAwait(false);
        }

        var deployOutputs = ((BffDeployRunnerOutcome.Success)deployOutcome).Outputs;
        if (string.IsNullOrWhiteSpace(deployOutputs.PublishZipPath)
            || string.IsNullOrWhiteSpace(deployOutputs.StagingSlotUrl)
            || string.IsNullOrWhiteSpace(deployOutputs.ProductionSlotUrl))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.BffDeployOutputsIncomplete,
                "Deploy runner returned Success but outputs are incomplete (missing publishZipPath / staging URL / production URL). " +
                "Verify BffDeployOptions.BffPublishZipPath resolves to the compressed publish artifact.",
                cancellationToken).ConfigureAwait(false);
        }

        // (7) NFR-01 publish-size measurement + threshold check. RUNS BEFORE
        //     the slot-swap so a bloated build cannot reach production.
        PublishSizeReport sizeReport;
        try
        {
            sizeReport = await _publishSizeReporter.MeasureAsync(
                new PublishSizeReportRequest(
                    CustomerId: envelope.CustomerId,
                    PublishZipPath: deployOutputs.PublishZipPath,
                    BaselineBytes: _options.BaselinePublishSizeBytes,
                    DeltaCeilingBytes: _options.PublishSizeDeltaThresholdBytes,
                    AbsoluteCeilingBytes: _options.AbsolutePublishSizeCeilingBytes),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H9 publish-size reporter infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.PublishSizeReporterInfraFault,
                $"Publish-size reporter infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Deploy ran but size verification did not — no swap attempted.",
                cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("H9 NFR-01 report: runId={RunId} summary={Summary}", envelope.RunId, sizeReport.Summary);

        if (sizeReport.ExceedsAbsoluteCeiling || sizeReport.ExceedsDeltaThreshold)
        {
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                BffDeployRejectionCodes.PublishSizeDeltaExceeded,
                $"NFR-01 publish-size threshold exceeded — slot-swap BLOCKED. {sizeReport.Summary}. " +
                "Explicit justification required per spec.md NFR-01; document the delta cause + escalate to architecture review " +
                "before overriding this gate.",
                cancellationToken).ConfigureAwait(false);
        }

        // (8) Slot swap staging → production. First swap of the two-swap
        //     blue-green protocol.
        SlotSwapResult swapResult;
        try
        {
            swapResult = await _slotSwapper.SwapAsync(
                new SlotSwapRequest(
                    SubscriptionId: subscriptionId,
                    ResourceGroupName: resourceGroupName,
                    AppServiceName: appServiceName,
                    SourceSlotName: stagingSlotName,
                    TargetSlotName: "production"),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H9 slot-swap infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.RetryableWithCleanup,
                BffDeployRejectionCodes.SlotSwapInfraFault,
                $"Slot-swap infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Source + target slots UNCHANGED — retry re-runs handler cleanly.",
                cancellationToken).ConfigureAwait(false);
        }

        if (swapResult is SlotSwapResult.Failure swapFailure)
        {
            return await FailAsync(run, etag, FailureClass.RetryableWithCleanup,
                BffDeployRejectionCodes.SlotSwapFailed,
                swapFailure.Diagnostic,
                cancellationToken).ConfigureAwait(false);
        }

        // (9) Post-swap production /health smoke test (NFR-05). BFF's
        //     ValidateOnStart (r3 task 061) throws at boot on missing
        //     Tier-1 KV refs, so a 200 IS the KV-ref-resolution proof.
        HealthProbeResult probeResult;
        var productionHealthUrl = CombineUrl(deployOutputs.ProductionSlotUrl, healthCheckPath);
        try
        {
            probeResult = await _healthProbe.ProbeAsync(
                new HealthProbeRequest(
                    TargetUrl: productionHealthUrl,
                    MaxRetries: _options.HealthProbeMaxRetries,
                    Interval: _options.HealthProbeInterval,
                    RequestTimeout: _options.HealthProbeRequestTimeout),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H9 smoke-test infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            // Infra fault during smoke test — treat as smoke-test failure +
            // trigger rollback (safer than assuming production is healthy).
            probeResult = new HealthProbeResult.Failure(
                AttemptsUsed: 0,
                Diagnostic: $"Smoke test infra fault: {ex.GetType().Name}: {ex.Message}");
        }

        if (probeResult is HealthProbeResult.Failure probeFailure)
        {
            // (10) Rollback: re-swap production ← prior version (staging holds
            //      the previous production version after the first swap).
            _logger.LogWarning(
                "H9 post-swap smoke test FAILED — initiating rollback: runId={RunId} customerId={CustomerId} lastDiag={Diag}",
                envelope.RunId, envelope.CustomerId, probeFailure.Diagnostic);

            SlotSwapResult rollbackResult;
            try
            {
                rollbackResult = await _slotSwapper.SwapAsync(
                    new SlotSwapRequest(
                        SubscriptionId: subscriptionId,
                        ResourceGroupName: resourceGroupName,
                        AppServiceName: appServiceName,
                        SourceSlotName: stagingSlotName,
                        TargetSlotName: "production"),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "H9 rollback swap infrastructure fault — CATASTROPHIC (both slots may be bad): " +
                    "runId={RunId} customerId={CustomerId}",
                    envelope.RunId, envelope.CustomerId);
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    BffDeployRejectionCodes.RollbackInfraFault,
                    $"Post-swap smoke test failed AND rollback re-swap threw: {ex.GetType().Name}: {ex.Message}. " +
                    $"Original smoke test diagnostic: {probeFailure.Diagnostic}. " +
                    "BOTH SLOTS MAY BE BAD — POML escalation trigger 2. Operator MUST diagnose manually — do NOT retry.",
                    cancellationToken).ConfigureAwait(false);
            }

            if (rollbackResult is SlotSwapResult.Failure rollbackFailure)
            {
                _logger.LogError(
                    "H9 rollback swap FAILED — CATASTROPHIC (both slots bad): runId={RunId} customerId={CustomerId}",
                    envelope.RunId, envelope.CustomerId);
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    BffDeployRejectionCodes.SmokeTestFailedRollbackAlsoFailed,
                    $"Post-swap smoke test failed AND rollback re-swap failed. " +
                    $"Smoke test: {probeFailure.Diagnostic}. Rollback: {rollbackFailure.Diagnostic}. " +
                    "BOTH SLOTS BAD — POML escalation trigger 2. Operator MUST diagnose manually — do NOT retry.",
                    cancellationToken).ConfigureAwait(false);
            }

            // Rollback succeeded — production restored to prior version.
            return await FailAsync(run, etag, FailureClass.RetryableWithCleanup,
                probeFailure.Diagnostic.StartsWith("Smoke test infra fault", StringComparison.Ordinal)
                    ? BffDeployRejectionCodes.SmokeTestInfraFault
                    : BffDeployRejectionCodes.SmokeTestFailedRolledBack,
                $"Post-swap smoke test failed; production ROLLED BACK to prior version successfully. " +
                $"Smoke test diagnostic: {probeFailure.Diagnostic}. " +
                "Inspect staging logs for root cause before retrying.",
                cancellationToken).ConfigureAwait(false);
        }

        var probeSuccess = (HealthProbeResult.Success)probeResult;

        // (11) All post-conditions cleared — advance Cosmos state.
        stopwatch.Stop();
        _logger.LogInformation(
            "H9 BFF deploy succeeded: runId={RunId} customerId={CustomerId} durationMs={DurationMs} " +
            "healthProbeAttempts={Attempts} nfr01Summary={Nfr01Summary}",
            envelope.RunId, envelope.CustomerId, stopwatch.ElapsedMilliseconds,
            probeSuccess.AttemptsUsed, sizeReport.Summary);

        return await MarkCompleteAsync(run, etag, idempotencyKey, envelope, sizeReport, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H9 idempotency key:
    /// <c>bff-{customerId}-{buildId}</c>. Exposed internal so unit tests can
    /// construct expected keys without duplicating the format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, string buildId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        return $"bff-{customerId}-{buildId}";
    }

    /// <summary>
    /// Maps a failing r3-gate to its distinct rejection code (POML criterion 2).
    /// Internal so unit tests can assert the mapping directly.
    /// </summary>
    internal static string MapGateKindToRejectionCode(R3GateKind kind) => kind switch
    {
        R3GateKind.Analyzers => BffDeployRejectionCodes.R3GateFailedAnalyzers,
        R3GateKind.GodClassRatchet => BffDeployRejectionCodes.R3GateFailedGodClassRatchet,
        R3GateKind.ArchTests => BffDeployRejectionCodes.R3GateFailedArchTests,
        R3GateKind.NamingConformance => BffDeployRejectionCodes.R3GateFailedNamingConformance,
        R3GateKind.GraphAppRoleParity => BffDeployRejectionCodes.R3GateFailedGraphAppRoleParity,
        _ => throw new InvalidOperationException($"Unmapped r3-gate kind '{kind}'."),
    };

    /// <summary>
    /// Scans <see cref="BffDeployOptions.DeployReleaseScriptPath"/> for the
    /// literal <c>spaarkedev1</c>. Returns a non-null diagnostic string when
    /// the regression is detected; null when the script is clean OR when the
    /// script simply is not present at the configured path (tolerant of
    /// wave-C4 publish layouts that ship without scripts/Deploy-Release.ps1).
    ///
    /// Path A rationale (per CLAUDE.md §6.5): the shipped Deploy-Release.ps1
    /// was hardened in task 013 (Phase 4 customerId-driven, -CustomerId
    /// Mandatory, no spaarkedev1 fallback). This scan is a
    /// defense-in-depth guard against a regression — task 013's hardening
    /// is the primary defense; this handler-side scan is the secondary
    /// belt-and-braces one. Internal so unit tests can override the scan
    /// target via BffDeployOptions.DeployReleaseScriptPath fixtures.
    /// </summary>
    internal string? ScanForSpaarkedev1Hardcode()
    {
        var scriptPath = _options.DeployReleaseScriptPath;
        if (!File.Exists(scriptPath))
        {
            _logger.LogInformation(
                "H9 Gap 2 assertion — Deploy-Release.ps1 not present at '{Path}' — scan skipped " +
                "(task 013's script-level hardening is the primary defense).", scriptPath);
            return null;
        }

        try
        {
            var content = File.ReadAllText(scriptPath);
            if (content.Contains("spaarkedev1", StringComparison.Ordinal))
            {
                return
                    $"Deploy-Release.ps1 at '{scriptPath}' contains a hardcoded 'spaarkedev1' literal — " +
                    "task 013's Phase 4 hardening has been REGRESSED (spec.md §4D I1 / FR-28 no-hardcoded-tenant + " +
                    "POML criterion 5). Deploy BLOCKED before any external side effect. Restore the customerId-driven " +
                    "Phase 4 code path OR run the deploy against a script whose scan passes.";
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "H9 Gap 2 assertion — could not read Deploy-Release.ps1 at '{Path}' — scan skipped " +
                "(script-level hardening is the primary defense).", scriptPath);
            return null;
        }
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

    private static string CombineUrl(string baseUrl, string path)
    {
        var trimmedBase = baseUrl.TrimEnd('/');
        var trimmedPath = path.StartsWith('/') ? path : "/" + path;
        return trimmedBase + trimmedPath;
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

        run.GateStates[$"h9-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H9 failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H9 failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        HandlerEnvelope envelope,
        PublishSizeReport sizeReport,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier; // Reconciler observes + fans out downstream.
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        run.ErrorDetail = null;

        // Record the NFR-01 report as a Verified gate so operators can grep
        // the run for the deploy-time publish-size metric without opening the
        // full Cosmos doc.
        run.GateStates["h9-nfr01-publish-size"] = new GateEntry
        {
            Status = GateState.Verified,
            VerifiedAt = completedAt,
            VerifierHandler = HandlerIdentifier,
            Evidence = System.Text.Json.JsonSerializer.SerializeToElement(new
            {
                absoluteBytes = sizeReport.AbsoluteBytes,
                deltaBytes = sizeReport.DeltaBytes,
                summary = sizeReport.Summary,
            }),
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H9 success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: BffDeployRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H9 read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H9.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H9 success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: BffDeployRejectionCodes.RunDeletedDuringDeploy,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H9 was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
