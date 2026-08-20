// -----------------------------------------------------------------------------
// E2EValidationRunner.cs
//
// Pure-C# port of scripts/Validate-DeployedEnvironment.ps1 (r2 shipped surface,
// 532 lines) -- the extended H13 validation runner. ZERO ProcessStartInfo /
// pwsh dependency: every check the port performs is a live HttpClient call
// against the deployed BFF (health, ping, CORS). Per DS-4 section 6 this
// retires the last shell-out from the H13 collaborator surface so the L2
// Worker's Option D zero-shell posture holds; the .ps1 script remains on
// disk as the operator-invokable off-cluster diagnostic tool (see the
// ValidateDeployedEnvironmentScriptRunner.cs retirement banner).
//
// TASK: 181 (Phase C'' Wave G-7 Batch G-7B). Sibling of tasks 182
// (NamingConformanceChecker -- pure-C# port) and 183 (ArmCostEnvelopeChecker
// -- Azure.ResourceManager.CostManagement SDK port). Combined they retire all
// three E2EAcceptance shell-outs so H13 executes cleanly under Option D.
//
// SILENT-FAIL AUDIT (per this task's dispatch directive):
//   The POML task 181 prompt described the .ps1 as containing "5 effect probes:
//   BFF /health, sample analysis, doc upload+index, layout render, wizard
//   field-map". Direct inspection of scripts/Validate-DeployedEnvironment.ps1
//   contradicts that: the r2 SHIPPED script contains a DIFFERENT set of 5
//   checks -- Dataverse env-vars, BFF /healthz + /ping, CORS origin,
//   dev-value-leakage, naming-conformance. The "sample analysis / doc upload+
//   index / layout render / wizard field-map" set is a Phase-B EXTENSION that
//   was TRACKED in IE2EValidationRunner.cs's own file header but NEVER added
//   to the .ps1 (grep in the .ps1 for any of those phrases returns zero
//   matches). This port therefore ports the checks that ACTUALLY EXIST in the
//   .ps1 today; the Phase-B extended set is surfaced as an explicit Skipped
//   list per the interface's E2EValidationOutcome.Success(ChecksPassed,
//   ChecksSkipped) shape -- Phase F rerun (task 186) closes the extended set
//   once the H12a/H12b seed content the POML escalation trigger references
//   actually exists on a fresh customer stamp. Silent-fail defense: the
//   Skipped set is emitted with an explicit name + rationale so an operator
//   grepping the H13 outcome cannot mistake "not-yet-implemented" for
//   "verified".
//
// WHAT THIS PORT COVERS TODAY (real HttpClient effect probes):
//   1. bff-healthz-200   GET  {bffApiUrl}/healthz             HTTP 200 required
//   2. bff-ping-200      GET  {bffApiUrl}/ping                HTTP 200 required
//   3. cors-dataverse-origin
//                         OPTIONS {bffApiUrl}/healthz
//                         Origin: {dataverseUrl}
//                         Access-Control-Request-Method: GET
//                         Response Access-Control-Allow-Origin header must
//                         equal {dataverseUrl} or '*' (parity with the .ps1's
//                         `-eq $DataverseUrl -or -eq '*'` predicate).
//
// WHAT THIS PORT EXPLICITLY SKIPS (surfaced in ChecksSkipped so H13's outcome
// is honest about the interim posture):
//   * dataverse-env-vars-present
//   * dataverse-env-vars-dev-leakage
//       Both require Dataverse Web API authentication against the customer
//       tenant (the .ps1 uses the operator's `pac auth` session + az rest with
//       -resource {DataverseUrl}). L2 has no equivalent Dataverse identity on
//       the H13 envelope today -- the MI-Dataverse App User (H10) is scoped to
//       the BFF's UAMI, not the L2 UAMI. Wiring a second Dataverse identity
//       into H13 would require a new bindings surface AND per-run KV lookup;
//       out-of-scope for this task's convergence-with-probes brief. Task 186
//       (Phase F rerun) closes -- until then this port SURFACES the gap.
//
//   * naming-conformance
//       Delegated to INamingConformanceChecker (task 182's pure-C# port,
//       registered in E2EAcceptanceModule.cs). H13's own handler invokes it
//       INDEPENDENTLY per the interface header: "H13 owns SC #17 ... as its
//       own explicit pass/fail boundary so that failure in either surface
//       fails H13 with a distinct rejection code." Re-checking here would
//       duplicate the invocation.
//
//   * sample-ai-analysis, sample-doc-upload-index, sample-workspace-layout-
//     render, sample-wizard-field-map
//       Phase-B extension set from the IE2EValidationRunner.cs header comment
//       + POML task 181 prompt. NEVER implemented in the .ps1 (verified by
//       grep). Requires H12a/H12b seed content that a fresh customer stamp
//       does not yet ship with -- the POML escalation trigger explicitly
//       calls this out. Deferred to Phase F rerun (task 186).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11):
//   Existing:  The four effect-probe rules the port implements already exist
//              in Validate-DeployedEnvironment.ps1 (functions Test-BffApiHealth
//              lines 207-267 + Test-CorsOrigin lines 273-330). ZERO
//              reinvention -- semantics + status-code expectations + fail
//              messages carry over verbatim.
//   Extension: this is the direct extension of the existing IE2EValidationRunner
//              seam (same interface, same request/outcome shape). Only the
//              collaborator swaps in E2EAcceptanceModule.cs. Sibling probes
//              171/172/175/176/177/178/179/180 all use IHttpClientFactory
//              via a named client; this port follows the same pattern and
//              REUSES its DI ceremony (named HttpClient registered next to
//              the sibling I2/I4 clients) rather than authoring a parallel
//              HTTP-probing helper class -- per POML constraint 1 (DS-4
//              section 5 / DS-1b section 1 convergence bonus).
//   Cost-of-doing-nothing: H13 cannot execute the SC #5 sample-check portion
//              of its acceptance gate under the L2 Worker's zero-shell
//              posture; ValidateDeployedEnvironmentScriptRunner throws
//              FileNotFoundException on any App Service publish that does not
//              carry pwsh + the .ps1 (the DS-4 target state). Without the
//              port H13 collapses to Resumable-forever for SC #5 -- the exact
//              defect the shell-out retirement wave exists to close.
//
// PATTERN PARITY:
//   Mirrors SpeContainerResolverInvariantProbe (task 176) for the injected
//   IHttpClientFactory + named-client convention + FakeHttpMessageHandler-
//   friendly test seam; mirrors NamingConformanceChecker (task 182) for the
//   pure-C# port banner + forcing-function ArchTest-style source-file
//   scanning. ADR-038 path-1 pyramid: tests exercise the runner against
//   hand-rolled FakeHttpMessageHandler (never Mock<HttpMessageHandler>).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10):
//   Sprk.Provisioning.ControlPlane.Core (L2, not BFF). Consumes NO
//   AI-internal types (ADR-013 -- no IActionResolver / IActionRunner /
//   IOpenAiClient / IPlaybookService injection). Uses the same H13AcceptanceOptions
//   already registered for the sibling seams; no new options class.
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <inheritdoc cref="IE2EValidationRunner"/>
public sealed class E2EValidationRunner : IE2EValidationRunner
{
    /// <summary>
    /// Named HttpClient key the DI module registers so the runner can pull an
    /// isolated client via <see cref="IHttpClientFactory"/> (parity with the
    /// sibling H13 probe named-client convention -- see
    /// <see cref="AiSearchTenantFilterInvariantProbe.HttpClientName"/> and
    /// <see cref="SpeContainerResolverInvariantProbe.HttpClientName"/>).
    /// </summary>
    public const string HttpClientName = "H13-E2EValidationRunner";

