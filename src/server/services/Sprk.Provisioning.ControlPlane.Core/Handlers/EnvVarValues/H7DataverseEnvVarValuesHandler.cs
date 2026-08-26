// -----------------------------------------------------------------------------
// H7DataverseEnvVarValuesHandler.cs
//
// L2 CONTROL-PLANE H7 Dataverse env-var values handler (task 050, wave C4
// Batch 3E).
//
// PURPOSE:
//   Sets the 7 canonical per-customer Dataverse environment variables (spec.md
//   FR-10 + design.md §10.3) that gate MDA + Code Page client startup. Task
//   024 (r1-lineage) established the client-fails-fast contract: NO hardcoded
//   URL fallbacks in client code — every one of the 7 values MUST exist as a
//   real `environmentvariablevalue` record before a client bootstraps against
//   the customer's Dataverse env. H7 is the ONLY handler responsible for
//   guaranteeing that precondition holds.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-10:
//       7 canonical Dataverse env-var values; client startup fails fast if
//       any is missing.
//   - projects/customer-provisioning-orchestration-r1/design.md §10.2 + §10.3:
//       Value sources — bffApiAppId/msalClientId/tenantId/bffApiBaseUrl/
//       shareLinkBaseUrl are run parameters (with documented defaults for
//       bffApiBaseUrl + msalClientId + shareLinkBaseUrl); azureOpenAiEndpoint
//       is H2a Bicep output; sharePointEmbeddedContainerId is H8 output.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H7 row:
//       Idempotency key `envvars-{customerId}-{configVer}`.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.2 (v3.2):
//       Fire-and-forget handler execution model.
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback:
//       Every failure mode is Resumable — see EnvVarValuesRejectionCodes.cs
//       file header for the full rationale (all writes are natural upserts).
//   - .claude/adr/ADR-004-job-contract.md: idempotent + at-least-once safe.
//   - .claude/adr/ADR-010-di-minimalism.md: 1 seam (writer) — ≥2 impls
//       (production + test), no NIH abstraction.
//   - .claude/adr/ADR-036-background-job-infrastructure.md: reuse Service
//       Bus + IJobHandler infrastructure (spec FR-22 / R20).
//   - .claude/adr/ADR-044-dataverse-guid-canonicalization.md: sprk_BffApiAppId
//       / sprk_MsalClientId / sprk_TenantId are Dataverse-adjacent GUID
//       values sourced verbatim from upstream state (already canonicalized at
//       their origin boundary — H3's InterStepState.BffAppRegId write, the
//       operator-supplied tenantId parameter); H7 does not re-canonicalize
//       (no braces/casing transform needed for these string-typed values).
//   - spec.md §4D I1: tenantId flows through explicitly; no default tenant
//       fallback (enforced as a hard MissingUpstreamState guard, mirroring
//       H5/H6's tenantId guard).
//
// VALUE-SOURCING DECISION (documented deviation — see
// projects/customer-provisioning-orchestration-r1/notes/task-050-deviations.md
// for the full CLAUDE.md §6.5-adjacent write-up):
//   The POML's acceptance criterion #2 ("missing from Cosmos interStepState")
//   groups {bffAppRegId, dataverseEnvUrl, openAiEndpoint, tenantId,
//   speContainerId} as REQUIRED-fail-if-missing. design.md §10.2 documents
//   THREE of the 7 env-vars (bffApiBaseUrl, msalClientId, shareLinkBaseUrl)
//   as OPTIONAL run parameters with explicit defaults. This handler follows
//   design.md §10.2/§10.3 (the more detailed, authoritative source) over the
//   POML's terser paraphrase: the 5 AC#2-listed keys are hard-required
//   (MissingUpstreamState); the other 3 resolve via parameter-with-default
//   per §10.2's documented defaults. This is Path C (pivot to comply with
//   the more detailed design doc) — not a silent deviation from either
//   source, since both sources are satisfied: AC#2's 5 keys still fail hard,
//   and §10.2's defaults are honored exactly as documented.
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌─────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                        │ §4C class                 │
//   ├─────────────────────────────────────┼───────────────────────────┤
//   │ Missing tenantId (§4D I1)           │ Resumable                 │
//   │ Missing bffAppRegId (H3)            │ Resumable                 │
//   │ Missing dataverseEnvUrl (H5)        │ Resumable                 │
//   │ Missing openAiEndpoint (H2a)        │ Resumable                 │
//   │ Missing speContainerId (H8)         │ Resumable                 │
//   │ Missing ClientSecret (config)       │ Resumable                 │
//   │ Run not found in Cosmos partition   │ Resumable                 │
//   │ Writer AuthFailure                  │ Resumable                 │
//   │ Writer RateLimited                  │ Resumable                 │
//   │ Writer DefinitionNotFound           │ Resumable                 │
//   │ Writer UnknownInvocationFailure     │ Resumable                 │
//   │ Writer infrastructure exception     │ Resumable                 │
//   │ Concurrent Cosmos writer conflict   │ Resumable                 │
//   │ Run row deleted mid-flight          │ Resumable                 │
//   └─────────────────────────────────────┴───────────────────────────┘
//   No QuarantineRequired path — every environmentvariablevalue write is a
//   natural upsert (PATCH-if-exists / POST-if-not), so retrying the FULL
//   handler after ANY failure (including one that occurs mid-way through the
//   7-variable loop) is always safe. There is no partial external state that
//   requires cleanup.
//
// IDEMPOTENCY (3-level per ADR-004 / design.md §4.1):
//   Level 1 (Service Bus MessageId dedup): the future reconciler computes
//           deterministic MessageId per (HandlerId, RunId, CustomerId,
//           paramHash); SB duplicate-detection collapses re-enqueues.
//   Level 2 (Redis IdempotencyService): NOT YET IMPLEMENTED in L2 (parity
//           with H0 / H0.5 / H1 / H2a / H5 / H6 / H12a).
//   Level 3 (handler body durable dedup): scans
//           ProvisioningRun.CompletedPhases for (Phase == "H7",
//           IdempotencyKey == envvars-{customerId}-{configVer}). Match →
//           Success no-op (no writer invocation). configVer is a SHA-256
//           hash of the 7 resolved (schemaName, value) pairs in fixed
//           canonical order — a content change to ANY resolved value
//           (upstream state advanced, operator changed a parameter) produces
//           a new hash + new key + re-write (parity with H6's catalogHash /
//           H4's secretsVer pattern).
//
// DOWNSTREAM ENQUEUE (WAVE C4 NOTE):
//   H7's successor in the DAG (design.md §4.1) is H10 (app user). Wave C4
//   does NOT enqueue H10 from H7 — parity with H5/H6's single-successor
//   pattern. Wave C5 reconciler observes CurrentPhase == "H7" +
//   CompletedPhases + fans out to H10.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H7DataverseEnvVarValuesHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md § 4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H7;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the operator-supplied BFF API base URL override.</summary>
    public const string BffApiBaseUrlParameterKey = "bffApiBaseUrl";

    /// <summary>Non-secret parameter key carrying the operator-supplied MSAL public-client id override.</summary>
    public const string MsalClientIdParameterKey = "msalClientId";

    /// <summary>Non-secret parameter key carrying the operator-supplied share-link base URL.</summary>
    public const string ShareLinkBaseUrlParameterKey = "shareLinkBaseUrl";

    /// <summary>design.md §10.2 documented default for <c>bffApiBaseUrl</c> when the operator does not supply an override.</summary>
    public const string DefaultBffApiBaseUrl = "https://api.spaarke.com";

    // ---- Canonical Dataverse env-var schema names (design.md §10.3 — exact spelling) ----
    public const string BffApiBaseUrlSchemaName = "sprk_BffApiBaseUrl";
    public const string BffApiAppIdSchemaName = "sprk_BffApiAppId";
    public const string MsalClientIdSchemaName = "sprk_MsalClientId";
    public const string TenantIdSchemaName = "sprk_TenantId";
    public const string AzureOpenAiEndpointSchemaName = "sprk_AzureOpenAiEndpoint";
    public const string ShareLinkBaseUrlSchemaName = "sprk_ShareLinkBaseUrl";
    public const string SharePointEmbeddedContainerIdSchemaName = "sprk_SharePointEmbeddedContainerId";

    /// <summary>
    /// The 7 canonical schema names in the FIXED order design.md §10.3 lists
    /// them (# 1-7). This order drives both the configVer hash computation
    /// (determinism) and the write order sent to <see cref="IEnvVarValuesWriter"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> CanonicalSchemaNamesInOrder = new[]
    {
        BffApiBaseUrlSchemaName,
        BffApiAppIdSchemaName,
        MsalClientIdSchemaName,
        TenantIdSchemaName,
        AzureOpenAiEndpointSchemaName,
        ShareLinkBaseUrlSchemaName,
        SharePointEmbeddedContainerIdSchemaName,
    };

    /// <summary>Verified gate identifier for the H7 env-vars-set post-condition.</summary>
    public const string EnvVarsSetGateId = "h7-env-vars-set";

    private readonly IProvisioningRunRepository _repository;
    private readonly IEnvVarValuesWriter _writer;
    private readonly EnvVarValuesOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<H7DataverseEnvVarValuesHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H7 handler. All collaborators are interface-abstracted
    /// so unit tests can substitute stubs for each seam. <see cref="TimeProvider"/>
    /// is injected (per docs/standards/TEST-ARCHITECTURE.md) for deterministic
    /// testability, matching H5/H6/H4's constructor shape.
    /// </summary>
    public H7DataverseEnvVarValuesHandler(
        IProvisioningRunRepository repository,
        IEnvVarValuesWriter writer,
        IOptions<EnvVarValuesOptions> options,
        TimeProvider timeProvider,
        ILogger<H7DataverseEnvVarValuesHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _writer = writer;
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
            // Defensive: the reconciler routes by HandlerId string match. A
            // mismatch here means a dispatch bug — fail loud rather than
            // silently mis-executing (parity with H0/H3/H5/H6).
            throw new InvalidOperationException(
                $"H7DataverseEnvVarValuesHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H7 Dataverse env-var values starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H7 aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: EnvVarValuesRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;
        var state = run.InterStepState;

        // (2) Required-upstream-state guards (§4D I1 + design.md §10.2/§10.3;
        // see file header VALUE-SOURCING DECISION for why these 5 — and only
        // these 5 — are hard-required). Each fires BEFORE the writer is ever
        // invoked, so acceptance criterion "no writer call fired" holds by
        // construction for every one of these.
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            return await FailMissingUpstreamAsync(run, etag, "tenantId",
                "Run parameter 'tenantId' is required by H7 (§4D I1 no-hardcoded-tenant). " +
                "H7 refuses to guess the tenant for token acquisition.", cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(state.BffAppRegId))
        {
            return await FailMissingUpstreamAsync(run, etag, "bffAppRegId",
                "InterStepState.bffAppRegId not present. H3 (Entra app registration) MUST complete before H7 " +
                "dispatches — this value is both the client-credentials identity H7 authenticates with AND the " +
                "source for sprk_BffApiAppId.", cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(state.DataverseEnvUrl))
        {
            return await FailMissingUpstreamAsync(run, etag, "dataverseEnvUrl",
                "InterStepState.dataverseEnvUrl not present. H5 (Dataverse env creation) MUST complete before " +
                "H7 dispatches — H7 has no target environment to write to.", cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(state.OpenAiEndpoint))
        {
            return await FailMissingUpstreamAsync(run, etag, "openAiEndpoint",
                "InterStepState.openAiEndpoint not present. H2a (Bicep infra deploy) MUST complete before H7 " +
                "dispatches — this is the source for sprk_AzureOpenAiEndpoint.", cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(state.SpeContainerId))
        {
            return await FailMissingUpstreamAsync(run, etag, "speContainerId",
                "InterStepState.speContainerId not present. H8 (SPE container-type + root container) MUST " +
                "complete before H7 dispatches — this is the source for sprk_SharePointEmbeddedContainerId.",
                cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            var diagnostic =
                "EnvVarValuesOptions:ClientSecret is not populated. H7 authenticates to the target Dataverse " +
                "env via confidential-client credentials against the BFF app-reg (same pattern H6 uses) — the " +
                "MI-Dataverse App User (H10) does not exist yet at H7's point in the DAG. Wave C5 wires this to " +
                "a Key Vault reference; wave C4 requires operator to set the app-setting explicitly. " +
                "Handler did NOT invoke the writer.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                EnvVarValuesRejectionCodes.MissingClientSecret, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (3) Resolve the 3 optional-with-default values per design.md §10.2.
        var bffApiBaseUrl = TryGetNonEmpty(parameters, BffApiBaseUrlParameterKey, out var baseUrlOverride)
            ? baseUrlOverride
            : DefaultBffApiBaseUrl;
        var msalClientId = TryGetNonEmpty(parameters, MsalClientIdParameterKey, out var msalOverride)
            ? msalOverride
            : state.BffAppRegId!; // design.md §10.2: "Typically same as bffApiAppId".
        var shareLinkBaseUrl = TryGetNonEmpty(parameters, ShareLinkBaseUrlParameterKey, out var shareLinkOverride)
            ? shareLinkOverride
            : string.Empty; // design.md §10.2: optional, default empty.

        // (4) Assemble the 7 canonical (schemaName, value) pairs in fixed order.
        var values = new List<KeyValuePair<string, string>>(7)
        {
            new(BffApiBaseUrlSchemaName, bffApiBaseUrl),
            new(BffApiAppIdSchemaName, state.BffAppRegId!),
            new(MsalClientIdSchemaName, msalClientId),
            new(TenantIdSchemaName, tenantId),
            new(AzureOpenAiEndpointSchemaName, state.OpenAiEndpoint!),
            new(ShareLinkBaseUrlSchemaName, shareLinkBaseUrl),
            new(SharePointEmbeddedContainerIdSchemaName, state.SpeContainerId!),
        };

        // (5) Level-3 idempotency: durable no-op on duplicate. A content
        // change to ANY resolved value produces a new configVer + new key +
        // re-write (parity with H6's catalogHash pattern).
        var configVer = ComputeConfigVer(values);
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, configVer);
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H7 idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (6) Invoke the writer. Infrastructure exceptions classified
        // Resumable (except OperationCanceledException which propagates for
        // shutdown) — parity with H5/H6.
        var writeRequest = new EnvVarValuesWriteRequest(
            TargetDataverseUrl: state.DataverseEnvUrl!,
            TenantId: tenantId,
            ClientId: state.BffAppRegId!,
            ClientSecret: _options.ClientSecret!,
            Values: values);

        EnvVarValuesWriteOutcome writeOutcome;
        try
        {
            writeOutcome = await _writer.WriteAsync(writeRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "H7 writer infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"Env-var writer infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Every write is a natural upsert — resuming re-runs H7 safely.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                EnvVarValuesRejectionCodes.WriterInvocationFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        if (writeOutcome is EnvVarValuesWriteOutcome.Failure writeFailure)
        {
            var (rejectionCode, failureClass) = MapWriterFailure(writeFailure.FailureKind);
            var diagnostic = writeFailure.SchemaName is null
                ? writeFailure.Diagnostic
                : $"[{writeFailure.SchemaName}] {writeFailure.Diagnostic}";
            return await FailAsync(run, etag, failureClass, rejectionCode, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        var success = (EnvVarValuesWriteOutcome.Success)writeOutcome;

        // (7) All post-conditions cleared — advance Cosmos state. Reconciler
        // (Wave C5) fans out to H10.
        stopwatch.Stop();
        _logger.LogInformation(
            "H7 Dataverse env-var values succeeded: runId={RunId} customerId={CustomerId} " +
            "varCount={Count} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, success.WrittenVariables.Count, stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(
            run, etag, idempotencyKey, configVer, success.WrittenVariables, envelope, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H7 idempotency key:
    /// <c>envvars-{customerId}-{configVer}</c>. Exposed internal so unit
    /// tests can construct expected keys without duplicating the format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, string configVer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(configVer);
        return $"envvars-{customerId}-{configVer}";
    }

    /// <summary>
    /// Computes a deterministic SHA-256 hex hash over the resolved 7
    /// (schemaName, value) pairs in their FIXED declaration order. A content
    /// change to any resolved value (upstream state advanced or an operator
    /// parameter changed) produces a different hash. Exposed internal for
    /// direct testing. Parity with H6's CatalogHash / H4's secretsVer shape.
    /// </summary>
    internal static string ComputeConfigVer(IReadOnlyList<KeyValuePair<string, string>> values)
    {
        var sb = new StringBuilder();
        foreach (var (schemaName, value) in values)
        {
            sb.Append(schemaName).Append('=').Append(value).Append('|');
        }
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Maps <see cref="EnvVarValuesWriteFailureKind"/> to the pair
    /// (rejectionCode, §4C failureClass). Every kind classifies Resumable
    /// (see file header). Exposed internal so tests can assert the mapping
    /// without depending on handler internals.
    /// </summary>
    internal static (string RejectionCode, FailureClass Class) MapWriterFailure(
        EnvVarValuesWriteFailureKind kind) => kind switch
        {
            EnvVarValuesWriteFailureKind.AuthFailure =>
                (EnvVarValuesRejectionCodes.DataverseAuthFailure, FailureClass.Resumable),
            EnvVarValuesWriteFailureKind.RateLimited =>
                (EnvVarValuesRejectionCodes.RateLimited, FailureClass.Resumable),
            EnvVarValuesWriteFailureKind.DefinitionNotFound =>
                (EnvVarValuesRejectionCodes.EnvVarDefinitionNotFound, FailureClass.Resumable),
            EnvVarValuesWriteFailureKind.UnknownInvocationFailure =>
                (EnvVarValuesRejectionCodes.WriterInvocationFailed, FailureClass.Resumable),
            _ =>
                (EnvVarValuesRejectionCodes.WriterInvocationFailed, FailureClass.Resumable),
        };

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

    private Task<HandlerResult> FailMissingUpstreamAsync(
        ProvisioningRun run,
        string etag,
        string missingKeyName,
        string detail,
        CancellationToken cancellationToken)
    {
        var diagnostic = $"Missing required upstream value '{missingKeyName}'. {detail}";
        return FailAsync(run, etag, FailureClass.Resumable,
            EnvVarValuesRejectionCodes.MissingUpstreamState, diagnostic, cancellationToken);
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

        // Gate entry — one per failure. Verifier is H7; state is Pending
        // (external precondition to resolve). Parity with H5/H6's FailAsync.
        run.GateStates[$"h7-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H7 failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H7 failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        string configVer,
        IReadOnlyList<KeyValuePair<string, string>> writtenVariables,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completedAt = _timeProvider.GetUtcNow();
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier; // Reconciler observes + fans out to H10.
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        run.ErrorDetail = null;

        // Verified-gate entry — one for the env-vars-set post-condition.
        // Evidence captures the schema names actually written + configVer so
        // an operator sees the manifest without pulling logs. None of these
        // 7 values are secret-shaped (they are public config surface the
        // client reads via environmentvariablevalue query per task 024).
        var evidencePayload = new
        {
            configVer,
            schemaNames = writtenVariables.Select(kv => kv.Key).ToArray(),
        };
        var evidence = JsonDocument.Parse(JsonSerializer.Serialize(evidencePayload)).RootElement.Clone();
        run.GateStates[EnvVarsSetGateId] = new GateEntry
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
                "H7 success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: EnvVarValuesRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H7 read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H7 " +
                             "which will short-circuit on idempotency.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H7 success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: EnvVarValuesRejectionCodes.RunDeletedDuringWrite,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H7 was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
