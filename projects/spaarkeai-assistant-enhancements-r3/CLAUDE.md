# SpaarkeAI Assistant Enhancements R3 - AI Context

> **Purpose**: Context for Claude Code working on spaarkeai-assistant-enhancements-r3.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Tasks generated — **execution owner-gated (NOT auto-started)**.
- **Last Updated**: 2026-08-10
- **Current Task**: Not started
- **Next Action**: Re-sync `origin/master` (branch 5 behind at init), then begin Phase 0 (task 001) via task-execute — **owner go-ahead required**.

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized spec (15 FRs / 9 NFRs) — permanent reference.
- [`design.md`](design.md) — the Assistant ⇄ Workspace Interaction Contract (§5.5 orchestration model).
- [`plan.md`](plan.md) — WBS + parallel groups + hot-path coordination.
- [`current-task.md`](current-task.md) — **active task state** (context recovery).
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker + dependencies + parallel groups.
- [`notes/R3-SESSION-CONTEXT.md`](notes/R3-SESSION-CONTEXT.md) — the bridge from R2; code-reference map.
- [`notes/design-review-2026-08-10.md`](notes/design-review-2026-08-10.md) — external review (§A1–A6 applied).

### Project Metadata
- **Type**: BFF API (`Services/Ai/Chat` + parity tools) + Frontend (SpaarkeAi `ConversationPane`/`WorkspacePane` + shared widgets/email components).
- **Complexity**: Medium-High (wiring/reuse over existing machinery; heavy cross-worktree contention).
- **Predecessor**: spaarkeai-assistant-enhancements-r2 (shipped — do not reopen).

---

## Context Loading Rules

1. Always load this file first.
2. Check `current-task.md` for active work state (especially after compaction).
3. Reference `spec.md` for FRs, acceptance criteria, ADR tensions, MUST rules.
4. Load the relevant task file from `tasks/`.
5. Apply ADRs via adr-aware.

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|---|---|
| "work on task X" | Execute task X via task-execute |
| "continue" / "keep going" / "next task" | Execute next 🔲 in TASK-INDEX.md via task-execute |
| "continue with task X" / "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Parallel execution**: each task still uses task-execute (one message, multiple Skill invocations). **This project's contention rule**: `ConversationPane.tsx` is a sequential spine (per-item card tasks 025/026 + follow-ons 041) → `parallel-safe:false` among themselves. `Services/Ai/Chat` tasks coordinate with `spaarke-ai-architecture-redesign-r2` (consume `PublicContracts/`, no fork). Email-component tasks (024) coordinate with `email-communication-solution-r5`. See TASK-INDEX Parallel Groups.

**Multi-file work (4+ files)**: decompose into a dependency graph, parallelize independent modules, serialize coupled files. See task-execute Step 8.0.

---

## Execution Model & Tiering (Sonnet-5)

- **Planning** (this pipeline): Opus 4.8 / Fable 5.
- **Execution**: default **Sonnet 5 @ high**.
- **opus / xhigh escalations**: **001** (shared conduit blast radius), **011** (ADR-015 prompt boundary + redesign-r2 seams), **020** (the overview DoD; server-side query + `today`), **023** (email tools + AI facade), **024** (thread-preservation invariant — data-loss-adjacent), **030** (PreFilter on ADR-039 boundary).
- **Coverage-first gates**: code-review + adr-check unconditional on FULL-rigor tasks; orchestrator filters findings.

---

## Key Technical Constraints

- **ADR-039**: tab-driven mounting uses ONLY the sanctioned deterministic `PreFilter` — **NO classifier, NO second dispatch surface**. Parity capabilities are catalog rows.
- **ADR-015 (Path A, honest)**: prompt carries `{id,type,label}` handle + per-tab identity — **NO item content**. All content tool-fetched by id. (Tighter than R2 ambient content; the label is a thin slice, stated honestly — not pure identity-only.)
- **ADR-013 / BFF §10**: use `Services/Ai/PublicContracts/` facade; **do NOT fork `Services/Ai/` internals** (redesign-r2 sole owner). No AI-internal types into CRUD code.
- **ADR-047**: the reactive/local-selection card surface stays **distinct** from the server-initiated notification spine. No merge, no new push channel.
- **Reuse-first (§11)**: ONE parameterized overview tool (not N per-grid handlers); email tools **extend `EmailDraftToolHandler`**; document tools reuse `IRagService`/document-context; active-item conduit **generalizes** `composeActionBridge`/`active-doc-follows-tab`.
- **Thread-preservation invariant (FR-10, BINDING)**: `bodyOverride` overrides only the authored-message region; the composer still appends the reducer-derived `quotedThread`. A whole-body-replacing `bodyOverride` is a defect.
- **Dual-mount parity (NFR-05)**: shared email component changes MUST NOT break the standalone `sprk_emailpage` code page.
- **BFF §10**: publish ≤60 MB (baseline ~49.63 MB incl. PDBs). Measure per BFF-touching task. `/conflict-check` before `Services/Ai`/`ConversationPane` PRs.

