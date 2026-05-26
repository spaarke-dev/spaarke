# Spaarke AI Platform Unification R4 - AI Context

> **Purpose**: This file provides context for Claude Code when working on spaarke-ai-platform-unification-r4.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Implementation (planning artifacts complete; tasks pending)
- **Last Updated**: 2026-05-26
- **Current Task**: Not started — see [`current-task.md`](current-task.md)
- **Next Action**: Run `task-create` to decompose [`plan.md`](plan.md) into POML task files, then start Phase 0 (E-1 + F-1)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized specification (14 FRs / 9 NFRs / 7 DRs / 2 PRs)
- [`plan.md`](plan.md) — Implementation plan + Discovered Resources
- [`plan.original.md`](plan.original.md) — Operator-authored WBS with full risk register (load for full per-task effort breakdown)
- [`backlog.md`](backlog.md) — Per-item analysis + 2026-05-25 IN/DEFER scoping decisions
- [`README.md`](README.md) — Project overview and graduation criteria
- [`README.original.md`](README.original.md) — Operator-authored README (pre-pipeline)
- [`current-task.md`](current-task.md) — **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Task tracker (will be created by `task-create`)

### Project Metadata
- **Project Name**: spaarke-ai-platform-unification-r4
- **Type**: Mixed — 14 code refactors (React 19 Code Pages + .NET 8 BFF + shared TS libs) + 7 governance docs + 9 build hygiene / NFR items
- **Complexity**: Medium-High — 34 items / 11 applicable ADRs / 4 load-bearing (012, 021, 022, 028)
- **Branch**: `work/spaarke-ai-platform-unification-r4` (worktree)
- **Predecessor**: [`projects/spaarke-ai-platform-unification-r3/`](../spaarke-ai-platform-unification-r3/) — shipped at master `3813af32` (task 140)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check [`current-task.md`](current-task.md)** for active work state (especially after compaction / new session)
3. **Reference [`spec.md`](spec.md)** for FR/NFR/DR/PR definitions and acceptance criteria
4. **Reference [`plan.original.md`](plan.original.md)** for full operator-authored WBS, per-task effort, risk register, and acceptance criteria — it remains authoritative alongside the pipeline-generated `plan.md`
5. **Load the relevant task file** from [`tasks/`](tasks/) based on current work
6. **Apply load-bearing ADRs** (ADR-012, ADR-021, ADR-022, ADR-028) on every code-touching task — non-negotiable
7. **Apply CLAUDE.md §10 BFF Hygiene** on every BFF-touching task (A-4, B-4, B-5, F-1, F-2, F-3) — Placement Justification + publish-size verification REQUIRED
8. **Apply other applicable ADRs** as task tags trigger them (loaded automatically via `adr-aware`)

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
- ❌ Missed CLAUDE.md §10 Placement Justification on BFF tasks

### Parallel Task Execution

R4 has known parallel opportunities (see `plan.md` §3 + TASK-INDEX.md once generated):
- Phase 1 Wave 1 (parallel): W-1, A-2, C-1, F-3
- Phase 4: W-3 + W-6 parallel; W-4 + W-5 parallel after W-1
- Phase 5 Wave 1: A-4, C-3, B-4, B-5
- Phase 6: all 8 items mostly independent → 1-2 waves of 4-6

When dispatching parallel tasks:
- Send one message with multiple Skill tool invocations (one per task)
- Each invocation calls task-execute with a different task file
- **Hard cap**: 6 concurrent agents per wave (skill enforced — API overload guard)
- **`.claude/` paths MUST be sequential (main session only)**: A-2 (`.claude/adr/ADR-030`, `ADR-031`), D-2 (ADR-031 amendment), F-3 (`.claude/constraints/azure-deployment.md`)
- **Build verification between waves is MANDATORY**: `dotnet build src/server/api/Sprk.Bff.Api/` for BFF tasks; `npm run build` per package for client tasks

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

### 🚨 MUST: Multi-File Work Decomposition

For tasks modifying 4+ files, Claude Code MUST decompose into a dependency graph (group by module, identify what depends on what), delegate to subagents in parallel where safe, and serialize when files have tight coupling.

