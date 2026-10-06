// -----------------------------------------------------------------------------
// E2EValidationRunner.cs
//
// H13's live checks against the deployed CUSTOMER BFF (spec.md SC #5) — pure C#, HttpClient only (no pwsh; the L2
// Worker host has none). Origin: task 181's port of scripts/Validate-DeployedEnvironment.ps1 (which stays on disk as
// the operator's off-cluster diagnostic).
//
// WHAT IT CHECKS:
//   1. bff-healthz-200          GET  {bffApiUrl}/healthz   → HTTP 200
//   2. bff-ping-200             GET  {bffApiUrl}/ping      → HTTP 200
//   3. cors-dataverse-origin    OPTIONS {bffApiUrl}/healthz with Origin: {dataverseUrl} → Access-Control-Allow-Origin
//                               equals {dataverseUrl} or '*' (the .ps1's predicate)
//   4. keyless-proof-{service}  POST {bffApiUrl}/api/platform/keyless-proof (task 230b, owner D13), authenticated as the
//                               L2 Worker identity with a token for api://{BffAppRegId} — the audience the BFF
//                               validates. The BFF calls each Azure service of its stamp with ITS OWN managed identity
//                               and reports one result per service; each service is one check, passed only when
//                               "proved". The openai-chat result is the ADR-028 E-2 measurement on the stamp's
//                               kind: OpenAI account (logged as such).
//
// FAIL-CLOSED (task 230b): an auth refusal is a FAILURE, never a skip — the role missing on the BFF (401/403), no
//   token, a service that refused the stamp identity, a key configured instead of the identity, a missing setting, a
//   service error, an unparseable answer, or a BFF build without the route (404). Only a fault with no verdict —
//   transport, timeout, throttling, 5xx, or a service the BFF could not reach — is INCONCLUSIVE (H13: Resumable).
//
// REMOVED BY TASK 230b — the four G-8 Batch 11 "sample-workload" checks (POST /api/agent/message, POST
//   /api/ai/search/count scope=all, GET /api/workspace/layouts, GET /api/v1/field-mappings/profiles). Each is a USER
//   workflow: the agent and layout routes need a signed-in user, and a tenant-wide search count has been refused
//   outright since unified-access-control-r2 task 070. With the L2 Worker's app-only token they could never pass, so
//   their 401/403 was reported as a "skip" — and skips never failed H13, so none of them ever ran for real. Turning
//   that skip into a failure (task 230b's rule) would have failed every stamp. What they were meant to prove — the
//   AI, search and storage round-trips work on the deployed stamp — is what the keyless proof proves, per service.
//
// WHAT IT DOES NOT CHECK PER RUN (reported in ChecksSkipped so the outcome is honest):
//   * dataverse-env-vars-present / dataverse-env-vars-dev-leakage — need Dataverse auth as the customer environment;
//     H6/H7 own those values (the .ps1 still checks them off-cluster).
//   * naming-conformance — a repo lint, enforced once as a blocking CI step (task 230a).
//
// PLACEMENT (CLAUDE.md §10): Sprk.Provisioning.ControlPlane.Core (L2, not the BFF). Tests use a hand-rolled
//   FakeHttpMessageHandler (never Mock<HttpMessageHandler>).
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Options;
using Spaarke.Contracts.Provisioning;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <inheritdoc cref="IE2EValidationRunner"/>
public sealed class E2EValidationRunner : IE2EValidationRunner
{
    /// <summary>Named HttpClient key the DI module registers (parity with the sibling H13 probes' named clients).</summary>
    public const string HttpClientName = "H13-E2EValidationRunner";

    /// <summary>Check name for the BFF /healthz probe. Parity with the .ps1's Test-BffApiHealth.</summary>
    public const string CheckBffHealthz = "bff-healthz-200";

    /// <summary>Check name for the BFF /ping probe. Parity with the .ps1's Test-BffApiHealth.</summary>
    public const string CheckBffPing = "bff-ping-200";

    /// <summary>Check name for the CORS origin preflight. Parity with the .ps1's Test-CorsOrigin.</summary>
    public const string CheckCorsDataverseOrigin = "cors-dataverse-origin";

    /// <summary>Check name for the keyless-proof CALL itself (token, route, response) — task 230b.</summary>
    public const string CheckKeylessProof = "keyless-proof";

