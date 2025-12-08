namespace Spaarke.Core.Auth;

/// <summary>
/// Interface for authorization service.
/// Enables unit testing of filters and services that depend on authorization.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Evaluates whether the user is authorized to perform the operation on the resource.
    /// </summary>
    /// <param name="context">The authorization context containing user, resource, and operation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The authorization result.</returns>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizationContext context, CancellationToken ct = default);
}
