using System.Text.Json.Serialization;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

// unified-access-control-r2 task 140 (#1063) — contact-side Grant Access (owner C4 / Q1 / Q2, session 27).
//
// The three request shapes of the contact-side routes on the /api/v1/external group. Each is CLOSED: an unknown JSON
// member is a 400, not silently ignored. System.Text.Json IGNORES unknown members by default, so a body carrying
// `organizationId` would otherwise bind, drop the field, and grant — the organization-wide grant the owner forbids
// (Q2) would look accepted. There is deliberately no organization member on any of them.

/// <summary>
/// <c>POST /api/v1/external/contact-grants</c> — a contact grants ONE colleague of their own organization access to a
/// record, at or below their own level.
/// </summary>
/// <param name="RecordType"><c>project</c> | <c>matter</c> | <c>workassignment</c>.</param>
/// <param name="RecordId">The record to grant access to.</param>
/// <param name="GranteeContactId">The colleague, by contact id. Exactly one of this and <paramref name="GranteeEmail"/>.</param>
/// <param name="GranteeEmail">The colleague, by email — matched among the active contacts holding a current membership
/// of one of the grantor's own organizations ONLY (<c>ExternalParticipationService.FindConferringMembersByEmailAsync</c>);
/// nobody outside those organizations is considered or disclosed. No such colleague → 422; several → 409.</param>
/// <param name="AccessLevel">The requested level (option-set value). Written at most at the grantor's own level.</param>
/// <param name="ExpiryDate">Optional. Absent → today + 90 days. Never later than the grantor's own grant on the record
/// (owner G2 (i)); a past date is refused.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContactGrantRequest(
    string? RecordType,
    Guid? RecordId,
    Guid? GranteeContactId,
    string? GranteeEmail,
    ExternalAccessLevel AccessLevel,
    DateOnly? ExpiryDate);

/// <summary><c>GET /api/v1/external/contact-grants?recordType=…&amp;recordId=…</c> — the grants the caller issued on a record.</summary>
public sealed record ContactGrantListQuery(string? RecordType, Guid? RecordId);

/// <summary><c>POST /api/v1/external/contact-grants/revoke</c> — revoke one grant the caller issued.</summary>
/// <param name="AccessRecordId">The <c>sprk_externalrecordaccess</c> row.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContactGrantRevokeRequest(Guid AccessRecordId);

/// <summary>The outcome of a contact grant.</summary>
/// <param name="AccessRecordId">The surviving <c>sprk_externalrecordaccess</c> row.</param>
/// <param name="GranteeContactId">The colleague the grant names.</param>
/// <param name="GrantedAccessLevel">The level written — the requested one capped at the grantor's own.</param>
/// <param name="Narrowed">The grantor's level lowered the request.</param>
/// <param name="ExpiryDate">The expiry the grant carries.</param>
/// <param name="ExpiryNarrowed">The requested (or default) expiry was cut back to the grantor's own.</param>
public sealed record ContactGrantResponse(
    Guid AccessRecordId,
    Guid GranteeContactId,
    ExternalAccessLevel? GrantedAccessLevel,
    bool Narrowed,
    DateOnly? ExpiryDate,
    bool ExpiryNarrowed);

/// <summary>One grant the caller issued.</summary>
public sealed record ContactIssuedGrant(
    Guid AccessRecordId,
    Guid ContactId,
    string? FullName,
    string? Email,
    ExternalAccessLevel? AccessLevel,
    DateOnly? ExpiryDate);

/// <summary>The grants the caller issued on one record (active rows only).</summary>
public sealed record ContactIssuedGrantsResponse(IReadOnlyList<ContactIssuedGrant> Grants);

/// <summary>The outcome of a contact revoke.</summary>
/// <param name="AccessRecordId">The row the caller named.</param>
/// <param name="DeactivatedCount">How many of the caller's own active rows on that grant were deactivated.</param>
/// <param name="AccessRemainsFromOthers">
/// <c>true</c> when the colleague still holds an active row on the record that somebody ELSE issued — the caller's
/// revoke never touches it, so the colleague keeps that access.
/// </param>
public sealed record ContactGrantRevokeResponse(Guid AccessRecordId, int DeactivatedCount, bool AccessRemainsFromOthers);