    /// <summary>Check name emitted in ChecksPassed / ChecksFailed for the BFF /healthz probe. Parity with the .ps1's Test-BffApiHealth line 231.</summary>
    public const string CheckBffHealthz = "bff-healthz-200";

    /// <summary>Check name emitted in ChecksPassed / ChecksFailed for the BFF /ping probe. Parity with the .ps1's Test-BffApiHealth line 253.</summary>
    public const string CheckBffPing = "bff-ping-200";

    /// <summary>Check name emitted in ChecksPassed / ChecksFailed for the CORS origin preflight. Parity with the .ps1's Test-CorsOrigin line 306.</summary>
    public const string CheckCorsDataverseOrigin = "cors-dataverse-origin";

    /// <summary>Check name emitted in ChecksSkipped for the .ps1's Test-DataverseEnvironmentVariables (needs Dataverse auth material not on H13 envelope).</summary>
    public const string SkippedDataverseEnvVarsPresent = "dataverse-env-vars-present";

    /// <summary>Check name emitted in ChecksSkipped for the .ps1's Test-DevValueLeakage (dependent on env-vars).</summary>
    public const string SkippedDataverseEnvVarsDevLeakage = "dataverse-env-vars-dev-leakage";

    /// <summary>Check name emitted in ChecksSkipped for naming-conformance (delegated to INamingConformanceChecker).</summary>
    public const string SkippedNamingConformance = "naming-conformance-delegated-to-INamingConformanceChecker";

