using Sprk.Bff.Api.Infrastructure.Authentication;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Api.Filters;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Implementation of AI authorization using FullUAC mode.
/// </summary>
/// <remarks>
/// <para>
/// FullUAC mode calls Dataverse's RetrievePrincipalAccess for each document to verify
/// the user has at least Read access. This is required for AI operations in non-Dataverse
/// contexts (Office.js, web apps, external integrations).
/// </para>
/// <para>
/// <strong>Performance Note:</strong> This service makes one Dataverse call per document.
/// For batch operations with many documents, consider implementing parallel calls with
/// appropriate throttling.
/// </para>
/// <para>
/// <strong>Retry Note:</strong> This service does NOT implement retry logic. Retry is
/// implemented at the storage layer (task 003) for operations that may fail due to
/// eventual consistency in newly created documents.
/// </para>
/// <para>
/// <strong>External document narrowing</strong> (unified-access-control-r2 task 188 round 4a, F1-1). Read alone is not
/// enough for a caller who is not a resolved INTERNAL systemuser (a systemuser flagged <c>sprk_isexternal</c>, an
/// unresolved caller, or a failed lookup): after the Read loop, every authorized id goes through THE document narrowing
/// (<see cref="InternalPlaneDocumentNarrowing.ForCallerAsync"/>, one app-only call per request). An id it hides moves to
/// the denied set with the SAME reason as an unreadable id, so a hidden document and one the caller cannot Read are
/// indistinguishable. A narrowing fault hides every id (deny all). Internal systemusers are not narrowed and nothing is
/// read for them. Every caller of this service (chat document context, analysis execute, RAG search rows, visualization
/// rows) downloads or returns content only for the ids it authorizes, so the narrowing covers them all.
/// </para>
/// </remarks>
public class AiAuthorizationService : IAiAuthorizationService
{
    /// <summary>The denial reason of an id the caller may not Read — also given to an id the narrowing hides.</summary>
    private const string InsufficientReadReason = "Insufficient permissions (Read access required)";

    /// <summary>The narrowing's log label for this seam (no ids).</summary>
    private const string NarrowingSurface = "ai-authorization";

    private readonly IAccessDataSource _accessDataSource;
    private readonly ILogger<AiAuthorizationService> _logger;

