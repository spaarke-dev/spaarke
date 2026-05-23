# Spaarke AI Platform Unification R3 - AI Context

> **Purpose**: This file provides context for Claude Code when working on spaarke-ai-platform-unification-r3.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Implementation (planning artifacts complete; tasks pending)
- **Last Updated**: 2026-05-20
- **Current Task**: Not started — see [`current-task.md`](current-task.md)
- **Next Action**: Run `task-create` to decompose [`plan.md`](plan.md) into POML task files, then start task 001 (FR-07 backend spike)

---

## Quick Reference

### Key Files

- [`spec.md`](spec.md) — AI-optimized design specification (25 FRs, 12 NFRs, 12 ADRs)
- [`design.md`](design.md) — Original human design document for Moment 1: Arrival
- [`README.md`](README.md) — Project overview and graduation criteria
- [`plan.md`](plan.md) — Implementation plan + Discovered Resources (ADRs, skills, patterns, guides)
- [`current-task.md`](current-task.md) — **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Task tracker (will be created by `task-create`)

### Project Metadata

- **Project Name**: spaarke-ai-platform-unification-r3
- **Type**: React 19 full-page Code Page refactor (NOT PCF) — SpaarkeAi + shared component primitives + 1 conditional BFF endpoint extension
- **Complexity**: Medium-High — 25 FRs / 12 NFRs / 12 applicable ADRs / 3 load-bearing ADRs (012, 021, 028)
- **Branch**: `work/spaarke-ai-platform-unification-r3` (worktree)
- **Predecessor**: [`projects/spaarke-ai-platform-unification-r2/`](../spaarke-ai-platform-unification-r2/) — shipped 86/86 (commit `b40dc3e6`)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check [`current-task.md`](current-task.md)** for active work state (especially after compaction / new session)
3. **Reference [`spec.md`](spec.md)** for design decisions, requirements, and acceptance criteria
4. **Load the relevant task file** from [`tasks/`](tasks/) based on current work
5. **Apply load-bearing ADRs** (012, 021, 028) on every code-touching task — these are non-negotiable
6. **Apply other applicable ADRs** as the task tags trigger them (loaded automatically via `adr-aware`)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md).

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke task-execute skill:

| User Says | Required Action |
|-----------|-----------------|
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
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in current-task.md
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5
- ✅ Progress is recoverable after compaction

**Bypassing this skill leads to**:
- ❌ Missing ADR constraints
- ❌ No checkpointing — lost progress after compaction
- ❌ Skipped quality gates

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute:
- Send one message with multiple Skill tool invocations
- Each invocation calls task-execute with a different task file
- Example: Tasks 020, 021, 022 in parallel → Three separate task-execute calls in one message
- **Hard cap**: 6 concurrent agents per wave (skill enforced — API overload guard)

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

### 🚨 MUST: Multi-File Work Decomposition

**For tasks modifying 4+ files, Claude Code MUST:**

1. **Decompose into dependency graph**:
   - Group files by module/component
   - Identify which changes depend on others
   - Separate parallel-safe work from sequential work

2. **Delegate to subagents in parallel where safe**:
   - Use Task tool with `subagent_type="general-purpose"`
   - Send ONE message with MULTIPLE Task tool calls for independent work
   - Each subagent handles one module/component

3. **Parallelize when**: files in different modules; no shared interfaces; no imports between files.

4. **Serialize when**: tight coupling; one file must exist before another consumes it; sequential logic required; **any modification touches `.claude/` paths** (per [`CLAUDE.md` §3 Sub-Agent Write Boundary](../../CLAUDE.md)).

**Example**: Phase B has 7 tasks, several touch SprkChat.tsx — those must serialize. Phase A foundations (PaneHeader, MAX_TABS, telemetry) are independent → parallelize.

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

**Load-bearing ADRs** (apply on every code-touching task):

- **ADR-012**: Shared components live in `@spaarke/ui-components`. `<PaneHeader>` and (if needed) `ActionCard` are lifted here. All context-agnostic components → shared lib, not solution-local.
- **ADR-021**: Fluent v9 tokens only. **NO hex literals**, **NO `rgba(...)` literals**, **NO Fluent v8**. Dark-mode safe by construction.
- **ADR-028**: All BFF calls go through `authenticatedFetch` from `@spaarke/auth`. **NO token snapshots in props or state.** Per-request `getAccessToken()`. Function-based contract per INV-1..INV-8.

**Other applicable ADRs**:

