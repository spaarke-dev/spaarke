---
name: dataverse-poa-grant-modify-revoke-semantics-2026-09-15
description: Exact documented semantics of Dataverse GrantAccess / ModifyAccess / RevokeAccess (POA shares) — what Learn says vs what is NOT documented (Grant additive-vs-replace, Modify-with-no-share, Revoke-with-no-share, CreateAccess on shares, disabled/app-user targets). For UAC-r2 three-level share endpoints.
metadata:
  type: project
---

# POA share messages: documented vs undocumented semantics — 2026-09-15

**Question**: For BFF endpoints sharing a record with a systemuser at 1 of 3 levels (seam exposes only Grant+Revoke): does GrantAccess OR or REPLACE an existing share mask; does ModifyAccess replace / fail with no share; is RevokeAccess with no share an error; which AccessRights are meaningful on a share; disabled/app-user targets?

## Documented (quotable)
- **ModifyAccess REPLACES**: Web API ref + SDK class: "Replaces the access rights on the target record for the specified security principal (user, team, or organization)." Conceptual page: "To modify the access of a shared record, use the `ModifyAccess` message." Payload = `POST /api/data/v9.2/ModifyAccess` `{ Target:{<pk>, @odata.type}, PrincipalAccess:{ AccessMask:"ReadAccess, WriteAccess", Principal:{systemuserid, @odata.type:"Microsoft.Dynamics.CRM.systemuser"} } }` → 204. Same shape as GrantAccess. RevokeAccess = `{ Target, Revokee }`.
- **GrantAccess** ref only says "Grants a security principal ... access to the specified record." — NO add-vs-replace statement anywhere (Web API ref, SDK class, conceptual page, sample).
- **Role gate**: "you can't give a user any rights that they wouldn't have for that type of table, based on the role assigned to that user" (security-sharing-assigning). Privilege check ignores depth: "The access level isn't taken into account in the privilege check" (how-record-access-determined). Share target must also hold READ (security-access-rights dependency table).
- Rights compose as union: "the access rights that this user has on the record are the union of all the rights."
- AccessRights enum lists None/Read/Write/Append/AppendTo/Create/Delete/Share/Assign; [Flags]; "used for the AccessMask property". NOTHING says which are meaningful on a share; CreateAccess-on-share undocumented.

## NOT documented
- GrantAccess on existing share: OR vs replace. (Community/folklore says OR — unverified.) → use ModifyAccess for any level change; spike it.
- ModifyAccess when no share exists (error vs create).
- RevokeAccess when no share exists. Indirect evidence: MS sample `PowerApps-Samples/dataverse/orgsvc/CSharp/GrantModifyRevokeAccess/.../SampleProgram.cs` revokes systemUser2, who is never directly granted (only the team is), with no try/catch → suggests no-op. Sample evidence, not contract.
- Sharing with disabled user or application user. Error catalog has disabled-user codes only for team-add (`CannotAssociateDisabledUsersToTeams` 0x80048d36) and role-assign (`CannotAssignRoleToDisabledUser` 0x80048d35) — none for sharing.

## Doc bug worth knowing
RevokeAccess Web API ref AND RevokeAccessRequest SDK class both carry ModifyAccess's description ("Replaces the access rights...") — copy-paste error; don't cite it as Revoke semantics.

## Sources
- learn.microsoft.com/power-apps/developer/data-platform/security-sharing-assigning (ms.date 2026-08-31)
- .../webapi/reference/grantaccess | modifyaccess | revokeaccess | accessrights | principalaccess (2026-09-03)
- learn.microsoft.com/dotnet/api/microsoft.crm.sdk.messages.{grantaccessrequest,modifyaccessrequest,revokeaccessrequest,accessrights}
- learn.microsoft.com/power-platform/admin/how-record-access-determined
- .../data-platform/security-access-rights ; .../reference/web-service-error-codes ; .../webapi/web-api-functions-actions-sample §7
- github.com/microsoft/PowerApps-Samples/.../GrantModifyRevokeAccess/SampleProgram.cs

## Open questions (empirical spike needed)
Grant-on-existing (OR?), Modify-with-no-share, Revoke-with-no-share, CreateAccess in mask (ignored/stored/error?), disabled + app-user targets. Verify via `principalobjectaccessset` accessrightsmask read-back (NOT RetrievePrincipalAccess, which returns effective union incl. role/owner). Related: [[dataverse-record-access-security-2026-07-16]], [[dataverse-cascade-share-parent-child-access-2026-08-18]].
