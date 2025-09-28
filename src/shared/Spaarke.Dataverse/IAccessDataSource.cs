namespace Spaarke.Dataverse;

public interface IAccessDataSource
{
    Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default);
}

public class AccessSnapshot
{
    public required string UserId { get; init; }
    public required string ResourceId { get; init; }
    public AccessLevel AccessLevel { get; init; }
    public IEnumerable<string> TeamMemberships { get; init; } = Array.Empty<string>();
    public IEnumerable<string> Roles { get; init; } = Array.Empty<string>();
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum AccessLevel
{
    None = 0,
    Deny = 1,
    Grant = 2
}