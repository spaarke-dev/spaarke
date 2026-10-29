---
name: dataverse-user-bu-change-semantics-2026-10-28
description: What a systemuser BU change does under matrix BUs with default orgdb settings — roles stripped, user-owned records move, default-team swap, SetBusinessSystemUser bound action params (DoNotMoveAllRecords), Assign→owningbusinessunit + cascade
metadata:
  type: reference
---

# Dataverse user BU change semantics (2026-10-28)

**Question**: Exact effects of changing a systemuser's BU (UI / SetBusinessSystemUser) with EnableOwnershipAcrossBusinessUnits=true and DoNotRemoveRoles/AlwaysMoveRecord settings absent; plus Assign→owningbusinessunit + cascade.

**Findings**:
- create-edit-business-units (ms.date 2025-05-21): `DoNotRemoveRolesOnChangeBusinessUnit` default false = "the user's assigned security role is removed"; `AlwaysMoveRecordToOwnerBusinessUnit` default true = "user owned records are moved to the new user's business unit". Default team swap automatic ("removes the user from the previous business unit's default team and adds…"). Whether roles from THIRD BUs (neither old nor new) are stripped is undocumented; Important box says "can remove all security role assignments".
- Web API `SetBusinessSystemUser` (ms.date 2026-09-03) is BOUND to systemuser; params `BusinessUnit` (required), `ReassignPrincipal` (required, Nullable=False), `DoNotMoveAllRecords` (Edm.Boolean, nullable). SDK class is "Deprecated" → use Update of SystemUser.BusinessUnitId. Same user in UserId+ReassignPrincipal = keep records. Manager nulled if moved out of manager's BU hierarchy.
- cascading page (ms.date 2026-10-26): with matrix on + AlwaysMove=true (default), Assign updates record BU to new owner's BU; Cascade All updates child owner+BU; Cascade None leaves children. Parental = Assign Cascade All/Active/UserOwned.
- wp-security-cds (2026-10-05) advises AlwaysMove=false for envs that enabled matrix in preview and for non-prod.
- KA-25456: legacy UI hides other-BU roles; check in PPAC. Owner/access team memberships, FSP (systemuserprofiles), team-owned records, rollback symmetry, app-user specifics: UNDOCUMENTED.

**Sources**: learn.microsoft.com power-platform/admin/{create-edit-business-units,wp-security-cds,assign-security-roles,manage-application-users}; power-apps/developer/data-platform/{webapi/reference/setbusinesssystemuser,configure-entity-relationship-cascading-behavior,special-update-operation-behavior}; dotnet/api/microsoft.crm.sdk.messages.setbusinesssystemuserrequest; power-apps/maker/data-platform/create-edit-entity-relationships; community.dynamics.com/article/KA-25456.

**Open questions**: third-BU role retention; DoNotMoveAllRecords vs AlwaysMove precedence; plain PATCH businessunitid ReassignPrincipal default; whether user-move cascades to children owned by others.
Related: [[uac191a-flagged-external-dataverse-baseline-2026-10-21]], [[dataverse-cascade-share-parent-child-access-2026-08-18]], [[dataverse-secure-family-access-teams-2026-10-12]].
