// -----------------------------------------------------------------------------
// DataverseWebApiSolutionVerifier.cs
//
// Production <see cref="ISolutionVerifier"/> implementation (task 141; T218b) —
// one stateless GET of the SpaarkeMaster solution row
// (`uniquename, version, solutionid, ismanaged`): present AND ismanaged equal
// to the run's package type, or Missing. No polling, no retry loop.
//
// CREDENTIAL: acquires its OWN bearer token through the FR-39 ordered chain
// (WorkerDataverseCredentialFactory — MI-FIC first) as the same BFF app-reg
// identity the importer used.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.Credentials;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// <see cref="ISolutionVerifier"/> implementation that issues a single filtered <c>GET /api/data/v9.2/solutions</c>
/// for SpaarkeMaster against the target Dataverse environment and checks presence + type.
/// </summary>
public sealed class DataverseWebApiSolutionVerifier : ISolutionVerifier
{
    private const string ODataVersion = "4.0";
    private const int DiagnosticTailBudget = 800;

    private readonly HttpClient _httpClient;
    private readonly SolutionImportOptions _options;
    private readonly Func<string, string, string, TokenCredential> _credentialFactory;
    private readonly ILogger<DataverseWebApiSolutionVerifier> _logger;

    /// <summary>
    /// Constructs the verifier bound to a typed <see cref="HttpClient"/>
    /// (production via DI typed-client registration in Worker/Program.cs —
    /// <paramref name="credentialFactory"/> resolves from the container).
    /// A44.5 (task 205i): the credential is selected by the FR-39 ordered
    /// chain (MI-FIC first on secret-free envs; ClientSecret only for
    /// prong-3 unmigrated envs) — raw <c>ClientSecretCredential</c>
    /// construction lives ONLY in the factory's fallback branch.
    /// </summary>
    public DataverseWebApiSolutionVerifier(
        HttpClient httpClient,
        IOptions<SolutionImportOptions> options,
        WorkerDataverseCredentialFactory credentialFactory,
        ILogger<DataverseWebApiSolutionVerifier> logger)
        : this(
            httpClient,
            options,
            logger,
            (tenantId, clientId, clientSecret) => credentialFactory
                .Create(
                    options.Value.Credentials,
                    SolutionImportOptions.SectionName,
                    tenantId,
                    clientId,
                    string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret)
                .Credential)
    {
        ArgumentNullException.ThrowIfNull(credentialFactory);
    }

    /// <summary>
    /// Test seam constructor — injects a <paramref name="credentialFactory"/>
    /// so tests never invoke a real credential network path. The delegate's
    /// third parameter is the resolved secret slot value — EMPTY STRING on
    /// secret-free environments (A44.5).
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
        // A44.5: ClientSecret deliberately NOT required — empty on secret-free
        // envs (the signal, §9.1); the FR-39 credential factory selects MI-FIC.
        if (!Uri.TryCreate(request.TargetDataverseUrl, UriKind.Absolute, out var envUri))
        {
            return AllMissing(
                request,
                $"Target Dataverse URL '{request.TargetDataverseUrl}' is not a valid absolute URI.");
        }

        var scope = $"{new Uri(envUri, "/")}".TrimEnd('/') + "/.default";

        AccessToken token;
        try
        {
            // A44.5: credential selection (FR-39 ordered chain in production)
            // can itself throw on an exhausted chain — same failure boundary
            // as a failed token acquisition.
            var credential = _credentialFactory(
                request.TenantId, request.ClientId, request.ClientSecret ?? string.Empty);
            token = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Diagnostic prefix kept as "Token acquisition failed" (task-141
            // test contract preserved verbatim) — an A44.5 credential-SELECTION
            // failure is still surfaced distinctly via the inner exception's
            // own message ("No credential could be selected …").
            _logger.LogWarning(ex, "H6 verifier credential selection / token acquisition failed for env={EnvUrl}", request.TargetDataverseUrl);
            return AllMissing(request, $"Token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var requestUri = new Uri(envUri,
            $"/api/data/v9.2/solutions?$select=uniquename,version,solutionid,ismanaged&$filter=uniquename eq '{SpaarkePackage.SolutionUniqueName}'");
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

            return ParseSolutionsResponse(bodyText, request.Managed, request.ExpectedVersion);
        }
    }

    /// <summary>
    /// Parses the JSON <c>solutions</c> response and checks SpaarkeMaster is present with <c>ismanaged</c> equal to
    /// <paramref name="managed"/> and, when given, the version equal to <paramref name="expectedVersion"/>. Exposed
    /// <c>internal</c> for direct unit testing.
    /// </summary>
    internal static SolutionVerificationOutcome ParseSolutionsResponse(string bodyText, bool managed, string? expectedVersion = null)
    {
        var name = SpaarkePackage.SolutionUniqueName;
        try
        {
            using var doc = JsonDocument.Parse(bodyText);
            if (doc.RootElement.TryGetProperty("value", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in array.EnumerateArray())
                {
                    if (!(element.TryGetProperty("uniquename", out var un) && un.ValueKind == JsonValueKind.String
                          && string.Equals(un.GetString(), name, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var version = element.TryGetProperty("version", out var ver) && ver.ValueKind == JsonValueKind.String
                        ? ver.GetString()!
                        : string.Empty;
                    var solutionId = element.TryGetProperty("solutionid", out var sid) && sid.ValueKind == JsonValueKind.String
                        ? sid.GetString()!
                        : string.Empty;
                    var isManaged = element.TryGetProperty("ismanaged", out var m) && m.ValueKind == JsonValueKind.True;

                    if (isManaged != managed)
                    {
                        return new SolutionVerificationOutcome.Missing(
                            ImmutableArray.Create(name),
                            $"{name} {version} is installed as {(isManaged ? "managed" : "unmanaged")}, but the run " +
                            $"imported the {(managed ? "managed" : "unmanaged")} package.");
                    }

                    if (expectedVersion is not null && SpaarkePackage.CompareVersions(version, expectedVersion) != 0)
                    {
                        return new SolutionVerificationOutcome.Missing(
                            ImmutableArray.Create(name),
                            $"{name} is installed at version '{version}', not the imported package version " +
                            $"'{expectedVersion}' — the import reported success but the environment did not change.");
                    }

                    return new SolutionVerificationOutcome.AllPresent(ImmutableArray.Create(
                        new ImportedSolutionRecord(name, version, solutionId, isManaged)));
                }
            }
        }
        catch (JsonException ex)
        {
            return new SolutionVerificationOutcome.Missing(
                ImmutableArray.Create(name),
                $"Solutions GET response could not be parsed as JSON: {ex.Message}. Body tail: {Truncate(bodyText, DiagnosticTailBudget)}");
        }

        return new SolutionVerificationOutcome.Missing(
            ImmutableArray.Create(name),
            $"Dataverse did not return {name} after the import. Body tail: {Truncate(bodyText, DiagnosticTailBudget)}");
    }

    private static SolutionVerificationOutcome AllMissing(SolutionVerificationRequest request, string diagnostic)
        => new SolutionVerificationOutcome.Missing(ImmutableArray.Create(SpaarkePackage.SolutionUniqueName), diagnostic);

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
