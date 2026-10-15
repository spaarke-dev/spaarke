---
name: dataverse-retrieveprincipalaccess-vs-privilege-2026-10-09
description: Does RetrievePrincipalAccess (RPA) report POA-shared ShareAccess/WriteAccess when the user holds no prvShare/prvWrite? Documented two-stage privilege-then-access check vs undocumented RPA computation. UAC-r2 share-level checks.
metadata:
  type: project
---

# RetrievePrincipalAccess vs role privileges — 2026-10-09

**Question**: If a user has a POA share with ShareAccess/WriteAccess but no prvShare/prvWrite at any depth, does RPA return those rights? Can that user GrantAccess?

## Documented
- Two-stage check (how-record-access-determined): privilege check first ("required privilege ... through a role directly, or ... team ... If not, ... access denied"); depth ignored there. Then access check: ownership (any depth), role access (depth must cover owner/BU/child-BU/org), shared access, hierarchy. Every shared-access row says "provided the user passed the privilege check".
- security-sharing-assigning: "you can't give a user any rights that they wouldn't have for that type of table, based on the role"; example: shared account w/o Read privilege → user "is unable to see that account" (implies POA row is stored but ineffective, not rejected).
- security-access-rights: GrantAccess/ModifyAccess/RevokeAccess require SHARE; sharer needs SHARE+READ; target needs READ.
- RPA reference + SDK class + security-access-coding: only "Retrieves the access rights of the principal to the record"; NO statement on whether privileges are intersected.

## Inferred (not documented)
- RPA returns *effective* rights = (ownership ∪ role-depth ∪ POA ∪ hierarchy) ∩ held privileges → POA ShareAccess without prvShare would NOT appear. Consistent with docs' framing and community experience, but unverified → spike: grant POA Share to a user lacking prvShare, compare RPA vs RetrieveSharedPrincipalsAndAccess / principalobjectaccess accessrightsmask.
- For raw POA mask use RetrieveSharedPrincipalsAndAccess (documented as "access ... because the record was shared").

## Sources
- learn.microsoft.com/power-platform/admin/how-record-access-determined (2025-07-15)
- learn.microsoft.com/power-apps/developer/data-platform/security-sharing-assigning (2026-08-31)
- learn.microsoft.com/power-apps/developer/data-platform/security-access-rights
- learn.microsoft.com/power-apps/developer/data-platform/security-access-coding
- .../webapi/reference/retrieveprincipalaccess ; dotnet/api/microsoft.crm.sdk.messages.retrieveprincipalaccessrequest

Related: [[dataverse-poa-grant-modify-revoke-semantics-2026-09-15]], [[dataverse-record-access-security-2026-07-16]].
