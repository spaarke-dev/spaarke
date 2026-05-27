# Spaarke Matter UI Enhancement R1 - AI Context

> **Purpose**: This file provides context for Claude Code when working on `spaarke-matter-ui-enhancement-r1`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Phase 0 — Foundation
- **Last Updated**: 2026-05-27
- **Current Task**: Not started
- **Next Action**: Run `task-execute` on `tasks/001-chart-def-regression-inventory.poml`

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — Original AI-optimized design specification (Rev 6, recon-validated) — **PERMANENT REFERENCE**
- [`design.md`](design.md) — Human design document (source for spec.md)
- [`README.md`](README.md) — Project overview and graduation criteria
- [`plan.md`](plan.md) — Implementation plan with WBS, phase breakdown, parallel groups
- [`current-task.md`](current-task.md) — **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Task tracker + dependency graph + parallel-execution waves
- [`screenshots/`](screenshots/) — 5 prototype screenshots (Variant H reference)

### Project Metadata
- **Project Name**: spaarke-matter-ui-enhancement-r1
- **Type**: PCF + BFF + Dataverse (multi-surface)
- **Complexity**: Medium-High (34 tasks across 9 phases, multiple parallel groups, binding NFR-05 regression discipline)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check [`current-task.md`](current-task.md)** for active work state (especially after compaction / new session)
3. **Reference [`spec.md`](spec.md)** for FR-level requirements, owner clarifications, technical constraints, and acceptance criteria — every FR in spec maps to one or more tasks
4. **Load the relevant task file** from [`tasks/`](tasks/) based on current work
5. **Apply ADRs** relevant to the surface (PCF / BFF / Dataverse) — loaded automatically via `adr-aware` per the task's `<constraints>` section
6. **Visual contract**: when implementing UI, check [`screenshots/`](screenshots/) AND the prototype at `c:\code_files\spaarke-prototype\projects\2026-05-matter-form-redesign\` (Variant H)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md).

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke `task-execute` via the Skill tool:

| User Says | Required Action |
|---|---|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Implementation**: Invoke `Skill` with `skill="task-execute"` and the task file path.

### Why This Matters

The `task-execute` skill ensures:
- ✅ Knowledge files loaded (ADRs, constraints, patterns)
- ✅ Context tracked in `current-task.md`
- ✅ Proactive checkpointing every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5 for FULL rigor
- ✅ `/fluent-v9-component` invocation at Step 0.5 for every UI task (design.md §6.0 binding)
- ✅ Progress recoverable after compaction

**Bypassing leads to**: missed ADR constraints, skipped Fluent v9 patterns, no portal-gotcha checks, no checkpointing, lost progress after compaction, skipped quality gates.

### Parallel Task Execution

Tasks marked `<parallel-safe>true</parallel-safe>` in the same `<parallel-group>` can run simultaneously:
- Send ONE message with MULTIPLE Skill tool invocations
- Each invocation calls `task-execute` with a different task file
- Example: Tasks 020, 023, 024 in parallel → three separate `task-execute` calls in one message

Max concurrency: 6 agents per wave. Build verification (`dotnet build` / `npm run build`) runs between waves.

See [`task-execute` SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

### 🚨 MUST: Multi-File Work Decomposition

For tasks modifying 4+ files, Claude Code MUST decompose into a dependency graph and delegate to subagents in parallel where files are independent (no imports, no shared state). Serialize when files have tight coupling (shared interfaces, sequential logic). See `task-execute` Step 8.0.

---

## 🚨 MANDATORY: `/fluent-v9-component` Skill Invocation (Project-Level Binding)

**Per [spec.md §Skill invocation map](spec.md#skill-invocation-map-per-fr) + [design.md §6.0]** — every task in this project that authors or modifies a Fluent v9 React component MUST invoke `/fluent-v9-component` at task Step 0.5 BEFORE reading affected files. Applies to:

- FR-DOC-01..06 (Documents PCF UI tasks 040..045)
- FR-VH-01..05 (Visual Host renderer extensions tasks 020..024)
- FR-SC-01..02 (shared component tasks 010..011)

**Why**: Fluent v9 has too many subtle gotchas (portals, slots, theming, React version boundaries) for the skill to be optional. `code-review` skill enforces this on UI PRs — PRs that skipped `/fluent-v9-component` are blocked from merge.

**Cite Fluent v9 patterns informed by the skill** (e.g., `fluent-v9-portal-gotcha.md`, `fluent-v9-theming.md`) in the PR description.

---

## Key Technical Constraints

Extracted from [spec.md §Technical Constraints](spec.md#technical-constraints) and §MUST Rules — these are binding for every task:

### Must DO

- ✅ MUST invoke `/fluent-v9-component` at task start for every Fluent v9 React task (FR-DOC-01..06, FR-VH-01..05, FR-SC-01..02) — design.md §6.0 binding
- ✅ MUST use existing Visual Host visual types only (Donut / MetricCard / HorizontalStackedBar / DueDateCard) — no new visual types (spec §6.4.0)
- ✅ MUST extend Visual Host via Custom Options keys + minimal renderer code; extensions MUST be generic
- ✅ MUST preserve backward compatibility for every existing `sprk_chartdefinition` record (NFR-05 — binding)
- ✅ MUST document every new Custom Options key in [VISUALHOST-ARCHITECTURE.md](../../docs/architecture/VISUALHOST-ARCHITECTURE.md) + [VISUALHOST-SETUP-GUIDE.md](../../docs/guides/VISUALHOST-SETUP-GUIDE.md) in the same PR as the code change (FR-DOC-09)
- ✅ MUST include a "Why not Custom Options alone?" paragraph in every PR that adds Visual Host renderer code
- ✅ MUST use `@spaarke/auth` `authenticatedFetch` for all BFF calls (no manual bearer headers)
- ✅ MUST use Fluent v9 semantic tokens only (no hex/rgb — NFR-04)
- ✅ MUST preserve `AssociatedOnly` auto-search behavior at `SemanticSearchControl.tsx:359-375` verbatim during filter relocation (FR-DOC-06)
- ✅ MUST use `npm run build:prod` (NOT `npm run build`) for PCF production builds (per [`.claude/FAILURE-MODES.md#AP-1`](../../.claude/FAILURE-MODES.md#ap-1-skill-prescribes-x-but-x-is-wrong))
- ✅ MUST cite [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) Placement Justification for FR-BFF-01 + FR-BFF-02 in PR description

