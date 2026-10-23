using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// <c>POST /api/v1/external/workforce/link</c> (ADR-028 A9, r3 task 048) — the guest half of the link proof. CLOSED: any
/// other property is a 400. The token is validated, never stored, logged or echoed.
/// </summary>
/// <param name="GuestAccessToken">The same person's Spaarke-tenant guest access token for the stamp BFF app.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExternalWorkforceLinkRequest(string? GuestAccessToken);

/// <summary>The link's answer (200).</summary>
/// <param name="SystemUserId">The systemuser the home identity is now linked to.</param>
/// <param name="Outcome"><c>linked</c> (written now) or <c>already_linked</c> (nothing written).</param>
public sealed record ExternalWorkforceLinkResponse(Guid SystemUserId, string Outcome);
