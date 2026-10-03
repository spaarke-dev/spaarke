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
| `sprk_sourcefield` | Text 100 | The "Assigned *" column (logical name) that named the subject. |
| `sprk_project` / `sprk_matter` / `sprk_workassignment` | Lookup | The root — exactly one is set (same typed-root shape as `sprk_externalrecordaccess`). Nav props `sprk_Project` / `sprk_Matter` / `sprk_WorkAssignment`. Delete = **Cascade** (a deleted root takes its ledger). |
| `sprk_subjectcontact` / `sprk_subjectorganization` | Lookup → `contact` / `sprk_organization` | The named subject — exactly one is set. Delete = RemoveLink. |
| `sprk_subjectsystemuser` | Lookup → `systemuser` | The internal user a linked contact represents (a share, a suggested share, or an ineligible link). Delete = RemoveLink. |
| `sprk_externalrecordaccess` | Lookup → `sprk_externalrecordaccess` | The grant row the rule wrote, raised or found covering. Delete = RemoveLink. |
| `sprk_state` | Choice (local) | `AssignedAccessState` — see below. |
| `sprk_reason` | Text 100 | A stable code (`AssignedAccessReason`): why Skipped (`restricted`, `organization-on-secure`, `no-access`, `ineligible`, `link-unreadable`, …), how an assignment ended (`access-removed`, `kept-other-field`, `kept-modified`, `kept-adopted`, `kept-secure-record`, `prior-level-restored`, `assignment-ended`), how a removal happened (`removed-by-operator`, `removed-out-of-band`, `removed-by-no-access`, `dismissed`), or what a raise replaced (`raised-from:100000000`, `raised-from-mask:1`). |
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
   NOTHING (fail closed — no existing access is lost, but no auto-grant is made).

### Verify live before relying on it

- `sprk_AssignedAccessLedgerKey` index status **Active** (the script waits for it).
- The three root relationships accept `Delete = Cascade` with the other cascades `NoCascade` (a non-parental custom
  cascade; if Dataverse refuses it, fall back to `RemoveLink` and note it here — the ledger then keeps rows of a deleted
  root, which the job ignores because the root reads as not found).
- The privilege census in step (f) lists only System Administrator / System Customizer for Create/Write/Delete.

### Deployment record

| Date | Environment | Step | Result |
|---|---|---|---|
| 2026-10-03 | spaarkedev1 | Dry run (read-only) | Every component WOULD be created; nothing written |
| — | spaarkedev1 | `-Apply` / `-Verify` | **PENDING** (manual gate, main session) |
