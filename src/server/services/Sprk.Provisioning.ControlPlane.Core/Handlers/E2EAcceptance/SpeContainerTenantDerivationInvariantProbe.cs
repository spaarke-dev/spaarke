// -----------------------------------------------------------------------------
// SpeContainerTenantDerivationInvariantProbe.cs
//
// H13 I4 REAL invariant probe (task 204c B07 — sub-agent authored 2026-08-26).
// INDEPENDENT re-verification variant for InvariantKind.I4SpeContainerResolver
// — replaces the earlier SpeContainerResolverInvariantProbe (task 176) whose
// verdict was mediated by the customer BFF's own /api/diagnostics/tenant-
// container-resolver endpoint. Per task 204c dispatch directive:
//
//     "INDEPENDENT re-verification: do NOT trust RunStatus.HandlerReports[X].Outcome;
//      re-read the underlying Azure/Cosmos/Graph/SPE surface directly."
//
// This probe reads the DEPLOYED App Service configuration DIRECTLY via ARM
// (`Microsoft.Web/sites/{name}/config/appsettings/list`), inspects the
// canonical SPE container-id app-setting VALUE, and verifies it is a
// `@Microsoft.KeyVault(...)` reference expression rather than a hardcoded
// container-id literal — the runtime static-config assertion §4D I4 (FR-31)
// mandates. This runs even when the BFF diagnostic endpoint is unreachable
// (task 176's biggest limitation), because it inspects the App Service
// configuration surface, not the BFF's own responses.
//
// PURPOSE (spec.md FR-31 / design.md §4D I4):
//   Every SPE container ID handed to the customer BFF's Graph SDK MUST derive
//   from tenant-scoped storage (KV secret / IOptions bag bound from KV / env
//   var at boot) — NEVER a hardcoded string literal or a fallback default. A
//   fallback-default SPE container ID silently routes a customer's SPE uploads
//   to another customer's container — privileged documents into wrong hands
//   (CATASTROPHIC — §4D I4 rationale).
//
// WHAT THIS PROBE VERIFIES (independent ARM-config read):
//   1. GET `https://management.azure.com/subscriptions/{sub}/providers/Microsoft.Web/sites?api-version=2022-03-01`
//      — enumerate App Services in the customer subscription; match on name
//      derived from BffApiUrl hostname (e.g., `bff-acme.azurewebsites.net`
//      → `bff-acme`).
//   2. POST `https://management.azure.com{siteResourceId}/config/appsettings/list?api-version=2022-03-01`
//      — read app-settings dictionary (POST-not-GET is intentional: the App
//      Service management API only exposes secret app-settings values via a
//      POST /list operation).
//   3. Look for the CANONICAL `SharePointEmbedded__ContainerTypeId` setting
//      (colon in code → double-underscore in App Service app-settings, per
//      ASP.NET Core config-provider convention; see
//      Sprk.Bff.Api/Configuration/SharePointEmbeddedOptions.cs).
//   4. Classify the VALUE:
//      * `@Microsoft.KeyVault(SecretUri=https://{vault}.vault.azure.net/...)`
//        → PASSED (tenant-derived from KV — the compliant lookup pattern
//        documented by the I4 ArchTest, tests/Spaarke.ArchTests/TenantIsolation/
//        I4_SpeContainerIdLiteralTests.cs).
//      * Empty/whitespace / missing key → FAILED (blank; the resolver has no
//        source and would fall back to any wired default at BFF boot).
//      * Canonical Graph SPE container-id literal (`b!` + 20+ URL-safe base64
//        chars, matching the I4 ArchTest regex) → FAILED CATASTROPHIC (the
//        exact class of bug I4 exists to catch — inline literal instead of
//        KV reference).
//      * Any other value shape (looks like an option-string, a partial URL,
//        etc.) → FAILED (unexpected value; not a KV reference; assume worst).
//
// WHAT THIS PROBE CAN AND CANNOT DETECT:
//   CAN detect (Failed, CATASTROPHIC):
//     * App-setting value is a canonical `b!...` SPE container id literal
//       (hardcoded value in Bicep / deploy pipeline, bypassing KV).
//     * App-setting is missing / empty (BFF would fail-fast at boot OR fall
//       back to a compiled-in default — either way, no tenant derivation).
//   CAN detect (Failed):
//     * App-setting value is a non-KV-reference string that isn't the SPE
//       shape — the value is not sourced from KV even if it happens to be
//       the right container id, so the tenant-derivation invariant is not
//       met (the resolver isn't reading the tenant-scoped secret at runtime).
//   CAN detect (InfraFault):
//     * ARM enumeration fails (401/403/404/5xx) — L2 UAMI lacks Reader RBAC
//       on the customer subscription, or the App Service doesn't exist yet
//       (H9 hasn't deployed / DNS not propagated).
//     * Matching App Service not found (BffApiUrl points at a non-existent
//       or foreign App Service — probe classifies Resumable so the operator
//       can investigate the BFF URL misconfig without a false Pass).
//   CANNOT detect (falls to Passed under this probe alone):
//     * A KV reference that resolves at runtime to a wrong-tenant secret (the
//       vault URI *shape* is a KV reference, but the referenced secret happens
//       to be another customer's — would require cross-checking the secret
//       value at runtime; that's task 176's SpeContainerResolverInvariantProbe
//       coverage, not this probe's). This probe + task 176's probe are
//       COMPLEMENTARY; when both wire in parallel via distinct kinds, both
//       run. When they overlap on I4, task 204c's B07 dispatch keeps THIS
//       probe (independent re-verification) as the I4 registration and
//       retires task 176's registration (see § SILENT-FAIL AUDIT below).
//
// SILENT-FAIL AUDIT (§4D CATASTROPHIC class prevented by this probe):
//   The failure mode this probe catches is a compromised deploy that ships
//   a BFF whose ContainerTypeId is set to a HARDCODED container id in Bicep
//   (bypassing the KV secret / tenant-scoped derivation) — but whose runtime
//   `ITenantContainerResolver` diagnostic still returns a plausible-looking
//   response (resolvedFromLiteral=false, echoed tenantId matching the query)
//   because the resolver implementation itself was compromised or misordered
//   in DI so it echoes rather than resolves. Task 176's BFF-diagnostic probe
//   would PASS in that scenario (the BFF lies to itself); THIS probe FAILS
//   (the deployed app-setting VALUE reveals the hardcoding directly). That's
//   the "assert EFFECTS not intentions" R7 principle applied to I4: read the
//   deployed configuration, do not trust the runtime's own self-report.
//
// EDGE CASES + INFRA-FAULT DISCIPLINE (parity with sibling probes 171/174/179):
//   * request.TenantId blank                       → Failed (§4D I1 defense-
//                                                    in-depth: probe refuses
//                                                    ambient/default-tenant
//                                                    verification).
//   * request.SubscriptionId blank                 → InfraFault.
//   * request.BffApiUrl blank                      → InfraFault.
//   * request.BffApiUrl not http(s) URL            → InfraFault.
//   * BffApiUrl hostname does not follow the App
//     Service `.azurewebsites.net` convention      → InfraFault (custom-domain
//                                                    setups need explicit
//                                                    plumbing that this MVP
//                                                    probe intentionally does
//                                                    not attempt; documented
//                                                    limitation).
//   * ARM token acquisition throws                 → InfraFault.
//   * ARM enumeration times out / throws           → InfraFault.
//   * ARM enumeration 401 / 403                    → InfraFault (RBAC gap).
//   * ARM enumeration 404 / no matching site       → InfraFault (site not yet
//                                                    provisioned or foreign
//                                                    BffApiUrl).
//   * ARM app-settings list 401 / 403 / 404 / 5xx  → InfraFault.
//   * app-settings response is not parseable JSON  → InfraFault.
//
// LIVE-VS-FAKES POSTURE:
//   Wired at author-time against a hand-rolled FakeHttpMessageHandler that
//   simulates the ARM management-plane responses (parity with sibling probes
//   173 / 174 / 179 that all use the same fake-HTTP-transport unit-test
//   pattern per ADR-038 — no Mock<HttpMessageHandler>). Live verification
//   against a real deployed customer stamp runs as part of task 186 (Phase F
//   E2E rerun).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   Sprk.Provisioning.ControlPlane.Core (L2, not BFF). Consumes NO AI-internal
//   types (ADR-013). No BFF-facade dependencies. No shell-out.
//
// COMPONENT JUSTIFICATION (CLAUDE.md §11):
//   Existing: SpeContainerResolverInvariantProbe (task 176) — its verdict
//     depends on the customer BFF's own /api/diagnostics/tenant-container-
//     resolver endpoint being deployed AND being truthful. That coverage is
//     legitimate but NOT INDEPENDENT of the subject BFF.
//   Extension: implementing IInvariantProbe (task 174's per-invariant seam) IS
//     the minimal extension move — same contract, one line change in
//     E2EAcceptanceModule.cs to swap the registered probe class name.
//   Cost-of-doing-nothing: I4 acceptance verdict is dependent on a subject
//     endpoint that may itself be compromised in the failure mode I4 exists
//     to catch — the whole point of the acceptance gate is INDEPENDENT
//     re-verification.
//
// ADR ALIGNMENT:
//   * ADR-028 UAMI outbound: probe uses the shared TokenCredential singleton
//     (DefaultAzureCredential pinned to L2 UAMI) — parity with sibling probes
//     173 (I2) and 174 (I3). Zero admin-key handling; the L2 UAMI needs
//     Reader RBAC on the customer subscription (already required by H1's
//     ArmSubscriptionReadinessProbe).
//   * ADR-032 (unconditional registration): registered UNCONDITIONALLY in
//     E2EAcceptanceModule; no feature-gate branch.
//   * ADR-038 (integration-heavy pyramid): tests exercise the probe against a
//     hand-rolled FakeHttpMessageHandler (never Mock<HttpMessageHandler>),
//     hand-rolled FakeTokenCredential — parity with
//     SpeContainerResolverInvariantProbeTests (task 176) and
//     AiSearchTenantFilterInvariantProbeTests (task 173).
//
// DI SWAP NOTE (task 204c B07 dispatch — main-session action):
//   The composite CompositeInvariantVerifier throws at composition time if two
//   probes register for the same InvariantKind (fail-loud silent-fail
//   protection). Wiring THIS probe requires REPLACING task 176's registration
//   in E2EAcceptanceModule.cs (currently lines 183-184: HttpClient +
//   AddSingleton<IInvariantProbe, SpeContainerResolverInvariantProbe>). Both
//   probes cover InvariantKind.I4SpeContainerResolver; keep exactly ONE
//   registered. Recommended swap: retire task 176's registration; keep task
//   176's class on disk with an updated banner explaining the retirement (per
//   Wave G-6 retired-on-disk-with-banner convention).
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Independent H13 I4 invariant probe — reads the deployed App Service
/// configuration directly via ARM to verify the canonical SPE container-id
/// app-setting is a `@Microsoft.KeyVault(...)` reference (tenant-derived),
/// NOT a hardcoded literal. Runs independently of the customer BFF's own
/// diagnostic endpoint (contrast task 176's SpeContainerResolverInvariantProbe).
/// See file header for the honest can-vs-cannot-detect breakdown and the
/// silent-fail class this probe catches that task 176's probe cannot.
/// </summary>
public sealed class SpeContainerTenantDerivationInvariantProbe : IInvariantProbe
{
    /// <summary>
    /// Named HttpClient key the DI module registers so the probe can pull an
    /// isolated client via <see cref="IHttpClientFactory"/> (parity with
    /// <see cref="SpeContainerResolverInvariantProbe.HttpClientName"/>).
    /// </summary>
    public const string HttpClientName = "H13-I4-SpeContainerTenantDerivationProbe";