### Must NOT

- ❌ MUST NOT create new visual types in Visual Host (e.g., `MatterHealthComposite`, `ScorecardDonut`)
- ❌ MUST NOT build parallel chart components in `@spaarke/ui-components` (e.g., `HealthDonut`, `KpiCard`) — Visual Host owns chart rendering
- ❌ MUST NOT build a `MatterPerformancePane` container PCF — form section handles stacking
- ❌ MUST NOT add Matter-specific code paths inside generic Visual Host components
- ❌ MUST NOT introduce new schema fields on `sprk_matter` — all rollups already exist
- ❌ MUST NOT add new BFF endpoints for the Performance pane — Visual Host uses Dataverse WebAPI / FetchXML directly
- ❌ MUST NOT use hover-popover for document preview (`FilePreviewDialog` is the only preview path)
- ❌ MUST NOT use `console.log` for production telemetry — App Insights only (FR-DOC-07, FR-TEL-01)
- ❌ MUST NOT skip `/fluent-v9-component` invocation "because the change is small"
- ❌ MUST NOT mix Fluent v8 imports (`@fluentui/react`) with v9 (`@fluentui/react-components`) in any touched file
- ❌ MUST NOT use React 18+ exclusive features in shared components consumed by PCFs (React 16/17 per ADR-022)
- ❌ MUST NOT add new NuGet packages to BFF for FR-BFF-02 (use BCL `System.IO.Compression.ZipArchive`)

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: YYYY-MM-DD — Decision — Reason -->

