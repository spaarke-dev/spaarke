// -----------------------------------------------------------------------------
// H6SolutionImportHandler.cs
//
// L2 CONTROL-PLANE H6 Package Deployer solution-import handler (task 049,
// wave C4 Batch 3D).
//
// PURPOSE:
//   Imports the 8 authoritative Spaarke managed solutions (spec.md §11.1a +
//   FR-09) into the per-customer Dataverse env created by H5. Wraps the
//   wave-0 (task 012) hardened scripts/Deploy-DataverseSolutions.ps1 which
//   IS the runtime source of truth for the solution list ($SolutionImportOrder
//   per task 008 R5 binding) + dependency-tier ordering (Tier 1 SpaarkeCore →
//   Tier 2 webresources → Tier 3 six feature solutions) + Package Deployer
//   stage-and-upgrade semantics (retires holding solution on upgrade per
//   spec.md FR-09 acceptance).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-09:
//       Package Deployer 8-solution import with dependency ordering + upgrade
//       mode retires holding solution.
//   - projects/customer-provisioning-orchestration-r1/spec.md §11.1a:
//       Authoritative list of 8 solutions per Deploy-DataverseSolutions.ps1
//       $SolutionImportOrder (v3 correction from "~10" to 8).
//   - projects/customer-provisioning-orchestration-r1/spec.md §4D I1:
//       No hardcoded default tenant — MUST flow tenantId through explicitly.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H6:
//       Long-running (up to 60 min); idempotency key
//       `solimport-{customerId}-{solutionVer}` (solutionVer = catalog hash).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.2 (v3.2):
//       Fire-and-forget handler execution model — L2 REST endpoints enqueue
//       + return 202; reconciler owns advancement.
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback:
//       Auth / rate-limit / quota → Resumable (no side effect OR PS idempotent
//       on retry). Partial-import + verification-failure + retired-artifact
//       reintroduction → QuarantineRequired (partial state or ADR violation).
//       Timeout → Resumable (PS is idempotent; re-run resumes safely).
//   - projects/customer-provisioning-orchestration-r1/design.md §6.2
//       interStepState field: `importedSolutions` is the H6-owned slot
//       (controlled schema extension per task 049; see InterStepState.cs).
//   - .claude/adr/ADR-004-job-contract.md: idempotent + at-least-once safe.
//   - .claude/adr/ADR-010-di-minimalism.md: 3 seams (catalog + importer +
//       verifier) — ≥2 impls each (production + test), no NIH.
//   - .claude/adr/ADR-036-background-job-infrastructure.md: reuse Service
//       Bus + IJobHandler infrastructure (spec FR-22 / R20).
//   - .claude/adr/ADR-039-grounded-execution-closed-catalogs.md: single AI
//       routing surface — defense-in-depth guard against retired-artifact
//       reintroduction via any solution (catalog scan at pre-check).
//   - .claude/adr/ADR-044-dataverse-guid-canonicalization.md: solution ids
//       are Dataverse GUIDs — canonicalize on write to Cosmos.
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌─────────────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                                │ §4C class                 │
//   ├─────────────────────────────────────────────┼───────────────────────────┤
//   │ Missing tenantId (§4D I1)                   │ Resumable                 │
//   │ Missing InterStepState.DataverseEnvUrl (H5) │ Resumable                 │
//   │ Missing InterStepState.BffAppRegId (H3)     │ Resumable                 │
//   │ Missing bound ClientSecret (Wave C5 wire)   │ Resumable                 │
//   │ Run not found in Cosmos partition           │ Resumable                 │
//   │ Retired-solution catalog match (ADR-039)    │ QuarantineRequired        │
//   │ pac auth failure                            │ Resumable                 │
//   │ Rate-limited                                │ Resumable                 │
//   │ Quota exhausted                             │ Resumable                 │
//   │ Missing solution ZIPs (build error)         │ Resumable                 │
//   │ pac timeout (import window)                 │ Resumable (ImportTimeout) │
//   │ Partial import (Tier N fail after Tier N-1) │ QuarantineRequired        │
//   │ Unknown invocation failure (no partial)     │ Resumable                 │
//   │ Verifier reports missing solutions          │ QuarantineRequired        │
//   │ Importer infrastructure exception           │ Resumable                 │
//   │ Concurrent Cosmos writer conflict           │ Resumable                 │
//   │ Run row deleted mid-flight                  │ Resumable                 │
//   └─────────────────────────────────────────────┴───────────────────────────┘
//
// IDEMPOTENCY (3-level per ADR-004 / design.md §4.1):
//   Level 1 (Service Bus MessageId dedup): reconciler computes deterministic
//           MessageId per (HandlerId, RunId, CustomerId, paramHash); SB
//           duplicate-detection collapses re-enqueues.
//   Level 2 (Redis IdempotencyService): NOT YET IMPLEMENTED in L2 (parity
//           with H0 / H0.5 / H1 / H2a / H5 / H12a).
//   Level 3 (handler body durable dedup): scans
//           ProvisioningRun.CompletedPhases for (Phase == "H6",
//           IdempotencyKey == solimport-{customerId}-{catalogHash}). Match →
//           Success no-op (no importer / verifier invocation, no state mutation).
//           A catalog change (adding a solution, changing a Tier) produces
//           a new hash + new key → forces re-import (Package Deployer handles
//           per-solution version dedup idempotently on the Dataverse side).
//
// DOWNSTREAM ENQUEUE (WAVE C4 NOTE):
//   H6's successor is H7 (env-var values — task 050, Wave 3E). Wave C4 does
//   NOT enqueue H7 from H6 — parity with H5 (which also has a single-successor
//   pattern). Wave C5 reconciler observes CurrentPhase == "H6" +
//   CompletedPhases + fans out to H7. This handler mutates Cosmos state
//   (CurrentPhase + CompletedPhases + InterStepState.ImportedSolutions) and
//   returns Success.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H6SolutionImportHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md § 4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = "H6";

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Verified gate identifier for the H6 solution-import post-condition.</summary>
    public const string SolutionsImportedGateId = "h6-solutions-imported";

    private readonly IProvisioningRunRepository _repository;
    private readonly ISolutionCatalog _catalog;
    private readonly ISolutionImporter _importer;
    private readonly ISolutionVerifier _verifier;
    private readonly SolutionImportOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<H6SolutionImportHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H6 handler. All collaborators are interface-abstracted
    /// so unit tests can substitute stubs (parity with H5 / H2b / H12a
    /// constructor shapes). <see cref="TimeProvider"/> is injected (per
    /// docs/standards/TEST-ARCHITECTURE.md — "TimeProvider over Stopwatch")
    /// for deterministic testability.
    /// </summary>
    public H6SolutionImportHandler(
        IProvisioningRunRepository repository,
        ISolutionCatalog catalog,
        ISolutionImporter importer,
        ISolutionVerifier verifier,
        IOptions<SolutionImportOptions> options,
        TimeProvider timeProvider,
        ILogger<H6SolutionImportHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _catalog = catalog;
        _importer = importer;
        _verifier = verifier;
        _options = options.Value;
        _timeProvider = timeProvider;
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
            // Defensive: reconciler routes by HandlerId string match. Mismatch
            // = dispatch bug — fail loud rather than silently mis-executing.
            throw new InvalidOperationException(
                $"H6SolutionImportHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H6 solution import starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H6 aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SolutionImportRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, _catalog.CatalogHash);

        // (2) Level-3 idempotency: durable no-op on duplicate. A catalog change
        //     (add solution, change Tier) produces a new hash → new key →
        //     re-import (Package Deployer handles per-solution dedup).
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H6 idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey} existingSolutionCount={Count}",
                envelope.RunId, idempotencyKey,
                run.InterStepState.ImportedSolutions?.Count ?? 0);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (3) ADR-039 defense-in-depth: catalog MUST NOT reintroduce retired
        //     artifacts. Runs BEFORE any Dataverse-side operation so an
        //     ADR-violating deployment fails at pre-check with a clean
        //     diagnostic instead of importing then discovering the problem.
        var retiredHit = FindRetiredMatch(_catalog);
        if (retiredHit is not null)
        {
            var diagnostic =
                $"Canonical solution catalog contains retired-artifact match: " +
                $"solution '{retiredHit.Value.SolutionUniqueName}' matches retired pattern '{retiredHit.Value.Pattern}'. " +
                "ADR-039 (single AI routing surface) + amendment 2026-07-05 forbid reintroducing the retired " +
                "dispatcher / embeddings surface via any solution. Handler did NOT invoke Deploy-DataverseSolutions.ps1 — " +
                "review the CanonicalSolutionCatalog vs the retired-artifact list before re-running H6.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SolutionImportRejectionCodes.RetiredSolutionReintroduction, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (4) §4D I1 tenant guard — H6 MUST NOT fall back to a default tenant.
        //     Fires BEFORE any Dataverse-side call so acceptance criterion
        //     (no importer call fired) holds by construction.
        var parameters = run.Parameters.NonSecret;
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            var diagnostic =
                "Run parameter 'tenantId' is required by H6 (§4D I1 no-hardcoded-tenant). " +
                "Upstream handler (H0.5 for Model 2, L2 endpoint for Model 1) MUST populate this before H6 dispatches. " +
                "pac auth create --tenant requires an explicit tenant — H6 refuses to guess.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                SolutionImportRejectionCodes.MissingTenantId, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (5) Target Dataverse URL guard — sourced from InterStepState.DataverseEnvUrl
        //     (written by H5). If null, H5 has not completed and H6 cannot
        //     proceed.
        var targetDataverseUrl = run.InterStepState.DataverseEnvUrl;
        if (string.IsNullOrWhiteSpace(targetDataverseUrl))
        {
            var diagnostic =
                "Target Dataverse URL not present on ProvisioningRun.interStepState.dataverseEnvUrl. " +
                "H5 (Dataverse env creation) MUST complete before H6 dispatches. " +
                "Handler did NOT invoke Deploy-DataverseSolutions.ps1.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                SolutionImportRejectionCodes.MissingDataverseUrl, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (6) BFF app-reg id guard — sourced from InterStepState.BffAppRegId
        //     (written by H3). Required for pac auth create --applicationId.
        var clientId = run.InterStepState.BffAppRegId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            var diagnostic =
                "BFF app-reg id not present on ProvisioningRun.interStepState.bffAppRegId. " +
                "H3 (Entra app registration) MUST complete before H6 dispatches. " +
                "Handler did NOT invoke Deploy-DataverseSolutions.ps1.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                SolutionImportRejectionCodes.MissingBffAppRegId, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (7) Client-secret guard — resolved from bound options. Wave C5
        //     wires this to a Key Vault reference; wave C4 requires
        //     operator to populate SolutionImportOptions:ClientSecret at
        //     deploy time.
        var clientSecret = _options.ClientSecret;
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            var diagnostic =
                "SolutionImportOptions:ClientSecret is not populated. " +
                "Wave C5 wires this to a Key Vault reference (@Microsoft.KeyVault(SecretUri=...)); " +
                "wave C4 requires operator to set the app-setting explicitly. " +
                "Handler did NOT invoke Deploy-DataverseSolutions.ps1.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                SolutionImportRejectionCodes.MissingClientSecret, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (8) Invoke the importer. Long-running (up to 60 min per POML) —
        //     reconciler owns keeping the ambient Service Bus lock alive per
        //     FR-22 / R20. Infrastructure exceptions classified Resumable
        //     (except OperationCanceledException which propagates for shutdown).
        var importRequest = new SolutionImportRequest(
            CustomerId: envelope.CustomerId,
            TenantId: tenantId,
            ClientId: clientId,
            ClientSecret: clientSecret,
            TargetDataverseUrl: targetDataverseUrl);

        SolutionImportOutcome importOutcome;
        try
        {
            importOutcome = await _importer.ImportAsync(importRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "H6 importer infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"Solution importer infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Verify pwsh + scripts/Deploy-DataverseSolutions.ps1 are on the L2 App Service.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                SolutionImportRejectionCodes.ImportInvocationFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        if (importOutcome is SolutionImportOutcome.Failure importFailure)
        {
            var (rejection, cls) = MapImporterFailure(importFailure.FailureKind);
            return await FailAsync(run, etag, cls, rejection, importFailure.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (9) Post-import verification — POML acceptance criterion #1.
        //     Independently confirms the 8 catalog solutions are installed +
        //     builds the Cosmos manifest with actual (version, solutionId).
        var verifyRequest = new SolutionVerificationRequest(
            TargetDataverseUrl: targetDataverseUrl,
            TenantId: tenantId,
            ClientId: clientId,
            ExpectedCatalog: _catalog.Solutions);

        SolutionVerificationOutcome verifyOutcome;
        try
        {
            verifyOutcome = await _verifier.VerifyAsync(verifyRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "H6 verifier infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"Solution verifier infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Import may have succeeded — operator inspects `pac solution list` manually + resumes.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SolutionImportRejectionCodes.VerificationFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        if (verifyOutcome is SolutionVerificationOutcome.Missing missing)
        {
            var diagnostic =
                $"Post-import verification FAILED: importer reported Success but the following expected " +
                $"solutions are missing on the target env: {string.Join(", ", missing.MissingUniqueNames)}. " +
                $"Verifier detail: {missing.Diagnostic}. " +
                "Operator inspects `pac solution list --environment {envUrl}` to reconcile before resume.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SolutionImportRejectionCodes.VerificationFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        var allPresent = (SolutionVerificationOutcome.AllPresent)verifyOutcome;

        // (10) All post-conditions cleared — write ImportedSolutions manifest
        //      + advance Cosmos state. Reconciler (Wave C5) fans out to H7.
        stopwatch.Stop();
        _logger.LogInformation(
            "H6 solution import succeeded: runId={RunId} customerId={CustomerId} " +
            "solutionCount={Count} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId,
            allPresent.ImportedRecords.Length, stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(
            run, etag, idempotencyKey, allPresent.ImportedRecords, envelope, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H6 idempotency key:
    /// <c>solimport-{customerId}-{catalogHash}</c>. Exposed <c>internal</c>
    /// so unit tests can construct expected keys without duplicating the format.
    /// Parity with H2a's <c>infra-{customerId}-{bicepVer}</c> +
    /// H12a's <c>h12a-{customerId}-{manifestHash}</c>.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, string catalogHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogHash);
        return $"solimport-{customerId}-{catalogHash}";
    }

    /// <summary>
    /// Maps <see cref="SolutionImportFailureKind"/> to the pair
    /// (rejectionCode, §4C failureClass). Exposed <c>internal</c> so tests
    /// can assert the mapping without depending on handler internals. Parity
    /// with H5's <c>MapCreatorFailure</c> shape.
    /// </summary>
    internal static (string RejectionCode, FailureClass Class) MapImporterFailure(
        SolutionImportFailureKind kind) => kind switch
        {
            SolutionImportFailureKind.AuthFailure =>
                (SolutionImportRejectionCodes.PacAuthFailure, FailureClass.Resumable),
            SolutionImportFailureKind.RateLimited =>
                (SolutionImportRejectionCodes.RateLimited, FailureClass.Resumable),
            SolutionImportFailureKind.QuotaExhausted =>
                (SolutionImportRejectionCodes.QuotaExhausted, FailureClass.Resumable),
            SolutionImportFailureKind.MissingSolutionZips =>
                (SolutionImportRejectionCodes.MissingSolutionZips, FailureClass.Resumable),
            SolutionImportFailureKind.PartialImport =>
                (SolutionImportRejectionCodes.PartialImportDetected, FailureClass.QuarantineRequired),
            SolutionImportFailureKind.Timeout =>
                (SolutionImportRejectionCodes.ImportTimeout, FailureClass.Resumable),
            SolutionImportFailureKind.UnknownInvocationFailure =>
                (SolutionImportRejectionCodes.ImportInvocationFailed, FailureClass.Resumable),
            _ =>
                (SolutionImportRejectionCodes.ImportInvocationFailed, FailureClass.Resumable),
        };

    /// <summary>
    /// Cross-references the catalog entries against the retired-artifact list.
    /// Returns the FIRST match (solution + pattern) or null if the catalog is
    /// clean. Exposed <c>internal</c> for direct testing.
    /// </summary>
    internal static (string SolutionUniqueName, string Pattern)? FindRetiredMatch(ISolutionCatalog catalog)
    {
        foreach (var entry in catalog.Solutions)
        {
            foreach (var retired in catalog.RetiredSolutionUniqueNames)
            {
                if (entry.SolutionUniqueName.Contains(retired, StringComparison.OrdinalIgnoreCase))
                {
                    return (entry.SolutionUniqueName, retired);
                }
            }
        }
        return null;
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
                QuarantinedAt = _timeProvider.GetUtcNow(),
            };
        }

        // Gate entry — one per failure. Verifier is H6; state is Pending
        // (external precondition to resolve). Parity with H5's FailAsync.
        run.GateStates[$"h6-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H6 failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H6 failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        ImmutableArray<ImportedSolutionRecord> importedRecords,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completedAt = _timeProvider.GetUtcNow();
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier; // Reconciler observes + fans out to H7.
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        run.ErrorDetail = null;

        // Populate interStepState.ImportedSolutions (design.md §6.2 controlled
        // extension per task 049) — H6-owned slot consumed by H7 for option-set /
        // config lookups keyed by solutionId.
        run.InterStepState.ImportedSolutions = importedRecords.ToList();

        // Verified-gate entry — one for the solutions-imported post-condition.
        // Evidence captures per-solution version + solutionId + tier so an
        // operator sees the manifest without pulling logs.
        var evidencePayload = new
        {
            catalogHash = _catalog.CatalogHash,
            solutionCount = importedRecords.Length,
            solutions = importedRecords.Select(r => new
            {
                uniqueName = r.SolutionUniqueName,
                version = r.Version,
                solutionId = r.SolutionId,
                tier = r.Tier,
            }).ToArray(),
        };
        var evidence = JsonDocument.Parse(JsonSerializer.Serialize(evidencePayload)).RootElement.Clone();
        run.GateStates[SolutionsImportedGateId] = new GateEntry
        {
            Status = GateState.Verified,
            VerifierHandler = HandlerIdentifier,
            VerifiedAt = completedAt,
            Evidence = evidence,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H6 success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SolutionImportRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H6 read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H6 " +
                             "which will short-circuit on idempotency.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H6 success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SolutionImportRejectionCodes.RunDeletedDuringImport,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H6 was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
