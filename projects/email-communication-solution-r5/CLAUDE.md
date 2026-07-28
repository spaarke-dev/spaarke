# Email Workspace (Outlook-style) — R5 · AI Context

> **Purpose**: Context for Claude Code when working on `email-communication-solution-r5`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Development (initialized 2026-07-27 via `/project-pipeline`)
- **Last Updated**: 2026-07-27
- **Current Task**: Not started
- **Next Action**: Execute Phase 0 task 001 (shared `sanitizeEmailHtml`)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized spec (19 FR / 7 NFR) — permanent reference
- [`design.md`](design.md) — use-case → design (6 lenses, 7 Explore audits)
- [`README.md`](README.md) — overview + graduation criteria
- [`plan.md`](plan.md) — phases, WBS, critical path, parallel groups, discovered resources
- [`current-task.md`](current-task.md) — **active task state** (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker + parallel groups

### Project Metadata
- **Type**: UI/surface (dual-use Pattern D) + 1 new BFF endpoint (`eml-render`) + 1 config change (archiving default-on)
- **Complexity**: High (6 phases, cross-surface: BFF + shared React libs + PCFs + code page + widget)
- **Hot-path**: BFF=Y · SpaarkeAi=Y · CI=N · Skills=N

---

## Context Loading Rules

1. **Always load this file first** when starting any task.
2. **Check current-task.md** for active state (especially after compaction/new session).
3. **Reference spec.md** for requirements + acceptance criteria; **plan.md** for phase/critical-path.
4. **Load the relevant task file** from `tasks/`.
5. **Apply ADRs** relevant to the technologies (loaded via adr-aware).
6. **Run `/conflict-check`** before opening any PR that touches `Services/Communication/**`, `@spaarke/communication-components`, `@spaarke/ui-components`, `EmailComposer/**`, or the 4 Communication PCFs — shared with notification-spine-r1, messaging-r1/r2/r3, email-r4.

**Context Recovery**: If resuming, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md).

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" / "keep going" / "next task" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" / "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Implementation**: invoke Skill tool with `skill="task-execute"` and the task file path.

### Why This Matters

task-execute ensures: knowledge files loaded (ADRs/constraints/patterns) · context tracked in current-task.md · checkpointing every 3 steps · quality gates (code-review + adr-check) at Step 9.5 · progress recoverable after compaction.

### Parallel Task Execution

Tasks in a parallel group each STILL use task-execute — one message, multiple Skill invocations. **Never** parallelize tasks marked `parallel-safe: false` (002 archiving, 010 endpoint, 032 shell, 040 assembly) or any task touching `.claude/` (main-session-only per Sub-Agent Write Boundary).

---

## Execution Model & Tiering (Sonnet-5)

- **Planning** (project-pipeline Steps 0–3): Opus 4.8 / Fable 5.
- **Execution** (task-execute per task): default **Sonnet 5 @ effort `high`**. Each POML carries `<model-tier>` (`opus` for the security-sensitive `eml-render` endpoint + sanitizer + XSS verification, and the `Services/Communication/` shared-file edit) and `<effort>` (`xhigh` for the brownfield `.eml` parse/sanitize + the production-vs-stub `ConnectionsEditor` extraction).
- **Sonnet-5 authoring discipline**: POMLs are explicit — exact files, cite the canonical reference to copy (`GraphMessageToEmlConverter.cs`, `TextExtractorService.cs`, `MessageRow.tsx`, `ConnectionsEditor.tsx`, `CalendarWorkspaceWidget.tsx`, `DailyBriefing/main.tsx`), state exact contracts, scope every constraint, closed-set acceptance criteria incl. negative/authorization cases.

---

## Key Technical Constraints

