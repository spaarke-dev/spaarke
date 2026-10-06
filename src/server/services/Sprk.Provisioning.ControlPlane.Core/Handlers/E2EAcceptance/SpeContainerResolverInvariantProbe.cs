// -----------------------------------------------------------------------------
// ⚠️ RETIRED 2026-08-26 (Wave G-6 on-disk-with-banner convention) — task 204c B07.
//
// The I4 (InvariantKind.I4SpeContainerResolver) registration in
// E2EAcceptanceModule.cs was swapped from THIS class to
// SpeContainerTenantDerivationInvariantProbe (task 204c B07) on 2026-08-26.
// Owner directive SESSION 12 2026-08-26 authorized the replacement per the
// task 204c dispatch principle: "do NOT trust RunStatus.HandlerReports;
// re-read the underlying Azure/Cosmos/Graph/SPE surface directly."
//
// The new probe reads the DEPLOYED App Service config VALUE directly via ARM
// `Microsoft.Web/sites/{name}/config/appsettings/list` and catches the
// silent-fail class this probe cannot: a compromised deploy that hardcodes
// SharePointEmbedded__ContainerTypeId in Bicep (bypassing KV) but whose
// runtime BFF diagnostic still echoes plausibly (resolvedFromLiteral=false).
// THIS probe would PASS in that scenario; the new probe FAILS by inspecting
// the app-setting value directly. See SpeContainerTenantDerivationInvariantProbe.cs
// § SILENT-FAIL AUDIT for the failure-mode delta.
//
// This class is retained on disk (Wave G-6 retirement convention) rather than
// deleted so its BFF-diagnostic coverage stays reference-visible: it verifies
// a COMPLEMENTARY class of leak (the resolver returns another tenant's
// container-id via secret misconfig, not a hardcoded Bicep literal). If a
// post-186 audit determines both angles should run in parallel, this class
// can be re-registered under a DISTINCT InvariantKind (composite disallows
// duplicate Kind, so a new `InvariantKind.I4SpeContainerRuntimeAssertion`
// or similar would need to be added).
//
// DO NOT re-register this class under the current I4SpeContainerResolver Kind
// — the composite would fail with InvalidOperationException on duplicate Kind
// registration at DI composition, breaking the entire H13 aggregate gate.
// -----------------------------------------------------------------------------
// SpeContainerResolverInvariantProbe.cs
//
// H13 I4 REAL invariant probe (task 176, Phase C'' Wave G-7 Batch G-7A2.1)
// REPLACING the wave-C4 PlaceholderInvariantVerifier's I4 branch (which
// returned InfraFault unconditionally). Composed into the aggregate
// IE2EInvariantVerifier via CompositeInvariantVerifier (task 174 seam).
// [RETIRED — SUPERSEDED BY SpeContainerTenantDerivationInvariantProbe, task 204c B07 2026-08-26]
//
// PURPOSE (spec.md FR-31 / design.md §4D I4):
//   Every SPE container ID handed to the customer's BFF Graph SDK MUST derive
//   from ITenantContainerResolver (or an IOptions bag whose value is bound
//   from the customer's KV secret / Dataverse env-var at boot) — NEVER a
//   hardcoded string literal or a fallback default. A fallback-default
//   container ID silently routes a customer's SPE uploads to another
//   customer's container — privileged documents into wrong hands
//   (CATASTROPHIC — §4D I4 rationale).
//
// WHAT THIS PROBE VERIFIES (runtime BFF diagnostic):
//   Issues a live GET against the customer's DEPLOYED BFF diagnostic endpoint:
//
//     GET  {bffApiUrl-trimmed}/api/diagnostics/tenant-container-resolver
//          ?tenantId={escaped-tenantId}
//     Authorization: Bearer {aad-token scoped to BFF app}
//     Accept: application/json
//
//   Expected response (documented BFF-side contract; §7):
//     200 OK application/json
//     {
//       "tenantId": "<echoed request tenant id>",
//       "containerId": "b!<real Graph container id (~72 chars url-safe base64)>",
//       "resolverSource": "kv" | "options" | "env",
//       "resolvedFromLiteral": false
//     }
//
//   The BFF-side handler MUST call ITenantContainerResolver.ResolveAsync on
//   the same tenant scope its production upload path uses; the probe asserts
//   the returned tuple satisfies I4:
//
//     * containerId is non-blank AND matches the canonical Graph SPE shape
//       ('b!' followed by 20+ URL-safe base64 chars — same shape the I4
//       ArchTest scans for in BFF Services/**);
//     * resolvedFromLiteral is FALSE (any 'true' means the resolver's
//       fallback-default path fired — silent-fail-trap by definition);
//     * echoed tenantId matches request.TenantId ordinal (any mismatch means
//       the resolver returned another tenant's container — CATASTROPHIC
//       cross-tenant leak, the exact failure mode I4 exists to catch).
//
// WHAT THIS PROBE CAN AND CANNOT DETECT:
//   CAN detect (Failed, CATASTROPHIC):
//     * BFF response says resolvedFromLiteral=true (the resolver was bypassed).
//     * BFF response echoes a tenantId different from the requested one
//       (resolver returned another tenant's container — cross-tenant leak).
//     * containerId is blank / not the canonical 'b!' Graph shape (resolver
//       silently returned a placeholder).
//   CANNOT detect (falls to Passed under this probe alone):
//     * Whether the SAME container id is legitimate for the tenant (would
//       require a Graph GET against the container to compare drive metadata;
//       out of scope for a per-tenant resolution invariant probe — that's
//       T6's job at a different layer).
//     * A resolver that internally memoizes a wrong id per tenant but ECHOES
//       the correct tenantId in its diagnostic response (i.e., a resolver
//       that lies to its own diagnostic). Defense-in-depth per §4D
//       expectation: the BFF-side diagnostic MUST be implemented as a live
//       ITenantContainerResolver.ResolveAsync call, not a canned mirror of
//       inputs — this is a contract obligation on the diagnostic endpoint,
//       not something the L2 probe can attest to alone. Called out here for
//       operator awareness.
//
// EDGE CASES + INFRA-FAULT DISCIPLINE (parity with sibling probes 173/174/179):
//   * request.TenantId blank                       → Failed (probe refuses
//                                                    ambient-tenant probing;
//                                                    §4D I1 defense-in-depth).
//   * request.BffApiUrl blank                      → InfraFault (H9 did not
//                                                    populate; probe cannot
//                                                    verdict without the URL).
//   * request.BffApiUrl not http(s) URL            → InfraFault.
//   * AAD token acquisition throws                 → InfraFault.
//   * HTTP call throws / times out                 → InfraFault (transient
//                                                    or endpoint unreachable).
//   * HTTP 401 / 403                               → InfraFault (L2 UAMI lacks
//                                                    RBAC on the BFF app —
//                                                    an infra gap, not an I4
//                                                    semantic violation).
//   * HTTP 404                                     → InfraFault (BFF diagnostic
//                                                    endpoint not yet deployed
//                                                    — the LIVE-verification
//                                                    gap task 185/186 will
//                                                    close per POML escalation
//                                                    trigger; until then the
//                                                    probe SURFACES the gap
//                                                    as Resumable, NOT a
//                                                    false Pass).
//   * HTTP 5xx / other non-2xx                     → InfraFault.
//   * HTTP 200 with malformed JSON                 → InfraFault.
//   * HTTP 200 with missing required fields        → InfraFault.
//   * HTTP 200 with resolvedFromLiteral == true    → Failed CATASTROPHIC.
//   * HTTP 200 with echoed tenantId mismatch       → Failed CATASTROPHIC.
//   * HTTP 200 with containerId not 'b!' shape     → Failed.
//
// SILENT-FAIL AUDIT (per task dispatch directive — silent tenant-data-leak
// trap for container resolvers):
//   The probe's raison d'être is exactly to prevent the resolver-substitution
//   silent-fail. Every Failed branch above corresponds to a CONCRETE observed
//   symptom that would otherwise route a customer's file uploads to another
//   customer's container. No Failed branch is speculative — each is directly
//   the shape §4D I4 forbids.
//
// LIVE-VS-FAKES POSTURE (per POML <escalation>):
//   Task 132 (H9) landed the artifact-based BFF rebuild handler, but the
//   dedicated `/api/diagnostics/tenant-container-resolver` endpoint on the
//   BFF is a FUTURE deliverable (task 185's H13 acceptance-assembly is the
//   terminal owner for wiring this end-to-end; ITenantContainerResolver
//   itself is not yet in the BFF codebase per repo-wide grep). This L2-side
//   probe is authored + unit-tested against a fake HttpMessageHandler NOW so
//   it drops into the composite verifier and stops returning
//   InvariantProbeDeferralMessages.DeferralDiagnostic for I4; the LIVE
//   verification against a real deployed BFF is deferred to Phase F rerun
//   (task 186) per the escalation trigger. The BFF-side endpoint contract is
//   documented in this file's header so the future implementer builds to it.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   Sprk.Provisioning.ControlPlane.Core (L2, not BFF). Consumes NO
//   AI-internal types (ADR-013). No BFF-facade dependencies. No shell-out.
//
// COMPONENT JUSTIFICATION (CLAUDE.md §11):
//   Existing: PlaceholderInvariantVerifier / CompositeInvariantVerifier
//     surface for I4 returns the deferral diagnostic. No real probe existed.
//   Extension: implementing IInvariantProbe (shared per-invariant seam
//     authored by sibling task 174) IS the minimum extension move — same
//     contract, adds ONE line in E2EAcceptanceModule.cs.
//   Cost-of-doing-nothing: I4 stays Resumable forever; every customer stamp
//     reaching H13 sees the deferral diagnostic for I4 alone; the CATASTROPHIC
//     cross-tenant SPE-container-leak class of failure has NO onboarding-time
//     detection — the exact silent-fail spec.md §4D I4 / FR-31 mandates
//     catching before Ready.
//
// ADR ALIGNMENT:
//   * ADR-028 UAMI outbound: probe uses the shared TokenCredential singleton
//     (DefaultAzureCredential pinned to L2 UAMI) — parity with sibling probes
//     173 (I2) and 174 (I3). Zero admin-key handling.
//   * ADR-032 (unconditional registration): registered UNCONDITIONALLY in
//     E2EAcceptanceModule; no feature-gate branch.
//   * ADR-038 (integration-heavy pyramid): tests exercise the probe against a
//     hand-rolled FakeHttpMessageHandler (never Mock<HttpMessageHandler>),
//     hand-rolled FakeTokenCredential — parity with
//     AiSearchTenantFilterInvariantProbeTests (task 173).
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Runtime <see cref="IInvariantProbe"/> for
/// <see cref="InvariantKind.I4SpeContainerResolver"/> — issues a live GET
/// against the customer's BFF diagnostic endpoint exercising
/// <c>ITenantContainerResolver</c> and asserts the resolved container-id is
/// derived from tenant context, not from a hardcoded literal / fallback
/// default. See file header for the honest can-vs-cannot-detect breakdown +
/// the documented BFF-side diagnostic endpoint contract.
/// </summary>
public sealed class SpeContainerResolverInvariantProbe : IInvariantProbe
{
    /// <summary>
    /// Named HttpClient key the DI module registers so the probe can pull an
    /// isolated client via <see cref="IHttpClientFactory"/> (parity with
    /// <see cref="AiSearchTenantFilterInvariantProbe.HttpClientName"/>).
    /// </summary>
    public const string HttpClientName = "H13-I4-SpeContainerResolverProbe";

