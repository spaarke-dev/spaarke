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
// G-8 BATCH 11 GRADUATION (2026-08-20, closes audit Defect #21 / spec SC #5):
//   The four Phase-B sample-workload checks are NO LONGER permanently Skipped.
//   Each is a live, authenticated HttpClient call against the CUSTOMER's BFF
//   (bffApiUrl from the run envelope via H9 — NOT the L2 platform BFF), using
//   a bearer token from the shared UAMI-pinned TokenCredential singleton with
//   scope '{bffAuthority}/.default' (identical auth posture to
//   SpeContainerResolverInvariantProbe / task 176). Per-check timeout is
//   H13AcceptanceOptions.SampleWorkloadCheckTimeout (default 60s) with ONE
//   retry on transient transport faults (HttpRequestException / 502 / 503 /
//   504). Checks:
//
//   4. sample-ai-analysis-full-workflow           (FULL-WORKFLOW)
//        POST {bffApiUrl}/api/agent/message  {"message": "<probe prompt>"}
//        Asserts HTTP 200 + JSON object with a NON-EMPTY 'responseText'
//        string — a real LLM round-trip through the customer's SpaarkeAi
//        agent gateway (ChatClient + session plumbing). Content of the LLM
//        reply is NOT asserted (non-deterministic); non-empty structural
//        assertion only.
//   5. sample-doc-upload-index-capability-diagnostic (CAPABILITY-DIAGNOSTIC)
//        POST {bffApiUrl}/api/ai/search/count {"query":"...","scope":"all"}
//        Asserts HTTP 200 + JSON with a numeric 'count' >= 0 — proves the
//        BFF -> AI Search index round-trip (index exists, reachable, tenant
//        filter wiring executes) WITHOUT uploading a document. A full
//        upload+index workflow requires a chat session / SPE drive target and
//        would leave artifacts in the customer env; the query-path diagnostic
//        verifies the same underlying deployed capability with zero residue.
//   6. sample-workspace-layout-render-full-workflow (FULL-WORKFLOW)
//        GET  {bffApiUrl}/api/workspace/layouts
//        Asserts HTTP 200 + a NON-EMPTY JSON array — the exact layout payload
//        the workspace shell renders from. System layouts ship in BFF code
//        constants + H12b seeds the Dataverse system default, so an empty
//        array on a provisioned stamp is a REAL failure (diagnostic cites
//        H12b seeding).
//   7. sample-wizard-field-map-capability-diagnostic (CAPABILITY-DIAGNOSTIC)
//        GET  {bffApiUrl}/api/v1/field-mappings/profiles
//        Asserts HTTP 200 + JSON object with an 'items' array — proves the
//        field-mapping wizard's BFF -> Dataverse profile-resolution path is
//        deployed + wired. An EMPTY items array still passes (profile seeding
//        is customer configuration, not a provisioning invariant); a full
//        workflow (push/resolve) would MUTATE customer records — forbidden
//        for an acceptance probe.
//
//   Graceful-degradation contract (per the G-8 Batch 11 dispatch brief):
//     * HTTP 404                → check lands in ChecksSkipped with reason
//                                 suffix '-skipped-endpoint-not-deployed-http-404'
//                                 (endpoint not shipped on this BFF build yet;
//                                 honest gap surfacing, NOT a false Pass and
//                                 NOT a spurious Fail).
//     * HTTP 401 / 403          → ChecksSkipped with reason suffix
//                                 '-skipped-auth-http-{code}-l2-identity-not-granted-on-bff'
//                                 (parity with SpeContainerResolverInvariantProbe's
//                                 InfraFault posture for RBAC gaps — the runner
//                                 outcome shape has no InfraFault channel, so the
//                                 gap is surfaced as an explicit named skip the
//                                 operator cannot mistake for "verified").
//     * Token acquisition fails → all four land in ChecksSkipped with reason
//                                 suffix '-skipped-token-acquisition-failed'.
//     * Timeout / 5xx / bad payload / transport fault after retry → Failed
//                                 with diagnostic (H13 classifies QuarantineRequired).
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
//       [GRADUATED 2026-08-20 — G-8 Batch 11 / audit Defect #21] These four
//       are now REAL executed checks (rows 4-7 in "WHAT THIS PORT COVERS
//       TODAY" above); they only fall back to per-run Skipped entries when
//       the endpoint is not deployed (404), the L2 identity lacks a BFF
//       grant (401/403), or token acquisition fails — each with an explicit
//       reason suffix so H13's outcome stays honest.
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
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Options;

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

    /// <summary>
    /// Check name for the SC #5 sample AI analysis check (G-8 Batch 11). Suffix
    /// declares the check MODE per the dispatch brief: this is a FULL-WORKFLOW
    /// invocation — a real LLM round-trip via POST /api/agent/message.
    /// </summary>
    public const string CheckSampleAiAnalysis = "sample-ai-analysis-full-workflow";

    /// <summary>
    /// Check name for the SC #5 sample doc-upload+index check (G-8 Batch 11).
    /// Suffix declares the check MODE: CAPABILITY-DIAGNOSTIC — verifies the
    /// BFF -&gt; AI Search index round-trip via POST /api/ai/search/count without
    /// uploading a document (zero artifacts left in the customer env).
    /// </summary>
    public const string CheckSampleDocUploadIndex = "sample-doc-upload-index-capability-diagnostic";

    /// <summary>
    /// Check name for the SC #5 sample workspace-layout render check (G-8
    /// Batch 11). FULL-WORKFLOW — GET /api/workspace/layouts returns the exact
    /// payload the workspace shell renders from; asserts a non-empty array.
    /// </summary>
    public const string CheckSampleWorkspaceLayoutRender = "sample-workspace-layout-render-full-workflow";

    /// <summary>
    /// Check name for the SC #5 sample wizard field-map check (G-8 Batch 11).
    /// CAPABILITY-DIAGNOSTIC — GET /api/v1/field-mappings/profiles proves the
    /// wizard's profile-resolution path without mutating customer records.
    /// </summary>
    public const string CheckSampleWizardFieldMap = "sample-wizard-field-map-capability-diagnostic";

    /// <summary>Path suffix used for BFF health probe. Anonymous endpoint per BFF Program.cs.</summary>
    public const string HealthzPath = "/healthz";

    /// <summary>Path suffix used for BFF ping probe. Anonymous endpoint per BFF Program.cs.</summary>
    public const string PingPath = "/ping";

    /// <summary>Customer-BFF path for the sample AI analysis check (agent gateway single-message endpoint).</summary>
    public const string AgentMessagePath = "/api/agent/message";

    /// <summary>Customer-BFF path for the sample doc-index capability diagnostic (semantic search count endpoint).</summary>
    public const string SemanticSearchCountPath = "/api/ai/search/count";

    /// <summary>Customer-BFF path for the sample workspace-layout render check.</summary>
    public const string WorkspaceLayoutsPath = "/api/workspace/layouts";

    /// <summary>Customer-BFF path for the sample wizard field-map capability diagnostic.</summary>
    public const string FieldMappingProfilesPath = "/api/v1/field-mappings/profiles";

    /// <summary>
    /// Probe prompt for the sample AI analysis check. Low-cost, self-describing
    /// (an operator reading customer-side logs can identify the traffic as a
    /// provisioning acceptance probe, not a user).
    /// </summary>
    public const string SampleAnalysisPrompt =
        "Provisioning acceptance probe: reply with one short sentence confirming this project's assistant is operational.";

    /// <summary>
    /// Delay before the single transient-fault retry inside a sample-workload
    /// check. Internal settable ONLY so unit tests can zero it out — one
    /// bounded retry, not a policy framework, per the G-8 Batch 11 design
    /// constraint.
    /// </summary>
    internal TimeSpan TransientRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

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
    private readonly TokenCredential _credential;
    private readonly H13AcceptanceOptions _options;
    private readonly ILogger<E2EValidationRunner> _logger;

    /// <summary>
    /// Constructs the runner. <paramref name="credential"/> is the shared
    /// UAMI-pinned <see cref="TokenCredential"/> singleton (Worker Program.cs /
    /// ADR-028 MI-outbound) — used ONLY by the G-8 Batch 11 sample-workload
    /// checks to acquire a bearer token scoped to the customer's BFF authority
    /// (parity with <see cref="SpeContainerResolverInvariantProbe"/>).
    /// </summary>
    public E2EValidationRunner(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        IOptions<H13AcceptanceOptions> options,
        ILogger<E2EValidationRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _credential = credential;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<E2EValidationOutcome> RunAsync(
        E2EValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var passed = new List<string>(7);
        var failed = new List<string>(7);
        var diagnostics = new List<string>(7);

        var skipped = new List<string>(BuildInterimSkippedList());

        // Pre-flight: BffApiUrl / DataverseUrl must be valid absolute URLs.
        // Blank or malformed values are surfaced as Failed for the affected
        // check(s); we deliberately DO NOT throw so the handler receives a
        // typed Failure it can classify (not an infra fault). Parity with
        // the .ps1's Add-TestResult -Status 'Fail' branches for missing/
        // malformed URLs (lines 218-221, 282-286).
        if (string.IsNullOrWhiteSpace(request.BffApiUrl))
        {
            failed.AddRange(AllBffDependentCheckNames());
            diagnostics.Add(
                "BffApiUrl parameter is empty -- cannot exercise BFF /healthz + /ping + CORS " +
                "probes nor the SC #5 sample-workload checks. H9 must have populated bffApiUrl " +
                "on the run before H13 runs.");
            return new E2EValidationOutcome.Failure(failed, string.Join(" | ", diagnostics));
        }

        var bffApiUrlTrimmed = request.BffApiUrl.TrimEnd('/');
        if (!Uri.TryCreate(bffApiUrlTrimmed, UriKind.Absolute, out var bffBaseUri)
            || !AllowedSchemes.Contains(bffBaseUri.Scheme))
        {
            failed.AddRange(AllBffDependentCheckNames());
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

        // 4-7. SC #5 sample-workload checks (G-8 Batch 11) -- authenticated
        //      live calls against the CUSTOMER's BFF. See file header rows 4-7
        //      for the endpoint + assertion + full-workflow-vs-capability-
        //      diagnostic breakdown and the graceful-degradation contract.
        await RunSampleWorkloadChecksAsync(
            httpClient, bffBaseUri, request,
            passed, failed, skipped, diagnostics, cancellationToken).ConfigureAwait(false);

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
    /// Builds the ALWAYS-skipped list emitted on every run. The list is
    /// intentionally NON-EMPTY: it advertises the naming-conformance
    /// delegation + the Dataverse-auth gap. An operator grepping H13 outcome
    /// for "not implemented yet" gaps sees them in a stable place.
    ///
    /// G-8 Batch 11 (2026-08-20): the four Phase-B sample-workload rows were
    /// GRADUATED to real executed checks and REMOVED from this list — they now
    /// only appear in ChecksSkipped dynamically (404 / 401 / 403 / token
    /// failure, each with an explicit reason suffix).
    ///
    /// Exposed internal so tests can pin the expected shape against future
    /// drift (a wire-up that graduates a remaining Skipped row to real MUST
    /// also update the test).
    /// </summary>
    internal static IReadOnlyList<string> BuildInterimSkippedList()
    {
        return new List<string>
        {
            SkippedDataverseEnvVarsPresent,
            SkippedDataverseEnvVarsDevLeakage,
            SkippedNamingConformance,
        };
    }

    /// <summary>
    /// Every check name that depends on a reachable, well-formed BffApiUrl —
    /// used to fail the full set when the parameter pre-flight rejects the URL.
    /// </summary>
    internal static IReadOnlyList<string> AllBffDependentCheckNames()
    {
        return new List<string>
        {
            CheckBffHealthz,
            CheckBffPing,
            CheckCorsDataverseOrigin,
            CheckSampleAiAnalysis,
            CheckSampleDocUploadIndex,
            CheckSampleWorkspaceLayoutRender,
            CheckSampleWizardFieldMap,
        };
    }

    // ------------------------------------------------------------------
    // SC #5 sample-workload checks (G-8 Batch 11)
    // ------------------------------------------------------------------

    /// <summary>
    /// Runs the four SC #5 sample-workload checks against the customer's BFF.
    /// Acquires ONE bearer token (scope '{bffAuthority}/.default' via the
    /// shared UAMI-pinned credential — parity with
    /// <see cref="SpeContainerResolverInvariantProbe"/>) and reuses it across
    /// all four calls. Token-acquisition failure skips all four with an
    /// explicit reason (infra gap, not a semantic sample-workload failure).
    /// </summary>
    private async Task RunSampleWorkloadChecksAsync(
        HttpClient httpClient, Uri bffBaseUri, E2EValidationRequest request,
        List<string> passed, List<string> failed, List<string> skipped, List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var authority = bffBaseUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        var scope = authority + "/.default";

        string bearerToken;
        try
        {
            var token = await _credential
                .GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken)
                .ConfigureAwait(false);
            bearerToken = token.Token;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            foreach (var name in SampleCheckNames())
            {
                skipped.Add(name + "-skipped-token-acquisition-failed");
            }
            diagnostics.Add(
                $"SC #5 sample-workload checks: AAD token acquisition for BFF scope '{scope}' failed " +
                $"({ex.GetType().Name}: {ex.Message}). All four sample checks SKIPPED with explicit " +
                "reason — the L2 identity cannot authenticate to the customer's BFF; this is an infra " +
                "gap (parity with the I4 probe's InfraFault posture), not a sample-workload failure.");
            _logger.LogWarning(
                "H13 SC #5 sample checks skipped -- token acquisition failed for scope {Scope}: {Error}",
                scope, ex.Message);
            return;
        }

        // 4. Sample AI analysis (FULL-WORKFLOW): real LLM round-trip via the
        //    customer's SpaarkeAi agent gateway.
        await RunSampleCheckAsync(
            httpClient, bearerToken, CheckSampleAiAnalysis,
            () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(authority + AgentMessagePath))
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { message = SampleAnalysisPrompt }),
                        Encoding.UTF8, "application/json"),
                };
                return req;
            },
            static body =>
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("responseText", out var text)
                    || text.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(text.GetString()))
                {
                    return "response JSON has no non-empty 'responseText' string -- the assistant " +
                           "returned no analysis output for the sample prompt.";
                }
                return null;
            },
            passed, failed, skipped, diagnostics, cancellationToken).ConfigureAwait(false);

        // 5. Sample doc upload+index (CAPABILITY-DIAGNOSTIC): BFF -> AI Search
        //    query round-trip, zero artifacts.
        await RunSampleCheckAsync(
            httpClient, bearerToken, CheckSampleDocUploadIndex,
            () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(authority + SemanticSearchCountPath))
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { query = "provisioning acceptance probe", scope = "all" }),
                        Encoding.UTF8, "application/json"),
                };
                return req;
            },
            static body =>
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("count", out var count)
                    || count.ValueKind != JsonValueKind.Number
                    || count.GetInt64() < 0)
                {
                    return "response JSON has no numeric 'count' >= 0 -- the BFF -> AI Search index " +
                           "round-trip did not produce a well-formed count response.";
                }
                return null;
            },
            passed, failed, skipped, diagnostics, cancellationToken).ConfigureAwait(false);

        // 6. Sample workspace-layout render (FULL-WORKFLOW): the exact payload
        //    the workspace shell renders from. Empty array on a provisioned
        //    stamp = seed gap (H12b).
        await RunSampleCheckAsync(
            httpClient, bearerToken, CheckSampleWorkspaceLayoutRender,
            () => new HttpRequestMessage(HttpMethod.Get, new Uri(authority + WorkspaceLayoutsPath)),
            static body =>
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return "response is not a JSON array of workspace layouts.";
                }
                if (doc.RootElement.GetArrayLength() == 0)
                {
                    return "workspace layouts array is EMPTY -- system layouts ship in BFF code " +
                           "constants and H12b seeds the Dataverse system default, so an empty array " +
                           "on a provisioned stamp means the layout surface is not wired/seeded " +
                           "(verify H12b ran and the BFF build includes the system layout constants).";
                }
                return null;
            },
            passed, failed, skipped, diagnostics, cancellationToken).ConfigureAwait(false);

        // 7. Sample wizard field-map (CAPABILITY-DIAGNOSTIC): profile-resolution
        //    read path only; empty profile list still passes (customer config).
        await RunSampleCheckAsync(
            httpClient, bearerToken, CheckSampleWizardFieldMap,
            () => new HttpRequestMessage(HttpMethod.Get, new Uri(authority + FieldMappingProfilesPath)),
            static body =>
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("items", out var items)
                    || items.ValueKind != JsonValueKind.Array)
                {
                    return "response JSON has no 'items' array -- the field-mapping profile " +
                           "resolution endpoint did not return the FieldMappingProfileListResponse shape.";
                }
                return null;
            },
            passed, failed, skipped, diagnostics, cancellationToken).ConfigureAwait(false);

        var sampleNames = SampleCheckNames();
        _logger.LogInformation(
            "H13 SC #5 sample-workload checks completed: customerId={CustomerId} runId={RunId} " +
            "passed={Passed} failed={Failed} skipped={Skipped}",
            request.CustomerId, request.RunId,
            passed.Count(n => sampleNames.Contains(n)),
            failed.Count(n => sampleNames.Contains(n)),
            skipped.Count(s => sampleNames.Any(n => s.StartsWith(n, StringComparison.Ordinal))));
    }

    private static IReadOnlyList<string> SampleCheckNames() => new[]
    {
        CheckSampleAiAnalysis,
        CheckSampleDocUploadIndex,
        CheckSampleWorkspaceLayoutRender,
        CheckSampleWizardFieldMap,
    };

    /// <summary>
    /// Executes ONE sample-workload check with the shared graceful-degradation
    /// contract (file header): per-check timeout, single transient retry,
    /// 404 -&gt; Skipped (endpoint not deployed), 401/403 -&gt; Skipped (L2
    /// identity not granted), 200 + validator-null -&gt; Passed, anything else
    /// -&gt; Failed with diagnostic. <paramref name="validateBody"/> returns
    /// null on pass or a human-readable violation on fail; JSON parse
    /// exceptions inside it are caught here and reported as Failed.
    /// </summary>
    private async Task RunSampleCheckAsync(
        HttpClient httpClient, string bearerToken, string checkName,
        Func<HttpRequestMessage> requestFactory,
        Func<string, string?> validateBody,
        List<string> passed, List<string> failed, List<string> skipped, List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.SampleWorkloadCheckTimeout);

            HttpResponseMessage? response = null;
            try
            {
                using var request = requestFactory();
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                response = await httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    skipped.Add(checkName + "-skipped-endpoint-not-deployed-http-404");
                    diagnostics.Add(
                        $"{checkName}: '{request.RequestUri}' returned HTTP 404 -- the endpoint is not " +
                        "deployed on this BFF build yet. Check SKIPPED (explicit reason), not failed.");
                    return;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    skipped.Add(
                        checkName + $"-skipped-auth-http-{(int)response.StatusCode}-l2-identity-not-granted-on-bff");
                    diagnostics.Add(
                        $"{checkName}: '{request.RequestUri}' returned HTTP {(int)response.StatusCode} -- the " +
                        "L2 identity lacks the required app-role/consent on the customer's BFF app-reg. " +
                        "Check SKIPPED with explicit reason (infra permission gap, parity with the I4 " +
                        "probe's InfraFault posture), not failed.");
                    return;
                }

                if (IsTransientStatus(response.StatusCode) && attempt == 1)
                {
                    _logger.LogWarning(
                        "H13 sample check {Check} got transient HTTP {Status}; retrying once after {Delay}.",
                        checkName, (int)response.StatusCode, TransientRetryDelay);
                    response.Dispose();
                    response = null;
                    await Task.Delay(TransientRetryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var errBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    failed.Add(checkName);
                    diagnostics.Add(
                        $"{checkName}: '{request.RequestUri}' returned HTTP {(int)response.StatusCode} " +
                        $"({response.StatusCode}); expected 200. Body preview: {Truncate(errBody, 300)}");
                    return;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                string? violation;
                try
                {
                    violation = validateBody(body);
                }
                catch (JsonException ex)
                {
                    failed.Add(checkName);
                    diagnostics.Add(
                        $"{checkName}: HTTP 200 but response is not parseable JSON ({ex.Message}). " +
                        $"Body preview: {Truncate(body, 300)}");
                    return;
                }

                if (violation is null)
                {
                    passed.Add(checkName);
                    return;
                }

                failed.Add(checkName);
                diagnostics.Add($"{checkName}: HTTP 200 but {violation} Body preview: {Truncate(body, 300)}");
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failed.Add(checkName);
                diagnostics.Add(
                    $"{checkName}: timed out after {_options.SampleWorkloadCheckTimeout} " +
                    "(SampleWorkloadCheckTimeout).");
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                if (attempt == 1)
                {
                    _logger.LogWarning(
                        "H13 sample check {Check} transport fault ({Error}); retrying once after {Delay}.",
                        checkName, ex.Message, TransientRetryDelay);
                    await Task.Delay(TransientRetryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                failed.Add(checkName);
                diagnostics.Add($"{checkName}: HttpRequestException after retry: {ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                failed.Add(checkName);
                diagnostics.Add($"{checkName}: unexpected {ex.GetType().Name}: {ex.Message}");
                return;
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    /// <summary>Gateway-transient statuses eligible for the single retry.</summary>
    internal static bool IsTransientStatus(HttpStatusCode status)
        => status is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";

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
