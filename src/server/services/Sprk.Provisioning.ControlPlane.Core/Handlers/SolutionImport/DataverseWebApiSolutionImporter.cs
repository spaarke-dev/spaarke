// -----------------------------------------------------------------------------
// DataverseWebApiSolutionImporter.cs
//
// Production <see cref="ISolutionImporter"/> implementation (task 141; T218b) —
// pure HttpClient import of the ONE Spaarke package, SpaarkeMaster, via the
// Dataverse Web API `ImportSolutionAsync` / `StageAndUpgradeAsync` actions,
// polling `asyncoperations` for completion and `importjobs` for the detail.
//
// T218b (ADR-027 §3-§4, amended 2026-10-07): the run's package type picks the
// managed (default) or unmanaged blob; the importer reads the installed
// SpaarkeMaster FIRST and refuses — nothing imported — a managed↔unmanaged
// switch and a downgrade; an equal version is skipped; an older managed package
// is upgraded with StageAndUpgradeAsync, an older unmanaged one updated with
// ImportSolutionAsync. A failed read of the installed solution is a failure,
// never "assume a fresh install".
//
// ARTIFACT PACKAGING (DS-1b §1 H6 row): the package zips are resolved from the
// `provisioning-artifacts` blob container through its manifest
// (`dataverse-solutions-latest.json`:
// {"solutions":{"SpaarkeMaster":{"version","managedBlobName","unmanagedBlobName"}}},
// published by publish-dataverse-solutions-manifest.yml — T218d). This importer
// never reads a local filesystem path for a solution ZIP.
//
// GROUND-TRUTHED WEB API SHAPES (Wave G-1..G-4 discipline — verify, don't
// guess; WebFetch against learn.microsoft.com 2026-08-20):
//   - ImportSolutionAsync / StageAndUpgradeAsync (T218b review, verified against
//     learn.microsoft.com 2026-10-07) take OverwriteUnmanagedCustomizations,
//     PublishWorkflows, CustomizationFile, ImportJobId (CLIENT-GENERATED) and
//     SkipProductUpdateDependencies; ImportSolutionAsync also HoldingSolution.
//     Both return { AsyncOperationId, ImportJobKey } at once. The synchronous
//     ImportSolution / StageAndUpgrade actions used before held the HTTP request
//     for the whole import and outlived the client timeout on a package this size.
//   - Absent, or an older UNMANAGED package → ImportSolutionAsync (an unmanaged
//     package has no holding-solution mechanism; its update overwrites the
//     components). Older MANAGED package → StageAndUpgradeAsync.
//   - COMPLETION (ISS-032): the IMPORT decides, not the async operation. The
//     importer polls `importjobs({ImportJobId})` (`completedon`, `progress`,
//     result `data` XML) and `msdyn_solutionhistories` for the solution (status 0
//     Started / 1 Completed / 2 Queued, result Success/Failure). The async
//     operation (`asyncoperations({AsyncOperationId})`: statecode 3 = Completed;
//     statuscode 30 = Succeeded, 31 = Failed, 32 = Canceled — Microsoft's
//     documented asynchronous-import pattern, /power-platform/alm/solution-async)
//     is ADVISORY: Dataverse retries a long-running operation, the retry fails
//     ("Cannot start another [Import] ... previous [Import] running") while the
//     first attempt is still importing, and that first attempt then succeeds. A
//     Failed operation beside a running import job, or an open history entry,
//     does not end the wait. See PollImportAsync for the full decision table.
//     BEFORE starting, FindInFlightImportAsync looks for an import of the
//     solution already running (an incomplete import job, or an open history
//     entry) and waits on that one instead of starting a second. Throttling
//     (429), 5xx, an expired token (401 — a fresh token is requested for every
//     poll), a not-yet-visible row (404) and transport errors are retried until
//     ImportTimeout; only a failure Dataverse REPORTED counts as a failure
//     (and only that, for a managed upgrade, is PartialImport). A timeout names
//     the import job and async operation ids in its diagnostic (the run's
//     errorDetail), so a resume attaches to that import.
//   - importjobs.data is an XML text field (Format=Text, MaxLength
//     1,073,741,823) containing solution-import result nodes. Exact schema
//     is not published in a single canonical Microsoft Learn reference page;
//     this importer defensively scans for any element carrying a `result`
//     attribute equal to "failure" (collecting `errortext` attributes as the
//     diagnostic) or "warning" (surfaced but non-fatal) — see
//     EvaluateImportJobData. An unparseable/empty data field does NOT
//     silently mask a failure: it is treated as a provisional success WHOSE
//     diagnostic explicitly says so, and the SEPARATE ISolutionVerifier
//     (DataverseWebApiSolutionVerifier, called by the handler immediately
//     after this importer returns) independently re-confirms via a fresh
//     `solutions` GET — the two-collaborator split (per ISolutionVerifier.cs's
//     own "WHY A SEPARATE VERIFIER" doc) is exactly the defense-in-depth this
//     design leans on for the data-field ambiguity.
//
// CREDENTIAL (task 142 / DS-4 customer-env auth model — NOT a new S2S
// secret): OAuth2 client-credentials via Azure.Identity.ClientSecretCredential
// using the BFF Entra app-reg's ClientId (H3 output) + ClientSecret (resolved
// from SolutionImportOptions.ClientSecret, wired to the H4-populated KV
// BFF-API-ClientSecret secret per task 142's H7 precedent — SAME identity +
// pattern H7's DataverseWebApiEnvVarValuesWriter uses). Token audience is the
// target Dataverse env's origin + `/.default` (Dataverse Web API convention,
// parity with DataverseWebApiHealthProbe / DataverseWebApiEnvVarValuesWriter).
// Blob-artifact resolution (fetching the package ZIP from Spaarke's OWN
// provisioning-artifacts storage account) uses a SEPARATE, unrelated
// credential — the shared L2 UAMI TokenCredential singleton (ADR-028
// MI-outbound), injected via the pre-constructed BlobContainerClient (parity
// with H9's ArtifactManifestVerifier/BlobArtifactDownloader registration
// pattern in Worker/Program.cs). These are two intentionally distinct
// credentials serving two distinct trust boundaries — never conflated.
//
// DISPATCHER TIMEOUT SIZING (POML constraint / DS-2b §1.2 — this is the
// fleet's longest-running handler): SolutionImportOptions.ImportTimeout
// (default 120 min since ISS-032; a real SpaarkeMaster upgrade took 73) remains the OVERALL
// deadline this importer respects via TimeProvider-based polling (never
// Stopwatch/Task.Delay wall-clock — docs/standards/TEST-ARCHITECTURE.md).
// The POML's own <escalation> trigger (a live import showing a materially
// different completion-time distribution than assumed) cannot be resolved
// statically in this sandbox — flagged here for the required live-dev
// re-validation pass, not silently assumed correct.
//
// ADR-038 TEST-BOUNDARY DESIGN: typed-HttpClient injection (constructor
// takes HttpClient directly — same pattern as BapRestEnvironmentCreator,
// task 140) + an internal Func<string,string,string,TokenCredential> test
// seam so unit tests never invoke the real ClientSecretCredential network
// path. Tests use a hand-rolled HttpMessageHandler subclass (this project's
// shared ArmSdkTestFakes.NewHandler/FakeArmHttpMessageHandler helpers,
// extended for the raw-JSON Dataverse Web API shape) — never
// Mock<HttpMessageHandler> (banned per ADR-038 / testing.md).
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// <see cref="ISolutionImporter"/> implementation that imports the SpaarkeMaster package (managed or unmanaged, per
/// run) via Dataverse Web API <c>ImportSolutionAsync</c>/<c>StageAndUpgradeAsync</c> + <c>asyncoperations</c> polling,
/// resolving the ZIP from the versioned blob-artifact manifest (never a local filesystem path).
/// </summary>
public sealed class DataverseWebApiSolutionImporter : ISolutionImporter
{
    /// <summary>
    /// Named HttpClient for outbound Dataverse Web API calls (parity with
    /// DataverseWebApiHealthProbe (H5) / DataverseWebApiEnvVarValuesWriter
    /// (H7) — this collaborator's public constructor takes an extra
    /// <see cref="BlobContainerClient"/> dependency the DI container does not
    /// register as its own service (established "self-contained against
    /// sibling handler ports" convention), so it is wired via a manual
    /// factory lambda + this named client rather than
    /// <c>AddHttpClient&lt;TInterface,TImpl&gt;()</c> typed-client resolution).
    /// </summary>
    public const string HttpClientName = "H6.DataverseWebApiSolutionImporter";

