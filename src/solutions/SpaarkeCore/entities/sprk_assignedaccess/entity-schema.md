# sprk_assignedaccess Entity Schema

> **Owner**: unified-access-control-r2 task 142 (GitHub #1065) — the Assigned-To auto-grants (owner round 2 item 5 +
> Q5; round 3 A1–A8, R3/R4). **Status**: designed, provisioned by `scripts/Set-AssignedAccessLedgerSchema.ps1`;
> **live deployment PENDING** (manual gate — see Deployment). Dry run against spaarkedev1 2026-10-03: every component
> `WOULD` be created; the table does not exist (`describe('tables/sprk_assignedaccess')` → not found).

**Purpose.** The provenance ledger behind the Assigned-To auto-grants. The invariant owner
(`Services/ExternalAccess/AssignedAccessMaterializer.cs`) gives every contact or organization named in an access-conferring
"Assigned *" column of a project, matter or work assignment Collaborate access — a removable `sprk_externalrecordaccess`
grant, or a POA share when the contact is linked (task 141) to an eligible internal user. The ledger records, per
**(root, source field, subject)**, what it did and what an operator decided, so that:

- an operator's removal **sticks** (`Declined` is never re-created while the assignment persists);
- a manual grant onto an auto subject is **adopted** and never revoked by the rule;
- changing or clearing the field revokes **only** the rule's own, **unmodified** access (owner A4);
- a secure record's assignee is **suggested**, not granted (owner A3 = prompt: `PendingConfirmation`).

**Why a new table (CLAUDE.md §11).** `sprk_externalrecordaccess` cannot carry the provenance: a manual `/grant` upserts
onto the SAME row (one row per subject × root), and a POA share has no row at all. `sprk_userentityassociation` is owned
by the ADR-034 membership pipeline, whose reconciliation DELETES orphan rows exactly when revoke-on-change needs them, and
its writers are flag-off by default. `sprk_noaccessentry` is a veto — a different, stronger statement than "the operator
removed this auto grant" (Declined is NOT a No Access entry: a manual grant still succeeds).

## Entity Definition

| Property | Value |
|---|---|
| Logical name | `sprk_assignedaccess` |
| Entity set | `sprk_assignedaccesses` |
| Display name | Assigned Access |
| Ownership | **Organization-owned** — no user owns a ledger row; only the BFF writes it |
| Primary name | `sprk_name` (Text 200) — readable `{source field} → {contact|organization}:{id}` |
| Activities / Notes | none |
| Solution / publisher | `SpaarkeCore` (Spaarke publisher, `sprk_`) — created with the `MSCRM.SolutionUniqueName` header; never the default publisher |

## Fields