    /// <summary>
    /// Diagnostic endpoint path the probe GETs on the customer's BFF. The
    /// BFF-side handler MUST call <c>ITenantContainerResolver.ResolveAsync</c>
    /// on the same tenant scope its production upload path uses (contract).
    /// </summary>
    public const string DiagnosticEndpointPath = "/api/diagnostics/tenant-container-resolver";

    /// <summary>
    /// Canonical Graph SPE container-id regex — matches the same shape the
    /// I4 ArchTest scans for in BFF Services/** (parity to avoid probe-side
    /// drift from the compile-time gate). 'b!' + 20+ URL-safe base64 chars.
    /// </summary>
    private static readonly Regex CanonicalContainerIdShape = new(
        @"^b![A-Za-z0-9_\-]{20,}$",
        RegexOptions.Compiled);

    /// <summary>
    /// URL scheme allow-list — accepts only http(s). Any other scheme (file://,
    /// ftp://) means BffApiUrl was populated from a malformed source and is
    /// surfaced as InfraFault rather than probed blindly.
    /// </summary>
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp, Uri.UriSchemeHttps,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenCredential _credential;
    private readonly H13AcceptanceOptions _options;
    private readonly ILogger<SpeContainerResolverInvariantProbe> _logger;

    /// <inheritdoc/>
    public InvariantKind Kind => InvariantKind.I4SpeContainerResolver;

