// -----------------------------------------------------------------------------
// H2bAiSearchIndexHandler.cs
//
// L2 CONTROL-PLANE H2b AI Search index-provisioning handler (task 045, wave C4).
//
// PURPOSE:
//   Provisions (Model 2) or verifies + templates (Model 1) the 7 canonical
//   Spaarke AI Search indexes per FR-05 / §11.2 catalog. Model 2 uses
//   SearchIndexClientProvisioner (Azure.Search.Documents.Indexes.SearchIndexClient
//   under UAMI RBAC, task 124 — REPLACES the retired script-shelling
//   DeployAllIndexesScriptProvisioner; the embedded JSON schemas remain the
//   FR-07 catalog authority per task 002 audit § 1). Model 1 verifies
//   presence on the shared platform AI Search + provisions a REAL per-tenant
//   tenantId-filter query template via AiSearchTenantFilterTemplateProvisioner
//   (§4D I2 / FR-29 enforcement at onboarding — task 124 replaced the
//   wave-C4 logging-only Stub with a Cosmos-backed real provisioner).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-05 (H2b):
//       7 canonical indexes; per-index invariant verifier passes; Model 1
//       verifies presence + provisions per-tenant filter template.
//   - projects/customer-provisioning-orchestration-r1/spec.md § MUST rules
//       (§4D I1 / FR-28): -TenantId mandatory; no hardcoded default.
//   - projects/customer-provisioning-orchestration-r1/spec.md § MUST rules
//       (§4D I2 / FR-29): unconditional `tenantId eq` filter on every AI
//       Search query — Model 1 template provisioning is the onboarding-time
//       enforcement mechanism.
//   - projects/customer-provisioning-orchestration-r1/spec.md SC #10:
//       spaarke-playbook-embeddings (ADR-039) + spaarke-knowledge-index*
//       (audit § 2) MUST NOT be re-provisioned.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1a
//       Model 1 vs Model 2 handler-behavior differences (H2b row).
//   - projects/customer-provisioning-orchestration-r1/design.md §4C
//       rollback taxonomy — failure-mode classification.
//   - projects/customer-provisioning-orchestration-r1/notes/
//       ai-search-catalog-audit-2026-08.md — canonical 7 + retired lineage.
//   - ADR-013: MUST use PublicContracts/ facade if AI needed; H2b consumes
//       NO AI-internal types (only AI Search REST/SDK collaborators).
//   - ADR-028: DefaultAzureCredential + UAMI outbound — verifier,
//       SearchIndexClientProvisioner, and the tenant-filter template
//       provisioner's Cosmos store ALL use the shared UAMI-pinned credential;
//       zero admin-key / zero operator `az` chain anywhere in this handler's
//       collaborator graph (task 124, Wave G-2).
//   - ADR-032: SignalR feature-gate does NOT apply to H2b DI — unconditional
//       registration parity with H2a.
//   - ADR-036: fire-and-forget via Service Bus; reconciler owns DAG
//       advancement to H3 (sibling task 046) — H2b does NOT enqueue H3
//       directly. Both handlers are Wave 3C parallel siblings.
//   - ADR-039: `spaarke-playbook-embeddings` retired (FR-P2-06); H2b's
//       catalog guard rejects it structurally.
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌─────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                        │ §4C class                 │
//   ├─────────────────────────────────────┼───────────────────────────┤
//   │ Missing tenantId (§4D I1)           │ Resumable                 │
//   │ Missing indexVer                    │ Resumable                 │
//   │ Run not found in Cosmos partition   │ Resumable                 │
//   │ Missing search endpoint             │ Resumable (Model 2 —      │
//   │ (Model 2 InterStepState blank)      │ H2a must have populated)  │
//   │ Missing shared platform endpoint    │ Resumable (Model 1 —      │
//   │ (Model 1 config blank)              │ config error, operator    │
//   │                                     │ fixes app-setting +       │
//   │                                     │ resumes)                  │
//   │ Retired index in requested catalog  │ QuarantineRequired        │
//   │ (spec SC #10 / ADR-039)             │ (structural design-intent │
//   │                                     │ violation — never proceed)│
//   │ Model 2 provisioner failed          │ QuarantineRequired        │
//   │ (partial deploy possible)           │                           │
//   │ Verifier InvariantViolation         │ QuarantineRequired        │
//   │ (index exists but schema wrong —    │                           │
//   │ won't self-heal on retry)           │                           │
//   │ Model 1 verifier: index Missing     │ QuarantineRequired        │
//   │ (shared platform under-provisioned) │ (operator ops action)     │
//   │ Model 1 template provisioner failed │ Resumable                 │
//   │ (PUT is idempotent — retry-safe)    │                           │
//   │ Concurrent Cosmos writer conflict   │ Resumable                 │
//   │ Run row deleted mid-flight          │ Resumable                 │
//   └─────────────────────────────────────┴───────────────────────────┘
//
// IDEMPOTENCY (3-level per ADR-004 / design.md §4.1):
//   Level 1 (Service Bus MessageId dedup): the wave-C5 reconciler computes
//           deterministic MessageId per (HandlerId, RunId, CustomerId,
//           paramHash); SB duplicate-detection collapses re-enqueues.
//   Level 2 (Redis IdempotencyService): NOT YET IMPLEMENTED in L2 (design.md
//           §4.1 preamble; parity with H0 / H0.5 / H1 / H2a).
//   Level 3 (handler body durable dedup): this handler scans
//           ProvisioningRun.CompletedPhases for (Phase=="H2b",
//           IdempotencyKey==aisearch-{customerId}-{indexVer}). Match ⇒ Success
//           no-op. indexVer is the manifest hash of the schema JSONs in
//           scripts/ai-search/ (POML constraint) — NOT an attempt counter.
//
// DOWNSTREAM ENQUEUE (Wave C4 note):
//   H2b does NOT enqueue H3 (sibling task 046) directly. The downstream DAG
//   per design.md §4.1 fans H3 out from H4 (KV secrets), not from H2b.
//   H2b + H3 are Wave 3C parallel siblings; the wave-C5 reconciler owns
//   fan-out based on Cosmos state. H2b's job is to mutate the run row
//   (CurrentPhase + CompletedPhases + no InterStepState writes — H2a already
//   populated AiSearchEndpoint) and return Success.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H2bAiSearchIndexHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md § 4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H2b;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>
    /// Non-secret parameter key carrying the AI Search index-schema version.
    /// Feeds the idempotency key <c>aisearch-{customerId}-{indexVer}</c> per
    /// POML constraint (manifest hash of schema JSONs in
    /// <c>scripts/ai-search/</c>).
    /// </summary>
    public const string IndexVersionParameterKey = "indexVer";

    /// <summary>
    /// Non-secret parameter key carrying the target environment (dev / staging /
    /// prod / demo) — passed through to <c>Deploy-AllIndexes.ps1 -Environment</c>
    /// for Model 2. Defaults to <c>prod</c> when absent.
    /// </summary>
    public const string EnvironmentNameParameterKey = "environmentName";

    /// <summary>
    /// Optional non-secret parameter key carrying a comma-separated subset of
    /// canonical index short-keys to provision (parity with the script's
    /// <c>-Indexes</c> parameter). Empty / absent ⇒ provision all canonical 7.
    /// The retired-name guard runs BEFORE the provisioner regardless of subset.
    /// </summary>
    public const string RequestedIndexesParameterKey = "requestedIndexes";

    /// <summary>Default target environment when the parameter is absent.</summary>
    private const string DefaultEnvironmentName = "prod";

    private readonly IProvisioningRunRepository _repository;
    private readonly ICanonicalIndexCatalog _catalog;
    private readonly IAiSearchIndexProvisioner _provisioner;
    private readonly IAiSearchIndexVerifier _verifier;
    private readonly IAiSearchTenantFilterTemplateProvisioner _templateProvisioner;
    private readonly AiSearchIndexOptions _options;
    private readonly ILogger<H2bAiSearchIndexHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H2b AI Search index-provisioning handler. All
    /// collaborators are interface-abstracted so unit tests can substitute
    /// stubs for each seam.
    /// </summary>
    public H2bAiSearchIndexHandler(
        IProvisioningRunRepository repository,
        ICanonicalIndexCatalog catalog,
        IAiSearchIndexProvisioner provisioner,
        IAiSearchIndexVerifier verifier,
        IAiSearchTenantFilterTemplateProvisioner templateProvisioner,
        IOptions<AiSearchIndexOptions> options,
        ILogger<H2bAiSearchIndexHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(provisioner);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(templateProvisioner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _catalog = catalog;
        _provisioner = provisioner;
        _verifier = verifier;
        _templateProvisioner = templateProvisioner;
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
            // silently mis-executing (parity with H2a / H0PreflightHandler).
            throw new InvalidOperationException(
                $"H2bAiSearchIndexHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H2b AI Search provisioning starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H2b aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: AiSearchIndexRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;

        // (2) §4D I1 tenant guard — H2b MUST NOT fall back to a default tenant.
        //     Model 1's tenant-filter template embeds this value verbatim; a
        //     default fallback would put the template on the wrong tenant.
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            var diagnostic =
                "Run parameter 'tenantId' is required by H2b (§4D I1 no-hardcoded-tenant). " +
                "Upstream handler (H0.5 for Model 2, L2 endpoint for Model 1) MUST populate this before H2b dispatches.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                AiSearchIndexRejectionCodes.MissingTenantId, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (3) Index-version guard — feeds the idempotency key. Absence means
        //     level-3 dedup would collide across upgrades → refuse.
        if (!TryGetNonEmpty(parameters, IndexVersionParameterKey, out var indexVer))
        {
            var diagnostic =
                "Run parameter 'indexVer' is required by H2b (idempotency key: aisearch-{customerId}-{indexVer}). " +
                "indexVer MUST be the manifest hash of the schema JSONs in scripts/ai-search/ per POML constraint " +
                "/ design.md §4.1 preamble.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                AiSearchIndexRejectionCodes.MissingIndexVersion, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, indexVer);

        // (4) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H2b idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (5) Resolve requested index catalog. Empty request ⇒ canonical 7.
        //     The retired-name guard fires BEFORE the provisioner OR verifier
        //     runs regardless of Model 1 / Model 2 branch — structural
        //     design-intent violation must never proceed to a live API call.
        var requestedIndexes = ResolveRequestedIndexes(parameters);
        var retiredHit = requestedIndexes.FirstOrDefault(_catalog.IsRetired);
        if (!string.IsNullOrEmpty(retiredHit))
        {
            var diagnostic =
                $"Requested index catalog contains retired name '{retiredHit}'. " +
                "Retired / archived indexes MUST NOT be re-provisioned " +
                "(spaarke-playbook-embeddings per ADR-039 / FR-P2-06; " +
                "spaarke-knowledge-index lineage per spec SC #10). " +
                "See projects/customer-provisioning-orchestration-r1/notes/ai-search-catalog-audit-2026-08.md.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                AiSearchIndexRejectionCodes.RetiredIndexProvisioningForbidden, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (6) Branch on tenancy model — Model 2 provisions dedicated indexes;
        //     Model 1 verifies shared platform + provisions per-tenant template.
        var tenancyModel = string.IsNullOrWhiteSpace(run.TenancyModel) ? "Model2Dedicated" : run.TenancyModel;
        var environmentName = TryGetNonEmpty(parameters, EnvironmentNameParameterKey, out var env)
            ? env
            : DefaultEnvironmentName;

        HandlerResult branchResult;
        if (string.Equals(tenancyModel, "Model1Shared", StringComparison.OrdinalIgnoreCase))
        {
            branchResult = await HandleModel1BranchAsync(
                run, etag, envelope, tenantId, requestedIndexes, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            branchResult = await HandleModel2BranchAsync(
                run, etag, envelope, tenantId, environmentName, requestedIndexes, indexVer, cancellationToken)
                .ConfigureAwait(false);
        }

        if (branchResult is HandlerResult.Failure)
        {
            return branchResult;
        }

        // (7) All post-conditions cleared — advance Cosmos state. H2b writes
        //     no interStepState (H2a populated AiSearchEndpoint; H2b's
        //     side effects are on the AI Search service itself, not on the
        //     Cosmos run doc). The wave-C5 reconciler observes CurrentPhase
        //     + CompletedPhases + fans H3 out from H4 per design.md §4.1.
        stopwatch.Stop();
        _logger.LogInformation(
            "H2b AI Search provisioning succeeded: runId={RunId} customerId={CustomerId} durationMs={DurationMs} " +
            "tenancyModel={TenancyModel} indexCount={IndexCount}",
            envelope.RunId, envelope.CustomerId, stopwatch.ElapsedMilliseconds,
            tenancyModel, requestedIndexes.Length);

        return await MarkCompleteAsync(run, etag, idempotencyKey, envelope, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H2b idempotency key:
    /// <c>aisearch-{customerId}-{indexVer}</c>. Exposed internal so unit
    /// tests can construct expected keys without duplicating the format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, string indexVer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexVer);
        return $"aisearch-{customerId}-{indexVer}";
    }

    private async Task<HandlerResult> HandleModel2BranchAsync(
        ProvisioningRun run,
        string etag,
        HandlerEnvelope envelope,
        string tenantId,
        string environmentName,
        ImmutableArray<string> requestedIndexes,
        string indexVer,
        CancellationToken cancellationToken)
    {
        // Model 2 endpoint comes from H2a's InterStepState — if blank, H2a
        // hasn't run (or the reconciler dispatched H2b out of order).
        var endpoint = run.InterStepState.AiSearchEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            var diagnostic =
                "Model 2 branch requires ProvisioningRun.InterStepState.AiSearchEndpoint (populated by H2a). " +
                "H2b was dispatched before H2a completed OR H2a's output was not persisted — " +
                "reconciler MUST advance DAG in order.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                AiSearchIndexRejectionCodes.MissingSearchEndpoint, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (M2.a) Invoke the Model 2 provisioner (wraps Deploy-AllIndexes.ps1).
        AiSearchIndexProvisionOutcome outcome;
        try
        {
            var request = new AiSearchIndexProvisionRequest(
                CustomerId: envelope.CustomerId,
                TenantId: tenantId,
                EnvironmentName: environmentName,
                SearchEndpoint: endpoint,
                RequestedIndexNames: requestedIndexes,
                IndexVersion: indexVer);
            outcome = await _provisioner.ProvisionAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H2b Model 2 provisioner infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"Model 2 provisioner infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Partial index state may exist — treated as Quarantine-required per §4C.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                AiSearchIndexRejectionCodes.IndexProvisioningFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        if (outcome is AiSearchIndexProvisionOutcome.Failure provFailure)
        {
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                AiSearchIndexRejectionCodes.IndexProvisioningFailed, provFailure.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (M2.b) Independent post-deploy verifier — belt-and-suspenders vs
        //         script's Invoke-PostDeployVerifier. See
        //         RestApiAiSearchIndexVerifier file header.
        return await RunVerifierAsync(run, etag, endpoint, envelope, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HandlerResult> HandleModel1BranchAsync(
        ProvisioningRun run,
        string etag,
        HandlerEnvelope envelope,
        string tenantId,
        ImmutableArray<string> requestedIndexes,
        CancellationToken cancellationToken)
    {
        // Model 1 endpoint comes from L2 config (shared platform), NOT from
        // H2a's InterStepState (Model 1 does not deploy dedicated AI Search).
        var endpoint = _options.SharedPlatformSearchEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            var diagnostic =
                "Model 1 branch requires AiSearchIndex:SharedPlatformSearchEndpoint config. " +
                "Operator must set the shared platform AI Search endpoint app-setting on L2 App Service " +
                "(e.g. https://spaarke-search-prod.search.windows.net) before onboarding Model 1 tenants.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                AiSearchIndexRejectionCodes.MissingSearchEndpoint, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (M1.a) Verify the 7 canonical indexes ALREADY exist on the shared
        //         service. H2b MUST NOT re-create them (spec MUST rule +
        //         design.md §4.1a — Model 1 shares platform indexes).
        var verifyResult = await RunVerifierRawAsync(endpoint, requestedIndexes, cancellationToken)
            .ConfigureAwait(false);

        if (verifyResult is AiSearchIndexVerifyResult.Missing missing)
        {
            var diagnostic =
                $"Model 1 shared platform AI Search '{endpoint}' is missing indexes: " +
                $"[{string.Join(", ", missing.MissingIndexNames)}]. " +
                "H2b MUST NOT re-create shared indexes — operator must run " +
                "scripts/ai-search/Deploy-AllIndexes.ps1 -Environment {env} against the shared service " +
                "BEFORE onboarding this Model 1 tenant.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                AiSearchIndexRejectionCodes.SharedIndexMissing, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        if (verifyResult is AiSearchIndexVerifyResult.InvariantViolation violation)
        {
            var diagnostic = FormatInvariantDiagnostic(endpoint, violation);
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                AiSearchIndexRejectionCodes.IndexInvariantViolation, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (M1.b) Provision the per-tenant filter template — §4D I2 / FR-29
        //         enforcement at onboarding time.
        AiSearchTenantFilterTemplateOutcome templateOutcome;
        try
        {
            var templateRequest = new AiSearchTenantFilterTemplateRequest(
                SearchEndpoint: endpoint,
                TenantId: tenantId,
                CustomerId: envelope.CustomerId,
                IndexNames: requestedIndexes);
            templateOutcome = await _templateProvisioner.ProvisionAsync(templateRequest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H2b Model 1 template provisioner infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"Model 1 tenant-filter template provisioner infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "PUT semantics make retry safe — treated as Resumable per §4C.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                AiSearchIndexRejectionCodes.TenantFilterTemplateProvisionFailed, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        if (templateOutcome is AiSearchTenantFilterTemplateOutcome.Failure templateFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                AiSearchIndexRejectionCodes.TenantFilterTemplateProvisionFailed,
                templateFailure.Diagnostic, cancellationToken).ConfigureAwait(false);
        }

        return new HandlerResult.Success(BuildIdempotencyKey(envelope.CustomerId,
            run.Parameters.NonSecret[IndexVersionParameterKey]));
    }

    private async Task<HandlerResult> RunVerifierAsync(
        ProvisioningRun run,
        string etag,
        string endpoint,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        // Always verify against the FULL canonical 7 (even if the run
        // requested a subset via requestedIndexes). Rationale: the subset
        // param is a script-level optimization; the invariant guarantee is
        // "the canonical 7 are all present + correct" from the platform's
        // perspective. Sub-subset verification adds no protection.
        var verifyResult = await RunVerifierRawAsync(endpoint, _catalog.CanonicalIndexNames, cancellationToken)
            .ConfigureAwait(false);

        if (verifyResult is AiSearchIndexVerifyResult.Missing missing)
        {
            // Model 2 provisioner returned success but verifier says an
            // expected index is absent — SDK PUT drift.
            var diagnostic =
                $"Model 2 provisioner returned Success but verifier found missing indexes at '{endpoint}': " +
                $"[{string.Join(", ", missing.MissingIndexNames)}]. " +
                "SearchIndexClientProvisioner reported all PUTs succeeded but drift is present — investigate provisioner vs deployed state.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                AiSearchIndexRejectionCodes.IndexProvisioningFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        if (verifyResult is AiSearchIndexVerifyResult.InvariantViolation violation)
        {
            var diagnostic = FormatInvariantDiagnostic(endpoint, violation);
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                AiSearchIndexRejectionCodes.IndexInvariantViolation, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        return new HandlerResult.Success(BuildIdempotencyKey(envelope.CustomerId,
            run.Parameters.NonSecret[IndexVersionParameterKey]));
    }

    private async Task<AiSearchIndexVerifyResult> RunVerifierRawAsync(
        string endpoint,
        ImmutableArray<string> indexNames,
        CancellationToken cancellationToken)
    {
        var request = new AiSearchIndexVerifyRequest(
            SearchEndpoint: endpoint,
            ExpectedIndexNames: indexNames);
        return await _verifier.VerifyAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private ImmutableArray<string> ResolveRequestedIndexes(IDictionary<string, string> parameters)
    {
        if (TryGetNonEmpty(parameters, RequestedIndexesParameterKey, out var raw))
        {
            var names = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToImmutableArray();
            return names.IsDefaultOrEmpty ? _catalog.CanonicalIndexNames : names;
        }
        return _catalog.CanonicalIndexNames;
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

    private static string FormatInvariantDiagnostic(
        string endpoint,
        AiSearchIndexVerifyResult.InvariantViolation violation)
    {
        var lines = violation.Issues
            .Select(i => $"  - {i.IndexName}.{i.FieldName}: {i.Reason}");
        return
            $"Per-index invariant verifier reported {violation.Issues.Length} violation(s) against '{endpoint}':\n" +
            string.Join("\n", lines) + "\n" +
            "See scripts/ai-search/Deploy-AllIndexes.ps1 Invoke-PostDeployVerifier for the full invariant catalog.";
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

        run.GateStates[$"h2b-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H2b failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H2b failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier; // Reconciler observes + fans out (H2b + H3 are wave-C parallel siblings).
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
                "H2b success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: AiSearchIndexRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H2b read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H2b.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H2b success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: AiSearchIndexRejectionCodes.RunDeletedDuringProvision,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H2b was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
