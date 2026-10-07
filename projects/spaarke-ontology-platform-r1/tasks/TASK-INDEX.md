# TASK-INDEX — Spaarke Ontology Platform R1

> **Generated**: 2026-10-03 by `/project-pipeline` · **42 tasks** across 10 phases
> **Source**: [`../spec.md`](../spec.md) (44 FRs) · WBS in [`../plan.md`](../plan.md)
> **Validation**: all 42 POMLs pass `scripts/Validate-TaskPoml.ps1` — 0 errors, 0 warnings
>
> 🚨 **Every task runs via `task-execute`.** Never read a `.poml` and implement manually (project CLAUDE.md §4).
> When you complete a task, update **both** the marker here **and** the POML `<status>` — or
> `scripts/check-task-status-drift.ps1` fails the next push.

**Legend**: 🔲 not started · 🔄 in progress / needs retry · ✅ complete · ⛔ blocked

---

## Registry

### Phase 0 — Foundations

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ 001 | [Schema: the four missing columns](001-schema-complete-missing-columns.poml) | FULL | sonnet/high | — | — | **Unblocks everything.** Two columns block a path outright |
| ✅ 002 | [Privilege re-verify](002-privilege-reverify-after-column-adds.poml) | STANDARD | sonnet/medium | 001, 006 | — | Owner chose **option A** (dedicated identity, task 006); completes when the union check passes for the new writer |
| ✅ 003 | [ADR-040 amendment + ADR-039 exception](003-adr-040-amendment-and-039-exception.poml) | FULL | **opus/xhigh** | — | — | ⚠️ `.claude/` → **main session only**. Must merge before/alongside 031 |
| ✅ 004 | [Seed Policy + taxonomy rows](004-seed-policy-and-taxonomy-rows.poml) | STANDARD | sonnet/medium | 001 | — | Policy stays `enabled = No` until reviewed. `sprk_policy` GUID `4d204810-61bf-f111-aaaf-0022482913fc`; `sprk_policyversion` GUID `42b3e716-61bf-f111-aaaf-0022482913fc`. Taxonomy rows already enabled (no-op) |
| ✅ 005 | [Seed dev data + 2 negative controls](005-seed-dev-data-and-negative-controls.poml) | STANDARD | sonnet/medium | 004 | — | Live-classified via `/api/office/save`; see `notes/seed-data-state.md` for all GUIDs + seeded-vs-real |
| ✅ 006 | [Provision the dedicated writer identity](006-provision-dedicated-writer-identity.poml) | FULL | **opus**/high | — | — | **Owner: option A** for 002. Azure changes need owner confirmation |

### Phase 1 — Cleanup that gates the row

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ 010 | [C-1 dead briefing hooks + 2 stale comments](010-c1-remove-dead-briefing-hooks.poml) | FULL | sonnet/high | — | **A** | The misdirection hazard. Gates the row |
| ✅ 011 | [C-3 disambiguate MetricCard](011-c3-disambiguate-metriccard.poml) | STANDARD | sonnet/medium | — | **A** | Two unrelated components share the name |
| ✅ 012 | [C-4 generic status badge](012-c4-generic-status-badge.poml) | FULL | sonnet/high | — | **A** | The one legitimately-new UI primitive |

### Phase 2 — Policy and rule bodies

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ 020 | [`Existence` rule type + JSON Schema](020-existence-rule-type.poml) | FULL | sonnet/high | 003 | **B** | Without it the capability is unsavable |
| ✅ 021 | [**The predicate compiler**](021-predicate-compiler-exists-notexists.poml) | FULL | **opus/xhigh** | 020, 005 | — | 🔴 **THE RISK ITEM.** Serial. `notExists` has no in-repo template |
| ✅ 022 | [Rule-body validation refusal](022-rule-body-validation-refusal.poml) | FULL | sonnet/high | 020 | — | `PolicyVersionValidator` seam for task 031; owner: **validate at evaluation**, fail closed (app authoring stays). **Done 2026-10-05**: 3 independent reviews, full suite 14541/0 failed, +0.01 MB |
| ✅ 023 | [Scope semantics + policy defaults](023-scope-semantics-and-policy-defaults.poml) | FULL | sonnet/high | 003 | **B** | Copy `CommunicationRuleGate` verbatim; fail closed |

