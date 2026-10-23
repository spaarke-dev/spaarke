using System.Diagnostics;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Sprk.Bff.Api.Infrastructure.Http;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Handles SharePoint Embedded container operations.
/// Responsible for container creation and retrieval.
/// </summary>
/// <remarks>
/// App-only work goes through <see cref="SpeContainerOwnershipGuard"/> (task 227d): a container this
/// stamp did not create or configure is refused before Graph is called, and a container it creates is
/// marked as its own.
/// </remarks>
public class ContainerOperations
{
    private readonly IGraphClientFactory _factory;
    private readonly SpeContainerOwnershipGuard _ownership;
    private readonly ILogger<ContainerOperations> _logger;
    private readonly GraphMetadataCache? _metadataCache;

    public ContainerOperations(
        IGraphClientFactory factory,
        SpeContainerOwnershipGuard ownership,
        ILogger<ContainerOperations> logger,
        GraphMetadataCache? metadataCache = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metadataCache = metadataCache; // Optional: cache can be null if not configured
    }

    /// <summary>
    /// The token the create POST (#1735) and the bind of the container it made (#1652 round 1) run on, instead of the
    /// caller's: a client disconnect mid-POST could leave a container whose id was never read, and one mid-bind used to
    /// skip the compensating delete and leave a live, unbound container. The POST itself is bounded sooner, by the Graph
    /// handler's pessimistic timeout (<c>GraphResilience:TimeoutSeconds</c>, 30 s across all its attempts); this bounds
    /// the bind and anything the handler does not. Settable for tests only.
    /// </summary>
    internal TimeSpan UncancellableStepTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Creates an SPE container and binds it to <paramref name="owningBusinessUnitId"/> before returning
    /// (<see cref="Sprk.Bff.Api.Services.SpeAdmin.SpeContainerBusinessUnitStamp"/>; unified-access-control-r2 task 165,
    /// owner round 20 item 1). A container whose stamp cannot be written and read back is soft-deleted again and the
    /// call throws — no container this path makes is left active and unbound.
    /// </summary>
    /// <remarks>
    /// Never null: a create that does not come back with a container throws. When Graph may have created one anyway
    /// (no answer, a server error, or a 2xx without a container) it throws <see cref="ContainerCreateOutcomeUnknownException"/>,
    /// which this method does not log — its caller logs it once, with what finds the possible container (#1755).
    /// </remarks>
    public async Task<ContainerDto> CreateContainerAsync(
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

        // Resolve the marker value BEFORE creating, so an unresolved identity cannot leave an unmarked container.
        _ownership.RequireCustomerId();

        // The caller's last chance to stop the create (#1735/#1755): once the POST is sent, Graph may commit a container
        // whose id only its answer carries, so a cancel during the POST would leave one nothing can find or compensate.
        // From the send on, the POST runs on its own token, not the caller's. It is bounded by the Graph handler's
        // pessimistic timeout (30 s across all attempts); UncancellableStepTimeout bounds the activate and bind below.
        ct.ThrowIfCancellationRequested();

        try
        {
            var graphClient = _ownership.ForTypeWideOperation();

            var container = new FileStorageContainer
            {
                DisplayName = displayName,
                Description = description,
                ContainerTypeId = containerTypeId
            };

            FileStorageContainer? createdContainer;
            using (var send = new CancellationTokenSource(UncancellableStepTimeout))
            {
                // NoTransientRetryOption: the create is not idempotent, so the Graph handler does not re-send it after a
                // 5xx, 408 or transport fault (Graph may have committed the first) — only after a 429 (#1755). The handler
                // records on it whether an attempt reached Graph and what it answered, which is how a failure is classified.
                var createOption = new NoTransientRetryOption();
                try
                {
                    createdContainer = await graphClient.Storage.FileStorage.Containers
                        .PostAsync(container, rc => rc.Options.Add(createOption), send.Token);
                }
                catch (Exception ex) when (OutcomeIsUnknown(createOption, ex))
                {
                    throw new ContainerCreateOutcomeUnknownException(displayName, ex);
                }
            }

            // A 2xx is Graph committing the create. One that names no container leaves a container nothing names (#1755
            // round 2): not "nothing was created".
            if (string.IsNullOrEmpty(createdContainer?.Id))
            {
                throw new ContainerCreateOutcomeUnknownException(displayName,
                    new InvalidOperationException(createdContainer is null
                        ? "Graph answered the create with success but no container."
                        : "Graph answered the create with a container that has no id."));
            }

            _logger.LogInformation("Successfully created SPE container {ContainerId} with display name {DisplayName}",
                createdContainer.Id, displayName);

            // From here the container exists, so neither step runs on the caller's token (#1652 round 1): a cancelled
            // bind or mark would skip its compensating delete and leave a live container no route reaches.
            await BindNewContainerAsync(graphClient, createdContainer.Id!, owningBusinessUnitId);

            // Without the marker this stamp could not reach its own new container (task 227d). The guard skips its
            // compensation when ITS token is cancelled, so it gets none; Graph's request timeout still bounds it.
            await _ownership.MarkOwnedAsync(createdContainer.Id!, CancellationToken.None);

            return new ContainerDto(
                createdContainer.Id!,
                createdContainer.DisplayName!,
                createdContainer.Description,
                createdContainer.CreatedDateTime ?? DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is ContainerCreateOutcomeUnknownException or ContainerLeftActiveException)
        {
            // Not logged again here: the caller that reports either one logs it at Error (#1755 rounds 2 and 3), and for a
            // container left active BindNewContainerAsync has already logged the failed bind and the failed removal.
            throw;
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
    /// True when a failed create POST leaves unknown whether Graph created the container (#1755): an attempt reached the
    /// wire and its answer was not a settled refusal — no answer at all (a timeout, a transport fault), or one Graph may
    /// send after committing (a 2xx, a 5xx, a 408). The Graph handler records this on <paramref name="option"/>.
    /// A failure before anything was sent (an open circuit, a token that could not be acquired, a timeout during a 429's
    /// backoff), or a last answer that is a refusal (a 4xx other than 408, or a 429), means nothing was created.
    /// </summary>
    /// <remarks>
    /// An <see cref="ApiException"/> carrying a 5xx or 408 is unknown even when no handler recorded it (a client built
    /// without the Graph handler): an answer proves the request was sent. Reading the option closes it, so a retry
    /// not yet sent cannot overtake the classification.
    /// </remarks>
    private static bool OutcomeIsUnknown(NoTransientRetryOption option, Exception ex) =>
        option.CloseAndReadOutcomeUnknown() || ex is ApiException { ResponseStatusCode: >= 500 or 408 };

    /// <summary>
    /// Activates the just-created container, stamps it, reads the stamp back, and on any failure soft-deletes it and throws
    /// <see cref="InvalidOperationException"/> (the caller's existing "container could not be created" path) — or, when
    /// that delete fails too, <see cref="ContainerLeftActiveException"/> naming the container left active (#1755 round 3).
    /// Runs on its own token bounded by <see cref="UncancellableStepTimeout"/>, and compensates for EVERY failure,
    /// cancellation and timeout included (#1652 round 1).
    /// </summary>
    /// <remarks>
    /// Graph creates a container <c>inactive</c> and deletes it on its own if it is not activated within 24 hours. A
    /// modifying write such as the stamp PATCH may activate it implicitly, but nothing has measured that it does, so the
    /// container is activated explicitly (#1755 round 3), as the control plane's <c>GraphContainerProvisioner</c> does.
    /// Activate is idempotent, so it keeps the Graph handler's transient retry (no <c>NoTransientRetryOption</c>).
    /// </remarks>
    private async Task BindNewContainerAsync(
        GraphServiceClient graphClient, string containerId, Guid owningBusinessUnitId)
    {
        using var step = new CancellationTokenSource(UncancellableStepTimeout);
        var ct = step.Token;
        try
        {
            await graphClient.Storage.FileStorage.Containers[containerId].Activate.PostAsync(cancellationToken: ct);

            await SpeAdminGraphService.WriteBusinessUnitStampAsync(graphClient, containerId, owningBusinessUnitId, ct);

            var readBack = await SpeAdminGraphService.ReadContainerBindingAsync(graphClient, containerId, deleted: false, ct);
            if (readBack?.Binding.BusinessUnitId != owningBusinessUnitId)
            {
                throw new InvalidOperationException("The business-unit stamp did not read back after the write.");
            }

            _logger.LogInformation("Container {ContainerId} activated and bound to business unit {BusinessUnitId}",
                containerId, owningBusinessUnitId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Container {ContainerId} was created but could not be activated and bound to business unit {BusinessUnitId} — removing it.",
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

                // #1755 round 3: not the "so it was removed" failure below — a live container exists that nothing names,
                // and the caller must say so with its id instead of "nothing needs undoing".
                throw new ContainerLeftActiveException(containerId,
                    $"The SPE container '{containerId}' was created but could not be activated and bound to its owning " +
                    "business unit, and removing it failed: it is left in place, unbound.", ex);
            }

            throw new InvalidOperationException(
                $"The SPE container '{containerId}' was created but could not be activated and bound to its owning business " +
                "unit, so it was removed.", ex);
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

        // Ownership first, outside the try and before the cache: a cached mapping must not let a foreign
        // container through, and a refusal must surface as the guard's 404, not as a null "not found" (task 227d).
        var graphClient = await _ownership.ForOwnedContainerAsync(containerId, ct);

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