R4-specific examples:
- **C-3 (hook consolidation)** — touches `@spaarke/ai-widgets` + 2 hook copies + 2 consumer adapters. Serialize: lib first, then both consumers in parallel.
- **B-6/B-7/B-8 (events-components)** — touch the same shared lib package. Sequence them OR clean modular split (different files).
- **A-4 (attachment cap)** — touches client hook + server endpoint + policy doc. Three separate-domain files; can parallelize after API contract agreed.

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

**Load-bearing ADRs** (apply on every code-touching task):
- **ADR-012**: Shared components live in `@spaarke/*` lib family; context-agnostic. C-3, C-4, B-6, B-7 directly invoke this constraint.
- **ADR-021**: Fluent v9 tokens only — NO hex, NO `rgba()`, NO Fluent v8. All UI tasks (W-3, W-4, W-5, B-3, B-6, B-7, B-8) MUST comply.
- **ADR-022**: React 19 only for Code Pages — no React 16 fallbacks. W-3, W-4, W-5 + all SpaarkeAi/LegalWorkspace touch points.
- **ADR-028**: Function-based auth via `@spaarke/auth` `authenticatedFetch`. NO token snapshots in props/state. A-4, A-5, C-3 directly inherit.

**Other applicable ADRs**:
- **ADR-001** (Minimal API) — B-4, B-5 inherit endpoint patterns
- **ADR-008** (Endpoint filters) — B-5 PATCH inherits filter pipeline
- **ADR-010** (DI minimalism) — A-4, C-3 register in existing feature modules
- **ADR-013** (AI architecture, refined 2026-05-20) — F-1, F-2 placement justification base
- **ADR-030** (PaneEventBus) — NEW per A-2; W-4 and W-5 must conform
- **ADR-031** (Stage lifecycle + heavy library handling) — NEW per A-2 + amended per D-2
- **ADR-029** (BFF publish hygiene) — F-3 codifies as workflow rule; NFR-01 enforces

**Spec MUST / MUST NOT rules**:
- ✅ **MUST** follow CLAUDE.md §10 Placement Justification for every BFF addition (A-4, B-4, B-5, F-1, F-2, F-3)
- ✅ **MUST** verify publish size ≤60 MB compressed on every BFF-touching task (NFR-01)
- ✅ **MUST** use `authenticatedFetch` from `@spaarke/auth` — no token snapshots (ADR-028)
- ✅ **MUST** keep PaneEventBus channels typed — no `any` payloads (ADR-030)
- ✅ **MUST** verify A-5 BEFORE remediating (verify-then-fix protocol)
- ❌ **MUST NOT** introduce new direct `IOpenAiClient` / `IPlaybookService` injections outside `Services/Ai/` (F-2 facade rule)
- ❌ **MUST NOT** regenerate hardcoded section catalogs (W-3 fix establishes `SECTION_REGISTRY` as single source of truth)
- ❌ **MUST NOT** deploy standalone `sprk_corporateworkspace` web resource (W-6 retirement)
- ❌ **MUST NOT** treat R3 FR-25 / NFR-10 as forward constraints — superseded by W-6
- ❌ **MUST NOT** add new BFF DI feature modules (ADR-010); extend existing modules
- ❌ **MUST NOT** introduce React 16 fallbacks (ADR-022); React 19 only for Code Pages

**Repo conventions**:
- Stay on `work/spaarke-ai-platform-unification-r4` worktree branch (project-pipeline default of `feature/...` was overridden — R3 precedent)
- Use `npm install --legacy-peer-deps --no-audit --no-fund` for Vite solutions (NOT `npm ci`)
- BFF builds: `dotnet build src/server/api/Sprk.Bff.Api/`
- Vite solution builds: `scripts/Build-ViteSolutionsDirect.ps1` or `npm run build` per package
- Use `task-execute` skill for ALL task work (auto-detection of trigger phrases)

---

## Decisions Made

