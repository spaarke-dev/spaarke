// -----------------------------------------------------------------------------
// GraphRestUserProvisioner.cs
//
// Production IGraphUserProvisioner — raw Microsoft Graph REST calls (NOT the
// Microsoft.Graph SDK; see H10DataverseAppUserGraphParityHandler.cs file
// header "NFR-09 IMPLEMENTATION NOTE" for the Path-C rationale this handler
// follows) via HttpClient + DefaultAzureCredential. Ports the BFF's
// GraphUserService.CreateUserAsync + AssignLicensesAsync shape (UPN
// generation, forceChangePasswordNextSignIn, license SKU assignment) to L2's
// REST-only collaborator style — parity with GraphRestAppRoleGranter.cs.
//
// Endpoint shapes:
//   GET  /v1.0/users?$filter=userPrincipalName eq '{upn}'&$select=id&$top=1
//   POST /v1.0/users
//   POST /v1.0/users/{id}/assignLicense
//
// Auth: DefaultAzureCredential with explicit TenantId (§4D I5 — never a
// default-tenant credential); scope "https://graph.microsoft.com/.default".
//
// IDEMPOTENCY: CreateUserAsync checks the generated UPN for an existing user
// FIRST (per POML constraint: "user-level idempotency via UPN alt-key inside
// Graph SDK") — a match short-circuits to Success with the existing user's
// id + UPN rather than attempting a duplicate POST /users.
//
// NOT under test in the CI unit suite (real Graph REST calls). Handler unit
// tests substitute a fake IGraphUserProvisioner.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <inheritdoc cref="IGraphUserProvisioner"/>
public sealed class GraphRestUserProvisioner : IGraphUserProvisioner
{
    private static readonly string[] GraphScope = { "https://graph.microsoft.com/.default" };

    private readonly HttpClient _httpClient;
    private readonly H11UserProvisioningOptions _options;
    private readonly ILogger<GraphRestUserProvisioner> _logger;

    public GraphRestUserProvisioner(
        HttpClient httpClient,
        IOptions<H11UserProvisioningOptions> options,
        ILogger<GraphRestUserProvisioner> logger)
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
    public async Task<UserCreationOutcome> CreateUserAsync(
        UserProvisioningEntry entry, string tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new UserCreationOutcome.Failure(
                $"Graph token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var upn = BuildUpn(entry.FirstName, entry.LastName);

        // Idempotency: existing-UPN short-circuit (POML constraint — user-
        // level idempotency via UPN alt-key).
        try
        {
            var existingId = await FindUserIdByUpnAsync(token, upn, cancellationToken).ConfigureAwait(false);
            if (existingId is not null)
            {
                _logger.LogInformation(
                    "H11 user already exists for UPN {Upn} — idempotent no-op (userId={UserId})", upn, existingId);
                return new UserCreationOutcome.Success(existingId, upn);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new UserCreationOutcome.Failure($"UPN availability check failed for '{upn}': {ex.Message}");
        }

        var payload = new Dictionary<string, object?>
        {
            ["accountEnabled"] = true,
            ["displayName"] = $"{entry.FirstName} {entry.LastName}",
            ["givenName"] = entry.FirstName,
            ["surname"] = entry.LastName,
            ["companyName"] = entry.CompanyName,
            ["mailNickname"] = $"{SanitizeName(entry.FirstName)}.{SanitizeName(entry.LastName)}",
            ["userPrincipalName"] = upn,
            ["usageLocation"] = "US",
            ["passwordProfile"] = new Dictionary<string, object?>
            {
                ["forceChangePasswordNextSignIn"] = true,
                ["password"] = GenerateTemporaryPassword(),
            },
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/users")
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new UserCreationOutcome.Failure(
                    $"POST /users failed for '{upn}': {(int)response.StatusCode} {response.StatusCode}. " +
                    $"Body: {Truncate(body, 300)}");
            }

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var userId = doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new UserCreationOutcome.Failure($"Graph returned no 'id' after creating user '{upn}'.");
            }

            _logger.LogInformation("H11 created Entra user {UserId} with UPN {Upn}", userId, upn);
            return new UserCreationOutcome.Success(userId, upn);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new UserCreationOutcome.Failure(
                $"POST /users infrastructure error for '{upn}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<LicenseAssignmentOutcome> AssignLicenseAsync(
        string userId, string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var skuIds = new[] { _options.PowerAppsPlan2TrialSkuId, _options.FabricFreeSkuId, _options.PowerAutomateFreeSkuId }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (skuIds.Count == 0)
        {
            _logger.LogInformation(
                "H11 no license SKUs configured — skipping assignLicense for user {UserId}", userId);
            return new LicenseAssignmentOutcome.Success();
        }

        AccessToken token;
        try
        {
            token = await AcquireTokenAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new LicenseAssignmentOutcome.Failure(
                $"Graph token acquisition failed: {ex.GetType().Name}: {ex.Message}");
        }

        var payload = new Dictionary<string, object?>
        {
            ["addLicenses"] = skuIds.Select(sku => new Dictionary<string, object?> { ["skuId"] = sku }).ToList(),
            ["removeLicenses"] = Array.Empty<string>(),
        };

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"https://graph.microsoft.com/v1.0/users/{userId}/assignLicense")
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new LicenseAssignmentOutcome.Failure(
                    $"POST /users/{userId}/assignLicense failed: {(int)response.StatusCode} {response.StatusCode}. " +
                    $"Body: {Truncate(body, 300)}");
            }

            return new LicenseAssignmentOutcome.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new LicenseAssignmentOutcome.Failure(
                $"POST /users/{userId}/assignLicense infrastructure error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<AccessToken> AcquireTokenAsync(string tenantId, CancellationToken cancellationToken)
    {
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });
        return await credential.GetTokenAsync(new TokenRequestContext(GraphScope), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string?> FindUserIdByUpnAsync(AccessToken token, string upn, CancellationToken cancellationToken)
    {
        var filter = Uri.EscapeDataString($"userPrincipalName eq '{upn}'");
        var uri = new Uri($"https://graph.microsoft.com/v1.0/users?$filter={filter}&$select=id&$top=1");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET /users?$filter=userPrincipalName failed: {(int)response.StatusCode} {response.StatusCode}. " +
                $"Body: {Truncate(text, 300)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        if (!doc.RootElement.TryGetProperty("value", out var values)
            || values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() == 0)
        {
            return null;
        }
        return values[0].TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
    }

    private string BuildUpn(string firstName, string lastName)
        => $"{SanitizeName(firstName)}.{SanitizeName(lastName)}@{_options.AccountDomain}";

    private static string SanitizeName(string name)
    {
        var sanitized = new string(name.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "user" : sanitized;
    }

    /// <summary>
    /// Generates a Graph-password-policy-compliant temporary password (16
    /// chars, mixed upper/lower/digit/symbol) for the forced-change-on-first-
    /// sign-in flow. Not a secrets-management concern — the value is
    /// immediately invalidated by the user's first sign-in and is never
    /// persisted to Cosmos (only passed through the POST /users request body).
    /// </summary>
    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%^&*";
        var pools = new[] { upper, lower, digits, symbols };

        Span<byte> buffer = stackalloc byte[16];
        RandomNumberGenerator.Fill(buffer);

        var chars = new char[16];
        for (var i = 0; i < chars.Length; i++)
        {
            var pool = pools[i % pools.Length];
            chars[i] = pool[buffer[i] % pool.Length];
        }
        return new string(chars);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
