# Active Projects Registry — `projects/INDEX.md`

> **Spec authority**: `projects/ci-cd-unit-test-remediation-r1/spec.md` FR-C02
> **Last refresh**: 2026-06-26 (initial sweep by task CICD-030)
> **Next auto-refresh**: on demand — see Maintenance Contract below

---

## Purpose

This file is the **single-source-of-truth registry of currently-active Spaarke projects** and their **hot-path touch declarations** across four cross-cutting surfaces that drive parallel-project coordination:

1. **BFF** — `src/server/api/Sprk.Bff.Api/**` (the unified backend)
2. **SpaarkeAi** — `src/solutions/SpaarkeAi/**` (the AI workspace UI surface)
3. **CI Workflows** — `.github/workflows/**` (Tier 1 / Tier 2 / nightly health)
4. **Skill Directives** — `.claude/skills/**` and `.claude/constraints/**` (shared agent guidance)

When two or more active projects touch the same hot-path surface, the second-to-merge project incurs merge friction and (potentially) wasted task work. This registry exists so the `project-pipeline` skill can warn at Step 2 (resource discovery) and so `task-execute` can warn before opening a PR that overlaps with an in-flight peer.

---

## Maintenance Contract (binding per spec FR-C02)

This file is maintained **atomically by two skills** — no cron, no nightly job, no manual editorial sweep:

| Trigger | Skill | Action |
|---|---|---|
| New project starts | `project-pipeline` (Step 4 — worktree setup) | Append the new project's row with its declared hot-path touches |
| Task touches a previously-undeclared hot-path | `task-execute` (Step 9 — pre-PR check) | Update the project's row to flip the relevant column to `Y` |
| Project completes / worktree archived | `devops-project-archive` skill | Remove the project's row OR move it to a `## Archived` section (TBD by tracking project) |

**Scoping rule**: Only worktrees with **last commit ≥ 30 days ago** are listed here. Worktrees with older last-touch are considered dormant and require manual re-introduction via `worktree-sync` + `project-continue` before reappearing in this index. This is the binding scope per user clarification 2026-06-25.

**No CI script enforces this.** The contract is enforced by the two skills above + reviewer judgment at PR time, per `ci-cd-unit-test-remediation-r1` spec FR-C02 + design.md §6 ("the registry is editorial, not gated").

---

## Active Projects (last-commit ≥ 2026-05-27)

Hot-path columns: `Y` = project actively modifies this surface; `N` = no touch; `?` = ambiguous in design.md (treat as `Y` defensively until clarified).

Status legend:
- **Active** — last commit within 7 days
- **Recent** — last commit 7–30 days
- **Dormant** — last commit > 30 days (NOT listed here per scoping rule)

