---
name: uac191a-flagged-external-dataverse-baseline-2026-10-21
description: UAC-r2 T191a read-only baseline on spaarkedev1 — hierarchy off, matrix on, flagged-external users' BU/roles/teams/POA, role depths (root-BU Deep = org-wide), prvWriteUser/prvReadUser cannot be Basic, BU-change role-strip default, SPE item invite to non-member documented
metadata:
  type: project
---

# T191a flagged-external Dataverse baseline (dev, 2026-10-21)

**Question**: Live read-only state for #2179 design A spikes (hierarchy, matrix, role depths, flagged users, POA baseline) + SPE item invite to non-members.

**Findings**:
- Org: `ishierarchicalsecuritymodelenabled=false` (maxdepth 3 default, position hierarchy off, 0 hierarchysecurityconfiguration rows). orgdborgsettings `EnableOwnershipAcrossBusinessUnits=true`; `DoNotRemoveRolesOnChangeBusinessUnit` and `AlwaysMoveRecordToOwnerBusinessUnit` absent = defaults (roles stripped on BU change; user-owned records move) per Learn create-edit-business-units.
- 2 flagged users (both B2B guests, 1 disabled), both in ROOT BU "Spaarke"; only membership = root default team, which carries Spaarke Basic User + Office Add In User + Console User (isinherited=1). Root-BU role at Deep = whole org. Spaarke Basic User: Read Deep doc/event/matter/todo, comm Basic CRUD+Share+Assign. Office role: Write/Assign/Share matter Deep, Create event Deep. Platform Basic User: only prvReadUser Global of the asked set.
- Privilege metadata: prvReadUser/prvWriteUser canbebasic=false (min Local) — the external role cannot hold them at Basic. Privilege names are cased (prvWritesprk_Document); Web API `name eq` is case-insensitive.
- POA baseline: 5 rows for flagged users (self-shares on systemuser/usersettings mask 852023, one activitypointer Read). Org POA total 1,980. Table sizes: document 600, communication 319, event 83, todo 66, matter 62, project 23.
- No "External Collaborators" BU or team exists. SPE: item invite to non-member documented (configure-authentication-authorization ms.date 2026-07-13); Office web needs "permission to view or edit the file" (open-office-files); live edit unverified.

**Sources**: live GETs spaarkedev1 (organizations, systemusers, systemuserrolescollection, teammemberships, teamrolescollection, roleprivilegescollection, privileges, principalobjectaccessset); learn.microsoft.com power-platform/admin/create-edit-business-units; sharepoint/dev/embedded/build/{configure-authentication-authorization,share-files-manage-permissions,open-office-files}; graph/api/driveitem-invite.

**Open questions**: S1–S4 writes not run (owner approval). Whether app-only invite under restrictive sharing model works; whether External BU default team can be the share target.
Related: [[dataverse-secure-family-access-teams-2026-10-12]], [[spe-jit-membership-item-sharing-office-edit-2026-10-06]].