    /// <summary>Constructs the probe. All collaborators are seams (ADR-010 ≥2 impls per test suite).</summary>
    public SpeContainerResolverInvariantProbe(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        IOptions<H13AcceptanceOptions> options,
        ILogger<SpeContainerResolverInvariantProbe> logger)
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
    public async Task<InvariantVerificationOutcome> ProbeAsync(
        InvariantVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // (1) Pre-flight guards.
        //
        // Blank tenantId is a FAILED verdict — the probe refuses to exercise
        // an ambient/default-tenant call even if the runtime allowed one
        // (parity with I5's identical posture; §4D I1 defense-in-depth).
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            _logger.LogWarning(
                "I4 probe FAILED — blank tenantId (customerId={CustomerId} runId={RunId}). " +
                "The probe refuses to exercise ambient/default-tenant SPE container resolution.",
                request.CustomerId, request.RunId);
            return Failed(
                "observed=blank tenantId; expected=an explicit Entra tenant GUID. " +
                "§4D I1 defense-in-depth: the probe refuses to attempt an ambient-tenant SPE " +
                "container resolution — a resolver returning ANY container id under a blank " +
                "tenant scope is by definition the fallback-default silent-fail this invariant catches.");
        }

        if (string.IsNullOrWhiteSpace(request.BffApiUrl))
        {
            return InfraFault(
                "request.BffApiUrl is empty — cannot reach the BFF diagnostic endpoint. " +
                "H9 must have populated bffApiUrl on the run before H13 runs; the parent " +
                "handler is expected to guard this but I4 defense-in-depth surfaces it here too.");
        }