    /// <summary>
    /// Creates a new instance of AiAuthorizationService.
    /// </summary>
    /// <param name="accessDataSource">Data source for querying user access from Dataverse.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public AiAuthorizationService(
        IAccessDataSource accessDataSource,
        ILogger<AiAuthorizationService> logger)
    {
        _accessDataSource = accessDataSource ?? throw new ArgumentNullException(nameof(accessDataSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        IReadOnlyList<Guid> documentIds,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(documentIds);
        ArgumentNullException.ThrowIfNull(httpContext);

        if (documentIds.Count == 0)
        {
            throw new ArgumentException("At least one document ID is required.", nameof(documentIds));
        }

        // Extract Azure AD Object ID from claims
        var userId = ExtractUserId(user);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning(
                "[AI-AUTH] Authorization DENIED: No user identity found in claims. " +
                "DocumentCount={DocumentCount}",
                documentIds.Count);

            return AuthorizationResult.Denied("User identity not found in claims");
        }

        // Extract user's bearer token for OBO authentication
        string? userAccessToken = null;
        try
        {
            userAccessToken = TokenHelper.ExtractBearerToken(httpContext);
            _logger.LogDebug("[AI-AUTH] Extracted user bearer token for OBO authentication");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "[AI-AUTH] Failed to extract bearer token from request");
            return AuthorizationResult.Denied("Missing or invalid authorization token");
        }

        _logger.LogInformation(
            "[AI-AUTH] Authorization START: UserId={UserId}, DocumentCount={DocumentCount}, " +
            "DocumentIds=[{DocumentIds}]",
            userId,
            documentIds.Count,
            string.Join(", ", documentIds.Take(5)) + (documentIds.Count > 5 ? "..." : ""));

        var authorizedIds = new List<Guid>();
        var deniedIds = new List<Guid>();
        var denialReasons = new List<string>();

        // Check each document individually
        // Note: For better performance with many documents, consider Task.WhenAll with throttling
        foreach (var documentId in documentIds)
        {
            var (isAuthorized, reason) = await CheckDocumentAccessAsync(userId, documentId, userAccessToken, cancellationToken);

            if (isAuthorized)
            {
                authorizedIds.Add(documentId);
            }
            else
            {
                deniedIds.Add(documentId);
                if (!string.IsNullOrEmpty(reason))
                {
                    denialReasons.Add($"{documentId}: {reason}");
                }
            }
        }

        // Task 188 round 4a (F1-1): THE external document narrowing over the ids Read allowed. A hidden id is denied
        // exactly like an unreadable one; a narrowing fault hides them all (fail closed).
        if (authorizedIds.Count > 0)
        {
            var visibility = await InternalPlaneDocumentNarrowing.ForCallerAsync(
                httpContext, authorizedIds.Select(id => id.ToString()), NarrowingSurface, _logger).ConfigureAwait(false);
            if (visibility.Narrowed)
            {
                foreach (var hiddenId in authorizedIds.Where(id => !visibility.IsShown(id.ToString())).ToList())
                {
                    authorizedIds.Remove(hiddenId);
                    deniedIds.Add(hiddenId);
                    denialReasons.Add($"{hiddenId}: {InsufficientReadReason}");
                }
            }
        }

        // All authorized
        if (deniedIds.Count == 0)
        {
            _logger.LogInformation(
                "[AI-AUTH] Authorization GRANTED: UserId={UserId}, AllDocumentsAuthorized={Count}",
                userId,
                authorizedIds.Count);

            return AuthorizationResult.Authorized(authorizedIds);
        }

        // All denied
        if (authorizedIds.Count == 0)
        {
            var reason = denialReasons.Count > 0
                ? string.Join("; ", denialReasons.Take(3))
                : "Access denied to all documents";

            _logger.LogWarning(
                "[AI-AUTH] Authorization DENIED: UserId={UserId}, AllDocumentsDenied={Count}, " +
                "Reason={Reason}",
                userId,
                deniedIds.Count,
                reason);

            return AuthorizationResult.Denied(reason);
        }

        // Partial authorization
        var partialReason = $"Access denied to {deniedIds.Count} of {documentIds.Count} documents";

        _logger.LogWarning(
            "[AI-AUTH] Authorization PARTIAL: UserId={UserId}, Authorized={AuthCount}, " +
            "Denied={DenyCount}, DeniedIds=[{DeniedIds}]",
            userId,
            authorizedIds.Count,
            deniedIds.Count,
            string.Join(", ", deniedIds.Take(3)) + (deniedIds.Count > 3 ? "..." : ""));

        return AuthorizationResult.Partial(authorizedIds, partialReason);
    }

    /// <summary>
    /// Checks if the user has read access to a specific document.
    /// </summary>
    private async Task<(bool IsAuthorized, string? Reason)> CheckDocumentAccessAsync(
        string userId,
        Guid documentId,
        string? userAccessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug(
                "[AI-AUTH] Checking access: UserId={UserId}, DocumentId={DocumentId}",
                userId,
                documentId);

            var accessSnapshot = await _accessDataSource.GetUserAccessAsync(
                userId,
                documentId.ToString(),
                userAccessToken,
                cancellationToken);

            // AI operations require at least Read access
            var hasReadAccess = accessSnapshot.AccessRights.HasFlag(AccessRights.Read);

            if (hasReadAccess)
            {
                _logger.LogDebug(
                    "[AI-AUTH] Access check PASSED: UserId={UserId}, DocumentId={DocumentId}, " +
                    "AccessRights={AccessRights}",
                    userId,
                    documentId,
                    accessSnapshot.AccessRights);

                return (true, null);
            }
            else
            {
                _logger.LogDebug(
                    "[AI-AUTH] Access check FAILED: UserId={UserId}, DocumentId={DocumentId}, " +
                    "AccessRights={AccessRights} (Read required)",
                    userId,
                    documentId,
                    accessSnapshot.AccessRights);

                return (false, InsufficientReadReason);
            }
        }
        catch (Exception ex)
        {
            // Fail-closed: deny access on errors
            _logger.LogError(
                ex,
                "[AI-AUTH] Access check ERROR: UserId={UserId}, DocumentId={DocumentId}. " +
                "Fail-closed: denying access.",
                userId,
                documentId);

            return (false, "Authorization check failed");
        }
    }

    /// <summary>
    /// Extracts the Azure AD Object ID from the claims principal.
    /// </summary>
    private static string? ExtractUserId(ClaimsPrincipal user)
    {
        // Try multiple claim types in order of preference
        // 'oid' is the Azure AD Object ID claim
        return CallerResolution.ResolveObjectId(user);
    }
}
