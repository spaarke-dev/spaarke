# Spaarke DataGrid Framework — R2 - AI Context

> **Purpose**: This file provides context for Claude Code when working on `spaarke-dataset-grid-framework-r2`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Ready for Implementation (tasks not yet generated)
- **Last Updated**: 2026-07-02
- **Current Task**: none (project just initialized)
- **Next Action**: Run `/task-create projects/spaarke-dataset-grid-framework-r2` to decompose plan into POML task files

---

## Quick Reference

### Key Files

- [`spec.md`](spec.md) — AI-optimized implementation specification (10 FRs, owner clarifications, ADR tensions)
- [`design.md`](design.md) — Original 574-line human design document
- [`README.md`](README.md) — Project overview and graduation criteria
- [`plan.md`](plan.md) — Implementation plan with 4-phase WBS
- [`current-task.md`](current-task.md) — **Active task state** (for context recovery across compaction)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Task tracker (will be created by `/task-create`)

### Project Metadata

- **Project Name**: `spaarke-dataset-grid-framework-r2`
- **Type**: Client-side framework enhancement (shared library + wizard code page + consumer unwind)
- **Complexity**: Medium — 10 FRs across 4 phases, 3 phased PRs, ~3.5 days
- **Worktree**: `C:/code_files/spaarke-wt-spaarke-dataset-grid-framework-r2` on branch `work/spaarke-dataset-grid-framework-r2`
- **Hot Paths**: BFF=Y (DEF-002 follow-on 2026-07-02 — see spec.md hot-path-declaration), SpaarkeAi=Y, ci-workflows=N, skill-directives=Y, root-CLAUDE=N

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for FR definitions, acceptance criteria, owner clarifications
4. **Reference plan.md** for phase structure + phase-level deliverables
5. **Load the relevant task file** from `tasks/` based on current work
6. **Apply ADRs** — ADR-012, ADR-021, ADR-022, ADR-028, ADR-038 (paths in Resources section below)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke task-execute skill:

| User Says | Required Action |
|---|---|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and task file path.

### Why This Matters

The task-execute skill ensures:
- ✅ Knowledge files loaded (ADRs, constraints, patterns)
- ✅ Context tracked in `current-task.md`
- ✅ Proactive checkpointing every 3 steps
- ✅ Quality gates run (`code-review` + `adr-check`) at Step 9.5
- ✅ Progress recoverable after compaction

**Bypassing this skill leads to**: missing ADR constraints, lost progress after compaction, skipped quality gates.

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute:
- Send one message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- **Sub-Agent Write Boundary**: Tasks touching `.claude/` paths (only FR-09 in this project) MUST be sequential (main-session-only) per CLAUDE.md §3

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

### 🚨 MUST: Multi-File Work Decomposition

**For tasks modifying 4+ files** (relevant for this project: FR-08 touches 7 files, FR-10 touches many):

1. **Decompose into dependency graph** — group by module, identify inter-file dependencies
2. **Delegate to subagents in parallel where safe** — one message, multiple Agent calls
3. **Parallelize when**: different modules, no shared interfaces, no imports between files
4. **Serialize when**: shared state, one file must be created before another uses it

**Example (FR-08)**: 6 registration files + `sectionMetadataCatalog.ts` = 7 files
- Phase 1 (serial): `sectionMetadataCatalog.ts` (adds `contentSizing: 'clamped'` — depended on by verification)
- Phase 2 (parallel): 6 registration files, one subagent per file (all identical edit pattern: remove `maxHeight` + `display: flex`)

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

**Additive schema — no version bump:**
- ✅ MUST preserve `sprk_configjson._version = '1.0'`
- ✅ All new fields optional; omitted-field defaults reproduce current behavior
- ✅ Bare-string entries in `LayoutJsonRow.sections` continue to work
- ❌ MUST NOT reshape existing config record payloads or delete records

