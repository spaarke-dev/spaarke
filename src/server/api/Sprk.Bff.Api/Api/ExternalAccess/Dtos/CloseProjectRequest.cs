namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for POST /api/v1/external-access/close-project.
/// Closes a Secure Project by revoking all external access and removing the revoked grantees from the project's
/// own SharePoint Embedded container.
/// </summary>
/// <param name="ProjectId">The Dataverse Project (sprk_project) ID to close.</param>
/// <remarks>
/// <para><b><c>ContainerId</c> was DELETED 2026-10-03</b> (unified-access-control-r2 task 166, route-authorization
/// sweep finding S-39; the task 085 precedent). It was a free string the handler passed straight to an app-only
/// permission sweep, while <c>DelegationRuleFilter</c> authorized only <see cref="ProjectId"/> — so a caller with
/// Write on any project could strip the permissions of ANY container. The container is now derived server-side
/// from the authorized project (<c>RecordContainerResolver.ResolveForRecordAsync</c>).</para>
/// <para><b>Deploy ordering — BFF-safe-first.</b> A shipped client that still sends <c>containerId</c> has it
/// ignored: System.Text.Json skips unknown members by default and the BFF configures no
/// <c>UnmappedMemberHandling.Disallow</c> on the request pipeline.</para>
/// </remarks>
public record CloseProjectRequest(Guid ProjectId);
