// -----------------------------------------------------------------------------
// H9BffDeployHandler.cs
//
// L2 CONTROL-PLANE H9 BFF-deploy handler (task 052, wave C4 Batch 4B;
// RE-SCOPED task 132, Wave G-3, per DS-4 §5's artifact-based design).
//
// PURPOSE:
//   Deploys the BFF API to the customer's Azure App Service using a blue-
//   green slot-swap flow with re-swap rollback on smoke-test failure.
//   RE-SCOPED (task 132): consumes the CI-published artifact
//   (task 116's deploy-bff-api.yml pushes `bff-api-{buildId}.zip` +
//   `latest.json` to the `provisioning-artifacts` blob container) instead of
//   running the dotnet-publish build step at provision time. Flow: (1) resolve + verify
//   the artifact manifest (pure C# metadata check — the r3-era gates already
//   ran in CI; this handler only READS their recorded results, hard-blocking
//   if missing/red) → (2) download the artifact blob (Azure.Storage.Blobs,
//   UAMI RBAC, no stored key) → (3) Kudu zip-deploy to the staging slot →
//   (4) health-probe staging (EXISTING HttpHealthProbe, reused unchanged) →
//   (5) NFR-01 publish-size measurement against the downloaded zip →
//   (6) swap staging→production (Azure.ResourceManager.AppService
//   WebSiteSlotResource.SwapSlotAsync — a proper LRO) → (7) verify
//   production → (8) rollback re-swap on failure (EXISTING logic, PRESERVED
//   unchanged). ZERO dotnet-publish build step, ZERO repo checkout, ZERO dotnet SDK
//   dependency at provision time — DeployBffApiScriptRunner and
//   DotnetR3GateVerifier's shell-outs are RETIRED (kept on disk unregistered).
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
//   - notes/design-study-ds4-handler-audit.md §5 — the authoritative H9
//     artifact-based re-scope design this task implements verbatim.
//   - .claude/adr/ADR-004: single IProvisioningHandler-shape impl registered
//     in L2 DI (Path A exception for L2 orchestration — see project spec.md
//     § ADR Tensions).
//   - .claude/adr/ADR-010: register in L2, NOT BFF.
//   - .claude/adr/ADR-028: 21 MUSTs — KV-ref resolution via UAMI is verified
//     by the /health smoke test; the Kudu zip-deploy + slot-swap collaborators
//     authenticate via the SAME shared UAMI-pinned TokenCredential/ArmClient
//     every sibling Class-A handler uses — no stored keys, no operator az
//     login chain.
//   - .claude/adr/ADR-036: reuse background-job infrastructure; fire-and-
//     forget; return 202 at endpoint layer.
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌───────────────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                                  │ §4C class                 │
//   ├───────────────────────────────────────────────┼───────────────────────────┤
//   │ Missing tenantId/subscriptionId/              │ Resumable                 │
//   │ resourceGroupName/appServiceName               │ (external precondition —  │
//   │ (§4D I1 no-hardcoded-tenant)                  │ operator fixes params +   │
//   │                                               │ resumes)                  │
//   │ Run not found in Cosmos partition             │ Resumable                 │
//   │ spaarkedev1 hardcode detected in script       │ QuarantineRequired        │
//   │ (Gap 2 assertion — script REGRESSION is a     │ (script regression must   │
//   │ code-integrity concern)                       │ NOT silently deploy)      │
//   │ Artifact manifest rejected (missing/red gate, │ Resumable                 │
//   │ requested buildId mismatch)                   │ (fix code + push new      │
//   │                                               │ build + resume)           │
//   │ Manifest verifier infra fault                 │ Resumable                 │
//   │ (blob container unreachable, auth failure)    │ (no external side effect) │
//   │ Artifact download failed (blob not found)     │ Resumable                 │
//   │ Artifact download infra fault                 │ Resumable                 │
//   │ Kudu zip-deploy failed (non-2xx)              │ Resumable                 │
//   │                                               │ (staging is dedicated;    │
//   │                                               │ re-deploy is safe)        │
//   │ Kudu zip-deploy infra fault                   │ Resumable                 │
//   │ Staging health-check failed post-deploy       │ Resumable                 │
//   │                                               │ (no production impact)    │
//   │ Publish-size reporter infra fault             │ Resumable                 │
//   │ Publish-size delta > threshold OR absolute >  │ QuarantineRequired        │
//   │ ceiling (NFR-01)                              │ (explicit justification   │
//   │                                               │ required)                 │
//   │ Slot-swap Failure (ARM 4xx/5xx)               │ RetryableWithCleanup      │
//   │                                               │ (source + target slot     │
//   │                                               │ UNCHANGED; swap is        │
//   │                                               │ atomic — retry safe)      │
//   │ Slot-swap infra fault (ARM SDK call threw)    │ RetryableWithCleanup      │
//   │ Smoke test failure + rollback SUCCESS         │ RetryableWithCleanup      │
//   │ (SmokeTestFailedRolledBack)                   │ (production safe; retry   │
//   │                                               │ re-runs whole handler)    │
//   │ Smoke test failure + rollback FAILURE         │ QuarantineRequired        │
//   │ (BothSlotsBadRollbackFailed — POML            │ (BOTH slots bad — do NOT  │
//   │ escalation trigger 2)                         │ retry; operator required) │
//   │ Rollback infra fault (ARM SDK call threw)     │ QuarantineRequired        │
//   │ mid-swap                                       │                           │
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
//           IdempotencyKey==bff-{customerId}-{buildId}). buildId is now
//           RESOLVED (task 132, DS-4 §5 item 1) — either the run's `buildId`
//           parameter (checked EARLY, before any network call, for the fast
//           path) OR, if absent, task 116's latest.json manifest's own
//           buildId (checked AFTER manifest resolution — the only path that
//           requires a network round-trip before the idempotency short-
//           circuit). Match ⇒ Success no-op BEFORE any external side effect;
//           a repeat/retry of the same build is a no-op; a NEW build gets a
//           NEW key + re-deploys.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   H9 lives in L2 (not BFF) per spec §5.2 / D3 / D8 / D12; consumes NO
//   AI-internal types (ADR-013 forcing-function rule — no IActionResolver,
//   IActionRunner, IOpenAiClient, IPlaybookService injection). Uses
//   IProvisioningRunRepository (task 037) + six dedicated seams
//   (IArtifactManifestVerifier, IBffArtifactDownloader, IKuduZipDeployer,
//   IAppServiceSlotSwapper, IHealthProbe, IBffPublishSizeReporter); no BFF-
//   facade dependencies. Deployed by the L2 App Service using its OWN UAMI —
//   the handler itself never handles BFF secrets or KV refs (BFF app-only
//   auth happens inside the BFF's own boot pipeline that the /health probe
//   verifies).
//
// COMPONENT JUSTIFICATION (CLAUDE.md §11):
//   Existing: task 116's deploy-bff-api.yml CI publish (E3) + the EXISTING
//     HttpHealthProbe (already real, reused unchanged) + the EXISTING
//     rollback-re-swap logic (already correct, reused unchanged).
//   Extension: this task extends the artifact-consumption side of task 116's
//     CI-side publish, exactly per DS-4 §5's designed split (CI publishes,
//     handler resolves/verifies/downloads/deploys/swaps).
//   Cost-of-doing-nothing: H9 was "the heaviest environment dependency of
//     all" per DS-4 — it could not execute under Option D's zero-shell,
//     no-dotnet-SDK main site at all without this re-scope; the BFF itself
//     could never be deployed to a customer stamp through the new pipeline.
//
// LIVE-CEREMONY DEPENDENCY (documented, not silent — per task-132 dispatch
// context): the `provisioning-artifacts` blob-push target storage account
// does NOT YET EXIST (item #4 in the Wave G-1 live-ceremony backlog,
// current-task.md). This handler + its collaborators are fully buildable and
// unit-testable today (fake BlobContainerClient / fake HTTP transport, see
// BlobArtifactDownloaderTests.cs / ArtifactManifestVerifierTests.cs /
// KuduZipDeployerTests.cs); a REAL end-to-end run additionally requires the
// storage account to be provisioned + the CI workflow to have published at
// least one build before H9 can resolve a manifest live.
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
    public const string HandlerIdentifier = HandlerIds.H9;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the target subscription id (ADR-027 D4).</summary>
    public const string SubscriptionIdParameterKey = "subscriptionId";

    /// <summary>Non-secret parameter key carrying the App Service resource group name.</summary>
    public const string ResourceGroupNameParameterKey = "resourceGroupName";

    /// <summary>Non-secret parameter key carrying the BFF App Service name (production slot binds to <c>https://{name}.azurewebsites.net</c>).</summary>
    public const string AppServiceNameParameterKey = "appServiceName";

    /// <summary>
    /// Non-secret parameter key carrying the desired BFF CI build number —
    /// feeds the idempotency key <c>bff-{customerId}-{buildId}</c>. OPTIONAL
    /// (task 132, DS-4 §5 item 1) — when absent, H9 resolves the buildId from
    /// task 116's <c>latest.json</c> manifest instead. When present, it MUST
    /// match the manifest's own buildId (latest.json is a single mutable
    /// pointer — H9 has no verified gate data for any other build).
    /// </summary>
    public const string BuildIdParameterKey = "buildId";

    /// <summary>Non-secret parameter key carrying the staging slot name. Optional — defaults to <see cref="BffDeployOptions.DefaultStagingSlotName"/>.</summary>
    public const string StagingSlotNameParameterKey = "stagingSlotName";

    /// <summary>Non-secret parameter key carrying the /health probe path. Optional — defaults to <see cref="BffDeployOptions.DefaultHealthCheckPath"/>.</summary>
    public const string HealthCheckPathParameterKey = "healthCheckPath";

    private readonly IProvisioningRunRepository _repository;
    private readonly IArtifactManifestVerifier _manifestVerifier;
    private readonly IBffArtifactDownloader _artifactDownloader;
    private readonly IKuduZipDeployer _kuduDeployer;
    private readonly IAppServiceSlotSwapper _slotSwapper;
    private readonly IHealthProbe _healthProbe;
    private readonly IBffPublishSizeReporter _publishSizeReporter;
    private readonly BffDeployOptions _options;
    private readonly ILogger<H9BffDeployHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    public H9BffDeployHandler(
        IProvisioningRunRepository repository,
        IArtifactManifestVerifier manifestVerifier,
        IBffArtifactDownloader artifactDownloader,
        IKuduZipDeployer kuduDeployer,
        IAppServiceSlotSwapper slotSwapper,
        IHealthProbe healthProbe,
        IBffPublishSizeReporter publishSizeReporter,
        IOptions<BffDeployOptions> options,
        ILogger<H9BffDeployHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(manifestVerifier);
        ArgumentNullException.ThrowIfNull(artifactDownloader);
        ArgumentNullException.ThrowIfNull(kuduDeployer);
        ArgumentNullException.ThrowIfNull(slotSwapper);
        ArgumentNullException.ThrowIfNull(healthProbe);
        ArgumentNullException.ThrowIfNull(publishSizeReporter);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _manifestVerifier = manifestVerifier;
        _artifactDownloader = artifactDownloader;
        _kuduDeployer = kuduDeployer;
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
        //     buildId is now OPTIONAL (task 132, DS-4 §5 item 1) — resolved
        //     from the manifest below if the run parameter is absent.
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
                "Run parameter 'resourceGroupName' is required by H9 (ARM targeting for deploy + swap ops).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, AppServiceNameParameterKey, out var appServiceName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.MissingAppServiceName,
                "Run parameter 'appServiceName' is required by H9 (BFF App Service name — deploy + swap targeting).",
                cancellationToken).ConfigureAwait(false);
        }

        var hasRequestedBuildId = TryGetNonEmpty(parameters, BuildIdParameterKey, out var requestedBuildIdRaw);
        var requestedBuildId = hasRequestedBuildId ? requestedBuildIdRaw : null;

        var stagingSlotName = TryGetNonEmpty(parameters, StagingSlotNameParameterKey, out var slotRaw)
            ? slotRaw : _options.DefaultStagingSlotName;
        var healthCheckPath = TryGetNonEmpty(parameters, HealthCheckPathParameterKey, out var pathRaw)
            ? pathRaw : _options.DefaultHealthCheckPath;

        // (3) EARLY level-3 idempotency check — fast path when buildId was
        //     explicitly supplied (e.g. from sprk_bffversion, DS-4 §5 item 4):
        //     no network call needed to know the idempotency key. When
        //     buildId is absent, this check is skipped here and re-attempted
        //     after manifest resolution below (the only path that needs a
        //     network round-trip before dedup can be determined).
        if (hasRequestedBuildId)
        {
            var earlyKey = BuildIdempotencyKey(envelope.CustomerId, requestedBuildId!);
            if (IsAlreadyCompleted(run, earlyKey))
            {
                _logger.LogInformation(
                    "H9 idempotent no-op (early, buildId supplied): runId={RunId} idempotencyKey={IdempotencyKey}",
                    envelope.RunId, earlyKey);
                return new HandlerResult.Success(earlyKey);
            }
        }

        // (4) Gap 2 assertion — spaarkedev1-hardcode pre-flight scan on the
        //     shipped Deploy-Release.ps1 script. Task 013 hardened Phase 4
        //     to be customerId-driven; a regression MUST NOT silently
        //     proceed to production. POML acceptance criterion 5. Unrelated
        //     to task 132's re-scope (Deploy-Release.ps1 is a different,
        //     broader release-orchestration script than the retired
        //     Deploy-BffApi.ps1-shelling collaborators) — left unchanged.
        var hardcodeScanResult = ScanForSpaarkedev1Hardcode();
        if (hardcodeScanResult is not null)
        {
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                BffDeployRejectionCodes.Spaarkedev1HardcodeDetected,
                hardcodeScanResult,
                cancellationToken).ConfigureAwait(false);
        }

        // (5) Artifact manifest resolve + verify (task 132, DS-4 §5 items 1+2
        //     — replaces the old r3-era shell-out gate verification). Hard
        //     block if the manifest is missing, a required gate key is
        //     absent, any gate is RED, or a requested buildId does not match
        //     the manifest's own buildId.
        ArtifactManifestVerificationResult manifestResult;
        try
        {
            manifestResult = await _manifestVerifier.VerifyAsync(
                new ArtifactManifestVerificationRequest(requestedBuildId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H9 artifact-manifest verifier infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.ManifestVerifierInfraFault,
                $"Artifact-manifest verifier infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "No external side effect — verify the provisioning-artifacts blob container is reachable " +
                "and BffDeployOptions:ProvisioningArtifactsContainerUri is correctly configured.",
                cancellationToken).ConfigureAwait(false);
        }

        if (manifestResult is ArtifactManifestVerificationResult.Rejected rejected)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.ArtifactManifestRejected,
                rejected.Diagnostic,
                cancellationToken).ConfigureAwait(false);
        }

        var manifest = ((ArtifactManifestVerificationResult.Verified)manifestResult).Manifest;
        _logger.LogInformation(
            "H9 artifact manifest verified: runId={RunId} buildId={BuildId} artifactBlobName={ArtifactBlobName}",
            envelope.RunId, manifest.BuildId, manifest.ArtifactBlobName);

        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, manifest.BuildId);

        // (6) LATE level-3 idempotency check — covers the resolve-from-
        //     manifest path (buildId was absent from run parameters). Cheap
        //     no-op re-check when buildId WAS supplied (already matched the
        //     manifest per the Rejected branch above, so this key equals the
        //     early key).
        if (IsAlreadyCompleted(run, idempotencyKey))
        {
            _logger.LogInformation(
                "H9 idempotent no-op (post-manifest-resolve): runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (7) Download the artifact blob (Azure.Storage.Blobs, UAMI RBAC —
        //     no stored storage account key).
        ArtifactDownloadResult downloadResult;
        try
        {
            downloadResult = await _artifactDownloader.DownloadAsync(
                new ArtifactDownloadRequest(manifest.ArtifactBlobName), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H9 artifact-download infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.ArtifactDownloadInfraFault,
                $"Artifact-download infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "No slot-deploy attempted.",
                cancellationToken).ConfigureAwait(false);
        }

        if (downloadResult is ArtifactDownloadResult.Failure downloadFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.ArtifactDownloadFailed,
                downloadFailure.Diagnostic,
                cancellationToken).ConfigureAwait(false);
        }

        var localZipPath = ((ArtifactDownloadResult.Success)downloadResult).LocalZipPath;

        // (8) Kudu zip-deploy the downloaded artifact to the STAGING slot.
        KuduZipDeployResult kuduResult;
        try
        {
            kuduResult = await _kuduDeployer.DeployAsync(
                new KuduZipDeployRequest(appServiceName, stagingSlotName, localZipPath),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H9 Kudu zip-deploy infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.KuduZipDeployInfraFault,
                $"Kudu zip-deploy infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "No slot-swap attempted.",
                cancellationToken).ConfigureAwait(false);
        }

        if (kuduResult is KuduZipDeployResult.Failure kuduFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.KuduZipDeployFailed,
                kuduFailure.Diagnostic,
                cancellationToken).ConfigureAwait(false);
        }

        // (9) Health-probe STAGING post-deploy (EXISTING HttpHealthProbe,
        //     reused unchanged — no new health-check code, per DS-4 §5
        //     item 3 / POML step 4).
        var stagingUrl = $"https://{appServiceName}-{stagingSlotName}.azurewebsites.net";
        var productionUrl = $"https://{appServiceName}.azurewebsites.net";
        var stagingHealthUrl = CombineUrl(stagingUrl, healthCheckPath);

        HealthProbeResult stagingProbeResult;
        try
        {
            stagingProbeResult = await _healthProbe.ProbeAsync(
                new HealthProbeRequest(
                    TargetUrl: stagingHealthUrl,
                    MaxRetries: _options.HealthProbeMaxRetries,
                    Interval: _options.HealthProbeInterval,
                    RequestTimeout: _options.HealthProbeRequestTimeout),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H9 staging health-probe infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            stagingProbeResult = new HealthProbeResult.Failure(
                AttemptsUsed: 0,
                Diagnostic: $"Staging health probe infra fault: {ex.GetType().Name}: {ex.Message}");
        }

        if (stagingProbeResult is HealthProbeResult.Failure stagingProbeFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BffDeployRejectionCodes.StagingHealthCheckFailed,
                $"Staging /health check failed after deploy — swap BLOCKED (production untouched). " +
                $"Diagnostic: {stagingProbeFailure.Diagnostic}. Staging is dedicated + re-deployable — safe to retry.",
                cancellationToken).ConfigureAwait(false);
        }

        // (10) NFR-01 publish-size measurement + threshold check. RUNS
        //      BEFORE the slot-swap so a bloated build cannot reach
        //      production. Measures the DOWNLOADED artifact zip (task 132 —
        //      the blob download itself is the publish artifact this metric
        //      tracks; no separate local build output to measure anymore).
        PublishSizeReport sizeReport;
        try
        {
            sizeReport = await _publishSizeReporter.MeasureAsync(
                new PublishSizeReportRequest(
                    CustomerId: envelope.CustomerId,
                    PublishZipPath: localZipPath,
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

        // (11) Slot swap staging → production. First swap of the two-swap
        //      blue-green protocol. UNCHANGED from the pre-task-132 handler
        //      (only the underlying IAppServiceSlotSwapper impl changed —
        //      DI-swapped from AzCliAppServiceSlotSwapper to ArmSlotSwapper).
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

        // (12) Post-swap production /health smoke test (NFR-05). BFF's
        //      ValidateOnStart (r3 task 061) throws at boot on missing
        //      Tier-1 KV refs, so a 200 IS the KV-ref-resolution proof.
        //      UNCHANGED from the pre-task-132 handler.
        HealthProbeResult probeResult;
        var productionHealthUrl = CombineUrl(productionUrl, healthCheckPath);
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
            // (13) Rollback: re-swap production ← prior version (staging holds
            //      the previous production version after the first swap).
            //      UNCHANGED from the pre-task-132 handler — this is the
            //      "PRESERVE the existing rollback-re-swap logic unchanged"
            //      constraint's exact code, now wired to the new SDK-based
            //      swap call (ArmSlotSwapper) via DI only.
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

        // (14) All post-conditions cleared — advance Cosmos state.
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

    private static bool IsAlreadyCompleted(ProvisioningRun run, string idempotencyKey)
        => run.CompletedPhases.Any(cp =>
            string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
            && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

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
    /// UNCHANGED by task 132 — orthogonal to the artifact-based re-scope.
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