| Column | Type | Meaning |
|---|---|---|
| `sprk_ledgerkey` | Text 200 | **Uniqueness.** BFF-computed `{rootLogicalName}:{rootId}:{sourceField}:{contact|organization}:{subjectId}` (lower-case "D" GUIDs; `AssignedAccessStore.LedgerKey` — the ONE place it is computed). Carries alternate key `sprk_AssignedAccessLedgerKey`. |
| `sprk_sourcefield` | Text 100 | The "Assigned *" column (logical name) that named the subject — or, on an inherited-share row (task 158 r1), `inherited:{parentTable}:{parentId}`: the secure matter / project whose share was passed on to the filed record. |
| `sprk_project` / `sprk_matter` / `sprk_workassignment` | Lookup | The root — exactly one is set (same typed-root shape as `sprk_externalrecordaccess`). Nav props `sprk_Project` / `sprk_Matter` / `sprk_WorkAssignment`. Delete: **Cascade** for `sprk_project`, **RemoveLink** for `sprk_matter` / `sprk_workassignment` (Dataverse allows one cascade-delete parent per table; see "Verify live"). |
| `sprk_subjectcontact` / `sprk_subjectorganization` | Lookup → `contact` / `sprk_organization` | The named subject — exactly one is set. Delete = RemoveLink. |
| `sprk_subjectsystemuser` | Lookup → `systemuser` | The internal user a linked contact represents (a share, a suggested share, or an ineligible link). On an inherited-share row (task 158 r1): the USER a secure parent's share was passed on to. Delete = RemoveLink. |
| `sprk_subjectteam` | Lookup → `team` | **Task 158 r1 (owner round 30).** On an inherited-share row: the TEAM a secure parent's share was passed on to. Nav prop `sprk_SubjectTeam`. Delete = RemoveLink. |
| `sprk_externalrecordaccess` | Lookup → `sprk_externalrecordaccess` | The grant row the rule wrote, raised or found covering. Delete = RemoveLink. |
| `sprk_state` | Choice (local) | `AssignedAccessState` — see below. |
| `sprk_reason` | Text 100 | A stable code (`AssignedAccessReason`): why Skipped (`restricted`, `organization-on-secure`, `no-access`, `ineligible`, `link-unreadable`, …), how an assignment ended (`access-removed`, `kept-other-field`, `kept-modified`, `kept-adopted`, `kept-secure-record`, `prior-level-restored`, `prior-level-restored-lapsed` (the earlier level and date were put back, but that date has passed — no access restored; if the rule's renewal had kept the grant alive past it, the restore ENDED the access), `assignment-ended`), how a removal happened (`removed-by-operator`, `removed-out-of-band`, `removed-by-no-access`, `dismissed`), or what a raise replaced (`raised-from:100000000@2026-10-13` — the earlier level AND the date the subject's access ran until, both put back when the assignment ends, because the rule renews a raised grant while it is assigned; `raised-from-mask:1`). On an inherited-share row (task 158, rule 4) also `share-pending` (written ahead of its share), `covered-by-existing`, `kept-direct`, `kept-other-source`, `kept-last-reader`, `record-unsecured`. |
| `sprk_grantedlevel` | Whole number | The grant level (`10000000x`) or share mask the BFF last wrote — compared to the live row to tell **unmodified** from modified. |
| `sprk_grantedexpiry` | Date Only | The expiry the BFF last wrote (renewal per owner A5 updates it). |

### `sprk_state` (local choice)

| Value | Label | Meaning |
|---|---|---|
| 100000000 | Granted | The rule wrote (or raised) a grant. |
| 100000001 | Shared | The rule wrote (or raised) a POA share to the linked internal user. |
| 100000002 | Covered by existing | Equal or higher access already existed that the rule did not create — nothing written (never lower). |
| 100000003 | Pending confirmation | A SECURE record: suggested in Manage Access (Grant / Dismiss). |
| 100000004 | Skipped | Not written, for `sprk_reason`. Re-evaluated every pass (never sticky). |
| 100000005 | Declined | An operator removed it (or dismissed the suggestion). Sticky while the assignment persists. |
| 100000006 | Adopted | A manual grant/share landed on the subject. Never revoked by the rule. |
| 100000007 | Revoked | The assignment ended; `sprk_reason` says whether access was removed or kept. |

## Business Rules (enforced by the BFF, `AssignedAccessMaterializer`)

1. One row per (root, source field, subject) — the alternate key on `sprk_ledgerkey`. A create that loses a race to the
   key is re-read and updated (`AssignedAccessStore.CreateLedgerAsync`).
2. A row is written only when its values change: an unchanged root costs zero writes (idempotent).
3. Rows are never deleted by the BFF and never deactivated; an ended assignment is `Revoked`, and a later re-assignment of
   the same subject on the same field reuses the row.
4. **Inherited shares (task 158 r1, owner round 30 — `SecureRootInheritance`, not the materializer).** One row per (filed
   secure work assignment / project, secure parent, principal), key
   `{root}:{rootId}:inherited:{parentTable}:{parentId}:{systemuser|team}:{id}`. `Shared` = the rule wrote or raised the
   share (`sprk_grantedlevel` = the mask written; `raised-from-mask:N` = what it held before); `Covered by existing` = the
   principal already held the parent's mirror there (direct access, never removed by the rule); `Declined` = an operator
   removed it on the filed record (`/unshare-user`, or found removed / narrowed outside the BFF — `removed-out-of-band`):
   never re-added while the parent share persists; `Adopted` = an operator shared it on the filed record. On the parent's
   unshare only an UNMODIFIED `Shared` share is removed (put back to `raised-from-mask` when it raised one), unless
   another provenance justifies it (`kept-other-field`, `kept-other-source`); the row ends `Revoked`. The materializer
   ignores these rows (they name no contact or organization); its operator markers (`MarkShareRemovedAsync` /
   `MarkShareAdoptedAsync`) apply to them by user.
   **Write-ahead (task 158 r1c-v1):** a row is written BEFORE its share — `Shared`, reason `share-pending` (then
   `;raised-from-mask:N`), `sprk_grantedlevel` = the mask about to be written — and confirmed (the marker dropped) once
   the share reads back; no share is ever written without its row. A `share-pending` row whose share is not in place is
   written again by the next pass, never read as a removal. A create that loses the alternate-key race writes NOTHING over
   the row (`AssignedAccessStore.CreateInheritedLedgerAsync` answers `null`; the pass is retried) — unlike rule 1's
   Assigned-To rows. The record's LAST reader is kept with its row live (`Shared`, reason `kept-last-reader`, S5) and
   removed by a later pass once someone else can open the record. `Skipped` + `removed-by-no-access` = the share was
   removed by task 143's No Access enforcer (the person is on the filed record's list): passed on again once the wall is
   lifted, never `Declined`.

