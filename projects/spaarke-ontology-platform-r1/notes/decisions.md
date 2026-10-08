# Decisions log — Spaarke Ontology Platform R1

> **What this is**: the consolidated log of every owner decision for this project — one line per decision, its
> source, and whether it still binds. The binding subset is restated as one-liners in [`../CLAUDE.md`](../CLAUDE.md)
> §3; full rationale stays in the source documents linked here (not copied).
> **Created**: 2026-10-07 (project CLAUDE.md restructure). **Add** new decisions here AND as a one-liner in
> CLAUDE.md §3 in the same commit; when a decision is replaced, move it to "Superseded" below with the date.

Sources: **design** = [`design.md`](../design.md) §8 (D-1..D-12, §8.0c) and §10 (29 settled items) ·
**spec** = [`spec.md`](../spec.md) §9 Owner clarifications (D-9..D-56) and §6 ADR tensions ·
**v4** = [`v4-prototype-vs-solution.md`](v4-prototype-vs-solution.md) (`#n` entries) and
[`v4-reconciliation.md`](v4-reconciliation.md) (R-/C-/S- rows).

---

## Design-time decisions (2026-09-30 → 2026-10-03)

| ID | Outcome (one line) | Source | Status |
|---|---|---|---|
| §10 ×29 | 29 settled design decisions — "do not re-litigate" | design §10 | Binding |
| D-1 | Spend-data spike resolved by **seeding dev data** (§8.1 checklist) | design §8 | Done (task 005) |
| D-2 | Decision Record field list approved: mandatory `sprk_factsnapshot`; nullable action; nullable decision ref on the Signal; **Decision Record 1 → N Signals** (FK on the Signal) | design §8, schema-draft §2 | Binding |
| D-3..D-8 | Remaining design-phase resolutions (taxonomy reuse D-4: `sprk_memo` reuses `sprk_triagecategory`; MM connector out of scope D-5; …) | design §8 | Binding as written there |
| D-9 | **ADR-039 → path A** (Policy decides what is *true*, Binding what *executes*); **ADR-040 → path B** (`SessionGate` and Decision Record are siblings) | spec §9, §6; design §8.0c | Binding (ADR-040 amended, task 003) |
| D-10 | Classifier recall floor **≥ 80% on ≥ 50 labelled items** — exit gate (task 074) | spec §9 | Binding |
| D-11 | Suppression: 3 dismissals → suppressed, **expires after 30 days**. Grain amended by D-34/D-36/D-39 (per policy + core record; per item when no core record) | spec §9 | Binding (as amended) |
| D-12 | **One evaluator, cadence by lane, two event hooks** (nightly `IScheduledJob` + classification and budget-revision triggers) | spec §9; design §8.3 | Binding |

## Earlier owner decisions recorded during execution (2026-10-04 → 2026-10-06)

