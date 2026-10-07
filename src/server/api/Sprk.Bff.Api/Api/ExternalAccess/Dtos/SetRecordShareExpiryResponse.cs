namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response for a successful record-wide share expiry change (spec FR-33, task 098).
/// </summary>
/// <param name="UpdatedCount">
/// How many active shares on the record now end on <paramref name="ExpiresDate"/> — contact and organization
/// shares, including any that had lapsed. <c>0</c> when the record has no active shares; the date then has
/// nowhere to be stored until a share exists, and the client keeps it as the default for the next one (task 099).
/// </param>
/// <param name="ExpiresDate">The date that was applied.</param>
public record SetRecordShareExpiryResponse(
    int UpdatedCount,
    DateOnly ExpiresDate);
