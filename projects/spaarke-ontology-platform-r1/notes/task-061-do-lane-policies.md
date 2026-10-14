# Task 061 — Do lane: four policies as subject-only rules

Branch `task/ontology-061-do-lane-policies` (PR into `docs/ontology-platform-design`). Rigor FULL. Sonnet 5.5 at effort high.

## Escalation and resolution

**061 escalation → option A chosen by the main session 2026-10-14, null test added to the grammar.** POL-WA-RESPONSE needs
"no response recorded", and the rule grammar had no null test (an `eq` cannot express it; `notExists` is a different thing, a
related-row anti-join). The grammar gained `{"null": true}` / `{"notNull": true}` in its own commit (9b382ede0), before the
policy commit, per the coordinator's closed scope.

## What shipped

| Commit | What |
|---|---|
| 9b382ede0 | Rule-body null test: schema (Existence and Threshold copies kept verbatim-equal), compiler (FetchXML `null` / `not-null`, no value, not the notExists anti-join), describer ("it has no response recorded"), evaluator judge agreement, tests |
| e8abb0f33 | `config/do-lane.policies.json`, `scripts/Apply-DoLanePolicies.ps1`, `DoLanePolicyDefinitionTests` (30) |
| e47360b1f | `DoLanePolicySeamTests` (live, read-only evaluator versus the collector), DI-77 |

The collector (`DailyBriefingCollector`) is NOT modified (D-16). Event rules read `sprk_duedate`, never `sprk_finalduedate` (D-27).

### The four policies (ids are fixed so the script is an idempotent upsert)

| Policy | Policy id | Version id | Work type | Severity | Body (`when`) | Decision plan |
|---|---|---|---|---|---|---|
| POL-TASK-OVERDUE | `00000000-0000-4000-8000-000000006101` | `...006111` | Finish or reschedule | Warning | Task event type, status Open (659490001), `sprk_duedate <= now-1d` | mark-complete, reschedule, reassign |
| POL-TASK-DUESOON | `...006102` | `...006112` | Coming due | Info | same type and status, `sprk_duedate >= now` and `<= now+3d` | mark-complete, reschedule |
| POL-WA-RESPONSE | `...006103` | `...006113` | Chase a response | Warning | `statecode 0`, `sprk_responseduedate <= now-1d`, `sprk_respondedon` null | send-reminder, extend-response-date, record-the-response |
| POL-TODO-OVERDUE | `...006104` | `...006114` | Finish or reschedule | Info | `statuscode in (1, 659490001)`, `sprk_duedate <= now-1d` | mark-complete, reschedule, reassign |

All four: Do lane, Existence, `all: []` (subject-only), `sprk_enabled = No`, version 1, `inForceFrom` 2026-10-14T00:00:00Z, a literal message
template (no tokens, so a knob change can never make the sentence lie, section 0.3). The overdue knob is the `now-1d` in each overdue body (D-43).

## Dev data written (owner approval D-138, 2026-10-14)

Environment `https://spaarkedev1.crm.dynamics.com`. Policy rows are real configuration and are NOT deleted. Before this task dev held
only POL-COMMIT-BUDGET (`4d204810-61bf-f111-aaaf-0022482913fc`).

### 1. WhatIf (recorded before applying)

