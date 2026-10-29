using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// <c>POST /api/v1/external/registration</c> (ADR-028 A9, r3 task 047) — the profile form. CLOSED: an unknown property
/// (an <c>email</c>, an owner, an oid) is a 400, so the email can only ever come from the token.
/// </summary>
/// <param name="FirstName">Required, at most 50 characters after trimming, no control characters.</param>
/// <param name="LastName">Required, at most 50 characters after trimming, no control characters.</param>
/// <param name="Department">Optional, at most 100 characters after trimming, no control characters.</param>
/// <param name="ConfirmExistingRecord">
/// True when the person confirmed, after a 409 <c>existing_record_found</c>, that the existing contact is theirs.
/// </param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExternalRegistrationRequest(
    string? FirstName,
    string? LastName,
    string? Department,
    bool? ConfirmExistingRecord);

/// <summary>The registration's answer (201 created, 200 otherwise).</summary>
/// <param name="ContactId">The caller's contact; null only for a resolved systemuser with no linked contact.</param>
/// <param name="SystemUserId">Set when the caller already resolved to a systemuser (a stored home link).</param>
/// <param name="Outcome"><c>created</c>, <c>bound</c>, <c>already_registered</c> or <c>resolved</c>.</param>
public sealed record ExternalRegistrationResponse(Guid? ContactId, Guid? SystemUserId, string Outcome);
