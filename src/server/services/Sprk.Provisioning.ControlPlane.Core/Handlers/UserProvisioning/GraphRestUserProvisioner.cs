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

    private readonly Func<string, TokenCredential> _credentialFactory;

    /// <summary>Production constructor (typed HttpClient registration in Worker/Program.cs).</summary>
    public GraphRestUserProvisioner(
        HttpClient httpClient,
        IOptions<H11UserProvisioningOptions> options,
        ILogger<GraphRestUserProvisioner> logger)
        : this(httpClient, options, logger,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>Test seam constructor — injects the credential factory so tests never touch the real chain.</summary>
    internal GraphRestUserProvisioner(
        HttpClient httpClient,
        IOptions<H11UserProvisioningOptions> options,
        ILogger<GraphRestUserProvisioner> logger,
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
                    "H11 user already exists — idempotent no-op (userId={UserId})", existingId);   // D15: no UPN in logs
                return new UserCreationOutcome.Success(existingId, upn);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new UserCreationOutcome.Failure($"UPN availability check failed: {ex.Message}");
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
                    $"POST /users failed: {(int)response.StatusCode} {response.StatusCode}. " +
                    $"Body: {Truncate(body, 300)}");
            }

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var userId = doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new UserCreationOutcome.Failure("Graph returned no 'id' for the created user.");
            }

            _logger.LogInformation("H11 created Entra user {UserId}", userId);   // D15: no UPN in logs
            return new UserCreationOutcome.Success(userId, upn);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new UserCreationOutcome.Failure(
                $"POST /users infrastructure error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<LicenseAssignmentOutcome> AssignLicenseAsync(
        string userId, string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        // Task 232: no SKU is a failure, never a silent skip (R7 "validated but not wired" — an unlicensed user cannot
        // open the environment). H11 refuses such a run before creating anyone; this guards any other caller.
        var skuIds = _options.LicenseSkuIds;
        if (skuIds.Count == 0)
        {
            return new LicenseAssignmentOutcome.Failure(
                "No licence SKU is configured (H11UserProvisioningOptions: PowerAppsPlan2TrialSkuId / FabricFreeSkuId / " +
                "PowerAutomateFreeSkuId).");
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
            // T186 audit: assignLicense on a user created moments ago 404s (or 400s "does not exist" / usage location)
            // until Entra replicates it. Wait that out; a genuine licence error (no available licences, bad SKU) is not
            // retried and fails as before.
            var reply = await PostAssignLicenseAsync(
                token, userId, payload, "assignLicense on the new user", cancellationToken).ConfigureAwait(false);
            if (!reply.IsSuccess)
            {
                return new LicenseAssignmentOutcome.Failure(
                    $"POST /users/{userId}/assignLicense failed: {(int)reply.Status} {reply.Status}. " +
                    $"Body: {Truncate(reply.Body, 300)}");
            }

            return new LicenseAssignmentOutcome.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new LicenseAssignmentOutcome.Failure(
                $"POST /users/{userId}/assignLicense infrastructure error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<LicenseAssignmentOutcome> AssignGuestLicenseAsync(
        string userId, string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        // Owner 2026-10-16 (T186 L51): Dataverse will not add an unlicensed guest. H11 refuses a B2BGuest run before
        // inviting anyone when this is blank; this guards any other caller.
        var skuId = _options.GuestLicenseSkuId?.Trim();
        if (string.IsNullOrEmpty(skuId))
        {
            return new LicenseAssignmentOutcome.Failure(
                "No guest licence SKU is configured (H11UserProvisioningOptions:GuestLicenseSkuId).");
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

        try
        {
            // The guest was invited and added to the group moments ago: the read waits out Entra replication.
            var userUrl = $"https://graph.microsoft.com/v1.0/users/{userId}";
            var read = await H11ReplicationRetry.RunAsync(
                async ct =>
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get, $"{userUrl}?$select=usageLocation,assignedLicenses");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                    using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                    var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return new H11ReplicationRetry.Reply(response.StatusCode, text);
                },
                H11ReplicationRetry.IsGraphObjectNotYetReplicated,
                _options.ReplicationRetryCount, _options.ReplicationRetryDelay, _logger, "read of the guest's licences",
                cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess)
            {
                return new LicenseAssignmentOutcome.Failure(
                    $"GET /users/{userId} failed: {(int)read.Status} {read.Status}. Body: {Truncate(read.Body, 300)}");
            }

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(read.Body) ? "{}" : read.Body);
            if (doc.RootElement.TryGetProperty("assignedLicenses", out var assigned)
                && assigned.ValueKind == JsonValueKind.Array
                && assigned.EnumerateArray().Any(l =>
                    l.TryGetProperty("skuId", out var id)
                    && id.ValueKind == JsonValueKind.String
                    && string.Equals(id.GetString(), skuId, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogInformation(
                    "H11 guest already holds the guest licence — idempotent no-op (userId={UserId})", userId);
                return new LicenseAssignmentOutcome.Success();
            }

            var hasUsageLocation = doc.RootElement.TryGetProperty("usageLocation", out var location)
                                   && location.ValueKind == JsonValueKind.String
                                   && !string.IsNullOrWhiteSpace(location.GetString());
            if (!hasUsageLocation)
            {
                using var patch = new HttpRequestMessage(HttpMethod.Patch, userUrl)
                {
                    Content = JsonContent.Create(
                        new Dictionary<string, object?> { ["usageLocation"] = _options.GuestUsageLocation }),
                };
                patch.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                using var patchResponse = await _httpClient.SendAsync(patch, cancellationToken).ConfigureAwait(false);
                if (!patchResponse.IsSuccessStatusCode)
                {
                    var patchBody = await patchResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return new LicenseAssignmentOutcome.Failure(
                        $"PATCH /users/{userId} (usageLocation) failed: {(int)patchResponse.StatusCode} " +
                        $"{patchResponse.StatusCode}. Body: {Truncate(patchBody, 300)}");
                }
            }

            var payload = new Dictionary<string, object?>
            {
                ["addLicenses"] = new[] { new Dictionary<string, object?> { ["skuId"] = skuId } },
                ["removeLicenses"] = Array.Empty<string>(),
            };
            var reply = await PostAssignLicenseAsync(
                token, userId, payload, "assignLicense on the guest", cancellationToken).ConfigureAwait(false);
            if (!reply.IsSuccess)
            {
                return new LicenseAssignmentOutcome.Failure(
                    $"POST /users/{userId}/assignLicense failed: {(int)reply.Status} {reply.Status}. " +
                    $"Body: {Truncate(reply.Body, 300)}");
            }

            _logger.LogInformation("H11 assigned the guest licence (userId={UserId})", userId);   // D15: no UPN in logs
            return new LicenseAssignmentOutcome.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new LicenseAssignmentOutcome.Failure(
                $"POST /users/{userId}/assignLicense infrastructure error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// <c>POST /users/{id}/assignLicense</c> with the T186-audit replication retry: a user created or invited moments
    /// ago 404s (or 400s "does not exist" / usage location) until Entra replicates it. A genuine licence error (no
    /// available licences, bad SKU) is not retried.
    /// </summary>
    private Task<H11ReplicationRetry.Reply> PostAssignLicenseAsync(
        AccessToken token, string userId, Dictionary<string, object?> payload, string operation,
        CancellationToken cancellationToken)
        => H11ReplicationRetry.RunAsync(
            async ct =>
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post, $"https://graph.microsoft.com/v1.0/users/{userId}/assignLicense")
                {
                    Content = JsonContent.Create(payload),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return new H11ReplicationRetry.Reply(response.StatusCode, text);
            },
            H11ReplicationRetry.IsLicenseTargetNotYetReplicated,
            _options.ReplicationRetryCount, _options.ReplicationRetryDelay, _logger, operation,
            cancellationToken);

    private async Task<AccessToken> AcquireTokenAsync(string tenantId, CancellationToken cancellationToken)
    {
        var credential = _credentialFactory(tenantId);
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
