// -----------------------------------------------------------------------------
// H3EntraAppRegHandler.cs
//
// L2 CONTROL-PLANE H3 Entra app-registration handler (task 046, wave C4;
// REWRITTEN task 130, Wave G-3, xhigh, per DS-1b/DS-4 — Graph SDK port +
// Model 1/Model 2 tenancy branch + real consent verifier + FIC).
//
// TASK 130 REWRITE SUMMARY:
//   - Replaced the shell-out IEntraAppRegProvisioner
//     (RegisterEntraAppRegScriptProvisioner, RETIRED) with the Graph-SDK
//     GraphAppRegistrationProvisioner.
//   - Replaced the always-Verified IAdminConsentVerifier
//     (NullAdminConsentVerifier, RETIRED) with GraphAdminConsentVerifier — a
//     REAL oauth2PermissionGrants query. This closes DS-4 §3's primary
//     defect finding: "the consent gate can advance on fiction."
//   - Added the Model 1 vs Model 2 tenancy-model runtime branch (spec.md
//     FR-39 + design.md §4.1 H3 row v3.5 split), I6-enforced (design.md §4D,
//     spec.md FR-40): the branch-selection MUST take an explicit
//     tenancyModel value with NO default/fallback — see step (2) below.
//   - Added Model 2's federated-identity-credential (FIC) step trusting the
//     shared BFF UAMI (auth-v4 §3.1 recipe).
//   - Added the BFF-API-ClientId / BFF-API-Audience RunParameters.Secrets
//     writes H4 consumes via its FromRunParameters resolver (task 129's
//     manifest.yaml reclassification — H3 is the documented value producer).
//   - REMOVED H3's own Dataverse-app-user-assignment step. DELIBERATE
//     DEVIATION from the POML's literal step 5 text — see the "SCOPE
//     DEVIATION" note below for the full rationale (Path C per root
//     CLAUDE.md §6.5).
//   - REMOVED the 14-AppRoleAssignedTo-grant step the POML's step 2/acceptance
//     criteria described. DELIBERATE DEVIATION — see "SCOPE DEVIATION" below.
//
// SCOPE DEVIATION #1 (14 AppRoleAssignedTo grants — Path C, comply with the
// MORE AUTHORITATIVE source): design.md §4.1's H3 SDK-surface table (line
// ~197) lists ONLY "Microsoft.Graph 6.x (Applications/ServicePrincipals/
// Oauth2PermissionGrants)" for H3 — no AppRoleAssignedTo. H10
// (H10DataverseAppUserGraphParityHandler + GraphRestAppRoleGranter +
// GraphRestAppRoleParityVerifier, task 053, ALREADY LANDED before this task)
// already grants ALL 14 application-only roles from
// Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles.cs onto the customer's UAMI
// service principal — a DIFFERENT service principal than this app-reg's own
// (GraphAppRoles.cs's own doc comment: "roles MUST be granted on the UAMI SP
// — not on the app registration"). Duplicating that grant loop here would
// (a) violate CLAUDE.md §11 Component Justification (near-identical logic to
// an already-correct, already-tested collaborator) and (b) grant roles onto
// the WRONG principal if pointed at this app-reg's SP instead of the UAMI's
// — a functional bug, not just redundant code. This task's own POML text
// ("~14 grants") describes the COMBINED H3+H10 "Gap 3" scope at the spec
// level (design.md line 119's row literally says "Entra app registration
// (~14 grants)" as the umbrella description spanning both handlers); the
// concrete SDK-surface table is the authoritative per-handler boundary.
// H3's own real consent verifier instead covers the 5 DELEGATED
// (OAuth2PermissionScope) grants — EntraAppRegPermissionCatalog.cs.
//
// SCOPE DEVIATION #2 (Dataverse app-user assignment — Path C, avoid
// duplicating already-correct + DAG-respecting behavior): H10's own
// HandleAsync (steps 9-10) ALREADY registers BOTH the BFF app-reg
// (interStepState.BffAppRegId, H3's own output) AND the UAMI as Dataverse
// System Administrator App Users via DataverseWebApiAppUserCreator — the
// EXACT idiom the POML's step 5 asks H3 to reuse. Attempting the SAME
// registration inside H3 would be genuinely redundant AND, per design.md's
// DAG ("H4 → H3" is a SEPARATE branch from "H5 → H6 → H7 → H10"), would run
// BEFORE InterStepState.DataverseEnvUrl is populated in the common case
// (H5/H6 have not necessarily completed when H3 dispatches) — an early H3
// attempt would almost always short-circuit on a MissingDataverseEnvUrl-style
// guard, making the duplicate code dead weight in the success path and a
// source of drift risk if it ever DID have the value (two independent
// find-by-applicationid/create/associate call sites is exactly what DS-4's
// own "REUSE... do not write a second, parallel implementation" instruction
// forbids). H3's sole obligation toward this step is to write
// InterStepState.BffAppRegId — the exact value H10 already consumes.
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌──────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                         │ §4C class                 │
//   ├──────────────────────────────────────┼───────────────────────────┤
//   │ Missing/invalid tenancyModel (I6)    │ Resumable                 │
//   │ Missing tenantId (§4D I1)            │ Resumable                 │
//   │ Missing keyVaultName (Model 2)       │ Resumable                 │
//   │ Missing UAMI principalId (Model 2)   │ Resumable                 │
//   │ Missing shared app-reg config (M1)   │ Resumable                 │
//   │ Model 1 shared-app config drift      │ Resumable (operator fixes │
//   │                                      │ the SHARED platform app,  │
//   │                                      │ out of per-customer scope)│
//   │ Provisioner PS-era shell-out failure │ N/A (no shell-out remains)│
//   │ Provisioner Graph failure            │ Resumable                 │
//   │ Provisioner outputs incomplete       │ Resumable                 │
//   │ Admin consent Pending (WaitingOnGate │ (NOT a failure — Success  │
//   │ transition)                          │ with WaitingOnGate state) │
//   │ FIC creation/verification failed     │ Resumable                 │
//   │ Deferred KV-write commit failed      │ QuarantineRequired (app + │
//   │                                      │ consent both real, but KV │
//   │                                      │ state is now ambiguous)   │
//   │ Cleartext-secret-leak detected       │ QuarantineRequired        │
//   │ S2S app-reg accidentally provisioned │ QuarantineRequired        │
//   │ Concurrent Cosmos writer conflict    │ Resumable                 │
//   │ Run row deleted mid-flight           │ Resumable                 │
//   └──────────────────────────────────────┴───────────────────────────┘
//
// KV-WRITE ORDERING (DS-4 §3 BINDING, task 130): Graph app ensure -> consent
// gate -> real Oauth2PermissionGrants verify -> KV writes -> (H10 owns
// Dataverse app-user assign, see SCOPE DEVIATION #2). Model 2's
// GraphAppRegistrationProvisioner.ProvisionAsync STAGES (does not write) the
// 3 KV secrets; this handler commits them via CommitPendingSecretsAsync ONLY
// after the consent verifier returns Verified — never before.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H3EntraAppRegHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md § 4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H3;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the target Key Vault name (Model 2 only).</summary>
    public const string KeyVaultNameParameterKey = "keyVaultName";

    /// <summary>Tenancy-model value — Model 1 (shared/SMB). Matches ProvisioningRun.TenancyModel + H2b's identical literal.</summary>
    public const string Model1Shared = "Model1Shared";

    /// <summary>Tenancy-model value — Model 2 (dedicated). Matches ProvisioningRun.TenancyModel + H2b's identical literal.</summary>
    public const string Model2Dedicated = "Model2Dedicated";

    /// <summary>
    /// Simple heuristics to detect a cleartext secret pattern accidentally
    /// smuggled into the provisioner output. NOT a cryptographic scan — a
    /// forcing-function to catch obvious regressions.
    /// </summary>
    private static readonly Regex[] CleartextSecretPatterns =
    {
        new(@"[A-Za-z0-9~._\-]{40,}", RegexOptions.CultureInvariant | RegexOptions.Compiled,
            matchTimeout: TimeSpan.FromSeconds(1)),
    };

    private readonly IProvisioningRunRepository _repository;
    private readonly IEntraAppRegProvisioner _provisioner;
    private readonly IAdminConsentVerifier _consentVerifier;
    private readonly EntraAppRegOptions _options;
    private readonly ILogger<H3EntraAppRegHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    public H3EntraAppRegHandler(
        IProvisioningRunRepository repository,
        IEntraAppRegProvisioner provisioner,
        IAdminConsentVerifier consentVerifier,
        IOptions<EntraAppRegOptions> options,
        ILogger<H3EntraAppRegHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(provisioner);
        ArgumentNullException.ThrowIfNull(consentVerifier);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _provisioner = provisioner;
        _consentVerifier = consentVerifier;
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
                $"H3EntraAppRegHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H3 Entra app-reg starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H3 aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: EntraAppRegRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;

        // (1) I6 ENFORCEMENT (design.md §4D, spec.md FR-40, BINDING): the
        // Model 1 vs Model 2 branch MUST be selected from an EXPLICIT,
        // non-blank, recognized tenancyModel value — NO default/fallback.
        // Unlike H2b's `string.IsNullOrWhiteSpace(run.TenancyModel) ?
        // "Model2Dedicated" : run.TenancyModel` (a legacy scaffolding
        // convenience H2b is still allowed), H3's branch selection is the
        // exact call site design.md §4D's NEW invariant I6 targets — no
        // silent default is permitted here.
        if (!IsRecognizedTenancyModel(run.TenancyModel, out var tenancyModel))
        {
            var diagnostic =
                $"ProvisioningRun.tenancyModel is '{run.TenancyModel ?? "(null)"}' — I6 (design.md §4D, spec.md " +
                $"FR-40) requires an explicit '{Model1Shared}' or '{Model2Dedicated}' value with NO default. " +
                "Upstream (H0/H0.5 for Model 2 self-service, or the operator intake for Model 1) MUST populate " +
                "this before H3 dispatches.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.MissingOrInvalidTenancyModel, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (2) §4D I1 tenant guard — H3 MUST NOT fall back to a default tenant.
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            var diagnostic =
                "Run parameter 'tenantId' is required by H3 (§4D I1 no-hardcoded-tenant). " +
                "Upstream handler (H0.5 for Model 2, L2 endpoint for Model 1) MUST populate this " +
                "before H3 dispatches.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.MissingTenantId, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (3) Expected-delegated-scope-count guard (renamed from the
        // Wave-C4 scaffold's ExpectedAppRoleCount — see EntraAppRegOptions.cs
        // + EntraAppRegPermissionCatalog.cs file headers for the scope
        // correction). Zero/negative would silently pass the admin-consent
        // Verified branch even when NO grants exist — refuse.
        if (_options.ExpectedDelegatedScopeCount < 1)
        {
            var diagnostic =
                $"EntraAppReg:ExpectedDelegatedScopeCount is {_options.ExpectedDelegatedScopeCount} — MUST be >= 1. " +
                "Configuration drift; defaults to 5 per EntraAppRegPermissionCatalog.All.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.NullAppRoleIdInCatalog, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, tenantId);

        // (4) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H3 idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (5) Branch on tenancy model.
        EntraAppRegOutputs outputs;
        IReadOnlyList<PendingKvSecretWrite> pendingKvWrites;
        if (string.Equals(tenancyModel, Model1Shared, StringComparison.Ordinal))
        {
            var model1Result = await HandleModel1Async(run, etag, cancellationToken).ConfigureAwait(false);
            if (model1Result.Failure is not null)
            {
                return model1Result.Failure;
            }
            outputs = model1Result.Outputs!;
            pendingKvWrites = Array.Empty<PendingKvSecretWrite>(); // Model 1 never writes — references only.
        }
        else
        {
            if (!TryGetNonEmpty(parameters, KeyVaultNameParameterKey, out var keyVaultName))
            {
                var diagnostic =
                    "Run parameter 'keyVaultName' is required by H3 Model 2 (target for BFF-API-ClientSecret/" +
                    "ClientId/Audience). Upstream handler (H2a Bicep) MUST populate this from the deployed " +
                    "platform KV name before H3 dispatches.";
                return await FailAsync(run, etag, FailureClass.Resumable,
                    EntraAppRegRejectionCodes.MissingKeyVaultName, diagnostic, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(run.InterStepState.MiObjectId))
            {
                var diagnostic =
                    "InterStepState.miObjectId (the shared BFF UAMI's principalId — the FIC 'subject' per " +
                    "auth-v4 §3.1) is not populated. H2a (uami.bicep) MUST complete before H3's Model 2 branch " +
                    "can create the FIC.";
                return await FailAsync(run, etag, FailureClass.Resumable,
                    EntraAppRegRejectionCodes.MissingUamiObjectId, diagnostic, cancellationToken).ConfigureAwait(false);
            }

            var model2Result = await HandleModel2Async(
                run, etag, envelope, tenantId, keyVaultName, run.InterStepState.MiObjectId!, cancellationToken)
                .ConfigureAwait(false);
            if (model2Result.Failure is not null)
            {
                return model2Result.Failure;
            }
            outputs = model2Result.Outputs!;
            pendingKvWrites = model2Result.PendingKvWrites!;
        }

        // (6) Structural outputs guard — reject any incomplete output payload.
        if (string.IsNullOrWhiteSpace(outputs.BffAppRegId) || string.IsNullOrWhiteSpace(outputs.BffClientSecretKvUri))
        {
            var diagnostic =
                "Entra app-reg provisioning returned incomplete outputs — one of BffAppRegId / " +
                "BffClientSecretKvUri is blank.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.ProvisioningOutputsIncomplete, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (7) ADR-028 cleartext-secret-leak guard — the KV URI ref MUST start
        // with '@Microsoft.KeyVault('; anything else that looks secret-shaped
        // is a data-leak (Quarantine-required).
        if (IsCleartextSecretPattern(outputs.BffClientSecretKvUri))
        {
            var diagnostic =
                "Entra app-reg provisioning returned a client-secret-shaped value where a KV URI " +
                "reference was expected — ADR-028 MUST rule violation. Refusing to persist to Cosmos.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                EntraAppRegRejectionCodes.CleartextSecretLeak, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (8) S2S-forbidden structural guard — MUST NOT re-introduce Dataverse
        // S2S app-reg per r3 task 060.
        if (!string.IsNullOrWhiteSpace(run.InterStepState.S2SAppRegId))
        {
            var diagnostic =
                $"ProvisioningRun.interStepState.s2sAppRegId is populated ('{run.InterStepState.S2SAppRegId}') — " +
                "spec MUST rule violation (r3 task 060 dropped the Dataverse S2S app-reg). Refusing to advance.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                EntraAppRegRejectionCodes.S2SAppRegForbidden, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (9) Admin-consent verification (REAL — GraphAdminConsentVerifier).
        AdminConsentVerificationResult consentResult;
        try
        {
            consentResult = await _consentVerifier.VerifyAsync(
                outputs.BffAppRegId, tenantId, _options.ExpectedDelegatedScopeCount, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H3 admin-consent verifier threw unexpected exception: runId={RunId} customerId={CustomerId} bffAppRegId={BffAppRegId}",
                envelope.RunId, envelope.CustomerId, outputs.BffAppRegId);
            var diagnostic =
                $"Admin-consent verifier infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                $"App-registration reference is valid (bffAppRegId={outputs.BffAppRegId}); only consent " +
                "verification failed. Resumable.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.ProvisioningFailed, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (10) DS-4 §3 BINDING ORDER: KV writes commit ONLY after Verified —
        // never before. Model 1 has nothing to commit (pendingKvWrites empty).
        if (consentResult is AdminConsentVerificationResult.Verified && pendingKvWrites.Count > 0)
        {
            var commitDiagnostic = await _provisioner.CommitPendingSecretsAsync(pendingKvWrites, cancellationToken)
                .ConfigureAwait(false);
            if (commitDiagnostic is not null)
            {
                var diagnostic =
                    $"Deferred KV-secret commit failed AFTER consent was verified: {commitDiagnostic}. " +
                    "App-reg + consent are both real; KV state is now ambiguous (some/none of ClientId/" +
                    "Audience/ClientSecret may be written) — QuarantineRequired per §4C.";
                return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                    EntraAppRegRejectionCodes.ProvisioningFailed, diagnostic, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "H3 Entra app-reg provisioning succeeded: runId={RunId} customerId={CustomerId} " +
            "tenancyModel={TenancyModel} bffAppRegId={BffAppRegId} adminConsent={ConsentState} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, tenancyModel, outputs.BffAppRegId,
            consentResult.GetType().Name, stopwatch.ElapsedMilliseconds);

        return await MarkAdvanceAsync(run, etag, idempotencyKey, outputs, envelope,
            consentResult, cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------
    // Model 1 / Model 2 branches
    // ---------------------------------------------------------------------

    /// <summary>
    /// MODEL 1 (shared multitenant BFF app-reg). Creates ZERO new app-reg
    /// objects and ZERO new FIC objects (acceptance criterion) — verifies the
    /// shared app-reg's grant currency, then REFERENCES its pre-existing KV
    /// entries (does NOT write to the shared platform vault from a
    /// per-customer handler — avoids concurrent-write risk on a shared
    /// resource). "Consent-callback trust registration" (POML text) is
    /// already satisfied by H0.5 having run BEFORE H3 in the DAG — H3's own
    /// contribution here is the grant-currency verification + the real
    /// per-customer-tenant consent check (step 9 in HandleAsync, shared by
    /// both branches).
    /// </summary>
    private async Task<(HandlerResult? Failure, EntraAppRegOutputs? Outputs)> HandleModel1Async(
        ProvisioningRun run, string etag, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SharedBffAppRegistrationId)
            || string.IsNullOrWhiteSpace(_options.SharedPlatformKeyVaultName))
        {
            var diagnostic =
                "EntraAppReg:SharedBffAppRegistrationId and/or EntraAppReg:SharedPlatformKeyVaultName are not " +
                "configured. Model 1 requires the shared multitenant app-reg to already exist with its KV " +
                "entries seeded — operator must complete initial platform setup before onboarding Model 1 tenants.";
            return (await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.MissingSharedAppRegConfig, diagnostic, cancellationToken)
                .ConfigureAwait(false), null);
        }

        EntraAppRegSharedVerifyOutcome verifyOutcome;
        try
        {
            verifyOutcome = await _provisioner.VerifySharedAsync(
                new EntraAppRegSharedVerifyRequest(_options.SharedBffAppRegistrationId), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var diagnostic = $"Model 1 shared-app verification infrastructure error: {ex.GetType().Name}: {ex.Message}. Resumable.";
            return (await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.ProvisioningFailed, diagnostic, cancellationToken).ConfigureAwait(false), null);
        }

        switch (verifyOutcome)
        {
            case EntraAppRegSharedVerifyOutcome.Failure f:
                return (await FailAsync(run, etag, FailureClass.Resumable,
                    EntraAppRegRejectionCodes.ProvisioningFailed, f.Diagnostic, cancellationToken).ConfigureAwait(false), null);
            case EntraAppRegSharedVerifyOutcome.Drifted d:
                return (await FailAsync(run, etag, FailureClass.Resumable,
                    EntraAppRegRejectionCodes.SharedAppRegConfigurationDrift, d.Diagnostic, cancellationToken).ConfigureAwait(false), null);
            case EntraAppRegSharedVerifyOutcome.Current:
                // Reference pre-existing shared-vault entries only — Model 1
                // creates ZERO new app-reg / FIC objects (acceptance criterion).
                return (null, new EntraAppRegOutputs
                {
                    BffAppRegId = _options.SharedBffAppRegistrationId!,
                    BffClientSecretKvUri = BuildSharedKvUriReference(),
                    PendingKvWrites = Array.Empty<PendingKvSecretWrite>(),
                });
            default:
                // Defensive — a future 4th case must not silently fall through
                // as either Failure or Current.
                throw new InvalidOperationException(
                    $"Unhandled EntraAppRegSharedVerifyOutcome subtype '{verifyOutcome.GetType().Name}'.");
        }
    }

    private async Task<(HandlerResult? Failure, EntraAppRegOutputs? Outputs, IReadOnlyList<PendingKvSecretWrite>? PendingKvWrites)>
        HandleModel2Async(
            ProvisioningRun run, string etag, HandlerEnvelope envelope,
            string tenantId, string keyVaultName, string uamiPrincipalId, CancellationToken cancellationToken)
    {
        EntraAppRegOutcome outcome;
        try
        {
            var request = new EntraAppRegRequest(
                CustomerId: envelope.CustomerId,
                TenantId: tenantId,
                VaultName: keyVaultName,
                UamiPrincipalId: uamiPrincipalId,
                Profile: run.Profile ?? string.Empty);
            outcome = await _provisioner.ProvisionAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (CrossTenantFicRefusedException ex)
        {
            // A42 / SF-5: the tenancy guard fired — a cross-tenant
            // (app-reg, UAMI) FIC pair was refused BEFORE creation. Distinct
            // rejection code so operators/reconcilers route it without
            // string-matching (vs. discovering it weeks later at the
            // customer's first OBO as an opaque AADSTS error). Resumable:
            // operator corrects the run's tenant/profile configuration.
            _logger.LogError(ex,
                "H3 cross-tenant FIC REFUSED: runId={RunId} customerId={CustomerId} " +
                "appRegTenantId={AppRegTenantId} uamiTenantId={UamiTenantId} profile={Profile}",
                envelope.RunId, envelope.CustomerId,
                ex.AppRegistrationTenantId, ex.UamiTenantId, ex.Profile);
            return (await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.CrossTenantFicRefused, ex.Message, cancellationToken)
                .ConfigureAwait(false), null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H3 Entra app-reg provisioning infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"Entra app-reg provisioning infrastructure error: {ex.GetType().Name}: {ex.Message}. Resumable.";
            return (await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.ProvisioningFailed, diagnostic, cancellationToken).ConfigureAwait(false), null, null);
        }

        if (outcome is EntraAppRegOutcome.Failure runnerFailure)
        {
            return (await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.ProvisioningFailed, runnerFailure.Diagnostic, cancellationToken)
                .ConfigureAwait(false), null, null);
        }

        var outputs = ((EntraAppRegOutcome.Success)outcome).Outputs;
        return (null, outputs, outputs.PendingKvWrites);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static bool IsRecognizedTenancyModel(string? value, out string tenancyModel)
    {
        if (string.Equals(value, Model1Shared, StringComparison.Ordinal)
            || string.Equals(value, Model2Dedicated, StringComparison.Ordinal))
        {
            tenancyModel = value!;
            return true;
        }
        tenancyModel = string.Empty;
        return false;
    }

    private string BuildSharedKvUriReference()
        => $"@Microsoft.KeyVault(SecretUri=https://{_options.SharedPlatformKeyVaultName}.vault.azure.net/secrets/{GraphAppRegistrationProvisioner.ClientSecretName}/)";

    /// <summary>
    /// Computes the deterministic H3 idempotency key:
    /// <c>appreg-{customerId}-{tenantId}</c>.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return $"appreg-{customerId}-{tenantId}";
    }

    /// <summary>
    /// Detects candidate cleartext-secret patterns in the given value. Any
    /// value that BEGINS with <c>@Microsoft.KeyVault(</c> is treated as a KV
    /// URI reference literal (safe) and short-circuits to <c>false</c>.
    /// </summary>
    internal static bool IsCleartextSecretPattern(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.StartsWith("@Microsoft.KeyVault(", StringComparison.Ordinal)) return false;

        foreach (var pattern in CleartextSecretPatterns)
        {
            try
            {
                if (pattern.IsMatch(value)) return true;
            }
            catch (RegexMatchTimeoutException)
            {
                return true;
            }
        }
        return false;
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

        run.GateStates[$"h3-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H3 failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H3 failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkAdvanceAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        EntraAppRegOutputs outputs,
        HandlerEnvelope envelope,
        AdminConsentVerificationResult consentResult,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        // Write bffAppRegId regardless of consent state — H4/H10 need it
        // whether consent is Verified now or pending.
        run.InterStepState.BffAppRegId = outputs.BffAppRegId;

        // A42 / SF-8: when the FIC's creation-time result is the script
        // exit-2 equivalent (persisted + structurally re-GET-verified, but
        // NOT exchange-verified — the NORMAL L2 outcome, GOTCHA 2), record
        // the pending marker so the run report distinguishes
        // "persisted-verified" from "exchange-verified" and H13/T4 discharges
        // the REAL post-App-Service exchange verification. Exit-2 is NEVER
        // terminal success. Written on both consent paths (Pending +
        // Verified) — the FIC's verification debt is independent of the
        // admin-consent gate.
        if (outputs.FicVerification == FicVerificationState.PendingPostAppServiceVerification)
        {
            run.InterStepState.FicPendingPostAppServiceVerification = true;
        }

        run.ErrorDetail = null;

        if (consentResult is AdminConsentVerificationResult.Pending pending)
        {
            run.Status = RunStatus.WaitingOnGate;
            run.CurrentPhase = HandlerIdentifier;
            run.GateStates[EntraAppRegGates.AdminConsent] = new GateEntry
            {
                Status = GateState.Pending,
                VerifierHandler = HandlerIdentifier,
                Evidence = pending.Evidence,
            };

            _logger.LogInformation(
                "H3 admin-consent Pending — run transitioned to WaitingOnGate: " +
                "runId={RunId} customerId={CustomerId} bffAppRegId={BffAppRegId} " +
                "granted={GrantedCount}/{ExpectedCount}",
                run.RunId, run.CustomerId, outputs.BffAppRegId,
                pending.GrantedCount, pending.ExpectedCount);

            var pendingReplace = await _repository.ReplaceRunAsync(run, etag, cancellationToken)
                .ConfigureAwait(false);
            return HandlePendingReplace(pendingReplace, run, idempotencyKey);
        }

        // Verified — H3 completed its full job. Populate the RunParameters
        // .Secrets refs H4's FromRunParameters resolver (task 126) consumes
        // for BFF-API-ClientId / BFF-API-Audience / BFF-API-ClientSecret (task
        // 129's manifest.yaml reclassification — H3 is the documented value
        // producer for the first two; ClientSecret's manifest entry is
        // from-existing-kv, satisfied identically).
        var verified = (AdminConsentVerificationResult.Verified)consentResult;
        var (vaultName, secretName) = ParseKvUriReference(outputs.BffClientSecretKvUri);
        run.Parameters.Secrets[GraphAppRegistrationProvisioner.ClientIdSecretName] =
            new KeyVaultSecretRef(vaultName, GraphAppRegistrationProvisioner.ClientIdSecretName);
        run.Parameters.Secrets[GraphAppRegistrationProvisioner.AudienceSecretName] =
            new KeyVaultSecretRef(vaultName, GraphAppRegistrationProvisioner.AudienceSecretName);
        run.Parameters.Secrets[secretName] = new KeyVaultSecretRef(vaultName, secretName);

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
        run.GateStates[EntraAppRegGates.AdminConsent] = new GateEntry
        {
            Status = GateState.Verified,
            VerifiedAt = completedAt,
            VerifierHandler = HandlerIdentifier,
            Evidence = verified.Evidence,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H3 success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: EntraAppRegRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H3 read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H3.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H3 success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: EntraAppRegRejectionCodes.RunDeletedDuringProvisioning,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H3 was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }

    /// <summary>
    /// Parses a canonical <c>@Microsoft.KeyVault(SecretUri=https://{vault}.
    /// vault.azure.net/secrets/{name}/)</c> reference into (vault, name).
    /// Exposed internal so unit tests can construct expected
    /// RunParameters.Secrets entries without duplicating the parse.
    /// </summary>
    internal static (string VaultName, string SecretName) ParseKvUriReference(string kvUriRef)
    {
        // https://{vault}.vault.azure.net/secrets/{name}/
        const string prefix = "@Microsoft.KeyVault(SecretUri=https://";
        var inner = kvUriRef[prefix.Length..].TrimEnd(')');
        var vaultHost = inner[..inner.IndexOf(".vault.azure.net/", StringComparison.Ordinal)];
        var afterSecrets = inner[(inner.IndexOf("/secrets/", StringComparison.Ordinal) + "/secrets/".Length)..];
        var secretName = afterSecrets.TrimEnd('/');
        return (vaultHost, secretName);
    }

    private HandlerResult HandlePendingReplace(
        ReplaceRunResult replace,
        ProvisioningRun run,
        string idempotencyKey)
    {
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H3 WaitingOnGate write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: EntraAppRegRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H3 read + WaitingOnGate write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H3.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: EntraAppRegRejectionCodes.RunDeletedDuringProvisioning,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H3 was writing WaitingOnGate.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