    /// <summary>Check name emitted in ChecksSkipped for the Phase-B extended sample analysis probe (interface-header TRACKED; deferred to Phase F rerun task 186).</summary>
    public const string SkippedSampleAiAnalysis = "sample-ai-analysis-phase-B-deferred";

    /// <summary>Check name emitted in ChecksSkipped for the Phase-B extended sample doc upload+index probe.</summary>
    public const string SkippedSampleDocUploadIndex = "sample-doc-upload-index-phase-B-deferred";

    /// <summary>Check name emitted in ChecksSkipped for the Phase-B extended sample workspace-layout render probe.</summary>
    public const string SkippedSampleWorkspaceLayoutRender = "sample-workspace-layout-render-phase-B-deferred";

    /// <summary>Check name emitted in ChecksSkipped for the Phase-B extended sample wizard field-map probe.</summary>
    public const string SkippedSampleWizardFieldMap = "sample-wizard-field-map-phase-B-deferred";

    /// <summary>Path suffix used for BFF health probe. Anonymous endpoint per BFF Program.cs.</summary>
    public const string HealthzPath = "/healthz";

    /// <summary>Path suffix used for BFF ping probe. Anonymous endpoint per BFF Program.cs.</summary>
    public const string PingPath = "/ping";

    /// <summary>
    /// URL scheme allow-list -- accepts only http(s). Any other scheme
    /// (file://, ftp://) is treated as a Failed BffApiUrl parameter, mirroring
    /// the sibling probes' identical posture (SpeContainerResolverInvariantProbe).
    /// </summary>
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp, Uri.UriSchemeHttps,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<E2EValidationRunner> _logger;

