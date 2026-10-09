# TASK-INDEX — Spaarke Ontology Platform R1

> **Generated**: 2026-10-03 by `/project-pipeline` · **Updated 2026-10-07** for the owner's decisions D-13..D-56
> (spec §9) · **109 tasks** across 14 phases (51 ✅ · 1 🔄 · 56 🔲 · 1 cancelled; updated 2026-10-08)
> **2026-10-08 no-parking sweep (D-81)**: new tasks **121-129** (phase 8) turn every open issue and loose end into a task;
> **113** amended (#1420, #1421), **114** amended (deps 121; no tenant-wide publish; 127 auto-deploy), **120** amended (node
> descriptions match their queries). Still deferred by explicit owner decision only: ISS-004 (#1095) and ISS-007 (#1381, D-50).
> **Source**: [`../spec.md`](../spec.md) (FR-01..FR-63 plus FR-14a, FR-17a — 65 FRs; NFR-01..NFR-11) · WBS in [`../plan.md`](../plan.md)
> **2026-10-07 changes**: 34 new tasks (007-009, 024-026, 036-038, 043-046, 049, 056-059, 065-067, 079, 099,
> 100-105, 110-114); 17 existing POMLs amended (031-034, 040, 042, 050-055, 061-064, 070); **053 superseded** by 058 + 043.
> **Second pass (D-30..D-33)**: new task **039** (secure-child registration + writer ownership); 007, 008, 031, 032, 033,
> 037, 038, 040, 042, 059, 061 amended; **079 is now the uac-r2 coordination step**, not a deferred issue. D-33 **replaces
> D-15's Restricted/Limited skip**: Signals and Decision Records on **Secure** matters become secure children.
> **Third pass (D-34, D-35)**: Signals and Decision Records group under the item's **core record — matter OR project** (uac-r2's
> `CoreAncestorResolver`; 007 adds a typed `sprk_project` lookup on both tables); suppression is per (policy, core record); an item
> with no matter and no project is **owner-only** under "Not filed". Amended: 007, 031, 033, 034, 037, 038, 039, 040, 042, 043, 050,
> 051, 058, 059, 061, 064. New open points O-22..O-25 (spec §11.1); O-21 decided.
> **Fourth pass (D-36..D-39)**: all four access-control core types (matter, project, work assignment, service request), extensible
> through `CoreAncestorResolver`'s core set + the `sprk_recordtype_ref` catalog: 007 now adds a **generic** core-record reference
> (`sprk_corerecordtype` + `sprk_corerecordid`) that all logic reads, with typed lookups only where uac-r2's lineage requires them
> (on `sprk_decisionrecord`: matter, project, work assignment). Direct filed-under core record wins, then matter over project (D-37);
> **no skips** (D-38); no-core items suppress per (policy, item) and their record is owner-owned (D-39). 039 covers Secure work
> assignments; service requests cannot be Secure. Amended: 007, 031, 033, 034, 037, 038, 039, 040, 042, 050, 051, 059, 061, 064.
> **Fifth pass (D-42..D-56)**: every open point decided. New task **047** (WA response columns, D-54); 074 adds three recall columns
> (D-49); 057 files the source-freshness issue (D-50); 052 drops the RowMenu/OutcomeCard reuse (D-46); 045 in R1 (D-47).
> Amended: 031, 032, 036, 038, 043, 044, 045, 050, 052, 057, 058, 061, 062, 063, 064, 070, 074, 103.
> **Open points: none.** All O-1..O-25 are decided (O-20 dissolved); see spec §11.1.
> **uac-r2 coordination (owner, 2026-10-07)**: tasks marked **[uac]** below must re-read uac-r2's current code on
> `origin/master`, check its open PRs and active work, reuse its mechanisms, route edits to its files through its review,
> and stop if its code invalidates the plan (spec §8.3).
>
> 🚨 **Every task runs via `task-execute`.** Never read a `.poml` and implement manually (project CLAUDE.md §4).
> When you complete a task, update **both** the marker here **and** the POML `<status>` — or
> `scripts/check-task-status-drift.ps1` fails the next push.

**Legend**: 🔲 `[open]` not started · 🔄 `[wip]` in progress / needs retry · ✅ `[done]` complete · ⛔ blocked / superseded

---

## Parallel work streams (owner: speed) — updated 2026-10-07

