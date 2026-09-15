namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for setting ONE expiry on every active external share of a record (spec FR-33, task 098) —
/// the Manage Access toolbar's Expiration.
///
/// <para>
/// The record is named ONLY by <paramref name="RecordType"/> + <paramref name="RecordId"/>. Unlike
/// <see cref="GrantAccessRequest"/> there is no legacy <c>ProjectId</c> shorthand: the delegation check
/// authorizes exactly the record whose shares are written.
/// </para>
/// </summary>
/// <param name="RecordType">The record type: <c>project</c> | <c>matter</c> | <c>workassignment</c> (case-insensitive). Required.</param>
/// <param name="RecordId">The record whose shares change. Required.</param>
/// <param name="ExpiryDate">
/// Required — the date every active share on the record will end, as a calendar date (<c>yyyy-MM-dd</c>). A date
/// before today (UTC) is rejected; today is valid. Shares that have already lapsed are renewed to this date.
/// </param>
public record SetRecordShareExpiryRequest(
    string? RecordType,
    Guid? RecordId,
    DateOnly? ExpiryDate);