    /// <summary>ARM management-plane root URL.</summary>
    public const string ArmBaseUrl = "https://management.azure.com";

    /// <summary>ARM scope required for the App Service management calls.</summary>
    public const string ArmScope = "https://management.azure.com/.default";

    /// <summary>ARM API version for App Service reads (matches sibling probes' pins).</summary>
    public const string AppServiceApiVersion = "2022-03-01";

    /// <summary>
    /// Canonical BFF App Service app-setting name for the SPE container-type
    /// id. ASP.NET Core config-provider convention converts the code-level
    /// key <c>SharePointEmbedded:ContainerTypeId</c> (see
    /// <c>Sprk.Bff.Api/Configuration/SharePointEmbeddedOptions.cs</c>) to
    /// double-underscore in Azure App Service app-settings.
    /// </summary>
    public const string ContainerTypeAppSettingName = "SharePointEmbedded__ContainerTypeId";

    /// <summary>
    /// Fallback app-setting name — some deploy topologies use the single-
    /// colon form directly, and Azure App Service accepts both. Checked
    /// as a secondary lookup so the probe doesn't false-InfraFault on a
    /// legitimate deployment that used the colon form.
    /// </summary>
    public const string ContainerTypeAppSettingNameColon = "SharePointEmbedded:ContainerTypeId";