    /// <summary>Prefix of the per-service keyless-proof check names (<c>keyless-proof-{service}</c>).</summary>
    public const string KeylessProofCheckPrefix = "keyless-proof-";

    /// <summary>Skipped: the .ps1's Test-DataverseEnvironmentVariables (needs Dataverse auth as the customer environment).</summary>
    public const string SkippedDataverseEnvVarsPresent = "dataverse-env-vars-present";

    /// <summary>Skipped: the .ps1's Test-DevValueLeakage (depends on the env-vars read).</summary>
    public const string SkippedDataverseEnvVarsDevLeakage = "dataverse-env-vars-dev-leakage";

    /// <summary>Skipped: naming conformance — a repo lint enforced once in CI, not per run (task 230a).</summary>
    public const string SkippedNamingConformance = "naming-conformance-enforced-in-ci";

    /// <summary>Path suffix used for BFF health probe. Anonymous endpoint per BFF Program.cs.</summary>
    public const string HealthzPath = "/healthz";

    /// <summary>Path suffix used for BFF ping probe. Anonymous endpoint per BFF Program.cs.</summary>
    public const string PingPath = "/ping";

    /// <summary>Delay before the single transient-fault retry of the keyless-proof call. Internal so tests can zero it.</summary>
    internal TimeSpan TransientRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>URL scheme allow-list — only http(s), parity with the sibling probes.</summary>
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp, Uri.UriSchemeHttps,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenCredential _credential;
    private readonly H13AcceptanceOptions _options;
    private readonly ILogger<E2EValidationRunner> _logger;

    /// <summary>
    /// Constructs the runner. <paramref name="credential"/> is the shared UAMI-pinned <see cref="TokenCredential"/>
    /// (the L2 Worker identity, ADR-028) — used only to request the keyless-proof token for the customer's BFF.
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

        var passed = new List<string>();
        var failed = new List<string>();
        var inconclusive = new List<string>();
        var diagnostics = new List<string>();
        var skipped = new List<string>(BuildInterimSkippedList());

        // Pre-flight: BffApiUrl must be a valid absolute http(s) URL. Blank or malformed values fail every
        // BFF-dependent check; no exception, so the handler receives a typed Failure.
        if (string.IsNullOrWhiteSpace(request.BffApiUrl))
        {
            failed.AddRange(AllBffDependentCheckNames());
            diagnostics.Add(
                "BffApiUrl parameter is empty -- cannot exercise the BFF /healthz, /ping, CORS and keyless-proof " +
                "checks. H9 must have populated bffApiUrl on the run before H13 runs.");
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

        var dataverseUrlTrimmed = request.DataverseUrl?.TrimEnd('/') ?? string.Empty;
        var dataverseUrlValid = !string.IsNullOrWhiteSpace(dataverseUrlTrimmed)
            && Uri.TryCreate(dataverseUrlTrimmed, UriKind.Absolute, out var _);

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        _logger.LogInformation(
            "H13 E2E validation starting: customerId={CustomerId} runId={RunId} bffApiUrl={BffApiUrl} " +
            "dataverseUrl={DataverseUrl} slot={Slot}",
            request.CustomerId, request.RunId, bffApiUrlTrimmed, dataverseUrlTrimmed, request.TargetSlotName);

        // 1. GET /healthz
        var healthzUri = new Uri(bffBaseUri.GetLeftPart(UriPartial.Authority) + HealthzPath);
        await RunGetOkProbeAsync(httpClient, healthzUri, CheckBffHealthz,
            passed, failed, diagnostics, cancellationToken).ConfigureAwait(false);

        // 2. GET /ping
        var pingUri = new Uri(bffBaseUri.GetLeftPart(UriPartial.Authority) + PingPath);
        await RunGetOkProbeAsync(httpClient, pingUri, CheckBffPing,
            passed, failed, diagnostics, cancellationToken).ConfigureAwait(false);

        // 3. OPTIONS /healthz with Origin: {dataverseUrl}
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

        // 4. Keyless proof (task 230b) — one check per stamp service.
        await RunKeylessProofAsync(
            httpClient, bffBaseUri, request,
            passed, failed, inconclusive, diagnostics, cancellationToken).ConfigureAwait(false);

        if (failed.Count > 0)
        {
            var diag = $"E2EValidationRunner reported {failed.Count} failing check(s): " + string.Join(" | ", diagnostics);
            _logger.LogWarning(
                "H13 E2E validation FAILED: customerId={CustomerId} runId={RunId} failed={FailedCount} diagnostic={Diagnostic}",
                request.CustomerId, request.RunId, failed.Count, diag);
            return new E2EValidationOutcome.Failure(failed, diag);
        }

        if (inconclusive.Count > 0)
        {
            var diag = $"E2EValidationRunner could not reach a verdict for {inconclusive.Count} check(s): " +
                       string.Join(" | ", diagnostics);
            _logger.LogWarning(
                "H13 E2E validation INCONCLUSIVE: customerId={CustomerId} runId={RunId} diagnostic={Diagnostic}",
                request.CustomerId, request.RunId, diag);
            return new E2EValidationOutcome.Inconclusive(inconclusive, diag);
        }

        _logger.LogInformation(
            "H13 E2E validation PASSED: customerId={CustomerId} runId={RunId} passed={PassedCount} skipped={SkippedCount}",
            request.CustomerId, request.RunId, passed.Count, skipped.Count);
        return new E2EValidationOutcome.Success(passed, skipped);
    }