- ADR-006: PCF over web resources — confirms SpaarkeAi/LegalWorkspace are full-page custom pages, not PCFs (informs deployment skill choice: `code-page-deploy`, NOT `pcf-deploy`)
- ADR-008: Endpoint filters — new/changed BFF endpoint inherits auth/rate-limit/validation pipeline (Phase E if needed)
- ADR-010: DI minimalism — no new feature modules; register in existing ones
- ADR-013: AI architecture — Daily Briefing and chat-attachment paths extend BFF in-process; no new service
- ADR-014: AI caching — Daily Briefing response cached per-user with ~5 min TTL
- ADR-016: AI rate limits — Daily Briefing subject to existing rate-limit filters; 429 graceful UI
- ADR-022: React 19 — no React 16 fallbacks; React 19 only
- ADR-025: Fluent v9 icon library — `ChatRegular`, `AppsListRegular`, `DocumentRegular`, `HistoryRegular`, `AttachRegular`, `AddRegular`, `DismissRegular`; no new SVG web resources
- ADR-026: Full-page custom pages — Vite + React 19 bundled; singlefile output

**Spec MUST / MUST NOT rules**:

- ❌ **MUST NOT** create Dataverse Document entities from Assistant `+` button (in-memory message context only)
- ❌ **MUST NOT** add new BFF DI feature modules
- ❌ **MUST NOT** introduce React 16 fallbacks
- ❌ **MUST NOT** add happy-path App Insights events (error-only telemetry per OC-09)
- ✅ **MUST** keep standalone LegalWorkspace unchanged (FR-25, NFR-10)

**Repo conventions**:

- Stay on `work/spaarke-ai-platform-unification-r3` worktree branch (project-pipeline default of `feature/...` overridden)
- Use `npm install --legacy-peer-deps --no-audit --no-fund` for Vite solutions (NOT `npm ci`)
- PCF builds use `npm run build:prod` — but this project deploys CODE PAGES, use `scripts/Build-ViteSolutionsDirect.ps1` + `scripts/Deploy-SpaarkeAi.ps1`

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

- **2026-05-20**: Project initialized via `/project-pipeline`. Stayed on existing `work/` worktree branch instead of creating new `feature/` branch (repo convention override). Discovered 12 ADRs (3 load-bearing), 11 applicable skills, 4 key patterns, 5 key guides. — `spaarke-dev`

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

**From R2 lessons-learned that apply here**:

- **PaneEventBus pattern**: Multi-subscriber, type-safe channels (`workspace`, `context`, `conversation`, `safety`). Wizard widgets route through `workspace` channel via `widget_load` event. Reuse existing — do not invent new channels.
- **Widget registry pattern**: `WorkspaceWidgetRegistry` and `ContextWidgetRegistry` are SEPARATE registries with a shared `WorkspaceWidget` interface. New wizard widget wrappers (FR-19) go in `WorkspaceWidgetRegistry`; `GetStartedCardsWidget` goes in `ContextWidgetRegistry`.
- **Data-refreshed restore (D-08)**: Re-fetch fresh data on session restore vs replaying stale snapshots. Apply same logic for Daily Briefing — restore widget state by re-calling endpoint with current TTL cache, not by serializing the response.
- **Single-LLM-call invariant (D-01)**: Always exactly one LLM call per user turn. Preserve when wiring `attachments[]` — they go into the single chat message, do not trigger separate extraction LLM calls.
- **Write-through Cosmos persistence (D-06)**: Persist on every state change (~10–20 ms latency). Tab list updates (open / close / reorder) should write through immediately for NFR-09 compliance.

**Spec-derived gotchas**:

- FR-07 spike outcome is BLOCKING for task 026 (attachment payload wiring) and gates Phase E entirely
- `Xrm.Navigation.navigateTo` is host-only — Vite dev environment needs feature-detect + placeholder (FR-20, A-2)
- `MAX_WORKSPACE_TABS = 8` is configurable; Home tab is exempt from cap (FR-13)
- Standalone LegalWorkspace MUST continue to function identically (FR-25, NFR-10) — `templateFilter` prop defaults to no-filter to preserve this

---

## Resources

### Applicable ADRs (load via adr-aware on each task)

**Load-bearing (always)**: ADR-012, ADR-021, ADR-028

**Task-specific (auto-loaded via tags)**:
- API endpoint work → ADR-008, ADR-013
- AI features → ADR-013, ADR-014, ADR-016
- New BFF service → ADR-010
- React/Vite Code Page → ADR-022, ADR-026
- Icons → ADR-025
- PCF context (informational only) → ADR-006

### Related Projects

