# Task 046 — server-side work-assignment create: parity table and escalation

> **Date**: 2026-10-07 · stream C2 · **Status**: 🔔 **STOPPED at both escalation triggers — no code written.**
> The POML requires the parity table to be reviewed by the work-assignment area owner **before** any code (constraint 1-2,
> step 1), and it says to stop if uac-r2's code has changed in a way that invalidates the plan. Both apply:
> (1) no area owner is named (see `047-work-assignment-response-columns.md` §1) and none could review this;
> (2) since the POML was written, uac-r2 has (a) **retired** the old create route with a regression test that pins its
> absence, and (b) shipped the server's **one** root-create pattern, whose **ownership model differs** from the
> wizard's. The POML's goal ("same owner and business unit as the wizard") and that pattern cannot both hold.

## 1. uac-r2 re-read — `origin/master` @ `0ea74d1c3`

Same files and commits as `047-work-assignment-response-columns.md` §2, plus what this task depends on:

| File / mechanism | Commit | What it means for 046 |
|---|---|---|
| `Infrastructure/DI/EndpointMappingExtensions.cs:230` | `d254d7166` | `MapWorkAssignmentEndpoints()` **removed** (uac-r2 task 166, S-76): the old `POST /api/v1/work-assignments` created app-only with a caller-chosen `ownerid`, checked no Create/AppendTo, and had no caller. "Work assignments are created through the MDA / the Create Work Assignment wizard." |
| `tests/integration/regression/RouteAuthorization/DeadRouteRetirementTests.cs` | `d254d7166` | Pins **`POST /api/v1/work-assignments` → 404**. A new create route must not reuse that path without uac-r2 agreeing to retire the assertion. |
| `Services/Office/RecordCreationService.cs` | `d254d7166` | The server's **root** create (matter, project): G5 caller checks through `CallerRecordAccessProbe` (Create privilege + AppendTo on every secure parent), **app-only create owned by the BU default owner team** (task 080, owner decision 2026-09-22, via `IRecordOwnershipResolver`), `sprk_createdbyperson` = caller (task 133), BU defaults, server field mapping, `SecureRootFilingGate.PlanCreateAsync` (I-13 L1: a root filed under a secure parent is created **into isolation**, else the row is deleted), and `AssignedAccessMaterializer` inline (I-12 L1). |
| `Services/Access/SecureRootFilingGate.cs`, `SecureRootInheritance.cs` (`PlanCreateAsync`, `SecureAfterWriteAsync`), `SecureRootInheritanceJob` | `d254d7166` | I-13: a work assignment filed under a secure matter/project **is a secure root**. Browser creates are caught by the 5-minute job (L4); BFF creates must plan before the write (L1). |
| `Services/Ai/Handlers/Dataverse/OwnedChildWrite.cs`, `Api/ChildRecordEndpoints.cs`, client `bffChildWriteAdapter.ts` | `d254d7166` | The ONE client seam for **child** creates (`POST /api/v1/child-records/{table}`); `sprk_workassignment` is a **root**, deliberately not in `BFF_CHILD_CREATE_TABLES`. |
| `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §4.3, §5 I-2/I-12/I-13 | master | §4.3: "typed BFF create endpoints per invariant-bearing table **(via `RecordCreationService`)**, then switch each wizard". `workAssignmentService.ts` is listed as a client-only create path that bypasses server invariants. |

Open PRs (2026-10-07): none touches these files or the wizard (`#1372` touches one child-record test).
uac-r2 worktree `8bf068828`: batch 5 (tasks 154/113/105/101/064/153/067/099/036/090) — no work-assignment create work queued.

## 2. Field-by-field parity table (wizard → a server create on uac-r2's root pattern)

Wizard = `CreateWorkAssignmentWizard/workAssignmentService.ts` `createWorkAssignment` @ master (`:373-532`), a browser
`Xrm.WebApi` create **as the user**. "Server" = what a `RecordCreationService`-style create would write.

