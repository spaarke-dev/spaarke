// -----------------------------------------------------------------------------
// SearchIndexClientProvisioner.cs
//
// Production <see cref="IAiSearchIndexProvisioner"/> — Azure.Search.Documents.
// Indexes.SearchIndexClient port (task 124, Wave G-2) REPLACING
// DeployAllIndexesScriptProvisioner's shell-out to
// scripts/ai-search/Deploy-AllIndexes.ps1. UAMI RBAC auth via the shared
// <see cref="TokenCredential"/> singleton (DefaultAzureCredential pinned to
// the L2 UAMI per ADR-028, registered by Modules.CosmosModule and reused
// here — CLAUDE.md §11 extend-don't-duplicate) — DELETES the admin-key
// handling entirely (the retired script resolved `az search admin-key show`
// twice; a security improvement, not a mechanical swap).
//
// WHY A RAW HttpPipeline PUT INSTEAD OF THE TYPED CreateOrUpdateIndexAsync(SearchIndex):
//   Verified by reflection against the installed Azure.Search.Documents 11.6.0
//   package: <see cref="Azure.Search.Documents.Indexes.Models.SearchIndex"/>
//   implements Azure.Core.IUtf8JsonSerializable (object -> JSON, WRITE only)
//   but does NOT implement IPersistableModel&lt;T&gt; in this SDK version, so
//   there is no public JSON -> SearchIndex read path. Hand-authoring 7 typed
//   SearchIndex/SearchField/VectorSearch/SemanticSearch object graphs in C#
//   would FORK the schema away from infrastructure/ai-search/*.json (the
//   FR-05 / FR-07 canonical index catalog authority per this project's
//   CLAUDE.md + scripts/ai-search/Deploy-AllIndexes.ps1's own $Catalog) — a
//   duplicate-maintenance trap that is the exact opposite of
//   CanonicalIndexCatalog.cs's documented two-place-edit forcing function.
//
//   Instead, this provisioner sends the SAME JSON bytes the retired script
//   PUT (embedded verbatim from infrastructure/ai-search/*.json — see
//   IndexSchemas/ below), over <see cref="SearchIndexClient.Pipeline"/> — the
//   SDK's own PUBLIC extensibility seam (confirmed via reflection:
//   SearchIndexClient exposes a public `HttpPipeline Pipeline { get; }`
//   property). This is a genuine SDK call: auth-header injection (Bearer
//   token from the injected TokenCredential), retry policy, and
//   endpoint/transport handling all run through the client's real pipeline;
//   only the typed object-model layer is bypassed (there is none to bypass
//   FROM, since the source representation is already JSON, not a C# object).
//   Azure SDK guidance documents Pipeline as the sanctioned low-level escape
//   hatch for exactly this scenario.
//
// EMBEDDED SCHEMA FILES: Handlers/AiSearchIndex/IndexSchemas/*.json are
// byte-identical copies of infrastructure/ai-search/spaarke-*.json (verified
// via checksum at copy time), embedded as .csproj <EmbeddedResource> entries
// with explicit <LogicalName> so the L2 App Service publish output is
// self-contained (no repo-checkout dependency at runtime — the retired
// script read the same files via a local path, which does not survive an
// Azure App Service zip-deploy). Comment keys (`"// ...": "..."` /
// `"_comment_": "..."`) are stripped before PUT — Azure AI Search REST
// rejects unknown index properties. Ports Remove-JsonCommentKeys from the
// retired script verbatim (same two regex patterns, same semantics).
//
// TWO-PLACE-EDIT FORCING FUNCTION (parity with CanonicalIndexCatalog.cs):
//   SchemaResourceNames below maps canonical index name -> embedded resource
//   file name. A PR that adds/renames a file under infrastructure/ai-search/
//   MUST update BOTH the .csproj embedded-resource glob target (automatic —
//   the glob covers the whole IndexSchemas/ folder) AND this map (manual —
//   ResolveSchemaResourceName throws a domain Failure, not a silent skip, on
//   a canonical name with no registered resource).
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <summary>
/// Executes the 7-index deploy for Model 2 (dedicated) AI Search services via
/// <see cref="SearchIndexClient"/> under UAMI RBAC — zero admin-key handling.
/// See file header for the raw-pipeline-PUT design rationale.
/// </summary>
public sealed partial class SearchIndexClientProvisioner : IAiSearchIndexProvisioner
{
    private const string SchemaResourcePrefix =
        "Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex.IndexSchemas.";

