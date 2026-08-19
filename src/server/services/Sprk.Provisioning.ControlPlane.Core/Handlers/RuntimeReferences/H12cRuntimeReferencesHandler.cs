// -----------------------------------------------------------------------------
// H12cRuntimeReferencesHandler.cs
//
// L2 CONTROL-PLANE H12c runtime references handler (task 072, wave Cp — DAG
// join point requiring BOTH H12a + H12b + H2a OpenAI).
//
// PURPOSE:
//   Populates sprk_aimodeldeployment runtime-reference rows (the 3 ADR-020
//   pinned models — see PinnedModelCatalog.cs) with the correct Azure OpenAI
//   endpoint URI for this customer's tenancy tier: Model 2 (dedicated) reads
//   the customer's own OpenAI endpoint from H2a's Bicep output
//   (InterStepState.OpenAiEndpoint); Model 1 (shared) reads the shared
//   platform OpenAI endpoint from RuntimeReferencesOptions + wires a
//   per-tenant metering-attribution note (task 077 owns the queryable
//   metering mechanism — see "Model 1 metering attribution" remark below).
//   This is the last mile that makes the BFF's AzureOpenAI:Endpoint config +
//   sprk_aimodeldeployment join resolve to the right endpoint per spec.md
//   FR-17 acceptance.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-17 (H12c
//     acceptance): "sprk_aimodeldeployment rows point at customer's OpenAI
//     deployment (Model 2) or shared platform OpenAI (Model 1 with per-tenant
//     metering attribution). BFF AzureOpenAIOptions.Endpoint resolves to
//     correct endpoint via env-var lookup + sprk_aimodeldeployment join."
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1a Model 1
//     vs Model 2 handler-differences table, H12c row: "sprk_aimodeldeployment
//     rows point at customer's dedicated OpenAI deployment" (Model 2) vs
//     "...shared platform OpenAI deployment with per-tenant metering
//     attribution" (Model 1).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 DAG:
//     "H12c (runtime refs — needs both H12a + H12b + H2a OpenAI)".
//   - ADR-004: idempotent handler contract.
//   - ADR-010: DI minimalism — IModelDeploymentReferenceWriter seam earns
//     keep: production Dataverse Web API impl + test fake from day 1.
//   - ADR-020: written rows reference the 3 pinned model deployments
//     (gpt-4o 2024-08-06, gpt-4o-mini 2024-07-18, text-embedding-3-large 1)
//     — see PinnedModelCatalog.cs.
//   - ADR-036: 3-level idempotency stack (Service Bus MessageId dedup +
//     Redis idempotency at Level 2 (not yet in L2) + handler body durable
//     dedup at Level 3 via CompletedPhases scan).
//
// LIVE DATAVERSE SCHEMA (task 072 deviation note — see
// projects/customer-provisioning-orchestration-r1/notes/task-072-h12c-deviations.md):
//   The POML's literal step 4 asks for "a metering-attribution tenantId
//   field" on Model 1 rows. The live sprk_aimodeldeployment schema (verified
//   via mcp__dataverse__describe) carries NO tenantId column — the table's
//   fields are sprk_name/sprk_modelid/sprk_provider/sprk_capability/
//   sprk_endpoint/sprk_contextwindow/sprk_description/sprk_isactive/
//   sprk_isdefault only. Per CLAUDE.md §6.5 Path C (pivot to comply): H12c
//   does NOT invent a new Dataverse column (a schema change is its own task,
//   out of scope for a handler-implementation task per CLAUDE.md §11 —
//   "prefer extending existing" does not license adding columns to a live
//   table from inside a handler task). Instead, Model 1 rows carry the
//   tenantId attribution as a human-readable note in sprk_description; the
//   QUERYABLE metering-attribution mechanism is task 077's scope (per-tenant
//   token metering layer), which is expected to key off the run's tenantId
//   parameter / BFF request context rather than a redundant column on this
//   shared reference table.
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   EVERY H12c failure mode classifies Resumable — parity with H7's
//   reasoning ("every write is a natural upsert... no QuarantineRequired
//   path exists"). The IModelDeploymentReferenceWriter upsert is
//   find-by-sprk_name / PATCH-if-found / POST-if-not per row; a partial
//   batch failure (e.g. 2 of 3 models written before an HTTP fault) is safe
//   to retry in full — already-written rows are found + harmlessly re-PATCHed,
//   missing rows get created. No handler in this file writes any
//   non-idempotent external side effect.
//   ┌─────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                        │ §4C class                 │
//   ├─────────────────────────────────────┼───────────────────────────┤
//   │ Run not found in Cosmos partition   │ Resumable                 │
//   │ Missing tenantId (§4D I1)           │ Resumable                 │
//   │ Missing InterStepState.DataverseEnvUrl │ Resumable (H5/H6 not run yet) │
//   │ Missing H12a and/or H12b in         │ Resumable (DAG-join point  │
//   │ CompletedPhases                     │ not yet satisfied)         │
//   │ Unknown tenancyModel value          │ Resumable (data/config     │
//   │                                     │ issue — operator fixes +   │
//   │                                     │ resumes; does NOT upsert)  │
//   │ Missing InterStepState.OpenAiEndpoint │ Resumable (H2a not run   │
//   │ (Model 2)                           │ yet / didn't populate)     │
//   │ Missing SharedPlatformOpenAiEndpoint │ Resumable (operator      │
//   │ config (Model 1)                    │ configuration gap)        │
//   │ Writer upsert failure (any row)     │ Resumable (upsert-safe —  │
//   │                                     │ full retry re-drives)     │
//   │ Concurrent Cosmos writer conflict   │ Resumable                 │
//   │ Run row deleted mid-flight          │ Resumable                 │
//   └─────────────────────────────────────┴───────────────────────────┘
//
// IDEMPOTENCY (3-level per ADR-004 / design.md §4.1):
//   Level 1 (Service Bus MessageId dedup): deterministic MessageId per
//           (HandlerId, RunId, CustomerId, paramHash).
//   Level 2 (Redis IdempotencyService): NOT YET IMPLEMENTED in L2.
//   Level 3 (handler body durable dedup): this handler scans
//           ProvisioningRun.CompletedPhases for (Phase=="H12c",
//           IdempotencyKey==h12c-{customerId}-{tenancyModel}-{endpointHash}).
//           endpointHash = SHA-256 hex of the RESOLVED endpoint URI (not the
//           tenancyModel-independent Bicep version) — per POML constraint
//           "Idempotency key: h12c-{customerId}-{tenancyModel}-{endpointHash}".
//           A tenancy migration or endpoint rotation changes the hash,
//           forcing a re-write on next invocation.
//
// DOWNSTREAM ENQUEUE (WAVE Cp TEMPORARY BRIDGE):
//   H12c is the single upstream trigger for H14 (post-deploy integration
//   wiring, task 073) per the design.md §4.1 DAG (H12c -> H14 -> H13). Same
//   temporary-bridge pattern as H0->H0.5 and H12a/H12b->H12c: the wave-C5
//   reconciler does not exist yet, so H12c enqueues H14 directly here on its
//   own success. Enqueue failure does NOT fail the handler — Cosmos state
//   already records H12c complete; the reconciler's crash-recovery scan
//   (design.md §4C I6) re-emits H14 (parity with H12b's enqueue-failure
//   handling).
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H12cRuntimeReferencesHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = "H12c";

    /// <summary>
    /// Handler identifier of the downstream DAG successor H12c enqueues on
    /// its own success (temporary wave-Cp bridge — see file header).
    /// </summary>
    public const string DownstreamHandlerId = "H14";

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Tenancy-model value for the dedicated (Model 2) tier.</summary>
    public const string Model2Dedicated = "Model2Dedicated";

    /// <summary>Tenancy-model value for the shared (Model 1) tier.</summary>
    public const string Model1Shared = "Model1Shared";

    /// <summary>Handler identifiers this DAG-join point requires in CompletedPhases before it can proceed.</summary>
    private static readonly string[] RequiredUpstreamHandlers = { "H12a", "H12b" };

    private readonly IProvisioningRunRepository _repository;
    private readonly IModelDeploymentReferenceWriter _writer;
    private readonly IHandlerEnqueuer _enqueuer;
    private readonly RuntimeReferencesOptions _options;
    private readonly ILogger<H12cRuntimeReferencesHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H12c runtime references handler. All collaborators are
    /// interface-abstracted so unit tests can substitute fakes for each seam
    /// (parity with H10/H7/H2a's constructor shape).
    /// </summary>
    public H12cRuntimeReferencesHandler(
        IProvisioningRunRepository repository,
        IModelDeploymentReferenceWriter writer,
        IHandlerEnqueuer enqueuer,
        Microsoft.Extensions.Options.IOptions<RuntimeReferencesOptions> options,
        ILogger<H12cRuntimeReferencesHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(enqueuer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _writer = writer;
        _enqueuer = enqueuer;
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
            // silently mis-executing (parity with every sibling handler).
            throw new InvalidOperationException(
                $"H12cRuntimeReferencesHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H12c runtime references starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H12c aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: RuntimeReferencesRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;

        // (2) §4D I1 tenant guard.
        if (!TryGetNonEmpty(run.Parameters.NonSecret, TenantIdParameterKey, out var tenantId))
        {
            var diagnostic =
                "Run parameter 'tenantId' is required by H12c (§4D I1 no-hardcoded-tenant). " +
                "Upstream handler MUST populate this before H12c dispatches.";
            return await FailAsync(run, etag, RuntimeReferencesRejectionCodes.MissingTenantId, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (3) DAG-join guard — H12c needs BOTH H12a + H12b complete
        // (design.md §4.1 DAG). H12a and/or H12b sibling-race to enqueue
        // H12c on their own success; whichever finishes second is the one
        // whose invocation actually finds both entries present.
        var missingUpstream = RequiredUpstreamHandlers
            .Where(h => !run.CompletedPhases.Any(cp => string.Equals(cp.Phase, h, StringComparison.Ordinal)))
            .ToList();
        if (missingUpstream.Count > 0)
        {
            var diagnostic =
                $"H12c requires BOTH H12a and H12b to be present in CompletedPhases before it can populate " +
                $"runtime references (design.md §4.1 DAG). Missing: {string.Join(", ", missingUpstream)}. " +
                "This is the normal DAG-join race — the missing handler(s) will enqueue H12c again on their " +
                "own completion; no operator action required unless this persists.";
            return await FailAsync(run, etag, RuntimeReferencesRejectionCodes.MissingUpstreamHandlers, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (4) Target Dataverse URL guard (H5/H6 output).
        var targetDataverseUrl = run.InterStepState.DataverseEnvUrl;
        if (string.IsNullOrWhiteSpace(targetDataverseUrl))
        {
            var diagnostic =
                "Target Dataverse URL not present on ProvisioningRun.interStepState.dataverseEnvUrl. " +
                "H5/H6 (Dataverse env creation + solution import) MUST complete before H12c dispatches.";
            return await FailAsync(run, etag, RuntimeReferencesRejectionCodes.MissingDataverseUrl, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (5) Tenancy-model branch — resolves the endpoint source per
        // design.md §4.1a. Unknown values fail loud + do NOT upsert
        // (POML negative acceptance criterion 6).
        string endpoint;
        string? meteringDescription = null;
        switch (run.TenancyModel)
        {
            case Model2Dedicated:
                var dedicatedEndpoint = run.InterStepState.OpenAiEndpoint;
                if (string.IsNullOrWhiteSpace(dedicatedEndpoint))
                {
                    var diagnostic =
                        "Model2Dedicated tenancy but ProvisioningRun.interStepState.openAiEndpoint is null/blank. " +
                        "H2a (task 044 — Bicep infra deploy) MUST complete + populate interStepState before H12c " +
                        "dispatches on a dedicated-tier customer.";
                    return await FailAsync(run, etag, RuntimeReferencesRejectionCodes.MissingOpenAiEndpoint, diagnostic, cancellationToken)
                        .ConfigureAwait(false);
                }
                endpoint = dedicatedEndpoint;
                meteringDescription = "Customer-dedicated Azure OpenAI deployment (Model2Dedicated tenancy).";
                break;

            case Model1Shared:
                var sharedEndpoint = _options.SharedPlatformOpenAiEndpoint;
                if (string.IsNullOrWhiteSpace(sharedEndpoint))
                {
                    var diagnostic =
                        "Model1Shared tenancy but RuntimeReferencesOptions:SharedPlatformOpenAiEndpoint is not " +
                        "configured. Operator must set this app-setting for this L2 environment before ANY " +
                        "Model1Shared customer can complete H12c.";
                    return await FailAsync(run, etag, RuntimeReferencesRejectionCodes.MissingSharedPlatformEndpointConfiguration, diagnostic, cancellationToken)
                        .ConfigureAwait(false);
                }
                endpoint = sharedEndpoint;
                // Metering attribution (task 077 owns the queryable mechanism —
                // see file header "LIVE DATAVERSE SCHEMA" deviation note; this
                // table has no dedicated tenantId column).
                meteringDescription =
                    $"Shared platform Azure OpenAI deployment (Model1Shared tenancy). Per-tenant metering " +
                    $"attribution: tenantId={tenantId}, customerId={envelope.CustomerId} (task 077 owns the " +
                    $"queryable metering-attribution mechanism).";
                break;

            default:
                var unknownDiagnostic =
                    $"ProvisioningRun.tenancyModel '{run.TenancyModel ?? "(null)"}' is not a recognized value. " +
                    $"Expected '{Model2Dedicated}' or '{Model1Shared}'. Handler did NOT upsert any " +
                    "sprk_aimodeldeployment row.";
                return await FailAsync(run, etag, RuntimeReferencesRejectionCodes.UnknownTenancyModel, unknownDiagnostic, cancellationToken)
                    .ConfigureAwait(false);
        }

        // (6) Idempotency key — per POML constraint:
        // h12c-{customerId}-{tenancyModel}-{endpointHash}.
        var endpointHash = ComputeEndpointHash(endpoint);
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, run.TenancyModel!, endpointHash);

        // (7) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H12c idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (8) Build the 3-model ADR-020 catalog projection + invoke the writer.
        var deployments = PinnedModelCatalog.Models
            .Select(m => new ModelDeploymentReference(m.ModelId, m.Capability, endpoint, meteringDescription))
            .ToList();

        ModelDeploymentReferenceWriteOutcome outcome;
        try
        {
            outcome = await _writer.UpsertAsync(
                new ModelDeploymentReferenceWriteRequest(targetDataverseUrl, tenantId, ModelProvider.AzureOpenAI, deployments),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H12c model-deployment-reference-writer infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"Model deployment reference writer infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Upsert is retry-safe in full.";
            return await FailAsync(run, etag, RuntimeReferencesRejectionCodes.ModelDeploymentWriteFailed, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        if (outcome is ModelDeploymentReferenceWriteOutcome.Failure writeFailure)
        {
            return await FailAsync(run, etag, RuntimeReferencesRejectionCodes.ModelDeploymentWriteFailed, writeFailure.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        var success = (ModelDeploymentReferenceWriteOutcome.Success)outcome;

        // (9) All post-conditions cleared — advance Cosmos state + enqueue H14.
        stopwatch.Stop();
        _logger.LogInformation(
            "H12c runtime references succeeded: runId={RunId} customerId={CustomerId} tenancyModel={TenancyModel} " +
            "upsertedCount={UpsertedCount} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, run.TenancyModel, success.UpsertedModelIds.Count, stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAndAdvanceAsync(run, etag, idempotencyKey, success, envelope, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H12c idempotency key:
    /// <c>h12c-{customerId}-{tenancyModel}-{endpointHash}</c>. Exposed
    /// internal so unit tests can construct expected keys without
    /// duplicating the format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, string tenancyModel, string endpointHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenancyModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointHash);
        return $"h12c-{customerId}-{tenancyModel}-{endpointHash}";
    }

    /// <summary>
    /// Computes the SHA-256 hex hash of the resolved endpoint URI. Exposed
    /// internal so unit tests can validate the exact key format without
    /// duplicating the hash algorithm. A byte-for-byte-identical endpoint
    /// produces the same hash — an endpoint rotation (or a Model1/Model2
    /// tenancy migration) produces a new hash, forcing a re-write.
    /// </summary>
    internal static string ComputeEndpointHash(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        var bytes = Encoding.UTF8.GetBytes(endpoint);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
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
        string rejectionCode,
        string diagnostic,
        CancellationToken cancellationToken)
    {
        // Every H12c failure mode is Resumable — see file header ROLLBACK
        // CLASSIFICATION table.
        run.Status = RunStatus.Failed;
        run.CurrentPhase = HandlerIdentifier;
        run.ErrorDetail = $"[{rejectionCode}] {diagnostic}";

        run.GateStates[$"h12c-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H12c failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H12c failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(FailureClass.Resumable, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAndAdvanceAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        ModelDeploymentReferenceWriteOutcome.Success success,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        run.Status = RunStatus.Running;
        run.CurrentPhase = DownstreamHandlerId; // Reconciler observes + knows H14 is next.
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        run.ErrorDetail = null;

        run.GateStates[RuntimeReferencesGates.RuntimeReferencesWritten] = new GateEntry
        {
            Status = GateState.Verified,
            VerifierHandler = HandlerIdentifier,
            Evidence = BuildEvidence(success),
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H12c success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: RuntimeReferencesRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H12c read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H12c.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H12c success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: RuntimeReferencesRejectionCodes.RunDeletedDuringWrite,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H12c was in flight.");
        }

        // Enqueue H14 — TEMPORARY WAVE-Cp BRIDGE (see file header).
        var downstreamEnvelope = new HandlerEnvelope
        {
            HandlerId = DownstreamHandlerId,
            RunId = envelope.RunId,
            CustomerId = envelope.CustomerId,
            ParametersJson = "{}", // H14 reads its own parameters from Cosmos.
            EnqueuedAt = completedAt,
        };
        try
        {
            await _enqueuer.EnqueueAsync(downstreamEnvelope, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Cosmos state already records H12c complete + CurrentPhase = H14.
            // Enqueue failure is recoverable — the reconciler's background
            // scan (wave C5, design.md §crash recovery I6) will re-emit H14.
            _logger.LogError(
                ex,
                "H12c succeeded but H14 downstream enqueue failed — reconciler scan will re-emit: " +
                "runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Success(idempotencyKey);
    }

    private static JsonElement BuildEvidence(ModelDeploymentReferenceWriteOutcome.Success success)
    {
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            upsertedModelIds = success.UpsertedModelIds,
        }));
        return doc.RootElement.Clone();
    }
}
