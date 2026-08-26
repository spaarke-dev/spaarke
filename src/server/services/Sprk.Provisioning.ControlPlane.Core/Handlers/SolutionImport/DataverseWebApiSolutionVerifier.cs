// -----------------------------------------------------------------------------
// DataverseWebApiSolutionVerifier.cs
//
// Production <see cref="ISolutionVerifier"/> implementation (task 141, Wave
// G-4, Option D hybrid) — trivial pure-HttpClient GET
// `/api/data/v9.2/solutions?$select=uniquename,version,solutionid` port of
// the retired PacCliSolutionVerifier's pac-CLI solution-listing shell-out. Per the
// POML's own framing ("trivial Dataverse Web API GET"), this collaborator is
// deliberately the simplest of the two H6 ports — a single stateless read,
// no polling, no retry loop.
//
// CREDENTIAL: unlike the retired PacCliSolutionVerifier (which reused the
// importer's already-created pac auth profile — see that file's now-obsolete
// "NON-GOALS" note), this verifier is a fully independent stateless client
// and acquires its OWN bearer token via Azure.Identity.ClientSecretCredential
// using the SAME BFF app-reg identity (ClientId/ClientSecret) the importer
// used — task 141 extended SolutionVerificationRequest with a ClientSecret
// field for exactly this reason (see ISolutionVerifier.cs).
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// <see cref="ISolutionVerifier"/> implementation that issues a single
/// <c>GET /api/data/v9.2/solutions?$select=uniquename,version,solutionid</c>
/// against the target Dataverse environment and cross-references the result
/// against the expected catalog.
/// </summary>
public sealed class DataverseWebApiSolutionVerifier : ISolutionVerifier
{
    private const string ODataVersion = "4.0";
    private const int DiagnosticTailBudget = 800;

    private readonly HttpClient _httpClient;
    private readonly SolutionImportOptions _options;
    private readonly Func<string, string, string, TokenCredential> _credentialFactory;
    private readonly ILogger<DataverseWebApiSolutionVerifier> _logger;

    /// <summary>Constructs the verifier bound to a typed <see cref="HttpClient"/> (production via <c>services.AddSingleton</c> factory in Worker/Program.cs).</summary>
    public DataverseWebApiSolutionVerifier(
        HttpClient httpClient,
        IOptions<SolutionImportOptions> options,
        ILogger<DataverseWebApiSolutionVerifier> logger)
        : this(
            httpClient,
            options,
            logger,
            (tenantId, clientId, clientSecret) => new ClientSecretCredential(tenantId, clientId, clientSecret))
    {
    }

    /// <summary>
    /// Test seam constructor — injects a <paramref name="credentialFactory"/>
    /// so tests never invoke the real ClientSecretCredential network path.
    /// </summary>
    internal DataverseWebApiSolutionVerifier(
        HttpClient httpClient,
        IOptions<SolutionImportOptions> options,
        ILogger<DataverseWebApiSolutionVerifier> logger,
        Func<string, string, string, TokenCredential> credentialFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _credentialFactory = credentialFactory;
    }

