using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Represents the resolved context for an authenticated external caller (Power Pages Contact).
/// Set on HttpContext.Items by ExternalCallerAuthorizationFilter and consumed by downstream handlers.
/// </summary>
public sealed class ExternalCallerContext
{
    public static readonly object HttpContextItemsKey = new();

    /// <summary>
    /// The Dataverse Contact ID for the authenticated external user.
    /// </summary>
    public required Guid ContactId { get; init; }

    /// <summary>
    /// The external user's email / UPN (from portal token claims).
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// List of active project participations for this Contact.
    /// </summary>
    public required IReadOnlyList<ExternalParticipation> Participations { get; init; }

    /// <summary>
    /// Whether this context was loaded from Redis cache.
    /// </summary>
    public bool FromCache { get; init; }

    /// <summary>
    /// Checks if the Contact has access to the specified project.
    /// </summary>
    public bool HasProjectAccess(Guid projectId) =>
        Participations.Any(p => p.ProjectId == projectId);

    /// <summary>
    /// Gets the access level for the specified project, or null if no access.
    /// </summary>
    public ExternalAccessLevel? GetAccessLevel(Guid projectId) =>
        Participations.FirstOrDefault(p => p.ProjectId == projectId)?.AccessLevel;

    /// <summary>
    /// Gets the effective AccessRights for the specified project based on access level.
    /// </summary>
    public AccessRights GetEffectiveRights(Guid projectId)
    {
        var level = GetAccessLevel(projectId);
        return level switch
        {
            ExternalAccessLevel.ViewOnly => AccessRights.Read,
            ExternalAccessLevel.Collaborate => AccessRights.Read | AccessRights.Create | AccessRights.Write,
            ExternalAccessLevel.FullAccess => AccessRights.Read | AccessRights.Create | AccessRights.Write | AccessRights.Delete,
            _ => AccessRights.None
        };
    }

    /// <summary>
    /// Gets all project IDs the Contact can access (for AI search filter construction).
    /// </summary>
    public IEnumerable<Guid> GetAccessibleProjectIds() =>
        Participations.Select(p => p.ProjectId);
}

/// <summary>
/// A single external access grant for a Contact → Project relationship.
/// </summary>
public sealed class ExternalParticipation
{
    public required Guid ProjectId { get; init; }
    public required ExternalAccessLevel AccessLevel { get; init; }
}

/// <summary>
/// Access level values for external participation (matches sprk_accesslevel choice field).
/// </summary>
public enum ExternalAccessLevel
{
    ViewOnly = 100000000,
    Collaborate = 100000001,
    FullAccess = 100000002
}