```
Definition OK: C:\wts-061\scripts\..\config\do-lane.policies.json (4 policies).
What if: Performing the operation "CREATE POL-TASK-OVERDUE (sprk_enabled = No)" on target "https://spaarkedev1.crm.dynamics.com sprk_policy 00000000-0000-4000-8000-000000006101".
What if: Performing the operation "CREATE POL-TASK-OVERDUE v1" on target "https://spaarkedev1.crm.dynamics.com sprk_policyversion 00000000-0000-4000-8000-000000006111".
  would write sprk_rulebody = {"type":"Existence","subject":"sprk_event","when":{"sprk_eventtype_ref":"124f5fc9-98ff-f011-8406-7c1e525abd8b","statuscode":659490001,"sprk_duedate":{"<=":"now-1d"}},"all":[]}
  would write sprk_decisionplan = {"actions":["mark-complete","reschedule","reassign"],"nextSteps":[]}
  would set current version of POL-TASK-OVERDUE -> v1 once the policy exists
What if: Performing the operation "CREATE POL-TASK-DUESOON (sprk_enabled = No)" on target "https://spaarkedev1.crm.dynamics.com sprk_policy 00000000-0000-4000-8000-000000006102".
What if: Performing the operation "CREATE POL-TASK-DUESOON v1" on target "https://spaarkedev1.crm.dynamics.com sprk_policyversion 00000000-0000-4000-8000-000000006112".
  would write sprk_rulebody = {"type":"Existence","subject":"sprk_event","when":{"sprk_eventtype_ref":"124f5fc9-98ff-f011-8406-7c1e525abd8b","statuscode":659490001,"sprk_duedate":{">=":"now","<=":"now+3d"}},"all":[]}
  would write sprk_decisionplan = {"actions":["mark-complete","reschedule"],"nextSteps":[]}
  would set current version of POL-TASK-DUESOON -> v1 once the policy exists
What if: Performing the operation "CREATE POL-WA-RESPONSE (sprk_enabled = No)" on target "https://spaarkedev1.crm.dynamics.com sprk_policy 00000000-0000-4000-8000-000000006103".
What if: Performing the operation "CREATE POL-WA-RESPONSE v1" on target "https://spaarkedev1.crm.dynamics.com sprk_policyversion 00000000-0000-4000-8000-000000006113".
  would write sprk_rulebody = {"type":"Existence","subject":"sprk_workassignment","when":{"statecode":0,"sprk_responseduedate":{"<=":"now-1d"},"sprk_respondedon":{"null":true}},"all":[]}
  would write sprk_decisionplan = {"actions":["send-reminder","extend-response-date","record-the-response"],"nextSteps":[]}
  would set current version of POL-WA-RESPONSE -> v1 once the policy exists
What if: Performing the operation "CREATE POL-TODO-OVERDUE (sprk_enabled = No)" on target "https://spaarkedev1.crm.dynamics.com sprk_policy 00000000-0000-4000-8000-000000006104".
What if: Performing the operation "CREATE POL-TODO-OVERDUE v1" on target "https://spaarkedev1.crm.dynamics.com sprk_policyversion 00000000-0000-4000-8000-000000006114".
  would write sprk_rulebody = {"type":"Existence","subject":"sprk_todo","when":{"statuscode":[1,659490001],"sprk_duedate":{"<=":"now-1d"}},"all":[]}
  would write sprk_decisionplan = {"actions":["mark-complete","reschedule","reassign"],"nextSteps":[]}
  would set current version of POL-TODO-OVERDUE -> v1 once the policy exists
```

### 2. Apply, with the read-back

```
Definition OK: C:\wts-061\scripts\..\config\do-lane.policies.json (4 policies).
CREATED policy POL-TASK-OVERDUE -> 00000000-0000-4000-8000-000000006101 (enabled = No)
CREATED version POL-TASK-OVERDUE v1 -> 00000000-0000-4000-8000-000000006111
SET current version of POL-TASK-OVERDUE -> v1
CREATED policy POL-TASK-DUESOON -> 00000000-0000-4000-8000-000000006102 (enabled = No)
CREATED version POL-TASK-DUESOON v1 -> 00000000-0000-4000-8000-000000006112
SET current version of POL-TASK-DUESOON -> v1
CREATED policy POL-WA-RESPONSE -> 00000000-0000-4000-8000-000000006103 (enabled = No)
CREATED version POL-WA-RESPONSE v1 -> 00000000-0000-4000-8000-000000006113
SET current version of POL-WA-RESPONSE -> v1
CREATED policy POL-TODO-OVERDUE -> 00000000-0000-4000-8000-000000006104 (enabled = No)
CREATED version POL-TODO-OVERDUE v1 -> 00000000-0000-4000-8000-000000006114
SET current version of POL-TODO-OVERDUE -> v1
READ-BACK OK policy  POL-TASK-OVERDUE 00000000-0000-4000-8000-000000006101 lane=100000001 subject=100000006 worktype=100000003 severity=100000001 enabled=False statecode=0 currentversion=00000000-0000-4000-8000-000000006111
READ-BACK OK version POL-TASK-OVERDUE v1 00000000-0000-4000-8000-000000006111 ruletype=100000002 plan={"actions":["mark-complete","reschedule","reassign"],"nextSteps":[]}
READ-BACK OK policy  POL-TASK-DUESOON 00000000-0000-4000-8000-000000006102 lane=100000001 subject=100000006 worktype=100000004 severity=100000000 enabled=False statecode=0 currentversion=00000000-0000-4000-8000-000000006112
READ-BACK OK version POL-TASK-DUESOON v1 00000000-0000-4000-8000-000000006112 ruletype=100000002 plan={"actions":["mark-complete","reschedule"],"nextSteps":[]}
READ-BACK OK policy  POL-WA-RESPONSE 00000000-0000-4000-8000-000000006103 lane=100000001 subject=100000004 worktype=100000005 severity=100000001 enabled=False statecode=0 currentversion=00000000-0000-4000-8000-000000006113
READ-BACK OK version POL-WA-RESPONSE v1 00000000-0000-4000-8000-000000006113 ruletype=100000002 plan={"actions":["send-reminder","extend-response-date","record-the-response"],"nextSteps":[]}
READ-BACK OK policy  POL-TODO-OVERDUE 00000000-0000-4000-8000-000000006104 lane=100000001 subject=100000005 worktype=100000003 severity=100000000 enabled=False statecode=0 currentversion=00000000-0000-4000-8000-000000006114
READ-BACK OK version POL-TODO-OVERDUE v1 00000000-0000-4000-8000-000000006114 ruletype=100000002 plan={"actions":["mark-complete","reschedule","reassign"],"nextSteps":[]}
READ-BACK OK: every policy and version equals the definition.
```