    /// <summary>
    /// Canonical index name -&gt; embedded resource file name. MUST stay in
    /// sync with infrastructure/ai-search/*.json (task 002 audit § 1 catalog
    /// authority) and with <see cref="CanonicalIndexCatalog.CanonicalIndexNames"/>.
    /// </summary>
    private static readonly ImmutableDictionary<string, string> SchemaResourceNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["spaarke-files-index"] = "spaarke-files-index.json",
            ["spaarke-discovery-index"] = "spaarke-discovery-index.json",
            ["spaarke-records-index"] = "spaarke-records-index.json",
            ["spaarke-rag-references"] = "spaarke-rag-references.json",
            ["spaarke-insights-index"] = "spaarke-insights-index.json",
            ["spaarke-session-files"] = "spaarke-session-files.json",
            ["spaarke-invoices-index"] = "spaarke-invoices-index.json",
        }.ToImmutableDictionary();

    // Ports Deploy-AllIndexes.ps1's Remove-JsonCommentKeys regexes verbatim
    // (two comment-key conventions: `"// rationale": "..."` on most schemas;
    // `"_comment_": "..."` on spaarke-insights-index.json).
    [GeneratedRegex("\"//[^\"]*\"\\s*:\\s*\"[^\"]*\"\\s*,?\\s*")]
    private static partial Regex SlashCommentKeyPattern();

    [GeneratedRegex("\"_comment_\"\\s*:\\s*\"[^\"]*\"\\s*,?\\s*")]
    private static partial Regex UnderscoreCommentKeyPattern();

    private readonly TokenCredential _credential;
    private readonly AiSearchIndexOptions _options;
    private readonly SearchClientOptions? _clientOptionsOverride;
    private readonly ILogger<SearchIndexClientProvisioner> _logger;

    /// <summary>Constructs the production provisioner bound to the shared platform <see cref="TokenCredential"/>.</summary>
    public SearchIndexClientProvisioner(
        TokenCredential credential,
        IOptions<AiSearchIndexOptions> options,
        ILogger<SearchIndexClientProvisioner> logger)
        : this(credential, options, clientOptionsOverride: null, logger)
    {
    }

    /// <summary>
    /// Test seam constructor — injects a <see cref="SearchClientOptions"/>
    /// carrying a fake <see cref="HttpPipelineTransport"/> so unit tests
    /// exercise the REAL <see cref="SearchIndexClient"/> pipeline (auth
    /// header, URL building, retry policy) against a canned HTTP response.
    /// Never used in production — Worker DI resolves the public ctor.
    /// </summary>
    internal SearchIndexClientProvisioner(
        TokenCredential credential,
        IOptions<AiSearchIndexOptions> options,
        SearchClientOptions? clientOptionsOverride,
        ILogger<SearchIndexClientProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _credential = credential;
        _options = options.Value;
        _clientOptionsOverride = clientOptionsOverride;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AiSearchIndexProvisionOutcome> ProvisionAsync(
        AiSearchIndexProvisionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SearchEndpoint);

        // Contract parity with the retired DeployAllIndexesScriptProvisioner:
        // an empty request list means "canonical 7" (in practice the handler
        // always resolves a non-empty list via ICanonicalIndexCatalog before
        // building the request — this is a defensive fallback, not the
        // primary path).
        var indexNames = request.RequestedIndexNames.IsDefaultOrEmpty
            ? SchemaResourceNames.Keys.ToImmutableArray()
            : request.RequestedIndexNames;

        var clientOptions = _clientOptionsOverride ?? new SearchClientOptions();
        var client = new SearchIndexClient(new Uri(request.SearchEndpoint), _credential, clientOptions);

        var provisioned = ImmutableArray.CreateBuilder<string>(indexNames.Length);
        foreach (var indexName in indexNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!SchemaResourceNames.TryGetValue(indexName, out var resourceFileName))
            {
                return new AiSearchIndexProvisionOutcome.Failure(
                    $"No embedded schema resource registered for index '{indexName}'. " +
                    "SearchIndexClientProvisioner.SchemaResourceNames and infrastructure/ai-search/*.json are " +
                    "out of sync — this is the two-place-edit forcing function (parity with CanonicalIndexCatalog.cs) " +
                    "catching a drifted catalog BEFORE a live PUT, not a silent skip.");
            }

            string schemaJson;
            try
            {
                schemaJson = LoadAndStripSchema(resourceFileName);
            }
            catch (InvalidOperationException ex)
            {
                return new AiSearchIndexProvisionOutcome.Failure(ex.Message);
            }

            using var message = client.Pipeline.CreateMessage();
            message.Request.Method = RequestMethod.Put;
            message.Request.Uri.Reset(new Uri(
                $"{request.SearchEndpoint.TrimEnd('/')}/indexes/{Uri.EscapeDataString(indexName)}?api-version={_options.SearchApiVersion}"));
            message.Request.Headers.Add("Content-Type", "application/json");
            message.Request.Content = RequestContent.Create(schemaJson);

            _logger.LogInformation(
                "H2b SearchIndexClient PUT starting: customerId={CustomerId} index={IndexName} endpoint={Endpoint}",
                request.CustomerId, indexName, request.SearchEndpoint);

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.RestCallTimeout);
                await client.Pipeline.SendAsync(message, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new AiSearchIndexProvisionOutcome.Failure(
                    $"PUT index '{indexName}' at '{request.SearchEndpoint}' timed out after {_options.RestCallTimeout} " +
                    "(NFR-12 target < 30 min for the full 7-index pass).");
            }

            var status = message.Response.Status;
            if (status is not (200 or 201))
            {
                var body = message.Response.Content.ToString();
                _logger.LogError(
                    "H2b SearchIndexClient PUT failed: customerId={CustomerId} index={IndexName} status={Status}",
                    request.CustomerId, indexName, status);
                return new AiSearchIndexProvisionOutcome.Failure(
                    $"PUT index '{indexName}' at '{request.SearchEndpoint}' returned HTTP {status}: {Truncate(body, 800)}");
            }

            provisioned.Add(indexName);
        }

        _logger.LogInformation(
            "H2b SearchIndexClient PUT succeeded: customerId={CustomerId} indexCount={IndexCount}",
            request.CustomerId, provisioned.Count);

        return new AiSearchIndexProvisionOutcome.Success(provisioned.ToImmutable());
    }

    /// <summary>
    /// Reads the embedded schema resource + strips comment keys. Exposed
    /// internal so unit tests can assert the stripped-JSON shape directly
    /// without a full ProvisionAsync round-trip.
    /// </summary>
    internal static string LoadAndStripSchema(string resourceFileName)
    {
        var resourceName = SchemaResourcePrefix + resourceFileName;
        var assembly = typeof(SearchIndexClientProvisioner).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. Verify Sprk.Provisioning.ControlPlane.Core.csproj's " +
                "<EmbeddedResource> glob covers Handlers/AiSearchIndex/IndexSchemas/*.json with the matching <LogicalName>.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var raw = reader.ReadToEnd();
        var stripped = SlashCommentKeyPattern().Replace(raw, string.Empty);
        return UnderscoreCommentKeyPattern().Replace(stripped, string.Empty);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
