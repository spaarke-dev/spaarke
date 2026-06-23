namespace Sprk.Bff.Api.Api.Admin.Models;

/// <summary>
/// Request body for <c>POST /api/admin/membership/refresh-metadata</c> (R3 task 036).
/// Optional — when the body is omitted (or <see cref="EntityType"/> is null/empty/whitespace),
/// the endpoint invalidates every membership-discovery cache entry tracked by the
/// <see cref="Sprk.Bff.Api.Services.Ai.Membership.IMembershipFieldDiscoveryService"/>
/// since process start. When non-empty, invalidates only that entity's cached
/// <see cref="Sprk.Bff.Api.Services.Ai.Membership.DiscoveryResult"/>.
/// </summary>
/// <param name="EntityType">
/// Optional Dataverse entity logical name (case-insensitive — normalized to lowercase
/// by the service to match its cache-key convention). Example: <c>"sprk_matter"</c>.
/// </param>
public sealed record RefreshMetadataRequest(string? EntityType);
