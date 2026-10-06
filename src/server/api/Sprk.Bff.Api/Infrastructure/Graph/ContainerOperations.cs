using System.Diagnostics;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Handles SharePoint Embedded container operations.
/// Responsible for container creation, retrieval, and listing.
/// </summary>
public class ContainerOperations
{
    private readonly IGraphClientFactory _factory;
    private readonly ILogger<ContainerOperations> _logger;
    private readonly GraphMetadataCache? _metadataCache;

    public ContainerOperations(
        IGraphClientFactory factory,
        ILogger<ContainerOperations> logger,
        GraphMetadataCache? metadataCache = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metadataCache = metadataCache; // Optional: cache can be null if not configured
    }

    /// <summary>
    /// Creates an SPE container and binds it to <paramref name="owningBusinessUnitId"/> before returning
    /// (<see cref="Sprk.Bff.Api.Services.SpeAdmin.SpeContainerBusinessUnitStamp"/>; unified-access-control-r2 task 165,
    /// owner round 20 item 1). A container whose stamp cannot be written and read back is soft-deleted again and the
    /// call throws — no container this path makes is left active and unbound.
    /// </summary>
    public async Task<ContainerDto?> CreateContainerAsync(
        Guid containerTypeId,
        string displayName,
        Guid owningBusinessUnitId,
        string? description = null,
        CancellationToken ct = default)
    {
        // Task 093: tags go on the caller's request Activity, as always. No `using` — this method did not
        // start the Activity, and disposing it here ended the request's own trace span early (see
        // UploadSessionManager.UploadSmallAsync for the full explanation; same fix, all 20 sites).
        var activity = Activity.Current;
        activity?.SetTag("operation", "CreateContainer");
        activity?.SetTag("containerTypeId", containerTypeId.ToString());

        if (owningBusinessUnitId == Guid.Empty)
        {
            throw new ArgumentException("A container is created only with its owning business unit.", nameof(owningBusinessUnitId));
        }

        _logger.LogInformation("Creating SPE container {DisplayName} with type {ContainerTypeId}",
            displayName, containerTypeId);

        try
        {
            var graphClient = _factory.ForApp();

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

            await BindNewContainerAsync(graphClient, createdContainer.Id!, owningBusinessUnitId, ct);

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

    /// <summary>
    /// Stamps the just-created container, reads the stamp back, and on any failure soft-deletes it and throws
    /// <see cref="InvalidOperationException"/> (the caller's existing "container could not be created" path).
    /// </summary>
    private async Task BindNewContainerAsync(
        GraphServiceClient graphClient, string containerId, Guid owningBusinessUnitId, CancellationToken ct)
    {
        try
        {
            await SpeAdminGraphService.WriteBusinessUnitStampAsync(graphClient, containerId, owningBusinessUnitId, ct);

            var readBack = await SpeAdminGraphService.ReadContainerBindingAsync(graphClient, containerId, deleted: false, ct);
            if (readBack?.Binding.BusinessUnitId != owningBusinessUnitId)
            {
                throw new InvalidOperationException("The business-unit stamp did not read back after the write.");
            }

            _logger.LogInformation("Container {ContainerId} bound to business unit {BusinessUnitId}",
                containerId, owningBusinessUnitId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Container {ContainerId} was created but could not be bound to business unit {BusinessUnitId} — removing it.",
                containerId, owningBusinessUnitId);

            try
            {
                await graphClient.Storage.FileStorage.Containers[containerId].DeleteAsync(cancellationToken: CancellationToken.None);
            }
            catch (Exception deleteEx)
            {
                _logger.LogCritical(deleteEx,
                    "Container {ContainerId} is UNBOUND and could not be removed; no admin route reaches it until the backfill " +
                    "binds it (-Bind) or an operator removes it.", containerId);
            }

            throw new InvalidOperationException(
                $"The SPE container '{containerId}' was created but could not be bound to its owning business unit, so it was removed.", ex);
        }
    }

    public async Task<ContainerDto?> GetContainerDriveAsync(string containerId, CancellationToken ct = default)
    {
        // Task 093: tags go on the caller's request Activity, as always. No `using` — this method did not
        // start the Activity, and disposing it here ended the request's own trace span early (see
        // UploadSessionManager.UploadSmallAsync for the full explanation; same fix, all 20 sites).
        var activity = Activity.Current;
        activity?.SetTag("operation", "GetContainerDrive");
        activity?.SetTag("containerId", containerId);

        // Cache-aside: check Redis first (ADR-009, 24h TTL for stable mappings)
        if (_metadataCache != null)
        {
            var cached = await _metadataCache.GetContainerDriveAsync(containerId);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached drive mapping for container {ContainerId}", containerId);
                activity?.SetTag("cache.result", "hit");
                return cached;
            }
            activity?.SetTag("cache.result", "miss");
        }

        _logger.LogInformation("Getting drive for container {ContainerId}", containerId);

        try
        {
            var graphClient = _factory.ForApp();

            var drive = await graphClient.Storage.FileStorage.Containers[containerId].Drive
                .GetAsync(cancellationToken: ct);

            if (drive == null)
            {
                _logger.LogWarning("Drive not found for container {ContainerId}", containerId);
                return null;
            }

            _logger.LogInformation("Successfully retrieved drive {DriveId} for container {ContainerId}",
                drive.Id, containerId);

            var containerDrive = new ContainerDto(
                drive.Id!,
                drive.Name ?? "Unknown",
                drive.Description,
                drive.CreatedDateTime ?? DateTimeOffset.UtcNow);

            // Cache the stable container-to-drive mapping (24h TTL)
            if (_metadataCache != null)
            {
                await _metadataCache.SetContainerDriveAsync(containerId, containerDrive);
            }

            return containerDrive;
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
        // Task 093: tags go on the caller's request Activity, as always. No `using` — this method did not
        // start the Activity, and disposing it here ended the request's own trace span early (see
        // UploadSessionManager.UploadSmallAsync for the full explanation; same fix, all 20 sites).
        var activity = Activity.Current;
        activity?.SetTag("operation", "ListContainers");
        activity?.SetTag("containerTypeId", containerTypeId.ToString());

        _logger.LogInformation("Listing containers for type {ContainerTypeId}", containerTypeId);

        try
        {
            var graphClient = _factory.ForApp();

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
    /// <param name="ctx">HttpContext containing user's bearer token</param>
    public async Task<IList<ContainerDto>> ListContainersAsUserAsync(
        HttpContext ctx,
        Guid containerTypeId,
        CancellationToken ct = default)
    {
        // Task 093: tags go on the caller's request Activity, as always. No `using` — this method did not
        // start the Activity, and disposing it here ended the request's own trace span early (see
        // UploadSessionManager.UploadSmallAsync for the full explanation; same fix, all 20 sites).
        var activity = Activity.Current;
        activity?.SetTag("operation", "ListContainersAsUser");
        activity?.SetTag("containerTypeId", containerTypeId.ToString());

        _logger.LogInformation("Listing containers for type {ContainerTypeId} (user context)", containerTypeId);

        try
        {
            // Create Graph client with user token (OBO flow)
            var graphClient = await _factory.ForUserAsync(ctx, ct);

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