- **2026-05-25**: R4 scope finalized — 34 IN items across 8 phases (operator-authored `backlog.md` Scoping Decisions table). Two-wrapper architecture adopted; LegalWorkspace standalone retired (W-6). — `spaarke-dev`
- **2026-05-26**: spec.md generated from plan.md + backlog.md via `/design-to-spec`. Structure: FR/NFR/DR/PR four-category split (chosen vs strict R3-style FR+NFR mirror). — `Claude / spaarke-dev`
- **2026-05-26**: `/project-pipeline` run with selective scope: `project-setup` ran with backed-up originals (README.original.md, plan.original.md); branch creation skipped (already on work/* worktree); task execution NOT auto-started (operator hand-off). — `Claude / spaarke-dev`

---

## Implementation Notes

**From R3 lessons-learned that apply here**:
- **PaneEventBus pattern**: Multi-subscriber, type-safe channels. W-4/W-5 use existing `workspace` channel + `widget_load` event — do not invent new channels. ADR-030 codifies.
- **Calendar widget Pattern D** (R3 task 115): Canonical "shared-lib widget + thin LW shim" reference for W-4/W-5 mount-source demos and for the dual-use docs in W-1/W-2.
- **Data-refreshed restore (D-08, R2)**: Re-fetch fresh data on session restore vs replaying stale snapshots. Apply same logic to A-5 tab persistence remediation.
- **Single-LLM-call invariant (D-01, R2)**: Always exactly one LLM call per user turn. A-4 must preserve this when wiring 25 MB attachments — go into the single chat message, no separate extraction calls.
- **Write-through Cosmos persistence (D-06, R2)**: Persist on every state change. A-5 may use this if remediation lands in BFF-side storage.

**R4-specific gotchas**:
- **A-5 verify-first is non-negotiable** — Original R3 verification said tabs persist; user feedback contradicts. Do NOT skip the verification spike (~2h) to save time.
- **W-3 catalog drift is a real bug** discovered during scoping. The TODO comment in `WorkspaceLayoutWizard/src/App.tsx` says "In a production build this would be fetched from GET /api/workspace/sections" — it was never wired up. Fix by reading `SECTION_REGISTRY` directly.
- **C-3 hook consolidation risk**: Both LegalWorkspace AND SpaarkeAi currently have their own `useWorkspaceLayouts.ts`. Consolidation must not break either consumer. Plan a dual-surface regression test (manual + automated where possible) BEFORE merging C-3.
- **B-2 tsc rootDir fix** may surface previously-hidden type errors across multiple packages. Budget may stretch from ~3h → ~6h. Plan for it.
- **B-5 API design** (PUT+If-Match vs PATCH+JSON-Patch) is unresolved per spec UQ — decide at task start based on client-side complexity tradeoff. Either is acceptable.
- **CalendarSidePane vs embedded Calendar parity (B-6)** — visual + behavioral. Use the same regression-test approach R3 used for Calendar widget (task 115 lessons).
- **W-6 LW retirement requires consumer audit** before removing the deploy. Audit `corporateworkspace` references in Dataverse `Default Solution > Forms > grep` BEFORE deleting the deploy step.

**Resource discovery notes** (from pipeline Step 2):
- BFF workspace endpoint exact filename (`WorkspaceLayoutsEndpoint.cs`) and DTO (`WorkspaceLayoutDto.cs`) paths to confirm at task start for B-4/B-5
- SpaarkeAi code page exact path within `src/client/code-pages/` to confirm during first SpaarkeAi-touching task (likely W-3 or W-4)

---

## Resources

### Applicable ADRs (load via adr-aware on each task)

**Load-bearing (always)**: ADR-012, ADR-021, ADR-022, ADR-028
**NEW in R4**: ADR-030 (PaneEventBus) + ADR-031 (stage lifecycle + heavy library handling) — created by A-2

**Task-specific (auto-loaded via tags)**:
- BFF endpoint work → ADR-001, ADR-008, ADR-013, ADR-029
- AI features / facade audit → ADR-013 (refined), ADR-029
- New BFF service → ADR-010
- React/Vite Code Page → ADR-022
- PaneEventBus dispatch → ADR-030 (NEW)
- Stage lifecycle / heavy lib handling → ADR-031 (NEW)

### Related Projects

- [`projects/spaarke-ai-platform-unification-r3/`](../spaarke-ai-platform-unification-r3/) — predecessor (shipped at `3813af32`). [`notes/lessons-learned.md`](../spaarke-ai-platform-unification-r3/notes/lessons-learned.md) will be written by E-1 and is required reading after Phase 0.
- [`projects/spaarke-ai-platform-unification-r2/`](../spaarke-ai-platform-unification-r2/) — PaneEventBus pattern + D-08 data-refreshed restore origin. `notes/lessons-learned.md` recommended reading.
- [`projects/spaarke-auth-v2-and-hardening/`](../spaarke-auth-v2-and-hardening/) — Auth v2 foundation (provides `authenticatedFetch` / `useAuth()` consumed throughout R4).
- [`projects/sdap-bff-api-remediation-fix/`](../sdap-bff-api-remediation-fix/) — Recent BFF remediation; F-2 audit baseline traces here.

### Skills

See [`plan.md` §2 Discovered Resources](plan.md#discovered-resources) for the full list. Key entries:
- [`task-execute`](../../.claude/skills/task-execute/SKILL.md) — Mandatory protocol for every task
- [`code-review`](../../.claude/skills/code-review/SKILL.md) — Quality gate; enforces §10 BFF Hygiene
- [`adr-check`](../../.claude/skills/adr-check/SKILL.md) — ADR compliance check
- [`code-page-deploy`](../../.claude/skills/code-page-deploy/SKILL.md) — SpaarkeAi + LegalWorkspace deploys
- [`bff-deploy`](../../.claude/skills/bff-deploy/SKILL.md) — BFF deploys for A-4, B-4, B-5
- [`merge-to-master`](../../.claude/skills/merge-to-master/SKILL.md) — Final merge
- [`repo-cleanup`](../../.claude/skills/repo-cleanup/SKILL.md) — Phase 0 (E-1) + Phase 7 (R4 wrap-up)
- [`context-handoff`](../../.claude/skills/context-handoff/SKILL.md) — Proactive checkpointing per CLAUDE.md §5
- [`ui-test`](../../.claude/skills/ui-test/SKILL.md) — UI smoke tests for W-3/W-4/W-5/B-6/B-10
- [`doc-drift-audit`](../../.claude/skills/doc-drift-audit/SKILL.md) — Phase 7 wrap-up drift check

### Patterns

- [`.claude/patterns/api/endpoint-definition.md`](../../.claude/patterns/api/endpoint-definition.md) — BFF endpoint reference (A-4, B-4, B-5)
- [`.claude/patterns/auth/spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md) — ADR-028 invariants
- [`.claude/patterns/auth/bff-url-normalization.md`](../../.claude/patterns/auth/bff-url-normalization.md) — BFF URL building
- [`.claude/patterns/webresource/full-page-custom-page.md`](../../.claude/patterns/webresource/full-page-custom-page.md) — Code Page deploy

### Guides

- [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — Will be REWRITTEN in W-2 with two-wrapper decision tree
- [`docs/guides/SHARED-UI-COMPONENTS-GUIDE.md`](../../docs/guides/SHARED-UI-COMPONENTS-GUIDE.md) — `@spaarke/*` lib consumption
- [`docs/guides/auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md) — Auth v2 operator runbook
- [`docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md`](../../docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md) — Code Page smoke verification

### Architecture (existing — load before touching corresponding code)

- [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — End-to-end pipeline (W-1 supplements this)
- [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md) — Component inventory + PaneEventBus contract
- [`docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`](../../docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) — Audit that surfaced C-items
- [`docs/architecture/AI-ARCHITECTURE.md`](../../docs/architecture/AI-ARCHITECTURE.md) — F-1/F-2 evidence base

### Constraints (load before code change in matching domain)

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — **LOAD-BEARING** for §10 (A-4, B-4, B-5, F-1, F-2, F-3)
- [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) — Publish-size baseline (target of F-3 update)

### External Documentation

- Fluent v9: https://react.fluentui.dev/
- React 19: https://react.dev/blog/2024/12/05/react-19
- RFC 6902 (JSON Patch): https://datatracker.ietf.org/doc/html/rfc6902 (for B-5 if PATCH approach chosen)
- HTTP `If-Match` semantics: https://datatracker.ietf.org/doc/html/rfc7232 (for B-5 if ETag approach chosen)
- ESLint v9 flat config: https://eslint.org/docs/latest/use/configure/configuration-files (B-9)

---

*This file should be kept updated throughout project lifecycle. Decisions Made and Implementation Notes sections grow as tasks execute.*