| # | Column / effect | Wizard (today) | Server create on uac-r2's pattern | Parity |
|---|---|---|---|---|
| 1 | `sprk_name` | `form.name.trim()` (required) | same | ✅ |
| 2 | `sprk_priority` | `form.priority` | same | ✅ |
| 3 | `sprk_description` | trimmed, when present | same | ✅ |
| 4 | `sprk_responseduedate` (Date Only) | `form.responseDueDate` | same (`yyyy-MM-dd`, no zone shift) | ✅ |
| 5 | `sprk_searchindexname` | the **user's** BU `sprk_searchindexname`, unless set (INV-5) | the BU cascade server-side — BU read from the **target record first, the user's second** (`RecordCreationService.ApplyBusinessUnitDefaultsAsync`, task 080 refinement 2026-09-25) | ⚠️ differs when the user's BU ≠ the regarding record's BU |
| 6 | `sprk_ai_search_index` (lookup) | the user's BU `_sprk_ai_search_index_value` | same source order as #5 | ⚠️ as #5 |
| 7 | `sprk_containerid` | **not written** (task 076 W1) | not written | ✅ |
| 8 | Regarding: typed `sprk_regarding{matter,project,invoice,event}` | `applyResolverFields` | server mirror `TodoRegardingBuilder` pattern (ADR-024) | ✅ (communication is not offered by the wizard; unchanged) |
| 9 | ADR-024 resolver fields `sprk_regardingrecordtype` (→ `sprk_recordtype_ref`), `…recordid`, `…recordname`, `…recordnumber`, `…recordurl` | `applyResolverFields` | same five | ✅ |
| 10 | Field mapping `{parent → sprk_workassignment}` | client `applyFieldMappings`, after #8-9 | server field mapping (as `RecordCreationService` does for matter/project) | ✅ (server reverts a rule that writes `ownerid` / blanks the name) |
| 11 | `sprk_mattertype`, `sprk_practicearea` | from the form | same | ✅ |
| 12 | Assign Work lookups `sprk_assignedattorney1`, `…paralegal1`, `…lawfirm1`, `…lawfirmattorney1` | `bindLookup` by hint | same columns | ✅ |
| 13 | **`ownerid`** | **not set → the calling USER** | **the BU default owner TEAM** (task 080), or the named *Secure Record Owners* team under a secure parent (I-2/I-13) | ❌ **differs by design** |
| 14 | **`owningbusinessunit`** | **the user's BU** | derived from the owner team: the **target record's** BU first, the user's second | ❌ differs when the BUs differ |
| 15 | `sprk_createdbyperson` | **not set** | the caller (task 133) | ❌ server adds it (needed by I-13's "recorded creator") |
| 16 | Secure parent (I-13) | created **user-owned and visible to the user's BU**, secured by the 5-minute job | planned before the write; created **into isolation** (`sprk_issecure` in the create, named team), creator shared, or deleted and refused | ❌ server closes a ≤5-minute exposure window |
| 17 | Caller checks | Dataverse's own (Create on WA, AppendTo on each bound record) because it runs as the user | G5 through `CallerRecordAccessProbe` before an app-only write (Create + AppendTo on secure parents) | ⚠️ the project path (`CallerMayCreateUnderAsync`, `RecordCreationService.cs:~905`) checks Create **and** AppendTo **only when the plan finds secure parents**; the Create privilege is otherwise asked at the route by `QuickCreateSourceAccessFilter`. A work-assignment route needs the same pair: Create at the route, and AppendTo on the regarding record **always** (secure or not), for the POML's negative criterion |
| 18 | Assigned-To access (I-12) | `syncAssignedAccess` → `POST /api/v1/external-access/assigned-access/sync` after create | `AssignedAccessMaterializer` inline (L1) | ✅ same effect |
| 19 | Files, document records, indexing, follow-on event, email | separate wizard steps after the create | unchanged (stay in the wizard; out of scope) | ✅ |

**Rows 13-16 are the parity question.** A server create that reproduces the wizard exactly (user-owned, user's BU, no
creator stamp, secured only by the job) would be a **second root-create implementation with the opposite ownership rule**
from `RecordCreationService` — the "parallel mechanism" the coordination constraint forbids — and would reproduce the
exposure window I-13 L1 exists to close. A server create that follows uac-r2's pattern **cannot** meet the POML's
acceptance criterion 1 ("every field matches, including owner and owning business unit").

## 3. Escalation

🔔 **Human Input Required — task 046** (triggers 1 and 2)

- **Situation**: (a) the parity table above has no reviewer — no work-assignment area owner is named; (b) uac-r2 has since
  retired `POST /api/v1/work-assignments` (test-pinned) and made `RecordCreationService` the server's root-create pattern
  with **team** ownership, a creator stamp and create-into-isolation; the POML's "same owner and business unit as the
  wizard" contradicts that pattern (rows 13-16).
- **Options**:
  - **A (recommended)** — Re-scope 046 to *"extend `RecordCreationService` with a work-assignment create"* (uac-r2's
    pattern, uac-r2 reviews), accept rows 13-16 as **intended differences** (team owner, target-first BU, creator stamp,
    into-isolation create), add the AppendTo check on a non-secure regarding record (row 17), and amend acceptance
    criterion 1 to "matches the parity table's ✅ rows; the ❌ rows follow the server's root rule". The route is a new
    path uac-r2 names (not the retired `POST /api/v1/work-assignments`, which their test pins to 404), with its census
    entry added through uac-r2's review of the ledger. The wizard switch then also changes its records' owner from user to team —
    a visible change the owner should accept.
  - **B** — Keep the POML literal: an as-the-caller (`IDataverseUserClient`) create producing a user-owned row, plus
    `SecureRootInheritance.SecureAfterWriteAsync` after the write. Contradicts the owner's 2026-09-22 ownership decision
    and duplicates the root-create path; needs uac-r2 to accept a second pattern.
  - **C** — Drop D-21 from R1: keep v4-prototype-vs-solution #31's original recommendation (open the existing wizard
    **after** recording; Assign Work is not a Next step inside the commit). Task 043 then lists no work-assignment id.
- **Who decides**: the project owner (as area owner) for the ownership change; uac-r2 for reusing `RecordCreationService`,
  the route name, the ledger entry and the retired-route test.
- **Ready-to-post request to uac-r2**:
  > ontology-r1 task 046 (D-21) needs a server-side `sprk_workassignment` create so a decision can create one in-process.
  > Your task 166 retired `POST /api/v1/work-assignments` and `RecordCreationService` is now the root-create pattern. We
  > propose adding a work-assignment create to `RecordCreationService` (G5 + AppendTo on the regarding record,
  > team owner, `sprk_createdbyperson`, `SecureRootFilingGate.PlanCreateAsync`, `AssignedAccessMaterializer`), on a new
  > route you name, with a ledger entry you review. Parity table: `projects/spaarke-ontology-platform-r1/notes/046-work-assignment-parity.md`.
  > Do you agree, and which route name?

## 4. Not done (and why)

No `WorkAssignmentCreateService.cs`, no endpoint, no ledger edit, no wizard switch, no live proof, no publish-size
measurement: every one of them follows the parity review the POML puts first, and the plan they implement is the one
the escalation questions. No `zz-046-` records were created.