Status from the POMLs today: ✅ done includes 001-012, 020-024, 030, 065, 007, 008, 009, 080-097, 099; 🔄 079, 098.
**Startable now** = every dep done. **Gate** = the one thing still blocking. **PR** = *own PR to master* when it changes
shared UI / modal code other projects consume (or another domain's surface); otherwise it lands on this branch.
Streams do not share files, with one exception noted: every new BFF route edits the #1312 route ledger
(`RouteAuthorizationGuardTests.Ledger.cs`), so 036, 044, 046, 043, 038, 104, 100, 101, 103 merge their ledger edits one at a time.

| Stream | Task | Deps (POML) | Deps met today? | Gate | PR |
|---|---|---|---|---|---|
| **1 Evaluator critical path** (`Services/Signals`, compiler, writer) | 079 🔄 | — | yes (in progress) | uac-r2 review of the two entries | branch |
| | 039 | 007, 008, 079 | no | **079** + uac-r2 batch 4 in dev | branch (uac-r2 files reviewed by uac-r2) |
| | 037 | 007, 008, 024, 039 | no | 039 (same file: `SignalWriter.cs`) | branch |
| | 106 | 098 | no | **098 merged** (PR #1359); **PR #1429 open** (awaiting owner merge) | own PR |
| | 031 | 030, 003, 007, 008, 009, 024, 037, 039, 098, 106 | no | 037, 039, 098, 106 | branch |
| | 026 | 024 | **yes** | — | branch |
| | 025, 032, 033 | 024/031 | no | 031 | branch |
| | 034 | 031, 036 | no | 031 (+036) | branch |
| | 035 | 034 | no | 034 | branch (deploy) |
| | 038 | 050, 031, 037 | no | 031, 050 | branch |
| | 050 | 034, 007 | no | 034 | branch |
| **2 UI + modal foundation** (`@spaarke/ui-components`, wizards) | 110 | — | **yes** | main session only (`.claude/`) | own PR |
| | 056 | 110 | no | 110 | **own PR** (shared WizardShell, 8 consumers) |
| | 111, 112 | 056 | no | 056 | **own PR** |
| | 113 → 114 | 112 / 111, 113 | no | 112 | **own PR** |
| | 057 | 012 | **yes** | — | **own PR** (changes shared `StatusBadge` + barrel) |
| | 051 | 057, 012, 007 | no | 057 | branch (worklist-only component in the shared lib) |
| | 052 | 051 | no | 051 | **own PR** for C-9 (migrates 4 existing menus); MetricCard part with it |
| | 054 | 051, 057 | no | 051 | branch |
| | 058 | 056, 057, 036, 026, 034, 043 | no | 056, 057 (build on fixtures); 043 for live wiring | branch |
| | 059 → 055 | 038, 051, 052, 054, 058 / 059, 043, 035 | no | 058, 038 | branch |
| **3 Decision backend** (BFF actions, commit route, DR) | 036 | 007 | **yes** | — | branch |
| | 046 | 008 | **yes** | WA area owner review | branch (another domain's create path; reviewed) |
| | 047 | — | **yes** | WA area owner + uac-r2 agreement | branch (schema) |
| | 070 | 001, 036 | no | 036 | branch |
| | 044 | 036, 008, 047 | no | 036, 047 | branch |
| | 040 | 001, 030, 007, 036, 008, 039 | no | 036, 039 | branch |
| | 041, 042 | 040 | no | 040 | branch |
| | 043 | 040, 036, 044, 046, 070 | no | 040, 044, 046, 070 | branch |
| | 049 | 043, 055 | no | 055 deploy | branch (role edit) |
| **4 Classifier + recall gate** (`Services/Ai`, triage category) | 072 | 004 | **yes** | — | branch |
| | 073 | 030, 072, 037, 039, 031 | no | 037 + 039 + 031 (blocked 2026-10-07: writer subjects, sprk_regardingmemo schema) | branch |
| | 074 | 072 | no | 072 (+ email-project conflict-check for D-49) | branch |
| | 071 | 070 | no | 070 | branch |
| **5 Cleanups** (other domains, own worktrees) | 060 | — | **yes** | — | branch (data fix) |
| | 066 | 098 | no | 098 | own PR |
| | 067 | 081, 098 | no | 098 | **own PR** (SmartTodo scorer) |
| **Later** (admin, Do lane, cutover) | 061-064, 100-105 | see registry | no | 031-034, 059 | branch |

**Startable right now (deps met): 026, 036, 046, 047, 057, 060, 072, 110** (110 in the main session), plus 079 continuing.
Run them as five parallel agents at most, one per stream: **026** (stream 1), **057** (stream 2; 110 alongside in the main
session), **036 → then 046 or 047** (stream 3; 036 and 046 both touch the route ledger), **072** (stream 4), **060** (stream 5).
The first gates to clear for speed: **079** (uac-r2 review → 039 → 037 → 031), **098** (→ 106 → 031, 066, 067) and **110** (→ 056 → 058).

## Critical path to 031 (the evaluator — writes the first real Signals)

**031 must not start until 007, 008, 009, 024, 039, 037, 098 and 106 have landed** (007, 008, 009 and 024 are done). D-13 requires the episode-scoped key
before the first Signal is written; D-33 drops D-15's skip, so 031's **first write** on a Secure matter must already be
owned by the Secure Record Owners team, which only **039** provides (otherwise the first Secure-matter Signals are
written with the wrong visibility and need a re-own); D-25's per-item today is only testable once Do-lane Signals exist.

```
NOW ──┬─ 079 uac-r2: record 2 live refusals (before 008's SRO edit) + review request ──────────────┐
      ├─ 007 schema (episode key, severity, v4 columns) 3-4 h                                     │
      │     └─ 008 role grants incl. D-33's three (deploy order: before the BFF with the config) │
      │           ├── 009 re-seed POL-COMMIT-BUDGET v2 (1-2 h) ─────────────────────────────────┼──────┐
      │           └── 039 secure children: lineage + owner-role config + writer resolver path ──┘      │
      │                    (~3 dev-days; needs uac-r2's review from 079 and batch 4 in dev)              │
      └─ 024 grammar extension (6-8 h) ─────────────────────── 037 writer: Do subjects, D-31, D-33 ──────┤
                                                                     (after 039: same file)               ├─> 031
         098 Date Only + shared time-zone helper (in progress, own PR; parked behind #1309) ─────────────┘
```

- **Before 031**: 007 → 008 → **039** → 037, with 079 started now (its refusals must be recorded before 008's Secure
  Record Owner edit; its uac-r2 review gates 039), 024 in parallel from now (it feeds 037), 009 in parallel with
  039/037, and **098 merged** (031 reuses its `DataverseUserTimeZone` helper for D-25). Already done: 003, 030.
- **The new longest pole is 039 (~3 dev-days)**, and it carries two outside dependencies: uac-r2's review of the two
  list entries (079) and uac-r2 batch 4 deployed in dev.
- **After 031 (by design)**: **025 Threshold** — it compiles to the same compiled-predicate contract, and its
  acceptance proves the 031 job runs it unchanged; **026 describer**; 032-034; 038 read route.
- **The external gates are 098** (waiting on #1309) **and uac-r2** (review + batch 4 in dev). If either slips, 031
  slips: escalate rather than writing a second time-zone helper or a parallel ownership mechanism.

## Critical path to project exit

```
007 → 008 → 039 → 037 → 031 → 034 → 050 → 038 ─┐
110 → 056 ─────────────────────────────────────┼→ 058 → 059 → 055 → 064 → 105 → 090
036 → 044 ┐  040 ┐                              │
046 ──────┼──────┼→ 043 ──────────────────┘          074 (recall gate, can fail the project) → 090
070 ──────┘
```

**021 was the longest single pole and is done.** The new poles are the **decision wizard** (056 → 058, ~6-8 days)
and the **commit route** (043, which composes 040, 044, 046, 070). **074 can still fail the project.**

---

## Registry

### Phase 0 — Foundations

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 001 | [Schema: the four missing columns](001-schema-complete-missing-columns.poml) | FULL | sonnet/high | — | — | **Unblocks everything.** Two columns block a path outright |
| ✅ [done] 002 | [Privilege re-verify](002-privilege-reverify-after-column-adds.poml) | STANDARD | sonnet/medium | 001, 006 | — | Owner chose **option A** (dedicated identity, task 006); completes when the union check passes for the new writer |
| ✅ [done] 003 | [ADR-040 amendment + ADR-039 exception](003-adr-040-amendment-and-039-exception.poml) | FULL | **opus/xhigh** | — | — | ⚠️ `.claude/` → **main session only**. Must merge before/alongside 031 |
| ✅ [done] 004 | [Seed Policy + taxonomy rows](004-seed-policy-and-taxonomy-rows.poml) | STANDARD | sonnet/medium | 001 | — | Policy stays `enabled = No` until reviewed. `sprk_policy` GUID `4d204810-61bf-f111-aaaf-0022482913fc`; `sprk_policyversion` GUID `42b3e716-61bf-f111-aaaf-0022482913fc`. Taxonomy rows already enabled (no-op) |
| ✅ [done] 005 | [Seed dev data + 2 negative controls](005-seed-dev-data-and-negative-controls.poml) | STANDARD | sonnet/medium | 004 | — | Live-classified via `/api/office/save`; see `notes/seed-data-state.md` for all GUIDs + seeded-vs-real |
| ✅ [done] 006 | [Provision the dedicated writer identity](006-provision-dedicated-writer-identity.poml) | FULL | **opus**/high | — | — | **Owner: option A** for 002. Azure changes need owner confirmation |
| ✅ [done] 007 | [Schema: v4 data needs + episode-scoped dedupe key](007-schema-v4-data-needs-and-episode-key.poml) | FULL | sonnet/high | 001 | — | **D-13** key `…|{episode}` (re-key dev rows as episode 1); `sprk_signal.sprk_duedate`; policy **severity (D-30)** / short name / work type / retired reason; `sprk_decisionplan`; record `sprk_steps` / `sprk_followons` / `sprk_gatetier`. Records the `sprk_matter` required level (O-21). **Before 031** |
| ✅ [done] 008 | [Owner-approved role edits + assignment + union re-verify](008-owner-approved-role-edits.poml) | FULL | **opus**/high | 007 | L | **D-29** writer AppendTo on communication/event/todo/WA (closes F26) · **D-18** writer Create budgetrevision · **D-14** admin Write policyversion · **D-22** admin C/W/R triagecategory, role to owner, 5 tables read-only in Spaarke Platform. **D-33**: Secure Record Owner Read on both tables, Basic User Basic Read on both, Ontology Service Assign on DR; AppendTo at Organization depth; **role edits before the BFF with the secure-child config**. Console User Signal-write removal is **049**. **Before 031** · **[uac]** |
| ✅ [done] 009 | [Re-seed POL-COMMIT-BUDGET as v2](009-reseed-path-b-policy-v2.poml) | STANDARD | sonnet/medium | 007, 008 | — | Classification-phrased template, short name, work type, plan `send-budget-inquiry, revise-budget, approve-variance`; stamp v1 `sprk_inforceto` (D-14). Reconciliation's "008 re-seed", renumbered. **Before 031** |

### Phase 1 — Cleanup that gates the row

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 010 | [C-1 dead briefing hooks + 2 stale comments](010-c1-remove-dead-briefing-hooks.poml) | FULL | sonnet/high | — | **A** | The misdirection hazard. Gates the row |
| ✅ [done] 011 | [C-3 disambiguate MetricCard](011-c3-disambiguate-metriccard.poml) | STANDARD | sonnet/medium | — | **A** | Two unrelated components share the name |
| ✅ [done] 012 | [C-4 generic status badge](012-c4-generic-status-badge.poml) | FULL | sonnet/high | — | **A** | The one legitimately-new UI primitive |

### Phase 2 — Policy and rule bodies

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 020 | [`Existence` rule type + JSON Schema](020-existence-rule-type.poml) | FULL | sonnet/high | 003 | **B** | Without it the capability is unsavable |
| ✅ [done] 021 | [**The predicate compiler**](021-predicate-compiler-exists-notexists.poml) | FULL | **opus/xhigh** | 020, 005 | — | 🔴 **THE RISK ITEM.** Serial. `notExists` has no in-repo template |
| ✅ [done] 022 | [Rule-body validation refusal](022-rule-body-validation-refusal.poml) | FULL | sonnet/high | 020 | — | `PolicyVersionValidator` seam for task 031; owner: **validate at evaluation**, fail closed (app authoring stays). **Done 2026-10-05**: 3 independent reviews, full suite 14541/0 failed, +0.01 MB |
| ✅ [done] 023 | [Scope semantics + policy defaults](023-scope-semantics-and-policy-defaults.poml) | FULL | sonnet/high | 003 | **B** | Copy `CommunicationRuleGate` verbatim; fail closed |

### Phase 2b — Rule grammar (D-16)

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 024 | [Grammar extension: subject-only rules, now+Nd, Do subjects, quiet-window knob](024-grammar-extension-do-lane.poml) | FULL | **opus**/high | 021, 022 | K | **D-16**, **D-13** (FR-17a knob). Still one filter per body; everything else still refused. **Before 031** (feeds 037) · **[uac]** |
| 🔲 [open] 025 | [Threshold rule type: schema + compiler (spend threshold)](025-threshold-rule-type-schema-and-compiler.poml) | FULL | sonnet/high | 024, 031 | C | **D-16**. Proves the 031 job runs it with **no evaluator change**. Switch stays refused. **After 031** |
| ✅ [done] 026 | [Rule-body describer (plain-language condition, read-side)](026-rule-body-describer.poml) | FULL | sonnet/high | 024 | — | #11. Feeds the wizard's *How this was determined* (058) and the admin rule page (100) |

### Phase 3 — Evaluator, Signal lifecycle and read

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 030 | [Signal writer + dedupe + **ownership**](030-signal-writer-dedupe-and-ownership.poml) | FULL | sonnet/high | 021, 006 | — | Two independent reviews. Writer owns, owning BU from matter; create-first + reconcile; fail-closed MI; refusals logged (EventId 50300) + metered. Live as writer: matter PASS; **communication blocked by F26 (owner role decision)** |
| 🔲 [open] 031 | [Nightly re-evaluating `IScheduledJob` (+ episodes, secured skip, per-item today)](031-nightly-reevaluating-scheduled-job.poml) | FULL | sonnet/**xhigh** | 030, 003, 007, 008, 009, 024, 037, 039, 098, 106 | — | ONE evaluator, not evaluator + sweep. **Amended 2026-10-07**: episode key before the first Signal (**D-13**); **no Restricted/Limited skip** — Secure-matter ownership via 039, one narrow skip (**D-33**); Off→On re-raise (**D-32**); per-item today via 098's helper (**D-25**); witness evidence lines (**D-23**); side-effect-free `Evaluate(version, scope)` · **[uac]** |
| 🔲 [open] 032 | [Three event triggers (D-48)](032-event-triggers-classification-and-budgetrevision.poml) | FULL | sonnet/high | 031 | — | Budget-revision hook is correctness, not polish. **Amended**: shared core (D-13, D-33); never re-closes a decision-Acted Signal (X-6). **Association-confirmed trigger added (D-48)** · **[uac]** |
| 🔲 [open] 033 | [Closure semantics](033-closure-semantics.poml) | FULL | sonnet/high | 031 | C | Aged-out maps to `ConditionCleared`. **Amended**: Superseded → new episode (D-13), reads `sprk_inforceto` (D-14); Off (next pass, reversible: **D-32**) vs Retire (immediate) |
| 🔲 [open] 034 | [Suppression per (policy, matter), 30d](034-suppression-policy-matter-30-days.poml) | FULL | sonnet/high | 031, 036 | C | D-11 grain; auto-close never counts. **Amended**: lapse → new episode (D-13); *misresolved* never counts (R-10); count read for wizard/admin · **[uac]** |
| 🔲 [open] 035 | [**Deploy** BFF](035-deploy-bff-evaluator.poml) | FULL | sonnet/high | 034 | — | Merge master first; hash-verify |
| ✅ [done] 079 | [uac-r2 coordination: two recorded live refusals + review of the secure-child entries](079-issue-secure-record-mirror-for-signals.poml) | STANDARD | sonnet/medium | — | — | **D-33** (was the D-15 "later mirror" issue). Refusals must be recorded **before** 008's Secure Record Owner edit; the review gates 039. **Start now** · **[uac]** |
| 🔲 [open] 039 | [**Secure children: register Signal + Decision Record; writer owner from the resolver**](039-secure-signals-and-decision-records.poml) | FULL | **opus**/high | 007, 008, 079 | — | 🔴 **D-33** (replaces D-15's skip): two entries each in `SecureChildLineage.cs` and `config/secure-record-owner-role.json` (uac-r2 review via 079); task-146-shaped writer change; 2-minute secure-sync load measured; live gate on a provisioned Secure matter. ~3 dev-days. **Before 031** · **[uac]** |
| 🔲 [open] 037 | [Signal writer accepts Do-lane subjects (event, To Do, WA)](037-signal-writer-do-lane-subjects.poml) | FULL | sonnet/high | 007, 008, 024, 039 | — | **D-16** matter via `sprk_regardingmatter`; `sprk_duedate` (D-27) refreshed while open. Decides + flags project-filed tasks (D-16); **D-31** matterless To Do owned by the To Do owner's BU; **D-33** resolver parents + narrow skip. **Before 031** · **[uac]** |
| 🔲 [open] 038 | [BFF Signal read route: matter-access filter, Do-lane reader scope, order](038-signal-read-route-and-do-lane-scoping.poml) | FULL | **opus**/high | 050, 031, 037 | — | **D-15** (stands under D-33). #1312 census. Event "mine" = 097 decision B; WA in **both** assigner's and assignee's Do lane (**D-44**); rank **D-42**; no-core rows owner-only (D-35) · **[uac]** |

### Phase 4 — The Decision Record

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| 🔲 [open] 040 | [Decision Record writer + record class](040-decision-record-writer.poml) | FULL | sonnet/high | 001, 030, 007, 036, 008, 039 | — | BR-1 **as reversed**. **Amended**: ONE record per review with `sprk_steps` / `sprk_followons` / `sprk_gatetier`, class from the 036 catalog, called last by 043 (**D-17**); **secure child, still append-only** (owner from the resolver, Assign only) (**D-33**) · **[uac]** |
| 🔲 [open] 041 | [Append-only + relationship direction](041-append-only-and-relationship-direction.poml) | FULL | sonnet/high | 040 | **D** | Test as a real non-admin; 1 → N, FK on the Signal |
| 🔲 [open] 042 | [Wire writer into the consumer](042-wire-writer-into-rulegated-consumer.poml) | FULL | sonnet/high | 040 | D | One line before the branch. **Do not touch the gate**. **Amended**: one-step review shape (D-17); O-20 dissolved by D-33 · **[uac]** |

### Phase 4b — Commit and actions (D-17, D-18, D-19, D-21)

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 036 | [Closed BFF action catalog + decision-plan read endpoint](036-action-catalog-and-decision-plan-read.poml) | FULL | sonnet/high | 007 | L | Approve variance record-only (**D-19**); dismissal reasons per lane; reassign / extend **Routine (D-45)**; adds record-the-response (D-54) and assign-work as a plan action (D-51) · **[uac]** |
| 🔲 [open] 043 | [**The decision commit route** (record last, idempotent)](043-decision-commit-route.poml) | FULL | **opus**/high | 040, 036, 044, 046, 070 | — | 🔴 **D-17**: validate → writes → email last → one record → close. Failure = no record + which writes landed. Confirm = confirmation; gate tier from a pure `PublicContracts` function (**D-52**) · **[uac]** |
| 🔲 [open] 044 | [Executors: Revise budget, Approve variance, Do actions, Next steps](044-decision-action-executors.poml) | FULL | sonnet/high | 036, 008, 047 | — | **D-18** writer creates the revision after a caller check; budget amount written **as the signed-in user (D-55)**; reschedule writes `sprk_duedate` (D-27); record-the-response writes the **D-54** columns (047) · **[uac]** |
| 🔲 [open] 046 | [Server-side work-assignment create](046-server-side-work-assignment-create.poml) | FULL | **opus**/high | 008 | — | **D-21**. Preserve the wizard's BU cascade; area owner reviews · **[uac]** |
| ✅ [done] 047 | [Schema: response columns on `sprk_workassignment` (D-54)](047-schema-work-assignment-response-columns.poml) | FULL | sonnet/high | — | — | **D-54**: responded-on + outcome (exact set agreed with the WA owner); 007 recipe; WA is a secure root · **[uac]**. Feeds 044, 061. **Start now** |
| 🔲 [open] 048 | [D-66: move shared Dataverse write core (`OwnedChildWrite` + helpers) out of `Services/Ai` → `Services/Dataverse`, own PR (uac-r2 review)](048-move-dataverse-write-core-out-of-ai-namespace.poml) | FULL | sonnet/high | — | C | **D-66**; starts after uac-r2 replies on #1355; sequence after #1391 (same file) **[uac]** |
| 🔲 [open] 049 | [Remove Console User Write on `sprk_signal`](049-revoke-console-user-signal-write.poml) | STANDARD | sonnet/medium | 043, 055 | — | **D-17**, after the deployed Console closes Signals only via the BFF. Union re-verify · **[uac]** |

### Phase 5 — The worklist surface

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| 🔲 [open] 050 | [Worklist grid configuration row (membership + order)](050-worklist-gridconfiguration-row.poml) | STANDARD | sonnet/medium | 034, 007 | — | Membership by rule evaluation, never a user filter. **Amended**: per-lane + 3 secondary sets; executed server-side by 038 (D-15); no actions |
| 🔲 [open] 051 | [**The one row component: MatterCard + IssueLine**](051-worklist-row-component.poml) | FULL | **sonnet/xhigh** | 057, 012, 007 | — | 🔴 A second row component is a design failure. **Amended**: v4 row (no controls in a line; whole line opens the wizard); UI-composed headline (**D-23**). Builds on fixtures |
| 🔲 [open] 052 | [Extend MetricCard (count filters); C-9 RowActionMenu cleanup](052-extend-metriccard-rowmenu-outcomecard.poml) | FULL | sonnet/high | 051 | E | **D-24** selected / note / progress. DocumentRowMenu/OutcomeCard reuse **dropped (D-46)**; C-9 continues on its own (shared UI: own PR) |
| 🚫 [cancelled] 053 | [~~Gate host + acting~~](053-gate-host-and-acting.poml) | FULL | sonnet/high | — | — | **Superseded 2026-10-07** by **058** (wizard) + **043** (commit route), per D-17 / D-26. POML status `deferred`; do not execute |
| 🔲 [open] 054 | [Reconciliation tab + aggregate item](054-reconciliation-tab-and-aggregate-item.poml) | STANDARD | sonnet/medium | 051, 057 | E | ONE registration (already exists, #18 — verify). **Amended**: AggregateCard at the top of the Do lane; count from the Email Review feed (#36) |
| 🔲 [open] 055 | [**Deploy** Console + shared components](055-deploy-console-and-shared-components.poml) | FULL | sonnet/high | 059, 043, 035 | — | **Real Dataverse** verification. **Amended**: one full wizard review (2 actions + 1 Next step) live; BFF with 038/043 deployed first |
| ✅ [done] 057 | [Console UI kit: EvidenceLine, StatusBar, RecordRow, AggregateCard](057-console-ui-kit.poml) | FULL | sonnet/high | 012 | K | **D-24** reuse first (Accordion, MessageBar, Link, ConfirmModal, StatusBadge + success). Can start now |
| 🔲 [open] 058 | [**The decision wizard** (WizardShell, in-app)](058-decision-wizard-consumer.poml) | FULL | sonnet/**xhigh** | 056, 057, 036, 026, 034, 043 | — | **D-26**, **D-17**: writes nothing itself. **Templated (non-AI) drafts (D-53)**. Build on fixtures before 043 is live |
| 🔲 [open] 059 | [Worklist widget assembly](059-worklist-widget-assembly.poml) | FULL | sonnet/high | 038, 051, 052, 054, 058 | — | Two lanes, honest filters, narrative slot, aggregate row, disclosures; reads only via 038 (**D-15**); matterless To Do grouping = **O-21** |
| 🔲 [open] 045 | [Decision Record tab (D-47)](045-decision-record-tab.poml) | STANDARD | sonnet/medium | 058, 043 | — | In R1 (**D-47**) · **[uac]** |

### Phase 5b — Modal system (D-26)

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 110 | [ADR-050 path B amendment (WizardShell preset, in-app launch rule)](110-adr-050-amendment-wizard-preset-and-launch-rule.poml) | FULL | **opus**/high | — | — | ⚠️ `.claude/` → **main session only**. Amendment text is in the POML `<notes>`. Can start now |
| ✅ [done] 056 | [WizardShell: characterization tests, re-base on SprkModal, v4 props, delete WizardModal](056-wizardshell-rebase-on-sprkmodal.poml) | FULL | **opus**/high | 110 | — | Modal note P1-P3. Embedded markup must not move |
| 🔲 [open] 111 | [**Deploy** + regression of non-embedded WizardShell consumers](111-wizard-consumer-regression-and-deploy.poml) | FULL | sonnet/high | 056 | — | Modal note P4; SemanticSearchControl PCF last (`build:prod`) |
| ✅ [done] 112 | [Migrate Create wizards off `navigateTo` (in-app host)](112-migrate-create-wizards-in-app.poml) | FULL | **opus**/high | 056 | — | [PR #1422](https://github.com/spaarke-dev/spaarke/pull/1422) **open, awaiting owner merge** (head `0b2ec81bc`). `InAppWizardHost` mounted in the Console; bundle +3,584 B. Filed #1420, #1421 |
| 🔄 [in-progress] 113 | [Migrate remaining wizards off `navigateTo`](113-migrate-remaining-wizards-in-app.poml) | FULL | sonnet/high | 112 | — | P5 part 2: Summarize Files, Upload Documents, Find Similar, Workspace layout. **+ #1421** (create-project widget) **+ #1420** (Work Assignment completion) (no-parking sweep). [PR #1480](https://github.com/spaarke-dev/spaarke/pull/1480) open, not merged |
| 🔲 [open] 114 | [**Deploy** the in-app wizard migration](114-deploy-in-app-wizard-migration.poml) | FULL | sonnet/high | 111, 113, 121 | — | Verify all nine in-app on dev, full page and in an Xrm dialog |

### Phase 6 — Do lane and Briefing fold-in

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 060 | [ISS-003 stranded Draft events](060-iss003-stranded-draft-events.poml) | FULL | sonnet/high | — | — | D-74 (2026-10-08): 49 Draft classified; 0 platform-created; owner opened 1 (8a6b371f), 48 left Draft; issue #1050 closed; see `notes/060-draft-classification.md` |
| 🔲 [open] 061 | [Do lane: four policies as subject-only rules (incl. overdue To Do)](061-do-lane-temporal-policies.poml) | FULL | sonnet/high | 060, 034, 024, 036, 037, 047 | — | **Amended (D-16)**: evaluated by the one evaluator; the collector is the reference, **not modified**. Overdue starts at **1 day (D-43)**; WA rule reads the **D-54** response columns; **fourth policy: overdue To Do for ALL To Dos (D-31)** |
| 🔲 [open] 062 | [Know → narrative; retire Critical Today; remove LLM Top action](062-know-narrative-and-retire-critical-today.poml) | FULL | sonnet/high | 061, 038, 059 | F | Rank is deterministic or it is not explainable. `sprk_highpriority` is the 2nd rank key (**D-42**) |
| 🔲 [open] 063 | [First Know-promotion rule](063-first-know-promotion-rule.poml) | STANDARD | sonnet/medium | 061, 036, 046 | F | Offers **Assign Work (D-51)**; needs a (matter, budget) verified join — escalates |
| 🔲 [open] 064 | [Replace the Briefing tab](064-replace-briefing-tab-with-worklist.poml) | FULL | sonnet/high | 062, 063, 055, 059, 065 | — | Only once BOTH lanes exist. **Amended**: HANDOFF §1.4 as the cutover check |
| ✅ [done] 065 | [Daily Briefing reads `sprk_duedate`](065-briefing-reads-sprk-duedate.poml) | FULL | sonnet/high | 098 | R | **D-27**; other `sprk_finalduedate` readers listed, not changed |
| 🔲 [open] 066 | [Deprecate `sprk_eventstatus`: inventory, move readers to `statuscode`](066-deprecate-sprk-eventstatus-inventory.poml) | FULL | sonnet/high | 098 | — | **D-28**. Column removal needs the owner after the inventory |
| 🔲 [open] 067 | [To Do composite score on calendar days, one shared function](067-todo-composite-score-calendar-days.poml) | FULL | sonnet/high | 081, 098 | R | **D-29**; boards re-rank once |

### Phase 7 — Inquiry and the classifier

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| 🔲 [open] 070 | [The Inquiry: Action + Binding, an executor the commit route calls](070-inquiry-action-and-binding.poml) | FULL | sonnet/high | 001, 036 | — | The Action **row**, not the Action **Engine**. **Amended**: SLA deferred (**D-20**); record written by 043 (D-17); **no response-due date (D-56)**; no chat session (D-52) |
| 🔲 [open] 071 | [Disposition accrual](071-disposition-accrual.poml) | FULL | sonnet/high | 070 | **G** | Do-rule action rate reads `sprk_resolutiontype` |
| ✅ [done] 072 | [Guidance injection](072-classifier-guidance-injection.poml) | FULL | sonnet/high | 004 | **G** | Prompt changes; the schema `enum` does NOT |
| 🔲 [open] 073 | [`sprk_memo` as source #2](073-memo-as-second-signal-source.poml) | FULL | sonnet/high | 030, 072, 037, 039, 031 | **G** | The consumer diff must be **empty** |
| 🔲 [open] 074 | [**Recall measurement (exit gate)**](074-classifier-recall-measurement-gate.poml) | FULL | **opus/xhigh** | 072 | — | 🔴 **CAN FAIL THE PROJECT.** ≥80% on ≥50 items; writes **three recall columns on `sprk_triagecategory` (D-49)** after `/conflict-check` with the email project |

### Phase 8 — Remaining cleanup and repairs

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 080 | [The six hazards](080-six-hazards-cleanup.poml) | FULL | sonnet/high | — | **H** | Fixes relocated to their own PRs: C-10→084, C-5→085, C-19→086, C-22→088, C-23→[#1114](https://github.com/spaarke-dev/spaarke/pull/1114). C-21: owner chose **delete** (in 086) |
| ✅ [done] 081 | [On-branch cleanup the worklist needs (C-8, C-11, C-13, C-17)](081-duplication-cleanup.poml) | FULL | sonnet/high | 084 | — | C-13 waits for **084 merged**; C-17 = **3/7/10 days** (owner)  — **2026-10-05: own PR to master [#1309](https://github.com/spaarke-dev/spaarke/pull/1309)** (round 3; reviews 1-2 FAILED); SmartTodo palette everywhere; 38 packages build. Third review FAILED (no runtime regression): ~12 sites not asking for the Xrm capability they use, two hand-rolled walks + one EmptyState copy missed, feed Overdue filter still UTC. Round 4 running |
| ✅ [done] 082 | [Tokenizer repair](082-matter-number-tokenizer-repair.poml) | FULL | sonnet/high | — | **H** | Ship with a **measured** query-count delta |
| ✅ [done] 083 | [Association `reason` string](083-association-reason-string-repair.poml) | STANDARD | sonnet/medium | — | **H** | AP-12 in runtime prose |
| ✅ [done] 084 | [To-Do scorer, own PR (C-10)](084-todo-scorer-own-pr.poml) | FULL | sonnet/high | — | I | [PR #1118](https://github.com/spaarke-dev/spaarke/pull/1118) **merged** `b5b0c0ce0`. **Live bug.** Merge first; 081 depends on it |
| ✅ [done] 085 | [Compose cleanup, own PR (C-5, C-16)](085-compose-cleanup-own-pr.poml) | FULL | sonnet/high | — | I | [PR #1117](https://github.com/spaarke-dev/spaarke/pull/1117) **merged** `aad1c877f`. C-5 already lost comment data |
| ✅ [done] 086 | [Dead + misleading code, own PR (C-19, C-21, C-14, C-20, C-26, C-6, C-27)](086-dead-code-own-pr.poml) | FULL | **opus**/high | — | I | [PR #1120](https://github.com/spaarke-dev/spaarke/pull/1120) **merged** `a082cfcda`. C-21 deleted (owner). Closes #1112, #1113 |
| ✅ [done] 087 | [Events leftovers, own PR (C-2, C-24, C-25)](087-events-leftovers-own-pr.poml) | FULL | sonnet/high | — | I | [PR #1119](https://github.com/spaarke-dev/spaarke/pull/1119) **merged** `e24ad4d20`. Also dropped 2 more unused EventsPage deps |
| ✅ [done] 088 | [InsightSummaryCard, own PR (C-22)](088-insight-summary-card-own-pr.poml) | FULL | sonnet/medium | — | I | [PR #1116](https://github.com/spaarke-dev/spaarke/pull/1116) **merged** `71394e0d3`. Web resource: needs a deploy after merge |
| ✅ [done] 089 | [Shared building blocks, own PR (C-7, C-12, C-15)](089-shared-building-blocks-own-pr.poml) | FULL | sonnet/high | — | I | [PR #1121](https://github.com/spaarke-dev/spaarke/pull/1121) **merged** `ec7211aaf`. All ~40 deferred sites converged; case-semantics audit in PR body. #1118 and #1121 must merge before 081 |
| ✅ [done] 091 | [Unaudited sweeps, own PR (C-18)](091-unaudited-sweeps-own-pr.poml) | FULL | sonnet/high | — | I2 | [PR #1122](https://github.com/spaarke-dev/spaarke/pull/1122) **merged** `48d4fad04`. 4 colour violations fixed; 3 cards documented as different; SECTION_REGISTRY kept with consumer cited |
| ✅ [done] 092 | [Master build + test failures, own PR](092-master-build-and-test-failures-own-pr.poml) | FULL | sonnet/xhigh | — | I2 | [PR #1123](https://github.com/spaarke-dev/spaarke/pull/1123). 6 PCF builds fixed (broken since the 2026-08-14 CVE bump); 17 failing suites fixed or evidenced. ⚠️ **2026-10-05: its "FR-02 section height" change is WITHDRAWN** — it would have regressed the owner's R2 UAT §5.6 row-height fix on the DataGrid sections (`803c77ace1`; test (h) was stale, not the code). Agreed with uac-r2: #1123 keeps the four equivalent test files and merges BEFORE [#1293](https://github.com/spaarke-dev/spaarke/pull/1293), which rebases. **2026-10-05: full nightly run 18/18 PCFs pass** ([run 37260945864](https://github.com/spaarke-dev/spaarke/actions/runs/37260945864), head `7f53d3021`). Root cause of the last failures: PCFs compile SOURCE from `Spaarke.Communication.Components` / `Spaarke.Visuals`, whose imports resolve from the shared folder, so those packages must be installed (workflow + release build Step 1 now do). c2780ce29's VisualHost dep move fixed nothing and is reverted |
| ✅ [done] 093 | [CI: nightly advisory PCF build:prod, own PR](093-ci-nightly-pcf-build-prod-own-pr.poml) | FULL | sonnet/high | — | I2 | [PR #1282](https://github.com/spaarke-dev/spaarke/pull/1282) + fix [PR #1285](https://github.com/spaarke-dev/spaarke/pull/1285) **merged**. Nightly PCF build check now reports real results (10 fail / 8 pass on master, matching the logs) |
| ✅ [done] 094 | [PCF deploy procedures verify the real build result, own PR](094-pcf-deploy-build-verification-own-pr.poml) | FULL | **opus**/high | — | I2 | [PR #1286](https://github.com/spaarke-dev/spaarke/pull/1286) **merged 2026-10-05** (`b9dcae48b`): every PCF build judged by its output; release build builds each PCF in production mode (14/14 shared libs + 19/19 PCFs from a clean checkout); Step 1 dependency order fixed; ThemeEnforcer `build:prod`. Pinned notice [#1308](https://github.com/spaarke-dev/spaarke/issues/1308) |
| ✅ [done] 095 | [Flaky email-attachment regex timeout, own PR](095-flaky-email-attachment-regex-timeout-own-pr.poml) | FULL | sonnet/high | — | I2 | [PR #1287](https://github.com/spaarke-dev/spaarke/pull/1287) **merged 2026-10-05** (`b4b58a361`). Test now exercises production code (it tested a private copy without the catch); production unchanged |
| ✅ [done] 096 | [Json.Schema.Net concurrent Evaluate race in AI tool-schema validation, own PR](096-json-schema-net-metaschema-race-own-pr.poml) | FULL | **opus**/high | — | I2 | Found by 022's second review: `MetaSchemas.Draft202012.Evaluate` unlocked at `AnalysisToolService.cs:527` + `ToolHandlerToAIFunctionAdapter.cs:919` can pass malformed tool schemas under concurrency. [PR #1294](https://github.com/spaarke-dev/spaarke/pull/1294): one shared lock (both sites share one instance, so per-site locks still leaked 75-111/1000); 0 false-valid over 18 rounds. Review PASS-WITH-FINDINGS, all fixed (verdict cache; issue #1295; guard test; +437 bytes). **ADR-009 path A approved by owner 2026-10-05** (spec ADR Tensions). **Merged 2026-10-05** (`23bdc5679`) |
| ✅ [done] 097 | [Event "complete" status code + To Do reassign lookup target, own PR](097-event-complete-and-todo-reassign-write-paths-own-pr.poml) | FULL | **opus**/high | — | I2 | Found 2026-10-05 checking v4 actions: event complete writes statuscode 5 (live Completed = 659490002?); To Do reassign binds `sprk_assignedto` to systemusers (live target contact?). Prove against live schema, then fix. **Proven + wider**: BFF event status codes were invented (3/5/6/7), so BFF event create/complete/cancel/soft-delete all fail live (HTTP 400); side-pane Completed too; To Do + follow-on assignee binds. [PR #1302](https://github.com/spaarke-dev/spaarke/pull/1302); round 2 (`5415ea6d8`): **every BFF event read also failed live** (bad `$expand` case, lookup selected as a value, nonexistent column, `$skip`), so the events API was broken end to end; priority, regarding lookup, external-SPA events (never worked), To Do Dismissed (dismissed never regenerated) all fixed and proven live; BFF suite 14302/0 failed on rerun. Owner decisions pending: what counts as open/overdue event work; two status columns; reschedule date column. **Review 1 FAILED** (`sprk_eventlog.sprk_description` does not exist → create/delete/PUT-status write then return 500; regarding fields left inconsistent on re-parent; external status from the wrong column); round 4 (`9e427d5c9`) fixed F1-F10 and **proved the real routes live** (in-process BFF host → spaarkedev1): all pass; **on master, event create/get/list/logs/complete all return 500**. Full suite 14320/0 failed. Second review FAILED: **routes have no record-level authorization** (would expose every event once fixed) — owner: apply the solution's established record-access pattern; core-ancestor stamp missing/wiped (I-1 defect); regarding type misread; **owner: convert the six event date columns to Date Only** (ConvertDateAndTimeBehavior on existing rows). Round 5 (`05d6df240`): record-level authorization via the solution's existing pattern (Dataverse checks the caller's rights; proven with a real low-privilege user), stamp, regarding, tests; suite 14356/0 failed. Dates split to task 098. Third review PASS-WITH-FINDINGS; round 6 under the owner directive: I-6 ownership on create (child-BU users could not reach their own events), AppendTo on the parent, Create-privilege check, list-impersonation test, 401 vs 403. Round 6 (`28e9ecd36`) done, proven with a real child-BU user; suite 14371/0 failed; focused review of round 6 running. **MERGED 2026-10-07 as `87665c3da`** after 11 rounds; reconciled with #1312 (one auth mechanism); owner decisions A (Reassigned completable, one IsOpenWork predicate) + B (my events = owner/assignee/creator) implemented; all To Do rules paged; regarding number from the catalog. See POML `<completion>` |
| ✅ [done] 098 | [`sprk_event` date columns: UserLocal → Date Only, own PR](098-event-date-columns-date-only-own-pr.poml) | FULL | **opus**/high | 097 | I2 | Owner 2026-10-05: switch `CanChangeDateTimeBehavior` on, convert six columns to Date Only (one-way), UTC conversion, hand-correct 5 values; record for `SPRKDOCINTELLIGENCE`; code PR after #1302  — **Dataverse part done in dev 2026-10-06**: six columns Date Only, conversion job Succeeded, 120/120 values on the intended day (5 hand-corrected); procedure in `docs/data-model/sprk_event-date-columns.md`. Dev side-effect: ribbon/EventsPage complete now 400 and several readers show the previous day — being fixed in the same PR |
| ✅ [done] 106 | [`sprk_todo` date columns → Date Only, own PR](106-todo-date-columns-date-only-own-pr.poml) | FULL | **opus**/high | 098 | I2 | **D-41** (found by 024): To Do due dates are UTC timestamps of local midnight; convert like 098 (inventory first, evidence, fix readers/writers, per-env procedure). **031 depends on it** (per-item today, D-25) |
| ✅ [done] 068 | [D-63: remaining `sprk_finalduedate` readers → `sprk_duedate` (playbook, VisualHost card, CalendarVisual), own PR](068-finalduedate-readers-to-duedate.poml) | FULL | sonnet/high | 098 | E | **D-63** (owner 2026-10-07): final due date informational everywhere; live playbook node "Query Overdue Tasks" change approved |
| 🔄 [wip] 120 | [ISS-018: restore the notification playbooks (id lists, Condition operands, item templates, Due Soon, scheduler)](120-notification-playbooks-iss018-fix.poml) | FULL | **opus**/high | — | F | **D-77..D-80**: no notification has been delivered in dev since at least 2026-07-11 (#1452). Own PR; dev deploy needs separate owner approval |
| 🔲 [open] 121 | [D-81: LegalWorkspace 106 date filters live on dev with no tenant-wide publish; settle the LW deploy path + scoped solution import](121-legalworkspace-deploy-without-tenant-publish.poml) | FULL | sonnet/high | — | — | Fact-finding first: LW source is compiled into the Console (`sprk_corporateworkspace` retired; the Custom Page's PCF source is gone), so D-75 may already have shipped it. Script fix = own PR. **Import: owner approval required.** 114 depends on it |
| 🔲 [open] 122 | [D-81: external SPA dev deploy (Static Web App) with 056/098/106/111; close #1428](122-external-spa-dev-deploy.poml) | FULL | sonnet/high | — | — | Target is SWA `green-dune-…` via `deploy-external-spa.yml` (CIAM values non-secret, in the workflow + `config/environments.json`); last run 07:11Z predates 056/106. **Dispatch: owner approval required** (ship list) |
| 🔲 [open] 123 | [ISS-010 #1387 triage category honours enabled/active; ISS-002 #1049 every $choices degrade path counted + Error](123-triage-category-resolution-and-choices-visibility.poml) | FULL | sonnet/high | — | J | **Branch** (builds on 072's `AdditionalFilterFor` + `ChoicesResolutionTelemetry`). **Alert rule: owner approval required** |
| 🔲 [open] 124 | [ISS-017 #1447 GridOverviewHandler `{{today}}` = caller's day (D-25), own PR](124-grid-overview-today-user-local-own-pr.poml) | FULL | sonnet/high | — | J | Reuse `DataverseUserTimeZone` (copy `EventCompletionDate.cs`) |
| 🔲 [open] 125 | [ISS-001 #1048 drifted AI action mirrors + `Create_Task_From_Email` 400s](125-action-mirror-drift-suggest-followups-create-task.poml) | FULL | sonnet/high | — | J | Diagnosis read-only; **any `sprk_analysisaction` write: owner approval required** |
| 🔲 [open] 126 | [ISS-009 #1399 G5 AppendTo for business-owned lookup targets (uac-r2's rule), own PR](126-appendto-business-owned-lookup-targets-uac.poml) | FULL | **opus**/high | — | — | Gate: #1391 merged + uac-r2 names the rule on #1355; same file as 048 · **[uac]** |
| 🔲 [open] 127 | [ISS-014 #1412 SpaarkeAi ribbon build fails in a clean checkout (Deploy SpaarkeAi red on master), own PR](127-spaarkeai-ribbon-build-clean-checkout-own-pr.poml) | FULL | sonnet/**xhigh** | — | J | Once merged, master pushes auto-deploy the Console to dev again (owner told before merge) |
| 🔲 [open] 128 | [Client test-harness repairs, own PR: #1416, #1417, #1388, #1392](128-client-test-harness-repairs-own-pr.poml) | FULL | sonnet/high | — | J | Test files/config only; rebase SemanticSearchControl after #1415 |
| 🔲 [open] 129 | [D-63 completion: chart/view/grid definitions off `sprk_finalduedate` + deploy VisualHost 1.4.39 (068) without tenant-wide publish](129-visualhost-finalduedate-definitions-and-pcf-deploy.poml) | FULL | sonnet/high | 068 | — | Inventory read-only; **row edits and the import: owner approval required** |
| 🔲 [open] 130 | [D-83: remove tenant-wide publish repo-wide (one scoped-publish procedure for scripts + skills)](130-scoped-publish-repo-wide.poml) | FULL | sonnet/high | — | J | Owner D-83. Skill text applied by the main session; live proof needs owner approval |
| 🔲 [open] 131 | [Notification playbooks: honour schedules/windows, real or removed dedup, Designer cannot overwrite runtime config](131-notification-scheduler-schedules-dedup-designer.poml) | FULL | **opus**/high | 120 | F | From task 120's findings (no parking) |
| ✅ [done] 069 | [D-60: remove foreign tables from `OntologyPlatformSolution`](069-d60-solution-hygiene.poml) | STANDARD | sonnet/high | — | E | **D-60** done 2026-10-07 by stream C2: 17 → 10 components; 11 foreign tables removed (reference only); `sprk_servicerequest` direction/disposition/responseduedate kept as column components; issue #1385 closed |
| ✅ [done] 099 | [CI: Tier 2 ADR Compliance timeout 3 → 5 min, own PR](099-ci-adr-compliance-timeout-own-pr.poml) | STANDARD | sonnet/medium | — | I2 | **D-29**. ci-workflows hot path (declared N): note it in the PR. Can start now  **Merged 2026-10-07 as PR #1346 (`dbc58d139`).** |

### Phase 10 — Ontology admin (D-22; after 031-034 and the worklist core)

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| 🔲 [open] 104 | [Capability probe + role-gated "Ontology admin" tab](104-ontology-admin-capability-probe-and-tab.poml) | FULL | **opus**/high | 008, 059 | — | Probe as the caller; never the security boundary · **[uac]** |
| 🔲 [open] 100 | [Slice (a): Rules read-only + On/Off + Retire](100-ontology-admin-rules-readonly-on-off-retire.poml) | FULL | sonnet/high | 104, 033, 034, 057, 026 | — | Writes as the caller; Retire closes at once |
| 🔲 [open] 101 | [Slice (b) BFF: catalog from the compiler, dry run, save, publish](101-ontology-admin-authoring-bff.poml) | FULL | **opus**/high | 104, 031, 033, 022, 036, 024 | — | `ValidateForSave`; catalog from verified joins (#46); dry run as the evaluator, filtered by caller (#39); publish stamps `sprk_inforceto` (**D-14**) · **[uac]** |
| 🔲 [open] 102 | [Slice (b) UI: the rule-authoring wizard](102-ontology-admin-rule-wizard-ui.poml) | FULL | sonnet/**xhigh** | 056, 101, 057, 058 | — | Test gates Publish; no free-text JSON |
| 🔲 [open] 103 | [Slice (c): Classification admin](103-ontology-admin-classification.poml) | FULL | sonnet/high | 104, 008, 057, 072, 074 | — | Rename blocked while a rule reads it; recall from 074's columns (**D-49**) |

### Phase 11 — UAT

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| 🔲 [open] 105 | [v4 UAT rounds with the owner on dev](105-v4-uat-rounds.poml) | STANDARD | sonnet/medium | 055, 064, 102 | — | Main session. Owner decision 1 (2026-10-05): v4 is the baseline, refined in UAT |

### Phase 9 — Wrap-up

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| 🔲 [open] 090 | [Project wrap-up](090-project-wrap-up.poml) | STANDARD | sonnet/medium | all | — | `/test-diet` is a **binding gate** (CLAUDE.md §7) |

---

## Parallel execution waves

Max concurrency is **6 agents per wave** (hard limit). After each wave the main session **must verify the
build** before dispatching the next — `dotnet build src/server/api/Sprk.Bff.Api/` for `.cs` changes,
`npm run build:prod` for PCF packages.

| Wave | Tasks | Prerequisite | Files touched | `goal-eligible` | Why |
|---|---|---|---|---|---|
| **A** | 010, 011, 012 | 001 | separate components | ✅ yes | Done |
| **B** | 020, 023 | 003 | separate code paths | ✅ yes | Done |
| **K** | 024, 057 | none — **start now** | compiler/schema vs shared UI kit | ❌ no (2 tasks) | Independent; 024 is on the critical path to 031 |
| **L** | 008, 036 | 007 | Dataverse roles vs BFF catalog | ❌ no (008 is security) | Different surfaces |
| **C** | 025, 033, 034 | 031 (025 also 024; 034 also 036) | compiler vs job closure vs suppression | ✅ yes | Lifecycle semantics in separate paths |
| **D** | 041, 042 | 040 | notes vs consumer | ❌ no | 041 is a security verification needing human judgement on the union result |
| **E** | 052, 054 | 051 (054 also 057) | MetricCard vs registration/aggregate | ❌ no | UI judgement; 052 stops on C-10 |
| **F** | 062, 063 | 061 (062 also 038, 059; 063 also 036) | Briefing components vs policy rows | ✅ yes | Configuration plus one policy row |
| **G** | 071, 072, 073 | 070 | three independent surfaces | ✅ yes | Three independent surfaces |
| **H** | 080, 081, 082, 083 | none | — | ❌ no | 080 carries six separate hazards needing per-item judgement |
| **I** | 084, 085, 086, 087, 088, 089 | none | own worktrees | ❌ no | Cleanup unrelated to ontology, each in **its own worktree and PR** off master |
| **I2** | 091 … 098, 099 | after wave I | own worktrees | ❌ no | Own-PR work; 099 is one line |
| **R** | 065, 067 | 098 (067 also 081) | Briefing collector vs SmartTodo scorer | ❌ no (2 tasks) | Separate packages |
| **J** | 123, 124, 125, 127, 128 | none | separate files; 123 on this branch, the rest own worktrees | ❌ no | No-parking repairs (D-81); independent surfaces |

**Can start now, outside any wave**: 007 (serial, schema), **079** (refusals before 008's Secure Record Owner edit), 110 (main session), 099, 026 once 024 lands.

**Not grouped because of file overlap** (each edits `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`
or the same launcher files): 036/038/043/044/046/100/101/103/104 (route census) · 112/113 (`wizardLaunchers.ts`,
`WorkspaceGrid.tsx`).

**Never parallel** — `parallel-safe: false`: **003**, **110** (`.claude/` paths, main session only) · **021** (the
risk item, serial by choice) · **007**, **030**, **031**, **039**, **032**, **043**, **051**, **056**, **058**, **059**, **061**,
**064**, **070**, **074**, **090**, **101**, **104**, **113** (each is a prerequisite others attach to, or shares files)
· **035**, **055**, **111**, **114**, **049**, **121**, **122**, **129** (deploy or privilege change on a shared environment) · **126** (same file as 048 and #1391) · **105** (main
session with the owner).

> **On `/goal` waves**: the Haiku evaluator is a **stopping-condition check, not a quality gate**. A met
> condition means the wave is done being *iterated*, not that the work is *good*. Step 9.5 gates and
> orchestrator authority are unchanged, and tasks are never auto-completed on goal achievement.

---

## High-risk items

| Task | Risk | Why it is ranked here |
|---|---|---|
| **031** | 🔴 **High** | Writes the first real Signals. Episode key, secure-child ownership (039) and per-item today must all be right **before** the first write, or the fix is a data migration |
| **039** | 🔴 **High** | Edits two uac-r2-owned files and the writer's ownership; deploy-order hazard (role edits before the BFF, or every Secure-matter create 403s); depends on uac-r2's review and batch 4 in dev |
| **043** | 🔴 **High** | Irreversible email send plus an append-only record: a partial failure after the record cannot be fixed on it. Its escalation trigger is the owner's call, not the implementer's |
| **038** | 🔴 **High** | The only barrier between a Signal's sentence and a reader who cannot open its matter (#17) |
| **074** | 🔴 **High** | A measurement that gates project exit. At 70% recall the differentiated claim misses 30% of real cases **while every other criterion passes green** |
| **024** | 🟡 Medium | Widens the compiler that was the project's risk item; a widened grammar that compiles wrongly fires on every row or none |
| **056** | 🟡 Medium | Shared wizard engine with eight consumers, one across the PCF React 16 boundary |
| **008 / 049** | 🟡 Medium | Privilege edits on the tables the append-only and visibility guarantees rest on |
| **051** | 🟡 Medium | New UI surface carrying the row contract. A second row component would be a design failure |
| **060** | 🟡 Medium | Data remediation in another domain. A blanket update would make 48 tasks appear in users' briefings at once |
| **064** | 🟡 Medium | Replaces a shipped user surface. Premature replacement removes working awareness |

---

## Standing obligations on every task

- **`/conflict-check` before every BFF PR** — 56 of 62 active projects declare a hot path
- **Publish size** measured against a **fresh master build**, short path, file counts both sides (NFR-01)
- **§0.3** — a capability must TEST what its message CLAIMS (NFR-02)
- **Every new route** is authorized as the caller and listed in the #1312 census; unresolved caller = single 403 (NFR-10)
- **Every role edit** is followed by the union re-verify (NFR-11); D-33's edits land **before** the BFF carrying the secure-child config
- **[uac] tasks**: re-read uac-r2's current code on `origin/master`, check its open PRs/active work, `/conflict-check`, reuse its mechanisms, stop if it changed under the plan (spec §8.3)
- **Merge master before any deploy** — it moved 66 commits in a day during this project
- **UI tasks**: record the reconciliation §B.6 pre-start check against HANDOFF @ `ae1cc9f` before starting
- Anything deferred gets a **GitHub Issue URL** in `../notes/defer-issues.md`; `push-to-github` Step 1.6
  refuses a push without one
