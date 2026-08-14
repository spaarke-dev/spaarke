# Spaarke Compose R7 — AI Context

> **Purpose**: This file provides context for Claude Code when working on spaarkeai-compose-r7.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Development (initialized — tasks generated, execution owner-gated)
- **Last Updated**: 2026-08-13
- **Current Task**: Not started
- **Next Action**: Owner approval to begin Phase 1 (UC-8 save-identity fix)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI-optimized spec (13 FRs, 6 NFRs, ADR tensions) — permanent reference
- [`design.md`](design.md) - Human design document (input)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS (8 phases)
- [`current-task.md`](current-task.md) - **Active task state** (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker (created by task-create)
- [`notes/r6-defer-register-consolidated.md`](notes/r6-defer-register-consolidated.md) - R6→R7 defer register

### Project Metadata
- **Project Name**: spaarkeai-compose-r7
- **Type**: Compose editor-UX (React/TipTap client + narrow BFF) + data-integrity bug fix + PDF-import wiring. **Not** an AI-capability project.
- **Complexity**: Medium-High (live data-integrity bug + BFF contract change + shared-file coordination)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md** for the FR/NFR closed sets and acceptance criteria
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware)

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

The task-execute skill ensures: knowledge files loaded (ADRs, constraints, patterns) · context tracked in current-task.md · proactive checkpointing every 3 steps · quality gates (code-review + adr-check) at Step 9.5 · progress recoverable after compaction.

**Bypassing** leads to missing ADR constraints, lost progress after compaction, skipped quality gates.

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute: send one message with multiple Skill invocations. **`parallel-safe: false` on ALL `Services/Compose/` + `ComposeWorkspace.tsx` + `ConversationPane.tsx` tasks** (shared spine + cross-worktree contention). Tasks writing `.claude/` are main-session-only (§3).

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Execution Model & Tiering (Sonnet-5)

- **Planning** (this pipeline): Opus 4.8 / Fable 5.
- **Execution** (task-execute per task): default **Sonnet 5 @ effort `high`**. BFF contract-change tasks (FR-06 async `ProjectForMount`, FR-07d upsert) and the FR-07b stable-logical-id task may warrant `opus`/`xhigh` — assigned per POML `<model-tier>`/`<effort>`.
- **Sonnet-5 authoring discipline**: POMLs are explicit — exact file lists + line anchors, cite reference impls (`LoadAsync` PDF fork @502–508; `SaveAsync`/dedup), closed-set acceptance criteria incl. negative/dedup cases. No anti-laziness scaffolding.

---

## Key Technical Constraints

- **Autosave is CLIENT-ONLY** (local/session storage) — no server write until explicit Save; UC-4 touches no BFF surface. Do NOT add a server draft store (spec MUST-NOT).
- **Draft-recovery key + client dedup share ONE stable logical id** (`sprkDocumentId ?? speDriveItemId ?? persistedLogicalId`) — FR-07(b) introduces it; none exists today (confirmed).
- **`ProjectForMount` becomes async** for the PDF fork (ADR-007/013 contract change) — keep the docx path synchronous-fast; document in code + PR (NFR-04).
- **Server dedup is an atomic upsert** on `sprk_graphitemid_uk` — replace read-then-`CreateAsync`@2717 (the D1 hole).
- **Reuse, don't fork** — `FormModal`/`SprkModal` (ADR-050), `promptForInstruction`, `forceVisible`, R6's `ComposePdfModelProjector`/`ProjectPdfToDocxAsync`. Consume `Services/Ai/PublicContracts/` for FR-11 — NO fork of `Services/Ai/`.
- **.NET 10 (as of 2026-08-14)**: master + dev runtime are **net10**; this branch is net10-ready (merged net10 master 2026-08-14; BFF Release build clean). SDK ≥10.0.100 required (`global.json` pins 10.0.100) — if a shell errors "A compatible .NET SDK was not found. Requested SDK version: 10.0.100", open a fresh terminal (stale SDK resolution), it's not a code problem. **NEVER deploy the BFF from a net8 tree** (→ 503 on the net10 runtime). Graph/Kiota break notes: `projects/dotnet-10-upgrade-r1/notes/graph6-kiota2-break-assessment.md`.
- **BFF hygiene (root §10)**: BFF work stays in `Services/Compose/`; Placement Justification per BFF task; publish ≤60 MB — the **net10 baseline is re-measured in task 001** (the ~46.94 MB cited elsewhere was the net8 R6 baseline); no new HIGH CVE; `/conflict-check` before every BFF PR.
- **`ComposeSaveMode` stays `'version' | 'new'`** — map labels only.
- **NEVER delete `docxBridge.ts`.**
- **Commit `--no-verify`**; co-author trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- **Deploy BFF + `sprk_spaarkeai` together** (anti-clobber, NFR-05).

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->

