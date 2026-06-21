namespace Sprk.Bff.Api.Api.Admin.Models;

/// <summary>
/// Response DTO for <c>POST /api/admin/membership/refresh-metadata</c> (R3 task 036).
/// Surfaces which entity-type cache entries were invalidated so the operator can
/// confirm the action took effect for the entities they expected.
/// </summary>
/// <remarks>
/// Per R3 spec.md AC-1A.7: a second <c>DiscoverAsync</c> call against an invalidated
/// entity re-fetches from Dataverse metadata rather than returning the previous
/// cached <see cref="Sprk.Bff.Api.Services.Ai.Membership.DiscoveryResult"/>.
/// </remarks>
/// <param name="Refreshed">
/// Lowercase-normalized entity types whose discovery cache entries were invalidated.
/// Single-entity request always returns the requested entity (even if there was no
/// prior cache entry); refresh-all request returns every tracked entry the service
/// had populated. Empty when refresh-all is called on a cold process with no prior
/// discovery calls.
/// </param>
/// <param name="At">UTC timestamp the invalidation completed.</param>
public sealed record RefreshMetadataResponse(
    IReadOnlyList<string> Refreshed,
    DateTimeOffset At);
