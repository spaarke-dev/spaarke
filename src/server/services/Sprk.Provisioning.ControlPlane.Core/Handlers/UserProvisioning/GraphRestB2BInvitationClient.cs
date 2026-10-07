// -----------------------------------------------------------------------------
// GraphRestB2BInvitationClient.cs
//
// Production IB2BInvitationClient — raw Microsoft Graph REST via HttpClient +
// DefaultAzureCredential (parity with GraphRestAppRoleGranter.cs / H10's NFR-09
// Path-C rationale). Makes a customer user a B2B guest under the D6 B2BGuest
// identity preset.
//
// Task 232: a user who is ALREADY a guest of the tenant is reused, not
// re-invited. POST /invitations for an existing guest returns the same user but
// sends the invitation email again (sendInvitationMessage: true), so every H11
// re-run — and every gate re-check — mailed every user again. So:
//   1. GET /v1.0/users?$filter=mail eq '{email}'&$select=id,userType
//        a Guest  → reused, no invitation, no email;
//        a Member → refused: the address belongs to an account of the tenant
//                   itself (e.g. Spaarke staff), not a customer user;
//   2. none → POST /v1.0/invitations
//        { invitedUserEmailAddress, invitedUserDisplayName?, inviteRedirectUrl,
//          sendInvitationMessage: true }.
//
// Auth: DefaultAzureCredential with explicit TenantId (§4D I5); the internal
// constructor takes a credential factory so tests never touch the real chain.
// Permissions: User.Read.All (lookup), User.Invite.All (invitation) — both in the
// L2 Graph app-role catalog.
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
    private readonly Func<string, TokenCredential> _credentialFactory;
    private readonly ILogger<GraphRestB2BInvitationClient> _logger;

    /// <summary>Production constructor (typed HttpClient registration in Worker/Program.cs).</summary>
    public GraphRestB2BInvitationClient(
        HttpClient httpClient,
        IOptions<H11UserProvisioningOptions> options,
        ILogger<GraphRestB2BInvitationClient> logger)
        : this(httpClient, options, logger,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>Test seam constructor — injects the credential factory.</summary>
    internal GraphRestB2BInvitationClient(
        HttpClient httpClient,
        IOptions<H11UserProvisioningOptions> options,
        ILogger<GraphRestB2BInvitationClient> logger,
        Func<string, TokenCredential> credentialFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _credentialFactory = credentialFactory;
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
            token = await _credentialFactory(tenantId).GetTokenAsync(new TokenRequestContext(GraphScope), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new B2BInvitationOutcome.Failure(
                $"Graph token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var existing = await FindExistingUserAsync(entry.Email, token, cancellationToken).ConfigureAwait(false);
            if (existing is { Failure: { } lookupFailure })
            {
                return new B2BInvitationOutcome.Failure(lookupFailure);
            }
            if (existing is { UserId: { } existingId, UserType: var userType })
            {
                if (!string.Equals(userType, "Guest", StringComparison.OrdinalIgnoreCase))
                {
                    return new B2BInvitationOutcome.Failure(
                        $"the address belongs to user {existingId} of the tenant itself (userType '{userType}'), not a " +
                        "guest — a customer user is invited as a guest; nothing was sent.");
                }
                _logger.LogInformation(
                    "H11 B2B guest already exists — reused, no invitation sent: userId={UserId}", existingId);   // D15: no email in logs
                return new B2BInvitationOutcome.Success(existingId, InvitationId: null);
            }

            return await SendInvitationAsync(entry, token, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new B2BInvitationOutcome.Failure("A Graph request timed out; nothing is known to have been sent.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new B2BInvitationOutcome.Failure(
                $"B2B invitation infrastructure error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<(string? UserId, string? UserType, string? Failure)?> FindExistingUserAsync(
        string email, AccessToken token, CancellationToken cancellationToken)
    {
        // OData string literal: a single quote is doubled. Graph matches `mail` case-insensitively.
        var filter = Uri.EscapeDataString($"mail eq '{email.Replace("'", "''", StringComparison.Ordinal)}'");
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://graph.microsoft.com/v1.0/users?$filter={filter}&$select=id,userType");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Fail closed: inviting without knowing whether the user exists would mail an existing guest again.
            return (null, null, $"GET /users (existing-guest lookup) failed: {(int)response.StatusCode} {response.StatusCode} " +
                $"({GraphErrorCode(body)}).");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        if (!doc.RootElement.TryGetProperty("value", out var users) || users.ValueKind != JsonValueKind.Array)
        {
            return (null, null, "GET /users (existing-guest lookup) returned no 'value' array.");
        }
        var matches = users.EnumerateArray().ToList();
        if (matches.Count == 0)
        {
            return null;
        }
        if (matches.Count > 1)
        {
            return (null, null, $"{matches.Count} users of the tenant have this address — H11 cannot tell which is the " +
                "customer user; nothing was sent.");
        }

        var match = matches[0];
        var id = match.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        var type = match.TryGetProperty("userType", out var typeProp) ? typeProp.GetString() : null;
        return string.IsNullOrWhiteSpace(id)
            ? (null, null, "GET /users (existing-guest lookup) returned a user without an 'id'.")
            : (id, type, null);
    }

    private async Task<B2BInvitationOutcome> SendInvitationAsync(
        UserProvisioningEntry entry, AccessToken token, CancellationToken cancellationToken)
    {
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
                $"POST /invitations failed: {(int)response.StatusCode} {response.StatusCode} ({GraphErrorCode(body)}).");
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

    // D15: Graph's error message can echo the filter or the invited address, and the diagnostic is stored in the run
    // document — keep only Graph's error code.
    private static string GraphErrorCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var code)
                ? code.GetString() ?? "no error code"
                : "no error code";
        }
        catch (JsonException)
        {
            return "no error code";
        }
    }
}
