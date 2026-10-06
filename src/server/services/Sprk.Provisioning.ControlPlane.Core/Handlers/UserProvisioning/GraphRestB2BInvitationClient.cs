// -----------------------------------------------------------------------------
// GraphRestB2BInvitationClient.cs
//
// Production IB2BInvitationClient — raw Microsoft Graph REST POST /invitations
// call via HttpClient + DefaultAzureCredential (parity with
// GraphRestAppRoleGranter.cs / H10's NFR-09 Path-C rationale). Sends a B2B
// guest invitation for a customer user under the D6 B2BGuest identity preset.
//
// Endpoint shape: POST /v1.0/invitations
//   { invitedUserEmailAddress, invitedUserDisplayName, inviteRedirectUrl,
//     sendInvitationMessage: true }
//
// Auth: DefaultAzureCredential with explicit TenantId (§4D I5).
//
// IDEMPOTENCY: re-POSTing an invitation for an already-invited email is a
// Graph-side no-op that resends the invitation email and returns the SAME
// invitedUser.id — safe to retry (parity with H10's individually-idempotent
// role grants).
//
// NOT under test in the CI unit suite (real Graph REST calls). Handler unit
// tests substitute a fake IB2BInvitationClient.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <inheritdoc cref="IB2BInvitationClient"/>
public sealed class GraphRestB2BInvitationClient : IB2BInvitationClient
{
    private static readonly string[] GraphScope = { "https://graph.microsoft.com/.default" };

    private readonly HttpClient _httpClient;
    private readonly H11UserProvisioningOptions _options;
    private readonly ILogger<GraphRestB2BInvitationClient> _logger;

    public GraphRestB2BInvitationClient(
        HttpClient httpClient,
        IOptions<H11UserProvisioningOptions> options,
        ILogger<GraphRestB2BInvitationClient> logger)
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
    public async Task<B2BInvitationOutcome> InviteAsync(
        UserProvisioningEntry entry, string tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (string.IsNullOrWhiteSpace(entry.Email))
        {
            return new B2BInvitationOutcome.Failure("entry.Email is required for a B2B invitation.");
        }

        AccessToken token;
        try
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });
            token = await credential.GetTokenAsync(new TokenRequestContext(GraphScope), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new B2BInvitationOutcome.Failure(
                $"Graph token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var payload = new Dictionary<string, object?>
        {
            ["invitedUserEmailAddress"] = entry.Email,
            ["inviteRedirectUrl"] = _options.InvitationRedirectUrl,
            ["sendInvitationMessage"] = true,
        };
        // The display name is optional for a guest (task 245c: names are not required for B2BGuest) — send it only
        // when there is one, rather than a blank " ".
        var displayName = $"{entry.FirstName} {entry.LastName}".Trim();
        if (displayName.Length > 0)
        {
            payload["invitedUserDisplayName"] = displayName;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/invitations")
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new B2BInvitationOutcome.Failure(
                    $"POST /invitations failed: {(int)response.StatusCode} {response.StatusCode}. " +
                    $"Body: {Truncate(body, 300)}");
            }

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var invitationId = doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var invitedUserId = doc.RootElement.TryGetProperty("invitedUser", out var invitedUserProp)
                && invitedUserProp.TryGetProperty("id", out var invitedUserIdProp)
                ? invitedUserIdProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(invitedUserId) || string.IsNullOrWhiteSpace(invitationId))
            {
                return new B2BInvitationOutcome.Failure(
                    "Graph invitation response was missing 'id' or 'invitedUser.id'.");
            }

            _logger.LogInformation(
                "H11 B2B invitation sent — invitedUserId={InvitedUserId}", invitedUserId);   // D15: no email in logs
            return new B2BInvitationOutcome.Success(invitedUserId, invitationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new B2BInvitationOutcome.Failure(
                $"POST /invitations infrastructure error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