### 3. Verify (read-only, `-Verify -RequireRows`, exit 0) and idempotency

```
--- Verify (read-only) against https://spaarkedev1.crm.dynamics.com ---
POL-TASK-OVERDUE   policy IN SYNC (enabled=False, statecode=0)
POL-TASK-OVERDUE   v1 IN SYNC
POL-TASK-DUESOON   policy IN SYNC (enabled=False, statecode=0)
POL-TASK-DUESOON   v1 IN SYNC
POL-WA-RESPONSE    policy IN SYNC (enabled=False, statecode=0)
POL-WA-RESPONSE    v1 IN SYNC
POL-TODO-OVERDUE   policy IN SYNC (enabled=False, statecode=0)
POL-TODO-OVERDUE   v1 IN SYNC
```

A second apply changed nothing (`UNCHANGED policy ... (enabled stays False: the script never changes it)` and `UNCHANGED version ... v1`, for all four).

## Proof against real data (spaarkedev1, 2026-10-14, as the writer principal, `DoLanePolicySeamTests`, read-only)

The test reads the applied version rows and runs the real `SignalEvaluator.EvaluateAsync` (writes nothing). The reference is an independent
FetchXML of what the collector selects (`QueryOverdueTasksAsync`: Task, Open, `sprk_duedate` on or before today minus 1; the Upcoming window
today..today+3), minus the collector's per-caller people targeting (the policy answers for every item). The collector has no To Do overdue or
work-assignment-response channel, so those two are compared with FetchXML written from the POML.

| Policy | Evaluator hits | Reference | Only policy | Only reference |
|---|---|---|---|---|
| POL-TASK-OVERDUE | 22 | 22 | 0 | 0 |
| POL-TASK-DUESOON | 0 | 0 | 0 | 0 |
| POL-WA-RESPONSE | 19 | 19 (null test) | 0 | 0 |
| POL-TODO-OVERDUE | 43 | 43 | 0 | 0 |

A difference would be accepted only for a row whose date is within one day of the reference boundary (the assignee's "today" versus a UTC day,
D-25 / D-129); there were none.

- Core record of the hits: POL-TASK-OVERDUE 17 matter, 3 project, 2 no-core (owner-only); POL-TODO-OVERDUE 29 matter, 3 project, 11 no-core; every POL-WA-RESPONSE hit groups under the work assignment itself (D-36) and carries its response due date (D-27/D-33).
- The knob: `now-1d` = 22 hits, `now-5d` = 22, `now-3650d` = 0 (edited in memory, which is what publishing a new version does, minus the write): membership moves with a number in the body and no deployment. Dev has no task due 1 to 4 days ago, hence 22 = 22 for that pair.
- Dev holds no work assignment with a recorded response (the answered-but-past-due query returns 0) and no open task due in the next 60 days (the due-soon comparison is 0 = 0 at 3 and at 60 days), so the comparisons above do not discriminate those cases. The `zz-061-` rows below do.