    /// <summary><c>asyncoperation.statecode</c> Completed.</summary>
    private const int AsyncOperationCompleted = 3;

    /// <summary><c>asyncoperation.statuscode</c> Succeeded (31 = Failed, 32 = Canceled).</summary>
    private const int AsyncOperationSucceeded = 30;

    /// <summary>Manifest property naming the managed package blob (T218b).</summary>
    internal const string ManagedBlobProperty = "managedBlobName";

    /// <summary>Manifest property naming the unmanaged package blob (T218b).</summary>
    internal const string UnmanagedBlobProperty = "unmanagedBlobName";

    /// <summary>Allowance between this worker's clock and Dataverse's when comparing solution-history times.</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    private const string ODataVersion = "4.0";
    private const int DiagnosticTailBudget = 800;

    private readonly HttpClient _httpClient;
    private readonly BlobContainerClient _artifactsContainer;
    private readonly SolutionImportOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, string, string, TokenCredential> _credentialFactory;
    private readonly ILogger<DataverseWebApiSolutionImporter> _logger;

    /// <summary>
    /// Constructs the importer bound to a typed <see cref="HttpClient"/>
    /// (production via <c>services.AddSingleton</c> factory in
    /// Worker/Program.cs). A44.5 (task 205i): the credential is selected by
    /// the FR-39 ordered chain (<paramref name="credentialFactory"/> over
    /// <c>SolutionImportOptions:Credentials:Order</c> — MI-FIC first on
    /// secret-free envs; ClientSecret only for prong-3 unmigrated envs),
    /// mirroring master's <c>DataverseServiceClientImpl</c> migration. Raw
    /// <c>ClientSecretCredential</c> construction lives ONLY in the factory's
    /// fallback branch — never on the secret-free branch.
    /// </summary>
    public DataverseWebApiSolutionImporter(
        HttpClient httpClient,
        BlobContainerClient artifactsContainer,
        IOptions<SolutionImportOptions> options,
        WorkerDataverseCredentialFactory credentialFactory,
        ILogger<DataverseWebApiSolutionImporter> logger)
        : this(
            httpClient,
            artifactsContainer,
            options,
            logger,
            TimeProvider.System,
            (tenantId, clientId, clientSecret) => credentialFactory
                .Create(
                    options.Value.Credentials,
                    SolutionImportOptions.SectionName,
                    tenantId,
                    clientId,
                    string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret)
                .Credential)
    {
        ArgumentNullException.ThrowIfNull(credentialFactory);
    }

    /// <summary>
    /// Test seam constructor — injects a <paramref name="credentialFactory"/>
    /// (so tests never invoke a real credential network path)
    /// alongside a fake-transport <see cref="HttpClient"/> + <see cref="TimeProvider"/>.
    /// The delegate's third parameter is the resolved secret slot value —
    /// EMPTY STRING on secret-free environments (A44.5).
    /// </summary>
    internal DataverseWebApiSolutionImporter(
        HttpClient httpClient,
        BlobContainerClient artifactsContainer,
        IOptions<SolutionImportOptions> options,
        ILogger<DataverseWebApiSolutionImporter> logger,
        TimeProvider timeProvider,
        Func<string, string, string, TokenCredential> credentialFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(artifactsContainer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _httpClient = httpClient;
        _artifactsContainer = artifactsContainer;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
        _credentialFactory = credentialFactory;
    }

    /// <inheritdoc/>
    public async Task<SolutionImportOutcome> ImportAsync(
        SolutionImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientId);
        // A44.5: ClientSecret deliberately NOT required — empty on secret-free
        // envs (the signal, §9.1); the FR-39 credential factory selects MI-FIC.
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetDataverseUrl);

        if (!Uri.TryCreate(request.TargetDataverseUrl, UriKind.Absolute, out var envUri))
        {
            return new SolutionImportOutcome.Failure(
                SolutionImportFailureKind.UnknownInvocationFailure,
                $"Target Dataverse URL '{request.TargetDataverseUrl}' is not a valid absolute URI.");
        }

        var packageType = PackageTypeName(request.Managed);

        // (0) The published package entry, read ONCE up front. No manifest, no SpaarkeMaster entry, or no blob for the
        // requested type is a hard MissingSolutionZips failure — H6 never falls back to the other type or a local path.
        PackageManifestEntry entry;
        try
        {
            entry = await LoadPackageEntryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new SolutionImportOutcome.Failure(
                SolutionImportFailureKind.MissingSolutionZips,
                $"Solution artifact manifest '{_options.SolutionArtifactManifestBlobName}' not found in the " +
                "'provisioning-artifacts' blob container. Publish the package (publish-dataverse-solutions-manifest.yml) " +
                "and resume — H6 never reads a local path.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new SolutionImportOutcome.Failure(
                SolutionImportFailureKind.MissingSolutionZips,
                $"Solution artifact manifest '{_options.SolutionArtifactManifestBlobName}' could not be used: " +
                $"{ex.GetType().Name}: {ex.Message}.");
        }

        var blobName = request.Managed ? entry.ManagedBlobName : entry.UnmanagedBlobName;
        if (string.IsNullOrWhiteSpace(blobName))
        {
            return new SolutionImportOutcome.Failure(
                SolutionImportFailureKind.MissingSolutionZips,
                $"Solution artifact manifest '{_options.SolutionArtifactManifestBlobName}' has no {packageType} blob for " +
                $"'{SpaarkePackage.SolutionUniqueName}' ({(request.Managed ? ManagedBlobProperty : UnmanagedBlobProperty)}). " +
                "H6 never falls back to the other package type (ADR-027 §3).");
        }

        var scope = $"{new Uri(envUri, "/")}".TrimEnd('/') + "/.default";