| Date | Outcome | Source | Status |
|---|---|---|---|
| 2026-10-04 | **ADR-028 path A**: the Signal writer uses its own managed identity (`mi-ontology-writer-dev`), never the BFF's System Administrator identity, with no credential-chain fallback | spec §6; task 006 | Binding — shape amended 2026-10-07 (owner): tenant-pinned `DefaultAzureCredential` locked to the writer's UAMI, every other source excluded + `AZURE_TOKEN_CREDENTIALS` refused unless MI, pinned by tests; I5 satisfied |
| 2026-10-05 | Task 022: validate rule bodies **at evaluation** and fail closed; model-driven app authoring stays allowed | task 022 | Binding |
| 2026-10-05 | **ADR-009 path A** for task 096's in-process verdict cache | spec §6 | Done (#1294) |
| 2026-10-05 | Prototype **v4 `HANDOFF.md` @ `ae1cc9f`** is the UI/UX baseline; where v4 shows something the solution can't do, flag it to the owner | design header, spec §8.2 | Binding |
| 2026-10-05 | **Ontology admin is in R1** (coordinator's call, delegated) | spec D-22 | Binding |
| 2026-10-05 | One canonical modal approach (dark mode, no white OOB header) → became D-26 | spec D-26 | Binding |
| 2026-10-05 | SmartTodo's palette is the ONE due-urgency scheme (overdue red · 0–3 dark orange · 4–7 yellow · 8–10 grey) | notes/081-progress.md | Binding |
| 2026-10-05 | Unrelated defects found by this project ship as **their own PRs** to master (081, 084–099 …) | task POMLs | Binding |
| 2026-10-05 | Event date columns → **Date Only** (task 098) | task 098 | Binding |
| 2026-10-06 | Event routes use the solution's record-level authorization; after #1312: **one mechanism, `RecordRouteAccessAuthorizationFilter`**, #1312's route deletions stand | task 097 completion | Done (#1302) |
| 2026-10-06 | **Reassigned events are completable**: one predicate `EventStatusCode.IsOpenWork` (Draft, Open, On Hold, Reassigned) for the complete gate and all To Do generation rules | task 097 | Binding |
| 2026-10-06 | **"My events"** = owner OR assigned to caller's linked contact OR `sprk_createdbyperson` = caller; runs as the caller | task 097 | Binding |
| 2026-10-06 | To Do generation's "today" = the **To Do recipient's** time zone (→ owner if a systemuser → UTC), cached per user | task 098 (PR #1359) | Binding |
| 2026-10-06 | The Xrm capability guard is a **blocking Tier 1 job**; `ci-router.yml` docs-only = every changed file is documentation — **path A exception** to ci-cd-unit-test-remediation-r1 FR-A02 | spec §6 | Done (#1309) |

## v4 consolidated decisions (2026-10-07) — spec §9 D-13..D-56

| ID | Outcome | Status |
|---|---|---|
| D-13 | **R-11**: episode-scoped dedupe key `{policycode}|{type}|{id}|{episode}`; re-raise when not suppressed AND (Decide) the per-rule quiet window (default 14 d) passed, or (Do) the subject's date changed | Binding |
| D-14 | Spaarke Ontology Administrator gets **Write on `sprk_policyversion`** so publishing stamps `sprk_inforceto`; the rule body stays immutable by code | Binding (granted, task 008) |
| D-16 | R1 ships Path B + the Do-lane grammar extension + the **Threshold** rule type; Switch and the inquiry SLA deferred | Binding |
| D-17 | **One BFF commit route, record last**; no record on failure; Console User loses Write on Signal (task 049) | Binding |
| D-18 | The **writer creates budget revisions** (after a caller check) and the revision **updates the budget amount** | Binding (privilege granted, task 008) |
| D-19 | Approve variance = **record-only** action | Binding |
| D-20 | Inquiry SLA + its three actions **deferred after R1** | Binding |
| D-21 | This project builds the **server-side work-assignment create** | Binding |
| D-22 | Ontology admin = a **gated workspace tab**, writes as the caller; admin role granted on `sprk_triagecategory`; five tables in Spaarke Platform read-only; owner's account holds the admin role | Binding (role + app done, task 008) |
| D-23 | The **UI composes the row headline**; the rule sentence stays literal | Binding |
| D-24 | **FR-28 amended** to name the UI kit; reuse first | Binding |
| D-25 | "Today" for each item = **assignee's → owner's → UTC** time zone | Binding |
| D-26 | **Adopt the canonical modal** (`SprkModal` + `WizardShell`, in-app, ADR-050 amendment) **and migrate existing wizards** in this project | Binding |
| D-27 | Events: the Do lane, Reschedule and the Daily Briefing use **`sprk_duedate` always**; `sprk_finalduedate` informational | Binding |
| D-28 | **`statuscode` is authoritative**; `sprk_eventstatus` deprecated (inventory readers first) | Binding |
| D-29 | To Do score on **calendar days**; writer **AppendTo** on communication/event/todo/workassignment; caller-unresolved stays **#1312's single 403**; Tier 2 ADR Compliance timeout **5 min** | Binding (grants + CI done) |
| D-30 | **Severity column on `sprk_policy`** (Info/Warning/Critical, shown High/Medium/Low) | Binding (column live, task 007) |
| D-31 | Overdue-To Do rule applies to **all To Dos** | Binding (no-core visibility per D-35) |
| D-32 | Switching a rule Off → On **re-raises** still-true subjects as new episodes | Binding |
| D-33 | **Signals and Decision Records are secure children** of secured records (access-control mechanism), with three role edits | Binding (roles done, task 008; registration = task 039, awaiting uac-r2 review #1355) |
| D-34 | Signals and Decision Records group under the item's **core record** (not "matter" only), reusing `CoreAncestorResolver` | Binding (extended by D-36) |
| D-35 | Items with **no core record** → Signal visible to the item's **owner only**, "Not filed" group | Binding |
| D-36 | **All four core types** (matter, project, work assignment, service request), and the model is **extensible**: generic core-record reference (catalog type + id) on Signal and Decision Record; typed lineage lookups on the Decision Record only where the secure mechanism needs them | Binding (columns live, task 007) |
| D-37 | Two core records → the item's **direct filed-under** record wins; ambiguous → matter over project | Binding |
| D-38 | **No skips** anywhere | Binding |
| D-39 | No-core items: suppression per **(policy, item)**; their Decision Record is owned by the item's owner | Binding |
| D-40 | Rule date fields may take **one lower + one upper bound** (nothing more) | Binding (done, task 024) |
| D-41 | **To Do date columns → Date Only**, own task 106 (031 depends on it) | Binding |
| D-42 | Rank: **severity → `sprk_highpriority` → oldest → record number** | Binding (tasks 038, 062) |
| D-43 | Do-lane overdue starts at **1 day** (a knob; tune in UAT) | Binding (061, 064) |
| D-44 | A work assignment shows in the Do lane of **both** assigner and assignee | Binding (038) |
| D-45 | *Reassign* and *Extend response date* are **Routine** | Binding (036) |
| D-46 | **Drop** the `DocumentRowMenu` / `OutcomeCard` reuse (FR-27); status bar on `MessageBar`; C-9 continues on its own | Binding (052) |
| D-47 | Console **Decision Record tab** in R1 | Binding (045) |
| D-48 | **Association-confirmed** trigger (third enqueue site; email project calls it) | Binding (032) |
| D-49 | Recall stored in **three `sprk_triagecategory` columns** (task 074, conflict-check with email project) | Binding (074, 103) |
| D-50 | Null fact renders **Missing**; source freshness filed as a **GitHub issue** | Binding (057) |
| D-51 | Know-promotion rule offers **Assign Work** | Binding (063) |
| D-52 | Wizard **Confirm is the confirmation**; gate tier from a pure `PublicContracts` function; no chat session | Binding (043, 070) |
| D-53 | **Templated (non-AI) drafts** in R1 | Binding (058) |
| D-54 | **Response columns on `sprk_workassignment`** (exact set decided in task 047, with the WA owner and uac-r2) | Binding (047, 044, 061) |
| D-55 | Budget **amount written as the signed-in user**; the writer only creates the revision | Binding (044) |
| D-56 | **No response-due date** on the inquiry in R1 | Binding (070) |

## Execution decisions (2026-10-07, late) — spec §9 D-57..D-62

| ID | Outcome | Status |
|---|---|---|
| D-57 | Draft events: **no data change**; the Daily Briefing uses `IsOpenWork` (task 060 re-scoped, own PR) | Binding |
| D-58 | Work-assignment response columns `sprk_respondedon` + `sprk_responseoutcome` approved by the owner as area owner | Binding (task 047) |
| D-59 | Server-side work-assignment create on uac-r2's `RecordCreationService` (team-owned), under their review | Binding (task 046) |
| D-60 | Remove the 11 foreign tables from `OntologyPlatformSolution` (reference only) | Binding |
| D-61 | Evaluator writes Signal fields **only when the result changes** (uac-r2 100k-rows-per-pass limit) | Binding (task 031) |
| D-62 | Remove Console User **Create on Decision Record** with task 049 | Binding |
| D-63 | `sprk_finalduedate` informational **everywhere** (notification playbook node, VisualHost card, CalendarVisual) | Binding (task 068) |
| D-64 | Recall gate: **synthetic set, owner labels blind**, ~80–100 items, gate = combined fee-OR-scope recall ≥ 80%, one run | Binding (task 074) |
| D-65 | **One canonical server create path per table**; uac-r2 to pick one of `RecordCreationService` / `OwnedChildWrite` for work assignments and retire the other; 046 follows it, never a third (supersedes D-59's component choice) | Binding (046, awaiting uac-r2) |
| D-66 | Move the shared Dataverse write core out of `Services/Ai` to `Services/Dataverse` (AI → core only), own PR, uac-r2 review; **no ADR-013 exception** | Binding (task 048) |
| D-67 | Merge #1390 after uac-r2 approves → deploy master to dev → finish 039 live gate | Binding |
| D-68 | Merge #1391 (uac-r2 org-owned AppendTo fix) when green + uac-r2 approves | Binding |
| D-69 | WizardShell's skipped-step marker (dashed ring) is **opt-in** per wizard; existing wizards keep the tick for a skipped step; the 058 decision wizard opts in (from the #1386 review F1) | Binding (056, 058) |
| D-70 | **ADR-050 Path A** for 056's transitional `SprkModal.legacySize` and `WizardShell` `maxWidth`/`height`; **removed in task 111** with a test that they are gone (from the #1386 review F2) | Binding (056, 111) |

## Superseded or withdrawn

| ID | What it said | Replaced by | Date |
|---|---|---|---|
| D-15 | Skip **Restricted/Limited** matters in R1 and read Signals only through a BFF route | **D-33** — the premise was wrong: Restricted/Limited only exclude external contacts (ADR-003); the staff wall is the **Secure** flag. The BFF read route part of D-15 still stands (FR-24) | 2026-10-07 |
| D-31 (wording) | A no-matter To Do's Signal is "secured by the To Do owner's business unit" | **D-35** (owner only) | 2026-10-07 |
| D-34 (scope) | Core record = matter **or project** only | **D-36** (all four core types, extensible) | 2026-10-07 |
| D-11 (grain) | Suppression per **(policy, matter)** | **D-34/D-36** per (policy, core record); **D-39** per (policy, item) for no-core items | 2026-10-07 |
| C-12 option | Derive a version's end from its successor (never write `sprk_inforceto`) | **D-14** (grant Write; stamp at publish) | 2026-10-07 |
| task 053 | Gate host + acting | Tasks **058** (decision wizard) + **043** (commit route), per D-17/D-26 | 2026-10-07 |
| Narrow skip | Skip a To Do under a Secure project with no matter | **D-38** (no skips) | 2026-10-07 |
