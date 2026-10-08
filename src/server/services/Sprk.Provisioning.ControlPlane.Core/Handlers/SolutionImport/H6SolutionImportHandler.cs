// -----------------------------------------------------------------------------
// H6SolutionImportHandler.cs
//
// L2 CONTROL-PLANE H6 solution-import handler (task 049; T218b).
//
// PURPOSE:
//   Imports the ONE Spaarke Dataverse package, SpaarkeMaster, into the
//   customer's Dataverse environment adopted by H5 — managed by default,
//   unmanaged only when the run's solutionPackageType says so (ADR-027 §3-§4,
//   amended 2026-10-07, owner D8). The importer refuses — nothing imported —
//   a managed↔unmanaged switch and a downgrade; an equal version is skipped;
//   an older managed package is upgraded with StageAndUpgradeAsync, an older
//   unmanaged one updated with ImportSolutionAsync. The verifier then proves
//   SpaarkeMaster is present with the requested type and the imported version.
//
// SPEC / DESIGN references:
//   - docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md — the package, its
//       release, first import and upgrade (runbook).
//   - projects/customer-provisioning-orchestration-r1/notes/t218-plan.md —
//       why one package (the former 9-entry catalog named 6 solutions that
//       existed nowhere).
//   - projects/customer-provisioning-orchestration-r1/spec.md §4D I1:
//       No hardcoded default tenant — MUST flow tenantId through explicitly.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.2 (v3.2):
//       Fire-and-forget handler execution model — L2 REST endpoints enqueue
//       + return 202; reconciler owns advancement.
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback.
//   - projects/customer-provisioning-orchestration-r1/design.md §6.2
//       interStepState field: `importedSolutions` is the H6-owned slot.
//   - .claude/adr/ADR-004-job-contract.md: idempotent + at-least-once safe.
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
//   │ solutionPackageType not managed/unmanaged   │ Resumable                 │
//   │ Auth failure                                │ Resumable                 │
//   │ Rate-limited                                │ Resumable                 │
//   │ Quota exhausted                             │ Resumable                 │
//   │ Package artifact unusable / missing         │ Resumable                 │
//   │ Env holds the other package type            │ Resumable (nothing done)  │
//   │ Env holds a newer version (downgrade)       │ Resumable (nothing done)  │
//   │ Import timeout                              │ Resumable (ImportTimeout) │
//   │ Failed StageAndUpgrade (holding solution)   │ QuarantineRequired        │
//   │ Unknown invocation failure (no partial)     │ Resumable                 │
//   │ Verifier: absent, wrong type or version     │ QuarantineRequired        │
//   │ Verifier: environment unreadable            │ Resumable                 │
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
//           IdempotencyKey == solimport-{customerId}-{packageType}). Match →
//           Success no-op (no importer / verifier invocation, no state mutation).
//           CompletedPhases is per run, so the key only has to be stable within
//           one run; a new (upgrade) run re-imports, and the importer itself
//           skips an equal version.
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
    public const string HandlerIdentifier = HandlerIds.H6;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Verified gate identifier for the H6 solution-import post-condition.</summary>
    public const string SolutionsImportedGateId = "h6-solutions-imported";

    private readonly IProvisioningRunRepository _repository;
    private readonly ISolutionImporter _importer;
    private readonly ISolutionVerifier _verifier;
    private readonly IRequiredApplicationsInstaller _requiredAppsInstaller;
    private readonly IRequiredApplicationsManifest _requiredAppsManifest;
    private readonly IOrgSettingsContractApplier _orgSettingsApplier;
    private readonly IOrgSettingsContractManifest _orgSettingsManifest;
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
    ///
    /// HANDLER-07 + HANDLER-08 (Wave 2 pre-dispatch remediation 2026-08-27) —
    /// F13 + F14 verbatim absorption: the required-applications installer
    /// (msft_PowerBI_Anchor) + Org Settings contract applier
    /// (maxuploadfilesize=25MB) both run BEFORE
    /// the package import so a missing pre-req fails H6 fast
    /// with a specific rejection code instead of surfacing 5 min into the
    /// solution import as MissingDependency / "Webresource content size is
    /// too big".
    /// </summary>
    public H6SolutionImportHandler(
        IProvisioningRunRepository repository,
        ISolutionImporter importer,
        ISolutionVerifier verifier,
        IRequiredApplicationsInstaller requiredAppsInstaller,
        IRequiredApplicationsManifest requiredAppsManifest,
        IOrgSettingsContractApplier orgSettingsApplier,
        IOrgSettingsContractManifest orgSettingsManifest,
        IOptions<SolutionImportOptions> options,
        TimeProvider timeProvider,
        ILogger<H6SolutionImportHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(requiredAppsInstaller);
        ArgumentNullException.ThrowIfNull(requiredAppsManifest);
        ArgumentNullException.ThrowIfNull(orgSettingsApplier);
        ArgumentNullException.ThrowIfNull(orgSettingsManifest);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _importer = importer;
        _verifier = verifier;
        _requiredAppsInstaller = requiredAppsInstaller;
        _requiredAppsManifest = requiredAppsManifest;
        _orgSettingsApplier = orgSettingsApplier;
        _orgSettingsManifest = orgSettingsManifest;
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
        // T218b: managed by default (stored at CreateRun); unmanaged only on explicit instruction.
        var packageType = IntakeParameterCatalog.ResolveSolutionPackageType(run.Parameters.NonSecret);
        if (!IntakeParameterCatalog.AllowedSolutionPackageTypes.Contains(packageType))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, SolutionImportRejectionCodes.PackageTypeInvalid,
                $"Run parameter '{IntakeParameterCatalog.SolutionPackageType}' is '{packageType}'; allowed values are " +
                $"{IntakeParameterCatalog.ManagedSolutionPackage} | {IntakeParameterCatalog.UnmanagedSolutionPackage}. " +
                "Nothing was imported.", cancellationToken).ConfigureAwait(false);
        }
        var managed = string.Equals(packageType, IntakeParameterCatalog.ManagedSolutionPackage, StringComparison.Ordinal);
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, packageType);

        // (2) Level-3 idempotency: durable no-op on duplicate within this run.
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

        // (4) §4D I1 tenant guard — H6 MUST NOT fall back to a default tenant.
        //     Fires BEFORE any Dataverse-side call so acceptance criterion
        //     (no importer call fired) holds by construction.
        var parameters = run.Parameters.NonSecret;
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            var diagnostic =
                "Run parameter 'tenantId' is required by H6 (§4D I1 no-hardcoded-tenant). " +
                "Upstream handler (H0.5 for Model 2, L2 endpoint for Model 1) MUST populate this before H6 dispatches. " +
                "The importer signs in to an explicit tenant — H6 refuses to guess.";
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
                "H5 (Dataverse env adoption) MUST complete before H6 dispatches. " +
                "Nothing was imported.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                SolutionImportRejectionCodes.MissingDataverseUrl, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (6) BFF app-reg id guard — sourced from InterStepState.BffAppRegId
        //     (written by H3). The importer signs in as this application.
        var clientId = run.InterStepState.BffAppRegId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            var diagnostic =
                "BFF app-reg id not present on ProvisioningRun.interStepState.bffAppRegId. " +
                "H3 (Entra app registration) MUST complete before H6 dispatches. " +
                "Nothing was imported.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                SolutionImportRejectionCodes.MissingBffAppRegId, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (7) Client-secret guard — resolved from bound options. Wave C5
        //     wires this to a Key Vault reference; wave C4 requires
        //     operator to populate SolutionImportOptions:ClientSecret at
        //     deploy time.
        //     A44.5 (task 205i): chain-aware — the secret is REQUIRED only
        //     when the FR-39 ordered credential chain's primary is
        //     ClientSecret (legacy/unconfigured default — prong-3 unmigrated
        //     env). Under the MI-FIC-first secret-free chain an EMPTY slot is
        //     the SIGNAL (auth-v4 §9.1) — the importer/verifier resolve their
        //     credential via WorkerDataverseCredentialFactory instead.
        var clientSecret = _options.ClientSecret;
        if (_options.Credentials.ClientSecretIsRequiredFirst(SolutionImportOptions.SectionName)
            && string.IsNullOrWhiteSpace(clientSecret))
        {
            var diagnostic =
                "SolutionImportOptions:ClientSecret is not populated and the FR-39 credential chain requires " +
                "it (primary = ClientSecret — the legacy/unconfigured default). " +
                "Wave C5 wires this to a Key Vault reference (@Microsoft.KeyVault(SecretUri=...)); " +
                "wave C4 requires operator to set the app-setting explicitly. " +
                "Secret-free environments instead configure " +
                "SolutionImportOptions:Credentials:Order:0=ManagedIdentityFederated (A44.5). " +
                "Nothing was imported.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                SolutionImportRejectionCodes.MissingClientSecret, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (7.5) HANDLER-07 (Wave 2 pre-dispatch remediation 2026-08-27) — F13:
        //       ensure the canonical Power Platform applications (e.g.
        //       msft_PowerBI_Anchor) are installed on the target env BEFORE
        //       the importer fires. Fresh Production-tier envs lack this by
        //       default → SpaarkeMaster env-var dep on powerbimashupparameter
        //       → MissingDependency 5 min into the import. Runs BEFORE the
        //       importer + BEFORE the org-settings apply (order matters —
        //       admin-plane apps + admin-plane settings can be applied
        //       independently, but co-locating both gates here keeps the
        //       pre-import surface small + explicit).
        try
        {
            var appsRequest = new RequiredApplicationsInstallRequest(
                TenantId: tenantId,
                ClientId: clientId,
                ClientSecret: clientSecret,
                TargetDataverseUrl: targetDataverseUrl,
                RequiredApplicationNames: _requiredAppsManifest.RequiredApplicationNames);
            var appsOutcome = await _requiredAppsInstaller
                .EnsureInstalledAsync(appsRequest, cancellationToken).ConfigureAwait(false);
            if (appsOutcome is RequiredApplicationsInstallOutcome.Failure appsFailure)
            {
                return await FailAsync(run, etag, FailureClass.Resumable,
                    SolutionImportRejectionCodes.MissingRequiredApplication, appsFailure.Diagnostic, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "H6 required-applications installer infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                SolutionImportRejectionCodes.MissingRequiredApplication,
                $"Required-applications installer infrastructure error: {ex.GetType().Name}: {ex.Message}.",
                cancellationToken).ConfigureAwait(false);
        }

        // (7.6) HANDLER-08 (Wave 2 pre-dispatch remediation 2026-08-27) — F14:
        //       apply the canonical Org Settings contract (e.g.
        //       maxuploadfilesize=25_600_000) BEFORE the importer fires.
        //       Fresh Production-tier envs default 5MB → UniversalDocumentUpload
        //       PCF bundle exceeds this → import fails 5 min in.
        try
        {
            var orgSettingsRequest = new OrgSettingsContractApplyRequest(
                TenantId: tenantId,
                ClientId: clientId,
                ClientSecret: clientSecret,
                TargetDataverseUrl: targetDataverseUrl,
                OrgSettings: _orgSettingsManifest.OrgSettings);
            var orgSettingsOutcome = await _orgSettingsApplier
                .ApplyAsync(orgSettingsRequest, cancellationToken).ConfigureAwait(false);
            if (orgSettingsOutcome is OrgSettingsContractOutcome.Failure orgFailure)
            {
                return await FailAsync(run, etag, FailureClass.Resumable,
                    SolutionImportRejectionCodes.OrgSettingsContractFailed, orgFailure.Diagnostic, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "H6 org-settings applier infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                SolutionImportRejectionCodes.OrgSettingsContractFailed,
                $"Org-settings applier infrastructure error: {ex.GetType().Name}: {ex.Message}.",
                cancellationToken).ConfigureAwait(false);
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
            TargetDataverseUrl: targetDataverseUrl,
            Managed: managed);

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
                $"Solution importer infrastructure error: {ex.GetType().Name}: {ex.Message}.";
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
        //     Independently confirms SpaarkeMaster is installed with the requested
        //     type + builds the Cosmos manifest with actual (version, solutionId).
        var verifyRequest = new SolutionVerificationRequest(
            TargetDataverseUrl: targetDataverseUrl,
            TenantId: tenantId,
            ClientId: clientId,
            Managed: managed,
            ClientSecret: clientSecret,
            ExpectedVersion: ((SolutionImportOutcome.Success)importOutcome).PackageVersion);

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
                "Import may have succeeded — the operator checks the environment's solutions + resumes.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                SolutionImportRejectionCodes.VerificationFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        if (verifyOutcome is SolutionVerificationOutcome.Unavailable unavailable)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, SolutionImportRejectionCodes.VerificationUnavailable,
                $"Post-import verification could not read the environment: {unavailable.Diagnostic}. Resume re-checks " +
                "(the import is skipped when the version is already installed).", cancellationToken).ConfigureAwait(false);
        }

        if (verifyOutcome is SolutionVerificationOutcome.Missing missing)
        {
            var diagnostic =
                $"Post-import verification FAILED: the importer reported Success but the environment does not hold " +
                $"{string.Join(", ", missing.MissingUniqueNames)} as the imported {packageType} package. Verifier detail: " +
                $"{missing.Diagnostic}. The operator reconciles the environment's solutions before resume.";
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
            run, etag, idempotencyKey, packageType, allPresent.ImportedRecords, envelope, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H6 idempotency key:
    /// <c>solimport-{customerId}-{packageType}</c> (T218b). Exposed <c>internal</c>
    /// so unit tests can construct expected keys without duplicating the format.
    /// Parity with H2a's <c>infra-{customerId}-{bicepVer}</c> +
    /// H12a's <c>h12a-{customerId}-{manifestHash}</c>.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, string packageType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageType);
        return $"solimport-{customerId}-{packageType}";
    }

    /// <summary>
    /// Maps <see cref="SolutionImportFailureKind"/> to the pair
    /// (rejectionCode, §4C failureClass). Exposed <c>internal</c> so tests
    /// can assert the mapping without depending on handler internals. Parity
    /// with the former H5 creator-failure mapping's shape.
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
            SolutionImportFailureKind.PackageTypeMismatch =>
                (SolutionImportRejectionCodes.PackageTypeMismatch, FailureClass.Resumable),
            SolutionImportFailureKind.DowngradeRefused =>
                (SolutionImportRejectionCodes.DowngradeRefused, FailureClass.Resumable),
            _ =>
                (SolutionImportRejectionCodes.ImportInvocationFailed, FailureClass.Resumable),
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
        string packageType,
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
        // Evidence captures the package version + solutionId + type so an
        // operator sees it without pulling logs.
        var evidencePayload = new
        {
            packageType,
            solutions = importedRecords.Select(r => new
            {
                uniqueName = r.SolutionUniqueName,
                version = r.Version,
                solutionId = r.SolutionId,
                isManaged = r.IsManaged,
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
