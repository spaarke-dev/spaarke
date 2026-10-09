# Task 046 — server-side work-assignment create: parity table and escalation

> **2026-10-07, second pass — owner decision D-59 (option A): BUILT, live-proven; merge waits on uac-r2.** §0 is the
> execution record. Sections 1-4 below are the first pass's escalation record, kept unchanged (its §2 parity table is
> corrected by §0.3 where the live proof showed otherwise).

## 0. Execution record (D-59)

### 0.1 What was built, and one deviation from D-59's letter

| File | What |
|---|---|
| `Services/WorkAssignments/WorkAssignmentCreateService.cs` (new) | The create: maps the wizard's Web API payload AS THE CALLER, then `OwnedChildWrite.CreateAsync(…, SecureRootFilingGate)` (G5: Create/Append privilege, **AppendTo on every bound record — the regarding record every time**, no owner/audit/creator/field-secured column; app-only create owned by the resolver's team; `sprk_createdbyperson` = caller; the I-13 L1 plan — created INTO isolation under a secure parent), `CompleteIsolatedCreateAsync` for an isolated row, then the I-12 Assigned-To materializer. Refuses a nameless payload and a root's server-owned columns (`sprk_containerid`, `sprk_issecure`, `sprk_securitybu`). Callable in-process (task 043). |
| `Api/WorkAssignments/WorkAssignmentEndpoints.cs` (new) | `POST /api/v1/record-creation/workassignment` — **PROVISIONAL name** (uac-r2 asked to name it, #1355). Unresolved caller → the single 403 `sdap.access.deny.caller_unresolved`; 201 `{id, isolated, warnings}`; refusals as ProblemDetails with `reasonCode`. |
| `Infrastructure/DI/SignalsModule.cs`, `EndpointMappingExtensions.cs` | One unconditional scoped registration; one mapping line (§F.1). |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` (uac-r2's) | GovernedFile + HandlerDecision (`IDataverseUserClient` via one hop), census 117 → 118. Minimal. |
| `CreateWorkAssignmentWizard/workAssignmentService.ts` | The SAME payload is POSTed to the route instead of `Xrm.WebApi.createRecord`; server warnings carried; client `syncAssignedAccess` dropped for this create (the server materializes inline); UI unchanged. A pre-existing unreachable `else if` (eslint `no-dupe-else-if`) removed. |
| `utils/adapters/bffChildWriteAdapter.ts` (uac-r2's) | `failureOf` exported (one word) so the wizard reuses its ProblemDetails reader. |

**Deviation from D-59's letter (recorded, raised on #1355):** D-59 says "build it on `RecordCreationService`". That was
my own first-pass recommendation, written before I found that **`OwnedChildWrite.CreateAsync` with the secure-create
plan already creates work-assignment roots** (the chat `dataverse.create_record` tool, `DataverseCreateRecordHandler.cs:341-373`).
A work-assignment path in `RecordCreationService` would be a **third** root-create for the same table — a parallel
mechanism the coordination constraint forbids — and would need a typed request plus a server re-implementation of the
ADR-024 resolver fields the wizard already builds. The service therefore runs the existing core in the chat tool's order.
Everything D-59 names holds: uac-r2's pattern, team-owned, creator stamped, secured at create, AppendTo on the parent every
time, under uac-r2's review. If the owner or uac-r2 want `RecordCreationService` regardless, the service is the only file
that changes.

### 0.2 Live proof (spaarkedev1, 2026-10-08 ~00:49 UTC) — `WorkAssignmentCreateLiveTests` (opt-in)

The REAL route in-process against spaarkedev1 (harness pattern of 097's `EventRoutesLiveTests`; substituted: the inbound
token carries the acting user's real oid; `IDataverseUserClient` uses the operator's az token + `MSCRMCallerID` for another
user, with the production error mapping; outbound identity = AzureCliCredential). Browser leg = the same payload POSTed to
`sprk_workassignments` as the operator (what `Xrm.WebApi.createRecord` sends).

| Leg | Result |
|---|---|
| Operator (System Administrator, root BU), matter CMRCL-441482 (BU1): browser path | created `63baf70e…` |
| Same payload through `POST /api/v1/record-creation/workassignment` | **201** `3aa859b9…`, isolated false, no warnings |
| Field by field (17 columns: name, priority, description, response due date, search index name, AI search index, regarding matter, record type, record id/name/number/url, matter type, practice area, container id (null on both), state, status) | **all equal** |
| Owner | browser = the user `1d02f31c…`; server = BU1's default team `cf15f587…` (I-6, record-first) — intended |
| Owning BU | browser = root `06fbf21c…`; server = BU1 `cb15f587…` — intended |
| Creator person | browser = null; server = the operator — intended |
| `# UAC Child BU Test User` (BU1, holds Create+Append on WA, **no access** to root-BU matter EMPL-847770) | **404** `work_assignment.not_found`; zz-046 count unchanged |
| Cleanup | 2 of 2 deleted; **0 zz-046 work assignments remain** |

**Secure-parent leg: not run live.** Completing an isolated create runs provisioning (its own SPE container via Graph,
the creator share), which the in-process host cannot do with the operator's credential and fake Graph settings, and the
only secure parent in dev is uac-r2's live test project `31e232ae…` (their owner is testing on it now). Proving it needs
a dev BFF deploy of this branch after uac-r2's review — an Azure change for the owner to approve. The isolated path is
the chat tool's, covered by uac-r2's `SecureRootInheritanceWriterTests`; this service passes the same gate to the same core.

### 0.3 Corrections to the first-pass parity table (§2)

- **Rows 5-6 (BU search defaults): ✅ parity**, not ⚠️ — the wizard puts them in the payload from the user's BU, and the
  server writes the payload as given (live: both rows `spaarke-files-index` / `dd04e55f…`).
- **Row 14 (owning BU)**: the server owner is the **regarding record's** business-unit team (I-6 record-first), the
  caller's only when the record is unfiled — as measured live.
- **Row 17**: AppendTo on every bound record is checked by the core for ordinary filings too (the project path in
  `RecordCreationService` does it only under a secure parent) — so D-59's "every time" holds without new code.

### 0.4 Defect found: uac-r2's G5 core refused every ADR-024 payload (fixed in PR #1391)

The first live run was **404 for a System Administrator**. `OwnedChildWrite.CheckCallerMayAppendToAsync` asks
`RetrievePrincipalAccess` for every bound lookup; on an ORGANIZATION-OWNED table (`sprk_recordtype_ref`,
`sprk_mattertype_ref`, `sprk_practicearea_ref`) Dataverse answers `400 0x80040800 … does not support entities of type
'sprk_recordtype_ref'`, read as no rights → uniform 404. Every ADR-024 resolver payload binds `sprk_RegardingRecordType`,
so **uac-r2's own `POST /api/v1/child-records/{table}` refuses the Create To Do / Event / Invoice / Report Card wizards'
payloads whenever the record is filed under something** — live on dev (master `dc189faac`). Measured through the real route:
the same `zz-046` to-do is **201** without the record-type lookup, **404** with it, and **201** with the fix.
Fix: an org-owned target is allowed when the caller holds its AppendTo **privilege** and can read the row (what a
run-as-user create checks); otherwise the uniform 404 stands. The harness answered RetrievePrincipalAccess for every
table (why it slipped) — now it refuses org-owned tables as live does. **PR #1391** to master
(`fix/g5-appendto-org-owned-reference`, heads `1e8b27b9e` + `c23ba5032`), uac-r2 to review; **not merged**. Cherry-picked
onto this branch (`d0f1fb469` + the guard commit) because task 046 cannot work without it. Reported on #1355.

### 0.5 Gates

- Tests: 7 cases in `SecureChildOwnershipAiToolTests.WorkAssignmentCreate.cs` (created team-owned with the creator stamp and
  every wizard field; no AppendTo on the regarding matter = the same 404 as a missing matter; no Create privilege 403; no
  name 400; server-owned root column 400 ×3; unreadable parent flag 409; unresolved caller the single 403 before any
  Dataverse call) + 3 jest cases (route + no `Xrm.WebApi`; refusal message; warnings → partial) + the live test.
- Code review (Step 9.5): findings 1 (server-owned root columns honoured on an app-only create — F1, fixed), 2 (non-string
  `OwnershipType` would throw in the fix — fixed), 3 (stale header comment — fixed); known limits: K — the CRUD service uses
  `OwnedChildWrite`/`DataverseWriteItemMapper` (namespace `Services/Ai/Handlers/Dataverse`) and the endpoint
  `ICallerSystemUserResolver` (`Services/Ai/Context`) — Dataverse write/identity cores, not AI capability, the same placement
  uac-r2's `ChildRecordEndpoints`/`EventEndpoints` use (**ADR-013 tension, proposed path A**, for the main session to log in
  spec §6 if it agrees); K4 — `Created` location has no GET; K — the live test copies two small harness classes from 097's.
