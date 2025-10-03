using Microsoft.Graph;
using Microsoft.Graph.Models;
using Spe.Bff.Api.Models;

namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// User-specific Graph operations (user info, capabilities).
/// All operations use OBO (On-Behalf-Of) flow.
/// </summary>
public class UserOperations
{
    private readonly IGraphClientFactory _factory;
    private readonly ILogger<UserOperations> _logger;

    public UserOperations(IGraphClientFactory factory, ILogger<UserOperations> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets current user information via Microsoft Graph /me endpoint.
    /// </summary>
    public async Task<UserInfoResponse?> GetUserInfoAsync(
        string userToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userToken))
            throw new ArgumentException("User access token required", nameof(userToken));

        try
        {
            var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);
            var user = await graphClient.Me.GetAsync(cancellationToken: ct);

            if (user == null || string.IsNullOrEmpty(user.Id))
                return null;

            return new UserInfoResponse(
                DisplayName: user.DisplayName ?? "Unknown User",
                UserPrincipalName: user.UserPrincipalName ?? "unknown@domain.com",
                Oid: user.Id
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user info");
            return null;
        }
    }

    /// <summary>
    /// Gets user capabilities for a specific container.
    /// Returns what operations the user can perform based on their permissions.
    /// Note: Simplified implementation - checks drive access only.
    /// </summary>
    public async Task<UserCapabilitiesResponse> GetUserCapabilitiesAsync(
        string userToken,
        string containerId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userToken))
            throw new ArgumentException("User access token required", nameof(userToken));

        try
        {
            var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);

            // Try to access the container to determine capabilities
            var hasAccess = false;
            try
            {
                var drive = await graphClient.Storage.FileStorage.Containers[containerId].Drive.GetAsync(cancellationToken: ct);
                hasAccess = drive?.Id != null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "User denied access to container {ContainerId}", containerId);
                hasAccess = false;
            }

            var capabilities = new UserCapabilitiesResponse(
                Read: hasAccess,
                Write: hasAccess,
                Delete: hasAccess,
                CreateFolder: hasAccess
            );

            _logger.LogInformation("Retrieved capabilities for container {ContainerId}: {Capabilities}",
                containerId, capabilities);

            return capabilities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to determine capabilities for container {ContainerId}", containerId);
            return new UserCapabilitiesResponse(false, false, false, false);
        }
    }
}
