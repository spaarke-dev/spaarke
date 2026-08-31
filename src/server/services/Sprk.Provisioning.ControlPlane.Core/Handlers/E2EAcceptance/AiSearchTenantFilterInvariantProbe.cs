// -----------------------------------------------------------------------------
// AiSearchTenantFilterInvariantProbe.cs
//
// H13 I2 REAL invariant probe (task 173, Phase C'' Wave G-7 Batch G-7A1)
// REPLACING the wave-C4 <see cref="PlaceholderInvariantVerifier"/>'s I2 branch
// (which returned InfraFault unconditionally). Composed into the aggregate
// <see cref="IE2EInvariantVerifier"/> via <see cref="CompositeInvariantVerifier"/>.
//
// PURPOSE (spec.md FR-29 / design.md §4D I2):
//   Every AI Search query MUST include an unconditional `tenantId eq '{TenantId}'`
//   filter. This probe verifies that constraint holds AT ONBOARDING TIME by
//   issuing a live SAMPLE QUERY against the customer's AI Search endpoint and
//   asserting the filter is (a) SHAPE-CORRECT (per task 124's Model 1
//   tenant-filter template document) and (b) ENFORCED SERVER-SIDE (the query
//   round-trips 200 + any returned documents match request.TenantId — a
//   foreign-tenant doc in the response would be a CATASTROPHIC §4D I2
//   violation).
//
// WHY I DID NOT MERELY RE-CHECK THE TEMPLATE DOCUMENT (POML explicit):
//   The POML prompt says verbatim: "not merely present in the template, but
//   actually applied server-side to a live query". Re-reading the Cosmos-stored
//   template document (task 124's artifact) confirms the ARTIFACT SHAPE but
//   NOT that the AI Search service ACCEPTS the filter at query time — those are
//   two different checks. This probe does BOTH: it verifies the template shape
//   (Model 1 only — Model 2 has no template artifact by design) AND issues an
//   actual /docs/search POST with the filter, asserting HTTP 200 + returned
//   documents (if any) belong to the requested tenant. Every foreign-tenant doc
//   in a filtered response = Failed (server did not enforce). A malformed
//   filter payload rejected with HTTP 400 = Failed (tenantId field not
//   filterable — H2b's schema-verifier missed it). Empty results (HTTP 200 with
//   zero docs) are Pass-eligible provided the endpoint is reachable + accepts
//   the filter syntax — combined with H2b's structural "tenantId is filterable"
//   check (task 045's RestApiAiSearchIndexVerifier), the filter mechanic is
//   proven applicable at query time even when no data yet exists.
//
// WHAT THE PROBE CAN AND CANNOT CONCLUSIVELY DETECT:
//   CAN detect (Failed, CATASTROPHIC):
//     * Server returns a document whose tenantId does NOT match the requested
//       tenantId → the filter is being IGNORED server-side (cross-tenant leak).
//     * Server rejects the query with HTTP 4xx because the tenantId field
//       isn't filterable on the index → I2 filter cannot be applied at all.
//     * Model 1 template artifact carries a different tenantId than the
//       request → the onboarding record was written under the WRONG tenant
//       identity (silent-fail trap that would put future filtered queries on
//       the wrong scope).
//     * Model 1 template artifact's per-index filter predicate does not match
//       the exact shape `tenantId eq '{escaped-tenantId}'` → the recorded
//       enforcement intent is malformed.
//     * Model 1 template artifact is MISSING for a Model 1 tenant → H2b
//       silently failed OR the run isn't Model 1 as its parameters claim.
//     * Response body malformed / missing `value` array → server contract
//       violation.
//   CANNOT detect (Pass-eligible under this probe alone):
//     * A completely EMPTY index with the customer as the only tenant → the
//       positive probe returns 0 docs; combined with H2b's structural check
//       + template-artifact shape check, this is treated as Pass (the filter
//       APPLIES; the index just has no data). This is the honest limit of a
//       runtime probe without invasive test-data injection.
//
// EDGE CASES + INFRA-FAULT DISCIPLINE (Waves G-4..G-6 pattern):
//   * ProvisioningRun not found → InfraFault (retry from Cosmos).
//   * Model 1 template store unreachable → InfraFault (Cosmos transient).
//   * AI Search endpoint unreachable → InfraFault (network transient).
//   * AAD token acquisition failure → InfraFault (auth transient).
//   * Response body cannot be parsed → InfraFault (probe-side deserializer bug
//     is not the customer's I2 fault).
//   Every Failed diagnostic names the specific index + the observed-vs-expected
//   evidence so the operator can jump straight to the offender without
//   correlating logs.
//
// TENANCY-MODEL BRANCH (design.md §4.1a):
//   * Model 1 (Model1Shared): template artifact EXISTS in Cosmos (task 124's
//     H2b M1 branch provisions it). Endpoint comes from template.SearchEndpoint
//     (the shared platform Search service). Expected filters come from the
//     template's per-index list.
//   * Model 2 (Model2Dedicated): template artifact does NOT exist by design
//     (H2b M2 branch does not provision it — the customer has their own
//     dedicated Search service). Endpoint comes from request.AiSearchEndpoint
//     (InterStepState populated by H2a). Expected filters are derived from
//     ICanonicalIndexCatalog.CanonicalIndexNames × `tenantId eq '{tenantId}'`.
//
// SEAM JUSTIFICATION (ADR-010 / CLAUDE.md §11 extension test):
//   Existing: <see cref="IE2EInvariantVerifier"/> aggregate seam +
//   <see cref="PlaceholderInvariantVerifier"/> stub existed pre-Wave-G7. Task
//   173 adds ONE per-invariant probe class that plugs into the sibling-
//   composite pattern (see IInvariantProbe.cs coordination note).
//   Extension: this class is composable side-by-side with sibling I1/I3/I4/I5
//   probes without shared-file body edits.
//   Cost-of-doing-nothing: I2's placeholder-InfraFault is a permanent
//   Resumable block on H13 acceptance — spec-defined CATASTROPHIC invariant
//   would never be independently verified at onboarding.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   Sprk.Provisioning.ControlPlane.Core (L2, not BFF). No AI-internal type
//   dependencies (ADR-013). No BFF-facade dependencies. No shell-out / no
//   ProcessStartInfo (DS-1b §3 posture).
//
// ADR ALIGNMENT:
//   * ADR-028 UAMI outbound: probe injects the shared TokenCredential singleton
//     (DefaultAzureCredential pinned to L2 UAMI) — parity with H2b's
//     SearchIndexClientProvisioner + RestApiAiSearchIndexVerifier. Zero
//     admin-key handling.
//   * ADR-014 (spaarke-session-files dual-filter invariant): probe checks the
//     `tenantId` half of the invariant on every canonical index INCLUDING
//     session-files — I2 is a superset check per spec.md ADR Tensions row for
//     ADR-014.
//   * ADR-032 (unconditional registration): registered unconditionally in
//     E2EAcceptanceModule; no feature-gate branch.
//   * ADR-038 (integration-heavy pyramid): tests exercise the probe against a
//     hand-rolled FakeHttpMessageHandler (never Mock&lt;HttpMessageHandler&gt;),
//     a hand-rolled FakeTenantFilterTemplateStore, a hand-rolled
//     FakeProvisioningRunRepository, a FakeCanonicalIndexCatalog, and a
//     FakeTokenCredential — parity with ArmSdkTestFakes (task 123).
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// <see cref="IInvariantProbe"/> for <see cref="InvariantKind.I2AiSearchTenantFilter"/> —
/// issues a live sample search query against the customer's AI Search endpoint
/// and asserts the tenantId filter is enforced server-side. See file header
/// for the branch semantics (Model 1 vs Model 2) and the honest can-vs-cannot-
/// detect breakdown.
/// </summary>
public sealed class AiSearchTenantFilterInvariantProbe : IInvariantProbe
{
    /// <summary>AAD OAuth2 scope for AI Search AAD-authenticated REST calls (parity with RestApiAiSearchIndexVerifier).</summary>
    private static readonly string[] SearchAadScope = new[] { "https://search.azure.com/.default" };

