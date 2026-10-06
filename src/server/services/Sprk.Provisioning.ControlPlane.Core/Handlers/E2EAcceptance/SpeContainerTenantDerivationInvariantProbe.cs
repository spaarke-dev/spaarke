// -----------------------------------------------------------------------------
// SpeContainerTenantDerivationInvariantProbe.cs
//
// H13 I4 REAL invariant probe (task 204c B07 — sub-agent authored 2026-08-26).
// INDEPENDENT re-verification variant for InvariantKind.I4SpeContainerResolver
// — replaces the earlier task-176 resolver probe (deleted by task 227f) whose
// verdict was mediated by the customer BFF's own /api/diagnostics/tenant-
// container-resolver endpoint. Per task 204c dispatch directive:
//
//     "INDEPENDENT re-verification: do NOT trust RunStatus.HandlerReports[X].Outcome;
//      re-read the underlying Azure/Cosmos/Graph/SPE surface directly."
//
// This probe reads the DEPLOYED App Service configuration DIRECTLY via ARM
// (`Microsoft.Web/sites/{name}/config/appsettings/list`) and verifies the SPE
// settings name THIS run's container type and THIS customer's container (task
// 227c — see step 3 below; the original KV-reference rule is retired). This runs even when the BFF diagnostic endpoint is unreachable
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
//   3. TASK 227c REWRITE (owner D28). The original classifier PASSED only when
//      `SharePointEmbedded__ContainerTypeId` was a `@Microsoft.KeyVault(...)`
//      reference — but H4b writes that setting as a plain value (the container
//      TYPE id is the same for every Model 1 customer and is not a secret), so
//      it failed every stamp, and it never looked at the customer's CONTAINER.
//      Now it compares the deployed settings with the run's own values:
//      * `SharePointEmbedded__ContainerTypeId` (or the colon form) must equal
//        the run's containerTypeId (intake) — missing/blank/different → FAILED.
//      * `EmailProcessing__DefaultContainerId` and
//        `Communication__ArchiveContainerId` must equal the container H8 created
//        (InterStepState.SpeContainerId) — missing/blank → FAILED; a different
//        id → FAILED CATASTROPHIC: every Model 1 stamp's identity can reach every
//        container of the shared type (owner D28), so a wrong id here reads or
//        writes another customer's documents.
//      Literal values ARE the tenant derivation: H4b writes them from this run's
//      intake and H8 output (PerEnvSourceCatalog).
//
// WHAT THIS PROBE CAN AND CANNOT DETECT (task 227c):
//   CAN detect (Failed, CATASTROPHIC):
//     * A container setting names a container other than the one H8 created for
//       this customer — every Model 1 stamp identity reaches every container of
//       the shared type (owner D28), so that is another customer's documents.
//   CAN detect (Failed):
//     * Container-type setting missing / blank / not the run's container type.
//     * A container setting missing / blank, or still a Key Vault reference (a
//       stamp configured before 227c that H4b has not rewritten yet).
//   CAN detect (InfraFault):
//     * The run's containerTypeId or SpeContainerId is empty (H8 not run).
//     * ARM enumeration / app-settings read fails, or no matching App Service.
//   CANNOT detect:
//     * The staging slot's settings (only the production slot is read).
//     * What the BFF code does with the values — 227d guards app-only SPE calls.
//
// SILENT-FAIL AUDIT: the probe reads the DEPLOYED configuration and compares it
//   with the run's own values, instead of trusting the BFF's self-report (task
//   176's retired diagnostic probe) — "assert effects, not intentions" (R7).
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
//   Existing: the task-176 resolver probe (deleted by task 227f) — its verdict
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
//     the task-176 probe's tests (deleted with it) and
//     AiSearchTenantFilterInvariantProbeTests (task 173).
//
// DI HISTORY: this probe replaced task 176's registration for InvariantKind.I4SpeContainerResolver (task 204c B07;
//   CompositeInvariantVerifier refuses two probes for one kind). Task 176's class stayed on disk unregistered until
//   task 227f deleted it with the BFF diagnostic route it called.
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
/// configuration directly via ARM and verifies the SPE settings name this run's
/// container type and this customer's own container (task 227c, owner D28). Runs independently of the customer BFF's own
/// diagnostic endpoint (contrast task 176's resolver probe, deleted by task 227f).
/// See file header for the honest can-vs-cannot-detect breakdown and the
/// silent-fail class this probe catches that task 176's probe cannot.
/// </summary>
public sealed class SpeContainerTenantDerivationInvariantProbe : IInvariantProbe
{
    /// <summary>
    /// Named HttpClient key the DI module registers so the probe can pull an
    /// isolated client via <see cref="IHttpClientFactory"/> (parity with
    /// <see cref="AiSearchTenantFilterInvariantProbe.HttpClientName"/>).
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
    /// key <c>SharePointEmbedded:ContainerTypeId</c> (read by the BFF's
    /// <c>ProvisionProjectEndpoint</c>) to
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
    /// The BFF settings that name the customer's own SPE container (task 227c) — both must equal the container H8
    /// created (one container per customer, owner D28).
    /// </summary>
    public static readonly IReadOnlyList<string> CustomerContainerAppSettingNames =
        ["EmailProcessing__DefaultContainerId", "Communication__ArchiveContainerId"];

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

