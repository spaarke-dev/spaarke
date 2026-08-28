// -----------------------------------------------------------------------------
// GraphRestB2BConsentVerifier.cs
//
// Production IB2BConsentVerifier — raw Microsoft Graph REST GET calls to
// check each invited guest's externalUserState (parity with
// GraphRestAppRoleGranter.cs / H10's NFR-09 Path-C rationale + H3's
// IAdminConsentVerifier gate-verification shape). "Accepted" means the
// customer-tenant user redeemed the B2B invitation.
//
// Endpoint shape: GET /v1.0/users/{id}?$select=externalUserState
//
// Auth: DefaultAzureCredential with explicit TenantId (§4D I5).
//
// NOT under test in the CI unit suite (real Graph REST calls). Handler unit
// tests substitute a fake IB2BConsentVerifier.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <inheritdoc cref="IB2BConsentVerifier"/>
public sealed class GraphRestB2BConsentVerifier : IB2BConsentVerifier
{
    private static readonly string[] GraphScope = { "https://graph.microsoft.com/.default" };

    private readonly HttpClient _httpClient;
    private readonly H11UserProvisioningOptions _options;
    private readonly ILogger<GraphRestB2BConsentVerifier> _logger;

    public GraphRestB2BConsentVerifier(
        HttpClient httpClient,
        IOptions<H11UserProvisioningOptions> options,
        ILogger<GraphRestB2BConsentVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = _options.GraphRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<B2BConsentVerificationResult> VerifyAsync(
        string tenantId, IReadOnlyList<string> invitedUserIds, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(invitedUserIds);

        var expected = invitedUserIds.Count;
        if (expected == 0)
        {
            return new B2BConsentVerificationResult.Verified(0, 0, Evidence: null);
        }

        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });
        var token = await credential.GetTokenAsync(new TokenRequestContext(GraphScope), cancellationToken)
            .ConfigureAwait(false);

        var accepted = 0;
        var pendingIds = new List<string>();
        foreach (var userId in invitedUserIds)
        {
            var uri = new Uri($"https://graph.microsoft.com/v1.0/users/{userId}?$select=externalUserState");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                pendingIds.Add(userId);
                continue;
            }

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            var state = doc.RootElement.TryGetProperty("externalUserState", out var stateProp)
                ? stateProp.GetString()
                : null;
            if (string.Equals(state, "Accepted", StringComparison.OrdinalIgnoreCase))
            {
                accepted++;
            }
            else
            {
                pendingIds.Add(userId);
            }
        }

        if (accepted == expected)
        {
            return new B2BConsentVerificationResult.Verified(accepted, expected, Evidence: null);
        }

        var diagnostic =
            $"{pendingIds.Count} of {expected} invited guest(s) have not yet accepted their B2B invitation: " +
            $"{string.Join(", ", pendingIds)}.";
        _logger.LogInformation("H11 B2B consent Pending: {Diagnostic}", diagnostic);
        return new B2BConsentVerificationResult.Pending(accepted, expected, diagnostic, Evidence: null);
    }
}
