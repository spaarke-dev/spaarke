// -----------------------------------------------------------------------------
// H4SharedKvSecretsPopulationHandler.cs
//
// Task 200 — L2 CONTROL-PLANE H4-shared KV secrets-population handler.
// Sibling of H4 (task 047 H4KvSecretsPopulationHandler.cs — canonical
// reference). Targets the SHARED-tier Key Vault (Model 1 shared trial +
// future Model 2 shared services) and populates it by EXTRACTING credentials
// from source Azure services via the Azure.ResourceManager.* SDK
// (SdkSourceServiceKeyExtractor — 5 recipes per SourceServiceType enum).
//
// PURPOSE (F19 automation):
//   Per lessons-learned-model1-prod-standup-2026-08-22.md finding F19:
//   Bicep provisions the shared KV `sprk-{env}-kv` but leaves it EMPTY. All
//   6 BFF `@Microsoft.KeyVault(...)` refs to the shared secrets fail to
//   resolve → BFF fails-fast at boot (F20 chain). SESSION 2 manually seeded
//   6 secrets via ad-hoc az CLI; this handler codifies that recipe as an
//   IProvisioningHandler using the SDK (aligned with task 125's Option D
//   hybrid — zero `az` shell-out).
//
// FLOW:
//   (1) Load ProvisioningRun (Cosmos partition-key-scoped read).
//   (2) Parameter guards (tenantId, subscriptionId, sharedKeyVaultName,
//       sourceResourceGroupName, environmentName, secretsVer,
//       resourceGroupName, appServiceName, uamiResourceId).
//   (3) Idempotency Level-3: kv-shared-{environmentName}-{secretsVer}.
//   (4) Read manifest via IKvSecretManifest.
//   (5) Filter to FromSharedService entries (per-tenant entries belong to H4).
//   (6) BINDING pre-check on the FROM-SHARED-SERVICE subset — refuse if
//       Dataverse-ClientSecret / BFF-API-ClientSecret appear (defensive; the
//       manifest generator should never emit these as shared-service, but
//       H4-shared enforces the guard write-side per spec.md MUST rule).
//   (7) For each from-shared-service entry:
//         a. Parse service_ref → SharedKvSecretSource (type + resource name).
//         b. Extract fresh source value via ISourceServiceKeyExtractor.
//         c. Read current shared-KV value.
//         d. NO-OP on match; WriteAsync + audit log (hash comparison only,
//            never the values) on drift or absence.
//   (8) Post-condition: IArmKeyVaultRefProbe verifies the App Service's
//       keyVaultReferenceIdentity == UAMI (parity with H4's post-condition;
//       shared KV refs likewise resolve via UAMI).
//   (9) MarkComplete — write CompletedPhase(H4-shared, idempotencyKey) to
//       Cosmos with optimistic-concurrency.
//
// CLEARTEXT NO-LOG (ADR-028 MUST rule):
//   Extracted values pass through this handler as local variables only, sent
//   directly to ISharedKvSecretAccessor.WriteAsync. NO Log* call ever
//   includes the value; audit-log lines carry SHA-256 hashes of old + new
//   values so operators can detect rotation events without the values
//   themselves being persisted anywhere (Cosmos, App Insights, stdout).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10 + §11):
//   H4-shared lives in L2 (not BFF) per spec §5.2 / D3 / D8 / D12; consumes
//   NO AI-internal types (ADR-013 forcing-function). Existing components
//   cannot cover this: H4-per-tenant targets a DIFFERENT KV scope + does NOT
//   have source-extraction plumbing (would require conditional branching on
//   every step — see POML <justification> extension analysis). This handler
//   is a NEW surface: minimal, purpose-built, single responsibility.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H4SharedKvSecretsPopulationHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches HandlerIds.H4Shared.</summary>
    public const string HandlerIdentifier = HandlerIds.H4Shared;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the target subscription id (source services + shared KV live here).</summary>
    public const string SubscriptionIdParameterKey = "subscriptionId";

    /// <summary>Non-secret parameter key carrying the target SHARED Key Vault name (e.g. <c>sprk-prod-kv</c>).</summary>
    public const string SharedKeyVaultNameParameterKey = "sharedKeyVaultName";

    /// <summary>Non-secret parameter key carrying the resource group hosting the SHARED source services (Search, OpenAI, DocIntel, ServiceBus, Storage, Redis).</summary>
    public const string SourceResourceGroupNameParameterKey = "sourceResourceGroupName";

    /// <summary>Non-secret parameter key carrying the environment name (dev/prod) — feeds idempotency key.</summary>
    public const string EnvironmentNameParameterKey = "environmentName";

    /// <summary>Non-secret parameter key carrying the manifest content hash / semantic version — feeds idempotency key.</summary>
    public const string SecretsVersionParameterKey = "secretsVer";

    /// <summary>Non-secret parameter key carrying the resource group hosting the BFF App Service (T1 probe scope).</summary>
    public const string AppServiceResourceGroupNameParameterKey = "resourceGroupName";

    /// <summary>Non-secret parameter key carrying the BFF App Service name (T1 probe scope).</summary>
    public const string AppServiceNameParameterKey = "appServiceName";

    /// <summary>Non-secret parameter key carrying the App Service staging slot name (defaults to <c>staging</c>).</summary>
    public const string StagingSlotNameParameterKey = "stagingSlotName";

    /// <summary>Non-secret parameter key carrying the UAMI resource id H4-shared expects as keyVaultReferenceIdentity (T1 probe).</summary>
    public const string UamiResourceIdParameterKey = "userAssignedIdentityResourceId";

    /// <summary>
    /// Row A38a (task 205a, 2026-08-25) — mirror of
    /// <see cref="H4KvSecretsPopulationHandler.FicOmitSecretNamesParameterKey"/>
    /// (task 126 FR-39 seam) for the from-shared-service flow: comma-separated
    /// canonical names H4-shared MUST omit entirely (no parse / extract /
    /// read / write). SAME parameter name so one coordinated run-parameter
    /// value drives both handlers uniformly. Absent OR empty = no omissions.
    /// This key is DATA, not a hardcoded canonical-name check — parity with
    /// task 125's FR-39 "no special-casing" commitment.
    /// </summary>
    public const string FicOmitSecretNamesParameterKey = "ficOmitSecretNames";

    /// <summary>Default staging slot name.</summary>
    private const string DefaultStagingSlotName = "staging";

    /// <summary>Canonical secret names H4-shared MUST NEVER write via shared-service extraction (BINDING pre-check per spec.md MUST rule).</summary>
    public static readonly IReadOnlySet<string> BindingNeverDeleteSecrets = new HashSet<string>(StringComparer.Ordinal)
    {
        "Dataverse-ClientSecret",
        "BFF-API-ClientSecret",
    };

    private readonly IProvisioningRunRepository _repository;
    private readonly IKvSecretManifest _manifest;
    private readonly ISharedKvSecretAccessor _accessor;
    private readonly IArmKeyVaultRefProbe _t1Probe;
    private readonly ISourceServiceKeyExtractor _extractor;
    private readonly ISecretFreeMarkerApplier _markerApplier;
    private readonly KvSecretsPopulationOptions _options;
    private readonly ILogger<H4SharedKvSecretsPopulationHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H4-shared KV secrets-population handler. All collaborators
    /// are interface-abstracted so unit tests substitute stubs for each seam.
    /// Reuses <see cref="KvSecretsPopulationOptions"/> from H4 (no divergent
    /// knobs required for H4-shared today — deviates cleanly if that changes).
    /// </summary>
    public H4SharedKvSecretsPopulationHandler(
        IProvisioningRunRepository repository,
        IKvSecretManifest manifest,
        ISharedKvSecretAccessor accessor,
        IArmKeyVaultRefProbe t1Probe,
        ISourceServiceKeyExtractor extractor,
        ISecretFreeMarkerApplier markerApplier,
        IOptions<KvSecretsPopulationOptions> options,
        ILogger<H4SharedKvSecretsPopulationHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(t1Probe);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(markerApplier);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _manifest = manifest;
        _accessor = accessor;
        _t1Probe = t1Probe;
        _extractor = extractor;
        _markerApplier = markerApplier;
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
                $"H4SharedKvSecretsPopulationHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H4-shared KV secrets population starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun.
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H4-shared aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SharedKvSecretsPopulationRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;

        // (2) Parameter guards — every field H4-shared needs must be non-empty
        //     BEFORE any external side effect.
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SharedKvSecretsPopulationRejectionCodes.MissingTenantId,
                "Run parameter 'tenantId' is required by H4-shared (§4D I1 no-hardcoded-tenant).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, SubscriptionIdParameterKey, out var subscriptionId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SharedKvSecretsPopulationRejectionCodes.MissingSubscriptionId,
                "Run parameter 'subscriptionId' is required by H4-shared (target sub for source services + shared KV).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, SharedKeyVaultNameParameterKey, out var sharedKeyVaultName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SharedKvSecretsPopulationRejectionCodes.MissingSharedKeyVaultName,
                "Run parameter 'sharedKeyVaultName' is required by H4-shared (target vault for all shared writes).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, SourceResourceGroupNameParameterKey, out var sourceResourceGroupName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SharedKvSecretsPopulationRejectionCodes.MissingSourceResourceGroupName,
                "Run parameter 'sourceResourceGroupName' is required by H4-shared (RG hosting shared source services).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, EnvironmentNameParameterKey, out var environmentName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SharedKvSecretsPopulationRejectionCodes.MissingEnvironmentName,
                "Run parameter 'environmentName' is required by H4-shared (feeds idempotency key kv-shared-{env}-{secretsVer}).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, SecretsVersionParameterKey, out var secretsVer))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SharedKvSecretsPopulationRejectionCodes.MissingSecretsVersion,
                "Run parameter 'secretsVer' is required by H4-shared (manifest content hash — feeds idempotency key).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, AppServiceResourceGroupNameParameterKey, out var appServiceResourceGroupName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SharedKvSecretsPopulationRejectionCodes.MissingSourceResourceGroupName,
                "Run parameter 'resourceGroupName' is required by H4-shared (T1 App Service probe scope).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, AppServiceNameParameterKey, out var appServiceName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SharedKvSecretsPopulationRejectionCodes.MissingSourceResourceGroupName,
                "Run parameter 'appServiceName' is required by H4-shared (T1 probe target).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, UamiResourceIdParameterKey, out var uamiResourceId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SharedKvSecretsPopulationRejectionCodes.MissingSourceResourceGroupName,
                "Run parameter 'userAssignedIdentityResourceId' is required by H4-shared (T1 probe expected identity).",
                cancellationToken).ConfigureAwait(false);
        }

        var stagingSlotName = TryGetNonEmpty(parameters, StagingSlotNameParameterKey, out var slot)
            ? slot
            : DefaultStagingSlotName;

        // Row A38a (task 205a, 2026-08-25) — FR-39 omit seam, mirrored from
        // H4KvSecretsPopulationHandler (task 126): operator run-parameter set
        // + (on secret-free envs) the three A38a targets. from-shared-service
        // entries in the omit set are skipped ENTIRELY (no parse / extract /
        // read / write) — SB-conn + AiSearch admin-key travel through THIS
        // handler per manifest.yaml (:255/:433), so omitting them only from
        // per-tenant H4 would leave them re-seeded here (the exact
        // h4-shared-omit-path-missing failure the A38a re-scope names).
        // Defense-in-depth behind FileKvSecretManifest's served-entry filter.
        var omitCanonicalNames = TryGetNonEmpty(parameters, FicOmitSecretNamesParameterKey, out var ficOmitRaw)
            ? new HashSet<string>(
                ficOmitRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var secretFreeOmitActive = _options.RequireSecretFreeIdentity && !_options.SecretFreeIdentityRollback;
        if (secretFreeOmitActive)
        {
            omitCanonicalNames.UnionWith(FileKvSecretManifest.SecretFreeIdentityOmitTargets);
            _logger.LogInformation(
                "H4-shared A38a secret-free omit active: runId={RunId} — unioned {Targets} into the " +
                "FR-39 omit set (size now {Size})",
                envelope.RunId,
                string.Join(", ", FileKvSecretManifest.SecretFreeIdentityOmitTargets), omitCanonicalNames.Count);
        }

        var idempotencyKey = BuildIdempotencyKey(environmentName, secretsVer);

        // (3) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H4-shared idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (4) Read the manifest.
        KvSecretManifestReadResult manifestResult;
        try
        {
            manifestResult = await _manifest.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H4-shared manifest read infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                SharedKvSecretsPopulationRejectionCodes.ManifestReadFailed,
                $"Canonical secret-catalog manifest read failed: {ex.GetType().Name}: {ex.Message}. " +
                "Verify the manifest source is reachable + the L2 UAMI has read access.",
                cancellationToken).ConfigureAwait(false);
        }

        if (manifestResult is KvSecretManifestReadResult.Failure manifestFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                SharedKvSecretsPopulationRejectionCodes.ManifestReadFailed,
                $"Manifest reader reported failure: {manifestFailure.Diagnostic}",
                cancellationToken).ConfigureAwait(false);
        }

        var allEntries = ((KvSecretManifestReadResult.Success)manifestResult).Entries;

        // (5) Filter to from-shared-service entries only. Manifest-v1
        //     backwards compat: absent shared entries → 0 work, still Success.
        var sharedEntries = allEntries
            .Where(e => e.ValueSource == KvSecretValueSource.FromSharedService)
            .ToList();

        _logger.LogInformation(
            "H4-shared manifest loaded: runId={RunId} totalEntries={Total} sharedEntries={Shared}",
            envelope.RunId, allEntries.Count, sharedEntries.Count);

        // (6) BINDING pre-check — MUST fire BEFORE any external side effect.
        var forbidden = sharedEntries.FirstOrDefault(e =>
            BindingNeverDeleteSecrets.Contains(e.CanonicalName));
        if (forbidden is not null)
        {
            var diagnostic =
                $"BINDING pre-check violation: manifest lists never-delete canonical secret " +
                $"'{forbidden.CanonicalName}' with value_source=from-shared-service. spec.md MUST " +
                "rule + r3 handoff forbid H4-shared from writing these via source-extraction — they " +
                "MUST remain from-existing-kv managed. QuarantineRequired.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SharedKvSecretsPopulationRejectionCodes.BindingPreCheckViolation, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (7) Per-entry pipeline: parse service_ref → extract → read-back →
        //     compare → NO-OP or Write.
        var perEntryResults = new List<SharedEntryResult>(sharedEntries.Count);
        foreach (var entry in sharedEntries)
        {
            // Row A38a FR-39 omit — checked FIRST: an omitted entry gets NO
            // external call of any kind (no service_ref parse, no extraction,
            // no KV read, no KV write). §9.1 OMIT-is-the-signal — the entry
            // is intentionally absent, never sentinel-valued.
            if (omitCanonicalNames.Contains(entry.CanonicalName))
            {
                _logger.LogInformation(
                    "H4-shared omitted (FR-39) '{Canonical}' on '{Vault}' — omit set contains it " +
                    "(secretFree={SecretFree}); no extraction or write performed",
                    entry.CanonicalName, sharedKeyVaultName, secretFreeOmitActive);
                perEntryResults.Add(new SharedEntryResult(entry.CanonicalName, SharedEntryAction.Omitted, null));
                continue;
            }

            if (!SharedKvSecretSource.TryParse(entry.ServiceRef, out var source))
            {
                var diag =
                    $"Manifest entry '{entry.CanonicalName}' has malformed service_ref " +
                    $"'{entry.ServiceRef ?? "(null)"}' (expected '<type>:<az-resource-name>' with type ∈ " +
                    "{search, cognitiveservices, servicebus, storage, redis}).";
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    SharedKvSecretsPopulationRejectionCodes.InvalidServiceRef, diag, cancellationToken)
                    .ConfigureAwait(false);
            }

            string extractedValue;
            try
            {
                extractedValue = await _extractor
                    .ExtractAsync(source, subscriptionId, sourceResourceGroupName, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Azure.RequestFailedException ex)
            {
                var diag =
                    $"Source-service extraction failed for canonical '{entry.CanonicalName}' " +
                    $"(service_ref='{entry.ServiceRef}', HTTP {ex.Status} {ex.ErrorCode}). " +
                    "Root cause is typically L2-UAMI missing an RBAC role on the source service " +
                    "(Cognitive Services User / Search Service Contributor / Azure Service Bus Data " +
                    "Owner / Storage Account Contributor / Redis Cache Contributor) — Bicep hardening " +
                    "follow-on required. QuarantineRequired.";
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    SharedKvSecretsPopulationRejectionCodes.SourceServiceExtractionFailed, diag, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var diag =
                    $"Source-service extraction infrastructure fault for '{entry.CanonicalName}' " +
                    $"(service_ref='{entry.ServiceRef}'): {ex.GetType().Name}: {ex.Message}.";
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    SharedKvSecretsPopulationRejectionCodes.SourceServiceExtractionFailed, diag, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Read-back current KV value for drift detection. NotFound is
            // expected on first run; treat as "must write". Failure is a
            // hard stop (QuarantineRequired — RBAC / connectivity).
            var readResult = await _accessor.ReadAsync(sharedKeyVaultName, entry.CanonicalName, cancellationToken)
                .ConfigureAwait(false);
            switch (readResult)
            {
                case SharedKvSecretReadResult.Failure readFail:
                    var readDiag =
                        $"Shared-KV read failed for '{entry.CanonicalName}' on vault '{sharedKeyVaultName}': " +
                        $"{readFail.Diagnostic}";
                    return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                        SharedKvSecretsPopulationRejectionCodes.SharedKvReadFailed, readDiag, cancellationToken)
                        .ConfigureAwait(false);

                case SharedKvSecretReadResult.NotFound:
                    // Fresh write required.
                    var freshWrite = await _accessor.WriteAsync(sharedKeyVaultName, entry.CanonicalName, extractedValue, cancellationToken)
                        .ConfigureAwait(false);
                    if (freshWrite is SharedKvSecretWriteResult.Failure fwFail)
                    {
                        perEntryResults.Add(new SharedEntryResult(entry.CanonicalName, SharedEntryAction.Failed, fwFail.Diagnostic));
                    }
                    else
                    {
                        _logger.LogInformation(
                            "H4-shared wrote (initial) '{Canonical}' to '{Vault}' newHash={NewHash}",
                            entry.CanonicalName, sharedKeyVaultName, HashForAudit(extractedValue));
                        perEntryResults.Add(new SharedEntryResult(entry.CanonicalName, SharedEntryAction.WroteInitial, null));
                    }
                    break;

                case SharedKvSecretReadResult.Success existing:
                    if (string.Equals(existing.Value, extractedValue, StringComparison.Ordinal))
                    {
                        _logger.LogDebug(
                            "H4-shared no-drift NO-OP for '{Canonical}' on '{Vault}'",
                            entry.CanonicalName, sharedKeyVaultName);
                        perEntryResults.Add(new SharedEntryResult(entry.CanonicalName, SharedEntryAction.NoOpMatched, null));
                    }
                    else
                    {
                        // Drift detected — rotate + audit-log old + new hashes.
                        var rotateWrite = await _accessor.WriteAsync(sharedKeyVaultName, entry.CanonicalName, extractedValue, cancellationToken)
                            .ConfigureAwait(false);
                        if (rotateWrite is SharedKvSecretWriteResult.Failure rwFail)
                        {
                            perEntryResults.Add(new SharedEntryResult(entry.CanonicalName, SharedEntryAction.Failed, rwFail.Diagnostic));
                        }
                        else
                        {
                            _logger.LogInformation(
                                "H4-shared drift-rotated '{Canonical}' on '{Vault}' oldHash={OldHash} newHash={NewHash}",
                                entry.CanonicalName, sharedKeyVaultName,
                                HashForAudit(existing.Value), HashForAudit(extractedValue));
                            perEntryResults.Add(new SharedEntryResult(entry.CanonicalName, SharedEntryAction.RotatedOnDrift, null));
                        }
                    }
                    break;
            }
        }

        // Any per-entry Failed → QuarantineRequired (partial vault state).
        var failed = perEntryResults.Where(r => r.Action == SharedEntryAction.Failed).ToList();
        if (failed.Count > 0)
        {
            var diagnostic =
                $"H4-shared KV writes partially failed on vault '{sharedKeyVaultName}': " +
                $"{failed.Count} of {perEntryResults.Count} entries failed. " +
                $"Failed: {string.Join(", ", failed.Select(r => $"{r.CanonicalName}={r.Diagnostic}"))}. " +
                "Partial vault state — QuarantineRequired.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SharedKvSecretsPopulationRejectionCodes.SharedKvWritePartialFailure, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (8) T1 post-condition — verify keyVaultReferenceIdentity == UAMI
        //     on both slots. Parity with H4's post-condition.
        ArmKeyVaultRefProbeResult t1Result;
        try
        {
            t1Result = await _t1Probe.VerifyKeyVaultReferenceIdentityAsync(
                new ArmKeyVaultRefProbeInput(
                    SubscriptionId: subscriptionId,
                    ResourceGroupName: appServiceResourceGroupName,
                    AppServiceName: appServiceName,
                    StagingSlotName: stagingSlotName,
                    ExpectedUserAssignedIdentityResourceId: uamiResourceId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H4-shared T1 probe infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SharedKvSecretsPopulationRejectionCodes.SharedSecretRefUnresolvable,
                $"T1 verification probe infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "H4-shared writes succeeded but post-condition could not be verified.",
                cancellationToken).ConfigureAwait(false);
        }

        if (t1Result is ArmKeyVaultRefProbeResult.Mismatch t1Mismatch)
        {
            var diagnostic =
                $"H4-shared post-condition FAILED for App Service '{appServiceName}': " +
                $"expected keyVaultReferenceIdentity == '{uamiResourceId}' on both slots; " +
                $"observed prod='{t1Mismatch.ObservedProductionSlotIdentity ?? "(null)"}', " +
                $"staging='{t1Mismatch.ObservedStagingSlotIdentity ?? "(null)"}'. " +
                "Shared-KV refs will NOT resolve for BFF — QuarantineRequired (F16 remediation gap or " +
                "operator-RBAC misconfig).";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SharedKvSecretsPopulationRejectionCodes.SharedSecretRefUnresolvable, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (8.5) Row A38a positive migration marker — parity with H4's step
        //       (10.5): KV resource tag on the SHARED vault + registry
        //       sprk_credentialmode. Applied once per vault; H4-shared owns
        //       exactly one vault (the per-environment shared KV) per run —
        //       per-customer Model 2 vaults are tagged by their own H4 runs
        //       (dispatch fan-out). Idempotent; Failure is Resumable +
        //       FAIL-LOUD (§5.3). Not applied under Q3 Path A rollback.
        //       NOTE: the shared KV is assumed to live in
        //       sourceResourceGroupName (the shared-services RG deployed by
        //       model1-shared.bicep alongside the source services); a wrong
        //       RG surfaces as a loud ARM 404 Failure, never a silent skip.
        if (secretFreeOmitActive)
        {
            SecretFreeMarkerApplyOutcome markerOutcome;
            try
            {
                markerOutcome = await _markerApplier.ApplyAsync(
                    new SecretFreeMarkerApplyRequest(
                        SubscriptionId: subscriptionId,
                        ResourceGroupName: sourceResourceGroupName,
                        KeyVaultName: sharedKeyVaultName,
                        TenantId: tenantId,
                        CustomerIdForLog: envelope.CustomerId,
                        RunIdForLog: envelope.RunId),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "H4-shared A38a marker applier infrastructure fault: runId={RunId} customerId={CustomerId}",
                    envelope.RunId, envelope.CustomerId);
                return await FailAsync(run, etag, FailureClass.Resumable,
                    SharedKvSecretsPopulationRejectionCodes.SecretFreeMarkerApplyFailed,
                    $"A38a marker applier infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                    "Shared-KV writes/omits succeeded; marker application is idempotent — fix cause + resume.",
                    cancellationToken).ConfigureAwait(false);
            }

            if (markerOutcome is SecretFreeMarkerApplyOutcome.Failure markerFailure)
            {
                return await FailAsync(run, etag, FailureClass.Resumable,
                    SharedKvSecretsPopulationRejectionCodes.SecretFreeMarkerApplyFailed,
                    markerFailure.Diagnostic, cancellationToken).ConfigureAwait(false);
            }
        }

        // (9) All post-conditions cleared — advance Cosmos state.
        stopwatch.Stop();
        var wroteInitial = perEntryResults.Count(r => r.Action == SharedEntryAction.WroteInitial);
        var rotated = perEntryResults.Count(r => r.Action == SharedEntryAction.RotatedOnDrift);
        var noOp = perEntryResults.Count(r => r.Action == SharedEntryAction.NoOpMatched);
        var omitted = perEntryResults.Count(r => r.Action == SharedEntryAction.Omitted);
        _logger.LogInformation(
            "H4-shared KV secrets population succeeded: runId={RunId} customerId={CustomerId} " +
            "wrote-initial={Wrote} rotated={Rotated} no-op={NoOp} omitted-fr39={Omitted} " +
            "secretFree={SecretFree} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, wroteInitial, rotated, noOp, omitted,
            secretFreeOmitActive, stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(run, etag, idempotencyKey, envelope, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H4-shared idempotency key:
    /// <c>kv-shared-{environmentName}-{secretsVer}</c>. Env-scoped (not
    /// customer-scoped) because the shared KV is per-environment, not
    /// per-customer.
    /// </summary>
    internal static string BuildIdempotencyKey(string environmentName, string secretsVer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretsVer);
        return $"kv-shared-{environmentName}-{secretsVer}";
    }

    /// <summary>
    /// SHA-256 audit hash of a secret value — 8-char lower-hex prefix, enough
    /// entropy to distinguish rotation events in operator logs but NOT enough
    /// to reverse the secret. Values themselves NEVER traverse Log*.
    /// </summary>
    internal static string HashForAudit(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes)[..8];
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

        run.GateStates[$"h4shared-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H4-shared failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H4-shared failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
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
        run.CurrentPhase = HandlerIdentifier;
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
                "H4-shared success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SharedKvSecretsPopulationRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H4-shared read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H4-shared.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H4-shared success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SharedKvSecretsPopulationRejectionCodes.RunDeletedDuringPopulation,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H4-shared was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }

    /// <summary>Per-entry pipeline outcome — internal to the handler, never serialized.</summary>
    private sealed record SharedEntryResult(string CanonicalName, SharedEntryAction Action, string? Diagnostic);

    private enum SharedEntryAction
    {
        WroteInitial = 1,
        RotatedOnDrift = 2,
        NoOpMatched = 3,
        Failed = 4,

        /// <summary>
        /// Row A38a (FR-39 parity with <see cref="KvSecretWriteAction.Omitted"/>):
        /// entry intentionally NOT processed — no extraction, no KV read/write.
        /// </summary>
        Omitted = 5,
    }
}