- **2026-05-27** — Stay on branch `work/spaarke-matter-ui-enhancement-r1` (worktree convention). Reason: matches repo worktree pattern; no need to create `feature/*`.
- **2026-05-27** — ADR-019 labeling drift: spec.md line 332 says "ADR-019 — SPE access" but actual ADR-019 is ProblemDetails. Both ADR-007 (SpeFileStore facade) and ADR-019 (ProblemDetails) apply to FR-BFF-02. Tasks 050/051 reference both correctly.

---

## Implementation Notes

<!-- Gotchas, workarounds, important learnings from implementation -->

- **NFR-05 regression discipline**: every chart-def in production must render unchanged after Phase 2 ships. Task 001 enumerates the baseline; task 025 runs the regression smoke before merge. This is a binding constraint — failure is a blocker, not a warning.
- **`sprk_event` field-name verification**: spec.md Assumption #1 — `sprk_regardingmatter`, `sprk_finalduedate`, `sprk_eventstatus`. Task 003 confirms via MCP `describe_table` BEFORE Phase 3 chart defs depend on them.
- **Visual contract reference**: prototype at `c:\code_files\spaarke-prototype\projects\2026-05-matter-form-redesign\` (Variant H). Screenshots also in [`./screenshots/`](./screenshots/).
- **Existing in-production chart defs to regression-test against**: Matter KPI Scorecard, Matter Financial Metrics Scorecard, and any deprecated `matterMainCards.ts` configs still referenced (task 001 enumerates the full list).

---

## Resources

### Applicable ADRs (10)

| ADR | Title | Why it applies |
|---|---|---|
| [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) | PCF Over Web Resources | This project uses PCF as the binding surface |
| [ADR-007](../../.claude/adr/ADR-007-spefilestore.md) | SpeFileStore facade | FR-BFF-02 bulk-download accesses SPE via this facade only |
| [ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md) | Endpoint filters for auth | FR-BFF-02 uses endpoint filter (modeled on `SemanticSearchAuthorizationFilter`) |
| [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) | DI Minimalism | FR-BFF-02 must not push DI registrations past ≤15 cap |
| [ADR-012](../../.claude/adr/ADR-012-shared-components.md) | Shared component library | FR-SC-01, FR-SC-02 are added to `@spaarke/ui-components` |
| [ADR-019](../../.claude/adr/ADR-019-problemdetails.md) | ProblemDetails | FR-BFF-02 error responses emit RFC 7807 |
| [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) | Fluent UI v9 Design System | Binding for all UI; dark mode required; zero hardcoded hex/rgb |
| [ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md) | PCF Platform Libraries | React 16/17 boundary — no React 18+ exclusive features |
| [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) | Spaarke Auth v2 | Both PCFs use `@spaarke/auth` unchanged; FR-BFF-02 follows function-based contract + managed-identity pattern |
| [ADR-029](../../.claude/adr/ADR-029-bff-publish-hygiene.md) | BFF publish hygiene | No new NuGet packages; no transitive CVE; publish-size impact verified |

### Related Projects

- `projects/x-matter-performance-KPI-r1/` (archived) — earlier Matter performance work; reference for chart-def + Visual Host patterns
- `projects/x-ui-dialog-shell-standardization/` (archived) — Fluent v9 dialog patterns

### External Documentation

- [Fluent UI v9 Components](https://react.fluentui.dev/) — `@fluentui/react-components` reference
- [Application Insights JS SDK](https://learn.microsoft.com/en-us/azure/azure-monitor/app/javascript) — `@microsoft/applicationinsights-web`
- [Visual Host Architecture](../../docs/architecture/VISUALHOST-ARCHITECTURE.md) — internal — UPDATE per FR-DOC-09
- [Visual Host Setup Guide](../../docs/guides/VISUALHOST-SETUP-GUIDE.md) — internal — UPDATE per FR-DOC-09

---

*This file should be kept updated throughout the project lifecycle. Notably: update Decisions Made + Implementation Notes as tasks complete.*