    /// <inheritdoc/>
    public async Task<SolutionVerificationOutcome> VerifyAsync(
        SolutionVerificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetDataverseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientSecret);
        if (request.ExpectedCatalog.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "ExpectedCatalog must be non-empty — verifier cannot check against an empty catalog.",
                nameof(request));
        }

        if (!Uri.TryCreate(request.TargetDataverseUrl, UriKind.Absolute, out var envUri))
        {
            return AllMissing(
                request,
                $"Target Dataverse URL '{request.TargetDataverseUrl}' is not a valid absolute URI.");
        }

        var credential = _credentialFactory(request.TenantId, request.ClientId, request.ClientSecret);
        var scope = $"{new Uri(envUri, "/")}".TrimEnd('/') + "/.default";

        AccessToken token;
        try
        {
            token = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "H6 verifier token acquisition failed for env={EnvUrl}", request.TargetDataverseUrl);
            return AllMissing(request, $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var requestUri = new Uri(envUri, "/api/data/v9.2/solutions?$select=uniquename,version,solutionid");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpRequest.Headers.Add("OData-Version", ODataVersion);
        httpRequest.Headers.Add("OData-MaxVersion", ODataVersion);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException tcex) when (!cancellationToken.IsCancellationRequested)
        {
            return AllMissing(request, $"Solutions GET timed out after {_options.DataverseWebApiRequestTimeout}: {tcex.Message}");
        }
        catch (HttpRequestException hrex)
        {
            return AllMissing(request, $"Solutions GET infrastructure error: {hrex.Message}");
        }

        using (response)
        {
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return AllMissing(
                    request,
                    $"Solutions GET returned {(int)response.StatusCode} {response.StatusCode} against " +
                    $"'{request.TargetDataverseUrl}'. Body: {Truncate(bodyText, DiagnosticTailBudget)}");
            }

            return ParseSolutionsResponse(bodyText, request.ExpectedCatalog);
        }
    }

    /// <summary>
    /// Parses the JSON <c>solutions</c> collection response + cross-references
    /// against the expected catalog. Exposed <c>internal</c> for direct unit
    /// testing (parity with the retired PacCliSolutionVerifier.ParseListOutput).
    /// </summary>
    internal static SolutionVerificationOutcome ParseSolutionsResponse(
        string bodyText,
        ImmutableArray<CanonicalSolutionEntry> expectedCatalog)
    {
        var installed = new Dictionary<string, (string Version, string SolutionId)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(bodyText);
            if (doc.RootElement.TryGetProperty("value", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in array.EnumerateArray())
                {
                    if (element.TryGetProperty("uniquename", out var un) && un.ValueKind == JsonValueKind.String)
                    {
                        var version = element.TryGetProperty("version", out var ver) && ver.ValueKind == JsonValueKind.String
                            ? ver.GetString()!
                            : string.Empty;
                        var solutionId = element.TryGetProperty("solutionid", out var sid) && sid.ValueKind == JsonValueKind.String
                            ? sid.GetString()!
                            : string.Empty;
                        installed[un.GetString()!] = (version, solutionId);
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            return new SolutionVerificationOutcome.Missing(
                expectedCatalog.Select(e => e.SolutionUniqueName).ToImmutableArray(),
                $"Solutions GET response could not be parsed as JSON: {ex.Message}. Body tail: {Truncate(bodyText, DiagnosticTailBudget)}");
        }

        var manifest = ImmutableArray.CreateBuilder<ImportedSolutionRecord>(expectedCatalog.Length);
        var missing = ImmutableArray.CreateBuilder<string>();

        foreach (var expected in expectedCatalog)
        {
            if (installed.TryGetValue(expected.SolutionUniqueName, out var found))
            {
                manifest.Add(new ImportedSolutionRecord(
                    SolutionUniqueName: expected.SolutionUniqueName,
                    Version: found.Version,
                    SolutionId: found.SolutionId,
                    Tier: expected.Tier));
            }
            else
            {
                missing.Add(expected.SolutionUniqueName);
            }
        }

        if (missing.Count > 0)
        {
            var diagnostic =
                $"Dataverse solutions collection did not return the following expected solutions after import: " +
                $"{string.Join(", ", missing)}. Body tail: {Truncate(bodyText, DiagnosticTailBudget)}";
            return new SolutionVerificationOutcome.Missing(missing.ToImmutable(), diagnostic);
        }

        return new SolutionVerificationOutcome.AllPresent(manifest.ToImmutable());
    }

    private static SolutionVerificationOutcome AllMissing(SolutionVerificationRequest request, string diagnostic)
        => new SolutionVerificationOutcome.Missing(
            request.ExpectedCatalog.Select(e => e.SolutionUniqueName).ToImmutableArray(),
            diagnostic);

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
