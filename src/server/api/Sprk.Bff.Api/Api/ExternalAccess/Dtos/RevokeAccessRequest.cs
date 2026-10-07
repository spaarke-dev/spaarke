namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for revoking external access from a Contact for a specific Project.
/// </summary>
/// <param name="AccessRecordId">The ID of the sprk_externalrecordaccess record to deactivate.</param>
/// <param name="ContactId">The Dataverse Contact ID whose access is being revoked.</param>
/// <param name="ProjectId">The Dataverse Project ID the access record belongs to.</param>
/// <remarks>
/// <para><b><c>ContainerId</c> was DELETED 2026-10-03</b> (unified-access-control-r2 task 166, route-authorization
/// sweep amendment (b), owner round 12 item 9; the task 085 precedent). It was a client-chosen SPE container the
/// handler swept app-only while <c>DelegationRuleFilter</c> authorized the grant row's ROOT. The container is now
/// derived server-side from that root (<c>RecordContainerResolver</c>, secure roots only).</para>
/// <para><b>Deploy ordering — BFF-safe-first.</b> A client that still sends <c>containerId</c> has it ignored
/// (System.Text.Json skips unknown members; no <c>UnmappedMemberHandling.Disallow</c> on the request pipeline).
/// The shipped Manage Access modal never sent it.</para>
/// </remarks>
public record RevokeAccessRequest(
    Guid AccessRecordId,
    Guid ContactId,
    Guid ProjectId);
