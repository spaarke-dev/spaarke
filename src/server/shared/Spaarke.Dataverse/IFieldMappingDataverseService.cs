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

    Task UpdateRecordFieldsAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        CancellationToken ct = default);

    Task<FieldMappingProfileEntity?> GetFieldMappingProfileWithRulesAsync(
        string sourceEntity,
        string targetEntity,
        bool activeRulesOnly = true,
        CancellationToken ct = default);
}