    /// <summary>
    /// Canonical `@Microsoft.KeyVault(...)` reference-expression detector —
    /// case-insensitive prefix match. A value starting with this prefix is a
    /// tenant-derived reference by construction (the App Service resolver
    /// binds it to the referenced KV secret at boot).
    /// </summary>
    private static readonly Regex KvReferenceExpression = new(
        @"^\s*@Microsoft\.KeyVault\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Canonical Graph SPE container-id literal shape — parity with the
    /// tests/Spaarke.ArchTests/TenantIsolation/I4_SpeContainerIdLiteralTests
    /// regex so a value the ArchTest would flag as an inline literal is
    /// flagged HERE as a runtime FAILED verdict.
    /// </summary>
    private static readonly Regex CanonicalContainerIdLiteralShape = new(
        @"^b![A-Za-z0-9_\-]{20,}$",
        RegexOptions.Compiled);

    /// <summary>URL scheme allow-list — accepts only http(s).</summary>
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp, Uri.UriSchemeHttps,
    };

    /// <summary>App Service DNS suffix — used to extract site name from BffApiUrl.</summary>
    private const string AppServiceHostSuffix = ".azurewebsites.net";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenCredential _credential;
    private readonly H13AcceptanceOptions _options;
    private readonly ILogger<SpeContainerTenantDerivationInvariantProbe> _logger;