---

## Decisions Made (owner 2026-08-10)

- Per-item card scope = **Email + Documents** (others get overview parity only).
- Overview parity = **all grids + Briefing + Calendar** (one parameterized `configId` tool).
- Reply cards **auto-draft** the body (proactive AI) via `draft_reply` + `bodyOverride`.
- **Thread-preservation invariant** binding on the `bodyOverride` path.
- Selection hint stays **in the prompt** (no `get_selection` tool).
- Phase 0 is **out** (shipped on R2).
- `summarize_thread` output = plain narrative, identical to file-summarize.
- Follow-on element type: per-item→cards, query→chips (per `ASSISTANT-UI-ELEMENT-CRITERIA.md`).

---

## Implementation Notes (verified 2026-08-10)

- **Email selection already emits**: `EmailWorkspace.onVisibleEmailChange` (`:199`) → `deriveEmailWorkspaceVisibleState`; consumed today into tab `widgetData` (FR-C1 carrier). R3 redirects it to the conduit as an **id handle** (not content). FR-05 = small.
- **Document active item = tab-focus**: `document-viewer` is single-doc-per-tab with a stable `documentId` in `widgetData` (`DocumentViewerWidget.tsx:99-112`); reuse `WorkspacePane` `active-doc-follows-tab` (`:2283`, generalize from `widgetType === 'compose'`). No new in-widget selection. FR-11 = small.
- **Reply composer already includes the thread**: `useEmailComposeActions.openComposer` builds Re:/Fwd: recipients + subject + quoted body (`EmailComposer.reducer` `deriveReplyState`/`quotedThread`). The in-dialog sparkle already re-appends the thread (`runAiDraft`, owner UAT 2026-08-03 R5 items 1/2). The NEW `bodyOverride` path must reach parity.
- **Conduit precedent**: `composeActionBridge` (`registerActiveDocument`/`activeSourceDocRef`) is a non-bus sibling-pane conduit (ADR-030 keeps the bus content-free). Generalize to widget-agnostic `{id,type,label}`.
- **Overview tool driver**: `read_query` rejects `GETDATE()`/`COUNT`/aggregates → the DoD failure. The parameterized tool executes the grid's saved-query/FetchXML server-side with `today` injected.

---

## Deferrals & Issues — tracking obligation

Track deferred work + new issues in BOTH `notes/defer-issues.md` AND GitHub Issues via `/project-defer-issue-tracking` (alias `/defer`). §11 rule: every entry names a concrete behavior/contract that fails without the work.

---

## Resources

### Applicable ADRs
ADR-039 (closed catalogs/pre-filter), ADR-015 (data governance — Path A), ADR-013 (AI facade), ADR-030 (PaneEventBus), ADR-047 (spine — keep distinct), ADR-049 (Compose precedent), ADR-012 (shared components), ADR-028 (auth/OBO), ADR-038 (testing), ADR-032 (kill-switch — only if feature-gated).

### Related Projects (coordinate)
- `spaarke-ai-architecture-redesign-r2` — sole owner `Services/Ai/` internals (consume seams, no fork).
- `spaarkeai-assistant-enhancements-r2` — predecessor (shipped/merged; re-base on it).
- `spaarke-notification-spine-r1` — ADR-047 spine (keep distinct).
- `analysis-hub-r1` / `agreements-r1` — `ConversationPane` fork/routing (merge-order coordination).
- `email-communication-solution-r5` / `email-communication-intelligence-r2` — email components (`bodyOverride` coordination).
- `spaarkeai-compose-r5/r6` — `composeActionBridge`/`Services/Compose` (conduit generalization).

### External Documentation
`docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md`, `docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md`, `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`, `.claude/constraints/bff-extensions.md`.

---

*Keep this file updated throughout the project lifecycle.*
