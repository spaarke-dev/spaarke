using System.Security.Claims;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Result of AI authorization check.
/// </summary>
/// <param name="Success">Whether authorization was successful for all requested documents.</param>
/// <param name="Reason">Reason for failure when Success is false. Null when successful.</param>
/// <param name="AuthorizedDocumentIds">Document IDs that were successfully authorized. May be a subset of requested IDs.</param>
public record AuthorizationResult(
    bool Success,
    string? Reason,
    IReadOnlyList<Guid> AuthorizedDocumentIds)
{
    /// <summary>
    /// Creates a successful authorization result.
    /// </summary>
    public static AuthorizationResult Authorized(IReadOnlyList<Guid> documentIds)
        => new(true, null, documentIds);

    /// <summary>
    /// Creates a failed authorization result.
    /// </summary>
    public static AuthorizationResult Denied(string reason)
        => new(false, reason, Array.Empty<Guid>());

    /// <summary>
    /// Creates a partial authorization result (some documents authorized, some denied).
    /// </summary>
    public static AuthorizationResult Partial(IReadOnlyList<Guid> authorizedIds, string reason)
        => new(false, reason, authorizedIds);
}

/// <summary>
/// Unified authorization service for AI endpoints.
/// </summary>
/// <remarks>
/// <para>
/// This service provides FullUAC (User Access Control) authorization for AI operations.
/// FullUAC is required for non-Dataverse contexts (Office.js, web apps) where Dataverse
/// security cannot be relied upon.
/// </para>
///
/// <para>
/// <strong>Authorization Flow:</strong>
/// <list type="number">
/// <item>Extract documentIds from request</item>
/// <item>For each document, lookup sprk_document.sprk_graphitemid</item>
/// <item>Call RetrievePrincipalAccess to verify user has read permission</item>
/// <item>Return AuthorizationResult with authorized subset</item>
/// </list>
/// </para>
///
/// <para>
/// <strong>Retry Responsibilities:</strong>
/// Authorization itself does not retry. Retry logic is implemented at the storage layer
/// (see task 003) for operations that may fail due to eventual consistency (e.g., newly
/// created documents not yet visible via RetrievePrincipalAccess).
/// </para>
///
/// <para>
/// <strong>ADR Compliance:</strong>
/// <list type="bullet">
/// <item>ADR-008: Used by endpoint filters for resource-level authorization</item>
/// <item>ADR-013: Part of unified AI architecture in Sprk.Bff.Api</item>
/// </list>
/// </para>
/// </remarks>
public interface IAiAuthorizationService
{
    /// <summary>
    /// Authorizes access to one or more documents for AI operations.
    /// </summary>
    /// <param name="user">The claims principal representing the current user.</param>
    /// <param name="documentIds">The Dataverse document IDs (sprk_document.sprk_documentid) to authorize.</param>
    /// <param name="httpContext">HTTP context containing user's bearer token for OBO authentication.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="AuthorizationResult"/> indicating success or failure.
    /// When partially successful, contains the subset of authorized document IDs.
    /// </returns>
    /// <exception cref="ArgumentNullException">When user, documentIds, or httpContext is null.</exception>
    /// <exception cref="ArgumentException">When documentIds is empty.</exception>
    /// <remarks>
    /// The httpContext is required to extract the user's bearer token for On-Behalf-Of (OBO) authentication.
    /// This ensures permission checks are performed as the user, not as the service principal.
    /// </remarks>
    Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        IReadOnlyList<Guid> documentIds,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}
