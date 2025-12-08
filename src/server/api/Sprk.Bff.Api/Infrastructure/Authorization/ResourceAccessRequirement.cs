using Microsoft.AspNetCore.Authorization;

namespace Sprk.Bff.Api.Infrastructure.Authorization;

/// <summary>
/// Authorization requirement that checks if a user has a specific access level on a resource.
/// Used with ResourceAccessHandler to enforce Dataverse-backed authorization.
/// </summary>
public class ResourceAccessRequirement : IAuthorizationRequirement
{
    public string RequiredOperation { get; }

    public ResourceAccessRequirement(string requiredOperation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredOperation, nameof(requiredOperation));
        RequiredOperation = requiredOperation;
    }
}