### Phase 3 — The evaluator and Signal lifecycle

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ 030 | [Signal writer + dedupe + **ownership**](030-signal-writer-dedupe-and-ownership.poml) | FULL | sonnet/high | 021, 006 | — | Two independent reviews. Writer owns, owning BU from matter; create-first + reconcile; fail-closed MI; refusals logged (EventId 50300) + metered. Live as writer: matter PASS; **communication blocked by F26 (owner role decision)** |
| 🔲 031 | [Nightly re-evaluating `IScheduledJob`](031-nightly-reevaluating-scheduled-job.poml) | FULL | sonnet/high | 030, 003 | — | ONE evaluator, not evaluator + sweep |
| 🔲 032 | [Two event triggers](032-event-triggers-classification-and-budgetrevision.poml) | FULL | sonnet/high | 031 | — | Budget-revision hook is correctness, not polish |
| 🔲 033 | [Closure semantics](033-closure-semantics.poml) | FULL | sonnet/high | 031 | **C** | Aged-out maps to `ConditionCleared` |
| 🔲 034 | [Suppression per (policy, matter), 30d](034-suppression-policy-matter-30-days.poml) | FULL | sonnet/high | 031 | **C** | D-11 grain; auto-close never counts |
| 🔲 035 | [**Deploy** BFF](035-deploy-bff-evaluator.poml) | FULL | sonnet/high | 034 | — | Merge master first; hash-verify |

### Phase 4 — The Decision Record

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| 🔲 040 | [Decision Record writer + record class](040-decision-record-writer.poml) | FULL | sonnet/high | 001, 030 | — | BR-1 **as reversed**: every human resolution writes one |
| 🔲 041 | [Append-only + relationship direction](041-append-only-and-relationship-direction.poml) | FULL | sonnet/high | 040 | **D** | Test as a real non-admin; 1 → N, FK on the Signal |
| 🔲 042 | [Wire writer into the consumer](042-wire-writer-into-rulegated-consumer.poml) | FULL | sonnet/high | 040 | **D** | One line before the branch. **Do not touch the gate** |

### Phase 5 — The worklist surface

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| 🔲 050 | [Worklist grid configuration row](050-worklist-gridconfiguration-row.poml) | STANDARD | sonnet/medium | 034 | — | Membership by rule evaluation, never a user filter |
| 🔲 051 | [**The one row component**](051-worklist-row-component.poml) | FULL | **sonnet/xhigh** | 050, 012 | — | 🔴 All five row-contract requirements. A second row component is a design failure |
| 🔲 052 | [Extend MetricCard / RowMenu / OutcomeCard](052-extend-metriccard-rowmenu-outcomecard.poml) | FULL | sonnet/high | 051 | **E** | Reuse is binding (§1.3). **+ C-9** shared `RowActionMenu` |
| 🔲 053 | [Gate host + acting](053-gate-host-and-acting.poml) | FULL | sonnet/high | 051 | **E** | Dismissal requires a reason |
| 🔲 054 | [Reconciliation tab + aggregate item](054-reconciliation-tab-and-aggregate-item.poml) | STANDARD | sonnet/medium | 051 | **E** | ONE registration; the surface already exists |
| 🔲 055 | [**Deploy** Console + shared components](055-deploy-console-and-shared-components.poml) | FULL | sonnet/high | 054 | — | **Real Dataverse** verification, not a mock |

### Phase 6 — Do lane and Briefing fold-in

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| 🔲 060 | [ISS-003 stranded Draft events](060-iss003-stranded-draft-events.poml) | FULL | sonnet/high | — | — | **Blocks the Do lane** or it under-reports |
| 🔲 061 | [Do lane: three temporal policies](061-do-lane-temporal-policies.poml) | FULL | sonnet/high | 060, 034 | — | A **predicate migration**, not a collector rewrite |
| 🔲 062 | [Know → narrative; retire Critical Today; remove LLM Top action](062-know-narrative-and-retire-critical-today.poml) | FULL | sonnet/high | 061 | **F** | Rank is deterministic or it is not explainable |
| 🔲 063 | [First Know-promotion rule](063-first-know-promotion-rule.poml) | STANDARD | sonnet/medium | 061 | **F** | News vs work — the difference *is* the ontology |
| 🔲 064 | [Replace the Briefing tab](064-replace-briefing-tab-with-worklist.poml) | FULL | sonnet/high | 062, 063, 055 | — | Only once BOTH lanes exist |

