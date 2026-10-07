// -----------------------------------------------------------------------------
// DataverseWebApiSolutionImporter.cs
//
// Production <see cref="ISolutionImporter"/> implementation (task 141, Wave
// G-4, Option D hybrid) — pure HttpClient + Azure.Identity.ClientSecretCredential
// port of the retired DeployDataverseSolutionsScriptImporter's pwsh shell-out
// to scripts/Deploy-DataverseSolutions.ps1. Ports the SAME 8-solution
// dependency-tier import sequence + retry/partial-failure semantics the PS
// script implements, via Dataverse Web API `ImportSolution` / `StageAndUpgrade`
// actions + `importjobs` entity polling instead of the pac CLI's solution
// import command.
//
// ORDERING (POML constraint — port verbatim, do not re-derive): the tier
// grouping + per-tier iteration order below is sourced from the INJECTED
// <see cref="ISolutionCatalog"/> (CanonicalSolutionCatalog in production),
// which is already verified byte-for-byte equivalent to the PS script's
// $SolutionImportOrder by H6SolutionImportHandlerTests's catalog-mirror test.
// This importer does not re-derive dependency ordering from solution metadata
// inspection — it trusts the catalog's Tier field exactly as the PS script
// trusted its own $SolutionImportOrder hashtable.
//
// ARTIFACT PACKAGING (DS-1b §1 H6 row — "solution ZIPs must become versioned
// publish/content artifacts, not files assumed present on a local shell's
// filesystem"): the 9 solution ZIPs are resolved from the SAME
// `provisioning-artifacts` blob container task 116 (BFF zip)/117 (ARM JSON)/
// 132 (H9 artifact) already publish to, via a small solution-specific
// manifest (`dataverse-solutions-latest.json` by default — see
// SolutionImportOptions.SolutionArtifactManifestBlobName for the exact shape
// + the live-ceremony gap this manifest currently has no CI producer for).
// This importer never reads a local filesystem path for a solution ZIP.
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
// Blob-artifact resolution (fetching the 9 solution ZIPs from Spaarke's OWN
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
/// <see cref="ISolutionImporter"/> implementation that imports the 8
/// authoritative Spaarke managed solutions via Dataverse Web API
/// <c>ImportSolution</c>/<c>StageAndUpgrade</c> actions + <c>importjobs</c>
/// polling, resolving each solution ZIP from a versioned blob-artifact
/// manifest (never a local filesystem path).
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

    private const string ODataVersion = "4.0";
    private const int DiagnosticTailBudget = 800;

    private readonly HttpClient _httpClient;
    private readonly BlobContainerClient _artifactsContainer;
    private readonly ISolutionCatalog _catalog;
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
        ISolutionCatalog catalog,
        IOptions<SolutionImportOptions> options,
        WorkerDataverseCredentialFactory credentialFactory,
        ILogger<DataverseWebApiSolutionImporter> logger)
        : this(
            httpClient,
            artifactsContainer,
            catalog,
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
        ISolutionCatalog catalog,
        IOptions<SolutionImportOptions> options,
        ILogger<DataverseWebApiSolutionImporter> logger,
        TimeProvider timeProvider,
        Func<string, string, string, TokenCredential> credentialFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(artifactsContainer);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _httpClient = httpClient;
        _artifactsContainer = artifactsContainer;
        _catalog = catalog;
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

        // (0) Resolve the artifact manifest ONCE up front — a manifest that
        // cannot be read/parsed is a hard MissingSolutionZips failure (parity
        // with the PS script's "no ZIPs found -> exit 1" hard stop).
        Dictionary<string, SolutionArtifactManifestEntry> manifest;
        try
        {
            manifest = await LoadArtifactManifestAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new SolutionImportOutcome.Failure(
                SolutionImportFailureKind.MissingSolutionZips,
                $"Solution artifact manifest '{_options.SolutionArtifactManifestBlobName}' not found in the " +
                "'provisioning-artifacts' blob container. H6 refuses to fall back to a local filesystem path — " +
                "the 9 solution ZIPs must be published as versioned blob artifacts (DS-1b §1 H6 invariant).");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new SolutionImportOutcome.Failure(
                SolutionImportFailureKind.MissingSolutionZips,
                $"Solution artifact manifest '{_options.SolutionArtifactManifestBlobName}' could not be parsed: " +
                $"{ex.GetType().Name}: {ex.Message}.");
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

        // (1) Existing-solutions snapshot (mirrors PS Get-ExistingSolutions —
        // catch-and-proceed-with-empty-dict on failure, same as the script).
        var existing = await GetExistingSolutionsAsync(envUri, bearerToken, cancellationToken).ConfigureAwait(false);

        var anyImportedThisRun = false;

        foreach (var tier in _catalog.Solutions.GroupBy(s => s.Tier).OrderBy(g => g.Key))
        {
            foreach (var solution in tier)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!manifest.TryGetValue(solution.SolutionUniqueName, out var artifactEntry))
                {
                    return new SolutionImportOutcome.Failure(
                        SolutionImportFailureKind.MissingSolutionZips,
                        $"Solution artifact manifest does not contain an entry for '{solution.SolutionUniqueName}'.");
                }

                byte[] zipBytes;
                try
                {
                    var blob = _artifactsContainer.GetBlobClient(artifactEntry.BlobName);
                    var download = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
                    zipBytes = download.Value.Content.ToArray();
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    return new SolutionImportOutcome.Failure(
                        SolutionImportFailureKind.MissingSolutionZips,
                        $"Artifact blob '{artifactEntry.BlobName}' for solution '{solution.SolutionUniqueName}' " +
                        "not found (HTTP 404).");
                }

                var expectedVersion = artifactEntry.Version ?? TryReadSolutionVersionFromZip(zipBytes);
                var isUpgrade = existing.TryGetValue(solution.SolutionUniqueName, out var installedVersion);

                // "Already at version" skip — acceptance criterion b parity.
                if (isUpgrade && expectedVersion is not null
                    && string.Equals(installedVersion, expectedVersion, StringComparison.Ordinal))
                {
                    _logger.LogInformation(
                        "H6 solution {Solution} already at v{Version} — skipping import call.",
                        solution.SolutionUniqueName, expectedVersion);
                    continue;
                }

                var importJobId = Guid.NewGuid();
                var invokeResult = await InvokeImportActionAsync(
                    envUri, bearerToken, solution, zipBytes, isUpgrade, importJobId, cancellationToken)
                    .ConfigureAwait(false);

                if (invokeResult is ActionInvokeResult.Failed invokeFailed)
                {
                    var kind = anyImportedThisRun ? SolutionImportFailureKind.PartialImport : invokeFailed.Kind;
                    return new SolutionImportOutcome.Failure(kind, invokeFailed.Diagnostic);
                }

                var pollResult = await PollImportJobAsync(
                    envUri, bearerToken, importJobId, solution.SolutionUniqueName, deadline, cancellationToken)
                    .ConfigureAwait(false);

                if (pollResult is ImportJobPollResult.Failed pollFailed)
                {
                    // Timeout is never promoted to PartialImport — a timeout
                    // means "no confirmed failure, resume safely" per
                    // SolutionImportRejectionCodes.ImportTimeout's own doc
                    // comment; only genuine reported failures get promoted.
                    var kind = pollFailed.Kind != SolutionImportFailureKind.Timeout && anyImportedThisRun
                        ? SolutionImportFailureKind.PartialImport
                        : pollFailed.Kind;
                    return new SolutionImportOutcome.Failure(kind, pollFailed.Diagnostic);
                }

                anyImportedThisRun = true;
            }

            // Per-tier version-verification gate (mirrors PS Test-TierImport)
            // — refresh the existing-solutions snapshot + confirm every
            // solution in the just-completed tier is now present before
            // proceeding to the next tier.
            var refreshed = await GetExistingSolutionsAsync(envUri, bearerToken, cancellationToken).ConfigureAwait(false);
            foreach (var solution in tier)
            {
                if (!refreshed.ContainsKey(solution.SolutionUniqueName))
                {
                    return new SolutionImportOutcome.Failure(
                        SolutionImportFailureKind.PartialImport,
                        $"Tier {tier.Key} solution '{solution.SolutionUniqueName}' not found in the post-import " +
                        "solutions list — import reported complete but the per-tier verification gate disagrees. " +
                        "Aborting BEFORE attempting subsequent tiers (Package Deployer dependency-order guarantee).");
                }
            }
            existing = refreshed;
        }

        return new SolutionImportOutcome.Success();
    }

    private async Task<Dictionary<string, SolutionArtifactManifestEntry>> LoadArtifactManifestAsync(
        CancellationToken cancellationToken)
    {
        var manifestBlob = _artifactsContainer.GetBlobClient(_options.SolutionArtifactManifestBlobName);
        var response = await manifestBlob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        var json = response.Value.Content.ToString();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("solutions", out var solutionsElement)
            || solutionsElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Manifest '{_options.SolutionArtifactManifestBlobName}' is missing a 'solutions' object.");
        }

        var result = new Dictionary<string, SolutionArtifactManifestEntry>(StringComparer.Ordinal);
        foreach (var property in solutionsElement.EnumerateObject())
        {
            if (property.Value.TryGetProperty("blobName", out var blobNameEl)
                && blobNameEl.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(blobNameEl.GetString()))
            {
                var version = property.Value.TryGetProperty("version", out var versionEl)
                    && versionEl.ValueKind == JsonValueKind.String
                        ? versionEl.GetString()
                        : null;
                result[property.Name] = new SolutionArtifactManifestEntry(blobNameEl.GetString()!, version);
            }
        }
        return result;
    }

    private async Task<Dictionary<string, string>> GetExistingSolutionsAsync(
        Uri envUri, string bearerToken, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var uri = new Uri(envUri, "/api/data/v9.2/solutions?$select=uniquename,version");
        try
        {
            using var request = BuildRequest(HttpMethod.Get, uri, bearerToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "H6 existing-solutions query returned {StatusCode} — proceeding with fresh-import assumption.",
                    (int)response.StatusCode);
                return result;
            }
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(bodyText);
            if (doc.RootElement.TryGetProperty("value", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in array.EnumerateArray())
                {
                    if (element.TryGetProperty("uniquename", out var un) && un.ValueKind == JsonValueKind.String
                        && element.TryGetProperty("version", out var ver) && ver.ValueKind == JsonValueKind.String)
                    {
                        result[un.GetString()!] = ver.GetString()!;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // Parity with the PS script's Get-ExistingSolutions catch-and-proceed.
            _logger.LogWarning(ex, "H6 existing-solutions query failed — proceeding with fresh-import assumption.");
        }
        return result;
    }

    private async Task<ActionInvokeResult> InvokeImportActionAsync(
        Uri envUri,
        string bearerToken,
        CanonicalSolutionEntry solution,
        byte[] zipBytes,
        bool isUpgrade,
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        // Maps the PS script's Import-ManagedSolution branch exactly:
        // existing solution -> StageAndUpgrade action; absent -> ImportSolution.
        var actionName = isUpgrade ? "StageAndUpgrade" : "ImportSolution";
        var actionUri = new Uri(envUri, $"/api/data/v9.2/{actionName}");

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
                $"{actionName} POST for '{solution.SolutionUniqueName}' timed out after {_options.DataverseWebApiRequestTimeout}.");
        }
        catch (HttpRequestException ex)
        {
            return new ActionInvokeResult.Failed(
                SolutionImportFailureKind.UnknownInvocationFailure,
                $"{actionName} POST for '{solution.SolutionUniqueName}' infrastructure error: {ex.Message}");
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
                $"{actionName} POST for '{solution.SolutionUniqueName}' failed: {(int)response.StatusCode} " +
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

    private sealed record SolutionArtifactManifestEntry(string BlobName, string? Version);

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