    public E2EValidationRunner(
        IHttpClientFactory httpClientFactory,
        ILogger<E2EValidationRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<E2EValidationOutcome> RunAsync(
        E2EValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var passed = new List<string>(3);
        var failed = new List<string>(3);
        var diagnostics = new List<string>(3);

        var skipped = BuildInterimSkippedList();

        // Pre-flight: BffApiUrl / DataverseUrl must be valid absolute URLs.
        // Blank or malformed values are surfaced as Failed for the affected
        // check(s); we deliberately DO NOT throw so the handler receives a
        // typed Failure it can classify (not an infra fault). Parity with
        // the .ps1's Add-TestResult -Status 'Fail' branches for missing/
        // malformed URLs (lines 218-221, 282-286).
        if (string.IsNullOrWhiteSpace(request.BffApiUrl))
        {
            failed.Add(CheckBffHealthz);
            failed.Add(CheckBffPing);
            failed.Add(CheckCorsDataverseOrigin);
            diagnostics.Add(
                "BffApiUrl parameter is empty -- cannot exercise BFF /healthz + /ping + CORS " +
                "probes. H9 must have populated bffApiUrl on the run before H13 runs.");
            return new E2EValidationOutcome.Failure(failed, string.Join(" | ", diagnostics));
        }

        var bffApiUrlTrimmed = request.BffApiUrl.TrimEnd('/');
        if (!Uri.TryCreate(bffApiUrlTrimmed, UriKind.Absolute, out var bffBaseUri)
            || !AllowedSchemes.Contains(bffBaseUri.Scheme))
        {
            failed.Add(CheckBffHealthz);
            failed.Add(CheckBffPing);
            failed.Add(CheckCorsDataverseOrigin);
            diagnostics.Add(
                $"BffApiUrl parameter '{request.BffApiUrl}' is not a valid http(s) absolute URL -- " +
                "cannot construct probe request URIs.");
            return new E2EValidationOutcome.Failure(failed, string.Join(" | ", diagnostics));
        }

        // Normalize DataverseUrl for CORS check (parity with .ps1 line 74's TrimEnd('/')).
        var dataverseUrlTrimmed = request.DataverseUrl?.TrimEnd('/') ?? string.Empty;
        var dataverseUrlValid = !string.IsNullOrWhiteSpace(dataverseUrlTrimmed)
            && Uri.TryCreate(dataverseUrlTrimmed, UriKind.Absolute, out var _);

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        _logger.LogInformation(
            "H13 E2E validation (pure-C#) starting: customerId={CustomerId} runId={RunId} " +
            "bffApiUrl={BffApiUrl} dataverseUrl={DataverseUrl} slot={Slot}",
            request.CustomerId, request.RunId, bffApiUrlTrimmed,
            dataverseUrlTrimmed, request.TargetSlotName);

        // 1. GET /healthz
        var healthzUri = new Uri(bffBaseUri.GetLeftPart(UriPartial.Authority) + HealthzPath);
        await RunGetOkProbeAsync(httpClient, healthzUri, CheckBffHealthz,
            passed, failed, diagnostics, cancellationToken).ConfigureAwait(false);

        // 2. GET /ping
        var pingUri = new Uri(bffBaseUri.GetLeftPart(UriPartial.Authority) + PingPath);
        await RunGetOkProbeAsync(httpClient, pingUri, CheckBffPing,
            passed, failed, diagnostics, cancellationToken).ConfigureAwait(false);

        // 3. OPTIONS /healthz with Origin: {dataverseUrl}
        //    Skip if DataverseUrl is missing/malformed (rather than reporting
        //    a spurious CORS failure).
        if (!dataverseUrlValid)
        {
            failed.Add(CheckCorsDataverseOrigin);
            diagnostics.Add(
                $"DataverseUrl parameter '{request.DataverseUrl}' is not a valid absolute URL -- " +
                "cannot perform CORS preflight probe (§4D I1 defense-in-depth: refusing to send " +
                "an ambiguous Origin header).");
        }
        else
        {
            await RunCorsPreflightProbeAsync(
                httpClient, healthzUri, dataverseUrlTrimmed,
                passed, failed, diagnostics, cancellationToken).ConfigureAwait(false);
        }

        if (failed.Count > 0)
        {
            var diag = "E2EValidationRunner (pure-C#) reported "
                + failed.Count + " failing check(s): " + string.Join(" | ", diagnostics);
            _logger.LogWarning(
                "H13 E2E validation FAILED: customerId={CustomerId} runId={RunId} failed={FailedCount} " +
                "diagnostic={Diagnostic}",
                request.CustomerId, request.RunId, failed.Count, diag);
            return new E2EValidationOutcome.Failure(failed, diag);
        }

        _logger.LogInformation(
            "H13 E2E validation PASSED: customerId={CustomerId} runId={RunId} passed={PassedCount} skipped={SkippedCount}",
            request.CustomerId, request.RunId, passed.Count, skipped.Count);
        return new E2EValidationOutcome.Success(passed, skipped);
    }

    /// <summary>
    /// Builds the ChecksSkipped list emitted on every run. The list is
    /// intentionally NON-EMPTY: it advertises the interim Phase-B posture +
    /// the naming-conformance delegation + the Dataverse-auth gap. An operator
    /// grepping H13 outcome for "not implemented yet" gaps sees them in a
    /// stable place.
    ///
    /// Exposed internal so tests can pin the expected shape against future
    /// drift (e.g., a task 186 wire-up that graduates a probe from Skipped
    /// to real MUST also update the test).
    /// </summary>
    internal static IReadOnlyList<string> BuildInterimSkippedList()
    {
        return new List<string>
        {
            SkippedDataverseEnvVarsPresent,
            SkippedDataverseEnvVarsDevLeakage,
            SkippedNamingConformance,
            SkippedSampleAiAnalysis,
            SkippedSampleDocUploadIndex,
            SkippedSampleWorkspaceLayoutRender,
            SkippedSampleWizardFieldMap,
        };
    }

    /// <summary>
    /// Issues a GET against <paramref name="uri"/> and passes on HTTP 200,
    /// fails otherwise. Any network/transport exception is captured as a
    /// Failed diagnostic (parity with the .ps1's try/catch pattern -- the
    /// script itself never throws on a per-probe HTTP failure, it records
    /// a Fail row and continues).
    /// </summary>
    private static async Task RunGetOkProbeAsync(
        HttpClient httpClient, Uri uri, string checkName,
        List<string> passed, List<string> failed, List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                passed.Add(checkName);
                return;
            }
            failed.Add(checkName);
            diagnostics.Add(
                $"{checkName}: GET '{uri}' returned HTTP {(int)response.StatusCode} ({response.StatusCode}); expected 200.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            failed.Add(checkName);
            diagnostics.Add($"{checkName}: GET '{uri}' timed out.");
        }
        catch (OperationCanceledException)
        {
            // Caller-triggered cancellation propagates.
            throw;
        }
        catch (HttpRequestException ex)
        {
            failed.Add(checkName);
            diagnostics.Add(
                $"{checkName}: GET '{uri}' HttpRequestException: {ex.Message}");
        }
        catch (Exception ex)
        {
            failed.Add(checkName);
            diagnostics.Add(
                $"{checkName}: GET '{uri}' unexpected {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Issues an OPTIONS preflight against <paramref name="uri"/> with the
    /// Dataverse origin header + asserts the response includes an
    /// Access-Control-Allow-Origin header matching the Dataverse URL or
    /// '*' (parity with the .ps1's Test-CorsOrigin lines 294-311). Non-2xx
    /// responses are TOLERANT to CORS headers on the exception path -- some
    /// gateways/apps return CORS headers on OPTIONS with non-2xx status
    /// (the .ps1 has the same tolerance at lines 316-324).
    /// </summary>
    private static async Task RunCorsPreflightProbeAsync(
        HttpClient httpClient, Uri targetUri, string dataverseOrigin,
        List<string> passed, List<string> failed, List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Options, targetUri);
            req.Headers.TryAddWithoutValidation("Origin", dataverseOrigin);
            req.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");
            req.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "Authorization");

            using var response = await httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var allowOrigin = TryGetHeaderValue(response.Headers, "Access-Control-Allow-Origin");

            if (IsAllowOriginAcceptable(allowOrigin, dataverseOrigin))
            {
                passed.Add(CheckCorsDataverseOrigin);
                return;
            }

            failed.Add(CheckCorsDataverseOrigin);
            var observed = string.IsNullOrEmpty(allowOrigin) ? "<absent>" : allowOrigin;
            diagnostics.Add(
                $"{CheckCorsDataverseOrigin}: OPTIONS '{targetUri}' returned HTTP {(int)response.StatusCode} " +
                $"with Access-Control-Allow-Origin={observed}; expected '{dataverseOrigin}' or '*'.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            failed.Add(CheckCorsDataverseOrigin);
            diagnostics.Add($"{CheckCorsDataverseOrigin}: OPTIONS '{targetUri}' timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            failed.Add(CheckCorsDataverseOrigin);
            diagnostics.Add(
                $"{CheckCorsDataverseOrigin}: OPTIONS '{targetUri}' HttpRequestException: {ex.Message}");
        }
        catch (Exception ex)
        {
            failed.Add(CheckCorsDataverseOrigin);
            diagnostics.Add(
                $"{CheckCorsDataverseOrigin}: OPTIONS '{targetUri}' unexpected {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Predicate helper isolated for direct unit-test coverage: returns
    /// <c>true</c> iff the observed Access-Control-Allow-Origin header value
    /// satisfies the .ps1's `-eq $DataverseUrl -or -eq '*'` rule (case-
    /// sensitive on origin per RFC 6454; wildcard is a literal '*'). Empty/
    /// null observed values NEVER satisfy.
    /// </summary>
    internal static bool IsAllowOriginAcceptable(string? observed, string dataverseOrigin)
    {
        if (string.IsNullOrEmpty(observed))
        {
            return false;
        }
        if (observed == "*")
        {
            return true;
        }
        return string.Equals(observed, dataverseOrigin, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the first header value or returns null. Exposed for internal
    /// clarity -- HttpResponseHeaders.TryGetValues returns IEnumerable which
    /// is easy to mis-handle.
    /// </summary>
    private static string? TryGetHeaderValue(HttpResponseHeaders headers, string name)
    {
        if (headers.TryGetValues(name, out var values))
        {
            foreach (var v in values)
            {
                return v;
            }
        }
        return null;
    }
}