        if (!Uri.TryCreate(request.BffApiUrl.Trim(), UriKind.Absolute, out var baseUri)
            || !AllowedSchemes.Contains(baseUri.Scheme))
        {
            return InfraFault(
                $"request.BffApiUrl '{request.BffApiUrl}' is not a valid http(s) absolute URL — " +
                "cannot construct the diagnostic endpoint request.");
        }

        // (2) Build the diagnostic URL.
        var endpoint = new Uri(
            baseUri.GetLeftPart(UriPartial.Authority)
            + DiagnosticEndpointPath
            + "?tenantId=" + Uri.EscapeDataString(request.TenantId));

        // (3) Acquire an AAD token scoped to the BFF app. The scope derived
        //     from the BFF's authority ('{scheme}://{host}[:port]/.default')
        //     matches the Azure App Service default audience convention. The
        //     L2 UAMI must have a role/consent on the BFF app for this call
        //     to succeed at runtime; a 401/403 surfaces as InfraFault so
        //     RBAC gaps are reported explicitly rather than silently
        //     mistaken for I4 semantic violations.
        var scope = baseUri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/.default";
        AccessToken token;
        try
        {
            token = await _credential
                .GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return InfraFault(
                $"AAD token acquisition for BFF scope '{scope}' failed: {ex.GetType().Name}: {ex.Message}. " +
                "Cannot verdict I4 without a bearer token — handler classifies Resumable.");
        }

