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

**Count**: 18 active worktrees (R2 added 2026-06-26 by `project-pipeline`; exceeds spec's 5-6 estimate; this reflects current portfolio reality post-2026-05-20 ramp — flagged for spec refinement in `ci-cd-unit-test-remediation-r1` Phase 1 task `010`).

---

## Hot-Path Overlap Summary

This section surfaces where parallel projects collide on the same hot-path surface. A reviewer for an in-flight project should consult this before opening a PR that touches any of these surfaces.

### BFF (`src/server/api/Sprk.Bff.Api/**`)

**14 active projects touch BFF.** This is the single most-contested hot-path and the reason `.claude/constraints/bff-extensions.md` exists. Projects:

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

**8 active projects touch SpaarkeAi.** Concentrated in the AI/widget portfolio:

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

**2 active projects touch CI workflows in scope**: `ci-cd-unit-test-remediation-r1` (modifies existing workflows) and `spaarke-redis-cache-remediation-r2` (adds NEW `.github/workflows/redis-key-rotation.yml` — no existing-file conflict).

`spaarke-devops-project-tracking-r1` design notes a Phase-5 polish workflow but explicitly out of r1 acceptance (D-22). No conflict.

**Coordination action**: `ci-cd-unit-test-remediation-r1` owns existing CI workflow modifications for the 28-day window. R2 adds a NEW workflow file (`redis-key-rotation.yml`) — coordinate naming + OIDC pattern via `sdap-ci.yml` reference but no file collision.

### Skill Directives (`.claude/skills/**`, `.claude/constraints/**`)

**3 active projects touch skill directives**:

- `ci-cd-unit-test-remediation-r1` — modifies `task-execute`, `project-pipeline`, `conflict-check` SKILL.md (Phase 1 Stream C)
- `spaarke-devops-project-tracking-r1` — 9 new `/devops-*` skills + 9 hooked existing skills (this is the project's core deliverable)
- `customer-provisioning-orchestration-r1` — new skill + scripts for provisioning orchestration (`/master-deploy` extension)

**Coordination action**: All three projects must serialize PRs touching `.claude/skills/INDEX.md`. Recommended order: `devops-project-tracking-r1` first (it owns the skill registry concept), then `ci-cd-unit-test-remediation-r1` (it modifies existing skills), then `customer-provisioning-orchestration-r1` (it adds new skills).

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
