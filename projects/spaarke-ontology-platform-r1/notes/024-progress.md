# Task 024 — grammar extension for the Do lane: progress

> Kept here, NOT in `current-task.md` (the main session owns that file).

| Field | Value |
|---|---|
| Rigor | FULL (POML-declared; `bff-api`; `.cs` + tests) |
| Tier | opus @ high · steps mode directional |
| Status | Escalation trigger 2 fired 2026-10-07 (record below); **owner chose option A = D-40** the same day; resumed and implemented (see "Resumed after D-40"). |

## Step 0: coordination checks (owner constraint, 2026-10-07)

- `origin/master` @ `dbc58d139` (2026-10-07). uac-r2 files re-read there: `Services/Dataverse/CoreAncestorResolver.cs`
  (last change `d254d7166`, #1312), `Services/Access/SecureChildLineage.cs`, `config/secure-record-owner-role.json`,
  `Services/Dataverse/RecordOwnershipResolver.cs`, `Api/Filters/RecordRouteAccessAuthorizationFilter.cs`,
  `Infrastructure/ExternalAccess/CallerRecordAccessProbe.cs`, `Infrastructure/ExternalAccess/ExternalCallerContext.cs`,
  `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`, ADR-034.
- **Nothing there invalidates this task's plan.** `CoreAncestorResolver.CoreRecordEntities` = project, matter, work
  assignment, service request; `sprk_event` / `sprk_todo` are CHILD entities carrying all four `sprk_regarding{core}`
  lookups. This task adds NO grouping derivation: the compiler projects only the subject id, and core-record
  derivation (D-34/D-36/D-37) stays with `CoreAncestorResolver`, consumed by the evaluator/writer (031/037).
- Open PRs touching the six task files: only #1111 (this branch). uac-r2 #1353 / #1342 touch none of them, nor any
  uac-r2-owned file this task reads. uac-r2 worktree @ `bbb6cfde0`: no overlapping active work. `/conflict-check`:
  soft warn only (BFF hot path shared with uac-r2, no file overlap).

## Step 1: writer read depth: **all Global, so trigger 1 does NOT fire**

Writer `# mi-ontology-writer-dev` (`3121bf1b-…`), spaarkedev1, 2026-10-07, security-roles.md §9.2 method
(user privileges ∪ every team's privileges, then RetrievePrincipalAccess on real rows):

| Table | User-side Read | Root team `Spaarke` Read | RetrievePrincipalAccess | Rows admin / writer (`MSCRMCallerID`) |
|---|---|---|---|---|
| `sprk_event` | **Global** | Deep | Read on rows in 2 BUs | 79 / 79 |
| `sprk_todo` | **Global** | Deep | Read on rows in 2 BUs | 53 / 53 |
| `sprk_workassignment` | **Global** | Deep | Read | 22 / 22 |
| `sprk_servicerequest` | Global | — | (0 rows) | 0 / 0 (stays refused per POML) |

## Subject schema (live metadata, 2026-10-07): facts the implementation will need

| Column | Format | Behavior |
|---|---|---|
| `sprk_event.sprk_duedate` | DateOnly | **DateOnly** |
| `sprk_todo.sprk_duedate` | DateOnly | **UserLocal** (⚠️ not converted by 098; the stored value is the user's local midnight in UTC; matters for D-25 in 031) |
| `sprk_workassignment.sprk_responseduedate` | DateOnly | **TimeZoneIndependent** |

Status values: event Open = `659490001` (collector); To Do Open = 1, In Progress = `659490001`; work assignment
Active = statecode 0 / statuscode 1. Event type Task = `sprk_eventtype_ref` `124f5fc9-98ff-f011-8406-7c1e525abd8b`.

## 🔔 Escalation trigger 2 fired: "task due within 3 days" is not expressible in the D-16 grammar

Of the three collector rules, two fit the extended grammar as a single subject-only `when` filter:

- **Overdue task**: `sprk_event`, `sprk_eventtype_ref = Task`, `statuscode = 659490001`, `sprk_duedate < now-5d`
  (D-27 removes the collector's `sprk_duedate OR sprk_finalduedate`, which the grammar could not express either).
- **Work assignment past due**: `sprk_workassignment`, `statecode = 0`, `sprk_responseduedate < now`.

**"Task due within 3 days" needs a two-sided range on ONE field** (`sprk_duedate >= now AND <= now+3d`; the collector
uses `NextXDays`). The grammar cannot say that: a field maps to ONE value or ONE operator (`maxProperties: 1` in the
schema, "exactly one operator" in the compiler), and duplicate keys are refused. With only the upper bound
(`sprk_duedate <= now+3d`) the rule matches every overdue task too.

Live, 2026-10-07: 17 open Task events. The collector's "upcoming" set = **3** (due 10-07, 10-08, 10-08). An
upper-bound-only rule returns **17**: the 12 that the overdue rule also covers, plus 2 due 10-04 (which neither
collector rule covers today), plus the 3. So two Signals would be raised for each overdue task.

Options for the owner:

- **(A) Allow a two-bound range object** (recommended): `{">=": "now", "<=": "now+3d"}`, meaning one lower bound
  (`>` or `>=`) plus one upper bound (`<` or `<=`), and nothing else. It compiles to two conditions in the SAME
  AND filter, so it is still one Dataverse filter (CM-3) with no join and no OR. Cost: one schema `$def` and about 20
  compiler lines plus tests. It widens the grammar beyond D-16's four named items, which is why it needs the owner.
- **(B) Drop the lower bound**: `sprk_duedate <= now+3d` with "overdue" taken out by rule precedence in the
  evaluator. That is new evaluator logic (031) and makes one rule's meaning depend on another's.
- **(C) Defer "due within 3 days"** from the Do-lane rule set: ship overdue task and work assignment only, and leave
  "upcoming" to the Briefing narrative (Know, not Do).

Rejected: a `VerifiedJoins` entry or a self-join (forbidden by the POML); FetchXML `next-x-days` (truncates in the
caller's time zone, which the compiler deliberately avoids; see the `PredicateCompiler` remarks).

**Nothing else is blocked.** On an answer, the plan is: schema (`all` minItems 0 when `when` has ≥1 condition; refuse
an empty `when` with zero clauses; `quietWindowDays` integer ≥ 0, default 14), `now+Nd`, `sprk_workassignment` added to
`EvaluatorGlobalReadableEntities`, a live-verified Date Only column catalog surfaced on `CompiledPredicate` for the
`when` fields (field → Dataverse behavior), `QuietWindowDays` on `CompiledPredicate`, three fixtures, unit tests and
seam tests.

---

## Resumed after D-40 (owner, 2026-10-07): implemented

### What changed

| File | Change |
|---|---|
| `Services/Signals/Schemas/existence-rule.schema.json` | `all` minItems 0 + root `if`/`then` (zero clauses ⇒ `when` required with ≥1 condition); `quietWindowDays` integer 0..3650; `$defs/dateRange` (exactly one of `>=`/`>` plus one of `<=`/`<`, both `$defs/relativeDate`); `relativeDate` pattern `now`, `now-Nd`, `now+Nd` with a no-newline lookahead |
| `Services/Signals/PredicateCompiler.cs` | `now+Nd`; `sprk_workassignment` added to `EvaluatorGlobalReadableEntities`; subject-only compile + refusal of zero clauses with no `when` condition; D-40 range → two conditions in the same AND filter, refusing two lowers/uppers, a third key, `=`/`<>`, non-relative-date bounds and empty/inverted ranges; `quietWindowDays` (default 14); live-verified `DateOnlyColumns` catalog; `CompiledPredicate.QuietWindowDays` + `DateOnlyWhenFields` |
| `tests/fixtures/signals/do-*.rulebody.json` + `.reference.fetchxml` (×3) | Overdue task (`sprk_event`, type Task, `statuscode` Open, `sprk_duedate < now-5d`); task due within 3 days (`>= now`, `<= now+3d`, D-40); work assignment past due (`statecode` 0, `sprk_responseduedate < now`). References hand-written and run on spaarkedev1 FIRST (anchor 2026-10-07T00:00Z): **12 / 3 / 19 rows**, matching an independent SQL count (17 open Task events = 12 overdue + 2 due 10-04 + 3 upcoming) |
| `tests/unit/domain/Signals/PredicateCompilerTests.cs`, `PolicyVersionValidatorTests.cs`, `RuleBodySchemaValidatorTests.cs` | New shapes, still-refused shapes, golden byte-equality for the three fixtures; one existing test renamed (`Validate_EmptyAllWithoutWhen_IsRefused`, review F5) |
| `tests/integration/seam/Signals/SignalPredicateTests.cs` | Live: each Do fixture compiled vs its reference (same rows + pinned in/out rows + fewer than all rows); `DateOnlyColumns` vs live metadata in both directions |
| `tests/Spaarke.ArchTests/PredicateCompilerCallerGuardTests.cs` | The forged-`CompiledPredicate` control fixture passes the two new constructor arguments |

### Deviations from the POML (step 7)

- **`PolicyVersionValidator.cs` and `RuleBodySchemaValidator.cs` are unchanged.** Both pass the new shapes through as-is; nothing needed changing.
- **D-40 range** (owner decision after the escalation) is beyond the POML's original four items. Because the grammar is
  shared, it is legal in a clause filter too ("a date field", D-40). A `notExists` range lands inside the outer link
  (tested). Flagged for the owner in case clause-level ranges should be `when`-only.
- **`quietWindowDays` maximum 3650**: not in the POML ("minimum 0"); added so a date computed from it cannot overflow.
- **The Date Only catalog covers every allow-listed table**, not only the three Do subjects: any allow-listed table can be a
  subject, and a missing entry would make 031 judge a calendar date as an instant. Seam-checked in both directions.
- **Tests beyond the stated shapes (criterion 8)**: `Compile_RangeInAnExistsClause_NeverPinsItsField` (§0.3: a range must
  never feed a template token); `Compile_RangeInANotExistsClause_StaysInsideTheOuterLink` (the V4 failure mode);
  `Compile_DateAndTimeWhenField_IsNotReportedAsDateOnly` (catalog over-inclusion guard); arch control-fixture update
  (compile fix).
- **Seam harness credential**: the harness's `DefaultAzureCredential` cannot get a token on this workstation (task 021
  hit the same). It runs with `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`. No code change.

### Input recorded for task 031 (review F1, Medium)

The compiled query is **exact at `WindowAnchorUtc`**. For a Date Only field, per-item "today" (D-25) can differ from the
anchor's UTC date in either direction, so a row the instant-anchored filter EXCLUDES cannot be recovered by re-judging
the returned rows. **031 must widen the query for `DateOnlyWhenFields`**: for example, anchor at a UTC day boundary and
widen each Date Only bound by one day, then judge each returned item against its own "today". The fixtures anchor at
00:00Z, so the intra-day effect is not exercised here. `sprk_todo.sprk_duedate` is still `UserLocal` behaviour; the
owner-approved conversion is task 106, which 031 depends on.

### Test evidence

- **Signals unit tests** (`--filter FullyQualifiedName~Signals`): all pass. `PredicateCompilerCallerGuardTests`: 7/7.
- **Live seam, as the writer principal** (`SIGNALS_LIVE_CALLER_ID=3121bf1b-…`): **10/10 pass**.
  - 3 new Do-fixture rows plus the Date Only catalog check.
  - The 6 Path B / allow-list tests, which had never run through this harness before (021: "written, not run").
- **Each new test fails without its fix.** The mutation runs are in scratchpad `t024/mutate*.py`; each one reverts ONE
  fix and re-runs the Signals suite.

| Mutation (fix reverted) | New tests that go red |
|---|---|
| M1 schema `all` minItems back to 1 | subject-only, Do-subject, fixture golden, Date Only, quiet-window, range, now+Nd, validator subject-only (18 failures) |
| M2 `now+Nd` sign group removed | fixture golden, Date Only fixture, future-date, range |
| M3 `sprk_workassignment` off the allow-list | Do-subject, fixture golden, Date Only fixture |
| M4 compiler range branch removed | range, fixture golden, range-in-exists |
| M5 `quietWindowDays` ignored | quiet-window exposed, invalid quiet-window |
| M6 Date Only catalog not consulted | Date Only fixture, validator subject-only |
| M7 compiler zero-clause refusal removed | `Compile_ZeroClausesAndNoWhenCondition_*` |
| M8 compiler empty-range refusal removed | `Compile_RangeThatMatchesNothing_*` |
| M9 schema `dateRange` variant disabled | range, fixture golden, range-in-exists, matches-nothing |
| M10 compiler quiet-window range check removed | invalid quiet-window |
| M11 `VerifiedJoins` widened for `sprk_event` | `Compile_DoLaneSubjectWithAClause_*`, validator unverified-join (save + evaluation) |
| M12 `sprk_servicerequest` allow-listed | `Compile_ServiceRequestSubject_IsStillRefused` |
| M13 `sprk_document` allow-listed | existing read-depth refusals |
| M14 schema `dateRange` maxProperties 3 | none: **equivalent mutant** (with four possible bound keys, a third key always means two lowers or two uppers, which the `not` clauses refuse) |
| M15 schema allows two lower bounds | `Compile_RangeOutsideD40_*` (schema half) |
| M16 catalog over-includes `sprk_plannedstart` | `Compile_DateAndTimeWhenField_IsNotReportedAsDateOnly` |
| M17 schema subject-only `then` removed | `Compile_ZeroClausesAndNoWhenCondition_*`, `Validate_EmptyAllWithoutWhen_IsRefused` |
| M18 `quietWindowDays` back to `TryGetInt32` (review F2) | `Compile_QuietWindowDays_IsExposedOnThePredicate` (7.0, 1e2 rows) |
| M19 schema newline lookahead disabled (review F3) | `Compile_RangeOutsideD40_*` (the `now+3d\n` row) |

After the review fixes: Signals unit tests **328/328**, arch guard **7/7**, live seam **10/10** (re-run on the final
schema), BFF build with `-warnaserror` clean. The full BFF unit suite at `bf425060a` (before the review fixes, which
touch only Signals files): **14,589 passed, 0 failed, 54 skipped** (20 m 33 s). Full arch suite: 355/356; the one
failure is the pre-existing task 030 credential finding below.

- **Threshold / Switch** are still `RuleTypeUnsupported`: the existing `ValidateForSave_RuleTypeWithNoAuthoredSchemaYet_*` and
  `TryPrepareForEvaluation_ThresholdRuleType_*` tests are unchanged and pass.

### BFF §10

- **Placement**: in the BFF, unchanged location.
  - This extends the existing pure, synchronous compiler. `PolicyVersionValidator` and the BFF-hosted evaluator (031,
    ADR-036 `IScheduledJob` per D-12) call it in-process.
  - It has no I/O, no trigger and no host of its own, so ADR-052 gives nothing to place elsewhere.
  - No endpoint, no DI registration, no package, no AI type (ADR-013).
- **Publish size**: fresh worktrees from short paths, `dotnet publish -c Release`, PowerShell `Compress-Archive
  -CompressionLevel Optimal` over `deploy/api-publish/*`, incl. PDBs.

| Build | Path | Files | Zip |
|---|---|---|---|
| fresh `origin/master` @ `e34c7c0e2` | `C:\wt024m` | 192 | 37,946,853 B (36.19 MB) |
| branch before this task @ `c7dad4812` | `C:\wt024b` | 212 | 47,920,210 B (45.70 MB) |
| branch with this task @ `bf425060a` | `C:\wt024c` | 212 | 47,922,205 B (45.70 MB) |

  - **This task: +1,995 B, with the same 212 files on both sides.** The commits between the two branch builds touch no
    `src`/`tests`.
  - The branch-vs-master gap (+20 files, +9.5 MB) comes from this branch being ~320 commits behind master, not from this
    task. #1111 needs a master merge before it merges. All sizes are under the 60 MB ceiling.
- **CVE**: `dotnet list package --vulnerable --include-transitive` gives "no vulnerable packages"; no package or csproj
  change.
- **Pre-existing arch failure (not this task)**: `FR-32/§4D I5 … per-tenant scoped` fails on
  `Infrastructure/Auth/OntologyWriterCredentialFactory.cs:78`. That is task 030's writer credential:
  `new ManagedIdentityCredential(...)` with no `TenantId`. The failure reproduces identically on the pre-task commit in
  `C:\wt024b`. Reported to the coordinator; not fixed here.

### Step 9.5

An independent code-review + adr-check returned **PASS-WITH-FINDINGS**, with no Critical or High findings and no §6.5 ADR
conflict.
- **Fixed:** F1 (doc corrected and 031 input recorded above), F2 (`quietWindowDays` 7.0 / 1e2 accepted as integers),
  F3 (schema newline lookahead), F4 (`notExists` range test), F5 (rename), F7 / F8 (message asserts), F10 / F11
  (descriptions).
- **Accepted:** F6. The seam row-set comparison is near-tautological given the golden test; the in/out pins and
  `< Count` are the independent proof, and the seam was run live.
- **Kept:** F9. Equal inclusive bounds stay accepted, because `>= now`, `<= now` at a day-boundary anchor is meaningful
  for a Date Only field.
- **Skipped:** F12 (optional helper dedupe).
