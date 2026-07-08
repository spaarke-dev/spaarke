# Spaarke Compose R2 - AI Context

> **Purpose**: This file provides context for Claude Code when working on spaarkeai-compose-r2.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning → task decomposition
- **Last Updated**: 2026-07-08
- **Current Task**: Not started
- **Next Action**: Run task-create to decompose plan into task files

---

## Quick Reference

### Key Files
- [`design.md`](design.md) - Working design (feature-first; refined + code-grounded 2026-07-08)
- [`spec.md`](spec.md) - AI implementation spec (36 FRs, 9 NFRs; permanent reference)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan + WBS (phase sequencing around the core dependency)
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker (created by task-create)

### Project Metadata
- **Project Name**: spaarkeai-compose-r2
- **Type**: BFF (`Sprk.Bff.Api`) + SpaarkeAi code page + Dataverse catalog rows
- **Complexity**: High (5 AI actions, Word interop, memory, 3 entry paths; cross-project core dependency)
- **Hot-path**: BFF=Y · SpaarkeAi=Y · ci-workflows=N · skill-directives=N · root-CLAUDE=N

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for FRs/NFRs, acceptance criteria, and owner clarifications
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)

**Before ANY task that adds to `Sprk.Bff.Api`**: load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) (§10 BFF hygiene) and state the Placement Justification.

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

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

task-execute ensures: knowledge files loaded (ADRs, constraints, patterns) · context tracked in current-task.md · checkpointing every 3 steps · quality gates (code-review + adr-check) at Step 9.5 · progress recoverable after compaction. Bypassing → missing ADR constraints, lost progress, skipped gates.

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each still uses task-execute: one message, multiple Skill invocations. Tasks touching `.claude/` are main-session-only (sub-agent write boundary, CLAUDE.md §3).

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for the complete protocol.

---

## Execution Model & Tiering

- **Planning** (design-to-spec, project-pipeline Steps 0–3): run the session on **Opus 4.8 / Fable 5**.
- **Execution** (task-execute per task): default **Sonnet 5 at effort `high`**.
- **Per-task tier + effort**: each POML carries `<model-tier>` (`sonnet` default; `opus`/`fable` for high-blast-radius / architectural / ADR-migration / security tasks) AND `<effort>` (`high` default). This project's **catalog-authoring (Phase 4)**, **ledger/dispatch integration (draft-into-editor, undo/replace)**, and **create-on-save parity** tasks are `opus`-tier candidates (architectural + ADR-039/040 surface). Word DOCX shuttle + entry-path wiring are `sonnet` tasks.
- **Sonnet-5 authoring discipline**: POMLs must be explicit — exact file lists, cite the canonical reference to copy (e.g. `PromoteIfEphemeralAsync`, `docxBridge`, `StaleCheckoutSweeperHostedService`), state exact contracts, acceptance criteria as a closed set incl. negative/auth cases. FULL-rigor gates stay unconditional.

---

## Key Technical Constraints

- **No new AI dispatch endpoint** — dispatch through the shipped session-dispatch seam only (ADR-039; §7.2 of design is the reviewer guard). String-key resolution outside the Binding table is banned.
- **Ledger-first edits** — AI edit payloads are `SessionOutput`s with the `compose` disposition BEFORE render; undo = supersession, not client DOM undo (ADR-040).
- **AI facade** — `Services/Compose/` never injects `IOpenAiClient`/executor/routing types; Tier-1 NetArchTest enforces (ADR-013).
- **Graph isolation** — no `Microsoft.Graph` types above the `SpeFileStore`/Infrastructure facade (ADR-007).
- **Webhook renewal = `BackgroundService`**, not Azure Function (ADR-001).
- **Redis-first** for webhook/etag/re-anchor state (ADR-009).
- **Fluent v9 + dark mode** for all new UI; **`@spaarke/auth`** for client fetches (ADR-021, ADR-028).
- **Context pane is audit-only** — never an interactive input surface.
- **Catalog rows**: mirror-first under `infra/dataverse/inputschemas/`; author *through* the core triple-twin hoist; eval cases (golden + dispatch, ≥5 each); `OpenAiFunctionSchemaValidator` compliance; property-level boolean `required` BANNED.
- **Publish size ≤ 60 MB** compressed; measure per BFF task (baseline 49.63 MB incl. PDBs).
- **Engine frozen** — no new `sprk_analysisplaybook` records; playbooks read as reference data only.