| Project | Branch | Worktree Path | BFF | SpaarkeAi | CI Workflows | Skill Directives | Last Commit | Status |
|---|---|---|---|---|---|---|---|---|
| `spaarke-daily-update-service-r5` | `work/spaarke-daily-update-service-r5` | `C:/code_files/spaarke-wt-spaarke-daily-update-service-r5` | Y | Y | N | Y | 2026-07-08 | **INITIALIZED 2026-07-08** — Daily Briefing accuracy-by-construction (deterministic item rows + deterministic-fact TL;DR, no groundedness threshold) + visual redesign via `/prototype` + hardening sweep. BFF slice = `Services/Ai/Narrators/DailyBriefing*` + `Nodes/UpdateRecordNodeExecutor` (frozen-engine Path-A defect fix) ONLY — r2-core remains sole owner of the engine internals; coordinate `Services/Ai/` via `/conflict-check` before each wave. Skill = `jps-validate` Step 7.7 (main-session edit). Monitored-For + EventDetailSidePane DEFERRED. |
| `spaarke-ai-architecture-redesign-r2` | `work/spaarke-ai-architecture-redesign-r2` | `C:/code_files/spaarke-wt-spaarke-ai-architecture-redesign-r2` | Y | Y | N | Y | 2026-07-08 | **INITIALIZED 2026-07-08** — platform CORE (judgment + memory); **sole owner of `Services/Ai/` internals**; 53 tasks decomposed, awaiting operator plan review before execution; publishes seams (ComposeDisposition/OutcomeCard/ContextEnvelope/GateDecision v2/TraceEvent/JobAwareCompletionState) that `spaarkeai-compose-r2` consumes. ADR-041/042 candidates. |
| `spaarkeai-compose-r2` | `work/spaarkeai-compose-r2` | `C:/code_files/spaarke-wt-spaarkeai-compose-r2` | Y | Y | N | N | 2026-07-08 | **ACTIVE (registry backfill 2026-07-08)** — Compose editor/lifecycle satellite; **consumes** the r2-core seams (already re-based, 2026-07-08 design revision); does NOT modify `Services/Ai/` internals. Coordinate every BFF PR with `spaarke-ai-architecture-redesign-r2` via `/conflict-check`. |
| `spaarke-ai-architecture-redesign-r1` | `work/spaarke-ai-architecture-redesign-r1` | `C:/code_files/spaarke-wt-spaarke-ai-architecture-redesign-r1` | Y | Y | Y | Y | 2026-07-08 | **COMPLETE 2026-07-08 pending PR #551 merge** — 51/51 tasks + 090 wrap-up; ADR-039/040 Accepted; dispatcher stack + engine shells + builder surface DELETED (Track-B audit zero unexplained survivors); absorbed+closed `spaarke-ai-platform-unification-r7`; G-M deferred post-r2 (#555); deferrals #552–#557; successor `spaarke-ai-architecture-redesign-r2` (design v0.2, worktree TBD at /project-pipeline) |
| `spaarke-dataset-grid-framework-r2` | `work/spaarke-dataset-grid-framework-r2` | `C:/code_files/spaarke-wt-spaarke-dataset-grid-framework-r2` | Y | Y | N | Y | 2026-07-02 | **Code complete pending PR #537 merge + deploy regression (2026-07-02); DEF-002 follow-on flipped BFF hot-path N→Y** |
| `record-header-and-notepad-r1` | `work/record-header-and-notepad-r1` | `C:/code_files/spaarke-wt-record-header-and-notepad-r1` | N | N | N | N | 2026-07-03 | **Code complete (33/36 tasks, 237 tests); env-dependent tasks 025/039/040 pending owner deploy + QA** |
| `ai-spaarke-ai-workspace-UI-r2` | `work/ai-spaarke-ai-workspace-UI-r2` | `C:/code_files/spaarke-wt-ai-spaarke-ai-workspace-UI-r2` | N | Y | N | N | 2026-07-01 | **Complete pending PR #530 merge (2026-07-01)** |
| `spaarkeai-compose-r1` | `work/spaarkeai-compose-r1` | `C:/code_files/spaarke-wt-spaarkeai-compose-r1` | Y | Y | N | N | 2026-06-30 | Active |
| `spaarke-ai-platform-unification-r7` | `work/spaarke-ai-platform-unification-r7` | `C:/code_files/spaarke-wt-spaarke-ai-platform-unification-r7` | Y | N | N | Y | 2026-07-05 | **CLOSED / RE-SCOPED 2026-07-05** — Issue #501 closed; remaining waves absorbed by `spaarke-ai-architecture-redesign-r1` (#550) or dropped per `projects/spaarke-ai-platform-unification-r7/notes/close-out-absorbed-by-ai-architecture-redesign-r1.md`; branch disposition = redesign-r1 task 025 (FR-P1-06); archive row after task 025 |
| `spaarke-redis-cache-remediation-r2` | `work/spaarke-redis-cache-remediation-r2` | `C:/code_files/spaarke-wt-spaarke-redis-cache-remediation-r2` | Y | N | Y | N | 2026-06-26 | Active |
| `spaarke-redis-cache-remediation-r1` | `work/spaarke-redis-cache-remediation-r1` | `C:/code_files/spaarke-wt-spaarke-redis-cache-remediation-r1` | Y | N | N | N | 2026-06-26 | Active |
| `spaarke-daily-update-service-r4` | `work/spaarke-daily-update-service-r4` | `C:/code_files/spaarke-wt-spaarke-daily-update-service-r4` | Y | Y | N | N | 2026-06-26 | Active |
| `spaarke-ai-platform-unification-r6` | `work/spaarke-ai-platform-unification-r6` | `C:/code_files/spaarke-wt-spaarke-ai-platform-unification-r6` | Y | Y | N | N | 2026-06-26 | Active |
| `spaarke-ai-platform-chat-routing-redesign-r1` | `work/spaarke-ai-platform-chat-routing-redesign-r1` | `C:/code_files/spaarke-wt-spaarke-ai-platform-chat-routing-redesign-r1` | Y | Y | N | N | 2026-06-25 | Active |
| `ci-cd-unit-test-remediation-r1` | `work/ci-cd-unit-test-remediation-r1` | `C:/code_files/spaarke-wt-ci-cd-unit-test-remediation-r1` | N | N | Y | Y | 2026-06-25 | Active |
| `spaarke-daily-update-service-r3` | `work/spaarke-daily-update-service-r3` | `C:/code_files/spaarke-wt-spaarke-daily-update-service-r3` | Y | Y | N | N | 2026-06-25 | Active |
| `spaarke-ai-azure-setup-dev-r1` | `work/spaarke-ai-azure-setup-dev-r1` | `C:/code_files/spaarke-wt-spaarke-ai-azure-setup-dev-r1` | Y | N | N | N | 2026-06-25 | Active |
| `smart-todo-r4` | `work/smart-todo-r4-closeout` | `C:/code_files/spaarke-wt-smart-todo-r4` | Y | Y | N | N | 2026-06-25 | Active |
| `spaarke-devops-project-tracking-r1` | `work/spaarke-devops-project-tracking-r1` | `C:/code_files/spaarke-wt-spaarke-devops-project-tracking-r1` | N | N | N | Y | 2026-06-25 | Active |
| `spaarke-platform-foundations-r3` | `work/spaarke-platform-foundations-r3` | `C:/code_files/spaarke-wt-spaarke-platform-foundations-r3` | Y | N | N | N | 2026-06-24 | Active |
| `ai-spaarke-insights-engine-widgets-r1` | `work/ai-spaarke-insights-engine-widgets-r1` | `C:/code_files/spaarke-wt-ai-spaarke-insights-engine-widgets-r1` | Y | Y | N | N | 2026-06-24 | Active |
| `spaarke-multi-container-multi-index-r1` | `work/spaarke-multi-container-multi-index-r1-phase-g-followups` | `C:/code_files/spaarke-wt-spaarke-multi-container-multi-index-r1` | Y | Y | N | N | 2026-06-24 | Active |
| `spaarke-daily-update-service-r2` | `work/spaarke-daily-update-service-r2.3-orchestrator-diagnosis` | `C:/code_files/spaarke-wt-spaarke-daily-update-service-r2` | Y | Y | N | N | 2026-06-23 | Recent |
| `customer-provisioning-orchestration-r1` | `work/customer-provisioning-orchestration-r1` | `C:/code_files/spaarke-wt-customer-provisioning-orchestration-r1` | Y | N | N | Y | 2026-06-18 | Recent |
| `smart-todo-decoupling-r3` | `work/smart-todo-r3-wrap-up` | `C:/code_files/spaarke-wt-smart-todo-decoupling-r3` | Y | N | N | N | 2026-06-10 | Recent |
| `email-communication-solution-r3` | `work/email-communication-solution-r3` | `C:/code_files/spaarke-wt-email-communication-solution-r3` | Y | N | N | N | 2026-06-05 | Recent |
| `ai-spaarke-action-engine-r1` | `work/ai-spaarke-action-engine-r1` | `C:/code_files/spaarke-wt-ai-spaarke-action-engine-r1` | Y | Y | N | N | 2026-05-30 | Recent |

