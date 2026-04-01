using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.PowerBI.Api;
using Microsoft.PowerBI.Api.Models;
using Microsoft.Rest;

namespace Sprk.Bff.Api.Api.Reporting;

/// <summary>
/// Manages Power BI service principal profiles for multi-tenant workspace isolation.
///
/// Each customer receives a dedicated SP profile so that workspace-level permissions are scoped
/// per tenant without requiring separate Entra ID app registrations. The profile ID is passed as
/// the <c>X-PowerBI-Profile-Id</c> header on subsequent Power BI API calls (see
/// <see cref="ReportingEmbedService.GetPowerBIClientAsync"/>).
///
/// Profile display name convention: <c>sprk-{customerId}</c> (e.g. "sprk-contoso-legal").
///
/// Follows ADR-001 (Minimal API — registered as a concrete singleton),
/// ADR-010 (counts as 1 of ≤2 DI registrations for the Reporting module — the other being
/// <see cref="ReportingEmbedService"/>).
///
/// Thread-safety: all public methods are safe to call concurrently.
/// A <see cref="ConcurrentDictionary{TKey,TValue}"/> provides in-process caching to avoid
/// redundant list calls within a single host lifetime; profiles are created once and never deleted
/// by the application during normal operation.
/// </summary>
public sealed class ReportingProfileManager
{
    /// <summary>
    /// Power BI scope for client-credentials (App Owns Data).
    /// Matches <see cref="ReportingEmbedService"/>'s scope constant.
    /// </summary>
    private static readonly string[] PowerBiScopes = ["https://analysis.windows.net/.default"];

    /// <summary>
    /// Base retry delay for Power BI 429 (Too Many Requests) responses.
    /// Each subsequent attempt doubles the delay (exponential backoff).
    /// </summary>
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(2);

    /// <summary>Maximum number of retry attempts on rate-limit responses before giving up.</summary>
    private const int RetryMaxAttempts = 4;

    private readonly PowerBiOptions _options;
    private readonly ILogger<ReportingProfileManager> _logger;
    private readonly IConfidentialClientApplication _cca;

    /// <summary>
    /// In-memory profile cache keyed by customerId.
    /// Profiles are immutable once created in Power BI, so no TTL is needed.
    /// A process restart repopulates the cache on the next GetOrCreateProfileAsync call.
    /// </summary>
    private readonly ConcurrentDictionary<string, ServicePrincipalProfileInfo> _profileCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lock used to serialise the list-then-create operation so that concurrent callers
    /// for the same <c>customerId</c> do not race to create duplicate profiles.
    /// </summary>
    private readonly SemaphoreSlim _createLock = new(1, 1);

    public ReportingProfileManager(
        IOptions<PowerBiOptions> options,
        ILogger<ReportingProfileManager> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Build MSAL ConfidentialClientApplication once; MSAL manages token cache lifetime.
        // Identical setup to ReportingEmbedService so both services share the same token cache
        // behaviour (MSAL caches SP tokens in-process for ~60 minutes).
        _cca = ConfidentialClientApplicationBuilder
            .Create(_options.ClientId)
            .WithAuthority(_options.GetEffectiveAuthorityUrl())
            .WithClientSecret(_options.ClientSecret)
            .Build();
    }

    // -----------------------------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the service principal profile for <paramref name="customerId"/>, creating it
    /// in Power BI if it does not already exist. The operation is idempotent — if a profile
    /// with the display name <c>sprk-{customerId}</c> already exists in Power BI (from a
    /// previous deployment or host restart) it is returned without creating a duplicate.
    /// </summary>
    /// <param name="customerId">
    ///   Tenant/customer identifier used to derive the profile display name.
    ///   Typically the Dataverse environment unique name or organisation ID.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="ServicePrincipalProfileInfo"/> containing the profile ID and display name.</returns>
    public async Task<ServicePrincipalProfileInfo> GetOrCreateProfileAsync(
        string customerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        // Fast path: already in process cache.
        if (_profileCache.TryGetValue(customerId, out var cached))
        {
            _logger.LogDebug(
                "Profile cache hit for customer {CustomerId} → profile {ProfileId}",
                customerId, cached.Id);
            return cached;
        }

        // Slow path: serialise so concurrent callers for the same customer don't race.
        await _createLock.WaitAsync(ct);
        try
        {
            // Re-check after acquiring lock in case another thread just populated it.
            if (_profileCache.TryGetValue(customerId, out cached))
                return cached;

            var displayName = BuildDisplayName(customerId);

            _logger.LogInformation(
                "Profile cache miss for customer {CustomerId}; searching Power BI for profile '{DisplayName}'",
                customerId, displayName);

            var client = await GetPowerBIClientAsync(ct);

            // Check whether the profile already exists in Power BI (idempotency).
            var existing = await FindProfileByDisplayNameAsync(client, displayName, ct);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Found existing Power BI profile '{DisplayName}' ({ProfileId}) for customer {CustomerId}",
                    displayName, existing.Id, customerId);

                _profileCache[customerId] = existing;
                return existing;
            }

