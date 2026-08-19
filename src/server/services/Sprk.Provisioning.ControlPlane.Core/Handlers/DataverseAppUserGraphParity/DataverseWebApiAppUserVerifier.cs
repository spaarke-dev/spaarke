// -----------------------------------------------------------------------------
// DataverseWebApiAppUserVerifier.cs
//
// Production IDataverseAppUserVerifier — the T2 silent-fail trap post-
// condition check (spec.md FR-33): independently re-queries
// `systemusers?$filter=applicationid eq {applicationId}` and asserts the
// returned count is exactly 1. Deliberately does NOT reuse the creator's
// in-memory result — a fresh HTTP round-trip is the point (catches the
// "creator believed it succeeded but Dataverse never actually persisted it"
// shape).
//
// NOT under test in the CI unit suite (real Dataverse Web API call). Handler
// unit tests substitute a fake IDataverseAppUserVerifier.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <inheritdoc cref="IDataverseAppUserVerifier"/>
public sealed class DataverseWebApiAppUserVerifier : IDataverseAppUserVerifier
{
    private readonly HttpClient _httpClient;
    private readonly H10DataverseAppUserGraphParityOptions _options;
    private readonly ILogger<DataverseWebApiAppUserVerifier> _logger;

    public DataverseWebApiAppUserVerifier(
        HttpClient httpClient,
        IOptions<H10DataverseAppUserGraphParityOptions> options,
        ILogger<DataverseWebApiAppUserVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = _options.DataverseRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<DataverseAppUserVerificationResult> VerifyAsync(
        string environmentUrl,
        string tenantId,
        string applicationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        if (!Uri.TryCreate(environmentUrl, UriKind.Absolute, out var envUri))
        {
            _logger.LogWarning("T2 verify: invalid environment URL '{EnvUrl}'", environmentUrl);
            return new DataverseAppUserVerificationResult.CountMismatch(0);
        }

        // Defense-in-depth: same rationale as DataverseWebApiAppUserCreator —
        // applicationId is interpolated into an OData $filter below.
        if (!Guid.TryParse(applicationId, out _))
        {
            _logger.LogWarning("T2 verify: applicationId '{ApplicationId}' is not a valid GUID", applicationId);
            return new DataverseAppUserVerificationResult.CountMismatch(0);
        }

        var scopeBase = new Uri(envUri, "/").ToString().TrimEnd('/');
        var scope = $"{scopeBase}/.default";
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });
        AccessToken token;
        try
        {
            token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "T2 verify: token acquisition failed for applicationId={ApplicationId}", applicationId);
            return new DataverseAppUserVerificationResult.CountMismatch(0);
        }

        var uri = new Uri(envUri, $"/api/data/v9.2/systemusers?$filter=applicationid eq {applicationId}&$select=systemuserid");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Add("OData-MaxVersion", "4.0");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "T2 verify: systemusers query failed {StatusCode} for applicationId={ApplicationId}",
                (int)response.StatusCode, applicationId);
            return new DataverseAppUserVerificationResult.CountMismatch(0);
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        var values = doc.RootElement.TryGetProperty("value", out var v) ? v : default;
        var count = values.ValueKind == JsonValueKind.Array ? values.GetArrayLength() : 0;

        if (count == 1)
        {
            var systemUserId = values[0].GetProperty("systemuserid").GetString() ?? string.Empty;
            return new DataverseAppUserVerificationResult.Verified(systemUserId);
        }

        return new DataverseAppUserVerificationResult.CountMismatch(count);
    }
}
