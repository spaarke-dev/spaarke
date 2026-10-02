// -----------------------------------------------------------------------------
// H2bAiSearchIndexHandler.cs
//
// L2 CONTROL-PLANE H2b AI Search index-provisioning handler (task 045, wave C4).
//
// PURPOSE:
//   Provisions the 7 canonical Spaarke AI Search indexes per FR-05 / §11.2
//   catalog on the stamp's OWN AI Search service (the endpoint H2a deployed),
//   via SearchIndexClientProvisioner (Azure.Search.Documents.Indexes.SearchIndexClient
//   under UAMI RBAC, task 124 — the embedded JSON schemas remain the FR-07
//   catalog authority per task 002 audit § 1), then verifies them.
//
//   Model 1 and Model 2 take the same path (task 225b, D-12): every customer is
//   a dedicated stamp with its own AI Search service. The retired Model 1 branch
//   verified indexes on a SHARED platform service and wrote a per-tenant
//   tenantId-filter template to Cosmos; with one service per customer there is
//   no shared index to filter, so both are gone.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-05 (H2b):
//       7 canonical indexes; per-index invariant verifier passes.
//   - projects/customer-provisioning-orchestration-r1/spec.md § MUST rules
//       (§4D I1 / FR-28): -TenantId mandatory; no hardcoded default.
//   - projects/customer-provisioning-orchestration-r1/spec.md § MUST rules
//       (§4D I2 / FR-29): unconditional `tenantId eq` filter on every AI
//       Search query — enforced by the BFF at query time; H13's I2 probe
//       checks the stamp's indexes.
//   - projects/customer-provisioning-orchestration-r1/spec.md SC #10:
//       spaarke-playbook-embeddings (ADR-039) + spaarke-knowledge-index*
//       (audit § 2) MUST NOT be re-provisioned.
//   - projects/customer-provisioning-orchestration-r1/notes/
//       model1-dedicated-remediation-plan.md — D-12, T225b (one path).
//   - projects/customer-provisioning-orchestration-r1/design.md §4C
//       rollback taxonomy — failure-mode classification.
//   - projects/customer-provisioning-orchestration-r1/notes/
//       ai-search-catalog-audit-2026-08.md — canonical 7 + retired lineage.
//   - ADR-013: MUST use PublicContracts/ facade if AI needed; H2b consumes
//       NO AI-internal types (only AI Search REST/SDK collaborators).
//   - ADR-028: DefaultAzureCredential + UAMI outbound — verifier and
//       SearchIndexClientProvisioner use the shared UAMI-pinned credential;
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
//   │ Requested index has no schema (245b)│ Resumable                 │
//   │ Run not found in Cosmos partition   │ Resumable                 │
//   │ Missing search endpoint             │ Resumable (H2a must have  │
//   │ (InterStepState blank)              │ populated it)             │
//   │ Retired index in requested catalog  │ QuarantineRequired        │
//   │ (spec SC #10 / ADR-039)             │ (structural design-intent │
//   │                                     │ violation — never proceed)│
//   │ Provisioner failed                  │ QuarantineRequired        │
//   │ (partial deploy possible)           │                           │
//   │ Verifier InvariantViolation         │ QuarantineRequired        │
//   │ (index exists but schema wrong —    │                           │
//   │ won't self-heal on retry)           │                           │
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
//           no-op. indexVer is the content version of the embedded schema
//           bodies H2b applies (IndexSchemaSet — task 245b) — NOT an attempt
//           counter.
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
using Sprk.Provisioning.ControlPlane.Core.Models;
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
    /// Optional non-secret parameter key carrying a comma-separated subset of
    /// canonical index short-keys to provision (parity with the script's
    /// <c>-Indexes</c> parameter). Empty / absent ⇒ provision all canonical 7.
    /// The retired-name guard runs BEFORE the provisioner regardless of subset.
    /// </summary>
    public const string RequestedIndexesParameterKey = "requestedIndexes";

    private readonly IProvisioningRunRepository _repository;
    private readonly ICanonicalIndexCatalog _catalog;
    private readonly IAiSearchIndexProvisioner _provisioner;
    private readonly IAiSearchIndexVerifier _verifier;
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
        ILogger<H2bAiSearchIndexHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(provisioner);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _catalog = catalog;
        _provisioner = provisioner;
        _verifier = verifier;
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
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            var diagnostic =
                "Run parameter 'tenantId' is required by H2b (§4D I1 no-hardcoded-tenant). " +
                "The L2 intake (POST /api/runs) requires it; a run without it cannot reach H2b legitimately.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                AiSearchIndexRejectionCodes.MissingTenantId, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (3) Resolve requested index catalog. Empty request ⇒ canonical 7.
        //     The retired-name guard fires BEFORE the provisioner OR verifier
        //     runs — structural design-intent violation must never proceed to a
        //     live API call.
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

        // (4) Task 245b: the idempotency version is the content version of the schema bodies H2b
        //     applies (IndexSchemaSet) — formerly a run parameter nothing wrote. Same schemas ⇒ same
        //     key; an edited schema ⇒ a new key and a re-apply.
        string indexVer;
        string? unknownIndex;
        try
        {
            IndexSchemaSet.TryComputeVersion(requestedIndexes, out indexVer, out unknownIndex);
        }
        catch (InvalidOperationException ex)
        {
            // An embedded schema resource is missing from the build — a packaging fault, nothing applied.
            return await FailAsync(run, etag, FailureClass.Resumable,
                AiSearchIndexRejectionCodes.IndexSchemaUnavailable, ex.Message, cancellationToken).ConfigureAwait(false);
        }
        if (unknownIndex is not null)
        {
            var diagnostic =
                $"Requested index '{unknownIndex}' has no embedded schema (Handlers/AiSearchIndex/IndexSchemas/). " +
                $"Valid names: {string.Join(", ", _catalog.CanonicalIndexNames)}. Fix the run's requestedIndexes " +
                "intake value; nothing has been applied.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                AiSearchIndexRejectionCodes.IndexSchemaUnavailable, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, indexVer);

        // (5) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H2b idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (6) Task 223 (D-12): an unparseable tenancy model is a corrupted run — reject it rather
        //     than default. Both models then take the same path (task 225b): every customer has
        //     its own AI Search service, deployed by H2a.
        if (!TenancyModelParser.TryParse(run.TenancyModel, out var tenancyModel))
        {
            var diagnostic =
                $"ProvisioningRun.tenancyModel '{run.TenancyModel ?? "(null)"}' is not a recognized TenancyModel. " +
                $"Expected: {TenancyModelParser.FormatExpectedValues()}. Pre-D-12 handler defaulted blank to " +
                "Model2Dedicated; Task 223 retires that silent default (per INCOMING-D12-D13-REMEDIATION.md §5 Item 2 / D2).";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                AiSearchIndexRejectionCodes.InvalidTenancyModel, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        var environmentName = IntakeParameterCatalog.ResolveEnvironmentName(parameters);

        var provisionResult = await ProvisionIndexesAsync(
            run, etag, envelope, tenantId, environmentName, requestedIndexes, indexVer, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (provisionResult is HandlerResult.Failure)
        {
            return provisionResult;
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

    private async Task<HandlerResult> ProvisionIndexesAsync(
        ProvisioningRun run,
        string etag,
        HandlerEnvelope envelope,
        string tenantId,
        string environmentName,
        ImmutableArray<string> requestedIndexes,
        string indexVer,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // The endpoint comes from H2a's InterStepState — if blank, H2a
        // hasn't run (or the reconciler dispatched H2b out of order).
        var endpoint = run.InterStepState.AiSearchEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            var diagnostic =
                "H2b requires ProvisioningRun.InterStepState.AiSearchEndpoint (populated by H2a). " +
                "H2b was dispatched before H2a completed OR H2a's output was not persisted — " +
                "reconciler MUST advance DAG in order.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                AiSearchIndexRejectionCodes.MissingSearchEndpoint, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (a) Invoke the provisioner (SearchIndexClientProvisioner).
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
                "H2b provisioner infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"Index provisioner infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
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

        // (b) Independent post-deploy verifier. See RestApiAiSearchIndexVerifier
        //     file header.
        return await RunVerifierAsync(run, etag, endpoint, envelope, idempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HandlerResult> RunVerifierAsync(
        ProvisioningRun run,
        string etag,
        string endpoint,
        HandlerEnvelope envelope,
        string idempotencyKey,
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
            // The provisioner returned success but the verifier says an
            // expected index is absent — SDK PUT drift.
            var diagnostic =
                $"Index provisioner returned Success but verifier found missing indexes at '{endpoint}': " +
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

        return new HandlerResult.Success(idempotencyKey);
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