**Framework isolation:**
- ✅ Framework changes stay in `@spaarke/ui-components` (ADR-012)
- ✅ Shared-lib framework code stays React-16-safe for PCF consumer compat (ADR-022)
- ✅ New shared package (FR-10) follows `Spaarke.DailyBriefing.Components` structural template
- ❌ MUST NOT introduce Fluent UI v8 imports (ADR-021)
- ⚠️ **UPDATE 2026-07-02** — BFF hot-path flipped from N → Y via DEF-002 follow-on. CLAUDE.md §10 now applies. One new BFF endpoint (`GET /api/dataverse/gridconfigurations/{entity}`) + one new service (`GridConfigurationService`) + one DTO. Placement justified in [`design.md § Placement Justification (DEF-002)`](design.md).

**Wizard code lives in Vite app:**
- Wizard UI additions (FR-02/03/04) live in `src/solutions/WorkspaceLayoutWizard/` — Vite React 18 app
- Framework schema additions (types only) live in `src/client/shared/Spaarke.UI.Components/src/types/`
- This split is what keeps ADR-022 React-16-safety compliant

**Testing (ADR-038):**
- All new tests MAINTAIN-class per 6 KEEP categories
- ❌ NO `Mock<HttpMessageHandler>`
- ❌ NO DI-registration tests
- ❌ NO constructor null-check tests
- ❌ NO coverage-as-gate
- `/test-diet` gate at wrap-up (090-* task) — mandatory per CLAUDE.md §7

**Regression surface (mandatory pre-PR verification):**
- 6 entity-list widgets in Dashboard II: Communications, Documents, Invoices, Matters, Projects, Work Assignments
- 5 single-section full-page system layouts: Documents, Communications, Projects, Invoices, Work Assignments
- 8+ existing published `sprk_workspacelayout` records with bare-string sections (back-compat)
- Both LegalWorkspace + SpaarkeAi renders (SpaarkeAi=Y; every PR must confirm SpaarkeAi rebuild picks up the change)

**Owner clarifications applied (2026-07-02):**
- `pageSize` default: **25** (not 100, not 50, not context-aware)
- Issue 12: **Option A + Option B in R2** (Option B adds FR-10 shared-package extraction, +~1 day)
- `widthPreference` defaults: **all 6 entity-list widgets = 'full'** (dense grids read best full-width)

---

## Decisions Made

<!-- Format: YYYY-MM-DD | Decision | Rationale | Who -->

- **2026-07-02** — `pageSize` default changed from 100 → 25 (FR-07 becomes a code change, not just doc alignment). Rationale: workspace embedding is the dominant use case per owner. — Owner clarification during `/design-to-spec` interview.
- **2026-07-02** — Adopted Issue 12 Option B (shared-package extraction) in R2 scope. Rationale: eliminate SpaarkeAi ← LegalWorkspace source-alias trap permanently rather than documenting it. Adds ~1 day and PR 3. — Owner clarification during `/design-to-spec` interview.
- **2026-07-02** — All 6 entity-list widgets default to `widthPreference: 'full'`. Rationale: dense grids read best full-width; operator opts-out per-placement. — Owner clarification during `/design-to-spec` interview.
- **2026-07-02** — Ships in 3 PRs. Rationale: framework + wizard + extraction are independent review surfaces + independently reversible. — Design-time decision preserved through spec.

---

## Implementation Notes

<!-- Add gotchas, workarounds, or important learnings during implementation -->

- **File-path correction from discovery**: `sectionRegistry.ts` lives at `src/solutions/LegalWorkspace/src/sectionRegistry.ts`, NOT under `WorkspaceShell/` (spec.md corrected 2026-07-02). It may relocate to the new shared package as part of FR-10.
- **`scripts/config-templates/` does not exist yet** — FR-06 creates the directory.
- **Closest structural template for FR-10 new package**: `src/client/shared/Spaarke.DailyBriefing.Components/` (comparable scope: domain-specific section registry + widgets, similar consumer count).

---

## Deferrals & Issues — tracking obligation

This project tracks deferred work + newly-discovered issues in TWO places, kept in sync:

1. **`notes/defer-issues.md`** — source of truth
2. **GitHub Issues** on the portfolio board

### When to file