        var tokenContext = new TokenRequestContext(new[] { scope });
        TokenCredential credential;
        AccessToken token;
        try
        {
            // A44.5: credential selection (FR-39 ordered chain in production;
            // fake in tests) can itself throw on an exhausted chain — classify
            // as AuthFailure (→ §4C Resumable at the handler), same boundary
            // as a failed token acquisition.
            credential = _credentialFactory(
                request.TenantId, request.ClientId, request.ClientSecret ?? string.Empty);
            // T186 audit: the first exchange for a just-created app / FIC can hit Entra propagation errors
            // (AADSTS70025 / 700211-3 / 700016); they are waited out within the budget, anything else rethrows here.
            token = await EntraPropagationRetry.GetTokenAsync(
                credential, tokenContext, _options.Credentials, _timeProvider, _logger, "H6 importer", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Prefix convention preserved (parity with the verifier's task-141
            // test contract); credential-SELECTION failures carry their own
            // "No credential could be selected …" inner message.
            _logger.LogWarning(ex, "H6 importer credential selection / token acquisition failed for env={EnvUrl}", request.TargetDataverseUrl);
            return new SolutionImportOutcome.Failure(
                SolutionImportFailureKind.AuthFailure,
                $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var deadline = _timeProvider.GetUtcNow() + _options.ImportTimeout;
        var bearerToken = token.Token;
        // The poll can outlive one token (up to ImportTimeout); each poll asks the credential again (it caches).
        async Task<string> FreshTokenAsync(CancellationToken ct)
            => (await credential.GetTokenAsync(tokenContext, ct).ConfigureAwait(false)).Token;

        // (1) What the environment holds now. A failed read is a failure, not "fresh install": guessing would bypass
        // the type-switch and downgrade refusals below.
        // T186 audit: this is the first Dataverse call this importer makes; the H10 application user / role may
        // not be effective yet, so a 401/403 here is retried within the shared budget before it is classified.
        var propagationGate = new DataverseAppUserPropagationGate(
            _options.Credentials, _timeProvider, _logger, "H6 importer");
        var installed = await GetInstalledPackageAsync(envUri, bearerToken, propagationGate, cancellationToken)
            .ConfigureAwait(false);
        if (installed is InstalledPackage.Unknown unknown)
        {
            return new SolutionImportOutcome.Failure(unknown.Kind, unknown.Diagnostic);
        }

        // (2) The package itself.
        byte[] zipBytes;
        try
        {
            var blob = _artifactsContainer.GetBlobClient(blobName);
            var download = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            zipBytes = download.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new SolutionImportOutcome.Failure(
                SolutionImportFailureKind.MissingSolutionZips,
                $"Artifact blob '{blobName}' ({packageType} {SpaarkePackage.SolutionUniqueName}) not found (HTTP 404).");
        }

        var packageVersion = entry.Version ?? TryReadSolutionVersionFromZip(zipBytes);
        if (string.IsNullOrWhiteSpace(packageVersion) || SpaarkePackage.CompareVersions(packageVersion, packageVersion) is null)
        {
            return new SolutionImportOutcome.Failure(
                SolutionImportFailureKind.MissingSolutionZips,
                $"The package version is unknown or not a version ('{packageVersion}'): the manifest entry's 'version' or " +
                $"'{blobName}''s solution.xml must be major.minor[.build[.revision]]. H6 cannot rule out a downgrade without it.");
        }

        // (3) Type-switch and downgrade refusals; an equal version is already done.
        var stageAndUpgrade = false;
        if (installed is InstalledPackage.Present present)
        {
            if (present.IsManaged != request.Managed)
            {
                return new SolutionImportOutcome.Failure(
                    SolutionImportFailureKind.PackageTypeMismatch,
                    $"The environment holds {SpaarkePackage.SolutionUniqueName} {present.Version} as " +
                    $"{PackageTypeName(present.IsManaged)}; the run asks for {packageType}. H6 never switches an " +
                    "environment's package type (ADR-027 §3, amended 2026-10-07): re-run with solutionPackageType = " +
                    $"{PackageTypeName(present.IsManaged)}, or convert the environment first (owner-approved, backed up).");
            }

            var comparison = SpaarkePackage.CompareVersions(present.Version, packageVersion);
            if (comparison is null)
            {
                return new SolutionImportOutcome.Failure(
                    SolutionImportFailureKind.UnknownInvocationFailure,
                    $"Cannot compare the installed {SpaarkePackage.SolutionUniqueName} version '{present.Version}' with " +
                    $"the package version '{packageVersion}'.");
            }
            if (comparison > 0)
            {
                return new SolutionImportOutcome.Failure(
                    SolutionImportFailureKind.DowngradeRefused,
                    $"The environment holds {SpaarkePackage.SolutionUniqueName} {present.Version}, newer than the " +
                    $"published package {packageVersion}. H6 never downgrades — publish the right package and resume.");
            }
            if (comparison == 0)
            {
                _logger.LogInformation(
                    "H6 {Solution} already at v{Version} ({PackageType}) — skipping import.",
                    SpaarkePackage.SolutionUniqueName, packageVersion, packageType);
                return new SolutionImportOutcome.Success(packageVersion);
            }
            // A managed upgrade stages a holding solution and applies it (StageAndUpgrade). An unmanaged package has
            // no holding-solution mechanism: its update is a plain import that overwrites the components.
            stageAndUpgrade = request.Managed;
        }

        // (3b) ISS-032: an import of this solution already running (a previous attempt that timed out, or another
        //      operator) is waited on, never raced by a second import — Dataverse refuses a second import while one runs,
        //      and that refusal used to mask the first one's success. Unreadable import jobs fail closed (nothing started).
        var inFlight = await FindInFlightImportAsync(envUri, bearerToken, cancellationToken).ConfigureAwait(false);
        if (inFlight is InFlightImport.Unreadable unreadable)
        {
            return new SolutionImportOutcome.Failure(unreadable.Kind, unreadable.Diagnostic);
        }

        if (inFlight is InFlightImport.Running running)
        {
            _logger.LogInformation(
                "H6 {Solution}: an import is already in flight (import job {ImportJobId}, started {Started:O}) — waiting on it, not starting another.",
                SpaarkePackage.SolutionUniqueName, running.ImportJobId, running.Since);
            var attachedResult = await PollImportAsync(
                envUri, FreshTokenAsync, asyncOperationId: null, running.ImportJobId, running.Since, requiredVersion: null, deadline, cancellationToken)
                .ConfigureAwait(false);
            if (attachedResult is ImportPollResult.Failed attachedFailed)
            {
                return new SolutionImportOutcome.Failure(
                    stageAndUpgrade && attachedFailed.ReportedByDataverse ? SolutionImportFailureKind.PartialImport : attachedFailed.Kind,
                    attachedFailed.Diagnostic);
            }

            // That import finished well. It may not be the version this run wants, so re-read before claiming success.
            var after = await GetInstalledPackageAsync(envUri, bearerToken, propagationGate, cancellationToken).ConfigureAwait(false);
            if (after is InstalledPackage.Present nowPresent
                && nowPresent.IsManaged == request.Managed
                && SpaarkePackage.CompareVersions(nowPresent.Version, packageVersion) == 0)
            {
                return new SolutionImportOutcome.Success(packageVersion);
            }
            return new SolutionImportOutcome.Failure(
                SolutionImportFailureKind.UnknownInvocationFailure,
                $"The import that was already running (import job {running.ImportJobId:D}) finished, but {SpaarkePackage.SolutionUniqueName} " +
                $"is not at the package version {packageVersion} ({DescribeInstalled(after)}). Nothing was started; resume re-reads the environment.");
        }

        // (4) ImportSolutionAsync (absent, or an older unmanaged package) or StageAndUpgradeAsync (older managed
        //     package). The *Async actions return at once with an async operation; the synchronous actions would hold
        //     the HTTP request for the whole import and outlive the client timeout on a package this size.
        var importJobId = Guid.NewGuid();
        var importStartedAt = _timeProvider.GetUtcNow();
        var invokeResult = await InvokeImportActionAsync(
            envUri, bearerToken, zipBytes, stageAndUpgrade, importJobId, cancellationToken)
            .ConfigureAwait(false);

        if (invokeResult is ActionInvokeResult.Failed invokeFailed)
        {
            // The POST was refused — nothing started, so never a partial state.
            return new SolutionImportOutcome.Failure(invokeFailed.Kind, invokeFailed.Diagnostic);
        }

        var started = (ActionInvokeResult.Started)invokeResult;
        var pollResult = await PollImportAsync(
            envUri, FreshTokenAsync, started.AsyncOperationId, importJobId, importStartedAt - ClockSkew, packageVersion, deadline, cancellationToken)
            .ConfigureAwait(false);

        if (pollResult is ImportPollResult.Failed pollFailed)
        {
            // Only a failure Dataverse REPORTED for a started StageAndUpgrade may leave the holding solution behind →
            // PartialImport (QuarantineRequired). A timeout or an unreadable status is never promoted: no confirmed
            // failure, resume is safe.
            var kind = stageAndUpgrade && pollFailed.ReportedByDataverse
                ? SolutionImportFailureKind.PartialImport
                : pollFailed.Kind;
            return new SolutionImportOutcome.Failure(kind, pollFailed.Diagnostic);
        }

        return new SolutionImportOutcome.Success(packageVersion);
    }

    private async Task<PackageManifestEntry> LoadPackageEntryAsync(CancellationToken cancellationToken)
    {
        var manifestBlob = _artifactsContainer.GetBlobClient(_options.SolutionArtifactManifestBlobName);
        var response = await manifestBlob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        return ParsePackageEntry(response.Value.Content.ToString(), _options.SolutionArtifactManifestBlobName);
    }

    /// <summary>
    /// Reads the SpaarkeMaster entry of the artifact manifest:
    /// <c>{"solutions":{"SpaarkeMaster":{"version","managedBlobName","unmanagedBlobName"}}}</c> (T218b; written by
    /// publish-dataverse-solutions-manifest.yml). Throws <see cref="InvalidOperationException"/> when the entry is
    /// missing. Exposed <c>internal</c> for direct unit testing.
    /// </summary>
    internal static PackageManifestEntry ParsePackageEntry(string json, string manifestName)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("solutions", out var solutionsElement)
            || solutionsElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Manifest '{manifestName}' is missing a 'solutions' object.");
        }
        if (!solutionsElement.TryGetProperty(SpaarkePackage.SolutionUniqueName, out var package)
            || package.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Manifest '{manifestName}' has no '{SpaarkePackage.SolutionUniqueName}' entry — the published artifact " +
                "predates the one-package format (T218).");
        }

        return new PackageManifestEntry(
            Version: ReadString(package, "version"),
            ManagedBlobName: ReadString(package, ManagedBlobProperty),
            UnmanagedBlobName: ReadString(package, UnmanagedBlobProperty));

        static string? ReadString(JsonElement element, string name)
            => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
               && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim()
                : null;
    }

