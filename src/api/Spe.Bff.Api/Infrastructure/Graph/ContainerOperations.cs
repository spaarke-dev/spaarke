using System.Diagnostics;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Spe.Bff.Api.Models;

namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Handles SharePoint Embedded container operations.
/// Responsible for container creation, retrieval, and listing.
/// </summary>
public class ContainerOperations
{
    private readonly IGraphClientFactory _factory;
    private readonly ILogger<ContainerOperations> _logger;

    public ContainerOperations(IGraphClientFactory factory, ILogger<ContainerOperations> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ContainerDto?> CreateContainerAsync(
        Guid containerTypeId,
        string displayName,
        string? description = null,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "CreateContainer");
        activity?.SetTag("containerTypeId", containerTypeId.ToString());

        _logger.LogInformation("Creating SPE container {DisplayName} with type {ContainerTypeId}",
            displayName, containerTypeId);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();

            var container = new FileStorageContainer
            {
                DisplayName = displayName,
                Description = description,
                ContainerTypeId = containerTypeId
            };

            var createdContainer = await graphClient.Storage.FileStorage.Containers
                .PostAsync(container, cancellationToken: ct);

            if (createdContainer == null)
            {
                _logger.LogError("Failed to create container - Graph API returned null");
                return null;
            }

            _logger.LogInformation("Successfully created SPE container {ContainerId} with display name {DisplayName}",
                createdContainer.Id, displayName);

            return new ContainerDto(
                createdContainer.Id!,
                createdContainer.DisplayName!,
                createdContainer.Description,
                createdContainer.CreatedDateTime ?? DateTimeOffset.UtcNow);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error creating container: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to create SharePoint Embedded container: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating container: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<ContainerDto?> GetContainerDriveAsync(string containerId, CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "GetContainerDrive");
        activity?.SetTag("containerId", containerId);

        _logger.LogInformation("Getting drive for container {ContainerId}", containerId);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();

            var drive = await graphClient.Storage.FileStorage.Containers[containerId].Drive
                .GetAsync(cancellationToken: ct);

            if (drive == null)
            {
                _logger.LogWarning("Drive not found for container {ContainerId}", containerId);
                return null;
            }

            _logger.LogInformation("Successfully retrieved drive {DriveId} for container {ContainerId}",
                drive.Id, containerId);

            return new ContainerDto(
                drive.Id!,
                drive.Name ?? "Unknown",
                drive.Description,
                drive.CreatedDateTime ?? DateTimeOffset.UtcNow);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Container {ContainerId} not found", containerId);
            return null;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error getting container drive: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to get drive for container: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting container drive: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<IList<ContainerDto>?> ListContainersAsync(Guid containerTypeId, CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "ListContainers");
        activity?.SetTag("containerTypeId", containerTypeId.ToString());

        _logger.LogInformation("Listing containers for type {ContainerTypeId}", containerTypeId);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();

            // Get containers filtered by containerTypeId
            var response = await graphClient.Storage.FileStorage.Containers
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Filter = $"containerTypeId eq {containerTypeId}";
                }, cancellationToken: ct);

            if (response?.Value == null)
            {
                _logger.LogWarning("No containers found for type {ContainerTypeId}", containerTypeId);
                return new List<ContainerDto>();
            }

            var result = response.Value;
            _logger.LogInformation("Found {Count} containers for type {ContainerTypeId}",
                result.Count, containerTypeId);

            return result.Select(c => new ContainerDto(
                c.Id!,
                c.DisplayName!,
                c.Description,
                c.CreatedDateTime ?? DateTimeOffset.UtcNow)).ToList();
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error listing containers: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to list containers: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing containers: {Error}", ex.Message);
            throw;
        }
    }

    // =============================================================================
    // USER CONTEXT METHODS (OBO Flow)
    // =============================================================================

    /// <summary>
    /// Lists containers accessible to the user (OBO flow).
    /// </summary>
    /// <param name="userToken">User's bearer token for OBO flow</param>
    public async Task<IList<ContainerDto>> ListContainersAsUserAsync(
        string userToken,
        Guid containerTypeId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userToken))
        {
            throw new ArgumentException("User access token is required for OBO operations", nameof(userToken));
        }

        using var activity = Activity.Current;
        activity?.SetTag("operation", "ListContainersAsUser");
        activity?.SetTag("containerTypeId", containerTypeId.ToString());

        _logger.LogInformation("Listing containers for type {ContainerTypeId} (user context)", containerTypeId);

        try
        {
            // Create Graph client with user token (OBO flow)
            var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);

            // Query containers with user's permissions
            var containers = await graphClient.Storage.FileStorage.Containers
                .GetAsync(requestConfig =>
                {
                    requestConfig.QueryParameters.Filter = $"containerTypeId eq {containerTypeId}";
                }, ct);

            if (containers?.Value == null)
            {
                _logger.LogInformation("No containers found for containerTypeId {ContainerTypeId} (user context)", containerTypeId);
                return new List<ContainerDto>();
            }

            var result = containers.Value
                .Select(c => new ContainerDto(
                    c.Id ?? string.Empty,
                    c.DisplayName ?? string.Empty,
                    c.Description,
                    c.CreatedDateTime ?? DateTimeOffset.MinValue))
                .ToList();

            _logger.LogInformation("Listed {Count} containers for containerTypeId {ContainerTypeId} (user context)",
                result.Count, containerTypeId);

            return result;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered for user context, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Failed to list containers for user (containerTypeId: {ContainerTypeId})", containerTypeId);
            throw new InvalidOperationException($"Failed to list containers for user: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing containers for user: {Error}", ex.Message);
            throw;
        }
    }
}
