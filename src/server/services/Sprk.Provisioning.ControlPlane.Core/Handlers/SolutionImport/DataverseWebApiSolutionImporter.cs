// -----------------------------------------------------------------------------
// DataverseWebApiSolutionImporter.cs
//
// Production <see cref="ISolutionImporter"/> implementation (task 141; T218b) —
// pure HttpClient import of the ONE Spaarke package, SpaarkeMaster, via the
// Dataverse Web API `ImportSolution` / `StageAndUpgrade` actions + `importjobs`
// polling.
//
// T218b (ADR-027 §3-§4, amended 2026-10-07): the run's package type picks the
// managed (default) or unmanaged blob; the importer reads the installed
// SpaarkeMaster FIRST and refuses — nothing imported — a managed↔unmanaged
// switch and a downgrade; an equal version is skipped; an older version is
// upgraded with StageAndUpgrade. A failed read of the installed solution is a
// failure, never "assume a fresh install".
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
//   - ImportSolution / StageAndUpgrade actions share the SAME core parameter
//     set: OverwriteUnmanagedCustomizations (bool, required),
//     PublishWorkflows (bool, required), CustomizationFile (base64 binary,
//     required), ImportJobId (GUID, required — CLIENT-GENERATED, not
//     server-returned), SkipProductUpdateDependencies (bool, optional).
//     ImportSolution additionally exposes HoldingSolution (bool) +
//     StageAndUpgrade (bool) parameters; StageAndUpgrade (the action) can be
//     invoked directly with CustomizationFile populated for a single-call
//     stage+upgrade (no separate StageSolution round-trip is required).
//   - This importer maps the PS script's own $useStageAndUpgrade branch
//     (Import-ManagedSolution: existing solution + Auto/Upgrade mode ->
//     `--stage-and-upgrade`; else plain import) onto: existing solution ->
//     call the `StageAndUpgrade` action; absent solution -> call the
//     `ImportSolution` action (HoldingSolution=false). This literally
//     satisfies the POML's "ImportSolution + StageAndUpgrade" framing as TWO
//     distinct action calls on the two distinct PS branches, rather than
//     ImportSolution's own StageAndUpgrade=true parameter (functionally
//     equivalent, but the two-action mapping is a more faithful 1:1 port of
//     Import-ManagedSolution's own if/else).
//   - ASYNC-OPERATION POLLING CONTRACT — DEVIATION FROM THE DISPATCH CONTEXT'S
//     "poll /asyncoperations({id}) until statecode 0/3" framing, DOCUMENTED
//     per Wave G-2..G-4 discipline (see task 140's BapRestEnvironmentCreator
//     header for the precedent of documenting a ground-truthed deviation
//     rather than silently complying with a wrong assumption): this importer
//     polls `/api/data/v9.2/importjobs({ImportJobId})` using the SAME
//     client-generated ImportJobId GUID passed in the action request body,
//     NOT `/asyncoperations({AsyncOperationId})`. Rationale: ImportJobId is
//     deterministic and known BEFORE the POST is even sent (we generate it),
//     so polling importjobs requires no Location-header parsing or
//     AsyncOperationId discovery from the POST response — Microsoft's own
//     SDK-level CheckImportStatus sample (learn.microsoft.com
//     /power-platform/alm/solution-async) polls asyncoperation.statecode==3
//     PLUS separately reads importjobs by the SAME ImportJobKey for the
//     detailed result; this importer folds both into a single importjobs
//     poll by treating `completedon` (non-null) as the terminal signal —
//     importjobs.completedon is populated exactly when statecode reaches 3
//     (Completed), so this is a documented simplification, not a different
//     terminal-state definition. statecode 0 (Ready) referenced in the
//     dispatch context is Dataverse's PRE-RUN state, not a terminal one —
//     the dispatch context's framing conflated the two; this header
//     documents the correction for future auditors.
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
// (default 60 min, unchanged from the retired importer) remains the OVERALL
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
/// run) via Dataverse Web API <c>ImportSolution</c>/<c>StageAndUpgrade</c> actions + <c>importjobs</c> polling,
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

    /// <summary>Manifest property naming the managed package blob (T218b).</summary>
    internal const string ManagedBlobProperty = "managedBlobName";

    /// <summary>Manifest property naming the unmanaged package blob (T218b).</summary>
    internal const string UnmanagedBlobProperty = "unmanagedBlobName";

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

        AccessToken token;
        try
        {
            // A44.5: credential selection (FR-39 ordered chain in production;
            // fake in tests) can itself throw on an exhausted chain — classify
            // as AuthFailure (→ §4C Resumable at the handler), same boundary
            // as a failed token acquisition.
            var credential = _credentialFactory(
                request.TenantId, request.ClientId, request.ClientSecret ?? string.Empty);
            token = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken)
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

        // (1) What the environment holds now. A failed read is a failure, not "fresh install": guessing would bypass
        // the type-switch and downgrade refusals below.
        var installed = await GetInstalledPackageAsync(envUri, bearerToken, cancellationToken).ConfigureAwait(false);
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
        if (string.IsNullOrWhiteSpace(packageVersion))
        {
            return new SolutionImportOutcome.Failure(
                SolutionImportFailureKind.MissingSolutionZips,
                $"The package version is unknown: the manifest entry has no 'version' and '{blobName}' has no readable " +
                "solution.xml version. H6 cannot rule out a downgrade without it.");
        }

        // (3) Type-switch and downgrade refusals; an equal version is already done.
        var isUpgrade = false;
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
                return new SolutionImportOutcome.Success();
            }
            isUpgrade = true;
        }

        // (4) Import (absent) or StageAndUpgrade (older version present).
        var importJobId = Guid.NewGuid();
        var invokeResult = await InvokeImportActionAsync(
            envUri, bearerToken, zipBytes, isUpgrade, importJobId, cancellationToken)
            .ConfigureAwait(false);

        if (invokeResult is ActionInvokeResult.Failed invokeFailed)
        {
            // The POST was refused — nothing started, so never a partial state.
            return new SolutionImportOutcome.Failure(invokeFailed.Kind, invokeFailed.Diagnostic);
        }

        var pollResult = await PollImportJobAsync(
            envUri, bearerToken, importJobId, SpaarkePackage.SolutionUniqueName, deadline, cancellationToken)
            .ConfigureAwait(false);

        if (pollResult is ImportJobPollResult.Failed pollFailed)
        {
            // A failed upgrade may leave the holding solution behind → PartialImport (QuarantineRequired). A timeout is
            // never promoted — it means "no confirmed failure, resume safely" (SolutionImportRejectionCodes.ImportTimeout).
            var kind = isUpgrade && pollFailed.Kind != SolutionImportFailureKind.Timeout
                ? SolutionImportFailureKind.PartialImport
                : pollFailed.Kind;
            return new SolutionImportOutcome.Failure(kind, pollFailed.Diagnostic);
        }

        return new SolutionImportOutcome.Success();
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
        Uri envUri, string bearerToken, CancellationToken cancellationToken)
    {
        var uri = new Uri(envUri,
            $"/api/data/v9.2/solutions?$select=uniquename,version,ismanaged&$filter=uniquename eq '{SpaarkePackage.SolutionUniqueName}'");
        try
        {
            using var request = BuildRequest(HttpMethod.Get, uri, bearerToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
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
        bool isUpgrade,
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        // Installed (older) → StageAndUpgrade action; absent → ImportSolution.
        var actionName = isUpgrade ? "StageAndUpgrade" : "ImportSolution";
        var actionUri = new Uri(envUri, $"/api/data/v9.2/{actionName}");
        var solutionName = SpaarkePackage.SolutionUniqueName;

        var body = new Dictionary<string, object?>
        {
            ["OverwriteUnmanagedCustomizations"] = true, // PS --force-overwrite
            ["PublishWorkflows"] = true,                 // PS --publish-changes
            ["CustomizationFile"] = Convert.ToBase64String(zipBytes),
            ["ImportJobId"] = importJobId,
            ["SkipProductUpdateDependencies"] = false,
        };
        if (!isUpgrade)
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
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Accepted)
            {
                return new ActionInvokeResult.Started();
            }

            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var kind = ClassifyHttpFailure(response.StatusCode, bodyText);
            return new ActionInvokeResult.Failed(
                kind,
                $"{actionName} POST for '{solutionName}' failed: {(int)response.StatusCode} " +
                $"{response.ReasonPhrase}. Body: {Truncate(bodyText, DiagnosticTailBudget)}");
        }
    }

    private async Task<ImportJobPollResult> PollImportJobAsync(
        Uri envUri,
        string bearerToken,
        Guid importJobId,
        string solutionUniqueName,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        var pollUri = new Uri(envUri,
            $"/api/data/v9.2/importjobs({importJobId:D})?$select=importjobid,progress,completedon,data,solutionname");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = BuildRequest(HttpMethod.Get, pollUri, bearerToken);
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(bodyText);
                    var root = doc.RootElement;
                    var hasCompletedOn = root.TryGetProperty("completedon", out var completedEl)
                        && completedEl.ValueKind != JsonValueKind.Null;

                    if (hasCompletedOn)
                    {
                        var data = root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.String
                            ? dataEl.GetString()
                            : null;
                        var (success, diagnostic) = EvaluateImportJobData(data);
                        if (success)
                        {
                            if (!string.IsNullOrEmpty(diagnostic))
                            {
                                _logger.LogInformation(
                                    "H6 importjob for {Solution} completed: {Diagnostic}",
                                    solutionUniqueName, diagnostic);
                            }
                            return new ImportJobPollResult.Succeeded();
                        }
                        return new ImportJobPollResult.Failed(
                            SolutionImportFailureKind.UnknownInvocationFailure,
                            $"ImportJob for '{solutionUniqueName}' completed but reported failure: {diagnostic}");
                    }
                    // Else still in progress — fall through to the delay below.
                }
                else if (response.StatusCode != HttpStatusCode.NotFound)
                {
                    // NotFound is tolerated (Dataverse may not have created
                    // the importjobs row yet at the very start) — any OTHER
                    // non-success status is a genuine classified failure.
                    var kind = ClassifyHttpFailure(response.StatusCode, bodyText);
                    return new ImportJobPollResult.Failed(
                        kind,
                        $"ImportJob poll for '{solutionUniqueName}' returned {(int)response.StatusCode}: " +
                        $"{Truncate(bodyText, DiagnosticTailBudget)}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "H6 importjob poll error for solution={Solution} — retrying.", solutionUniqueName);
            }

            var now = _timeProvider.GetUtcNow();
            if (now >= deadline)
            {
                return new ImportJobPollResult.Failed(
                    SolutionImportFailureKind.Timeout,
                    $"ImportJob polling for '{solutionUniqueName}' did not complete within the configured " +
                    $"ImportTimeout window (deadline {deadline:O}).");
            }

            var remaining = deadline - now;
            var delay = _options.ImportJobPollInterval < remaining ? _options.ImportJobPollInterval : remaining;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

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
        public sealed record Started : ActionInvokeResult;
        public sealed record Failed(SolutionImportFailureKind Kind, string Diagnostic) : ActionInvokeResult;
    }

    private abstract record ImportJobPollResult
    {
        private ImportJobPollResult() { }
        public sealed record Succeeded : ImportJobPollResult;
        public sealed record Failed(SolutionImportFailureKind Kind, string Diagnostic) : ImportJobPollResult;
    }
}