**Count**: 24 active worktrees (`spaarke-ai-architecture-redesign-r1` added 2026-07-05 by `project-pipeline` — hot-path BFF=Y/SpaarkeAi=Y/Skills=Y; it formally closes `spaarke-ai-platform-unification-r7`, whose row should be archived when task 013/025 complete; `spaarke-dataset-grid-framework-r2` + `record-header-and-notepad-r1` both added 2026-07-02 by `project-pipeline` — the latter is hot-path=N across all four surfaces, no coordination required; `ai-spaarke-ai-workspace-UI-r2` added 2026-07-01 by `project-pipeline`; `spaarkeai-compose-r1` added 2026-06-29 by `project-pipeline`; R7 added 2026-06-28 by `project-pipeline`; R2 added 2026-06-26 by `project-pipeline`; exceeds spec's 5-6 estimate; this reflects current portfolio reality post-2026-05-20 ramp — flagged for spec refinement in `ci-cd-unit-test-remediation-r1` Phase 1 task `010`).

---

## Hot-Path Overlap Summary

This section surfaces where parallel projects collide on the same hot-path surface. A reviewer for an in-flight project should consult this before opening a PR that touches any of these surfaces.

### BFF (`src/server/api/Sprk.Bff.Api/**`)

**16 active projects touch BFF.** This is the single most-contested hot-path and the reason `.claude/constraints/bff-extensions.md` exists. Projects:

- `spaarke-ai-architecture-redesign-r1` (**broadest AI touch**: `Services/Ai/**` executor/loop/gate/router/tools/ledger + `Models/Ai/Chat` + endpoints + DI; DELETES the dispatcher stack (PlaybookDispatcher, IntentReranker, CandidateSelector, CompoundIntentDetector) at P2 and engine shells at P3; absorbs+closes `spaarke-ai-platform-unification-r7`; supersedes `spaarke-ai-platform-chat-routing-redesign-r1`'s dispatch scope — coordinate any AI-dispatch PR with this project first)
- `spaarkeai-compose-r1` (Compose drafting workspace: 7 new `/api/compose/` endpoints, 3 new `Services/Compose/*` services, `ConsumerTypes.ComposeSummarize` constant; ChatSession reuse + PublicContracts facade per refined ADR-013)
- `spaarke-ai-platform-unification-r7` (AiCompletionNodeExecutor + PlaybookOrchestrationService dispatch refactor + ActionType→ExecutorType enum rename + new executor-config-schemas endpoint — foundational dispatch reform)
- `spaarke-redis-cache-remediation-r2` (Theme A: `MetricsDistributedCache`, `TenantCache`, `CacheMetrics`, `Program.cs` — closure of R1 senior-review items DEF-007/008/009)
- `spaarke-redis-cache-remediation-r1` (117 `IDistributedCache` call sites — broadest touch; closure shipped via PR #458 + #460)
- `spaarke-daily-update-service-r4` (NotificationService, playbook membership queries)
- `spaarke-ai-platform-unification-r6` (handler registry, 8 typed handlers, persona scope)
- `spaarke-ai-platform-chat-routing-redesign-r1` (PlaybookDispatcher/CapabilityRouter unification, stateful chat memory)
- `spaarke-daily-update-service-r3` (NotificationService TTL fix)
- `spaarke-ai-azure-setup-dev-r1` (RagService, indexing pipeline, index name canonicalization — 13 files)
- `smart-todo-r4` (Office endpoints)
- `spaarke-platform-foundations-r3` (membership resolution)
- `ai-spaarke-insights-engine-widgets-r1` (widget endpoints)
- `spaarke-multi-container-multi-index-r1` (routing + search index)
- `spaarke-daily-update-service-r2` (widget framework migration)
- `customer-provisioning-orchestration-r1` (configuration maps)
- `smart-todo-decoupling-r3` (Office endpoints)
- `email-communication-solution-r3` (email pipeline)
- `ai-spaarke-action-engine-r1` (action handler registry)

**Coordination action**: Any task adding a new service to `Sprk.Bff.Api` MUST run the `.claude/constraints/bff-extensions.md` checklist + state the placement decision in PR description per root CLAUDE.md §10.

### SpaarkeAi (`src/solutions/SpaarkeAi/**`)

**11 active projects touch SpaarkeAi.** Concentrated in the AI/widget portfolio:

- `spaarke-ai-architecture-redesign-r1` (P1/P3: ConversationPane decomposition, one `dispatchConsumer` helper, widget-registry dedupe, ExecutionTraceWidget, FieldDelta deletion — any chat-surface or widget-registry PR should merge-order against this project)
- `spaarke-dataset-grid-framework-r2` (FR-10 shared-package extraction: removes SpaarkeAi's `@spaarke/legal-workspace` source alias in vite.config.ts; adds file: dependency on new `@spaarke/legal-workspace` shared package. Merge-order coordination with `spaarkeai-compose-r1` required — Compose adds a section-registry entry that FR-10 must accommodate.)
- `spaarkeai-compose-r1` (Compose workspace layout: new `sprk_workspacelayout` row + `components/compose/*` React surface + section-registry entry; reuses Pattern D from Calendar)
- `spaarke-daily-update-service-r4` (widget enhancements)
- `spaarke-daily-update-service-r3` (widget read-state)
- `spaarke-daily-update-service-r2` (widget framework migration)
- `spaarke-ai-platform-unification-r6` (chat UI convergence)
- `spaarke-ai-platform-chat-routing-redesign-r1` (chat surface)
- `smart-todo-r4` (workspace widget rebuild)
- `ai-spaarke-insights-engine-widgets-r1` (Matter Health widget pattern)
- `spaarke-multi-container-multi-index-r1` (Code Page search index parameter)
- `ai-spaarke-action-engine-r1` (action engine UI surface)

**Coordination action**: Daily-update-service r2/r3/r4 are sequential — confirm merge ordering before opening any widget framework PR.

### CI Workflows (`.github/workflows/**`)

**3 active projects touch CI workflows in scope**: `ci-cd-unit-test-remediation-r1` (modifies existing workflows), `spaarke-redis-cache-remediation-r2` (adds NEW `.github/workflows/redis-key-rotation.yml` — no existing-file conflict), and `spaarke-ai-architecture-redesign-r1` (adds ONE self-contained `eval-gate` job to `sdap-ci.yml` — zero existing lines modified; task 026, NFR-02 merge gate; coordinate with ci-cd-unit-test-remediation-r1 before restructuring that file).

`spaarke-devops-project-tracking-r1` design notes a Phase-5 polish workflow but explicitly out of r1 acceptance (D-22). No conflict.

**Coordination action**: `ci-cd-unit-test-remediation-r1` owns existing CI workflow modifications for the 28-day window. R2 adds a NEW workflow file (`redis-key-rotation.yml`) — coordinate naming + OIDC pattern via `sdap-ci.yml` reference but no file collision.

### Skill Directives (`.claude/skills/**`, `.claude/constraints/**`)

**6 active projects touch skill directives**:

- `spaarke-ai-architecture-redesign-r1` — P4 refreshes jps-* skill guidance + PlaybookBuilder-related directives after canvas de-scope; also `.claude/catalogs/scope-model-index.json` at P4 (main-session-only per Sub-Agent Write Boundary). Sequences AFTER R7 Wave 7 jps-* rewrites are cancelled by R7's close-out (task 013).

- `spaarke-dataset-grid-framework-r2` — FR-09 adds dual-deploy warning section to `.claude/skills/code-page-deploy/SKILL.md` (single skill; no `INDEX.md` touch). Sequential-only per CLAUDE.md §3 sub-agent write boundary.
- `ci-cd-unit-test-remediation-r1` — modifies `task-execute`, `project-pipeline`, `conflict-check` SKILL.md (Phase 1 Stream C)
- `spaarke-devops-project-tracking-r1` — 9 new `/devops-*` skills + 9 hooked existing skills (this is the project's core deliverable)
- `customer-provisioning-orchestration-r1` — new skill + scripts for provisioning orchestration (`/master-deploy` extension)
- ~~`spaarke-ai-platform-unification-r7` — REWRITES `jps-action-create`, `jps-playbook-design`, `jps-playbook-audit`, `jps-validate` + MINOR UPDATE `jps-scope-refresh` (Wave 7, FR-32/FR-33; node-first dispatch model)~~ **CANCELLED 2026-07-05** — R7 closed; jps-* skill work absorbed by `spaarke-ai-architecture-redesign-r1` P4 (re-based on the Action/Binding catalog model, not node-first dispatch)

**Coordination action**: Four projects serialize PRs touching `.claude/skills/INDEX.md`. Recommended order: `devops-project-tracking-r1` first (owns skill registry concept), then `ci-cd-unit-test-remediation-r1` (modifies existing skills), then `customer-provisioning-orchestration-r1` (adds new skills), then `spaarke-ai-architecture-redesign-r1` P4 (jps-* skill refresh — took over the slot formerly held by R7 Wave 7, which was cancelled at R7 close 2026-07-05; main-session-only per Sub-Agent Write Boundary CLAUDE.md §3).

---

## Excluded Worktrees (last commit < 2026-05-27)

The following worktrees are checked-out but dormant per the 30-day scoping rule. They are NOT included in the active table above and do NOT participate in hot-path coordination until re-activated:

- `work/ai-procedure-quality-r1` (last commit 2026-05-17)
- `work/spaarke-auth-v2-and-hardening` (last commit 2026-05-19)
- `work/spaarke-matter-ui-enhancement-r1` (last commit 2026-05-30; just under the threshold — re-check at next refresh)
- `work/sdap-bff.api-test-suite-repair` (last commit 2026-05-31; just under the threshold)

Plus all `work/r5-*`, `work/insights-engine-r2-*`, `work/insights-engine-r3-init`, `work/github-actions-rationalization-r1*`, `work/spaarke-datagrid-framework-r1`, and the broader set of pre-2026-05-27 worktrees.

---

## Pointers

- **Root CLAUDE.md §10 (BFF Hygiene)** — binding rules for any BFF-touching task
- **`.claude/constraints/bff-extensions.md`** — pre-merge checklist + decision criteria for BFF additions
- **`projects/ci-cd-unit-test-remediation-r1/spec.md` FR-C02** — this file's binding spec
- **`projects/ci-cd-unit-test-remediation-r1/design.md` §6** — registry maintenance rationale
- **`projects/spaarke-devops-project-tracking-r1/`** — GitHub Project-level portfolio tracker (complementary; this file is the LOCAL coordination registry)

---

*Maintained automatically by `project-pipeline` (new project) and `task-execute` (hot-path touch). Manual edits require an entry in `.claude/CHANGELOG.md`.*
