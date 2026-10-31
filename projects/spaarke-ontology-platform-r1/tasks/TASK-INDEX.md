# TASK-INDEX — Spaarke Ontology Platform R1

> **Generated**: 2026-10-03 by `/project-pipeline` · **Updated 2026-10-07** for the owner's decisions D-13..D-56
> (spec §9) · **154 tasks** (counted 2026-10-30: 129 ✅ · 7 🔄 awaiting the owner's dev re-check · 4 🔲 · 10 ⏸️ deferred · 4 🚫 cancelled)
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
| ✅ [done] 025 | [Threshold rule type: schema + compiler (spend threshold)](025-threshold-rule-type-schema-and-compiler.poml) | FULL | sonnet/high | 024, 031 | C | **D-16**. Proves the 031 job runs it with **no evaluator change**. Switch stays refused. **After 031** |
| ✅ [done] 026 | [Rule-body describer (plain-language condition, read-side)](026-rule-body-describer.poml) | FULL | sonnet/high | 024 | — | #11. Feeds the wizard's *How this was determined* (058) and the admin rule page (100) |

### Phase 3 — Evaluator, Signal lifecycle and read

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 030 | [Signal writer + dedupe + **ownership**](030-signal-writer-dedupe-and-ownership.poml) | FULL | sonnet/high | 021, 006 | — | Two independent reviews. Writer owns, owning BU from matter; create-first + reconcile; fail-closed MI; refusals logged (EventId 50300) + metered. Live as writer: matter PASS; **communication blocked by F26 (owner role decision)** |
| ✅ [done] 031 | [Nightly re-evaluating `IScheduledJob` (+ episodes, secured skip, per-item today)](031-nightly-reevaluating-scheduled-job.poml) | FULL | sonnet/**xhigh** | 030, 003, 007, 008, 009, 024, 037, 039, 098, 106 | — | ONE evaluator, not evaluator + sweep. **Amended 2026-10-07**: episode key before the first Signal (**D-13**); **no Restricted/Limited skip** — Secure-matter ownership via 039, one narrow skip (**D-33**); Off→On re-raise (**D-32**); per-item today via 098's helper (**D-25**); witness evidence lines (**D-23**); side-effect-free `Evaluate(version, scope)` · **[uac]** |
| ✅ [done] 032 | [Three event triggers (D-48)](032-event-triggers-classification-and-budgetrevision.poml) | FULL | sonnet/high | 031 | — | Budget-revision hook is correctness, not polish. **Amended**: shared core (D-13, D-33); never re-closes a decision-Acted Signal (X-6). **Association-confirmed trigger added (D-48)** · **[uac]** |
| ✅ [done] 033 | [Closure semantics](033-closure-semantics.poml) | FULL | sonnet/high | 031 | C | Aged-out maps to `ConditionCleared`. **Amended**: Superseded → new episode (D-13), reads `sprk_inforceto` (D-14); Off (next pass, reversible: **D-32**) vs Retire (immediate) |
| ✅ [done] 034 | [Suppression per (policy, matter), 30d](034-suppression-policy-matter-30-days.poml) | FULL | sonnet/high | 031, 036 | C | D-11 grain; auto-close never counts. **Amended**: lapse → new episode (D-13); *misresolved* never counts (R-10); count read for wizard/admin · **[uac]** |
| ✅ [done] 035 | [**Deploy** BFF](035-deploy-bff-evaluator.poml) | FULL | sonnet/high | 034, **141** | — | Merge master first; **schema pre-flight on the target**; hash-verify | · **Done** 2026-10-15: master `f292ad119` deployed to dev (4/4 SHA-256, healthz 200); job registered; alerts live (#1899); negative alert test fired 13:44:53Z; writer setting added, positive write 13:48:04Z (`notes/deploy-log.md`)
| ✅ [done] 079 | [uac-r2 coordination: two recorded live refusals + review of the secure-child entries](079-issue-secure-record-mirror-for-signals.poml) | STANDARD | sonnet/medium | — | — | **D-33** (was the D-15 "later mirror" issue). Refusals must be recorded **before** 008's Secure Record Owner edit; the review gates 039. **Start now** · **[uac]** |
| ✅ [done] 039 | [**Secure children: register Signal + Decision Record; writer owner from the resolver**](039-secure-signals-and-decision-records.poml) | FULL | **opus**/high | 007, 008, 079 | — | 🔴 **D-33** (replaces D-15's skip): two entries each in `SecureChildLineage.cs` and `config/secure-record-owner-role.json` (uac-r2 review via 079); task-146-shaped writer change; 2-minute secure-sync load measured; live gate on a provisioned Secure matter. ~3 dev-days. **Before 031** · **[uac]** |
| ✅ [done] 037 | [Signal writer accepts Do-lane subjects (event, To Do, WA)](037-signal-writer-do-lane-subjects.poml) | FULL | sonnet/high | 007, 008, 024, 039 | — | **D-16** matter via `sprk_regardingmatter`; `sprk_duedate` (D-27) refreshed while open. Decides + flags project-filed tasks (D-16); **D-31** matterless To Do owned by the To Do owner's BU; **D-33** resolver parents + narrow skip. **Before 031** · **[uac]** |
| ✅ [done] 038 | [BFF Signal read route: matter-access filter, Do-lane reader scope, order](038-signal-read-route-and-do-lane-scoping.poml) | FULL | **opus**/high | 050, 031, 037 | — | **D-15** (stands under D-33). #1312 census. Event "mine" = 097 decision B; WA in **both** assigner's and assignee's Do lane (**D-44**); rank **D-42**; no-core rows owner-only (D-35) · **[uac]** | · **Done** 2026-10-14: [#1891](https://github.com/spaarke-dev/spaarke/pull/1891) merged `b0fe65988` (uac-r2 CLEAN; 2 adversarial passes + scoped re-check). `GET /api/v1/signals/worklist` with read-time evidence witness rule (145 F1 option B, reuses `KeepsDetail`/`WitnessOf`). uac-r2 K1 (walker query count) fixed in [#1892](https://github.com/spaarke-dev/spaarke/pull/1892); K2 → #1894 (uac); read-time K2 folded into DI-80 #1873. K: `EvidenceLine.tsx` withheld rendering → 059; empty-worklist log level → 149. DI-83 #1882 role grant applied on dev; DI-82 #1881, DI-86 #1883 owner

### Phase 4 — The Decision Record

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 040 | [Decision Record writer + record class](040-decision-record-writer.poml) | FULL | sonnet/high | 001, 030, 007, 036, 008, 039 | — | BR-1 **as reversed**. **Amended**: ONE record per review with `sprk_steps` / `sprk_followons` / `sprk_gatetier`, class from the 036 catalog, called last by 043 (**D-17**); **secure child, still append-only** (owner from the resolver, Assign only) (**D-33**) · **[uac]** |
| ✅ [done] 041 | [Append-only + relationship direction](041-append-only-and-relationship-direction.poml) | FULL | sonnet/high | 040 | **D** | Test as a real non-admin; 1 → N, FK on the Signal · **Done** 2026-10-14: [#1865](https://github.com/spaarke-dev/spaarke/pull/1865) merged `f476f7dbb`; live on spaarkedev1 as a non-admin (MSCRMCallerID): update/delete/deactivate 3/3 refused on owned and shared rows; 1:N, FK on the Signal; zz-041 = 0 |
| ✅ [done] 042 | [Wire writer into the consumer](042-wire-writer-into-rulegated-consumer.poml) | FULL | sonnet/high | 040 | D | One line before the branch. **Do not touch the gate**. **Amended**: one-step review shape (D-17); O-20 dissolved by D-33 · **[uac]** | · **Done** 2026-10-14: [#1878](https://github.com/spaarke-dev/spaarke/pull/1878) merged `6a00d1c47` (uac-r2 review; main-session decision A+C). Gate allow and deny both write a Judgement DR via `WriteGateDecisionAsync` (one `CreateAsync` with `WriteAsync`); unfiled communications refused at Information on `ontology.writer.skips`. Filtered suite 838/0, ArchTests 56/0. Deferred DI-84 (#1876), DI-85 (#1877); idempotency + queueing → 149 (D-139)
| ✅ [done] 042b | [Gate DR visible to the communication's sharees; re-owned on filing](042b-gate-dr-participant-visibility.poml) | FULL | opus/high | 042 | D | D-169 (owner 2026-10-18, via uac-r2), closes DI-84 [#1876](https://github.com/spaarke-dev/spaarke/issues/1876). Share-at-write + mirror on communication grant + re-own/read-back/revoke on ordinary filing. **[uac]** review of the POML and the DirectThreadAccessService diff · **Done** 2026-10-18: [#2096](https://github.com/spaarke-dev/spaarke/pull/2096) merged `f66aa811a`; limits #2091 (UAC), DI-117, DI-118 |

### Phase 4b — Commit and actions (D-17, D-18, D-19, D-21)

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 036 | [Closed BFF action catalog + decision-plan read endpoint](036-action-catalog-and-decision-plan-read.poml) | FULL | sonnet/high | 007 | L | Approve variance record-only (**D-19**); dismissal reasons per lane; reassign / extend **Routine (D-45)**; adds record-the-response (D-54) and assign-work as a plan action (D-51) · **[uac]** |
| ✅ [done] 043 | [**The decision commit route** (record last, idempotent)](043-decision-commit-route.poml) | FULL | **opus**/high | 040, 036, 044, 046, 070 | — | 🔴 **D-17**: validate → writes → email last → one record → close. Failure = no record + which writes landed. Confirm = confirmation; gate tier from a pure `PublicContracts` function (**D-52**) · **[uac]** · **Done** 2026-10-13: [#1665](https://github.com/spaarke-dev/spaarke/pull/1665) merged `aeb9e0973` (D-129/D-130 resolver ownership, D-35 read, D-134 Assigned To; uac-r2 APPROVE; two-wall refusal → 142/#1778) |
| ✅ [done] 044 | [Executors: Revise budget, Approve variance, Do actions, Next steps](044-decision-action-executors.poml) | FULL | sonnet/high | 036, 008, 047 | — | **D-18** writer creates the revision after a caller check; budget amount written **as the signed-in user (D-55)**; reschedule writes `sprk_duedate` (D-27); record-the-response writes the **D-54** columns (047) · **[uac]** |
| ✅ [done] 046 | [Server-side work-assignment create](046-server-side-work-assignment-create.poml) | FULL | **opus**/high | 008, 048 | — | **D-21**; D-113: `OwnedChildWrite.CreateAsync` via `child-records` route, no new route. Preserve the wizard's BU cascade; area owner reviews · **[uac]** |
| ✅ [done] 047 | [Schema: response columns on `sprk_workassignment` (D-54)](047-schema-work-assignment-response-columns.poml) | FULL | sonnet/high | — | — | **D-54**: responded-on + outcome (exact set agreed with the WA owner); 007 recipe; WA is a secure root · **[uac]**. Feeds 044, 061. **Start now** |
| ✅ [done] 048 | [D-66: move shared Dataverse write core (`OwnedChildWrite` + helpers) out of `Services/Ai` → `Services/Dataverse`, own PR (uac-r2 review)](048-move-dataverse-write-core-out-of-ai-namespace.poml) | FULL | sonnet/high | — | C | **D-66**; uac-r2 agreed (D-113): after #1391, before 046; sequence after #1391 (same file) **[uac]** |
| ✅ [done] 049 | [Remove Console User Write on `sprk_signal`](049-revoke-console-user-signal-write.poml) | STANDARD | sonnet/medium | 043, 055 | — | **D-17**, after the deployed Console closes Signals only via the BFF. Union re-verify · **[uac]** | · 2026-10-17: APPLIED live incl. admin Signal Write (D-159, security-roles §17); repo #2050 merged `32a923ca4`

### Phase 4c — Standard child-record contract (D-131..D-133; added 2026-10-12, critical path, before 035)

Contract: `git show origin/work/unified-access-control-r2:projects/unified-access-control-r2/notes/ontology-standard-child-contract-2026-10-12.md`. Sequence: 140 → uac-r2 lineage + census PR → 141 → 142/143 → 144 → 145 → 146. All **[uac]** (uac-r2 reviews every PR).

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 140 | [Schema: 14 DR regarding columns; Signal pair type text → Lookup](140-child-contract-schema-delta.poml) | FULL | sonnet/high | — | CC1 | **D-132** owner-approved. Done on spaarkedev1 2026-10-12 (scoped publish; no dependency; upgrade runbook in the deployment guide §10.5; DR metadata cache key lives up to 6 h, so 146 waits) · see [notes/140-schema-delta.md](../notes/140-schema-delta.md) · **[uac]** |
| ✅ [done] 141 | [Generalise `TodoRegardingBuilder`; SignalWriter on the shared builder](141-shared-regarding-builder-signalwriter.poml) | FULL | **opus**/high | 140 (+ #1665/#1746 merged) | CC2 | **PRIORITY** (closes the pair-type deploy hazard; adds a schema-agreement guard). **D-133**. Builder commit reviewed alone by uac-r2; To Do/Event tests unchanged; dedupe key unchanged · **[uac]** |
| ✅ [done] 142 | [DecisionRecordWriter: subject filing, all-parent owner, two-wall refusal](142-decision-record-writer-contract.poml) | FULL | **opus**/high | 141, 043 | CC3 | Contract C1–C3, C9; acceptance 1–3, 6, 7 · **Done** 2026-10-13: [#1824](https://github.com/spaarke-dev/spaarke/pull/1824) merged `9c188eda3` (uac-r2 CLEAN at `226003f36`; Code Quality red repo-wide → #1826) · **[uac]** |
| ✅ [done] 143 | [D-130 owner on SignalWriter/Suppression/Commit; replace ownerid read rule](143-d130-owner-signal-writers.poml) | FULL | **opus**/high | 141, #1746 | CC3 | Audit F3 + F2 code side (F2 Read grant = owner decision). Absorbs the "F3 SignalWriter no-core ownership" lane · **Done** 2026-10-14: [#1834](https://github.com/spaarke-dev/spaarke/pull/1834) merged `96fec5d7b` (uac-r2 CLEAN; Q7 live passed; K1/K2 follow-up lane) · **[uac]** |
| ✅ [done] 144 | [Register Signal + DR with the core-ancestor restamper; re-file re-stamps/re-owns](144-restamper-registration-refile.poml) | FULL | **opus**/high | 142, 143, uac-r2 C4/C7 PR, #1747 | CC4 | uac-r2 conditions: separate commit; not an intermediate; root classification; report-only reconciliation first · **[uac]** · **Done** 2026-10-14: [#1862](https://github.com/spaarke-dev/spaarke/pull/1862) merged `8f7ff5ed5` (uac-r2 approve + CLEAN re-check at `bb649bc59`; word-add-in CLEAN). Signal/DR stamps report-only in the job; accepted K: AfterWriteAsync and the 409 handler still write them |
| ✅ [done] 145 | [`sprk_matter` readers (context vs filing), backfill, evidence leak](145-matter-readers-backfill-evidence-leak.poml) | FULL | **opus**/high | 141, 142, 144 | CC5 | D-34 split; uac-r2 #1747 finding (e) (D-123 leak); backfill write needs owner approval · **[uac]** · **Done** 2026-10-14: [#1874](https://github.com/spaarke-dev/spaarke/pull/1874) merged `459762cac` (uac-r2 CLEAN after 3 rounds; 2 adversarial passes). Evidence redaction across secure-family walls (create + monotonic sweep incl. early-stop policies); commit route reads matter from the core pair; typed cores kept (144 K1). Backfill dry run 0/0; `-Apply` = release of the 144 hold (D-138). Deferred DI-79..81 |
| ✅ [done] 146 | [Live gate: contract §5, 11 items](146-child-contract-live-gate.poml) | FULL | sonnet/high | 140–145 | CC6 | After the BFF with 141–145 is on dev and the DR metadata cache key has expired · **[uac]** | · **Done** 2026-10-15: [#1933](https://github.com/spaarke-dev/spaarke/pull/1933) merged `495faf82c` (review, fix round, re-check). Items 2, 3, 5, 6, 7, 10, 11 and K-c pass; items 1 and 9 partial; item 8 not run on the product route; bounded exposure windows observed (K-2, item 8). Follow-ups DI-93 to DI-98 (#1976 to #1981) and #1872 |
| ✅ [done] 147 | [Budget revision records its requester (`sprk_createdbyperson`); drop 182's exemption](147-budgetrevision-creator-person.poml) | FULL | sonnet/high | #1780 merged | CC2 | **D-135** (Q27; #1777). Column created on dev by the owner · **[uac]** |
| ✅ [done] 148 | [Follow the assignee: `TodoAssigneeOwnerReconciliationJob`](148-todo-assignee-owner-reconciler.poml) | FULL | **opus**/high | 142, 143, #1665, UAC `ReownOrdinaryRowAsync` | CC5 | **D-135** item 2. Shape approved by uac-r2 (F1 live filing, F2 nightly recompute, F3 re-owns via UAC's entry point). Report-only by default · **[uac]** · **Done** 2026-10-14: [#1858](https://github.com/spaarke-dev/spaarke/pull/1858) merged `d4adb1b57` (uac-r2 CLEAN at `159f069e4`); writes OFF until a report-only dev run is reviewed (D-138 delegates enabling) |
| ✅ [done] 149 | [Gate Decision Records: one per decision, written by an ADR-004 queue job](149-gate-decision-record-queue-idempotent.poml) | FULL | sonnet/xhigh | 042 (#1878) | CC6 | **D-139** (owner 2026-10-14, from uac-r2 review of 042 K2/K4). Idempotent on (communication, outcome); the gate never waits on the write. **Also D-144** (unfiled → BU default team) and uac-r2 K3 (log level) · **[uac]** · merged #1907 `5a30d11dc` |
| ✅ [done] 150 | [Do items private to their assignee on per-Signal routes](150-do-items-private-to-assignee.poml) | FULL | **opus**/high | 038 (#1891) | — | **D-141** (owner 2026-10-15; DI-86 #1883). One "mine" rule shared with the worklist · **[uac]** · merged #1905 `223cc7d47` |
| ✅ [done] 151 | [Hide evidence detail when the caller cannot read the witness](151-evidence-hidden-unless-witness-readable.poml) | FULL | **opus**/high | 038, #1892 | — | **D-142** (owner 2026-10-15; DI-80 #1873, uac-r2 K2 on #1891). Batched OBO read per witness table · **[uac]** · merged #1903 `fe05faa4f` |
| ✅ [done] 152 | [Do-lane suppression counts per person](152-do-lane-suppression-per-person.poml) | FULL | **opus**/high | 150, 151 merged | — | **D-145a** (owner 2026-10-15; #1921). A colleague's dismissals never mute your Do items · **[uac]** | · **Done** 2026-10-15: [#1946](https://github.com/spaarke-dev/spaarke/pull/1946) merged `7fc51a796` (adversarial review + uac-r2 CLEAN at `40468a5aa`); #1921 closed |
| ✅ [done] 153 | [Satisfied Do item closure writes a Routine Decision Record](153-do-item-satisfied-closure-records-decision.poml) | FULL | **opus**/high | 149 merged | — | **D-145b** (owner 2026-10-15). ConditionCleared on a Do Signal records who did the work, best effort · **[uac]** | · **Done** 2026-10-15: [#1953](https://github.com/spaarke-dev/spaarke/pull/1953) merged `53f19f0a2` after two Opus passes, four fix rounds and three uac-r2 re-checks (final CLEAN at `0d522d7bc`). Also closed the forged-keyed-DR hole in 149 `FindGateRecordAsync` and 043 `FindRecordAsync`: createdby = the writer |
| ✅ [done] 154 | [Worklist route: subject on Do rows, rule sentence, Not filed last; inquiry lookup for a To Do](154-worklist-route-subject-sentence-notfiled-inquiry-read.poml) | FULL | **opus**/high | 038, 071 | — | From 059's escalation (#1958, #1959 items 1+3). Caller-scoped only (D-15). **Gates 055** · **[uac]** | · **Done** 2026-10-15: [#1982](https://github.com/spaarke-dev/spaarke/pull/1982) merged `3c61380e3` (two Opus passes + fix rounds; uac-r2 CLEAN). K2 route named on #1984; sentence added to #1913 |
| ✅ [done] 155 | [Worklist: D-111 disposition picker; sentence pass-through; Not filed fixture](155-worklist-disposition-picker-and-route-fields.poml) | FULL | sonnet/high | 059, 154 | — | Success criterion 10 in the UI. Gates 105, not 055 | · **Done** 2026-10-15: [#1990](https://github.com/spaarke-dev/spaarke/pull/1990) merged `493bb8a49` (Opus review, fix round, Sonnet re-check, F3 test fixed with seeding proof). Follow-ups DI-101 #1991 (Signal stays until nightly), DI-102 #1992 (paging/Start) |
| ✅ [done] 156 | [Make the Worklist reachable: section shim + "Worklist" layout row](156-worklist-reachable-workspace-layout.poml) | FULL | sonnet/high | 059, 155 | — | Found at the 055 deploy: no Console path opened the worklist. Copies reconciliation shim + seed. **Gates the 055 browser check** | · **Done** 2026-10-16: [#2004](https://github.com/spaarke-dev/spaarke/pull/2004) merged `2160308e6` (Opus review: lookup/aggregate row/padding fixed in one round, re-check clean, seeding proof). Seed runs after the Console redeploy. Follow-up DI-103 #2006 |
| ✅ 157 | [Duplicate Signal: hide the earlier invoice on read (D-176, #2118)](157-duplicate-signal-hide-on-read.poml) | FULL | **opus**/high | 151 | — | D-176 option (b): ontology read check in SignalReadService; detect from stored evidence (billing code only on work/ebilling-r1); invoiceId or tracking-number fallback; fail closed; ebilling reviews |
| 🔄 158 | [Decision wizard: side-pane lookups for every Person/Lookup field; subject record link (D-182)](158-decision-wizard-side-pane-lookups-and-subject-link.poml) | FULL | sonnet/high | 105 | K | UAT round 1 findings 1, 4, 6. Host-injected openLookup/openRecord; inline field stays as fallback · 2026-10-23: #2295 merged to docs `539ab22ce9` (Opus review F1 clear button, F2 log, K2 supportedEntities; re-check clean; lint ratchet fixed). Open: dev check (modal stacking, light/dark) after docs→master sync + Console deploy |
| 🔄 159 | [Decide lane "Assign Task" creates sprk_event type Task; Next steps Add To Do + Assign Work (D-178)](159-decide-assign-task-creates-event-task.poml) | FULL | sonnet/high | 105 | K | UAT round 1 finding 2. New catalog code `assign-task`; POL-MATTER-NOBUDGET v2 (apply to dev after owner OK) · 2026-10-23: #2289 merged to docs `9b485535ce` (Opus review F1 2000-char refusal; script stamping + lints; CI Pester job; re-check clean; merged with 161). Open: BFF deploy, then POL-MATTER-NOBUDGET v2 apply after owner OK on -WhatIf (dry run 2026-10-23 clean), dev check |
| 🔄 160 | [Budget inquiry: EmailComposer compose-only, To from outside firm, link by recipient, footer names the record (D-179, D-181)](160-budget-inquiry-email-composer-and-record-link.poml) | FULL | **opus**/high | 105, 158 | K | UAT round 1 findings 5, 7. R3 reviewed the GrantAccessNotifier overload (R3-a/b fixed). **Merged to docs** `f2fa840474` (PR #2305; Opus review → fix round 1 incl. F1 stored XSS → re-check no F → K-2/3/4 fixed → lint ratchet fixed; uac-r2 F1 footer label internal-only fixed in #2314 `b01fb39039`, uac-r2 ACCEPT). **On master** `58ffc37da3` (PR #2313). Open: dev BFF (UAC OK covers the first master SHA with #2313+#2314), dev check |
| 🔄 161 | [Revise budget on a matter with no budget becomes "Create budget" (D-180)](161-create-budget-when-matter-has-none.poml) | FULL | sonnet/high | 105 | K | UAT round 1 finding 6. Plan substitution like WithoutMarkComplete, in both routes · 2026-10-23: #2290 merged to docs `811167613f` (Opus review F1-a app-only no-budget read, F1-b tri-state + 503, budget lease, resume fix; re-check clean). Open: dev check after docs→master sync + BFF deploy |
| 🔄 162 | [Wizards bind an uploaded document through the exact parent column (114 UAT)](162-wizard-document-lookup-exact-column.poml) | FULL | sonnet/high | 114 | K | `sprk_X` vs `sprk_relatedX`; blocks closing 114. Field-mapping half is #2281 (DI-122, field-mapping project); owner verified #2281 on dev 1fa0cf5cb8 (2026-10-23, via R3) | · 2026-10-23: #2286 merged to docs `84636b650a` (Opus review: 2 F4 test gaps fixed, re-check clean). Open: dev check after the docs→master sync; owner question sprk_X vs sprk_related* (DocumentLinkFieldMap marks sprk_X legacy). #2291 (DI-123) filed |
| 🔄 163 | [Worklist reads policy columns as the BFF; drop direct policy Read from Basic and Console User (D-185, #2285)](163-worklist-policy-columns-server-read.poml) | FULL | sonnet/high | — | K | security-tagged (2 review passes). Live role change by the main session after the BFF deploy (owner authority D-185). **Merged to docs** `5952a9c801` (PR #2298; pass 2 F2 fixed: a caller-resolver fault is 503; scoped re-check clean). Open: dev BFF deploy, config apply, role change |
| ✅ 164 | [Deploy-Release.ps1 applies and verifies the worklist grid configuration between the BFF and the SpaarkeMaster import (D-185 order)](164-deploy-release-applies-worklist-config.poml) | FULL | sonnet/high | 163 | K | Owner decision 2026-10-23 (automate, separate task). Config failure blocks Phase 3 per environment. Policies (Apply-DoLanePolicies) stay manual: DI-124 #2300. **Merged to docs** `9f53c53198` (PR #2299; 2 fix rounds, 71 Pester tests, $envFailed and BFF-gate mutants caught; guide 7.8 reconciled with 163). On master `0317579272` (PR #2303) |
| ✅ 165 | [Decision wizard keeps ONE dialog open from launch to close](165-decision-wizard-one-dialog.poml) | FULL | sonnet/high | 158 | L | UAT round 1 re-check (2026-10-28), check 2: the loading dialog closes and a second dialog opens (`DecisionWizard.tsx:441` vs :410); also on item move and record. Additive WizardShell reset |
| 🔄 166 | [POL-MATTER-NOBUDGET v3: no-budget items offer Create budget (D-192)](166-no-budget-policy-v3-create-budget.poml) | STANDARD | sonnet/high | 159, 161 | L | UAT round 1 re-check (2026-10-28), check 4. Plan `revise-budget` (→ Create budget by 161) + `assign-task`; dev apply approved after a clean -WhatIf |
| ✅ 167 | [Create budget: BUD- autonumber, business-unit currency, budget fields in the step](167-create-budget-number-currency-fields.poml) | FULL | sonnet/high | 161, 166 | M | UAT round 2 items 5-6, **D-193**. The autonumber schema change is approved; the main session applies it live · **Done** 2026-10-28: [#2403](https://github.com/spaarke-dev/spaarke/pull/2403) merged `4f52d3641f`; BUD- autonumber live on dev; K1 (secure-matter currency) awaits the owner |
| ✅ 167c | [Secure matter budget currency defaults to the customer business unit](167c-secure-matter-budget-currency-customer-bu.poml) | FULL | sonnet/high | 167, #2414 | M | **D-198** (owner, 2026-10-28; rule confirmed by uac-r2). Follow-up #2414 (owning business unit read as the caller) merged `f634761a63` · **Done** 2026-10-28: [#2422](https://github.com/spaarke-dev/spaarke/pull/2422) merged `e2456c89dc` |
| ✅ 168 | [Assign Task: "Send email notification" (ticked) emails the assignee and raises a bell notification](168-assign-task-email-and-bell-notification.poml) | FULL | **opus**/high | 159, 167 | M | UAT round 2 item 2, **D-194**. Delegated send first, system mailbox as fallback; never fails the decision. Starts after 167 merges (same files) **Done** 2026-10-29: #2418 merged `7cb3c263c0` after 4 fix rounds (D-199 gate). Live dev proof waits for the next BFF deploy; the owner From question is open. |
| ✅ 169 | [Worklist lanes: subtitles; card list collapsed by default, remembered per device](169-worklist-lane-subtitles-collapsible-list.poml) | STANDARD | sonnet/high | — | M | UAT round 2 item 4, **D-195**. Client only · **Done** 2026-10-28: [#2402](https://github.com/spaarke-dev/spaarke/pull/2402) merged `67313ad46d` |
| ✅ 170 | [Faster decision commit: per-request caller identity, cached entity metadata](170-decision-commit-latency-caches.poml) | FULL | **opus**/high | — | M | UAT round 2 item 3, **D-196** (24 s commit: 35 WhoAmI, about 30 metadata lookups). uac-r2 reviews if `CallerRecordAccessProbe` changes · **[auth]** · **Done** 2026-10-28: [#2404](https://github.com/spaarke-dev/spaarke/pull/2404) merged `0f9fd108fa`; ADR-009 path C; post-deploy WhoAmI count check pending |
| ✅ 171 | [Seed script owns created policy/version rows to the customer BU restricted-content team](171-seeded-policies-owned-by-customer-bu-team.poml) | STANDARD | sonnet/high | uac-r2 R4b-2c | N | **D-197** (UAC round 137). Dev unchanged; customer environments only. Release wiring stays DI-124 (#2300). Unblocked 2026-10-29: R4b-2c is on master in 194b `2cd0f62e2c`. **Done** 2026-10-29: #2433 merged `3452d3017e` after fix round 1 (empty parameter refused) |
| ✅ 172 | [Pin D-197 "author wins, root included": a rule publish asks the ownership question without a child table](172-authoring-owner-unit-unsupported-409.poml) | STANDARD | sonnet/high | 194b (`2cd0f62e2c`) | N | Rewritten 2026-10-29: the 409 premise was wrong (uac-r2 corrected, main session verified). A test plus a comment; no behaviour change · **[uac]** · **Done** 2026-10-29: [#2434](https://github.com/spaarke-dev/spaarke/pull/2434) merged `fab8c91531` (uac-r2 OK) |
| ✅ 173 | [An external Assign Task assignee is emailed from the system mailbox (D-199 amendment)](173-external-assignee-email-from-system-mailbox.poml) | FULL | sonnet/high | 168 | N | Owner 2026-10-29: never the assigner's mailbox for an external party; internal unchanged **Done** 2026-10-29: #2462 merged `f4a0fc6862`. |

### Phase 5 — The worklist surface

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 050 | [Worklist grid configuration row (membership + order)](050-worklist-gridconfiguration-row.poml) | STANDARD | sonnet/medium | 034, 007 | — | Membership by rule evaluation, never a user filter. **Amended**: per-lane + 3 secondary sets; executed server-side by 038 (D-15); no actions · **Done** 2026-10-14: [#1844](https://github.com/spaarke-dev/spaarke/pull/1844) merged `ba1c1dfdf`; row applied on spaarkedev1 (D-138), read-back in sync, 5 queries run |
| ✅ [done] 051 | [**The one row component: MatterCard + IssueLine**](051-worklist-row-component.poml) | FULL | **sonnet/xhigh** | 057, 012, 007 | — | 🔴 A second row component is a design failure. **Amended**: v4 row (no controls in a line; whole line opens the wizard); UI-composed headline (**D-23**). Builds on fixtures |
| ✅ [done] 052 | [Extend MetricCard (count filters); C-9 RowActionMenu cleanup](052-extend-metriccard-rowmenu-outcomecard.poml) | FULL | sonnet/high | 051 | E | **D-24** selected / note / progress. DocumentRowMenu/OutcomeCard reuse **dropped (D-46)**; C-9 continues on its own (shared UI: own PR) |
| 🚫 [cancelled] 053 | [~~Gate host + acting~~](053-gate-host-and-acting.poml) | FULL | sonnet/high | — | — | **Superseded 2026-10-07** by **058** (wizard) + **043** (commit route), per D-17 / D-26. POML status `deferred`; do not execute |
| ✅ [done] 054 | [Reconciliation tab + aggregate item](054-reconciliation-tab-and-aggregate-item.poml) | STANDARD | sonnet/medium | 051, 057 | E | ONE registration (already exists, #18 — verify). **Amended**: AggregateCard at the top of the Do lane; count from the Email Review feed (#36) |
| ✅ [done] 055 | [**Deploy** Console + shared components](055-deploy-console-and-shared-components.poml) | FULL | sonnet/high | 059, **154**, 043, 035 | — | **Real Dataverse** verification. **Amended**: one full wizard review (2 actions + 1 Next step) live; BFF with 038/043 deployed first | · **Done** 2026-10-17: owner browser check (D-154); DR-1066, budget revision, inquiry and Signal verified in Dataverse; Next step To Do carried to 105
| ✅ [done] 057 | [Console UI kit: EvidenceLine, StatusBar, RecordRow, AggregateCard](057-console-ui-kit.poml) | FULL | sonnet/high | 012 | K | **D-24** reuse first (Accordion, MessageBar, Link, ConfirmModal, StatusBadge + success). Can start now |
| ✅ [done] 058 | [**The decision wizard** (WizardShell, in-app)](058-decision-wizard-consumer.poml) | FULL | sonnet/**xhigh** | 056, 057, 036, 026, 034, 043 | — | **D-26**, **D-17**: writes nothing itself. **Templated (non-AI) drafts (D-53)**. Build on fixtures before 043 is live · **Done** 2026-10-14: [#1856](https://github.com/spaarke-dev/spaarke/pull/1856) merged `bb49334f6` (lint ratchet fixed by widening types); deferred DI-75 #1848, DI-76 #1849 |
| ✅ [done] 059 | [Worklist widget assembly](059-worklist-widget-assembly.poml) | FULL | sonnet/high | 038, 051, 052, 054, 058 | — | Two lanes, honest filters, narrative slot, aggregate row, disclosures; reads only via 038 (**D-15**); matterless To Do grouping = **O-21** | · **Done** 2026-10-15: [#1964](https://github.com/spaarke-dev/spaarke/pull/1964) merged `930c02c7f` (Opus review, fix round, re-check CLEAN, lint ratchet fix). D-111 picker split to 154/155; #1959 item 2 to 045; narrative #1960 to 062 |
| ✅ [done] 045 | [Decision Record tab (D-47)](045-decision-record-tab.poml) | STANDARD | sonnet/medium | 058, 043 | — | In R1 (**D-47**) · **[uac]** · **+#1959 item 2**: the worklist route returns the Decision Record (steps, note, number, decided on) for Closed today / Closed earlier so the wizard replays it | · **Done** 2026-10-16: [#2014](https://github.com/spaarke-dev/spaarke/pull/2014) merged `1931b405c` (Opus review, fix round: writer-authored guard, Acknowledged-with-DR, 401/403; re-checked). Layout seed after Console deploy. DI-104 #2015 |

### Phase 5b — Modal system (D-26)

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 110 | [ADR-050 path B amendment (WizardShell preset, in-app launch rule)](110-adr-050-amendment-wizard-preset-and-launch-rule.poml) | FULL | **opus**/high | — | — | ⚠️ `.claude/` → **main session only**. Amendment text is in the POML `<notes>`. Can start now |
| ✅ [done] 056 | [WizardShell: characterization tests, re-base on SprkModal, v4 props, delete WizardModal](056-wizardshell-rebase-on-sprkmodal.poml) | FULL | **opus**/high | 110 | — | Modal note P1-P3. Embedded markup must not move |
| 🔲 [open] 111 | [**Deploy** + regression of non-embedded WizardShell consumers](111-wizard-consumer-regression-and-deploy.poml) | FULL | sonnet/high | 056 | — | Modal note P4; SemanticSearchControl PCF last (`build:prod`) | · 2026-10-17: owner rows 1, 2, 3, 5 pass; row 4 waits for the external SPA dev deploy (122 / R3) (D-155)
| ✅ [done] 112 | [Migrate Create wizards off `navigateTo` (in-app host)](112-migrate-create-wizards-in-app.poml) | FULL | **opus**/high | 056 | — | [PR #1422](https://github.com/spaarke-dev/spaarke/pull/1422) **open, awaiting owner merge** (head `0b2ec81bc`). `InAppWizardHost` mounted in the Console; bundle +3,584 B. Filed #1420, #1421 |
| ✅ [done] 113 | [Migrate remaining wizards off `navigateTo`](113-migrate-remaining-wizards-in-app.poml) | FULL | sonnet/high | 112 | — | P5 part 2: Summarize Files, Upload Documents, Find Similar, Workspace layout. **+ #1421** (create-project widget) **+ #1420** (Work Assignment completion) (no-parking sweep). [PR #1480](https://github.com/spaarke-dev/spaarke/pull/1480) open, not merged |
| 🔲 [open] 114 | [**Deploy** the in-app wizard migration](114-deploy-in-app-wizard-migration.poml) | FULL | sonnet/high | 111, 113 | — | Verify all nine in-app on dev, full page and in an Xrm dialog |

### Phase 6 — Do lane and Briefing fold-in

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 060 | [ISS-003 stranded Draft events](060-iss003-stranded-draft-events.poml) | FULL | sonnet/high | — | — | D-74 (2026-10-08): 49 Draft classified; 0 platform-created; owner opened 1 (8a6b371f), 48 left Draft; issue #1050 closed; see `notes/060-draft-classification.md` |
| ✅ [done] 061 | [Do lane: four policies as subject-only rules (incl. overdue To Do)](061-do-lane-temporal-policies.poml) | FULL | sonnet/high | 060, 034, 024, 036, 037, 047 | — | **Amended (D-16)**: evaluated by the one evaluator; the collector is the reference, **not modified**. Overdue starts at **1 day (D-43)**; WA rule reads the **D-54** response columns; **fourth policy: overdue To Do for ALL To Dos (D-31)** · **Done** 2026-10-14: [#1869](https://github.com/spaarke-dev/spaarke/pull/1869) merged `661939617`; grammar null test (main-session option A); 4 policies on spaarkedev1, `sprk_enabled=No`; evaluator = collector reference 22/0/19/43; DI-77 catalog row fixed on dev |
| ✅ [done] 062 | [Know → narrative; retire Critical Today; remove LLM Top action](062-know-narrative-and-retire-critical-today.poml) | FULL | sonnet/high | 061, 038, 059 | F | Rank is deterministic or it is not explainable. `sprk_highpriority` is the 2nd rank key (**D-42**) · **Done** 2026-10-16: [#2018](https://github.com/spaarke-dev/spaarke/pull/2018) merged `33a98774` (Opus review, fix round, re-check). Prompt sync after master + BFF. DI-106 #2030 |
| ✅ [done] 063 | [First Know-promotion rule](063-first-know-promotion-rule.poml) | STANDARD | sonnet/medium | 061, 036, 046 | F | Offers **Assign Work (D-51)**; needs a (matter, budget) verified join — escalates | · 2026-10-17: owner — add the join inside 063 (D-156); lane running · **Done** 2026-10-17: #2053 merged `73960ac90`; POL-MATTER-NOBUDGET on dev, Off (enable = owner)
| ✅ [done] 064 | [Replace the Briefing tab](064-replace-briefing-tab-with-worklist.poml) | FULL | sonnet/high | 062, 063, 055, 059, 065 | — | Only once BOTH lanes exist. **Amended**: HANDOFF §1.4 as the cutover check | · **Done** 2026-10-17: switch run 14:38Z (D-164); non-admin checks and UI tests to 105
| ✅ [done] 065 | [Daily Briefing reads `sprk_duedate`](065-briefing-reads-sprk-duedate.poml) | FULL | sonnet/high | 098 | R | **D-27**; other `sprk_finalduedate` readers listed, not changed |
| ✅ [done] 066 | [Deprecate `sprk_eventstatus`: inventory, move readers to `statuscode`](066-deprecate-sprk-eventstatus-inventory.poml) | FULL | sonnet/high | 098 | — | **D-28**. Column removal needs the owner after the inventory |
| ✅ [done] 067 | [To Do composite score on calendar days, one shared function](067-todo-composite-score-calendar-days.poml) | FULL | sonnet/high | 081, 098 | R | **D-29**; boards re-rank once |

### Phase 7 — Inquiry and the classifier

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 070 | [The Inquiry: Action + Binding, an executor the commit route calls](070-inquiry-action-and-binding.poml) | FULL | sonnet/high | 001, 036 | — | The Action **row**, not the Action **Engine**. **Amended**: SLA deferred (**D-20**); record written by 043 (D-17); **no response-due date (D-56)**; no chat session (D-52) |
| ✅ [done] 071 | [Disposition accrual](071-disposition-accrual.poml) | FULL | sonnet/high | 070 | **G** | Do-rule action rate reads `sprk_resolutiontype` |
| ✅ [done] 072 | [Guidance injection](072-classifier-guidance-injection.poml) | FULL | sonnet/high | 004 | **G** | Prompt changes; the schema `enum` does NOT |
| ⏸️ [deferred] 073 | [`sprk_memo` as source #2](073-memo-as-second-signal-source.poml) | FULL | sonnet/high | 030, 072, 037, 039, 031 | **G** | The consumer diff must be **empty** | · **Deferred** 2026-10-17 by the owner (D-157): DI-108 [#2051](https://github.com/spaarke-dev/spaarke/issues/2051)
| ✅ [done] 074 | [**Recall measurement (exit gate)**](074-classifier-recall-measurement-gate.poml) | FULL | **opus/xhigh** | 072 | — | 🔴 **CAN FAIL THE PROJECT.** ≥80% on ≥50 items; writes **three recall columns on `sprk_triagecategory` (D-49)** after `/conflict-check` with the email project |

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
| ✅ [done] 120 | [ISS-018: restore the notification playbooks (id lists, Condition operands, item templates, Due Soon, scheduler)](120-notification-playbooks-iss018-fix.poml) | FULL | **opus**/high | — | F | **D-77..D-80**: no notification has been delivered in dev since at least 2026-07-11 (#1452). Own PR; dev deploy needs separate owner approval |
| ⏸️ [deferred] 121 | [D-81: LegalWorkspace 106 date filters live on dev with no tenant-wide publish; settle the LW deploy path + scoped solution import](121-legalworkspace-deploy-without-tenant-publish.poml) | FULL | sonnet/high | — | — | Fact-finding first: LW source is compiled into the Console (`sprk_corporateworkspace` retired; the Custom Page's PCF source is gone), so D-75 may already have shipped it. Script fix = own PR. **Import: owner approval required.** 114 depends on it |
| ⏸️ [deferred] 122 | [D-81: external SPA dev deploy (Static Web App) with 056/098/106/111; close #1428](122-external-spa-dev-deploy.poml) | FULL | sonnet/high | — | — | Target is SWA `green-dune-…` via `deploy-external-spa.yml` (CIAM values non-secret, in the workflow + `config/environments.json`); last run 07:11Z predates 056/106. **Dispatch: owner approval required** (ship list) |
| ✅ [done] 123 | [ISS-010 #1387 triage category honours enabled/active; ISS-002 #1049 every $choices degrade path counted + Error](123-triage-category-resolution-and-choices-visibility.poml) | FULL | sonnet/high | — | J | **Branch** (builds on 072's `AdditionalFilterFor` + `ChoicesResolutionTelemetry`). **Alert rule: owner approval required** |
| ✅ [done] 124 | [ISS-017 #1447 GridOverviewHandler `{{today}}` = caller's day (D-25), own PR](124-grid-overview-today-user-local-own-pr.poml) | FULL | sonnet/high | — | J | Reuse `DataverseUserTimeZone` (copy `EventCompletionDate.cs`) |
| ⏸️ [deferred] 125 | [ISS-001 #1048 drifted AI action mirrors + `Create_Task_From_Email` 400s](125-action-mirror-drift-suggest-followups-create-task.poml) | FULL | sonnet/high | — | J | Diagnosis read-only; **any `sprk_analysisaction` write: owner approval required** |
| ✅ [done] 126 | [ISS-009 #1399 G5 AppendTo for business-owned lookup targets (uac-r2's rule), own PR](126-appendto-business-owned-lookup-targets-uac.poml) | FULL | **opus**/high | — | — | Gate: #1391 + 048 merged; rule named (D-113) · **[uac]** | · **Done** 2026-10-16: [#2012](https://github.com/spaarke-dev/spaarke/pull/2012) merged `c7e0f8106` (2 Opus passes, 2 fix rounds, uac-r2 CLEAN, live proof passed; D-148) |
| ✅ [done] 127 | [ISS-014 #1412 SpaarkeAi ribbon build fails in a clean checkout (Deploy SpaarkeAi red on master), own PR](127-spaarkeai-ribbon-build-clean-checkout-own-pr.poml) | FULL | sonnet/**xhigh** | — | J | Once merged, master pushes auto-deploy the Console to dev again (owner told before merge) |
| ✅ [done] 128 | [Client test-harness repairs, own PR: #1416, #1417, #1388, #1392](128-client-test-harness-repairs-own-pr.poml) | FULL | sonnet/high | — | J | Test files/config only; rebase SemanticSearchControl after #1415 |
| ⏸️ [deferred] 129 | [D-63 completion: chart/view/grid definitions off `sprk_finalduedate` + deploy VisualHost 1.4.39 (068) without tenant-wide publish](129-visualhost-finalduedate-definitions-and-pcf-deploy.poml) | FULL | sonnet/high | 068 | — | Inventory read-only; **row edits and the import: owner approval required** |
| ✅ [done] 130 | [D-83: remove tenant-wide publish repo-wide (one scoped-publish procedure for scripts + skills)](130-scoped-publish-repo-wide.poml) | FULL | sonnet/high | — | J | Owner D-83. Skill text applied by the main session · **Done** 2026-10-21: live proof (D-176) of a scoped PublishXml for six web resources on spaarkedev1; nothing unrelated was published (deploy-log) |
| ⏸️ [deferred] 131 | [D-100: retire the seven notification playbooks (deploy path, alert, unused-code inventory)](131-notification-scheduler-schedules-dedup-designer.poml) | FULL | sonnet/high | — | S1 | Repurposed 2026-10-09 (D-100). Azure alert deletion and code deletion need owner approval |
| ⏸️ [deferred] 132 | [Invalid appnotification option values in the non-playbook writers (held-email alert, grant reminder, toast type)](132-notification-delivery-correctness.poml) | FULL | sonnet/high | — | S1 | D-98 item 9; rewritten on D-100 |
| ⏸️ [deferred] 133 | [System playbooks read-only in the Playbook Designer](133-designer-readonly-system-playbooks.poml) | FULL | sonnet/high | — | S1 | PR #1497 open (not merged). D-97 / sweep PB-08 (protects matter-health-single etc.) |
| ⏸️ [deferred] 134 | [uac-r2 follow-through: membership role credit bug (PB-07) + AiAnalysis write tools (AI-EX7)](134-uac-membership-role-credit-and-aianalysis-writes.poml) | FULL | **opus**/high | — | S1 | Blocked on uac-r2 (#1355 comment 6074035905) |
| 🚫 [cancelled] 135 | [Fix the Matter Health insights playbook; deactivate broken demo + junk playbooks](135-matter-health-playbook-fix.poml) | FULL | **opus**/high | — | S1 | D-96 / D-101 |
| ⏸️ [deferred] 136 | [Create Analysis from the Console hub fails at Finish (404 "filed under ... not found")](136-create-analysis-hub-finish-404.poml) | FULL | **opus**/high | — | S1 | Owner checklist r1 (2026-10-08) |
| 🚫 [cancelled] 137 | [Guard the per-node endpoints for system playbooks (#1498)](137-guard-per-node-endpoints-system-playbooks.poml) | FULL | sonnet/high | 133 | S1 | Task 133 finding |
| 🚫 [cancelled] 138 | [Retire the legacy canvas Playbook Designer (demo bundle 2026-05-19; PlaybookBuilderHost PCF)](138-retire-legacy-canvas-designer.poml) | FULL | sonnet/high | — | S1 | #1497 review F2; demo deploy needs owner |
| ✅ [done] 069 | [D-60: remove foreign tables from `OntologyPlatformSolution`](069-d60-solution-hygiene.poml) | STANDARD | sonnet/high | — | E | **D-60** done 2026-10-07 by stream C2: 17 → 10 components; 11 foreign tables removed (reference only); `sprk_servicerequest` direction/disposition/responseduedate kept as column components; issue #1385 closed |
| ✅ [done] 099 | [CI: Tier 2 ADR Compliance timeout 3 → 5 min, own PR](099-ci-adr-compliance-timeout-own-pr.poml) | STANDARD | sonnet/medium | — | I2 | **D-29**. ci-workflows hot path (declared N): note it in the PR. Can start now  **Merged 2026-10-07 as PR #1346 (`dbc58d139`).** |

### Phase 10 — Ontology admin (D-22; after 031-034 and the worklist core)

| # | Task | Rigor | Tier/Effort | Deps | Wave | Note |
|---|---|---|---|---|---|---|
| ✅ [done] 104 | [Capability probe + role-gated "Ontology admin" tab](104-ontology-admin-capability-probe-and-tab.poml) | FULL | **opus**/high | 008, 059 | — | Probe as the caller; never the security boundary · **[uac]** · **Done** 2026-10-16: [#2019](https://github.com/spaarke-dev/spaarke/pull/2019) merged `b187253a` (2 Opus passes, 2 fix rounds, re-check, uac-r2 OK) |
| ✅ [done] 100 | [Slice (a): Rules read-only + On/Off + Retire](100-ontology-admin-rules-readonly-on-off-retire.poml) | FULL | sonnet/high | 104, 033, 034, 057, 026 | — | Writes as the caller; Retire closes at once | · **Done** 2026-10-16: [#2037](https://github.com/spaarke-dev/spaarke/pull/2037) merged `315d2d55c` (Opus review, fix round, Sonnet re-check clean; K4 notes in notes file) |
| ✅ [done] 101 | [Slice (b) BFF: catalog from the compiler, dry run, save, publish](101-ontology-admin-authoring-bff.poml) | FULL | **opus**/high | 104, 031, 033, 022, 036, 024 | — | `ValidateForSave`; catalog from verified joins (#46); dry run as the evaluator, filtered by caller (#39); publish stamps `sprk_inforceto` (**D-14**) · **[uac]** | · **Done** 2026-10-16: [#2039](https://github.com/spaarke-dev/spaarke/pull/2039) merged `e153d225e` (2 Opus passes + uac-r2, 3 fix rounds, live tests 2/2). Owner: Append role change; oracle options (b)/(c) |
| ✅ [done] 102 | [Slice (b) UI: the rule-authoring wizard](102-ontology-admin-rule-wizard-ui.poml) | FULL | sonnet/**xhigh** | 056, 101, 057, 058 | — | Test gates Publish; no free-text JSON | · **Done** 2026-10-17: PR #2066 merged `b9603bab3`; browser UI tests carried to 105; scope read-only pending owner; DI-112 #2069, DI-113 #2070
| ✅ [done] 102a | [Authoring contract for the rule wizard + authorable-column allow-list (101(b))](102a-authoring-contract-for-rule-wizard.poml) | FULL | **opus**/high | 101 | — | D-149 option A + D-151 (b). **Done** 2026-10-17: PR #2054 merged `a9ed6f41a`; two adversarial passes + uac-r2. Residual oracle (D-151(c) A/B) open with the owner · **Gates 102** · **[auth]** |
| ✅ [done] 102b | [Rule scope in R1: author a matter scope, evaluate matter-scoped rules, exact dry run](102b-rule-matter-scope.poml) | FULL | **opus**/high | 102, 102a | — | D-165. Closes DI-62 #1647 and DI-112 #2069. Two adversarial passes + uac-r2 · **[tenant-isolation]** | · PR #2075 merged `4ff829fa5`; live cascade applied and checked 2026-10-18; **Done**
| ✅ [done] 103 | [Slice (c): Classification admin](103-ontology-admin-classification.poml) | FULL | sonnet/high | 104, 008, 057, 072, 074 | — | Rename blocked while a rule reads it; recall from 074's columns (**D-49**) | · **Done** 2026-10-16: [#2038](https://github.com/spaarke-dev/spaarke/pull/2038) merged `6b20467f4` (Opus review, 2 fix rounds, re-check + uac-r2 found and closed the per-row BU fail-open; seeding proof). Owner: sprk_name alternate key |

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
