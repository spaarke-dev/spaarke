namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response for <c>GET /api/v1/external/me/entitlements</c> — the caller's Tier-1 MODULE entitlement
/// context (project task 072, owner Option B). The external-spa widget registry gates tab visibility on
/// <see cref="Entitlements"/>; the shape mirrors the client's <c>MeEntitlementsResponse</c> contract in
/// <c>src/client/external-spa/src/api/me-client.ts</c> (camelCase JSON: displayName / email / plane /
/// entitlements) so the client's mock→real swap (task 073) is a one-line change.
/// </summary>
/// <param name="DisplayName">The caller's display name (from the <c>name</c> / <c>preferred_username</c>
/// claim, falling back to email).</param>
/// <param name="Email">The caller's email/UPN.</param>
/// <param name="Plane">The identity plane: <c>"workforce"</c> (internal Entra) or <c>"ciam"</c> (external
/// outside counsel). NOTE: <c>"admin"</c> is not a distinct server plane — an admin is a workforce caller
/// whose App-Roles map to the <c>admin</c> module.</param>
/// <param name="Entitlements">The flat list of module codes the caller is entitled to (Tier-1). Internal
/// from <c>sprk_approlemodulemap</c> (App-Role → module); external the blanket outside-counsel set.</param>
public record MeEntitlementsResponse(
    string DisplayName,
    string Email,
    string Plane,
    IReadOnlyList<string> Entitlements);