### Phase 7 — Inquiry and the classifier

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| 🔲 070 | [The Inquiry: Action + Binding + SLA](070-inquiry-action-and-binding.poml) | FULL | sonnet/high | 001, 053 | — | The Action **row**, not the Action **Engine** |
| 🔲 071 | [Disposition accrual](071-disposition-accrual.poml) | FULL | sonnet/high | 070 | **G** | Do-rule action rate reads `sprk_resolutiontype` |
| 🔲 072 | [Guidance injection](072-classifier-guidance-injection.poml) | FULL | sonnet/high | 004 | **G** | Prompt changes; the schema `enum` does NOT |
| 🔲 073 | [`sprk_memo` as source #2](073-memo-as-second-signal-source.poml) | FULL | sonnet/high | 030, 072 | **G** | The consumer diff must be **empty** |
| 🔲 074 | [**Recall measurement (exit gate)**](074-classifier-recall-measurement-gate.poml) | FULL | **opus/xhigh** | 072 | — | 🔴 **CAN FAIL THE PROJECT.** ≥80% on ≥50 items |

### Phase 8 — Remaining cleanup and repairs

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ 080 | [The six hazards](080-six-hazards-cleanup.poml) | FULL | sonnet/high | — | **H** | Fixes relocated to their own PRs: C-10→084, C-5→085, C-19→086, C-22→088, C-23→[#1114](https://github.com/spaarke-dev/spaarke/pull/1114). C-21: owner chose **delete** (in 086) |
| 🔄 081 | [On-branch cleanup the worklist needs (C-8, C-11, C-13, C-17)](081-duplication-cleanup.poml) | FULL | sonnet/high | 084 | — | C-13 waits for **084 merged**; C-17 = **3/7/10 days** (owner)  — **2026-10-05: own PR to master [#1309](https://github.com/spaarke-dev/spaarke/pull/1309)** (round 3; reviews 1-2 FAILED); SmartTodo palette everywhere; 38 packages build. Third review FAILED (no runtime regression): ~12 sites not asking for the Xrm capability they use, two hand-rolled walks + one EmptyState copy missed, feed Overdue filter still UTC. Round 4 running |
| ✅ 082 | [Tokenizer repair](082-matter-number-tokenizer-repair.poml) | FULL | sonnet/high | — | **H** | Ship with a **measured** query-count delta |
| ✅ 083 | [Association `reason` string](083-association-reason-string-repair.poml) | STANDARD | sonnet/medium | — | **H** | AP-12 in runtime prose |
| ✅ 084 | [To-Do scorer, own PR (C-10)](084-todo-scorer-own-pr.poml) | FULL | sonnet/high | — | I | [PR #1118](https://github.com/spaarke-dev/spaarke/pull/1118) **merged** `b5b0c0ce0`. **Live bug.** Merge first; 081 depends on it |
| ✅ 085 | [Compose cleanup, own PR (C-5, C-16)](085-compose-cleanup-own-pr.poml) | FULL | sonnet/high | — | I | [PR #1117](https://github.com/spaarke-dev/spaarke/pull/1117) **merged** `aad1c877f`. C-5 already lost comment data |
| ✅ 086 | [Dead + misleading code, own PR (C-19, C-21, C-14, C-20, C-26, C-6, C-27)](086-dead-code-own-pr.poml) | FULL | **opus**/high | — | I | [PR #1120](https://github.com/spaarke-dev/spaarke/pull/1120) **merged** `a082cfcda`. C-21 deleted (owner). Closes #1112, #1113 |
| ✅ 087 | [Events leftovers, own PR (C-2, C-24, C-25)](087-events-leftovers-own-pr.poml) | FULL | sonnet/high | — | I | [PR #1119](https://github.com/spaarke-dev/spaarke/pull/1119) **merged** `e24ad4d20`. Also dropped 2 more unused EventsPage deps |
| ✅ 088 | [InsightSummaryCard, own PR (C-22)](088-insight-summary-card-own-pr.poml) | FULL | sonnet/medium | — | I | [PR #1116](https://github.com/spaarke-dev/spaarke/pull/1116) **merged** `71394e0d3`. Web resource: needs a deploy after merge |
| ✅ 089 | [Shared building blocks, own PR (C-7, C-12, C-15)](089-shared-building-blocks-own-pr.poml) | FULL | sonnet/high | — | I | [PR #1121](https://github.com/spaarke-dev/spaarke/pull/1121) **merged** `ec7211aaf`. All ~40 deferred sites converged; case-semantics audit in PR body. #1118 and #1121 must merge before 081 |
| ✅ 091 | [Unaudited sweeps, own PR (C-18)](091-unaudited-sweeps-own-pr.poml) | FULL | sonnet/high | — | I2 | [PR #1122](https://github.com/spaarke-dev/spaarke/pull/1122) **merged** `48d4fad04`. 4 colour violations fixed; 3 cards documented as different; SECTION_REGISTRY kept with consumer cited |
| ✅ 092 | [Master build + test failures, own PR](092-master-build-and-test-failures-own-pr.poml) | FULL | sonnet/xhigh | — | I2 | [PR #1123](https://github.com/spaarke-dev/spaarke/pull/1123). 6 PCF builds fixed (broken since the 2026-08-14 CVE bump); 17 failing suites fixed or evidenced. ⚠️ **2026-10-05: its "FR-02 section height" change is WITHDRAWN** — it would have regressed the owner's R2 UAT §5.6 row-height fix on the DataGrid sections (`803c77ace1`; test (h) was stale, not the code). Agreed with uac-r2: #1123 keeps the four equivalent test files and merges BEFORE [#1293](https://github.com/spaarke-dev/spaarke/pull/1293), which rebases. **2026-10-05: full nightly run 18/18 PCFs pass** ([run 37260945864](https://github.com/spaarke-dev/spaarke/actions/runs/37260945864), head `7f53d3021`). Root cause of the last failures: PCFs compile SOURCE from `Spaarke.Communication.Components` / `Spaarke.Visuals`, whose imports resolve from the shared folder, so those packages must be installed (workflow + release build Step 1 now do). c2780ce29's VisualHost dep move fixed nothing and is reverted |
| ✅ 093 | [CI: nightly advisory PCF build:prod, own PR](093-ci-nightly-pcf-build-prod-own-pr.poml) | FULL | sonnet/high | — | I2 | [PR #1282](https://github.com/spaarke-dev/spaarke/pull/1282) + fix [PR #1285](https://github.com/spaarke-dev/spaarke/pull/1285) **merged**. Nightly PCF build check now reports real results (10 fail / 8 pass on master, matching the logs) |
| ✅ 094 | [PCF deploy procedures verify the real build result, own PR](094-pcf-deploy-build-verification-own-pr.poml) | FULL | **opus**/high | — | I2 | [PR #1286](https://github.com/spaarke-dev/spaarke/pull/1286) **merged 2026-10-05** (`b9dcae48b`): every PCF build judged by its output; release build builds each PCF in production mode (14/14 shared libs + 19/19 PCFs from a clean checkout); Step 1 dependency order fixed; ThemeEnforcer `build:prod`. Pinned notice [#1308](https://github.com/spaarke-dev/spaarke/issues/1308) |
| ✅ 095 | [Flaky email-attachment regex timeout, own PR](095-flaky-email-attachment-regex-timeout-own-pr.poml) | FULL | sonnet/high | — | I2 | [PR #1287](https://github.com/spaarke-dev/spaarke/pull/1287) **merged 2026-10-05** (`b4b58a361`). Test now exercises production code (it tested a private copy without the catch); production unchanged |
| ✅ 096 | [Json.Schema.Net concurrent Evaluate race in AI tool-schema validation, own PR](096-json-schema-net-metaschema-race-own-pr.poml) | FULL | **opus**/high | — | I2 | Found by 022's second review: `MetaSchemas.Draft202012.Evaluate` unlocked at `AnalysisToolService.cs:527` + `ToolHandlerToAIFunctionAdapter.cs:919` can pass malformed tool schemas under concurrency. [PR #1294](https://github.com/spaarke-dev/spaarke/pull/1294): one shared lock (both sites share one instance, so per-site locks still leaked 75-111/1000); 0 false-valid over 18 rounds. Review PASS-WITH-FINDINGS, all fixed (verdict cache; issue #1295; guard test; +437 bytes). **ADR-009 path A approved by owner 2026-10-05** (spec ADR Tensions). **Merged 2026-10-05** (`23bdc5679`) |
| ✅ 097 | [Event "complete" status code + To Do reassign lookup target, own PR](097-event-complete-and-todo-reassign-write-paths-own-pr.poml) | FULL | **opus**/high | — | I2 | Found 2026-10-05 checking v4 actions: event complete writes statuscode 5 (live Completed = 659490002?); To Do reassign binds `sprk_assignedto` to systemusers (live target contact?). Prove against live schema, then fix. **Proven + wider**: BFF event status codes were invented (3/5/6/7), so BFF event create/complete/cancel/soft-delete all fail live (HTTP 400); side-pane Completed too; To Do + follow-on assignee binds. [PR #1302](https://github.com/spaarke-dev/spaarke/pull/1302); round 2 (`5415ea6d8`): **every BFF event read also failed live** (bad `$expand` case, lookup selected as a value, nonexistent column, `$skip`), so the events API was broken end to end; priority, regarding lookup, external-SPA events (never worked), To Do Dismissed (dismissed never regenerated) all fixed and proven live; BFF suite 14302/0 failed on rerun. Owner decisions pending: what counts as open/overdue event work; two status columns; reschedule date column. **Review 1 FAILED** (`sprk_eventlog.sprk_description` does not exist → create/delete/PUT-status write then return 500; regarding fields left inconsistent on re-parent; external status from the wrong column); round 4 (`9e427d5c9`) fixed F1-F10 and **proved the real routes live** (in-process BFF host → spaarkedev1): all pass; **on master, event create/get/list/logs/complete all return 500**. Full suite 14320/0 failed. Second review FAILED: **routes have no record-level authorization** (would expose every event once fixed) — owner: apply the solution's established record-access pattern; core-ancestor stamp missing/wiped (I-1 defect); regarding type misread; **owner: convert the six event date columns to Date Only** (ConvertDateAndTimeBehavior on existing rows). Round 5 (`05d6df240`): record-level authorization via the solution's existing pattern (Dataverse checks the caller's rights; proven with a real low-privilege user), stamp, regarding, tests; suite 14356/0 failed. Dates split to task 098. Third review PASS-WITH-FINDINGS; round 6 under the owner directive: I-6 ownership on create (child-BU users could not reach their own events), AppendTo on the parent, Create-privilege check, list-impersonation test, 401 vs 403. Round 6 (`28e9ecd36`) done, proven with a real child-BU user; suite 14371/0 failed; focused review of round 6 running. **MERGED 2026-10-07 as `87665c3da`** after 11 rounds; reconciled with #1312 (one auth mechanism); owner decisions A (Reassigned completable, one IsOpenWork predicate) + B (my events = owner/assignee/creator) implemented; all To Do rules paged; regarding number from the catalog. See POML `<completion>` |
| 🔄 098 | [`sprk_event` date columns: UserLocal → Date Only, own PR](098-event-date-columns-date-only-own-pr.poml) | FULL | **opus**/high | 097 | I2 | Owner 2026-10-05: switch `CanChangeDateTimeBehavior` on, convert six columns to Date Only (one-way), UTC conversion, hand-correct 5 values; record for `SPRKDOCINTELLIGENCE`; code PR after #1302  — **Dataverse part done in dev 2026-10-06**: six columns Date Only, conversion job Succeeded, 120/120 values on the intended day (5 hand-corrected); procedure in `docs/data-model/sprk_event-date-columns.md`. Dev side-effect: ribbon/EventsPage complete now 400 and several readers show the previous day — being fixed in the same PR |

### Phase 9 — Wrap-up

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| 🔲 090 | [Project wrap-up](090-project-wrap-up.poml) | STANDARD | sonnet/medium | all | — | `/test-diet` is a **binding gate** (CLAUDE.md §7) |

---

## Parallel execution waves

Max concurrency is **6 agents per wave** (hard limit). After each wave the main session **must verify the
build** before dispatching the next — `dotnet build src/server/api/Sprk.Bff.Api/` for `.cs` changes,
`npm run build:prod` for PCF packages.

| Wave | Tasks | Prerequisite | `goal-eligible` | Why |
|---|---|---|---|---|
| **A** | 010, 011, 012 | 001 | ✅ yes | Three independent cleanup items, different files, machine-verifiable end states |
| **B** | 020, 023 | 003 | ✅ yes | Both well-specified, low ambiguity, separate code paths |
| **C** | 033, 034 | 031 | ✅ yes | Lifecycle semantics in separate paths |
| **D** | 041, 042 | 040 | ❌ no | 041 is a security verification needing human judgement on the union result |
| **E** | 052, 053, 054 | 051 | ❌ no | 053 touches the gate boundary; UI judgement throughout |
| **F** | 062, 063 | 061 | ✅ yes | Configuration plus one policy row |
| **G** | 071, 072, 073 | 070 | ✅ yes | Three independent surfaces |
| **H** | 080, 081, 082, 083 | none | ❌ no | 080 carries six separate hazards needing per-item judgement |
| **I** | 084, 085, 086, 087, 088, 089 | none | ❌ no | Cleanup unrelated to ontology, each in **its own worktree and PR** off master (owner, 2026-10-03). Separate worktrees, so no file overlap with the ontology branch |
| **I2** | 091 | after wave I | ❌ no | Audit-then-fix; held back so it does not compete with six other PR worktrees |

**Never parallel** — `parallel-safe: false`: **003** (`.claude/` paths, main session only) · **021** (the risk
item, serial by choice) · **030**, **031**, **032**, **051**, **064**, **070**, **074**, **090** (each is a
prerequisite others attach to) · **035**, **055** (deploys to a shared environment).

> **On `/goal` waves**: the Haiku evaluator is a **stopping-condition check, not a quality gate**. A met
> condition means the wave is done being *iterated*, not that the work is *good*. Step 9.5 gates and
> orchestrator authority are unchanged, and tasks are never auto-completed on goal achievement.

---

## Critical path

```
001 → 003 → 020 → 021 → 030 → 031 → 040 → 042 → 050 → 051 → 070 → 074 → 090
      ADR    type  PREDICATE writer  job  record  wire  config  ROW  inquiry RECALL
```

**021 is the longest single pole** and the only task whose value is unproven. **074 can fail the project** —
it is a measurement, not an implementation.

Phase 8 (wave H) sits **off** the critical path entirely and can absorb slack at any point.

---

## High-risk items

| Task | Risk | Why it is ranked here |
|---|---|---|
| **021** | 🔴 **High** | `notExists` has **no prior art anywhere in the repo**. A broken anti-join fails by returning every row or no row, and **both read as a working predicate**. Its acceptance criteria include an empty-source-table test for exactly that reason |
| **074** | 🔴 **High** | A measurement that gates project exit. At 70% recall the differentiated claim misses 30% of real cases **while every other criterion passes green** |
| **030** | 🔴 **High** | FR-14 ownership. A service-owned Signal at depth-4 read can expose a matter the reader cannot open — lookup blank, sentence telling them anyway. Live across 6 business units, and **invisible because nothing errors** |
| **003** | 🟡 Medium | ADR amendment with repo-wide blast radius; must stay narrow and must merge before 031 |
| **051** | 🟡 Medium | New UI surface carrying the whole row contract. A second row component would be a design failure |
| **060** | 🟡 Medium | Data remediation in another domain. A blanket update would make 48 tasks appear in users' briefings at once |
| **064** | 🟡 Medium | Replaces a shipped user surface. Premature replacement removes working awareness |

---

## Standing obligations on every task

- **`/conflict-check` before every BFF PR** — 56 of 62 active projects declare a hot path
- **Publish size** measured against a **fresh master build**, short path, file counts both sides (NFR-01)
- **§0.3** — a capability must TEST what its message CLAIMS (NFR-02)
- **Merge master before any deploy** — it moved 66 commits in a day during this project
- Anything deferred gets a **GitHub Issue URL** in `../notes/defer-issues.md`; `push-to-github` Step 1.6
  refuses a push without one
