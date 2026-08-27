// -----------------------------------------------------------------------------
// H4KvSecretsPopulationHandler.cs
//
// L2 CONTROL-PLANE H4 KV secrets-population handler (task 047, wave C4 Batch 3D).
//
// PURPOSE:
//   Populates a customer's Key Vault with the canonical secret set from the
//   Phase H canonical secret-catalog manifest (spec.md FR-36 / task 084) via
//   <see cref="IKvSecretsWriter"/>, then PATCHes App Service
//   <c>keyVaultReferenceIdentity</c> to the UAMI resource id on BOTH prod +
//   staging slots (T1 silent-fail trap owner — spec.md FR-33), and interim
//   grants KV Secrets User on the target KV to both slot System-Assigned MI
//   principals when they exist (T5 interim mitigation — spec.md FR-33 T5).
//
//   Post-Phase-C UAMI (task 028 uami.bicep + task 029 app-service.bicep +
//   task 030 RBAC migration), T5 is STRUCTURALLY IMPOSSIBLE — the granter
//   returns NoSlotSystemAssignedIdentity which this handler treats as
//   SUCCESS. Both traps are cleared at H4 as the source of truth (H2a
//   task 044 also verifies T1 but only H4 explicitly PATCHes it).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-07 (H4):
//       KV secrets populated per canonical §7.9 names; keyVaultReferenceIdentity
//       PATCHed to UAMI on both slots; interim T5 grants KV RBAC to both
//       slot System-Assigned MI principals.
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-33 (T1 + T5):
//       H4 owns both silent-fail traps.
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-34 (upgrade
//       mode H4): rotation-safe by default; explicit rotate=true (H4-rotate
//       variant) required to overwrite live secrets.
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-35 (Phase G
//       canonical naming): applied via <see cref="IKvSecretManifest"/> entries.
//   - projects/customer-provisioning-orchestration-r1/spec.md § MUST rules:
//       BINDING pre-check — NEVER delete Dataverse-ClientSecret or
//       BFF-API-ClientSecret (r3 handoff; OBO + shared-lib Dataverse depend).
//   - projects/customer-provisioning-orchestration-r1/spec.md §4D I1: -TenantId
//       flows through explicitly; no default tenant fallback.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H4 row +
//       §4B T1/T5 + §4C rollback + §7.9 canonical naming.
//   - .claude/adr/ADR-004: single IJobHandler impl registered in L2 DI.
//   - .claude/adr/ADR-010: register in L2, NOT BFF.
//   - .claude/adr/ADR-028: auth ceremony (21 MUSTs) — keyVaultReferenceIdentity
//       PATCH to UAMI is one; cleartext secrets NEVER traverse handler code
//       (only through <see cref="IKvSecretsWriter"/>'s process boundary).
//   - .claude/adr/ADR-036: reuse background-job infrastructure; fire-and-forget.
//   - .claude/adr/ADR-044: interStepState.MiObjectId + MiClientId are UAMI-
//       scoped IDs (H2a wrote them at task 044).
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌───────────────────────────────────────────┬──────────────────────────┐
//   │ Failure mode                              │ §4C class                │
//   ├───────────────────────────────────────────┼──────────────────────────┤
//   │ Missing tenantId / subscriptionId /       │ Resumable                │
//   │ keyVaultName / secretsVer /               │ (external precondition — │
//   │ resourceGroupName / appServiceName /      │ operator fixes params +  │
//   │ uamiResourceId (§4D I1 + structural)      │ resumes)                 │
//   │ Run not found in Cosmos partition         │ Resumable                │
//   │ Manifest reader failure                   │ Resumable                │
//   │ BINDING pre-check violation (manifest     │ QuarantineRequired       │
//   │ contains Delete for Dataverse-ClientSecret│ (would break OBO / shared│
//   │ or BFF-API-ClientSecret)                  │ Dataverse fleet-wide)    │
//   │ Cleartext-secret leak on interStepState   │ QuarantineRequired       │
//   │ KV write ALL failures (no partial state)  │ Resumable                │
//   │ KV write PARTIAL failure                  │ QuarantineRequired       │
//   │ T1 PATCH shell-out failure                │ QuarantineRequired       │
//   │ T1 ARM verify mismatch after PATCH        │ QuarantineRequired       │
//   │ T5 grant failure (System-Assigned MI      │ Resumable                │
//   │ present but grant broke)                  │ (T5 is INTERIM; operator │
//   │                                           │ retry / wait for UAMI)   │
//   │ T5 NoSlotSystemAssignedIdentity           │ SUCCESS (post-Phase-C    │
//   │ (post-Phase-C UAMI structural steady)     │ steady state)            │
//   │ Concurrent Cosmos writer conflict         │ Resumable                │
//   │ Run row deleted mid-flight                │ Resumable                │
//   └───────────────────────────────────────────┴──────────────────────────┘
//
// IDEMPOTENCY (3-level per ADR-004 / design.md §4.1):
//   Level 1 (Service Bus MessageId dedup): the reconciler computes deterministic
//           MessageId per (HandlerId, RunId, CustomerId, paramHash); SB
//           duplicate-detection collapses re-enqueues.
//   Level 2 (Redis IdempotencyService): NOT YET IMPLEMENTED in L2 (design.md
//           §4.1 preamble; parity with H0 / H0.5 / H1 / H2a / H3).
//   Level 3 (handler body durable dedup): this handler scans
//           ProvisioningRun.CompletedPhases for (Phase=="H4",
//           IdempotencyKey==kv-{customerId}-{secretsVer}). Match ⇒ Success
//           no-op. secretsVer is the manifest hash from Phase H (a content
//           hash — a content change to the manifest yields a new key + a
//           new invocation).
//
// DOWNSTREAM ENQUEUE (Wave C4 note):
//   H4 does NOT enqueue a specific successor. Parity with H2a/H2b/H3: the
//   downstream DAG per design.md §4.1 branches from H4 to H7 (env-var
//   population) — the wave-C5 reconciler owns fan-out. H4 mutates Cosmos
//   state (CurrentPhase + CompletedPhases) and returns Success.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   H4 lives in L2 (not BFF) per spec §5.2 / D3 / D8 / D12; consumes NO
//   AI-internal types (ADR-013 forcing-function rule — no IActionResolver,
//   IActionRunner, IOpenAiClient, IPlaybookService injection). H4 uses
//   IProvisioningRunRepository (task 037) + the four dedicated seams
//   (IKvSecretManifest, IKvSecretsWriter, IAppServiceIdentityPatcher,
//   ISlotIdentityRoleGranter) + reuses IArmKeyVaultRefProbe (task 044) for
//   T1 post-condition verification; no BFF-facade dependencies.
//
// ADR TENSION CITATIONS (per CLAUDE.md §6.5) for the PR description:
//   - ADR-028 (Path C — comply): all KV writes flow through az CLI's operator
//     auth chain (DefaultAzureCredential via `az login`); T1 PATCH sets
//     keyVaultReferenceIdentity to UAMI; cleartext secrets never touch
//     handler code (only pass through IKvSecretsWriter's process boundary).
//   - ADR-004 (Path C — comply): 3-level idempotency; kv-{customerId}-{secretsVer}
//     is the Level-3 durable key. Content change to manifest = new secretsVer =
//     new key = re-seed. Rotation-safe upgrade is the DEFAULT (never overwrites
//     live secrets absent explicit rotate=true) — this is the spec.md FR-34 H4
//     row behavior.
//   - ADR-010 (Path C — comply): registered in L2 Program.cs (not BFF); each
//     collaborator is a Singleton with a clear seam justification (≥2 impls
//     from day 1 per ADR-010 rule).
//   - ADR-032 (Path C — comply): all four seams UNCONDITIONALLY registered;
//     no feature-gate branch in Program.cs. The T5 granter returns
//     NoSlotSystemAssignedIdentity as a domain outcome (not a null-object
//     kill-switch) — a legitimate SUCCESS-equivalent post-Phase-C.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H4KvSecretsPopulationHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H4;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the target subscription id (ADR-027 D4).</summary>
    public const string SubscriptionIdParameterKey = "subscriptionId";

    /// <summary>Non-secret parameter key carrying the target Key Vault name (canonical §7.9 R3 sprk-{env}-kv).</summary>
    public const string KeyVaultNameParameterKey = "keyVaultName";

    /// <summary>Non-secret parameter key carrying the resource group name (T1 PATCH + T5 slot MI reads).</summary>
    public const string ResourceGroupNameParameterKey = "resourceGroupName";

    /// <summary>Non-secret parameter key carrying the App Service name (T1 PATCH + T5 slot MI reads).</summary>
    public const string AppServiceNameParameterKey = "appServiceName";

    /// <summary>Non-secret parameter key carrying the App Service staging slot name (defaults to <c>staging</c>).</summary>
    public const string StagingSlotNameParameterKey = "stagingSlotName";

    /// <summary>Non-secret parameter key carrying the UAMI resource id H4 patches into keyVaultReferenceIdentity (T1). Written by H2a to interStepState.MiObjectId is the OBJECT id; H4 needs the full RID which is passed as a param.</summary>
    public const string UamiResourceIdParameterKey = "userAssignedIdentityResourceId";

    /// <summary>Non-secret parameter key carrying the full KV resource id (T5 grant scope). May be derived from keyVaultName + subscription + rg when absent.</summary>
    public const string KeyVaultResourceIdParameterKey = "keyVaultResourceId";

    /// <summary>Non-secret parameter key carrying the manifest content hash / semantic version — feeds idempotency key kv-{customerId}-{secretsVer}.</summary>
    public const string SecretsVersionParameterKey = "secretsVer";

    /// <summary>Non-secret parameter key toggling rotation mode (H4-rotate variant per spec.md FR-34). Absent OR "false" = rotation-safe (default).</summary>
    public const string RotateExistingParameterKey = "rotate";

    /// <summary>
    /// Non-secret parameter key (task 126, spec.md FR-39 auth-v4 FIC
    /// pluggability) carrying a comma-separated list of manifest canonical
    /// names the writer MUST omit entirely — a coordinated run parameter
    /// indicates the corresponding OBO credential has already migrated to
    /// FIC. Absent OR empty = no omissions (default; matches today's live
    /// state — auth-v4 Phase 5 secret retirement has NOT landed). This key
    /// is DATA, not a hardcoded canonical-name check — parity with task
    /// 125's FR-39 "no special-casing" commitment.
    /// </summary>
    public const string FicOmitSecretNamesParameterKey = "ficOmitSecretNames";

    /// <summary>
    /// Non-secret parameter key carrying an ISO-8601 timestamp for
    /// <c>sprk_dataverseenvironment.sprk_provisionedon</c>. When present +
    /// non-empty, H4 runs in upgrade mode (spec.md FR-34 rotation-safe
    /// default). Parity with H2a's <c>ProvisionedOnParameterKey</c>.
    /// </summary>
    public const string ProvisionedOnParameterKey = "provisionedOn";

    /// <summary>Default staging slot name when the parameter is absent (parity with app-service.bicep task 029).</summary>
    private const string DefaultStagingSlotName = "staging";

    /// <summary>
    /// Cleartext-secret pattern heuristic. NOT cryptographic — a forcing-
    /// function to catch obvious regressions in interStepState writes (e.g.
    /// someone accidentally writes a raw client-secret literal). Values
    /// starting with <c>@Microsoft.KeyVault(</c> are safe KV URI references
    /// and short-circuit to false. Any value with 40+ alphanumeric + secret-
    /// separator chars trips the guard.
    /// </summary>
    private static readonly Regex CleartextSecretPattern = new(
        @"[A-Za-z0-9~._\-]{40,}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        matchTimeout: TimeSpan.FromSeconds(1));

    private readonly IProvisioningRunRepository _repository;
    private readonly IKvSecretManifest _manifest;
    private readonly IKvSecretsWriter _writer;
    private readonly IAppServiceIdentityPatcher _identityPatcher;
    private readonly IArmKeyVaultRefProbe _t1Probe;
    private readonly ISlotIdentityRoleGranter _t5Granter;
    private readonly ISecretFreeMarkerApplier _markerApplier;
    private readonly IOperatorKvRbacBootstrapper _operatorKvRbacBootstrapper;
    private readonly KvSecretsPopulationOptions _options;
    private readonly ILogger<H4KvSecretsPopulationHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>Canonical secret names H4 MUST NEVER delete (BINDING pre-check per spec.md MUST rule + r3 handoff).</summary>
    public static readonly IReadOnlySet<string> BindingNeverDeleteSecrets = new HashSet<string>(StringComparer.Ordinal)
    {
        "Dataverse-ClientSecret",
        "BFF-API-ClientSecret",
    };

    /// <summary>
    /// Constructs the H4 KV secrets-population handler. All collaborators are
    /// interface-abstracted so unit tests can substitute stubs for each seam.
    /// </summary>
    public H4KvSecretsPopulationHandler(
        IProvisioningRunRepository repository,
        IKvSecretManifest manifest,
        IKvSecretsWriter writer,
        IAppServiceIdentityPatcher identityPatcher,
        IArmKeyVaultRefProbe t1Probe,
        ISlotIdentityRoleGranter t5Granter,
        ISecretFreeMarkerApplier markerApplier,
        IOperatorKvRbacBootstrapper operatorKvRbacBootstrapper,
        IOptions<KvSecretsPopulationOptions> options,
        ILogger<H4KvSecretsPopulationHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(identityPatcher);
        ArgumentNullException.ThrowIfNull(t1Probe);
        ArgumentNullException.ThrowIfNull(t5Granter);
        ArgumentNullException.ThrowIfNull(markerApplier);
        ArgumentNullException.ThrowIfNull(operatorKvRbacBootstrapper);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _manifest = manifest;
        _writer = writer;
        _identityPatcher = identityPatcher;
        _t1Probe = t1Probe;
        _t5Granter = t5Granter;
        _markerApplier = markerApplier;
        _operatorKvRbacBootstrapper = operatorKvRbacBootstrapper;
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
            // mismatch means a dispatch bug — fail loud rather than silently
            // mis-executing (parity with sibling handlers).
            throw new InvalidOperationException(
                $"H4KvSecretsPopulationHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H4 KV secrets population starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H4 aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: KvSecretsPopulationRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;

        // (2) Parameter guards — every field H4 needs must be non-empty
        //     BEFORE any external side effect (§4C Resumable classification
        //     for external preconditions).
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                KvSecretsPopulationRejectionCodes.MissingTenantId,
                "Run parameter 'tenantId' is required by H4 (§4D I1 no-hardcoded-tenant). " +
                "Upstream handler (H0.5 for Model 2, L2 endpoint for Model 1) MUST populate this before H4 dispatches.",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, SubscriptionIdParameterKey, out var subscriptionId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                KvSecretsPopulationRejectionCodes.MissingSubscriptionId,
                "Run parameter 'subscriptionId' is required by H4 (ADR-027 D4). H1 subscription-readiness MUST populate this.",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, KeyVaultNameParameterKey, out var keyVaultName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                KvSecretsPopulationRejectionCodes.MissingKeyVaultName,
                "Run parameter 'keyVaultName' is required by H4 (target vault for all writes; §7.9 canonical sprk-{env}-kv).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, ResourceGroupNameParameterKey, out var resourceGroupName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                KvSecretsPopulationRejectionCodes.MissingResourceGroupName,
                "Run parameter 'resourceGroupName' is required by H4 (T1 App Service PATCH + T5 slot MI reads).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, AppServiceNameParameterKey, out var appServiceName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                KvSecretsPopulationRejectionCodes.MissingAppServiceName,
                "Run parameter 'appServiceName' is required by H4 (T1 App Service PATCH).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, UamiResourceIdParameterKey, out var uamiResourceId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                KvSecretsPopulationRejectionCodes.MissingUamiResourceId,
                "Run parameter 'userAssignedIdentityResourceId' is required by H4 (T1 PATCH target). H2a Bicep MUST populate this.",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, SecretsVersionParameterKey, out var secretsVer))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                KvSecretsPopulationRejectionCodes.MissingSecretsVersion,
                "Run parameter 'secretsVer' is required by H4 (idempotency key kv-{customerId}-{secretsVer}). " +
                "MUST be the manifest content hash / semantic version per Phase H canonical secret-catalog manifest.",
                cancellationToken).ConfigureAwait(false);
        }

        var stagingSlotName = TryGetNonEmpty(parameters, StagingSlotNameParameterKey, out var slot)
            ? slot
            : DefaultStagingSlotName;
        var rotateExisting = TryGetNonEmpty(parameters, RotateExistingParameterKey, out var rotateRaw)
            && bool.TryParse(rotateRaw, out var rotateParsed)
            && rotateParsed;
        var kvResourceId = TryGetNonEmpty(parameters, KeyVaultResourceIdParameterKey, out var kvRid)
            ? kvRid
            : BuildKvResourceId(subscriptionId, resourceGroupName, keyVaultName);
        var upgradeMode = TryGetNonEmpty(parameters, ProvisionedOnParameterKey, out var provisionedOnRaw)
            && !string.IsNullOrWhiteSpace(provisionedOnRaw);
        var omitCanonicalNames = TryGetNonEmpty(parameters, FicOmitSecretNamesParameterKey, out var ficOmitRaw)
            ? new HashSet<string>(
                ficOmitRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        // Row A38a (task 205a, 2026-08-25 — auth-v4 §9.1 OMIT-is-the-signal):
        // on secret-free environments, union the three A38a targets into the
        // EXISTING task-126 FR-39 omit seam above (same
        // KvSecretWriteRequest.OmitCanonicalNames -> KvSecretWriteAction.Omitted
        // path the operator's ficOmitSecretNames parameter uses — no parallel
        // seam, per task 125's "no special-casing" commitment). This is
        // defense-in-depth BEHIND FileKvSecretManifest's served-entry filter:
        // it also protects the emergency StaticKvSecretManifest DI-revert,
        // which serves the targets unfiltered. Q3 Path A rollback re-includes
        // them; Dataverse-ClientSecret is never in the target set (§6.5
        // record 2026-08-25, sunset 2026-11-23).
        var secretFreeOmitActive = _options.RequireSecretFreeIdentity && !_options.SecretFreeIdentityRollback;
        if (secretFreeOmitActive)
        {
            omitCanonicalNames.UnionWith(FileKvSecretManifest.SecretFreeIdentityOmitTargets);
            _logger.LogInformation(
                "H4 A38a secret-free omit active: runId={RunId} customerId={CustomerId} — unioned " +
                "{Targets} into the FR-39 OmitCanonicalNames seam (omit-set size now {Size})",
                envelope.RunId, envelope.CustomerId,
                string.Join(", ", FileKvSecretManifest.SecretFreeIdentityOmitTargets), omitCanonicalNames.Count);
        }

        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, secretsVer);

        // (3) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H4 idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (4) Read the manifest. Failure here is Resumable (external precondition
        //     — operator resolves manifest source + resumes; NO writes have
        //     happened yet so partial-state guarantees hold).
        KvSecretManifestReadResult manifestResult;
        try
        {
            manifestResult = await _manifest.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H4 manifest read infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                KvSecretsPopulationRejectionCodes.ManifestReadFailed,
                $"Canonical secret-catalog manifest read failed: {ex.GetType().Name}: {ex.Message}. " +
                "Verify the Phase H manifest source is reachable + the L2 UAMI has read access.",
                cancellationToken).ConfigureAwait(false);
        }

        if (manifestResult is KvSecretManifestReadResult.Failure manifestFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                KvSecretsPopulationRejectionCodes.ManifestReadFailed,
                $"Manifest reader reported failure: {manifestFailure.Diagnostic}",
                cancellationToken).ConfigureAwait(false);
        }

        var entries = ((KvSecretManifestReadResult.Success)manifestResult).Entries;

        // (5) BINDING pre-check — refuse to proceed if any manifest entry has
        //     a Delete op targeting a never-delete canonical name. This MUST
        //     fire BEFORE any external write (§4C QuarantineRequired for
        //     write-side data-leak / fleet-critical destructive intent).
        var forbiddenDelete = entries.FirstOrDefault(e =>
            e.Operation == KvSecretOperation.Delete
            && BindingNeverDeleteSecrets.Contains(e.CanonicalName));
        if (forbiddenDelete is not null)
        {
            var diagnostic =
                $"BINDING pre-check violation: manifest contains Delete op for canonical secret " +
                $"'{forbiddenDelete.CanonicalName}' — spec.md MUST rule + r3 handoff forbid removal of " +
                $"Dataverse-ClientSecret + BFF-API-ClientSecret (OBO + shared-lib Dataverse still depend). " +
                "Reject + QuarantineRequired — do not proceed to KV writes.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                KvSecretsPopulationRejectionCodes.BindingPreCheckViolation, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // Task 200: FromSharedService entries belong to
        // H4SharedKvSecretsPopulationHandler (targets the SHARED KV, not the
        // per-tenant KV). Filter them out here BEFORE the writer sees them so
        // the KvSecretValueResolver never hits its FromSharedService branch
        // during a per-tenant H4 run. The manifest is the single source of
        // truth for both flows; the tier-split lives in the handler.
        var perTenantEntries = entries
            .Where(e => e.ValueSource != KvSecretValueSource.FromSharedService)
            .ToList();

        _logger.LogInformation(
            "H4 manifest loaded: runId={RunId} customerId={CustomerId} entryCount={EntryCount} " +
            "perTenantCount={PerTenantCount} sharedSkipped={SharedSkipped} upgradeMode={UpgradeMode} rotate={Rotate}",
            envelope.RunId, envelope.CustomerId, entries.Count, perTenantEntries.Count,
            entries.Count - perTenantEntries.Count, upgradeMode, rotateExisting);

        // (5.5) HANDLER-09 (Wave 2 pre-dispatch remediation 2026-08-27) — F15 + F18:
        //       bootstrap KV Secrets Officer role on the target vault for the
        //       operator principal BEFORE the first SecretClient.SetSecretAsync
        //       call. Fresh RBAC-enabled KVs deny data-plane access even to
        //       subscription Owner; SESSION 2 hit this on BOTH per-tenant and
        //       shared KVs and manually granted the role. Automating this here
        //       eliminates the dead-loop halt.
        //       Idempotent — no-op when the role assignment already exists.
        //       Domain failure → Resumable + specific rejection code (operator
        //       manually grants + resumes).
        try
        {
            var bootstrapRequest = new OperatorKvRbacBootstrapRequest(
                SubscriptionId: subscriptionId,
                ResourceGroupName: resourceGroupName,
                KeyVaultName: keyVaultName,
                KeyVaultResourceId: kvResourceId,
                // Principal: use the UAMI object id (H2a wrote it to interStepState.MiObjectId).
                // Wave 2 scaffold accepts null when interStepState.MiObjectId is absent — real impl
                // fails hard in that case.
                PrincipalObjectId: run.InterStepState.MiObjectId ?? string.Empty,
                RoleDefinitionId: KvBuiltInRoleIds.SecretsOfficer);
            var bootstrapOutcome = await _operatorKvRbacBootstrapper
                .EnsureGrantedAsync(bootstrapRequest, cancellationToken).ConfigureAwait(false);
            if (bootstrapOutcome is OperatorKvRbacBootstrapOutcome.Failure bootstrapFailure)
            {
                return await FailAsync(run, etag, FailureClass.Resumable,
                    KvSecretsPopulationRejectionCodes.OperatorKvRbacBootstrapFailed,
                    bootstrapFailure.Diagnostic, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "H4 operator-KV-RBAC bootstrap infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                KvSecretsPopulationRejectionCodes.OperatorKvRbacBootstrapFailed,
                $"Operator-KV-RBAC bootstrap infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Manual role grant (Key Vault Secrets Officer) required on the vault before resume.",
                cancellationToken).ConfigureAwait(false);
        }

        // (6) Invoke the writer. Domain outcomes (per-entry Failed / whole-writer
        //     Failure) do NOT throw; only infra faults do. Classification:
        //       - Failure (writer itself broke) → Resumable (no partial state)
        //       - Success with 0 Failed results → proceed to T1
        //       - Success with ANY Failed results → QuarantineRequired
        //         (partial state on the target vault)
        KvSecretsWriteOutcome writeOutcome;
        try
        {
            var writeRequest = new KvSecretWriteRequest(
                CustomerId: envelope.CustomerId,
                TargetKeyVaultName: keyVaultName,
                SubscriptionId: subscriptionId,
                Entries: perTenantEntries,
                UpgradeMode: upgradeMode,
                RotateExisting: rotateExisting,
                SecretParameters: new Dictionary<string, KeyVaultSecretRef>(run.Parameters.Secrets, StringComparer.Ordinal),
                OmitCanonicalNames: omitCanonicalNames);
            writeOutcome = await _writer.WriteAsync(writeRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H4 KV writer infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                KvSecretsPopulationRejectionCodes.KvWriteFailedNoPartialState,
                $"KV writer infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "No partial vault state (writer failed before any per-entry work).",
                cancellationToken).ConfigureAwait(false);
        }

        if (writeOutcome is KvSecretsWriteOutcome.Failure writeFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                KvSecretsPopulationRejectionCodes.KvWriteFailedNoPartialState,
                writeFailure.Diagnostic, cancellationToken).ConfigureAwait(false);
        }

        var writeResults = ((KvSecretsWriteOutcome.Success)writeOutcome).Results;
        var failedResults = writeResults.Where(r => r.Action == KvSecretWriteAction.Failed).ToList();
        if (failedResults.Count > 0)
        {
            var diagnostic =
                $"H4 KV writes partially failed on vault '{keyVaultName}': " +
                $"{failedResults.Count} of {writeResults.Count} entries failed. " +
                $"Failed: {string.Join(", ", failedResults.Select(r => $"{r.CanonicalName}={r.ErrorMessage}"))}. " +
                "Partial vault state — QuarantineRequired per §4C.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                KvSecretsPopulationRejectionCodes.KvWritePartialFailure, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (7) T1 PATCH — set keyVaultReferenceIdentity to UAMI on BOTH slots.
        //     Patcher failure is QuarantineRequired (writes succeeded but
        //     downstream BFF boot cannot resolve KV refs — half-configured
        //     App Service is worse than fully broken).
        AppServiceIdentityPatchResult patchResult;
        try
        {
            patchResult = await _identityPatcher.PatchKeyVaultReferenceIdentityAsync(
                new AppServiceIdentityPatchInput(
                    SubscriptionId: subscriptionId,
                    ResourceGroupName: resourceGroupName,
                    AppServiceName: appServiceName,
                    StagingSlotName: stagingSlotName,
                    UserAssignedIdentityResourceId: uamiResourceId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H4 T1 patcher infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                KvSecretsPopulationRejectionCodes.TrapT1PatchFailed,
                $"T1 patcher infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "KV writes succeeded but App Service keyVaultReferenceIdentity NOT patched — QuarantineRequired.",
                cancellationToken).ConfigureAwait(false);
        }

        if (patchResult is AppServiceIdentityPatchResult.Failure patchFailure)
        {
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                KvSecretsPopulationRejectionCodes.TrapT1PatchFailed, patchFailure.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (8) T1 VERIFY — ARM read post-PATCH confirms keyVaultReferenceIdentity
        //     == UAMI on BOTH slots. Reuses the H2a-owned IArmKeyVaultRefProbe
        //     (task 044) — same trap, same verifier, single source of truth.
        ArmKeyVaultRefProbeResult t1Result;
        try
        {
            t1Result = await _t1Probe.VerifyKeyVaultReferenceIdentityAsync(
                new ArmKeyVaultRefProbeInput(
                    SubscriptionId: subscriptionId,
                    ResourceGroupName: resourceGroupName,
                    AppServiceName: appServiceName,
                    StagingSlotName: stagingSlotName,
                    ExpectedUserAssignedIdentityResourceId: uamiResourceId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H4 T1 probe infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                KvSecretsPopulationRejectionCodes.TrapT1VerificationMismatch,
                $"T1 verification probe infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "PATCH claimed success but post-condition could not be verified — QuarantineRequired.",
                cancellationToken).ConfigureAwait(false);
        }

        if (t1Result is ArmKeyVaultRefProbeResult.Mismatch t1Mismatch)
        {
            var diagnostic =
                $"T1 trap NOT cleared for App Service '{appServiceName}' post-PATCH: " +
                $"expected keyVaultReferenceIdentity == '{uamiResourceId}' on both slots; " +
                $"observed prod='{t1Mismatch.ObservedProductionSlotIdentity ?? "(null)"}', " +
                $"staging='{t1Mismatch.ObservedStagingSlotIdentity ?? "(null)"}'. " +
                "PATCH silently failed — QuarantineRequired.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                KvSecretsPopulationRejectionCodes.TrapT1VerificationMismatch, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (9) T5 INTERIM — grant KV Secrets User to both slot System-Assigned MI
        //     principals when present. NoSlotSystemAssignedIdentity is the
        //     desired post-Phase-C steady state (task 028/029/030) and is
        //     treated as SUCCESS. Grant failure is Resumable (T5 is INTERIM
        //     per spec.md FR-33; operator can re-run or wait for UAMI).
        SlotIdentityRoleGrantResult t5Result;
        try
        {
            t5Result = await _t5Granter.GrantAsync(
                new SlotIdentityRoleGrantInput(
                    SubscriptionId: subscriptionId,
                    ResourceGroupName: resourceGroupName,
                    AppServiceName: appServiceName,
                    StagingSlotName: stagingSlotName,
                    VaultResourceId: kvResourceId,
                    RoleDefinitionId: _options.KvSecretsUserRoleId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H4 T5 granter infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                KvSecretsPopulationRejectionCodes.TrapT5GrantFailed,
                $"T5 granter infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "T1 succeeded; T5 is INTERIM (structurally impossible post-Phase-C UAMI) — Resumable.",
                cancellationToken).ConfigureAwait(false);
        }

        if (t5Result is SlotIdentityRoleGrantResult.Failure t5Failure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                KvSecretsPopulationRejectionCodes.TrapT5GrantFailed, t5Failure.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }
        // Granted OR NoSlotSystemAssignedIdentity both proceed to Success.

        // (10) ADR-028 cleartext-secret-leak guard on interStepState BEFORE
        //      writing anything to Cosmos. H4 does not populate any interStepState
        //      field itself (H8 does ContainerTypeId; H2a already wrote endpoints
        //      + UAMI ids); but this belt-and-braces scan catches a manifest-
        //      driven regression where a writer output surfaces cleartext.
        var leak = FindCleartextSecretLeak(run.InterStepState);
        if (leak is not null)
        {
            var diagnostic =
                $"H4 cleartext-secret-leak guard tripped on interStepState field '{leak.Value.Field}' " +
                "(value looks secret-shaped) — ADR-028 MUST rule violation. " +
                "Refusing to persist to Cosmos. Investigate upstream handler outputs.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                KvSecretsPopulationRejectionCodes.CleartextSecretLeak, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (10.5) Row A38a positive migration marker (auth-v4 §9.1: the marker
        //        lives OUTSIDE the credential slots — KV resource tag
        //        spaarke-secret-free-identity=true + registry state field
        //        sprk_credentialmode=secret-free). Applied once per vault:
        //        under Model 2 the per-customer dispatch fan-out invokes H4
        //        once per customer vault, so this single call IS the
        //        once-per-vault application (no extra iteration pass).
        //        Idempotent (tag: check-then-apply; registry: value-idempotent
        //        PATCH; Level-3 idempotency above short-circuits re-runs
        //        entirely). Failure is Resumable + FAIL-LOUD — an unmarked
        //        secret-free vault is the §5.3 fleet-consistency gap. NOT
        //        applied under Q3 Path A rollback (env is not secret-free).
        if (secretFreeOmitActive)
        {
            SecretFreeMarkerApplyOutcome markerOutcome;
            try
            {
                markerOutcome = await _markerApplier.ApplyAsync(
                    new SecretFreeMarkerApplyRequest(
                        SubscriptionId: subscriptionId,
                        ResourceGroupName: resourceGroupName,
                        KeyVaultName: keyVaultName,
                        TenantId: tenantId,
                        CustomerIdForLog: envelope.CustomerId,
                        RunIdForLog: envelope.RunId),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "H4 A38a marker applier infrastructure fault: runId={RunId} customerId={CustomerId}",
                    envelope.RunId, envelope.CustomerId);
                return await FailAsync(run, etag, FailureClass.Resumable,
                    KvSecretsPopulationRejectionCodes.SecretFreeMarkerApplyFailed,
                    $"A38a marker applier infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                    "KV writes/omits succeeded; marker application is idempotent — fix cause + resume.",
                    cancellationToken).ConfigureAwait(false);
            }

            if (markerOutcome is SecretFreeMarkerApplyOutcome.Failure markerFailure)
            {
                return await FailAsync(run, etag, FailureClass.Resumable,
                    KvSecretsPopulationRejectionCodes.SecretFreeMarkerApplyFailed,
                    markerFailure.Diagnostic, cancellationToken).ConfigureAwait(false);
            }
        }

        // (11) All post-conditions cleared — advance Cosmos state. Downstream
        //      reconciler (wave C5) fans out to H7.
        stopwatch.Stop();
        var wroteCount = writeResults.Count(r => r.Action == KvSecretWriteAction.Wrote);
        var skippedCount = writeResults.Count(r => r.Action == KvSecretWriteAction.SkippedRotationSafe);
        var deletedCount = writeResults.Count(r => r.Action == KvSecretWriteAction.Deleted);
        var omittedCount = writeResults.Count(r => r.Action == KvSecretWriteAction.Omitted);
        var t5Summary = t5Result switch
        {
            SlotIdentityRoleGrantResult.Granted => "granted-both-slots",
            SlotIdentityRoleGrantResult.NoSlotSystemAssignedIdentity => "structural-noop-post-uami",
            _ => "unknown",
        };
        _logger.LogInformation(
            "H4 KV secrets population succeeded: runId={RunId} customerId={CustomerId} " +
            "wrote={Wrote} skipped-rotation-safe={Skipped} deleted={Deleted} omitted-fr39={Omitted} " +
            "secretFree={SecretFree} t1=cleared t5={T5} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, wroteCount, skippedCount, deletedCount, omittedCount,
            secretFreeOmitActive, t5Summary, stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(run, etag, idempotencyKey, envelope, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H4 idempotency key:
    /// <c>kv-{customerId}-{secretsVer}</c>. Exposed internal so unit tests
    /// can construct expected keys without duplicating the format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, string secretsVer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretsVer);
        return $"kv-{customerId}-{secretsVer}";
    }

    /// <summary>
    /// Builds the canonical KV resource id from subscription + rg + vault name.
    /// Used when the operator does not pass an explicit
    /// <see cref="KeyVaultResourceIdParameterKey"/> — the standard shape
    /// suffices for the T5 role-assignment scope.
    /// </summary>
    internal static string BuildKvResourceId(string subscriptionId, string resourceGroupName, string keyVaultName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceGroupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyVaultName);
        return $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.KeyVault/vaults/{keyVaultName}";
    }

    /// <summary>
    /// Detects candidate cleartext-secret patterns in a value. Exposed
    /// internal so unit tests can validate the guard's coverage. Values
    /// starting with <c>@Microsoft.KeyVault(</c> are treated as safe KV URI
    /// references. Parity with H3's IsCleartextSecretPattern.
    /// </summary>
    internal static bool IsCleartextSecretPattern(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.StartsWith("@Microsoft.KeyVault(", StringComparison.Ordinal)) return false;

        try
        {
            return CleartextSecretPattern.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return true; // Safer to treat timeout as suspicious.
        }
    }

    private static (string Field, string Value)? FindCleartextSecretLeak(InterStepState state)
    {
        (string, string?)[] pairs =
        {
            (nameof(state.BffAppRegId), state.BffAppRegId),
            (nameof(state.S2SAppRegId), state.S2SAppRegId),
            (nameof(state.MiObjectId), state.MiObjectId),
            (nameof(state.MiClientId), state.MiClientId),
            (nameof(state.ContainerTypeId), state.ContainerTypeId),
            (nameof(state.DataverseEnvUrl), state.DataverseEnvUrl),
            (nameof(state.OpenAiEndpoint), state.OpenAiEndpoint),
            (nameof(state.AiSearchEndpoint), state.AiSearchEndpoint),
            (nameof(state.CosmosEndpoint), state.CosmosEndpoint),
            (nameof(state.SystemUserId), state.SystemUserId),
            (nameof(state.SpeConsentCorrelationId), state.SpeConsentCorrelationId),
        };
        foreach (var (field, value) in pairs)
        {
            if (IsCleartextSecretPattern(value))
            {
                return (field, value!);
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
                QuarantinedAt = DateTimeOffset.UtcNow,
            };
        }

        run.GateStates[$"h4-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H4 failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H4 failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
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

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H4 success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: KvSecretsPopulationRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H4 read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H4.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H4 success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: KvSecretsPopulationRejectionCodes.RunDeletedDuringPopulation,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H4 was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