        if (string.IsNullOrWhiteSpace(request.ContainerTypeId) || string.IsNullOrWhiteSpace(request.SpeContainerId))
        {
            return InfraFault(
                "I4 needs the run's containerTypeId (intake) and the customer's container (InterStepState.SpeContainerId, " +
                "H8) to compare the BFF's settings with — one is empty. H8 must have run before H13.");
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

        // (5) Compare the deployed SPE settings with this run's own values (task 227c).
        return ClassifyAppSettings(appSettings, siteResourceId, request.ContainerTypeId, request.SpeContainerId);
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
    /// Compares the deployed SPE settings with this run's own values (task 227c): the container-type setting must equal
    /// <paramref name="expectedContainerTypeId"/>, and each <see cref="CustomerContainerAppSettingNames"/> setting must
    /// equal <paramref name="expectedContainerId"/>. Internal so unit tests exercise it without the ARM path.
    /// </summary>
    internal InvariantVerificationOutcome ClassifyAppSettings(
        IReadOnlyDictionary<string, string> appSettings, string siteResourceId,
        string expectedContainerTypeId, string expectedContainerId)
    {
        string? containerType = null;
        var containerTypeKey = ContainerTypeAppSettingName;
        if (appSettings.TryGetValue(ContainerTypeAppSettingName, out var v1))
        {
            containerType = v1;
        }
        else if (appSettings.TryGetValue(ContainerTypeAppSettingNameColon, out var v2))
        {
            containerType = v2;
            containerTypeKey = ContainerTypeAppSettingNameColon;
        }

        if (string.IsNullOrWhiteSpace(containerType))
        {
            return Failed(
                $"observed=App Service '{siteResourceId}' has {(containerType is null ? "NO" : "a BLANK")} " +
                $"'{ContainerTypeAppSettingName}' (or '{ContainerTypeAppSettingNameColon}') app-setting; expected the run's " +
                $"container type '{expectedContainerTypeId}'. §4D I4 (FR-31) — the BFF has no container type to work in.");
        }
        if (!string.Equals(containerType.Trim(), expectedContainerTypeId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Failed(
                $"observed=App Service '{siteResourceId}' '{containerTypeKey}'='{Display(containerType)}'; expected the run's " +
                $"container type '{expectedContainerTypeId}'. §4D I4 (FR-31) — the BFF is not configured with this run's " +
                "container type (H4b writes it from the run's intake).");
        }

        foreach (var name in CustomerContainerAppSettingNames)
        {
            if (appSettings.TryGetValue(name, out var kvRef) && kvRef.TrimStart().StartsWith("@Microsoft.KeyVault(", StringComparison.OrdinalIgnoreCase))
            {
                return Failed(
                    $"observed=App Service '{siteResourceId}' '{name}' is still a Key Vault reference (set before task 227c); " +
                    $"expected this customer's container '{Display(expectedContainerId)}' (H8) as a plain value — re-run H4b.");
            }
            if (!appSettings.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return Failed(
                    $"observed=App Service '{siteResourceId}' has no (or a blank) '{name}' app-setting; expected this " +
                    $"customer's container '{Display(expectedContainerId)}' (H8). §4D I4 (FR-31) — H4b writes it from H8's output.");
            }
            if (!string.Equals(value.Trim(), expectedContainerId.Trim(), StringComparison.Ordinal))
            {
                return Failed(
                    $"CATASTROPHIC — App Service '{siteResourceId}' '{name}' names container '{Display(value)}', not this " +
                    $"customer's container '{Display(expectedContainerId)}' (H8). Every Model 1 stamp's identity can reach every " +
                    "container of the shared container type (owner D28), so a wrong id here reads or writes another customer's " +
                    "documents — cross-customer leak. §4D I4 (FR-31).");
            }
        }

        _logger.LogInformation(
            "H13 I4 probe passed: App Service '{Site}' names the run's container type and this customer's container in " +
            "{Count} container settings.", siteResourceId, CustomerContainerAppSettingNames.Count);
        return new InvariantVerificationOutcome.Passed(InvariantKind.I4SpeContainerResolver);
    }

    /// <summary>Truncates an id for diagnostics (parity with the I4 ArchTest's no-full-id-echo defence).</summary>
    private static string Display(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 30 ? trimmed : trimmed[..20] + "...[truncated]";
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