    private async Task<InstalledPackage> GetInstalledPackageAsync(
        Uri envUri, string bearerToken, DataverseAppUserPropagationGate propagationGate, CancellationToken cancellationToken)
    {
        var uri = new Uri(envUri,
            $"/api/data/v9.2/solutions?$select=uniquename,version,ismanaged&$filter=uniquename eq '{SpaarkePackage.SolutionUniqueName}'");
        try
        {
            using var response = await propagationGate
                .SendAsync(_httpClient, () => BuildRequest(HttpMethod.Get, uri, bearerToken), cancellationToken)
                .ConfigureAwait(false);
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new InstalledPackage.Unknown(
                    ClassifyHttpFailure(response.StatusCode, bodyText),
                    $"Reading the installed {SpaarkePackage.SolutionUniqueName} returned {(int)response.StatusCode}: " +
                    $"{Truncate(bodyText, DiagnosticTailBudget)}. Nothing was imported.");
            }
            return ParseInstalledPackage(bodyText);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new InstalledPackage.Unknown(
                SolutionImportFailureKind.UnknownInvocationFailure,
                $"Reading the installed {SpaarkePackage.SolutionUniqueName} failed: {ex.GetType().Name}: {ex.Message}. " +
                "Nothing was imported.");
        }
    }

    /// <summary>
    /// Parses the filtered <c>solutions</c> response into what the environment holds. Exposed <c>internal</c> for
    /// direct unit testing.
    /// </summary>
    internal static InstalledPackage ParseInstalledPackage(string bodyText)
    {
        using var doc = JsonDocument.Parse(bodyText);
        if (doc.RootElement.TryGetProperty("value", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in array.EnumerateArray())
            {
                if (element.TryGetProperty("uniquename", out var un) && un.ValueKind == JsonValueKind.String
                    && string.Equals(un.GetString(), SpaarkePackage.SolutionUniqueName, StringComparison.OrdinalIgnoreCase))
                {
                    var version = element.TryGetProperty("version", out var ver) && ver.ValueKind == JsonValueKind.String
                        ? ver.GetString()!
                        : string.Empty;
                    var isManaged = element.TryGetProperty("ismanaged", out var managed)
                                    && managed.ValueKind == JsonValueKind.True;
                    return new InstalledPackage.Present(version, isManaged);
                }
            }
        }
        return new InstalledPackage.Absent();
    }

    private async Task<ActionInvokeResult> InvokeImportActionAsync(
        Uri envUri,
        string bearerToken,
        byte[] zipBytes,
        bool stageAndUpgrade,
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        var actionName = stageAndUpgrade ? "StageAndUpgradeAsync" : "ImportSolutionAsync";
        var actionUri = new Uri(envUri, $"/api/data/v9.2/{actionName}");
        var solutionName = SpaarkePackage.SolutionUniqueName;

        var body = new Dictionary<string, object?>
        {
            ["OverwriteUnmanagedCustomizations"] = true,
            ["PublishWorkflows"] = true,
            ["CustomizationFile"] = Convert.ToBase64String(zipBytes),
            ["ImportJobId"] = importJobId,
            ["SkipProductUpdateDependencies"] = false,
        };
        if (!stageAndUpgrade)
        {
            body["HoldingSolution"] = false;
        }

        using var httpRequest = BuildRequest(HttpMethod.Post, actionUri, bearerToken);
        httpRequest.Content = JsonContent.Create(body);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ActionInvokeResult.Failed(
                SolutionImportFailureKind.Timeout,
                $"{actionName} POST for '{solutionName}' timed out after {_options.DataverseWebApiRequestTimeout}.");
        }
        catch (HttpRequestException ex)
        {
            return new ActionInvokeResult.Failed(
                SolutionImportFailureKind.UnknownInvocationFailure,
                $"{actionName} POST for '{solutionName}' infrastructure error: {ex.Message}");
        }

        using (response)
        {
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new ActionInvokeResult.Failed(
                    ClassifyHttpFailure(response.StatusCode, bodyText),
                    $"{actionName} POST for '{solutionName}' failed: {(int)response.StatusCode} " +
                    $"{response.ReasonPhrase}. Body: {Truncate(bodyText, DiagnosticTailBudget)}");
            }

            var asyncOperationId = TryReadAsyncOperationId(bodyText);
            return asyncOperationId is { } id
                ? new ActionInvokeResult.Started(id)
                : new ActionInvokeResult.Failed(
                    SolutionImportFailureKind.UnknownInvocationFailure,
                    $"{actionName} for '{solutionName}' returned no AsyncOperationId: {Truncate(bodyText, DiagnosticTailBudget)}");
        }
    }

    /// <summary>Reads <c>AsyncOperationId</c> from an *Async action response. Exposed <c>internal</c> for unit testing.</summary>
    internal static Guid? TryReadAsyncOperationId(string bodyText)
    {
        try
        {
            using var doc = JsonDocument.Parse(bodyText);
            return doc.RootElement.TryGetProperty("AsyncOperationId", out var idEl)
                   && idEl.ValueKind == JsonValueKind.String
                   && Guid.TryParse(idEl.GetString(), out var id)
                ? id
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Waits for the import to finish and decides the outcome from the IMPORT itself (ISS-032): the import job
    /// (<c>importjobs</c>: <c>completedon</c>, result <c>data</c> XML) and the solution-history entries
    /// (<c>msdyn_solutionhistories</c>: status, result) are the completion signal. The async operation's status is
    /// advisory — Dataverse can retry the operation, and the retry fails ("Cannot start another [Import] … previous
    /// [Import] running") while the first attempt is still importing and then succeeds. An async operation that reports
    /// Failed while the import job is still running, or a solution-history entry is still open, therefore does not end
    /// the wait; it ends only when the job completes, the data shows a failure, or nothing is running any more: the job
    /// is gone or unreadable AND the history is readable with no open entry (an unreadable history is never "nothing
    /// open"). A readable, uncompleted job is never ended on the
    /// <see cref="SolutionImportOptions.AsyncOperationFailureGrace"/>; the overall deadline bounds a dead one.
    /// For an import this run started, the solution history decides only once the job has been seen (or the operation is
    /// terminal) and only through entries of the package version being imported.
    /// Throttling (429), server errors (5xx), an expired token (401), a not-yet-visible row (404) and transport errors
    /// are retried until <paramref name="deadline"/>; only a failure Dataverse REPORTED is
    /// <see cref="ImportPollResult.Failed.ReportedByDataverse"/>.
    /// </summary>
    /// <param name="asyncOperationId">The async operation this run started; <c>null</c> when attaching to an import already running.</param>
    /// <param name="importJobId">The import job to read; <c>null</c> when attaching to a running import whose job is not known yet (found by solution name).</param>
    /// <param name="since">Solution-history entries that started before this are not this import's.</param>
    /// <param name="requiredVersion">
    /// The package version this run is importing: solution-history entries of another version are not this import's.
    /// <c>null</c> when attaching to an import already running (its version is checked by the caller afterwards).
    /// </param>
    private async Task<ImportPollResult> PollImportAsync(
        Uri envUri,
        Func<CancellationToken, Task<string>> freshToken,
        Guid? asyncOperationId,
        Guid? importJobId,
        DateTimeOffset since,
        string? requiredVersion,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        var solutionName = SpaarkePackage.SolutionUniqueName;
        // An import THIS run started has its own job row and history entry only some seconds after the POST. Until the
        // job has been seen (or the async operation is terminal) the history inside the skew window may be a previous
        // attempt's, so it cannot decide. An attach (no async operation of ours) trusts the history from the start.
        var ownImport = asyncOperationId is not null;
        var jobSeen = false;
        DateTimeOffset? failedOperationSeenAt = null;
        var failedOperationLogged = false;
        double? lastProgress = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var bearerToken = await freshToken(cancellationToken).ConfigureAwait(false);

                // The async operation (advisory).
                AsyncOperationState operation = AsyncOperationState.Unknown;
                if (asyncOperationId is { } operationId)
                {
                    var operationUri = new Uri(envUri,
                        $"/api/data/v9.2/asyncoperations({operationId:D})?$select=statecode,statuscode,message");
                    using var request = BuildRequest(HttpMethod.Get, operationUri, bearerToken);
                    using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(bodyText);
                        var root = doc.RootElement;
                        if (ReadInt(root, "statecode") == AsyncOperationCompleted)
                        {
                            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                                ? m.GetString()
                                : null;
                            operation = new AsyncOperationState(true, ReadInt(root, "statuscode"), message);
                        }
                    }
                    else if (!IsTransientPollStatus(response.StatusCode))
                    {
                        return new ImportPollResult.Failed(
                            ClassifyHttpFailure(response.StatusCode, bodyText),
                            $"Async-operation poll for '{solutionName}' returned {(int)response.StatusCode}: " +
                            $"{Truncate(bodyText, DiagnosticTailBudget)}{IdsSuffix(importJobId, asyncOperationId)}",
                            ReportedByDataverse: false);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "H6 async-operation poll for {Solution} returned {StatusCode} — retrying.",
                            solutionName, (int)response.StatusCode);
                    }
                }

                // The import job and the solution history (the completion signal).
                if (importJobId is null)
                {
                    importJobId = await FindImportJobIdSinceAsync(envUri, bearerToken, since, cancellationToken).ConfigureAwait(false);
                }
                var job = importJobId is { } jobId
                    ? await ReadImportJobAsync(envUri, bearerToken, jobId, cancellationToken).ConfigureAwait(false)
                    : ImportJobSnapshot.Missing;
                lastProgress = job.Progress ?? lastProgress;
                jobSeen |= job.State is ImportJobState.Running or ImportJobState.Completed;
                var history = SummarizeHistory(
                    await ReadSolutionHistoryAsync(envUri, bearerToken, cancellationToken).ConfigureAwait(false), since, requiredVersion);

                var now = _timeProvider.GetUtcNow();

                // (1) The import job completed: its result data decides, whatever the async operation says.
                if (job.State == ImportJobState.Completed)
                {
                    var (completedOk, completedDiagnostic) = EvaluateImportJobData(job.Data);
                    if (completedOk)
                    {
                        LogSucceeded(operation, completedDiagnostic);
                        return new ImportPollResult.Succeeded();
                    }
                    return new ImportPollResult.Failed(
                        SolutionImportFailureKind.UnknownInvocationFailure,
                        $"Import of '{solutionName}' failed: import job {importJobId:D} completed with {completedDiagnostic}" +
                        OperationSuffix(operation),
                        ReportedByDataverse: true);
                }

                // (2) The async operation reached a terminal state.
                if (operation.Terminal)
                {
                    var (jobOk, jobDiagnostic) = EvaluateImportJobData(job.Data);
                    if (operation.StatusCode == AsyncOperationSucceeded)
                    {
                        if (jobOk)
                        {
                            LogSucceeded(operation, jobDiagnostic);
                            return new ImportPollResult.Succeeded();
                        }
                        return ReportedFailure(solutionName, operation, job, jobDiagnostic, importJobId, asyncOperationId);
                    }

                    // Failed or Canceled: advisory. The import job's own data showing a failure is conclusive.
                    if (!jobOk)
                    {
                        return ReportedFailure(solutionName, operation, job, jobDiagnostic, importJobId, asyncOperationId);
                    }

                    failedOperationSeenAt ??= now;

                    // The import is alive while its job is readable and not completed, or an entry of the solution
                    // history is open. An UNREADABLE history says nothing — it is never "nothing open" (one 429/5xx
                    // on that read used to end a still-running import as failed). Only the overall deadline ends a
                    // wait on a live import.
                    if (job.State == ImportJobState.Running || history.HasOpen || history.Unreadable)
                    {
                        if (!failedOperationLogged)
                        {
                            failedOperationLogged = true;
                            _logger.LogWarning(
                                "H6 async operation {AsyncOperationId} for {Solution} reports statuscode {StatusCode} while the import is not finished " +
                                "(job {JobState}, open history entry: {HasOpen}, history readable: {HistoryReadable}) — the operation status is advisory, waiting on the import.",
                                asyncOperationId, solutionName, operation.StatusCode, job.State, history.HasOpen, !history.Unreadable);
                        }
                    }
                    else if (job.State == ImportJobState.Unreadable)
                    {
                        // The job cannot be read, the history is readable and shows nothing open: allow the grace for
                        // the job to become readable, then the reported failure stands.
                        if (now - failedOperationSeenAt.Value >= _options.AsyncOperationFailureGrace)
                        {
                            return ReportedFailure(solutionName, operation, job, jobDiagnostic, importJobId, asyncOperationId);
                        }
                    }
                    else if (history.HasSuccess)
                    {
                        // No import job to read, but the solution history records the import as done.
                        LogSucceeded(operation, "solution history reports the import completed successfully");
                        return new ImportPollResult.Succeeded();
                    }
                    else
                    {
                        return ReportedFailure(solutionName, operation, job, jobDiagnostic, importJobId, asyncOperationId);
                    }
                }
                else if (job.State == ImportJobState.Missing && !history.HasOpen && (!ownImport || jobSeen))
                {
                    // (3) No readable job and the operation is not terminal (or is not ours to read): the history decides.
                    //     For an import this run started only once its job has been seen — before that, an entry inside
                    //     the skew window can belong to an earlier attempt.
                    if (history.HasSuccess)
                    {
                        LogSucceeded(operation, "solution history reports the import completed successfully");
                        return new ImportPollResult.Succeeded();
                    }
                    if (history.HasFailure)
                    {
                        return new ImportPollResult.Failed(
                            SolutionImportFailureKind.UnknownInvocationFailure,
                            $"Import of '{solutionName}' failed: solution history reports a failed import" +
                            (string.IsNullOrWhiteSpace(history.FailureText) ? string.Empty : $" ({Truncate(history.FailureText, DiagnosticTailBudget)})") +
                            IdsSuffix(importJobId, asyncOperationId),
                            ReportedByDataverse: true);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       or Azure.Identity.AuthenticationFailedException)
            {
                _logger.LogWarning(ex, "H6 import poll error for {Solution} — retrying.", solutionName);
            }

            var current = _timeProvider.GetUtcNow();
            if (current >= deadline)
            {
                return new ImportPollResult.Failed(
                    SolutionImportFailureKind.Timeout,
                    $"Import of '{solutionName}' did not complete within the configured ImportTimeout window " +
                    $"(deadline {deadline:O}){(lastProgress is { } p ? $"; last reported progress {p:0.#}%" : string.Empty)}. " +
                    $"The import may still be running in Dataverse: resume attaches to it and keeps waiting; it never starts a second import." +
                    IdsSuffix(importJobId, asyncOperationId),
                    ReportedByDataverse: false);
            }

            var remaining = deadline - current;
            var delay = _options.ImportJobPollInterval < remaining ? _options.ImportJobPollInterval : remaining;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void LogSucceeded(AsyncOperationState operation, string? diagnostic)
    {
        if (operation.Terminal && operation.StatusCode != AsyncOperationSucceeded)
        {
            _logger.LogInformation(
                "H6 import of {Solution} succeeded although its async operation reported statuscode {StatusCode}: {Diagnostic}",
                SpaarkePackage.SolutionUniqueName, operation.StatusCode, diagnostic);
        }
        else if (!string.IsNullOrEmpty(diagnostic))
        {
            _logger.LogInformation("H6 import of {Solution} completed: {Diagnostic}", SpaarkePackage.SolutionUniqueName, diagnostic);
        }
    }

    private static ImportPollResult.Failed ReportedFailure(
        string solutionName,
        AsyncOperationState operation,
        ImportJobSnapshot job,
        string jobDiagnostic,
        Guid? importJobId,
        Guid? asyncOperationId)
        => new(
            SolutionImportFailureKind.UnknownInvocationFailure,
            $"Import of '{solutionName}' completed with asyncoperation statuscode {operation.StatusCode}" +
            (string.IsNullOrWhiteSpace(operation.Message) ? string.Empty : $" ({Truncate(operation.Message, DiagnosticTailBudget)})") +
            $"; import job: {(job.Data is null ? "no import job detail available" : jobDiagnostic)}" +
            IdsSuffix(importJobId, asyncOperationId),
            ReportedByDataverse: true);

    private static string OperationSuffix(AsyncOperationState operation)
        => operation.Terminal ? $"; async operation statuscode {operation.StatusCode}" : string.Empty;

    /// <summary>
    /// The ids a resume (or an operator) needs to find the import that is or was running. Exposed <c>internal</c> for
    /// unit testing.
    /// </summary>
    internal static string IdsSuffix(Guid? importJobId, Guid? asyncOperationId)
        => $" [importJobId={(importJobId is { } j ? j.ToString("D") : "unknown")}; " +
           $"asyncOperationId={(asyncOperationId is { } a ? a.ToString("D") : "none")}]";

    /// <summary>
    /// Reads the import job: <c>completedon</c> set means it completed; <c>data</c> is the result XML. A 404 is "no such
    /// job (yet)"; any other unreadable answer is <see cref="ImportJobState.Unreadable"/> — never a verdict.
    /// </summary>
    private async Task<ImportJobSnapshot> ReadImportJobAsync(
        Uri envUri, string bearerToken, Guid importJobId, CancellationToken cancellationToken)
    {
        var uri = new Uri(envUri,
            $"/api/data/v9.2/importjobs({importJobId:D})?$select=completedon,startedon,progress,data");
        try
        {
            using var request = BuildRequest(HttpMethod.Get, uri, bearerToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return ImportJobSnapshot.Missing;
            }
            if (!response.IsSuccessStatusCode)
            {
                return ImportJobSnapshot.Unreadable;
            }
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(bodyText);
            var root = doc.RootElement;
            var completed = ReadDate(root, "completedon") is not null;
            var data = root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.String
                ? dataEl.GetString()
                : null;
            var progress = root.TryGetProperty("progress", out var progressEl) && progressEl.ValueKind == JsonValueKind.Number
                ? progressEl.GetDouble()
                : (double?)null;
            return new ImportJobSnapshot(completed ? ImportJobState.Completed : ImportJobState.Running, data, progress);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "H6 import-job read failed for {ImportJobId}.", importJobId);
            return ImportJobSnapshot.Unreadable;
        }
    }

    /// <summary>
    /// The newest import job of the solution created at or after <paramref name="since"/> (used when attaching to a
    /// running import whose job id is not known yet). <c>null</c> when none is visible or the read fails.
    /// </summary>
    private async Task<Guid?> FindImportJobIdSinceAsync(
        Uri envUri, string bearerToken, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var filter = $"solutionname eq '{SpaarkePackage.SolutionUniqueName}' and createdon ge {since.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
        var uri = new Uri(envUri,
            $"/api/data/v9.2/importjobs?$select=importjobid,createdon,startedon,completedon&$filter={Uri.EscapeDataString(filter)}" +
            "&$orderby=createdon desc&$top=1");
        try
        {
            using var request = BuildRequest(HttpMethod.Get, uri, bearerToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(bodyText);
            return ReadImportJobRows(doc.RootElement).FirstOrDefault()?.Id;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "H6 import-job lookup failed for {Solution}.", SpaarkePackage.SolutionUniqueName);
            return null;
        }
    }

    /// <summary>Import-job rows of an <c>importjobs</c> list response. Exposed <c>internal</c> for unit testing.</summary>
    internal static IReadOnlyList<ImportJobRow> ReadImportJobRows(JsonElement root)
    {
        var rows = new List<ImportJobRow>();
        if (root.TryGetProperty("value", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in array.EnumerateArray())
            {
                if (element.TryGetProperty("importjobid", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    && Guid.TryParse(idEl.GetString(), out var id))
                {
                    rows.Add(new ImportJobRow(
                        id,
                        ReadDate(element, "startedon") ?? ReadDate(element, "createdon"),
                        ReadDate(element, "completedon")));
                }
            }
        }
        return rows;
    }

    /// <summary>
    /// Looks for an import of the solution that is already running: an import job without <c>completedon</c>, or an
    /// open solution-history entry (an import that Dataverse has queued or started). Entries older than
    /// <see cref="SolutionImportOptions.InFlightImportMaxAge"/> are abandoned, not in flight. The import-job read must
    /// succeed (fail closed); the solution history is read best effort.
    /// </summary>
    private async Task<InFlightImport> FindInFlightImportAsync(
        Uri envUri, string bearerToken, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var oldest = now - _options.InFlightImportMaxAge;

        var filter = $"solutionname eq '{SpaarkePackage.SolutionUniqueName}' and completedon eq null";
        var uri = new Uri(envUri,
            $"/api/data/v9.2/importjobs?$select=importjobid,createdon,startedon,completedon&$filter={Uri.EscapeDataString(filter)}" +
            "&$orderby=createdon desc&$top=5");
        IReadOnlyList<ImportJobRow> jobs;
        try
        {
            using var request = BuildRequest(HttpMethod.Get, uri, bearerToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new InFlightImport.Unreadable(
                    ClassifyHttpFailure(response.StatusCode, bodyText),
                    $"Looking for an import of {SpaarkePackage.SolutionUniqueName} already in flight returned " +
                    $"{(int)response.StatusCode}: {Truncate(bodyText, DiagnosticTailBudget)}. Nothing was imported.");
            }
            using var doc = JsonDocument.Parse(bodyText);
            jobs = ReadImportJobRows(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new InFlightImport.Unreadable(
                SolutionImportFailureKind.UnknownInvocationFailure,
                $"Looking for an import of {SpaarkePackage.SolutionUniqueName} already in flight failed: " +
                $"{ex.GetType().Name}: {ex.Message}. Nothing was imported.");
        }

        var running = jobs.FirstOrDefault(j => j.CompletedOn is null && j.StartedOn is { } started && started >= oldest);
        foreach (var stale in jobs.Where(j => j.CompletedOn is null && !(j.StartedOn is { } s && s >= oldest)))
        {
            _logger.LogWarning(
                "H6 ignoring import job {ImportJobId} of {Solution}: no completedon, but older than {MaxAge} — treated as abandoned.",
                stale.Id, SpaarkePackage.SolutionUniqueName, _options.InFlightImportMaxAge);
        }
        if (running is not null)
        {
            return new InFlightImport.Running(running.Id, running.StartedOn!.Value - ClockSkew);
        }

        var entries = await ReadSolutionHistoryAsync(envUri, bearerToken, cancellationToken).ConfigureAwait(false);
        var open = entries?
            .Where(e => e.IsOpen && e.StartTime is { } t && t >= oldest)
            .OrderByDescending(e => e.StartTime)
            .FirstOrDefault();
        return open is not null
            ? new InFlightImport.Running(null, open.StartTime!.Value - ClockSkew)
            : new InFlightImport.None();
    }

    /// <summary>
    /// Reads the recent import entries of the solution from <c>msdyn_solutionhistories</c> (status 0 Started, 1
    /// Completed, 2 Queued; result true = Success). Best effort: <c>null</c> when unreadable — the import job is then the
    /// only signal.
    /// </summary>
    private async Task<IReadOnlyList<SolutionHistoryEntry>?> ReadSolutionHistoryAsync(
        Uri envUri, string bearerToken, CancellationToken cancellationToken)
    {
        var filter = $"msdyn_name eq '{SpaarkePackage.SolutionUniqueName}'";
        var uri = new Uri(envUri,
            "/api/data/v9.2/msdyn_solutionhistories?$select=msdyn_name,msdyn_operation,msdyn_suboperation,msdyn_status," +
            $"msdyn_result,msdyn_starttime,msdyn_endtime,msdyn_exceptionmessage,msdyn_solutionversion&$filter={Uri.EscapeDataString(filter)}" +
            "&$orderby=msdyn_starttime desc&$top=25");
        try
        {
            using var request = BuildRequest(HttpMethod.Get, uri, bearerToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "H6 solution-history read for {Solution} returned {StatusCode} — relying on the import job.",
                    SpaarkePackage.SolutionUniqueName, (int)response.StatusCode);
                return null;
            }
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(bodyText);
            return ParseSolutionHistory(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "H6 solution-history read failed for {Solution} — relying on the import job.", SpaarkePackage.SolutionUniqueName);
            return null;
        }
    }

    /// <summary>
    /// Import entries of the solution in a <c>msdyn_solutionhistories</c> list response. Exposed <c>internal</c> for
    /// unit testing.
    /// </summary>
    internal static IReadOnlyList<SolutionHistoryEntry> ParseSolutionHistory(JsonElement root)
    {
        var entries = new List<SolutionHistoryEntry>();
        if (root.TryGetProperty("value", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in array.EnumerateArray())
            {
                var name = element.TryGetProperty("msdyn_name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? nameEl.GetString()
                    : null;
                if (!string.Equals(name, SpaarkePackage.SolutionUniqueName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                // msdyn_operation 0 = Import; anything else (export, publish, uninstall) is not an import.
                var operation = ReadInt(element, "msdyn_operation");
                if (operation is not null && operation != 0)
                {
                    continue;
                }
                entries.Add(new SolutionHistoryEntry(
                    Status: ReadInt(element, "msdyn_status"),
                    Result: element.TryGetProperty("msdyn_result", out var resultEl) && resultEl.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? resultEl.GetBoolean()
                        : null,
                    StartTime: ReadDate(element, "msdyn_starttime"),
                    EndTime: ReadDate(element, "msdyn_endtime"),
                    ExceptionMessage: element.TryGetProperty("msdyn_exceptionmessage", out var exEl) && exEl.ValueKind == JsonValueKind.String
                        ? exEl.GetString()
                        : null,
                    SolutionVersion: element.TryGetProperty("msdyn_solutionversion", out var verEl) && verEl.ValueKind == JsonValueKind.String
                        ? verEl.GetString()
                        : null));
            }
        }
        return entries;
    }

    /// <summary>
    /// What the solution history says about the import that started at or after <paramref name="since"/>, limited to
    /// entries of <paramref name="requiredVersion"/> when given (an entry without a readable version never matches).
    /// An entry that failed does not outweigh one that succeeded (a retried operation leaves a failed entry beside the
    /// first attempt's). Unreadable history (<c>null</c> entries) is <see cref="HistorySummary.Unknown"/>, which is
    /// not "nothing open". Exposed <c>internal</c> for unit testing.
    /// </summary>
    internal static HistorySummary SummarizeHistory(IReadOnlyList<SolutionHistoryEntry>? entries, DateTimeOffset since, string? requiredVersion = null)
    {
        if (entries is null)
        {
            return HistorySummary.Unknown;
        }
        var mine = entries
            .Where(e => e.StartTime is { } t && t >= since
                        && (requiredVersion is null || SpaarkePackage.CompareVersions(e.SolutionVersion, requiredVersion) == 0))
            .ToList();
        return new HistorySummary(
            HasOpen: mine.Any(e => e.IsOpen),
            HasSuccess: mine.Any(e => e.Status == 1 && e.Result == true),
            HasFailure: mine.Any(e => e.Status == 1 && e.Result == false),
            FailureText: mine.FirstOrDefault(e => e.Status == 1 && e.Result == false)?.ExceptionMessage);
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string name)
        => element.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture,
               System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;

    private static string DescribeInstalled(InstalledPackage installed) => installed switch
    {
        InstalledPackage.Present p => $"installed {p.Version} {PackageTypeName(p.IsManaged)}",
        InstalledPackage.Absent => "not installed",
        InstalledPackage.Unknown u => $"installed state unreadable: {Truncate(u.Diagnostic, 200)}",
        _ => "unknown",
    };

    /// <summary>Poll statuses that are retried until the deadline. Exposed <c>internal</c> for unit testing.</summary>
    internal static bool IsTransientPollStatus(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.RequestTimeout
           || (int)statusCode == 429
           || (int)statusCode >= 500;

    private static int? ReadInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v)
            ? v
            : null;

    /// <summary>
    /// Parses <c>importjobs.data</c> (an XML text field — see file header for
    /// the ground-truthing note on its schema) for failure/warning result
    /// nodes. Exposed <c>internal</c> for direct unit testing.
    /// </summary>
    internal static (bool Success, string Diagnostic) EvaluateImportJobData(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return (true, "(no import job data returned — treated as success; the independent post-import verifier confirms.)");
        }

        try
        {
            var doc = XDocument.Parse(data);
            var failures = doc.Descendants()
                .Where(e => string.Equals((string?)e.Attribute("result"), "failure", StringComparison.OrdinalIgnoreCase))
                .Select(e => (string?)e.Attribute("errortext") ?? (string?)e.Attribute("result") ?? "unspecified failure")
                .Distinct()
                .ToList();
            var warnings = doc.Descendants()
                .Where(e => string.Equals((string?)e.Attribute("result"), "warning", StringComparison.OrdinalIgnoreCase))
                .Select(e => (string?)e.Attribute("errortext") ?? "unspecified warning")
                .Distinct()
                .ToList();

            if (failures.Count > 0)
            {
                var diag = $"{failures.Count} failure(s): {string.Join(" | ", failures)}"
                    + (warnings.Count > 0 ? $"; {warnings.Count} warning(s): {string.Join(" | ", warnings)}" : string.Empty);
                return (false, diag);
            }
            if (warnings.Count > 0)
            {
                return (true, $"succeeded with {warnings.Count} warning(s): {string.Join(" | ", warnings)}");
            }
            return (true, string.Empty);
        }
        catch (System.Xml.XmlException ex)
        {
            return (true,
                $"import job data could not be parsed as XML ({ex.Message}) — treated as provisional success; " +
                "the independent post-import verifier confirms.");
        }
    }

    /// <summary>
    /// Reads the SolutionManifest/Version value from a managed solution ZIP's
    /// solution.xml — C# port of the PS script's Get-SolutionVersionFromZip.
    /// Exposed <c>internal</c> for direct unit testing.
    /// </summary>
    internal static string? TryReadSolutionVersionFromZip(byte[] zipBytes)
    {
        try
        {
            using var ms = new MemoryStream(zipBytes);
            using var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            var entry = archive.Entries.FirstOrDefault(
                e => string.Equals(e.Name, "solution.xml", StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return null;
            }
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            return doc.Root?.Element("SolutionManifest")?.Element("Version")?.Value;
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Heuristic HTTP-response-shape classifier for Dataverse Web API
    /// failures. Exposed <c>internal</c> for direct unit testing.
    /// </summary>
    internal static SolutionImportFailureKind ClassifyHttpFailure(HttpStatusCode statusCode, string body)
    {
        var lower = (body ?? string.Empty).ToLowerInvariant();

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || lower.Contains("unauthorized", StringComparison.Ordinal)
            || lower.Contains("not authorized", StringComparison.Ordinal)
            || lower.Contains("forbidden", StringComparison.Ordinal)
            || lower.Contains("access is denied", StringComparison.Ordinal)
            || lower.Contains("privilege", StringComparison.Ordinal))
        {
            return SolutionImportFailureKind.AuthFailure;
        }

        if ((int)statusCode == 429
            || lower.Contains("rate limit", StringComparison.Ordinal)
            || lower.Contains("throttl", StringComparison.Ordinal)
            || lower.Contains("too many requests", StringComparison.Ordinal))
        {
            return SolutionImportFailureKind.RateLimited;
        }

        if (lower.Contains("quota", StringComparison.Ordinal)
            || lower.Contains("storage limit", StringComparison.Ordinal)
            || lower.Contains("capacity", StringComparison.Ordinal))
        {
            return SolutionImportFailureKind.QuotaExhausted;
        }

        return SolutionImportFailureKind.UnknownInvocationFailure;
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, Uri uri, string bearerToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", ODataVersion);
        request.Headers.Add("OData-MaxVersion", ODataVersion);
        return request;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";

    private static string PackageTypeName(bool managed) => managed ? "managed" : "unmanaged";

    /// <summary>The SpaarkeMaster entry of the artifact manifest (T218b). Any field may be absent.</summary>
    internal sealed record PackageManifestEntry(string? Version, string? ManagedBlobName, string? UnmanagedBlobName);

    /// <summary>What the target environment holds before the import.</summary>
    internal abstract record InstalledPackage
    {
        private InstalledPackage() { }
        public sealed record Absent : InstalledPackage;
        public sealed record Present(string Version, bool IsManaged) : InstalledPackage;
        public sealed record Unknown(SolutionImportFailureKind Kind, string Diagnostic) : InstalledPackage;
    }

    private abstract record ActionInvokeResult
    {
        private ActionInvokeResult() { }
        public sealed record Started(Guid AsyncOperationId) : ActionInvokeResult;
        public sealed record Failed(SolutionImportFailureKind Kind, string Diagnostic) : ActionInvokeResult;
    }

    /// <summary>The async operation as read by one poll. Advisory (ISS-032) — see <see cref="PollImportAsync"/>.</summary>
    private sealed record AsyncOperationState(bool Terminal, int? StatusCode, string? Message)
    {
        public static readonly AsyncOperationState Unknown = new(false, null, null);
    }

    private enum ImportJobState { Missing, Unreadable, Running, Completed }

    /// <summary>The import job as read by one poll.</summary>
    private sealed record ImportJobSnapshot(ImportJobState State, string? Data, double? Progress)
    {
        public static readonly ImportJobSnapshot Missing = new(ImportJobState.Missing, null, null);
        public static readonly ImportJobSnapshot Unreadable = new(ImportJobState.Unreadable, null, null);
    }

    /// <summary>One row of an <c>importjobs</c> list response.</summary>
    internal sealed record ImportJobRow(Guid Id, DateTimeOffset? StartedOn, DateTimeOffset? CompletedOn);

    /// <summary>One import entry of <c>msdyn_solutionhistories</c> (Status 0 Started, 1 Completed, 2 Queued; Result true = Success).</summary>
    internal sealed record SolutionHistoryEntry(
        int? Status, bool? Result, DateTimeOffset? StartTime, DateTimeOffset? EndTime, string? ExceptionMessage, string? SolutionVersion = null)
    {
        /// <summary>Started or queued and not ended.</summary>
        public bool IsOpen => Status is 0 or 2 || (Status is null && EndTime is null);
    }

    /// <summary>What the solution history says about one import.</summary>
    internal sealed record HistorySummary(bool HasOpen, bool HasSuccess, bool HasFailure, string? FailureText, bool Unreadable = false)
    {
        /// <summary>History unreadable: says nothing — in particular it is NOT "no open entry".</summary>
        public static readonly HistorySummary Unknown = new(false, false, false, null, Unreadable: true);
    }

    /// <summary>Result of the in-flight check made before an import is started.</summary>
    private abstract record InFlightImport
    {
        private InFlightImport() { }
        public sealed record None : InFlightImport;

        /// <param name="ImportJobId">The running import's job, or <c>null</c> when only the solution history shows it.</param>
        /// <param name="Since">Where its solution-history entries start (with clock-skew allowance).</param>
        public sealed record Running(Guid? ImportJobId, DateTimeOffset Since) : InFlightImport;

        public sealed record Unreadable(SolutionImportFailureKind Kind, string Diagnostic) : InFlightImport;
    }

    private abstract record ImportPollResult
    {
        private ImportPollResult() { }
        public sealed record Succeeded : ImportPollResult;

        /// <param name="ReportedByDataverse">The async operation completed and reported the failure (not a timeout or an unreadable status).</param>
        public sealed record Failed(SolutionImportFailureKind Kind, string Diagnostic, bool ReportedByDataverse) : ImportPollResult;
    }
}