    /// <summary>Canonical Model 1 tenancy-model marker (matches H2bAiSearchIndexHandler's own comparison).</summary>
    private const string Model1TenancyModel = "Model1Shared";

    /// <summary>Canonical Model 2 tenancy-model marker (matches H2bAiSearchIndexHandler's own comparison).</summary>
    private const string Model2TenancyModel = "Model2Dedicated";

    /// <summary>Default HttpClient name — the module registers this HttpClient by name so the probe can pull it via IHttpClientFactory.</summary>
    public const string HttpClientName = "H13-I2-AiSearchTenantFilterProbe";

    /// <summary>
    /// JSON serializer options for the request body. Uses
    /// <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> so single-
    /// quote characters inside the OData filter predicate serialize as literal
    /// <c>'</c> instead of <c>'</c> — server-parses identically either way,
    /// but keeps L2 logs and probe-body captures human-readable when a Failed
    /// diagnostic quotes them back to the operator.
    /// </summary>
    private static readonly JsonSerializerOptions RequestBodyOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IProvisioningRunRepository _repository;
    private readonly ITenantFilterTemplateStore _templateStore;
    private readonly ICanonicalIndexCatalog _catalog;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenCredential _credential;
    private readonly AiSearchIndexOptions _options;
    private readonly ILogger<AiSearchTenantFilterInvariantProbe> _logger;