        // (4) Issue the GET.
        using var request2 = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request2.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.InvariantVerifierTimeout);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request2, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return InfraFault(
                $"BFF diagnostic call to '{endpoint}' timed out after {_options.InvariantVerifierTimeout}. " +
                "BFF may be under-provisioned or the endpoint may not be deployed yet.");
        }
        catch (HttpRequestException ex)
        {
            return InfraFault(
                $"BFF diagnostic call to '{endpoint}' failed with {ex.GetType().Name}: {ex.Message}. " +
                "BFF endpoint may be unreachable from the L2 host.");
        }

        try
        {
            var respBody = await response.Content
                .ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return InfraFault(
                    $"BFF diagnostic endpoint '{DiagnosticEndpointPath}' returned HTTP 404 at '{endpoint.Authority}'. " +
                    "The BFF-side diagnostic endpoint is not yet deployed. Task 185/186 close this " +
                    "gap per the POML escalation trigger; until then I4 stays Resumable so H13 does " +
                    "NOT falsely mark Ready with the CATASTROPHIC I4 invariant unverified.");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                return InfraFault(
                    $"BFF diagnostic endpoint returned HTTP {(int)response.StatusCode} ({response.StatusCode}) for GET '{endpoint}'. " +
                    "The L2 UAMI likely lacks the required app-role/consent on the customer's BFF app-reg. " +
                    "This is an infra permission issue, not an I4 semantic violation.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return InfraFault(
                    $"BFF diagnostic endpoint returned HTTP {(int)response.StatusCode} for GET '{endpoint}': " +
                    Truncate(respBody, 400));
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(respBody);
            }
            catch (JsonException ex)
            {
                return InfraFault(
                    $"BFF diagnostic response for GET '{endpoint}' is not parseable JSON: {ex.Message}. " +
                    "Body preview: " + Truncate(respBody, 300));
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return InfraFault(
                        $"BFF diagnostic response for GET '{endpoint}' is not a JSON object. Body: " +
                        Truncate(respBody, 300));
                }

                // Required fields.
                if (!TryGetString(root, "containerId", out var containerId))
                {
                    return InfraFault(
                        "BFF diagnostic response missing required 'containerId' field. " +
                        "Cannot verdict I4 without the resolver's returned value. Body: " +
                        Truncate(respBody, 300));
                }
                if (!TryGetString(root, "tenantId", out var echoedTenantId))
                {
                    return InfraFault(
                        "BFF diagnostic response missing required 'tenantId' echo field. " +
                        "Cannot verify tenant-scoping without the echoed value. Body: " +
                        Truncate(respBody, 300));
                }

                // OPTIONAL: resolvedFromLiteral flag — if present + true, that's
                // an explicit CATASTROPHIC signal from the BFF-side handler
                // (the ITenantContainerResolver's fallback-default path fired).
                if (root.TryGetProperty("resolvedFromLiteral", out var literalEl))
                {
                    if (literalEl.ValueKind == JsonValueKind.True)
                    {
                        return Failed(
                            "CATASTROPHIC — BFF diagnostic reports resolvedFromLiteral=true. " +
                            "The customer's BFF is resolving SPE container id via a hardcoded literal / fallback " +
                            "default path instead of ITenantContainerResolver. §4D I4 (FR-31) CATASTROPHIC — " +
                            "customer's SPE uploads would route to another customer's container. " +
                            $"Diagnostic body: {Truncate(respBody, 400)}");
                    }
                    if (literalEl.ValueKind != JsonValueKind.False)
                    {
                        return InfraFault(
                            $"BFF diagnostic 'resolvedFromLiteral' has non-boolean kind {literalEl.ValueKind}. " +
                            "Contract violation — cannot verdict I4 from a malformed flag.");
                    }
                }

                // Echoed tenantId MUST match request.TenantId ordinally. A
                // mismatch means the resolver returned a container id scoped
                // to a DIFFERENT tenant — the exact cross-tenant SPE-container
                // leak this invariant catches.
                if (!string.Equals(echoedTenantId, request.TenantId, StringComparison.Ordinal))
                {
                    return Failed(
                        $"CATASTROPHIC — BFF diagnostic echoed tenantId='{echoedTenantId}' does NOT match " +
                        $"requested tenantId='{request.TenantId}'. The resolver returned another tenant's " +
                        "SPE container id — customer's file uploads would route to another customer's container. " +
                        "§4D I4 (FR-31) CATASTROPHIC — QuarantineRequired.");
                }

                // Container id must be non-blank + match the canonical Graph
                // SPE shape ('b!' + 20+ url-safe base64 chars, parity with the
                // I4 ArchTest regex).
                if (string.IsNullOrWhiteSpace(containerId))
                {
                    return Failed(
                        "BFF diagnostic returned an EMPTY containerId — the resolver silently returned a blank " +
                        "value where an SPE container id is required. §4D I4 (FR-31) — the resolver failed " +
                        "silently; any upload built from this value would target no container / fail unpredictably.");
                }
                if (!CanonicalContainerIdShape.IsMatch(containerId))
                {
                    // Truncate the observed value in the diagnostic to avoid
                    // echoing an eventual real container id into logs — parity
                    // with the I4 ArchTest's identical defense-in-depth posture.
                    var displayed = containerId.Length <= 30
                        ? containerId
                        : containerId.Substring(0, 20) + "...[truncated]";
                    return Failed(
                        $"BFF diagnostic returned containerId='{displayed}' — does NOT match the canonical Graph " +
                        "SPE container-id shape ('b!' + 20+ URL-safe base64 chars). §4D I4 (FR-31) — the resolver " +
                        "returned a placeholder / literal / malformed value; a real ITenantContainerResolver " +
                        "response would satisfy the shape by construction.");
                }
            }
        }
        finally
        {
            response.Dispose();
        }

        _logger.LogInformation(
            "H13 I4 probe passed: customerId={CustomerId} runId={RunId} tenantId={TenantId} endpoint={Endpoint}",
            request.CustomerId, request.RunId, request.TenantId, endpoint);
        return new InvariantVerificationOutcome.Passed(InvariantKind.I4SpeContainerResolver);
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        if (root.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString() ?? string.Empty;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private InvariantVerificationOutcome Failed(string diagnostic)
    {
        _logger.LogWarning("H13 I4 probe FAILED: {Diagnostic}", diagnostic);
        return new InvariantVerificationOutcome.Failed(InvariantKind.I4SpeContainerResolver, diagnostic);
    }

    private InvariantVerificationOutcome InfraFault(string diagnostic)
    {
        _logger.LogWarning("H13 I4 probe InfraFault: {Diagnostic}", diagnostic);
        return new InvariantVerificationOutcome.InfraFault(InvariantKind.I4SpeContainerResolver, diagnostic);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