- [`projects/spaarke-ai-platform-unification-r2/`](../spaarke-ai-platform-unification-r2/) — predecessor (86/86 shipped, `b40dc3e6`). [`notes/lessons-learned.md`](../spaarke-ai-platform-unification-r2/notes/lessons-learned.md) is required reading.
- [`projects/spaarke-auth-v2-and-hardening/`](../spaarke-auth-v2-and-hardening/) — auth v2 foundation (`e649f244`). Provides `authenticatedFetch` / `useAuth()` consumed throughout this project.

### External Documentation

- Fluent v9: https://react.fluentui.dev/
- React 19: https://react.dev/blog/2024/12/05/react-19
- PDF.js: https://github.com/mozilla/pdf.js (FR-07 client-side PDF text extraction)
- Mammoth: https://github.com/mwilliamson/mammoth.js (FR-07 client-side DOCX text extraction)
- Xrm.Navigation.navigateTo: https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto (FR-20)

### Skills

See [`plan.md` §2 Discovered Resources](plan.md) for the canonical list. Key entries:

- [`task-execute`](../../.claude/skills/task-execute/SKILL.md) — mandatory protocol for every task
- [`code-page-deploy`](../../.claude/skills/code-page-deploy/SKILL.md) — build + deploy SpaarkeAi
- [`ui-test`](../../.claude/skills/ui-test/SKILL.md) — Phase G smoke testing
- [`code-review`](../../.claude/skills/code-review/SKILL.md) — quality gate
- [`adr-check`](../../.claude/skills/adr-check/SKILL.md) — ADR compliance check
- [`merge-to-master`](../../.claude/skills/merge-to-master/SKILL.md) — final merge with safety checks

### Patterns

- [`.claude/patterns/auth/spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md) — auth invariants
- [`.claude/patterns/auth/bff-url-normalization.md`](../../.claude/patterns/auth/bff-url-normalization.md) — BFF URL building
- [`.claude/patterns/webresource/full-page-custom-page.md`](../../.claude/patterns/webresource/full-page-custom-page.md) — Vite + React 19 + Fluent v9 setup

### Guides

- [`docs/guides/SHARED-UI-COMPONENTS-GUIDE.md`](../../docs/guides/SHARED-UI-COMPONENTS-GUIDE.md) — `@spaarke/ui-components` consumption
- [`docs/guides/auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md) — auth v2 operator runbook
- [`docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md`](../../docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md) — Code Page smoke verification

### Workspace architecture (current — refreshed through Round 13)

Task 113 (2026-05-22) produced the foundational reference docs for the SpaarkeAi workspace pipeline; task 123 (2026-05-22) refreshed them to cover Rounds 10–13 — Calendar widget (task 115) + `@spaarke/events-components` shared lib (task 114) + ThreePaneLayout pane-width fracs (task 117) + all-panes-collapsed overlay (task 119) + Calendar polish history (tasks 116/118/120/121/122). Load these before adding or modifying any workspace-pipeline code:

- [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — end-to-end cold-load → widget render pipeline, storage contract (incl. `spaarke:calendar:collapsed`), BFF surface, the **6 system layouts** shipped today (incl. Calendar), pane-width precedence chain, all-panes-collapsed empty state UX.
- [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md) — inventory of `@spaarke/ui-components`, `@spaarke/ai-widgets`, `@spaarke/auth`, `@spaarke/legal-workspace`, **`@spaarke/events-components`** (task 114), plus solution-local components. Includes the PaneEventBus contract, ThreePaneLayout's new `defaultLeftWidthFrac` / `defaultRightWidthFrac` props + `resetToFracDefaults()` hook method, and CalendarSection's controlled-mode props.
- [`docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`](../../docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) — honest reuse + extendability audit. **§2A Calendar is the proven canonical "shared-lib widget + thin LW shim" pattern**; the audit's §2 prediction is revised — incremental hoisting of the 5 original sections is now deferred (Calendar's existence reduces urgency). Calls out the dual `useWorkspaceLayouts`, `WorkspaceLayoutWidget` hard-wired to `LegalWorkspaceApp`, Xrm.WebApi vs BFF undocumented decision criteria (highest-priority doc-only item, ~2h next round), and the informal embedded-mode contract.
- [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — step-by-step tutorial. Decision tree now includes **Pattern D — SHARED-LIB WIDGET + THIN LW SHIM** (recommended default for new widgets); Calendar is the worked example end-to-end (tasks 114 + 115 + polish history 116/118/120/121/122). New common pitfalls added: timezone-symmetric date keys (task 120), filter-state conflicts with passive indicators (task 122), field-priority chains for date derivation (task 121).

---

*This file should be kept updated throughout project lifecycle. Decisions Made and Implementation Notes sections grow as tasks execute.*
