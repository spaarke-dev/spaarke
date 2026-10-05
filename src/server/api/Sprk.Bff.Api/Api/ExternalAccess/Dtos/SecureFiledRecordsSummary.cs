using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// unified-access-control-r2 task 158 (owner round 6): what a provisioning did to the work assignments and projects FILED
/// UNDER the matter or project it secured — each made secure itself (its own named-team owner, container and creator share)
/// and given the record's sharees. Additive to the provisioning response.
/// </summary>
/// <param name="Status"><c>Completed</c>, <c>Incomplete</c>, <c>NotApplicable</c> (nothing can be filed under the record
/// for this rule) or <c>Failed</c> (the filed records could not be read, so nothing was decided).</param>
/// <param name="Examined">Filed work assignments and projects found.</param>
/// <param name="Secured">Made secure by this call.</param>
/// <param name="AlreadySecure">Already secure (isolated) before this call.</param>
/// <param name="Remaining">Not secure after this call — each listed in <paramref name="Records"/> with its reason.</param>
/// <param name="Records">One entry per filed record.</param>
public sealed record SecureFiledRecordsSummary(
    string Status,
    int Examined,
    int Secured,
    int AlreadySecure,
    int Remaining,
    IReadOnlyList<SecureFiledRecordOutcome> Records);

/// <summary>One filed record and what happened to it.</summary>
/// <param name="RecordType"><c>project</c> | <c>workassignment</c>.</param>
/// <param name="RecordId">The record.</param>
/// <param name="Outcome"><c>secured</c>, <c>already-secure</c>, <c>not-filed-under-secure</c>, <c>unverifiable</c>,
/// <c>refused</c> or <c>failed</c>.</param>
/// <param name="ReasonCode">For a record not secured: the reason code its own provisioning (or the filing check) answered.</param>
/// <param name="Detail">For a record not secured: why, in words (names no other record).</param>
public sealed record SecureFiledRecordOutcome(
    string RecordType,
    Guid RecordId,
    string Outcome,
    string? ReasonCode,
    string? Detail)
{
    /// <summary>True when the record is secure after the call.</summary>
    [JsonIgnore]
    public bool IsSecured => Outcome is "secured" or "already-secure";

    /// <summary>True when the record should be secure and is not (or its sharees could not all be given to it).</summary>
    [JsonIgnore]
    public bool IsOutstanding => Outcome is "unverifiable" or "refused" or "failed";
}
