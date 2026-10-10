# Task 179: Managing access needs the Share privilege (owner round 89 item 1)

Branch `task/uac-r2-179`, from `origin/master` `b71e83072`. Rigor FULL.

## What changed

The rule in `DelegationRuleFilter` (the `/api/v1/external-access` group filter) is now: **Write on the record AND
Dataverse's Share privilege on its table, at a depth that reaches the record, evaluated as the caller.**

- **One probe, no new round trip.** `CallerRecordAccessProbe.GetCallerRightsAsync` already returned the full
  `RetrievePrincipalAccess` rights string, and `DataverseAccessRightsMapper` already mapped `ShareAccess` to
  `AccessRights.Share`. The filter now checks both bits of that one answer. The POML's escalation trigger ("a second
  Dataverse round trip per request") does not fire.
- **Refusals.** No Write: 403 `sdap.access.deny.delegation_write_required` (unchanged). Write but no Share: 403
  `sdap.access.deny.delegation_share_required` (new, additive). Both carry one user-facing sentence that names the
  whole rule. Every other exit (no caller token, unresolvable target, probe failure, unreadable answer) still denies.
- **`can-manage-access`** sits behind the filter, so it reports the same verdict. `TrackingFieldTrio`, the Access
  flyout and every ribbon command gated on it (Update Access, Make Secure, Remove Secure, Manage Access) hide for a
  role without Share. No client code mirrors the rule; only the Manage Access modal's denial text changed, to name
  the rule correctly.
- **Internal user shares** (`/share-user`, `/unshare-user`, `/user-shares`) are on the same group, so they take the
  same rule. The handler's grantor-ceiling re-probe is unchanged.
- **System paths are unchanged.** The reconciliation jobs, the job-run Assigned-To materializer, the secure-child
  cascade and the other app-identity paths do not run as the caller and do not pass this filter.

## Deviations from the POML, with reasons (main session: please confirm)

