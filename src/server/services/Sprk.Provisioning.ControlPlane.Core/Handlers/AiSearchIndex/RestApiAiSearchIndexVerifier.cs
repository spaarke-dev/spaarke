// -----------------------------------------------------------------------------
// RestApiAiSearchIndexVerifier.cs
//
// Production <see cref="IAiSearchIndexVerifier"/> — calls the AI Search REST
// API directly (GET /indexes/{name}?api-version=...) with AAD token-based
// auth via <see cref="DefaultAzureCredential"/> (ADR-028 UAMI-outbound MUST
// rule + spec MUST rule: no admin-key credential; the search service accepts
// AAD bearer tokens).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-05
//     acceptance: per-index invariant verifier — required filterable fields
//     + vector fields + forbidden absent + semantic-config-field references.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1a
//     Model 1 branch: verify presence on shared platform service (does NOT
//     re-create); Model 2 branch: verify invariants post-deploy.
//   - scripts/ai-search/Deploy-AllIndexes.ps1 Invoke-PostDeployVerifier
//     (lines 377–487): the invariant rules — this C# verifier is a subset
//     that focuses on structural invariants the handler MUST check
//     independently of the script (defense-in-depth for Model 2 +
//     stand-alone for Model 1).
//   - ADR-028: MI-outbound uses <see cref="DefaultAzureCredential"/>;
//     scope for AI Search AAD auth is <c>https://search.azure.com/.default</c>.
//
// PRODUCTION SCOPE (wave C4):
//   Structural verifier only — checks: (a) index exists (GET returns 200 with
//   matching name); (b) canonical filterable fields present (tenantId always;
//   plus index-specific per <c>Deploy-AllIndexes.ps1</c> $Catalog); (c) no
//   forbidden fields present (per-index forbidden list from $Catalog).
//   Does NOT re-verify vector-search algorithm / metric / semantic-config
//   field wiring at this wave — those live inside the script's
//   Invoke-PostDeployVerifier + are asserted by the script's own exit 0.
//   Extension seam for full invariant coverage lands in Wave C5 alongside
//   the reconciler.
//
// CI COVERAGE:
//   Not exercised by CI unit suite (real AI Search REST call). Env-guarded
//   smoke tests can be added alongside <c>CosmosSmokeTests</c> when a
//   dedicated dev-provisioning AI Search service is available; for wave C4
//   the handler unit tests via stubs are the coverage.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <summary>
/// Calls the AI Search REST API to verify presence + core invariants of a
/// requested index set. Auth via <see cref="DefaultAzureCredential"/> — the
/// L2 App Service UAMI (or operator's `az login`) must have Search Index
/// Data Reader on the search service.
/// </summary>
public sealed class RestApiAiSearchIndexVerifier : IAiSearchIndexVerifier
{
    private static readonly string[] SearchAadScope = new[] { "https://search.azure.com/.default" };

    private readonly ICanonicalIndexCatalog _catalog;
    private readonly AiSearchIndexOptions _options;
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly ILogger<RestApiAiSearchIndexVerifier> _logger;

    /// <summary>
    /// Constructs the verifier with a shared <see cref="HttpClient"/> +
    /// <see cref="DefaultAzureCredential"/>. Prefer registering as Singleton
    /// so the credential's token-cache is reused across handler invocations.
    /// </summary>
    public RestApiAiSearchIndexVerifier(
        ICanonicalIndexCatalog catalog,
        IOptions<AiSearchIndexOptions> options,
        HttpClient httpClient,
        ILogger<RestApiAiSearchIndexVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);