### 🚨 Core Phase A0 dependency (READ before starting a gated task)

These tasks are **BLOCKED** until core R2 (`spaarke-ai-architecture-redesign-r2`) publishes Phase A0 contracts — the core has **no worktree yet**:
- Phase 4 (all 5 catalog rows) — needs `invoke`/dispatch seam + triple-twin hoist + eval-gate
- FR-04 draft-into-editor, FR-16 pending-redline, FR-17 undo/replace — need `ComposeDisposition v1` + SSE frame
- FR-05 OutcomeCard, FR-28 completion — need `JobAwareCompletionState v1` / `OutcomeCard v1`
- FR-30 memory writes — need `memory.write` gated tool; FR-32 trace — needs `TraceEvent v1` / D-F4 view
- FR-34 UI ack — needs D-F3 ack contract

**Do NOT build these against a guessed contract shape.** Start with the independent tracks (Phase 0 spikes, Phase 2 LLM services, Phase 5 DOCX shuttle, entry-path wiring, create-on-save). Confirm core timing before opening a gated task.

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->

- 2026-07-08: Inline redline (Word-style) is the output-staging surface; confirmation in the Assistant; undo/replace via ledger supersession. — Owner review #2
- 2026-07-08: Uploaded files mount transiently, create-on-save (container from business unit; optional parent prompt). — Owner review #2 + interview
- 2026-07-08: Save regenerates `.docx` from editor (original if unedited) — matches as-built `tipTapToDocxBytes` + §1.6.
- 2026-07-08: Clause library + cursor-insertion toolbar OUT of R2 (extensibility preserved). Defined-terms IN (overflow trigger → Context read-only).

---

## Implementation Notes

*No notes yet — verified as-built facts live in spec.md §Existing Patterns and plan.md §2.*

---

## Deferrals & Issues — tracking obligation

Track deferred work + newly-discovered issues in BOTH `notes/defer-issues.md` (source of truth) AND GitHub Issues (visibility), kept in sync via `/project-defer-issue-tracking` (alias `/defer`). Every entry must name a concrete behavior/contract that fails without the work (CLAUDE.md §11). `push-to-github` blocks push on entries lacking a GitHub URL.

---

## Resources

### Applicable ADRs
ADR-039 (dispatch/catalogs) · ADR-040 (ledger) · ADR-013 (AI facade) · ADR-028 (auth) · ADR-038 (testing/eval) · ADR-029 (publish hygiene) · ADR-030 (PaneEventBus) · ADR-031 (stage lifecycle) · ADR-033 (streaming side channel) · ADR-001 (Minimal API/BackgroundService) · ADR-007 (Graph isolation) · ADR-008 (endpoint filters) · ADR-009 (Redis-first) · ADR-010 (DI minimalism) · ADR-021 (Fluent v9) · ADR-032 (Null-Object) · ADR-015 (memory tiers) · ADR-005 (SPE storage).

### Related Projects
- `spaarkeai-compose-r1` — direct foundation (Compose service/layout/endpoints); we extend
- `spaarke-ai-architecture-redesign-r1` — ADR-039/040 + dispatch seam (merged to master)
- `spaarke-ai-architecture-redesign-r2` — **the core**; publishes Phase A0 contracts we consume (no worktree yet)
- `spaarke-dataset-grid-framework-r2` — `@spaarke/legal-workspace` extraction (merge-order coordinate)

### External Documentation
- Microsoft Open XML SDK 3.x (`DocumentFormat.OpenXml`) · Codeuctivity.OpenXmlPowerTools · SPE webhook subscriptions (Graph)
- `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` · `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`

---

*This file should be kept updated throughout project lifecycle.*