1. **The organization-owned branch keeps the table Write privilege.** The POML says that branch "uses the table's
   Share privilege". Dataverse creates no Share privilege for an organization-owned table. Live metadata on
   spaarkedev1 (2026-10-09): `sprk_noaccessentry` (OwnershipType `OrganizationOwned`) has only
   `prvCreate/Read/Write/Delete/Append/AppendTo sprk_noaccessentry`. Asking for `prvSharesprk_noaccessentry` would
   refuse every caller on `/no-access/enforce`, permanently, the Spaarke Access Administrator included, and no role
   change could fix it. The route only REMOVES access (it enforces an entry already saved), and the removals stay
   bounded by the entry author's Write on each record (owner N5).
   *Open question for the owner:* should N5 (the author's Write on the covered record) also require Share? Not
   changed here.
2. **`/assigned-access/sync` keeps Write alone; `GET /assigned-access` and `/assigned-access/dismiss` take the full
   rule.** Goal (4) says assigned access is unchanged, but the sync route DOES run as the caller: the post-save form
   script calls it on every save of a project, matter or work assignment, and every create wizard calls it after the
   create. It applies only the record's own Assigned-To columns, exactly as the app-only
   `AssignedAccessReconciliationJob` does within five minutes. Requiring Share there would protect nothing (the job
   grants the same access anyway) and would put a WARNING banner on every save for every Write-holder without Share.
   The list (Manage Access suggestions) and Dismiss (a decision about who gets access) are Manage Access actions and
   take the rule. Implemented as `DelegationTarget.WriteSuffices`, set only in the sync case; its default (`false`) is
   the full rule, so `default(DelegationTarget)` cannot widen anything.
3. **Make Secure, Remove Secure, Close Project and Invite now need Share too.** They are on the same group, and the
   POML scopes the rule to the whole group. A role with Create and Write but no Share can still create a project, but
   the wizard's Secure step (`/provision-project`) is refused 403. The project is created unflagged and stays
   non-secure (it fails closed, not open), and the wizard shows its generic failure message. On dev no role is
   affected (see the inventory). Owner round 2's "securing needs Write" now reads "securing needs Write and Share".

4. **The No Access entry list follows the rule too** (verifier finding, fixed here). `GET /api/v1/records/{table}/{id}/no-access`
   is not on the group, because it also answers Read-only callers with the banner signals. It gave the covering
   entries (the Manage Access list) to any Write-holder. It now asks the same predicate,
   `DelegationRuleFilter.MayManageAccess` (Write and Share), of the rights its own filter already read. There is no
   second probe and no second copy of the rule.

## Owner questions raised by the review (not changed here)

- **No Access removals (N5).** `/no-access/enforce` and the `NoAccessShareReconciliationJob` remove shares on each
  covered record when the entry's AUTHOR holds Write on that record (`NoAccessShareEnforcer`). Should the author also
  need Share there? The covered tables (project, matter, work assignment) do have a Share privilege. Only the ENTRY
  table lacks one.
- **Assigned-To is delegation in effect.** A Write-holder without Share who names an internal user in an "Assigned *"
  column gives that user a Collaborate share, which carries ShareAccess (owner A4: always Collaborate, uncapped). The
  callers that apply the rule include `/assigned-access/sync` (Write only, deliberately), the field-mapping push
  (`FieldMappingEndpoints`), the AI create/update record handlers and `UpdateRecordActionCore`, and the 5-minute job.
  The Share rule does not cover this path. Gating sync would not close it, because the job applies the same rule.
  Options: keep it as policy (A4), or drop ShareAccess from Assigned-To shares.
- **Outside the record set (for completeness).** Playbook `/share` / `/unshare` (owner-only) and direct-message
  thread shares are not project, matter or work assignment access, so this rule does not apply to them.

## Dev role inventory (read-only, spaarkedev1, 2026-10-09)

Read as the operator's `az` login: `EntityDefinitions(...)?$select=Privileges`, `roles` in the root business unit
"Spaarke" (`06fbf21c-1872-f011-b4cb-7c1e52671ad0`), `roleprivilegescollection` per role, and the role holders. Each
child-business-unit copy of a role inherits its root role's privileges. No role, team or user was changed.

### Privileges on the root tables

| Role (root BU) | Project W / S | Matter W / S | Work assignment W / S | No Access entry W |
|---|---|---|---|---|
| **Spaarke Core User** | Deep / **Deep** | Deep / **Deep** | Deep / **Deep** | – |
| **Spaarke Office Add In User** | Deep / **Deep** | Deep / **Deep** | Deep / **Deep** | – |
| Spaarke Basic User | – / – (Read Deep) | – / – (Read Deep) | – / – (Read Deep) | – |
| Spaarke Access Administrator | – / – | – / – | – / – | Global |
| Spaarke Ontology Service | – / – (Read Global) | – / – (Read Global) | – / – (Read Global) | – |
| Spaarke AI Analysis Admin / User, Console User, Ontology Administrator, Provisioning Registry, Reporting Access Admin / Author / Viewer | none on these tables | | | |
| System Administrator, System Customizer | Global / Global | Global / Global | Global / Global | Global |
| Service Writer (platform) | Global / **–** | Global / **–** | Global / **–** | Global |
| Service Reader, Support User (platform) | Read only | Read only | Read only | – |

W = Write, S = Share. Create follows Write in every Spaarke role (Core User and Office Add In User hold Create at
Deep). `sprk_noaccessentry` has no Share privilege to show.

### Who holds the roles that carry Share

| Role | Direct holders | Teams holding it |
|---|---|---|
| Spaarke Core User | # Spaarke Demo Access, # UAC Child BU Test User, Chelsea Friez, Jessica Broyles, Lori Witkin, Ralph Dewey | – |
| Spaarke Office Add In User | # Spaarke Demo Access, Chelsea Friez, Jessica Broyles, Lori Witkin, Ralph Dewey, Ralph Schroeder, **Test User 1** | **Spaarke** (root team), **Spaarke Business Unit 1** |
| Spaarke Basic User | # UAC Child BU Test User, Ralph Schroeder, Test User 1 | Spaarke, Spaarke Business Unit 1 |
| Service Writer | 14 Microsoft platform application users (AI Builder, Power Pages, DV-MetadataService, ...); no person | – |

### What this means

- **Nobody the owner relies on is locked out.** Every Spaarke role that grants Write on project, matter or work
  assignment also grants Share at the same depth (Deep). The only role with Write and no Share is the platform's
  Service Writer, held only by Microsoft application users, which never call the BFF as the caller. So the code
  change alone refuses no current human user anything they can do today.
- **The code change alone does NOT hide Manage Access for testuser1.** testuser1 (Test User 1, systemuser
  `8d7bad7a-…`) holds **Spaarke Office Add In User** directly and through both teams, and that role grants Write and
  Share at Deep on all three tables. The round-89 finding ("testuser1, Spaarke Basic User, could open Manage Access")
  came from that role, not from Basic User (which holds no Write at all). The same holds for every member of the
  `Spaarke` and `Spaarke Business Unit 1` teams.
- **Role changes for the owner to make (the agent made none):**
  1. **Spaarke Office Add In User:** remove `prvSharesprk_Project`, `prvSharesprk_Matter`, `prvSharesprk_WorkAssignment`
     (the add-in does not share records). Consider whether this role needs Create/Write Deep on these tables at all:
     `docs/guides/office-addins-admin-guide.md` §7.5 documents Read only for matter and project.
  2. **Spaarke Core User** keeps Share Deep if its holders should manage access. Remove it for any population that
     should edit but not manage access, or split a "can manage access" role off it.
  3. Remember that Dataverse unions a user's roles, direct and team: removing Share from one role does nothing while
     another role the user or their team holds still grants it.
  4. After any role edit, re-probe at least three times (the privilege cache lags, project gotcha).

## Platform question (researcher, 2026-10-09)

Microsoft Learn (*How access to a record is determined*, *Sharing and assigning*) documents that the privilege check
runs before the record-access check, and that a share cannot give a user rights their roles do not allow. It does NOT
document whether `RetrievePrincipalAccess` itself drops a SHARED `ShareAccess` for a user whose roles grant no Share.
The BFF follows Dataverse's answer either way. The live gate below settles it on dev.

## Live gate (after deploy AND after the owner's role change; owner OK needed for the share write)

**Task 179 is not finished until steps 1 and 2 pass.** The whole rule rests on step 2's platform answer.

1. As testuser1 (`AZURE_CONFIG_DIR=C:/tmp/az-uac-child`), after Share is removed from Office Add In User:
   `GET /api/v1/external-access/can-manage-access?recordType=matter&recordId=<a matter testuser1 can write>`.
   Expect 403 `sdap.access.deny.delegation_share_required`. On the form: no Manage Access icon, no Access flyout.
2. Same caller, a matter shared with testuser1 at **Collaborate** (the share carries ShareAccess). Expect 403 too.
   If it answers 200, `RetrievePrincipalAccess` reports a shared Share without the role privilege. Report that to the
   owner: the rule then needs the table privilege as well (`CallerHoldsPrivilegeAsync`, one more call per request,
   which is the POML's escalation trigger).
3. As a Core User holder: the same request is 200 `canManageAccess: true`.
4. testuser1 saves a project with an Assigned-To change: no warning banner (sync keeps Write); the grant appears.

## Tests

Seeding proof and counts: see the PR and the commit history of this branch.