- **2026-08-13** — Autosave draft store = **client-only** (owner clarification). Rationale: never-lose-work without version-per-tick; zero BFF footprint. Who: Ralph + Claude.
- **2026-08-13** — Draft-recovery + client dedup **unified on one stable logical id** (FR-07b introduces it). Rationale: no non-rotating id exists today; unify FR-03 + FR-07. Who: Ralph + Claude.
- **2026-08-13** — Fidelity wideners **deferred; home named at R7 wrap-up** (owner). Who: Ralph + Claude.
- **2026-08-13** — D1 dev-data hygiene (5 dup records) = **leave/defer** (accepted debt). Who: Ralph.

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

- **Hot-path coordination** (from INDEX.md at init): `ConversationPane.tsx`/`SprkChatInput.tsx` overlap with active `spaarkeai-assistant-enhancements-r3`; `Services/Ai/ComposePdfIntakeSource.cs` (FR-11) with sole-owner `spaarke-ai-architecture-redesign-r2`. Watch PRs #690 (ci-lfs — "fixes 5 Compose seam tests", relevant to FR-13) + #266 (OpenXml bump). `/conflict-check` before BFF/ConversationPane PRs.
- **IME caveat**: `Ctrl+Space` is an IME toggle on some stacks — guard `event.isComposing`; fall back to `Ctrl+/` if testing shows conflicts.
- **PDF DI gate**: FR-06 requires `Analysis:Enabled && DocumentIntelligence:Enabled` ON in the target env, else `NullComposePdfIntakeSource` → typed "PDF intake unavailable".

---

## Deferrals & Issues — tracking obligation (read this)

Track deferred work + newly-discovered issues in TWO places, kept in sync:
1. **`notes/defer-issues.md`** — source of truth (full context, links, traceability)
2. **GitHub Issues** on the portfolio board (visibility)

File via `/project-defer-issue-tracking` (alias `/defer`) — writes to BOTH in one step. NEVER add to `notes/defer-issues.md` and skip the GitHub Issue. CLAUDE.md §11 applies: every entry must name a concrete failing behavior/contract ("future flexibility" is refused).

**Known R7 deferral to file at wrap-up**: fidelity-wideners home (Idea or fast-follow project) — carry R6 defer-register §C evidence.

---

## Resources

### Applicable ADRs
- **ADR-049** — Compose Shadow Document (save path)
- **ADR-050** — Canonical Modal Shell (name modal)
- **ADR-032** — Null-Object kill-switch (PDF intake gate)
- **ADR-007 / ADR-013** — `ProjectForMount` contract (NFR-04 tension)
- **ADR-021** — Fluent v9 dark-mode (new UI: dropdown, modal, indicator)
- **ADR-038** — Testing strategy (seam tests for dispatch-spine changes; FR-13)

### Related Projects
- `spaarkeai-compose-r6` — predecessor (save + PDF-intake engines R7 rides on; merged to master)
- `spaarkeai-compose-templates-r8` — templates split-out (sequence AFTER R7)
- `spaarkeai-assistant-enhancements-r3` — active; `ConversationPane`/`SprkChatInput` coordination
- `spaarke-ai-architecture-redesign-r2` — sole owner of `Services/Ai/` (FR-11 consumes `PublicContracts/`)

### External Documentation
- Azure Document Intelligence (PDF intake) — target-env gate verification
- [COMPOSE-READ-REFERENCE-FIDELITY.md](../../docs/architecture/COMPOSE-READ-REFERENCE-FIDELITY.md)
- [MODAL-DESIGN-SYSTEM.md](../../docs/standards/MODAL-DESIGN-SYSTEM.md)

---

*This file should be kept updated throughout project lifecycle*