| Situation | Use |
|---|---|
| Spec scope item dropped to keep this project shippable | DEF-{NNN} |
| Refactor / cleanup > 2hr not in current spec | DEF-{NNN} |
| Production / dev bug uncovered outside this project's responsibility | ISS-{NNN} |
| Failure mode discovered + worked around (not fixed) | ISS-{NNN} |

### How to file

Invoke `/project-defer-issue-tracking` (alias `/defer`) — writes to BOTH places in one step.

### CLAUDE.md §11 rule applies

Every entry must name a concrete behavior or contract that fails without the work. "For future flexibility" / "improve testability" / "separation of concerns" = NOT a valid reason.

---

## Resources

### Applicable ADRs

- **[ADR-012](../../.claude/adr/ADR-012-shared-components.md)** (shared component library) — Framework changes stay in `@spaarke/ui-components`; new shared package follows same SSOT rule.
- **[ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md)** (Fluent Design System) — No Fluent v8; native scrollbar retained.
- **[ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md)** (PCF Platform Libraries) — Shared-lib code stays React-16-safe.
- **[ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)** (Spaarke Auth v2) — No auth surface touched (constraint only, no active use).
- **[ADR-038](../../docs/adr/ADR-038-testing-strategy.md)** (Testing Strategy) — MAINTAIN-class tests only; `/test-diet` gate at wrap-up.

### CLAUDE.md Cross-Cutting Rules

- **CLAUDE.md §4** — Mandatory task-execute protocol (see 🚨 section above)
- **CLAUDE.md §5** — Context checkpointing thresholds
- **CLAUDE.md §7** — Test-diet gate at wrap-up (mandatory for `090-*` tasks)
- **CLAUDE.md §8** — TEST-MODIFYING tasks trigger FULL rigor unconditionally
- **CLAUDE.md §10** — BFF Hygiene (NOT triggered — hot-path BFF=N)
- **CLAUDE.md §11** — Component Justification (applies to FR-10 new shared package)

### Architecture Documents

- **[`docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md)** — Framework entry point (§ 6.5 extended by this project)
- **[`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md)** — SpaarkeAi surface context for FR-10
- **[`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md)** — Component consumption patterns

### Guides

- **[`docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`](../../docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md)** — Configuration guide (§ Step 5 gets new fields; § Step 2 references FR-06 templates)
- **[`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md)** — Height-chain § 7.2 replaced by `contentSizing` description; § 12 gets FR-09 dual-deploy warning

### Patterns

- **[`.claude/patterns/ui/embedded-widget-sizing.md`](../../.claude/patterns/ui/embedded-widget-sizing.md)** — **CRITICAL** for FR-01 (existing height-chain pattern)
- **[`.claude/patterns/ui/fluent-v9-component-authoring.md`](../../.claude/patterns/ui/fluent-v9-component-authoring.md)** — For wizard UI additions
- **[`.claude/patterns/ui/fluent-v9-theming.md`](../../.claude/patterns/ui/fluent-v9-theming.md)** — Theme/dark mode integration in `WorkspaceShell`

### Related Projects

- **`spaarke-datagrid-framework-r1`** (predecessor, shipped 2026-06) — the R1 framework this R2 extends
- **`ai-spaarke-ai-workspace-UI-r2`** (parent context, shipped 2026-07-01) — surfaced the 11 gaps this R2 addresses; contains the tactical `maxHeight` hack FR-08 unwinds
- **`spaarkeai-compose-r1`** (active parallel worktree) — merge-order coordination needed for FR-10 shared-package rollout; Compose adds a section-registry entry

### Scripts

- `scripts/Build-AllClientComponents.ps1` — client-lib compilation (FR-10 amends for new shared package)
- `scripts/Build-ViteSolutionsDirect.ps1` — Vite bundles (SpaarkeAi, LegalWorkspace, WorkspaceLayoutWizard)
- `scripts/Deploy-AllDataGridConsumers.ps1` — deploy web resources for grid consumers
- `scripts/Deploy-AllWebResources.ps1` — generic web resource deployment
- `scripts/Build-SpaarkeMaster.ps1` — full-stack master orchestrator

---

*This file should be kept updated throughout project lifecycle. Every significant decision or gotcha gets an entry in "Decisions Made" or "Implementation Notes".*