- **Two-layer split (the load-bearing decision)** — share **React-agnostic logic** (Layer 1), NOT React components, across the PCF boundary. PCFs keep platform React 16/17 views; code page = React 19. No `as React.ComponentType` cast on new code-page work (ADR-022 slim-first, NFR-05).
- **Sanitize ALL email HTML before display** — server-side in `eml-render` (`.eml` path) + shared client `sanitizeEmailHtml` (field bodies). MUST render `.eml` in a **sandboxed iframe** (`sandbox`, no `allow-scripts`/`allow-same-origin`). No script execution from any email HTML (NFR-03).
- **Reuse the canonical `EmailComposer`/`SendEmailDialog`** for every compose/reply/forward/new — MUST NOT fork a new composer.
- **Use the existing additive association write path** (`applyRegardingSelection`) — MUST NOT clear-and-set. Use the **production** `ConnectionsEditor` (PCF version), NOT the stale `CommunicationPage` stub.
- **Keep the OOB `sprk_communication` form + 4 PCFs working** (regression-free after Layer-1 extraction — NFR-04).
- **No new BFF surface** beyond the single `eml-render` endpoint.
- **BFF hygiene (§10)** — the `eml-render` task: state Placement Justification in PR (cite `.claude/constraints/bff-extensions.md`), verify publish ≤60 MB compressed (baseline ~49.63 MB incl. PDBs), 0 new HIGH CVE, add endpoint tests in `tests/unit/Sprk.Bff.Api.Tests/`. MimeKit already referenced — no new package.
- **Fluent v9 + dark mode** via host `FluentProvider` (ADR-021).
- **All BFF calls** via `@spaarke/auth` `authenticatedFetch` (ADR-028).
- **PCF prod build** — `npm run build:prod` (NOT `npm run build`). Non-PCF packages: `npm run build`. Vite installs: `npm install --legacy-peer-deps --no-audit --no-fund`.

### ADR Tensions on record (cite in PRs)
- **ADR-022 (slim-first)** — Path **C** (comply): two-layer split shares logic, not components; React 19 views for the code page only. Matches `CommunicationConnections`/`CommunicationAttachments`.
- **ADR-022 (factual currency)** — Path **B** (minor amendment): text says "React 18 unavailable as of May 2026"; MDA runtime now 17.0.2 / Fluent 9.68.0. MUST-rules unchanged; non-blocking follow-up.
- **§10 BFF Hygiene** — Path **C**: new `eml-render` endpoint justified; MimeKit present; tests + size report required.
- **ADR-045 (Communication/Association)** — Path **C**: pane displays associations + reuses additive write path; no new client association logic. Thread-entity inheritance gap **deferred**, not worked around.
- **ADR-039 / BFF §10 (surface identity in code)** — Path **C**: widget registration + shim in code; no server-side surface identity.

---

## Decisions Made

*No decisions recorded yet* (see design.md §Resolved Decisions 2026-07-27 for the design-time set).

---

## Implementation Notes

- **`.eml` is the source of "as sent"** — the archived `.eml` preserves the full `body.content`; the queryable `sprk_body` field stores only Graph's stripped `uniqueBody`. Reading pane renders the `.eml`; degrades to `sprk_body` when no archive exists.
- **Archiving must exist first** — FR-17 (default-on) lands before/with the reading pane so `.eml` exists to render.
- **`.eml` caching** — production posture: render-on-open, response marked immutable / long-lived cacheable (the `.eml` is immutable). No bespoke server cache unless metrics require.
- **Predecessor `email-communication-solution-r4`** owns the merged `Services/Communication/**` + `EmailComposer`/send engine + the 4 Communication PCFs r5 extends. Confirm its state in the tree before extraction tasks.
- **Deferred (documented, not r5)**: server `.eml` render cache, remote-image privacy gate, thread-entity association inheritance (server round), historical `.eml` backfill.

---

## Deferrals & Issues — tracking obligation (read this)

Track deferred work + newly-discovered issues in TWO synced places:
1. **`notes/defer-issues.md`** — source of truth
2. **GitHub Issues** on the portfolio board — visibility

Invoke `/project-defer-issue-tracking` (alias `/defer`) — writes both in one step. NEVER file to `notes/` only. §11 rule: every entry names a concrete failing behavior/contract (not "flexibility"/"testability"). `push-to-github` blocks push on entries without GitHub URLs.

---

## Resources

### Applicable ADRs
- ADR-022 (PCF platform libs / slim-first), ADR-006 (PCF vs Code Page), ADR-012 (shared components), ADR-021 (Fluent v9/dark), ADR-028 (auth v2), ADR-045 (communication/association), ADR-038 (testing/endpoint+seam tests)

### Related Projects
- `email-communication-solution-r4` (predecessor — EmailComposer/send engine, `Services/Communication/**`, 4 Communication PCFs, ADR-045)
- `messaging-communication-app-r2/r3` (share `@spaarke/communication-components` / `Services/Communication/`)
- `spaarke-notification-spine-r1` (edits `Services/Communication/` persist path; lives in email-r4 worktree)

### External Documentation
- `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`, `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`
- `docs/standards/MODAL-DECISION-CRITERIA.md`, `DATA-ACCESS-DECISION-CRITERIA.md`
- `docs/data-model/sprk_communication.md`
- `.claude/constraints/bff-extensions.md` · root CLAUDE.md §10/§11

---

*Keep this file updated throughout the project lifecycle.*
