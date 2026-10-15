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
// HTTP-testable through the internal constructor (credential factory), over a
// hand-written HttpMessageHandler; handler unit tests substitute a fake
// IDataverseAppUserVerifier.
//
// T186 audit: the re-query runs seconds after the creator wrote the row, and
// Dataverse answers 429 / 5xx under load. A zero-row (or 404) answer is looked at
// again (bounded, PropagationRetry*) before it is judged CountMismatch(0); a
// 429 / 5xx / timeout / dropped connection is retried (TransientRetry*) and, if it
// persists, returned as Unverified — never as a CountMismatch, which H10 quarantines.
// 401 / 403 / 400 and a duplicate-row count are real answers: no retry, old outcome.
//
// auth-v4 §10.4 EXTENSION (task 205d / punch row A41): also returns the row's
// observed azureactivedirectoryobjectid so DataverseAppUserPairT2Probe (H13's
// independent re-verification) can byte-compare it against the expected UAMI
// principalId — a count=1 row alone does not prove the row is correct.
// -----------------------------------------------------------------------------

using System.Net;
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
    private readonly Func<string, TokenCredential> _credentialFactory;
    private readonly ILogger<DataverseWebApiAppUserVerifier> _logger;

    public DataverseWebApiAppUserVerifier(
        HttpClient httpClient,
        IOptions<H10DataverseAppUserGraphParityOptions> options,
        ILogger<DataverseWebApiAppUserVerifier> logger)
        : this(httpClient, options, logger,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>Test seam: a credential factory instead of DefaultAzureCredential.</summary>
    internal DataverseWebApiAppUserVerifier(
        HttpClient httpClient,
        IOptions<H10DataverseAppUserGraphParityOptions> options,
        ILogger<DataverseWebApiAppUserVerifier> logger,
        Func<string, TokenCredential> credentialFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _httpClient = httpClient;
        _options = options.Value;
        _credentialFactory = credentialFactory;
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
        AccessToken token;
        try
        {
            token = await _credentialFactory(tenantId).GetTokenAsync(
                new TokenRequestContext(new[] { scope }), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "T2 verify: token acquisition failed for applicationId={ApplicationId}", applicationId);
            return new DataverseAppUserVerificationResult.CountMismatch(0);
        }

        // $select includes azureactivedirectoryobjectid (task 205d / punch row
        // A41) so callers (DataverseAppUserPairT2Probe) can byte-compare the
        // observed value against the expected UAMI principalId per the
        // auth-v4 §10.4 silent-fail trap — a count=1 row is NOT sufficient
        // proof the row is correct.
        var uri = new Uri(envUri, $"/api/data/v9.2/systemusers?$filter=applicationid eq {applicationId}&$select=systemuserid,azureactivedirectoryobjectid");

        var lookAttempts = Math.Max(1, _options.PropagationRetryAttempts);
        var transientAttempts = Math.Max(1, _options.TransientRetryAttempts);
        var looks = 0;
        var transientFaults = 0;
        while (true)
        {
            HttpStatusCode status;
            string text;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add("OData-Version", "4.0");
                request.Headers.Add("OData-MaxVersion", "4.0");

                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                status = response.StatusCode;
                text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is OperationCanceledException)
                && H10Propagation.IsTransient(ex, cancellationToken))
            {
                // A dropped connection or an HttpClient timeout (the caller's own cancellation is not caught here).
                if (!await BackOffOnTransientAsync(++transientFaults, transientAttempts, applicationId,
                        $"{ex.GetType().Name}: {ex.Message}", cancellationToken).ConfigureAwait(false))
                {
                    return new DataverseAppUserVerificationResult.Unverified(
                        $"The systemusers query kept failing after {transientFaults} attempt(s): {ex.GetType().Name}: {ex.Message}");
                }
                continue;
            }

            if ((int)status is < 200 or > 299)
            {
                if (H10Propagation.IsTransientStatus(status))
                {
                    if (!await BackOffOnTransientAsync(++transientFaults, transientAttempts, applicationId,
                            $"HTTP {(int)status}", cancellationToken).ConfigureAwait(false))
                    {
                        return new DataverseAppUserVerificationResult.Unverified(
                            $"The systemusers query kept answering HTTP {(int)status} {status} after {transientFaults} attempt(s).");
                    }
                    continue;
                }

                if (status == HttpStatusCode.NotFound && ++looks < lookAttempts)
                {
                    await WaitForPropagationAsync(looks, lookAttempts, applicationId, "HTTP 404", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _logger.LogWarning(
                    "T2 verify: systemusers query failed {StatusCode} for applicationId={ApplicationId}",
                    (int)status, applicationId);
                return new DataverseAppUserVerificationResult.CountMismatch(0);
            }

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            var values = doc.RootElement.TryGetProperty("value", out var v) ? v : default;
            var count = values.ValueKind == JsonValueKind.Array ? values.GetArrayLength() : 0;

            if (count == 1)
            {
                var systemUserId = values[0].GetProperty("systemuserid").GetString() ?? string.Empty;
                var azureActiveDirectoryObjectId = values[0].TryGetProperty("azureactivedirectoryobjectid", out var aadObjIdProp)
                    && aadObjIdProp.ValueKind == JsonValueKind.String
                    ? aadObjIdProp.GetString()
                    : null;
                return new DataverseAppUserVerificationResult.Verified(systemUserId, azureActiveDirectoryObjectId);
            }

            if (count == 0 && ++looks < lookAttempts)
            {
                // A row the creator wrote seconds ago is not visible yet — look again before judging it absent.
                await WaitForPropagationAsync(looks, lookAttempts, applicationId, "0 rows", cancellationToken).ConfigureAwait(false);
                continue;
            }

            return new DataverseAppUserVerificationResult.CountMismatch(count);
        }
    }

    /// <summary>Logs and waits; false once the transient attempts are used (the caller returns Unverified).</summary>
    private async Task<bool> BackOffOnTransientAsync(int fault, int max, string applicationId, string what, CancellationToken ct)
    {
        if (fault >= max)
        {
            _logger.LogWarning(
                "T2 verify: systemusers query still failing after {Attempts} attempt(s) ({What}) for applicationId={ApplicationId}",
                fault, what, applicationId);
            return false;
        }
        _logger.LogInformation(
            "T2 verify: systemusers query attempt {Attempt}/{Max} hit a transient fault ({What}) for applicationId={ApplicationId}; retrying",
            fault, max, what, applicationId);
        await H10Propagation.DelayAsync(_options.TransientRetryDelay, fault, ct).ConfigureAwait(false);
        return true;
    }

    private async Task WaitForPropagationAsync(int look, int max, string applicationId, string what, CancellationToken ct)
    {
        _logger.LogInformation(
            "T2 verify: systemusers query returned {What} for applicationId={ApplicationId} (look {Look}/{Max}); " +
            "the application user may not be visible yet — looking again",
            what, applicationId, look, max);
        await H10Propagation.DelayAsync(_options.PropagationRetryDelay, look, ct).ConfigureAwait(false);
    }
}
