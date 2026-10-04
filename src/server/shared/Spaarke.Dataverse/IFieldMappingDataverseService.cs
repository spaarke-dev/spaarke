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
    /// Null = app-only (existing callers byte-unchanged). <see cref="Guid.Empty"/> is refused with an
    /// <see cref="ArgumentException"/> before the write is sent (unified-access-control-r2 task 104, fail closed;
    /// the app-only EntitySetName metadata lookup may already have run). It used to be read as app-only, which
    /// silently dropped the caller's row-level security from the write.
    /// Added for the Job B apply path (task 031); the confirming user's identity is threaded here so the field
    /// update is attributed to and gated by them.
    /// </param>
    Task UpdateRecordFieldsAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        CancellationToken ct = default,
        Guid? impersonateSystemUserId = null);

    /// <summary>
    /// Updates fields on an EXISTING record and never creates one. Same app-only PATCH as
    /// <see cref="UpdateRecordFieldsAsync"/>, plus <c>If-Match: *</c>, which turns Dataverse's default
    /// upsert into update-only.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a separate method (unified-access-control-r2 task 130).</b> A Web API PATCH without
    /// <c>If-Match</c> UPSERTS: if the id does not exist, Dataverse creates a row with it. That default is
    /// load-bearing elsewhere — <c>InvoiceReviewService</c> creates its invoice by PATCHing a fresh GUID — so
    /// it cannot be changed on <see cref="UpdateRecordFieldsAsync"/>. The recalculate routes must not create:
    /// a matter or project deleted between the authorization check and the rollup write would otherwise come
    /// back as an empty row carrying only derived rollup fields.</para>
    /// </remarks>
    /// <exception cref="KeyNotFoundException">The record does not exist (Dataverse refused the precondition).</exception>
    Task UpdateExistingRecordFieldsAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        CancellationToken ct = default);

    /// <summary>
    /// Updates fields on an existing record ONLY IF it is still at <paramref name="expectedVersion"/> — its
    /// <c>versionnumber</c> as read earlier. Sends <c>If-Match: W/"{expectedVersion}"</c> (a Dataverse row's
    /// ETag is its version number), so a concurrent write in between makes this one fail instead of silently
    /// overwriting it. Never creates.
    /// </summary>
    /// <remarks>
    /// Added by unified-access-control-r2 task 130 for invoice confirm: two concurrent confirms of the same
    /// document each created an invoice and the second link PATCH overwrote the first, orphaning an invoice.
    /// The link is now conditional on the version read in the resume check.
    /// </remarks>
    /// <exception cref="KeyNotFoundException">The record does not exist.</exception>
    /// <exception cref="System.Data.DBConcurrencyException">The record changed since
    /// <paramref name="expectedVersion"/> was read (HTTP 412); nothing was written.</exception>
    Task UpdateRecordFieldsIfUnchangedAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        long expectedVersion,
        CancellationToken ct = default);

    Task<FieldMappingProfileEntity?> GetFieldMappingProfileWithRulesAsync(
        string sourceEntity,
        string targetEntity,
        bool activeRulesOnly = true,
        CancellationToken ct = default);
}
