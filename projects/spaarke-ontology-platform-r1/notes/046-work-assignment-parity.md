# Task 046 — server-side work-assignment create: parity table and execution record

> **2026-10-10, third pass — D-113 (uac-r2's answer on #1355, comment 6089838200): BUILT on uac-r2's child-records
> route, live parity PROVEN on spaarkedev1, awaiting the two reviews.** §A–§F below are this pass. The second pass (D-59:
> a separate `WorkAssignmentCreateService` + `POST /api/v1/record-creation/workassignment`, branch
> `stream/c2-047-046`, never merged) and the first pass (escalation) follow unchanged as history; **D-113 supersedes
> both**: no new service, no new route, no new ledger entry.

## A. uac-r2 re-read (before starting; project branch `origin/docs/ontology-platform-design` @ `48742f4d2`, master @ `b4faf6222`)

| File | Last commit (same on master unless noted) | What 046 relies on |
|---|---|---|
| `Services/Dataverse/OwnedChildWrite.cs` | `da20ca7bf` (task 048 move, #1555) | `CreateAsync` (G5 as the caller → plan → app-only create owned by the resolver's team, creator stamped); the #1391 org-owned AppendTo fix is in |
| `Api/ChildRecordEndpoints.cs` | `da20ca7bf` | the route; `CreateTables`; it passed **no** secure-create plan (a work assignment under a secure parent would have been refused 409 `SecureFilingRefused`) |
| `Services/Access/SecureRootFilingGate.cs`, `SecureRootInheritance*.cs` | `61b0aa77a` (task 175) | `PlanCreateAsync` (refuses caller-supplied `sprk_issecure` / `sprk_accessinheritance`), `CompleteIsolatedCreateAsync` |
| `Services/Dataverse/RecordOwnershipResolver.cs` (I-6) | `d254d7166` | record-first owner team |
| `Services/Access/SecureChildLineage.cs`, `config/secure-record-owner-role.json` | branch `b2fc3fb78`, master `0615f9781` | untouched (a work assignment is a root, not a lineage child) |
| `Api/Filters/RecordRouteAccessAuthorizationFilter.cs` | `c5f71487f` | not on this route (handler-level decision, ledger `RouteLevelGate`) |
| `Services/Dataverse/CoreAncestorResolver.cs` | `d7fdcafc3` | the restamper is a no-op for a root |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | branch `48742f4d2`, master `d5e1dffe7` | the route is already in the census; **only** its HandlerDecision line references were refreshed |
| `Services/ExternalAccess/AssignedAccessMaterializer.cs` | `e78c47149` | `RunAfterWriteAsync` (I-12 L1) |
| client `utils/adapters/bffChildWriteAdapter.ts` | `d254d7166` | the one browser seam |

Open PRs touching these files: **#1501** (`fix/136-create-analysis-hub-404`, ChildRecordEndpoints/OwnedChildWrite) — deferred per
D-113, rebases later; soft overlap only. uac-r2's session (`bf82fa958`) is merging 175/176/180; no work-assignment create
work queued. **Plan not invalidated** — the D-113 shape works with one gap closed here (the route did not pass the plan).

## B. Parity table — wizard (browser) vs `POST /api/v1/child-records/sprk_workassignment`

Wizard = `CreateWorkAssignmentWizard/workAssignmentService.ts` `createWorkAssignment`. The wizard keeps building the SAME
payload (cascade, resolver fields, field mapping) client-side; only where it is sent changes. **Live** = proven on
spaarkedev1 2026-10-10 (§C).

| # | Column / effect | Wizard (before) | Route (after) | Parity | Live |
|---|---|---|---|---|---|
| 1 | `sprk_name` | `form.name.trim()` | as sent | ✅ | ✅ |
| 2 | `sprk_priority` | form | as sent | ✅ | ✅ |
| 3 | `sprk_description` | trimmed, when present | as sent | ✅ | ✅ |
| 4 | `sprk_responseduedate` (Date Only) | form | as sent | ✅ | ✅ |
| 5 | `sprk_searchindexname` (BU cascade, FR-WIZ-04, INV-5) | the **user's** BU value | as sent (built by the wizard) | ✅ | ✅ |
| 6 | `sprk_ai_search_index` lookup (BU cascade) | the user's BU index | as sent | ✅ | ✅ (operator leg) |
| 7 | `sprk_containerid` | not written (task 076 W1) | not written; **refused if sent** (403 — provisioning's column, not field-secured) | ✅ | ✅ null on both |
| 8 | typed regarding `sprk_regarding{matter,project,invoice,event}` | `applyResolverFields` | as sent; **AppendTo asked as the caller** | ✅ | ✅ |
| 9 | ADR-024 fields: `sprk_regardingrecordtype` (→ `sprk_recordtype_ref`), `…recordid`, `…recordname`, `…recordnumber`, `…recordurl` | `applyResolverFields` | as sent | ✅ | ✅ |
| 10 | field mapping `{parent → sprk_workassignment}` (live: `sprk_assignedattorney1`) | `applyFieldMappings` | as sent | ✅ | ✅ |
| 11 | `sprk_mattertype`, `sprk_practicearea` | form | as sent | ✅ | ✅ |
| 12 | Assign Work lookups (`…attorney1`, `…paralegal1`, `…lawfirm1`, `…lawfirmattorney1`) | `bindLookup` | as sent | ✅ | (not in the live payload; unit-tested) |
| 13 | `sprk_issecure`, `sprk_accesspermission`, `sprk_accessinheritance` (D-113) | not sent | not sent; `sprk_issecure`/`sprk_accessinheritance` refused if sent (403, field-secured + plan) | ✅ | ✅ equal (accesspermission 100000000; access record identical once the job ran) |
| 14 | **`ownerid`** | **the user** | **the regarding record's BU default team** (I-6), or the named Secure Record Owners team under a secure parent | ❌ **intended (D-113 / G5)** | ✅ as stated |
| 15 | **`owningbusinessunit`** | the user's BU | derived from the owner team (record first) | ✅ when the user's BU = the record's BU; ❌ intended otherwise | ✅ equal for the BU1 user; root-BU operator → BU1 (intended) |
| 16 | `sprk_createdbyperson` | not set | the caller | ❌ intended (task 133) | ✅ |
| 17 | `createdby` / `modifiedby` / `timezoneruleversionnumber` | the user | the application (BFF identity) | ❌ intended (app-only create) | ✅ |
| 18 | caller checks | Dataverse's own (Create; AppendTo on every bound record) | the same questions asked **as the caller** before the app-only write | ✅ | ✅ both refuse (§C) |
| 19 | secure parent (I-13) | created user-owned, secured by the ≤ 5-min job | created **INTO isolation** in the request (plan + `CompleteIsolatedCreateAsync`) | ❌ intended — closes the exposure window | unit/fixture-tested only (§D) |
| 20 | Assigned-To access (I-12) | client `syncAssignedAccess` → `/assigned-access/sync` | `AssignedAccessMaterializer` inline in the route; the client call is dropped | ✅ same materializer | harness could not authorise its secure-flag read (§C, K) — job is the safety net either way |
| 21 | the chat tool's "for person" default (`sprk_assignedtointernal` = caller's contact) | not set | **not added** by the route | ✅ | ✅ null on both |
| 22 | files, documents, indexing, follow-on event, email | later wizard steps | unchanged | ✅ | n/a |
| 23 | UI | — | unchanged; a server warning (secure create not finished) is shown as a "partial" result like any other warning | ✅ | jest |

**Area-owner review: PENDING.** Rows 14–17 and 19 are the differences the owner has to accept (they are uac-r2's G5 rule,
mandated by D-113). If the work-assignment area owner rejects any of them, the POML's escalation trigger fires — stop.
Reviewer: _(name, date, decision — to be recorded here by the main session)_.

## C. Live proof — spaarkedev1, 2026-10-10 15:5x UTC (`WorkAssignmentCreateLiveTests`, opt-in)

The REAL route in-process (`ChildRecordEndpoints.CreateAsync` via HTTP, real G5 core, resolver, gate, restamper, app-only
SDK + Web API clients) against spaarkedev1. Substitutions: inbound token (the acting user's real oid); the caller's
Dataverse = operator token + `MSCRMCallerID`, **and WhoAmI answered with the acting user** (Dataverse's WhoAmI ignores
`MSCRMCallerID` — found this pass: it made the second pass's denied-leg 404 untrustworthy evidence); outbound identity =
AzureCliCredential. Browser leg = the wizard's exact payload POSTed to `sprk_workassignments` as the acting user. Both
rows read **in full** and compared column by column after the environment's secure-root job wrote its access record on
both (243 s).

| Leg | Result |
|---|---|
| Operator (System Administrator, root BU), matter CMRCL-441482 (BU1, ordinary) | browser `d7adef07…` / route **201** `3cef8bf5…`; **59 columns, 51 equal, 0 unexpected** (differences: rows 14–17 + ids/timestamps/version) |
| `# UAC Child BU Test User` (BU1; Spaarke Basic User + Core User), same matter, BU without AI index bind | browser `bc40b2ba…` / route **201** `ef356bb4…`; **59 columns, 48 equal, 0 unexpected**; owning BU **equal** (BU1) |
| Same user, payload WITH the BU1 AI search index | browser **403** (missing `prvAppendTosprk_AISearchIndex`) / route **404** uniform; nothing created — refusal parity |
| Same user, root-BU matter EMPL-847770 (no AppendTo), no AI index | route **404** `child_record.not_found`; zz-046 count unchanged |
| Cleanup | **4 of 4 deleted** (`d7adef07…`, `3cef8bf5…`, `bc40b2ba…`, `ef356bb4…`); query: **0 zz-046 work assignments remain** |
| SPE containers | **none created**: every live work assignment was filed under an ordinary matter (`sprk_containerid` null; no isolated provisioning ran). The secure-parent leg was not run live (it would provision a container; the only secure parent in dev is uac-r2's test project) |

## D. What changed (code)

- `Api/ChildRecordEndpoints.cs` (uac-r2's): `sprk_workassignment` in `CreateTables`; the secure-create plan passed for
  every create (decides only for a root); `CompleteRootCreateAsync` after a root create (isolated completion — removed
  again → 500 and nothing created; stranded/incomplete → 201 with `warnings`; then the I-12 materializer); provisioning's
  columns refused for a root; plan refusals mapped (`AccessDenied` → uniform 404, No Access / server-only column → 403,
  unverifiable / no secure team → 500, else 409); **a caller WhoAmI cannot name → the single 403
  `sdap.access.deny.caller_unresolved`** (NFR-10, D-29) on this create route for every table; the share mirror only for
  secure children; `Noun` "work assignment".
- `Services/Signals/Actions/DecisionRouteCores.cs`: `CreateChildAsync` passes the gate and scope factory — **task 043**
  calls it in-process and reads `RouteReply.CreatedId`.
- `bffChildWriteAdapter.ts` (uac-r2's): `sprk_workassignment` in `BFF_CHILD_CREATE_TABLES`; `createRecordViaBffWithWarnings`.
- `workAssignmentService.ts`: create via the route; server warnings appended; `syncAssignedAccess` dropped for this
  create; a pre-existing unreachable `else if` removed (eslint `no-dupe-else-if` blocks the commit otherwise).
- Ledger: the two child-records HandlerDecision line references refreshed (text only).

## E. Deviations from the POML

1. **D-113 supersedes** outputs `WorkAssignmentCreateService.cs` / `WorkAssignmentEndpoints.cs` and the new ledger entry:
   not created.
2. **The BU cascade is not re-implemented server-side.** The wizard still builds it; the route writes the payload as
   given (rows 5–6). Task 043, which calls the route in-process, builds its own payload: if it wants the cascade it
   should mirror `EntityCreationService.applyUserBuDefaults` — and note §F item 1 (an AI-index bind fails for ordinary
   users). Server precedent: `RecordCreationService.ApplyBusinessUnitDefaultsAsync` (private).
3. **AC "single 403 for an unresolved caller"** is met by a pre-check added to the shared create route (all its tables),
   not by a new route.
4. The live proof runs the real route **in-process**, not a deployed BFF (a dev deploy of this branch is an Azure
   change for the owner, after uac-r2's review).

## F. Found, not fixed (D-106 — document only)

1. ([#1607](https://github.com/spaarke-dev/spaarke/issues/1607), F-56) **Ordinary users cannot create a work assignment through today's wizard when their BU names an AI search index**:
   Spaarke Basic User + Core User hold Read but not AppendTo on `sprk_aisearchindex`; the wizard always binds
   `sprk_AI_Search_Index` from the user's BU (live 403 above). The same bind is in the Matter / Project / Event / Invoice
   wizards (`EntityCreationService` cascade). Fix = a role privilege (AppendTo on `sprk_aisearchindex`) — a role change
   for the owner/uac-r2, not made here.
2. ([#1608](https://github.com/spaarke-dev/spaarke/issues/1608), F-57) The G5 core answers a missing **privilege** on a user-owned lookup target (here AppendTo on the AI index) with the
   uniform "A record … was not found" 404 — misleading for that case (uac-r2's core; related to task 126's rule).
3. ([#1608](https://github.com/spaarke-dev/spaarke/issues/1608), F-57) `OwnedChildWrite.ServerOwnedColumns` does not protect a root's `sprk_containerid` / `sprk_securitybu`, so the chat
   `dataverse.create_record` tool can still carry a caller-chosen container on a work-assignment or project create (the
   route now refuses them; the chat tool does not). uac-r2's core.
4. The opt-in live harness cannot authorise the inline Assigned-To step's secure-flag read
   (`ExternalParticipationService` → 401 in-process); production uses the managed identity. Harness limitation.

## G. Gates and measurements (this pass)

- **Publish size** (`.claude/rules/bff-hygiene.md`): fresh short-path worktrees, `dotnet restore` + `dotnet publish -c
  Release` (the `Deploy-BffApi.ps1` commands), PowerShell `Compress-Archive`, **192 files both sides**. Base = project
  branch tip `1df5a2c56` (fresh `C:\w46b`; differs from this branch's base `48742f4d2` only in project docs) →
  **38,340,724 B (36.56 MB)**; branch `941a1c851` → **38,343,043 B (36.57 MB)**; **delta +2,319 B**. Ceiling 60 MB.
- **CVE**: no `.csproj` / `package.json` dependency changed.
- **Tests**: see the PR body (unit suites, ArchTests, jest, live).

---

# Second pass (D-59) and first pass — history, superseded by D-113


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
