using Microsoft.Extensions.Logging;

namespace Spaarke.Dataverse;

public class DataverseAccessDataSource : IAccessDataSource
{
    private readonly ILogger<DataverseAccessDataSource> _logger;

    public DataverseAccessDataSource(ILogger<DataverseAccessDataSource> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching access data for user {UserId} on resource {ResourceId}", userId, resourceId);

        // Placeholder implementation - would query Dataverse in real implementation
        var snapshot = new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessLevel = AccessLevel.None,
            TeamMemberships = Array.Empty<string>(),
            Roles = Array.Empty<string>()
        };

        _logger.LogDebug("Access snapshot retrieved: {AccessLevel} for user {UserId}", snapshot.AccessLevel, userId);

        return Task.FromResult(snapshot);
    }
}