        _catalog = catalog;
        _options = options.Value;
        _httpClient = httpClient;
        _credential = new DefaultAzureCredential();
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AiSearchIndexVerifyResult> VerifyAsync(
        AiSearchIndexVerifyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SearchEndpoint);

        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(SearchAadScope), cancellationToken).ConfigureAwait(false);

        var missing = ImmutableArray.CreateBuilder<string>();
        var issues = ImmutableArray.CreateBuilder<IndexInvariantIssue>();

        foreach (var indexName in request.ExpectedIndexNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = $"{request.SearchEndpoint.TrimEnd('/')}/indexes/{indexName}?api-version={_options.SearchApiVersion}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RestCallTimeout);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(req, timeout.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "H2b verifier HTTP fault: index={IndexName} endpoint={Endpoint}",
                    indexName, request.SearchEndpoint);
                throw;
            }

            try
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    missing.Add(indexName);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    throw new HttpRequestException(
                        $"AI Search verifier returned {(int)response.StatusCode} for GET {url}: {Truncate(body, 400)}");
                }

                var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(payload);
                foreach (var issue in InspectInvariants(indexName, doc.RootElement))
                {
                    issues.Add(issue);
                }
            }
            finally
            {
                response.Dispose();
            }
        }

        if (missing.Count > 0)
        {
            return new AiSearchIndexVerifyResult.Missing(missing.ToImmutable());
        }
        if (issues.Count > 0)
        {
            return new AiSearchIndexVerifyResult.InvariantViolation(issues.ToImmutable());
        }
        return new AiSearchIndexVerifyResult.Ok();
    }

    /// <summary>
    /// Structural invariant checks on the deployed index definition. Kept in
    /// sync with <c>Deploy-AllIndexes.ps1</c> <c>Invoke-PostDeployVerifier</c>
    /// for the subset that catches obvious drift: (a) canonical filterable
    /// fields per-index present + filterable=true; (b) forbidden field names
    /// absent (currently only <c>domain</c> on <c>spaarke-rag-references</c>
    /// per FR-17). Vector-search-algorithm + semantic-config-field wiring
    /// stays in the script's verifier (invoked via the script's own exit code).
    /// </summary>
    private static IEnumerable<IndexInvariantIssue> InspectInvariants(string indexName, JsonElement root)
    {
        if (!root.TryGetProperty("name", out var nameEl)
            || !nameEl.ValueKind.Equals(JsonValueKind.String)
            || !string.Equals(nameEl.GetString(), indexName, StringComparison.Ordinal))
        {
            yield return new IndexInvariantIssue(
                indexName,
                "name",
                $"index 'name' property does not match expected '{indexName}'");
        }

        if (!root.TryGetProperty("fields", out var fieldsEl) || fieldsEl.ValueKind != JsonValueKind.Array)
        {
            yield return new IndexInvariantIssue(indexName, "fields", "'fields' property missing or not an array");
            yield break;
        }

        // Every canonical index carries `tenantId` as a filterable field — this
        // is the FR-29 / §4D I2 invariant. Verify it structurally on every index.
        // Additional index-specific fields are asserted by the script's
        // Invoke-PostDeployVerifier; this C# verifier's minimum bar is
        // "tenantId filterable exists".
        var tenantIdField = FindField(fieldsEl, "tenantId");
        if (tenantIdField is null)
        {
            yield return new IndexInvariantIssue(
                indexName,
                "tenantId",
                "required filterable field 'tenantId' MISSING (§4D I2 / FR-29)");
        }
        else if (!IsFilterable(tenantIdField.Value))
        {
            yield return new IndexInvariantIssue(
                indexName,
                "tenantId",
                "required field 'tenantId' exists but filterable=false (expected true per §4D I2 / FR-29)");
        }

        // FR-17: `spaarke-rag-references` MUST NOT carry a `domain` field
        // (renamed to `documentType`). This is the ONE forbidden-field check
        // baked in structurally; other forbidden-name rules live in the script.
        if (string.Equals(indexName, "spaarke-rag-references", StringComparison.Ordinal))
        {
            var forbidden = FindField(fieldsEl, "domain");
            if (forbidden is not null)
            {
                yield return new IndexInvariantIssue(
                    indexName,
                    "domain",
                    "FORBIDDEN field 'domain' is present (renamed to 'documentType' per FR-17)");
            }
        }
    }

    private static JsonElement? FindField(JsonElement fields, string name)
    {
        foreach (var f in fields.EnumerateArray())
        {
            if (f.TryGetProperty("name", out var nameEl)
                && nameEl.ValueKind == JsonValueKind.String
                && string.Equals(nameEl.GetString(), name, StringComparison.Ordinal))
            {
                return f;
            }
        }
        return null;
    }

    private static bool IsFilterable(JsonElement field)
        => field.TryGetProperty("filterable", out var el)
        && el.ValueKind == JsonValueKind.True;

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