            // Profile not found — create it.
            var created = await CreateProfileWithRetryAsync(client, displayName, ct);

            _logger.LogInformation(
                "Created Power BI profile '{DisplayName}' ({ProfileId}) for customer {CustomerId}",
                displayName, created.Id, customerId);

            _profileCache[customerId] = created;
            return created;
        }
        finally
        {
            _createLock.Release();
        }
    }

    /// <summary>
    /// Returns the Power BI profile ID for <paramref name="customerId"/>.
    /// This ID is used as the value of the <c>X-PowerBI-Profile-Id</c> request header on
    /// subsequent Power BI API calls to scope them to the customer's workspace.
    /// </summary>
    /// <param name="customerId">Customer/tenant identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The profile <see cref="Guid"/>.</returns>
    public async Task<Guid> GetProfileIdAsync(string customerId, CancellationToken ct = default)
    {
        var profile = await GetOrCreateProfileAsync(customerId, ct);
        return profile.Id;
    }

    /// <summary>
    /// Lists all service principal profiles registered under the current service principal.
    /// Useful for auditing, debugging, and administrative operations.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Snapshot of all profiles; never null, may be empty.</returns>
    public async Task<IReadOnlyList<ServicePrincipalProfileInfo>> ListProfilesAsync(
        CancellationToken ct = default)
    {
        _logger.LogDebug("Listing all Power BI service principal profiles");

        var client = await GetPowerBIClientAsync(ct);
        var profiles = await FetchAllProfilesAsync(client, ct);

        _logger.LogInformation("Listed {Count} Power BI service principal profiles", profiles.Count);
        return profiles;
    }

    /// <summary>
    /// Deletes a service principal profile from Power BI.
    /// Also evicts any matching entry from the in-process cache.
    /// </summary>
    /// <param name="profileId">Profile GUID to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DeleteProfileAsync(Guid profileId, CancellationToken ct = default)
    {
        _logger.LogInformation("Deleting Power BI service principal profile {ProfileId}", profileId);

        var client = await GetPowerBIClientAsync(ct);

        try
        {
            await client.Profiles.DeleteProfileAsync(profileId, ct);
        }
        catch (HttpOperationException ex) when ((int?)ex.Response?.StatusCode == 404)
        {
            // Already gone — treat as success to keep the operation idempotent.
            _logger.LogWarning(
                "Attempted to delete profile {ProfileId} but it was not found (already deleted)",
                profileId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete Power BI profile {ProfileId}", profileId);
            throw;
        }

        // Evict from in-process cache so subsequent calls repopulate.
        foreach (var kvp in _profileCache)
        {
            if (kvp.Value.Id == profileId)
            {
                _profileCache.TryRemove(kvp.Key, out _);
                _logger.LogDebug(
                    "Evicted cached profile for customer {CustomerId} after deletion", kvp.Key);
                break;
            }
        }

        _logger.LogInformation("Deleted Power BI service principal profile {ProfileId}", profileId);
    }

    // -----------------------------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Acquires a service principal access token via MSAL and returns a
    /// <see cref="PowerBIClient"/> configured with the base API URL.
    /// MSAL handles in-process token caching; repeated calls reuse the cached token.
    /// </summary>
    private async Task<PowerBIClient> GetPowerBIClientAsync(CancellationToken ct)
    {
        AuthenticationResult result;
        try
        {
            result = await _cca
                .AcquireTokenForClient(PowerBiScopes)
                .ExecuteAsync(ct);
        }
        catch (MsalServiceException ex)
        {
            _logger.LogError(ex,
                "MSAL service error acquiring Power BI token: {ErrorCode}", ex.ErrorCode);
            throw;
        }
        catch (MsalClientException ex)
        {
            _logger.LogError(ex,
                "MSAL client error acquiring Power BI token: {ErrorCode}", ex.ErrorCode);
            throw;
        }

        var credentials = new TokenCredentials(result.AccessToken, "Bearer");
        return new PowerBIClient(new Uri(_options.ApiUrl), credentials);
    }

    /// <summary>
    /// Fetches all profiles from the Power BI REST API and maps them to
    /// <see cref="ServicePrincipalProfileInfo"/> DTOs.
    /// </summary>
    private async Task<IReadOnlyList<ServicePrincipalProfileInfo>> FetchAllProfilesAsync(
        PowerBIClient client,
        CancellationToken ct)
    {
        ServicePrincipalProfiles profiles;
        try
        {
            profiles = await client.Profiles.GetProfilesAsync(cancellationToken: ct);
        }
        catch (HttpOperationException ex) when ((int?)ex.Response?.StatusCode == 429)
        {
            // Shouldn't normally happen for a list call, but handle defensively.
            _logger.LogWarning("Rate limited on GET /profiles; retrying after base delay");
            await Task.Delay(RetryBaseDelay, ct);
            profiles = await client.Profiles.GetProfilesAsync(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Power BI service principal profiles");
            throw;
        }

        return (profiles.Value ?? [])
            .Select(p => new ServicePrincipalProfileInfo(p.Id, p.DisplayName ?? string.Empty))
            .ToList();
    }

    /// <summary>
    /// Searches for an existing profile with <paramref name="displayName"/> among all profiles
    /// returned by the Power BI REST API.
    /// </summary>
    /// <returns>The matching profile, or <c>null</c> if not found.</returns>
    private async Task<ServicePrincipalProfileInfo?> FindProfileByDisplayNameAsync(
        PowerBIClient client,
        string displayName,
        CancellationToken ct)
    {
        var all = await FetchAllProfilesAsync(client, ct);
        return all.FirstOrDefault(p =>
            string.Equals(p.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a new service principal profile via POST /v1.0/myorg/profiles.
    /// Retries with exponential backoff on 429 (Too Many Requests) responses.
    /// </summary>
    /// <param name="client">Authenticated <see cref="PowerBIClient"/>.</param>
    /// <param name="displayName">Display name for the new profile.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="ServicePrincipalProfileInfo"/>.</returns>
    private async Task<ServicePrincipalProfileInfo> CreateProfileWithRetryAsync(
        PowerBIClient client,
        string displayName,
        CancellationToken ct)
    {
        var delay = RetryBaseDelay;

        for (var attempt = 1; attempt <= RetryMaxAttempts; attempt++)
        {
            try
            {
                _logger.LogDebug(
                    "Creating Power BI profile '{DisplayName}' (attempt {Attempt}/{Max})",
                    displayName, attempt, RetryMaxAttempts);

                var request = new CreateOrUpdateProfileRequest(displayName: displayName);
                var created = await client.Profiles.CreateProfileAsync(request, ct);

                return new ServicePrincipalProfileInfo(created.Id, created.DisplayName ?? displayName);
            }
            catch (HttpOperationException ex) when ((int?)ex.Response?.StatusCode == 429)
            {
                if (attempt == RetryMaxAttempts)
                {
                    _logger.LogError(
                        "Power BI rate limit (429) persisted after {Max} attempts creating profile '{DisplayName}'",
                        RetryMaxAttempts, displayName);
                    throw;
                }

                _logger.LogWarning(
                    "Power BI rate limit (429) on attempt {Attempt}/{Max} creating profile '{DisplayName}'. " +
                    "Retrying in {Delay}s.",
                    attempt, RetryMaxAttempts, displayName, delay.TotalSeconds);

                await Task.Delay(delay, ct);
                delay = TimeSpan.FromTicks(delay.Ticks * 2); // exponential backoff
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to create Power BI profile '{DisplayName}' on attempt {Attempt}",
                    displayName, attempt);
                throw;
            }
        }

        // Unreachable — the loop either returns or throws on the final attempt.
        throw new InvalidOperationException(
            $"Exhausted {RetryMaxAttempts} attempts creating Power BI profile '{displayName}'.");
    }

    /// <summary>
    /// Builds the Power BI profile display name for a given customer ID.
    /// Convention: <c>sprk-{customerId}</c> (e.g. "sprk-contoso-legal").
    /// </summary>
    private static string BuildDisplayName(string customerId) => $"sprk-{customerId}";
}

/// <summary>
/// Lightweight DTO representing a Power BI service principal profile.
/// Shields callers from <c>Microsoft.PowerBI.Api</c> SDK types (ADR-007).
/// </summary>
/// <param name="Id">The Power BI profile GUID. Pass as <c>X-PowerBI-Profile-Id</c> on API calls.</param>
/// <param name="DisplayName">Human-readable display name; follows convention <c>sprk-{customerId}</c>.</param>
public record ServicePrincipalProfileInfo(Guid Id, string DisplayName);