### `zz-061-` rows (approved test rows; created and deleted inside the test)

7 rows (4 Task events due +2, +10, -3, -30 days; 3 work assignments: past due and unanswered, past due and answered, not yet due), asserted by id against the real evaluator:

| Case | Result |
|---|---|
| due in 2 days | in POL-TASK-DUESOON; not in overdue |
| due in 10 days | in neither |
| overdue 3 days | in POL-TASK-OVERDUE at `now-1d`, **out** at `now-5d` (the knob, on a real row) |
| overdue 30 days | in at both |
| WA past due, no response | in POL-WA-RESPONSE |
| WA past due, **response recorded** | **out** (the null test, on a real row) |
| WA not yet due | out |

Cleanup: rows deleted in `finally`; the test then queries `startswith(name, 'zz-061-')` on `sprk_event` and `sprk_workassignment` and asserts 0
(recorded: `zz-061- rows remaining after cleanup: 0`). Re-checked with a direct Web API query after the run: 0 and 0.

### Finding: DI-77 (#1868) — dev catalog row names a column that does not exist

The first live run of POL-WA-RESPONSE failed: `'sprk_WorkAssignment' entity doesn't contain attribute with Name = 'sprk_workassignmentname'`. The active
`sprk_recordtype_ref` row for `sprk_workassignment` (`e8e1608c-781e-f111-88b3-7ced8d1dc988`) has `sprk_recorddisplaynamefield = sprk_workassignmentname`;
the entity's name column is `sprk_name`. `SignalEvaluator.ReadCatalogFieldsAsync` adds that column to its subject query, so **POL-WA-RESPONSE would fail
every night once enabled** until the one-row data fix (`sprk_recorddisplaynamefield` -> `sprk_name`) is made. That row is shared catalog configuration,
outside this task's approved writes, so it was NOT edited: it is filed (#1868, DI-77) and the live proof repairs the value in memory only
(`CatalogRepairingAdapter`, no write). `TheLiveCatalogRow_ForWorkAssignment_NamesColumnsThatExistOnTheEntity` is red until the row is fixed.
**Do not enable POL-WA-RESPONSE before DI-77 is closed.**

## Tests

| Suite | Result |
|---|---|
| `--filter FullyQualifiedName~Signals` (unit, full, after merging origin/docs/ontology-platform-design `bcd5824a8`) | 1562 passed, 0 failed (the live seam facts return immediately without the env vars) |
| `NullTestGrammarTests` + `SignalReevaluationJobWorkAssignmentResponseTests` | 40 (incl. 3 end-to-end through the real job: raise, close ConditionCleared with no Decision Record, no raise on a pre-existing response) |
| `DoLanePolicyDefinitionTests` | 30 |
| `Spaarke.ArchTests` `PredicateCompilerCallerGuard` + `Signal` | 7 passed |
| Live `SignalPredicateTests` + `RuleBodyDescriberSeamTests` as the writer | 14 passed |
| Live `DoLanePolicySeamTests` | all pass except the DI-77 fact, red by design until the row is fixed |
| `dotnet build Sprk.Bff.Api -warnaserror` | clean |

Fails-before proof (seeded defects, each restored afterwards, full filter re-run green):

| Mutation | Failures |
|---|---|
| M1 compiler swaps `null` and `not-null` | 11 |
| M2 compiler `"<"` -> `le` | agreement guard and fixture goldens fail |
| M3 judge treats a `true`-valued op as a bound | 6, incl. the agreement guard and the e2e tests |
| M4 `AbsentPin` removed | 2 |
| M5 schema drops `const: true` | 3, incl. the schema-copy guard |

## Deviations and decisions