## Security

- **Only the BFF application user writes** (it holds System Administrator). No Spaarke role is granted Create, Write or
  Delete on the table; `-Verify` FAILS if any role other than System Administrator / System Customizer holds one.
- **No user reads it directly**: Manage Access reads it through `GET /api/v1/external-access/assigned-access`, gated by
  `DelegationRuleFilter` (Write on the record). So no Spaarke role is granted Read either.

## BFF API Integration

| Component | Use |
|---|---|
| `Services/ExternalAccess/AssignedAccessStore.cs` | Reads (`ReadLedgerAsync`) and writes (`CreateLedgerAsync`, `UpdateLedgerAsync`) the ledger over `DataverseWebApiClient`; `LedgerSelect` names every column above. |
| `Services/ExternalAccess/AssignedAccessMaterializer.cs` | The invariant owner (decisions + the operator markers). |
| `Services/ExternalAccess/AssignedAccessReconciliationJob.cs` | Scans roots with a live ledger row (`statecode eq 0 and sprk_state ne 100000007`). |
| `Api/ExternalAccess/AssignedAccessSyncEndpoint.cs` | `POST …/assigned-access/sync`, `GET …/assigned-access`, `POST …/assigned-access/dismiss`. |

## Deployment

### ⚠️ Order — binding

1. `pwsh scripts/Set-AssignedAccessLedgerSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry run).
2. `… -Apply`, then `… -Verify` (exit 0) and `mcp describe('tables/sprk_assignedaccess')`.
3. Only then deploy a BFF carrying task 142. Without the table every materialization reads `ledger-unreadable` and writes
   NOTHING (fail closed — no existing access is lost, but no auto-grant is made). A BFF carrying task 158 r1 also needs
   `sprk_subjectteam` (the same script adds it): without it every `/share-user` / `/unshare-user` fan-out to filed secure
   records answers `children_incomplete` and passes nothing on (fail closed).

### Verify live before relying on it

- `sprk_AssignedAccessLedgerKey` index status **Active** (the script waits for it).
- Root relationships: only `sprk_project` takes `Delete = Cascade`. **Dataverse refused a second one** on dev
  (2026-10-06, 0x80047007 "sprk_assignedaccess is parented to sprk_project. Cannot create another parental relation
  with sprk_Matter"): a table may have one cascade-delete parent. So `sprk_matter` and `sprk_workassignment` use
  `RemoveLink`. A deleted matter or work assignment leaves its ledger rows with the root cleared, and the job ignores
  them because the root reads as not found.
- The privilege census in step (f) allows System Administrator / System Customizer for Create/Write/Delete, plus
  the Microsoft platform roles `Service Writer` / `Service Deleter`, which Dataverse grants on every new table. Those
  two pass only while every holder is an application user (no person, no team); the census reads the holders, and a
  failed read fails.

### Deployment record

| Date | Environment | Step | Result |
|---|---|---|---|
| 2026-10-03 | spaarkedev1 | Dry run (read-only) | Every component WOULD be created; nothing written |
| — | spaarkedev1 | `-Apply` / `-Verify` | **PENDING** (manual gate, main session) |