    /// <inheritdoc/>
    public InvariantKind Kind => InvariantKind.I4SpeContainerResolver;

    /// <summary>Constructs the probe. All collaborators are seams (ADR-010 ≥2 impls per test suite).</summary>
    public SpeContainerTenantDerivationInvariantProbe(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        IOptions<H13AcceptanceOptions> options,
        ILogger<SpeContainerTenantDerivationInvariantProbe> logger)
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
                "I4 tenant-derivation probe FAILED — blank tenantId (customerId={CustomerId} runId={RunId}). " +
                "The probe refuses to exercise ambient/default-tenant SPE container derivation verification.",
                request.CustomerId, request.RunId);
            return Failed(
                "observed=blank tenantId; expected=an explicit Entra tenant GUID. " +
                "§4D I1 defense-in-depth: the probe refuses to attempt an ambient-tenant SPE " +
                "container-id verification — a resolver returning ANY value under a blank tenant scope " +
                "is by definition the fallback-default silent-fail this invariant catches.");
        }

        if (string.IsNullOrWhiteSpace(request.SubscriptionId))
        {
            return InfraFault(
                "request.SubscriptionId is empty — cannot enumerate the customer's App Service " +
                "via ARM. H1 must have populated subscriptionId on the run before H13 runs.");
        }

        if (string.IsNullOrWhiteSpace(request.BffApiUrl))
        {
            return InfraFault(
                "request.BffApiUrl is empty — cannot derive the customer App Service name " +
                "for the ARM lookup. H9 must have populated bffApiUrl on the run before H13 runs.");
        }

        if (!Uri.TryCreate(request.BffApiUrl.Trim(), UriKind.Absolute, out var bffUri)
            || !AllowedSchemes.Contains(bffUri.Scheme))
        {
            return InfraFault(
                $"request.BffApiUrl '{request.BffApiUrl}' is not a valid http(s) absolute URL — " +
                "cannot derive the customer App Service name for the ARM lookup.");
        }

        var siteName = ExtractAppServiceName(bffUri);
        if (string.IsNullOrEmpty(siteName))
        {
            return InfraFault(
                $"BffApiUrl hostname '{bffUri.Host}' does not follow the App Service " +
                $"'{AppServiceHostSuffix}' convention. Custom-domain BFFs are not covered by this MVP " +
                "probe — plumb the App Service resource id explicitly (documented limitation).");
        }

        // (2) Acquire ARM token.
        AccessToken token;
        try
        {
            token = await _credential
                .GetTokenAsync(new TokenRequestContext(new[] { ArmScope }), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return InfraFault(
                $"ARM token acquisition (scope '{ArmScope}') failed: {ex.GetType().Name}: {ex.Message}. " +
                "Cannot verdict I4 without a bearer token — handler classifies Resumable.");
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.InvariantVerifierTimeout);

        // (3) Enumerate App Services in the customer subscription; match on siteName.
        string? siteResourceId;
        try
        {
            siteResourceId = await FindAppServiceIdByNameAsync(
                httpClient, token.Token, request.SubscriptionId, siteName, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return InfraFault(
                $"ARM enumeration of subscription '{request.SubscriptionId}' App Services timed out " +
                $"after {_options.InvariantVerifierTimeout}.");
        }
        catch (HttpRequestException ex)
        {
            return InfraFault(
                $"ARM enumeration of subscription '{request.SubscriptionId}' App Services failed: " +
                $"{ex.GetType().Name}: {ex.Message}.");
        }
        catch (ArmHttpFaultException ex)
        {
            return InfraFault(ex.Message);
        }
        catch (JsonException ex)
        {
            return InfraFault(
                $"ARM enumeration response is not parseable JSON: {ex.Message}. " +
                "Cannot verdict I4 without a valid site listing.");
        }

        if (siteResourceId is null)
        {
            return InfraFault(
                $"No App Service named '{siteName}' found in subscription '{request.SubscriptionId}'. " +
                "The customer BFF may not be deployed yet (H9 not landed), or BffApiUrl points at a " +
                "foreign App Service — probe classifies Resumable so the operator can investigate the " +
                "BFF URL misconfiguration without a false Pass.");
        }

        // (4) POST config/appsettings/list to read the app-settings dictionary.
        Dictionary<string, string> appSettings;
        try
        {
            appSettings = await ListAppSettingsAsync(
                httpClient, token.Token, siteResourceId, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return InfraFault(
                $"ARM app-settings/list on '{siteResourceId}' timed out after " +
                $"{_options.InvariantVerifierTimeout}.");
        }
        catch (HttpRequestException ex)
        {
            return InfraFault(
                $"ARM app-settings/list on '{siteResourceId}' failed: " +
                $"{ex.GetType().Name}: {ex.Message}.");
        }
        catch (ArmHttpFaultException ex)
        {
            return InfraFault(ex.Message);
        }
        catch (JsonException ex)
        {
            return InfraFault(
                $"ARM app-settings/list response is not parseable JSON: {ex.Message}. " +
                "Cannot verdict I4 without a valid app-settings dictionary.");
        }

        // (5) Classify the SPE container-id app-setting VALUE.
        return ClassifyContainerTypeAppSetting(appSettings, siteResourceId);
    }

    /// <summary>
    /// Extracts the App Service name from a BffApiUrl of the shape
    /// <c>https://{name}.azurewebsites.net[/]</c>. Returns <c>null</c> when
    /// the host does not end with the standard App Service suffix.
    /// </summary>
    internal static string? ExtractAppServiceName(Uri bffUri)
    {
        var host = bffUri.Host;
        if (!host.EndsWith(AppServiceHostSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var name = host.Substring(0, host.Length - AppServiceHostSuffix.Length);
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// Enumerates <c>Microsoft.Web/sites</c> in the subscription; returns the
    /// first entry whose ARM <c>name</c> matches <paramref name="siteName"/>
    /// ordinally-case-insensitive; returns <c>null</c> when no match found.
    /// </summary>
    private static async Task<string?> FindAppServiceIdByNameAsync(
        HttpClient httpClient, string bearerToken, string subscriptionId, string siteName,
        CancellationToken cancellationToken)
    {
        var url =
            $"{ArmBaseUrl}/subscriptions/{Uri.EscapeDataString(subscriptionId)}/providers/Microsoft.Web/sites" +
            $"?api-version={AppServiceApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ArmHttpFaultException(
                $"ARM sites-list returned HTTP {(int)response.StatusCode} ({response.StatusCode}) — " +
                $"the L2 UAMI likely lacks Reader RBAC on subscription '{subscriptionId}'. Body: " +
                Truncate(body, 400));
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("value", out var valueEl)
            || valueEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var site in valueEl.EnumerateArray())
        {
            if (site.ValueKind != JsonValueKind.Object) continue;
            if (!site.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                continue;
            if (!string.Equals(nameEl.GetString(), siteName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (site.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
            {
                var id = idEl.GetString();
                if (!string.IsNullOrEmpty(id)) return id;
            }
        }
        return null;
    }

    /// <summary>
    /// Reads the App Service app-settings dictionary via POST config/appsettings/list
    /// (App Service management convention — GET does not return secret app-
    /// setting values).
    /// </summary>
    private static async Task<Dictionary<string, string>> ListAppSettingsAsync(
        HttpClient httpClient, string bearerToken, string siteResourceId,
        CancellationToken cancellationToken)
    {
        var url = $"{ArmBaseUrl}{siteResourceId}/config/appsettings/list?api-version={AppServiceApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // App Service /list endpoints require an empty content body per REST spec.
        request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ArmHttpFaultException(
                $"ARM app-settings/list on '{siteResourceId}' returned HTTP {(int)response.StatusCode} " +
                $"({response.StatusCode}). Body: " + Truncate(body, 400));
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("properties", out var propsEl)
            || propsEl.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var prop in propsEl.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                result[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
            else if (prop.Value.ValueKind == JsonValueKind.Null)
            {
                result[prop.Name] = string.Empty;
            }
        }
        return result;
    }

    /// <summary>
    /// Classifies the SPE container-type app-setting VALUE. Exposed as
    /// internal-static so unit tests can exercise the classifier directly
    /// without needing to stub the ARM HTTP path.
    /// </summary>
    internal InvariantVerificationOutcome ClassifyContainerTypeAppSetting(
        IReadOnlyDictionary<string, string> appSettings, string siteResourceId)
    {
        string? value = null;
        string? matchedKey = null;
        if (appSettings.TryGetValue(ContainerTypeAppSettingName, out var v1))
        {
            value = v1;
            matchedKey = ContainerTypeAppSettingName;
        }
        else if (appSettings.TryGetValue(ContainerTypeAppSettingNameColon, out var v2))
        {
            value = v2;
            matchedKey = ContainerTypeAppSettingNameColon;
        }

        if (value is null)
        {
            return Failed(
                $"observed=App Service '{siteResourceId}' has NO '{ContainerTypeAppSettingName}' " +
                $"(or '{ContainerTypeAppSettingNameColon}') app-setting; expected=a " +
                $"'@Microsoft.KeyVault(SecretUri=https://{{vault}}.vault.azure.net/secrets/SPE-ContainerTypeId/)' " +
                "reference. §4D I4 (FR-31) — without a source, ITenantContainerResolver would return no value " +
                "or fall back to a compiled-in default at BFF boot; either way the tenant-derivation invariant is not met.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return Failed(
                $"observed=App Service '{siteResourceId}' has '{matchedKey}' app-setting present but " +
                $"BLANK; expected=non-empty '@Microsoft.KeyVault(...)' reference expression. §4D I4 " +
                "(FR-31) — a blank container-id setting resolves to null at BFF boot; ITenantContainerResolver " +
                "returns no tenant-derived value, silent-fail trap.");
        }

        if (KvReferenceExpression.IsMatch(value))
        {
            _logger.LogInformation(
                "H13 I4 tenant-derivation probe passed: App Service '{Site}' '{Key}' is a KV reference expression " +
                "(tenant-derived per §4D I4 / FR-31).", siteResourceId, matchedKey);
            return new InvariantVerificationOutcome.Passed(InvariantKind.I4SpeContainerResolver);
        }

        if (CanonicalContainerIdLiteralShape.IsMatch(value.Trim()))
        {
            // Truncate to avoid echoing a real container id into logs — parity
            // with the I4 ArchTest's identical defense-in-depth on the failure
            // message itself.
            var displayed = value.Trim().Length <= 30
                ? value.Trim()
                : value.Trim().Substring(0, 20) + "...[truncated]";
            return Failed(
                $"CATASTROPHIC — App Service '{siteResourceId}' '{matchedKey}' app-setting is a HARDCODED " +
                $"canonical SPE container-id literal ('{displayed}') instead of a '@Microsoft.KeyVault(...)' " +
                "reference. §4D I4 (FR-31) — the customer's BFF resolves ContainerTypeId from a static " +
                "literal, NOT from tenant-scoped KV storage. Uploads route to WHATEVER container that literal " +
                "names — cross-tenant leak by construction. Fix the Bicep app-setting to a KV reference " +
                "expression: '@Microsoft.KeyVault(SecretUri=https://{vault}.vault.azure.net/secrets/SPE-ContainerTypeId/)'.");
        }

        // Any other non-empty value shape — not a KV reference, not a canonical
        // container-id shape either. Could be a placeholder, an option string,
        // a partial URL, or malformed. Assume worst: the resolver is not
        // reading tenant-scoped storage at runtime.
        var displayedOther = value.Length <= 60 ? value : value.Substring(0, 50) + "...[truncated]";
        return Failed(
            $"observed=App Service '{siteResourceId}' '{matchedKey}' app-setting value='{displayedOther}'; " +
            "expected='@Microsoft.KeyVault(...)' reference expression. §4D I4 (FR-31) — value is not a KV " +
            "reference; ITenantContainerResolver is not sourcing ContainerTypeId from tenant-scoped storage " +
            "at runtime, so the tenant-derivation invariant is not met even if the literal value happens to " +
            "be a benign string.");
    }

    private InvariantVerificationOutcome Failed(string diagnostic)
    {
        _logger.LogWarning("H13 I4 tenant-derivation probe FAILED: {Diagnostic}", diagnostic);
        return new InvariantVerificationOutcome.Failed(InvariantKind.I4SpeContainerResolver, diagnostic);
    }

    private InvariantVerificationOutcome InfraFault(string diagnostic)
    {
        _logger.LogWarning("H13 I4 tenant-derivation probe InfraFault: {Diagnostic}", diagnostic);
        return new InvariantVerificationOutcome.InfraFault(InvariantKind.I4SpeContainerResolver, diagnostic);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";

    /// <summary>
    /// Internal marker exception used to hoist a rich HTTP-fault diagnostic
    /// out of the inner helpers into the top-level classification switch.
    /// Never leaks past <see cref="ProbeAsync"/>.
    /// </summary>
    private sealed class ArmHttpFaultException : Exception
    {
        public ArmHttpFaultException(string message) : base(message) { }
    }
}