- Event status is **Open only** (659490001), not IsOpenWork: D-74 / D-72 and the collector; the POML text says IsOpenWork.
- The overdue knob is written `<= now-1d` (D-43: overdue starts at one day).
- The WA decision plan adds `record-the-response` (the Do catalog's `RecordTheResponseExecutor` writes `sprk_respondedon`, which clears the Signal).
- Severities follow the v4 text: Warning, Info, Warning, Info.
- The Threshold schema also gets the null test, because its `$defs` are a guarded verbatim copy of the Existence grammar.
- The nightly job is not run against dev with writes: the policies stay Off and no Signal is written. The event-trigger negative criterion is covered by the existing `Trigger_DoesNotEvaluateADoLanePolicy` test (event triggers are Decide-lane only).
- The POML's own step 5 (TASK-INDEX, POML status, current-task) is not done here: the main session owns those files.
- `SignalWriterSeamTests.SysadminAdapter` went from `private sealed` to `internal` (one `virtual` member) so the new seam can wrap it.

## Known limits

- Dev has no task due 1 to 4 days ago, so the live `now-1d` versus `now-5d` comparison on dev's own rows is 22 = 22; the `zz-061-` task due three days ago proves the knob on a real row.
- POL-TASK-DUESOON is 0 = 0 on dev's own data; only the `zz-061-` rows exercise it.
- Briefing and notifications show Open events only (D-74 / D-72); a policy that also covers other statuses would be a new version.
- The reader-scope check (task 038, who sees these Signals) is not done: the Signal read route has not landed on `docs/ontology-platform-design`, and the policies are Off, so no Signal exists to read.
- Overdue To Do (D-31) covers every To Do, including 11 with no core record on dev today (owner-only Signals, D-35).

## BFF hygiene (CLAUDE.md section 10, bff-hygiene items 1-6)

The BFF change is two schema JSON files (embedded resources) and a few lines in `PredicateCompiler`, `RuleBodyDescriber` and the evaluator's
agreement guard: no endpoint, service, DI registration, package, background work or AI type was added (item 1/2 placement and justification: pure
modification of existing surface; item 3 no AI types). Publish size (item 4), both sides from fresh short-path worktrees
(`C:\w61m` = origin/docs/ontology-platform-design `78f6b45a8`, `C:\w61b` = this branch HEAD), command
`dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/ -r linux-x64 --no-self-contained`, zip
`Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, the publish folder's 4 PDBs included:
base 36.853 MB, branch 36.851 MB, **delta -0.002 MB**, 192 files each (counts match). The absolute figure is below the recorded 44.96 MB baseline
(different tree and PDB set); only the like-for-like delta is compared. Item 5: `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages.

## Step 9.5 review

Code review and adr-check by the main author on the change set; one full adversarial pass (`adversarial-reviewer`, read-only, one pass: not an auth, security or tenant-isolation task). No F1/F2 finding. Dispositions:

| Finding | Class | Disposition |
|---|---|---|
| Unpinned-token refusal blamed an `exists` clause for a `when` null-tested field (`PolicyVersionValidator`) | F3 | Fixed: the detail now names the `when` null test; `NullTestGrammarTests` asserts it |
| `-Verify` ignored `statecode` and `sprk_inforceto` of a version row | K3 | Fixed (cheap, same script): both are compared |
| Seam class doc said "nothing to clean up" while `ZzRows_` writes rows | K3 | Fixed: doc states the exact writes and the untracked-server-side-rows caveat |
| Seam comment and JSON description said the collector's window is three days (its own is the caller's Due-soon setting, default 5) | K4 | Fixed (wording) |
| Test name said `Off` but asserted only the column's presence | K4 | Renamed |
| Script: `ConvertFrom-Json` turns literal date scalars in a body into datetimes; `ConvertTo-Json` stores `<` as `<` (JSON-equivalent, compiler and describer unescape it, the live seam proves it evaluates) | K3 | Known limit: today's four bodies carry only relative dates; a future body with a literal date must be serialised from the raw JSON text |
| Script: `$DoLaneCodes` duplicates `DecisionActionCatalog`; a drifted `sprk_policycode` is reported, not repaired | K4 | Known limit: `DoLanePolicyDefinitionTests` resolves every plan against the real catalog and is the gate |

Fix-the-class Grep: every consumer of the `when` grammar (evaluator judge, describer, both schemas, validator, compiler, `ReadPinnedFacts`) handles or refuses the null test fail-closed; no TS mirror exists under `src/`. The defect class "a catalog row names a column the entity lacks" was found once (DI-77); no other Do-lane subject row is affected (task, event and To Do evaluate live).
