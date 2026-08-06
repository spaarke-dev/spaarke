namespace Spaarke.Dataverse;

/// <summary>
/// Field mapping profile, rule, and record operations.
/// Part of the IDataverseService composite (ISP segregation).
/// </summary>
public interface IFieldMappingDataverseService
{
    Task<FieldMappingProfileEntity[]> QueryFieldMappingProfilesAsync(CancellationToken ct = default);

    Task<FieldMappingProfileEntity?> GetFieldMappingProfileAsync(
        string sourceEntity,
        string targetEntity,
        CancellationToken ct = default);

    Task<FieldMappingRuleEntity[]> GetFieldMappingRulesAsync(
        Guid profileId,
        bool activeOnly = true,
        CancellationToken ct = default);

    Task<Dictionary<string, object?>> RetrieveRecordFieldsAsync(
        string entityLogicalName,
        Guid recordId,
        string[] fields,
        CancellationToken ct = default);

    Task<Guid[]> QueryChildRecordIdsAsync(
        string childEntityLogicalName,
        string parentLookupField,
        Guid parentRecordId,
        CancellationToken ct = default);

    /// <param name="impersonateSystemUserId">
    /// OPTIONAL Dataverse <c>systemuserid</c> to run the write AS (via <c>MSCRMCallerID</c> impersonation —
    /// effective privileges = intersection of the app user and the impersonated user; honest <c>modifiedby</c>).
    /// Null/empty = app-only (existing callers byte-unchanged). Added for the Job B apply path (task 031); the
    /// confirming user's identity is threaded here so the field update is attributed to and gated by them.
    /// </param>
    Task UpdateRecordFieldsAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        CancellationToken ct = default,
        Guid? impersonateSystemUserId = null);

    Task<FieldMappingProfileEntity?> GetFieldMappingProfileWithRulesAsync(
        string sourceEntity,
        string targetEntity,
        bool activeRulesOnly = true,
        CancellationToken ct = default);
}
