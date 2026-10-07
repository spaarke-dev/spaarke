// -----------------------------------------------------------------------------
// GraphRestEnvironmentSecurityGroupClient.cs
//
// Task 232. Production IEnvironmentSecurityGroupClient — raw Microsoft Graph REST
// via HttpClient + DefaultAzureCredential (same idiom as the other H11 seams).
//
//   GET  /v1.0/groups/{id}?$select=displayName,securityEnabled
//   POST /v1.0/groups/{id}/members/$ref
//        { "@odata.id": "https://graph.microsoft.com/v1.0/directoryObjects/{userId}" }
//        204 → added; 400 "One or more added object references already exist" → already a member.
//
// Permissions: GroupMember.ReadWrite.All (in the L2 Graph app-role catalog).
// Auth: DefaultAzureCredential with explicit TenantId (§4D I5); the internal
// constructor takes a credential factory so tests never touch the real chain.
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <inheritdoc cref="IEnvironmentSecurityGroupClient"/>
public sealed class GraphRestEnvironmentSecurityGroupClient : IEnvironmentSecurityGroupClient
{
    private static readonly string[] GraphScope = { "https://graph.microsoft.com/.default" };

    // Graph's message for adding a member that is already one (400 Request_BadRequest).
    private const string AlreadyMemberMessage = "added object references already exist";

    private readonly HttpClient _httpClient;
    private readonly Func<string, TokenCredential> _credentialFactory;

    /// <summary>Production constructor (typed HttpClient registration in Worker/Program.cs).</summary>
    public GraphRestEnvironmentSecurityGroupClient(HttpClient httpClient, IOptions<H11UserProvisioningOptions> options)
        : this(httpClient, options,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>Test seam constructor — injects the credential factory.</summary>
    internal GraphRestEnvironmentSecurityGroupClient(
        HttpClient httpClient,
        IOptions<H11UserProvisioningOptions> options,
        Func<string, TokenCredential> credentialFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _httpClient = httpClient;
        _credentialFactory = credentialFactory;
        _httpClient.Timeout = options.Value.GraphRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<SecurityGroupReadOutcome> ReadAsync(string groupId, string tenantId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(groupId, out var groupGuid))
        {
            return new SecurityGroupReadOutcome.Failure($"'{groupId}' is not a group object id (GUID).");
        }

        try
        {
            var token = await AcquireTokenAsync(tenantId, cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://graph.microsoft.com/v1.0/groups/{groupGuid:D}?$select=displayName,securityEnabled");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new SecurityGroupReadOutcome.Failure(
                    $"GET /groups/{groupGuid:D} failed: {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(body, 300)}");
            }

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var displayName = doc.RootElement.TryGetProperty("displayName", out var nameProp) ? nameProp.GetString() : null;
            var securityEnabled = doc.RootElement.TryGetProperty("securityEnabled", out var secProp)
                && secProp.ValueKind == JsonValueKind.True;
            return new SecurityGroupReadOutcome.Found(displayName ?? string.Empty, securityEnabled);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SecurityGroupReadOutcome.Failure($"Group read infrastructure error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<SecurityGroupMembershipOutcome> AddMemberAsync(
        string groupId, string userId, string tenantId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(groupId, out var groupGuid) || !Guid.TryParse(userId, out var userGuid))
        {
            return new SecurityGroupMembershipOutcome.Failure("group and user must be object ids (GUIDs).");
        }

        try
        {
            var token = await AcquireTokenAsync(tenantId, cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"https://graph.microsoft.com/v1.0/groups/{groupGuid:D}/members/$ref")
            {
                Content = JsonContent.Create(new Dictionary<string, string>
                {
                    ["@odata.id"] = $"https://graph.microsoft.com/v1.0/directoryObjects/{userGuid:D}",
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new SecurityGroupMembershipOutcome.Success();
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.BadRequest
                && body.Contains(AlreadyMemberMessage, StringComparison.OrdinalIgnoreCase))
            {
                return new SecurityGroupMembershipOutcome.Success();
            }

            return new SecurityGroupMembershipOutcome.Failure(
                $"POST /groups/{groupGuid:D}/members/$ref for user {userGuid:D} failed: {(int)response.StatusCode} " +
                $"{response.StatusCode}. Body: {Truncate(body, 300)}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SecurityGroupMembershipOutcome.Failure(
                $"Group membership infrastructure error for user {userGuid:D}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<AccessToken> AcquireTokenAsync(string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return await _credentialFactory(tenantId).GetTokenAsync(new TokenRequestContext(GraphScope), cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