    /// <summary>Constructs the probe. All collaborators are seams (ADR-010 ≥2 impls per test suite).</summary>
    public AiSearchTenantFilterInvariantProbe(
        IProvisioningRunRepository repository,
        ITenantFilterTemplateStore templateStore,
        ICanonicalIndexCatalog catalog,
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        IOptions<AiSearchIndexOptions> options,
        ILogger<AiSearchTenantFilterInvariantProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(templateStore);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _templateStore = templateStore;
        _catalog = catalog;
        _httpClientFactory = httpClientFactory;
        _credential = credential;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public InvariantKind Kind => InvariantKind.I2AiSearchTenantFilter;

    /// <inheritdoc/>
    public async Task<InvariantVerificationOutcome> ProbeAsync(
        InvariantVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            return InfraFault("request.CustomerId is empty — cannot look up ProvisioningRun / template.");
        }
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return InfraFault("request.RunId is empty — cannot look up ProvisioningRun.");
        }
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            // §4D I1 defense-in-depth: the parent handler already guards this,
            // but a probe that silently accepts an empty tenantId would build
            // a Passed verdict on a `tenantId eq ''` predicate — a false Pass.
            return InfraFault("request.TenantId is empty — I2 probe cannot verify tenant scope without an explicit tenantId (§4D I1 defense-in-depth).");
        }

        // (1) Load the run to determine tenancyModel — I2 branches on it.
        ProvisioningRunReadResult? read;
        try
        {
            read = await _repository.ReadRunAsync(request.CustomerId, request.RunId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return InfraFault($"ProvisioningRun read failed: {ex.GetType().Name}: {ex.Message}.");
        }
        if (read is null)
        {
            return InfraFault(
                $"ProvisioningRun '{request.RunId}' not found in customer partition '{request.CustomerId}'. " +
                "The run must exist for the probe to determine tenancyModel.");
        }

        var tenancyModel = string.IsNullOrWhiteSpace(read.Run.TenancyModel)
            ? Model2TenancyModel // Match H2b's own default fallback.
            : read.Run.TenancyModel;

        // (2) Determine endpoint + expected per-index filter set.
        string endpoint;
        ImmutableArray<(string IndexName, string ExpectedFilterPredicate)> targets;

        var expectedPredicateForRequestTenant = BuildFilterPredicate(request.TenantId);

        if (string.Equals(tenancyModel, Model1TenancyModel, StringComparison.OrdinalIgnoreCase))
        {
            TenantFilterTemplateDocument? template;
            try
            {
                template = await _templateStore.TryReadAsync(request.CustomerId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return InfraFault($"Model 1 tenant-filter template read failed: {ex.GetType().Name}: {ex.Message}.");
            }

            if (template is null)
            {
                return Failed(
                    "Model 1 tenant-filter template artifact is MISSING for this customer. H2b's Model 1 branch " +
                    "provisions this artifact at onboarding (task 124 real impl); its absence means H2b silently " +
                    "failed OR the run's tenancyModel claim ('Model1Shared') is inconsistent with H2b's actual " +
                    "execution branch. Either way I2's onboarding-time enforcement decision was NOT recorded.");
            }
            if (!string.Equals(template.TenantId, request.TenantId, StringComparison.Ordinal))
            {
                return Failed(
                    $"Model 1 template tenantId mismatch: template records '{template.TenantId}' but the H13 " +
                    $"request scopes to '{request.TenantId}'. Silent-fail-trap: any filtered query built from " +
                    "the template would apply the WRONG tenant scope — every future AI Search query for this " +
                    "customer would be under the wrong identity (§4D I1 CATASTROPHIC).");
            }

            var templateEntries = ImmutableArray.CreateBuilder<(string, string)>(template.Filters.Count);
            foreach (var entry in template.Filters)
            {
                if (!string.Equals(entry.FilterPredicate, expectedPredicateForRequestTenant, StringComparison.Ordinal))
                {
                    return Failed(
                        $"Model 1 template FILTER PREDICATE malformed for index '{entry.IndexName}': recorded " +
                        $"'{entry.FilterPredicate}' but expected '{expectedPredicateForRequestTenant}'. The template " +
                        "would apply an incorrect / bypassable filter — I2 enforcement is compromised.");
                }
                templateEntries.Add((entry.IndexName, entry.FilterPredicate));
            }
            if (templateEntries.Count == 0)
            {
                return Failed(
                    "Model 1 template has ZERO per-index filter entries — no I2 enforcement recorded at all. " +
                    "H2b silently produced an empty template.");
            }

            endpoint = template.SearchEndpoint;
            targets = templateEntries.ToImmutable();
        }
        else if (string.Equals(tenancyModel, Model2TenancyModel, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.AiSearchEndpoint))
            {
                return InfraFault(
                    "Model 2 branch requires request.AiSearchEndpoint (populated by H2a from InterStepState). " +
                    "H2a may not have completed OR its output was not persisted — H13 upstream should have " +
                    "caught this, but I2 defense-in-depth surfaces it explicitly.");
            }
            endpoint = request.AiSearchEndpoint;
            var canonical = _catalog.CanonicalIndexNames;
            if (canonical.IsDefaultOrEmpty)
            {
                return InfraFault("ICanonicalIndexCatalog.CanonicalIndexNames returned an empty set — probe cannot select target indexes.");
            }
            var derived = ImmutableArray.CreateBuilder<(string, string)>(canonical.Length);
            foreach (var name in canonical)
            {
                derived.Add((name, expectedPredicateForRequestTenant));
            }
            targets = derived.ToImmutable();
        }
        else
        {
            return InfraFault(
                $"Unknown tenancyModel '{tenancyModel}' on ProvisioningRun '{request.RunId}'. " +
                $"Expected '{Model1TenancyModel}' or '{Model2TenancyModel}'.");
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return InfraFault(
                $"Resolved AI Search endpoint is empty for tenancyModel '{tenancyModel}'. " +
                "Model 1 template.SearchEndpoint OR Model 2 request.AiSearchEndpoint MUST be non-empty.");
        }

        // (3) Acquire an AAD token for the AI Search AAD scope.
        AccessToken token;
        try
        {
            token = await _credential.GetTokenAsync(
                new TokenRequestContext(SearchAadScope), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return InfraFault($"AAD token acquisition for AI Search failed: {ex.GetType().Name}: {ex.Message}.");
        }

        // (4) Probe each (index, predicate) target with a POST /docs/search.
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        foreach (var (indexName, filterPredicate) in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var probeOutcome = await ProbeSingleIndexAsync(
                httpClient, endpoint, indexName, filterPredicate, request.TenantId, token, cancellationToken)
                .ConfigureAwait(false);
            if (probeOutcome is not null)
            {
                return probeOutcome;
            }
        }

        _logger.LogInformation(
            "H13 I2 probe passed: customerId={CustomerId} runId={RunId} tenancyModel={TenancyModel} " +
            "endpoint={Endpoint} indexCount={IndexCount}",
            request.CustomerId, request.RunId, tenancyModel, endpoint, targets.Length);
        return new InvariantVerificationOutcome.Passed(InvariantKind.I2AiSearchTenantFilter);
    }

    /// <summary>
    /// Runs one (index, predicate) sample probe. Returns <c>null</c> when the
    /// probe passed for this index; returns a non-null outcome (Failed /
    /// InfraFault) to short-circuit the aggregate on the first observed issue.
    /// </summary>
    private async Task<InvariantVerificationOutcome?> ProbeSingleIndexAsync(
        HttpClient httpClient,
        string endpoint,
        string indexName,
        string filterPredicate,
        string expectedTenantId,
        AccessToken token,
        CancellationToken cancellationToken)
    {
        var url =
            $"{endpoint.TrimEnd('/')}/indexes/{Uri.EscapeDataString(indexName)}/docs/search?api-version={_options.SearchApiVersion}";

        var body = JsonSerializer.Serialize(new
        {
            search = "*",
            filter = filterPredicate,
            top = 5,
            select = "tenantId",
            count = true,
        }, RequestBodyOptions);

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RestCallTimeout);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(req, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return InfraFault(
                $"Probe HTTP call to '{url}' timed out after {_options.RestCallTimeout}. " +
                "AI Search endpoint may be under-provisioned or unreachable from the L2 host.");
        }
        catch (HttpRequestException ex)
        {
            return InfraFault(
                $"Probe HTTP call to '{url}' failed with {ex.GetType().Name}: {ex.Message}. " +
                "AI Search endpoint may be unreachable from the L2 host.");
        }

        try
        {
            var respBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Failed(
                    $"Index '{indexName}' NOT FOUND at endpoint '{endpoint}' — I2 cannot be enforced on a " +
                    "missing index; H2b did not provision (Model 2) or shared platform is under-provisioned " +
                    "(Model 1). Diagnostic: " + Truncate(respBody, 300));
            }
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return Failed(
                    $"AI Search rejected filter '{filterPredicate}' on index '{indexName}' with HTTP 400. " +
                    "This means the 'tenantId' field is NOT filterable on this index — the I2 enforcement " +
                    "MECHANIC is broken at the schema level (H2b's RestApiAiSearchIndexVerifier should have " +
                    "caught this but did not). Server diagnostic: " + Truncate(respBody, 300));
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                return InfraFault(
                    $"AI Search returned HTTP {(int)response.StatusCode} ({response.StatusCode}) for POST '{url}'. " +
                    "The L2 UAMI likely lacks the 'Search Index Data Reader' RBAC role on this search service. " +
                    "This is an infra permission issue, not an I2 semantic violation.");
            }
            if (!response.IsSuccessStatusCode)
            {
                return InfraFault(
                    $"AI Search returned HTTP {(int)response.StatusCode} for POST '{url}': " +
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
                    $"AI Search response for POST '{url}' is not parseable JSON: {ex.Message}. " +
                    "Server contract violation — probe cannot verdict I2 from a malformed response.");
            }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("value", out var valArr) || valArr.ValueKind != JsonValueKind.Array)
                {
                    return InfraFault(
                        $"AI Search response for POST '{url}' missing 'value' array (contract violation). " +
                        $"Body: {Truncate(respBody, 400)}");
                }

                foreach (var d in valArr.EnumerateArray())
                {
                    if (!d.TryGetProperty("tenantId", out var tidEl))
                    {
                        return Failed(
                            $"CATASTROPHIC — index '{indexName}' returned a document with NO 'tenantId' field " +
                            "under filter '" + filterPredicate + "'. Either the indexer is not populating " +
                            "tenantId on ingest (in which case I2's per-doc scoping guarantee is broken by the " +
                            "producer) or the server is somehow serving un-fielded results — either way this " +
                            "is a §4D I2 violation.");
                    }
                    if (tidEl.ValueKind != JsonValueKind.String)
                    {
                        return Failed(
                            $"CATASTROPHIC — index '{indexName}' returned a document whose tenantId is not a " +
                            $"JSON string (kind={tidEl.ValueKind}) under filter '{filterPredicate}'. Producer " +
                            "contract violation breaks I2 enforcement.");
                    }
                    var observedTid = tidEl.GetString();
                    if (!string.Equals(observedTid, expectedTenantId, StringComparison.Ordinal))
                    {
                        return Failed(
                            $"CATASTROPHIC — index '{indexName}' returned document with tenantId='{observedTid}' " +
                            $"under filter '{filterPredicate}' scoping to tenantId='{expectedTenantId}'. Server " +
                            "did NOT enforce I2 tenant-scope filter — cross-tenant document leak. This is the " +
                            "exact silent-fail spec.md §4D I2 / FR-29 mandates catching at onboarding time.");
                    }
                }
            }
        }
        finally
        {
            response.Dispose();
        }

        return null;
    }

    /// <summary>
    /// Builds the canonical `tenantId eq '{tenantId}'` OData filter predicate,
    /// applying OData string-literal escaping (doubled single-quote) — parity
    /// with AiSearchTenantFilterTemplateProvisioner's BuildFilterEntries.
    /// </summary>
    private static string BuildFilterPredicate(string tenantId)
    {
        var escaped = tenantId.Replace("'", "''", StringComparison.Ordinal);
        return $"tenantId eq '{escaped}'";
    }

    private InvariantVerificationOutcome Failed(string diagnostic)
    {
        _logger.LogWarning("H13 I2 probe FAILED: {Diagnostic}", diagnostic);
        return new InvariantVerificationOutcome.Failed(InvariantKind.I2AiSearchTenantFilter, diagnostic);
    }

    private InvariantVerificationOutcome InfraFault(string diagnostic)
    {
        _logger.LogWarning("H13 I2 probe InfraFault: {Diagnostic}", diagnostic);
        return new InvariantVerificationOutcome.InfraFault(InvariantKind.I2AiSearchTenantFilter, diagnostic);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