    /// <summary>
    /// The checks this runner never performs per run, each named with its reason. Exposed internal so tests pin the
    /// shape.
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

    /// <summary>Every check that depends on a reachable, well-formed BffApiUrl.</summary>
    internal static IReadOnlyList<string> AllBffDependentCheckNames()
    {
        var names = new List<string> { CheckBffHealthz, CheckBffPing, CheckCorsDataverseOrigin, CheckKeylessProof };
        names.AddRange(KeylessProofContract.Services.All.Select(s => KeylessProofCheckPrefix + s));
        return names;
    }

    // ------------------------------------------------------------------
    // Keyless proof (task 230b)
    // ------------------------------------------------------------------

    /// <summary>
    /// Calls the customer BFF's keyless proof as the L2 Worker identity and records one check per service. See the
    /// file header for the fail-closed rule.
    /// </summary>
    private async Task RunKeylessProofAsync(
        HttpClient httpClient, Uri bffBaseUri, E2EValidationRequest request,
        List<string> passed, List<string> failed, List<string> inconclusive, List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.BffAppRegId, out var appId) || appId == Guid.Empty)
        {
            failed.Add(CheckKeylessProof);
            diagnostics.Add(
                $"{CheckKeylessProof}: BffAppRegId '{request.BffAppRegId}' is not an app (client) id -- H3 writes it, and " +
                "the keyless-proof token is requested for api://{BffAppRegId}.");
            return;
        }

        // The audience the BFF validates (AzureAd:ClientId / api://{clientId}); a token for the host name is not it.
        var scope = $"api://{appId:D}/.default";
        string bearerToken;
        try
        {
            var token = await _credential
                .GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken)
                .ConfigureAwait(false);
            bearerToken = token.Token;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // An auth failure, never a skip (task 230b).
            failed.Add(CheckKeylessProof);
            diagnostics.Add(
                $"{CheckKeylessProof}: the L2 identity could not get a token for '{scope}' ({ex.GetType().Name}: " +
                $"{ex.Message}). Check H3 created the app registration with identifier URI api://{appId:D}.");
            return;
        }

        var uri = new Uri(bffBaseUri.GetLeftPart(UriPartial.Authority) + KeylessProofContract.Route);
        string? body = null;
        for (var attempt = 1; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.KeylessProofTimeout);
            try
            {
                using var call = new HttpRequestMessage(HttpMethod.Post, uri);
                call.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                call.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await httpClient.SendAsync(call, timeout.Token).ConfigureAwait(false);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    failed.Add(CheckKeylessProof);
                    diagnostics.Add(
                        $"{CheckKeylessProof}: the customer BFF refused the L2 identity (HTTP {(int)response.StatusCode}). " +
                        $"H3 assigns the L2 Worker identity the '{KeylessProofContract.AppRoleValue}' app role on the BFF " +
                        "app registration; check that assignment and that the token audience is the BFF's.");
                    return;
                }
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    failed.Add(CheckKeylessProof);
                    diagnostics.Add(
                        $"{CheckKeylessProof}: '{uri}' returned HTTP 404 -- the deployed BFF build has no keyless-proof route. " +
                        "Deploy a build that contains task 230b (H9), then resume.");
                    return;
                }
                if (IsTransientStatus(response.StatusCode) && attempt == 1)
                {
                    await Task.Delay(TransientRetryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (IsTransientStatus(response.StatusCode))
                {
                    inconclusive.Add(CheckKeylessProof);
                    diagnostics.Add($"{CheckKeylessProof}: '{uri}' returned HTTP {(int)response.StatusCode} after one retry.");
                    return;
                }
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    failed.Add(CheckKeylessProof);
                    diagnostics.Add($"{CheckKeylessProof}: '{uri}' returned HTTP {(int)response.StatusCode}; expected 200.");
                    return;
                }

                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                inconclusive.Add(CheckKeylessProof);
                diagnostics.Add($"{CheckKeylessProof}: no answer within {_options.KeylessProofTimeout} (KeylessProofTimeout).");
                return;
            }
            catch (HttpRequestException ex) when (attempt == 1)
            {
                _logger.LogWarning("H13 keyless proof transport fault ({Error}); retrying once.", ex.Message);
                await Task.Delay(TransientRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                inconclusive.Add(CheckKeylessProof);
                diagnostics.Add($"{CheckKeylessProof}: transport fault after one retry: {ex.Message}");
                return;
            }
        }

        IReadOnlyDictionary<string, ProofEntry> entries;
        try
        {
            entries = ParseProof(body);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            failed.Add(CheckKeylessProof);
            diagnostics.Add($"{CheckKeylessProof}: the BFF's answer is not the keyless-proof shape ({ex.GetType().Name}).");
            return;
        }

        passed.Add(CheckKeylessProof);
        foreach (var service in KeylessProofContract.Services.All)
        {
            var check = KeylessProofCheckPrefix + service;
            if (!entries.TryGetValue(service, out var entry))
            {
                failed.Add(check);
                diagnostics.Add($"{check}: the BFF reported no result for this service.");
                continue;
            }

            switch (entry.Outcome)
            {
                case KeylessProofContract.Outcomes.Proved:
                    passed.Add(check);
                    break;
                case KeylessProofContract.Outcomes.Unreachable:
                    inconclusive.Add(check);
                    diagnostics.Add($"{check}: unreachable ({entry.Code}) -- no verdict on the identity.");
                    break;
                default:
                    // refused, key-credential, not-configured, failed — and any outcome this L2 build does not know.
                    failed.Add(check);
                    diagnostics.Add($"{check}: {entry.Outcome} ({entry.Code}{(entry.StatusCode is { } s ? $", HTTP {s}" : string.Empty)}).");
                    break;
            }
        }

        if (entries.TryGetValue(KeylessProofContract.Services.OpenAiChat, out var chat))
        {
            // ADR-028 E-2 was measured on the shared dev AIServices account; this is the stamp's kind: OpenAI account.
            _logger.LogInformation(
                "ADR-028 E-2 measurement (stamp kind: OpenAI, managed identity): customerId={CustomerId} runId={RunId} " +
                "openai-chat={Outcome} code={Code} status={Status}",
                request.CustomerId, request.RunId, chat.Outcome, chat.Code, chat.StatusCode);
        }
    }

    private sealed record ProofEntry(string Outcome, string Code, int? StatusCode);

    /// <summary>Parses the BFF's keyless-proof response (<c>{"services":[{"service","outcome","statusCode","elapsedMs","code"}]}</c>).</summary>
    private static Dictionary<string, ProofEntry> ParseProof(string? body)
    {
        using var doc = JsonDocument.Parse(body ?? string.Empty);
        var entries = new Dictionary<string, ProofEntry>(StringComparer.Ordinal);
        foreach (var item in doc.RootElement.GetProperty("services").EnumerateArray())
        {
            var service = item.GetProperty("service").GetString() ?? string.Empty;
            var outcome = item.GetProperty("outcome").GetString() ?? string.Empty;
            var code = item.TryGetProperty("code", out var c) ? c.GetString() ?? string.Empty : string.Empty;
            int? status = item.TryGetProperty("statusCode", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : null;
            // A duplicate entry is not trusted: the worse of the two wins.
            if (entries.TryGetValue(service, out var earlier) && earlier.Outcome != KeylessProofContract.Outcomes.Proved)
            {
                continue;
            }
            entries[service] = new ProofEntry(outcome, code, status);
        }
        return entries;
    }

    /// <summary>Gateway-transient statuses eligible for the single retry, then inconclusive.</summary>
    internal static bool IsTransientStatus(HttpStatusCode status)
        => status is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.RequestTimeout;

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