- ADR check: no violations; ADR-013 warning as above; ADR-010 low (registered in this project's SignalsModule).
- Arch tests (route/ownership/secure-child subset): 125/125. Full unit suite, publish size, CVE: §0.6.

### 0.6 Measurements

- **Publish size** (`.claude/rules/bff-hygiene.md`): three FRESH worktrees at short paths, `dotnet restore` +
  `dotnet publish -c Release` (the `Deploy-BffApi.ps1` commands), zipped with **PowerShell `Compress-Archive`**, PDBs as
  published. **192 files on all three sides.**

  | Side | Commit | Zip |
  |---|---|---|
  | master (fresh) | `ccde3eb6d` | 36.22 MB (37,978,933 B) |
  | this branch's merge-base with the ontology branch | `0977c274d` | 36.23 MB (37,994,147 B) |
  | this branch | `27a361c68` | 36.24 MB (38,001,292 B) |

  **Task 046's own delta: +7,145 B (+0.007 MB)** (branch vs its merge-base); +0.02 MB vs master, which also includes the
  ontology branch's other unmerged work. Ceiling 60 MB: far below. (The figures are lower than root CLAUDE.md's recorded
  45.42 MB baseline; the rule is to compare fresh builds, which these are, with equal file counts.)
- **CVE**: `dotnet list package --vulnerable --include-transitive` → "no vulnerable packages"; no `.csproj` changed.
- **Full BFF unit suite**: **18,782 passed, 0 failed, 54 skipped** (34 min; contended with other streams' suites).
- **Full ArchTests**: **818/818** (built explicitly).
- **UI package**: `npm run build` (tsc) exit 0; eslint clean on the changed files; jest 79/79 for the wizard + adapter.

## First pass (escalation record, unchanged)

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
