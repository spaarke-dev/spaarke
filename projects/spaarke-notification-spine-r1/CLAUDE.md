# Spaarke Notification & Action Spine — R1 - AI Context

> **Purpose**: This file provides context for Claude Code when working on spaarke-notification-spine-r1.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Development (Phase 0 — gate-zero spike is the first task)
- **Last Updated**: 2026-07-20
- **Current Task**: Not started
- **Next Action**: Execute Phase 0 (FR-01 SignalR footprint spike) — the BLOCKING go/no-go gate

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI-optimized specification (source of truth for requirements)
- [`design.md`](design.md) - Original human design (preserved verbatim)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan, WBS, and wave order
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker

### Project Metadata
- **Project Name**: spaarke-notification-spine-r1
- **Type**: BFF platform infrastructure (SignalR delivery spine) + Dataverse schema + shared client library + SpaarkeAi renderer
- **Complexity**: High (high-blast-radius; heavily-contested BFF hot path; hard FR-01 go/no-go gate; cross-project coordination)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task.
2. **Check current-task.md** for active work state (especially after compaction/new session).
3. **Reference spec.md** for requirements and acceptance criteria; **plan.md** for wave order + phase gates.
4. **Load the relevant task file** from `tasks/` based on current work.
5. **Apply ADRs** relevant to the technologies used (loaded automatically via adr-aware).
6. **Before ANY BFF PR**: run `/conflict-check` (this is one of the most-contested hot paths — 18 active worktrees touch BFF).
7. **Before adding to `Sprk.Bff.Api`**: load `.claude/constraints/bff-extensions.md`; state the Placement Justification; verify publish size ≤60 MB.

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

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and the task file path.

### Why This Matters

The task-execute skill ensures: knowledge files loaded (ADRs, constraints, patterns) · context tracked in current-task.md · checkpointing every 3 steps · quality gates (code-review + adr-check) at Step 9.5 · progress recoverable after compaction. **Bypassing it** loses ADR constraints, checkpointing, and quality gates.

### Parallel Task Execution

When tasks can run in parallel (no dependencies + `parallel-safe: true` + not `.claude/` paths), each task MUST still use task-execute: send ONE message with MULTIPLE Skill invocations. **`.claude/` writes are main-session only** (sub-agent write boundary) — ADR-047 authoring (FR-18) runs main-session, never in a parallel agent.

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md).

---

## Execution Model & Tiering (Sonnet-5)

- **Planning** (this pipeline run): Opus 4.8 / Fable 5.
- **Execution** (task-execute per task): default **Sonnet 5 @ effort `high`**; each POML carries `<model-tier>` + `<effort>`.
- **This project skews `opus` + higher effort** for the high-blast-radius tasks: Layer-A extraction (Phase 3), the Notification flip (ADR-043), fan-out targeting security (FR-08), and ADR-047 authoring. The outbox CRUD, envelope contract, and renderer tasks can run `sonnet @ high`.
- **Seam tests are DoD** — a task touching dispatch-spine code is not done without its `tests/integration/seam/**` test (ADR-038).
- Tasks with a judgment boundary (rule-store decision, Notification-flip audit, >60 MB spike outcome) carry `<escalation><trigger>` — firing it is a legitimate stop, not improvisation (root §6/§6.5).

---

## Key Technical Constraints

- **Spine is dumb transport** — grounding + gates live in producers; the spine NEVER carries ungrounded/ungated content (NFR-03). MUST NOT put message bodies, privileged content, or pre-authorized action tokens in envelopes (IDs + minimal display metadata only).
- **Outbox BEFORE ping** — write the durable outbox row, THEN best-effort SignalR ping; producers stay correct when SignalR is unreachable (ADR-041/043 store-before-render).
- **No second push/delivery/action path** — one spine; per-consumer hubs, a parallel proactive-action path, or a second gate/decider are hard MUST NOTs.
- **Layer A behind executors** — extract the seam BEHIND `*NodeExecutor.cs`; characterization tests pin the chat path FIRST; existing executor tests pass unmodified (ADR-013 facade via `PublicContracts`).
- **Fan-out from record security** — targeting derives from `sprk_communication`/thread + `sprk_communicationparticipant`; MUST test negative access (a leak is a compliance incident — R-5).
- **Grounded + gated BEFORE outbox write** (suggestions) — ADR-039 grounding + ADR-041 gate (`origin=proactive`) are the *input* to the outbox write, not after it.
- **ADR-032 null-object** — every new conditional service (SignalR delivery, producers) registered unconditionally with a null-object fallback; unconditional endpoint ⇒ startup metadata-gen must not abort.
- **BFF ≤60 MB compressed** — baseline ~49.63 MB incl-PDB / 45.87 excl-PDB; state PDB convention; ≥55 MB → architecture review; ≥60 MB → HARD STOP; SignalR SDK delta gated by FR-01. No new HIGH CVE.
- **Auth v2** — client uses `@spaarke/auth` `authenticatedFetch` / `getAccessToken()` per request; negotiate endpoint authenticated; SignalR/SSE raw-fetch is the enumerated exception (`// Auth v2 (D-AUTH-7):`).
- **MUST NOT route comms RI through `EventRulesService.FireAsync`** — reuse gate *primitives* (cost cap, confidence), NOT the chat/SSE user/session scoping.
- **MUST NOT let messaging-r3 wire its own `communication-arrived` producer** — the spine emits; R3 consumes only.

### Substrate corrections (verified 2026-07-20 — use these, not spec claims)
- Enrichment emit method is **`RunAssessmentEmissionAsync`** (`CommunicationEnrichmentService.cs:216-238`), not the spec's `RunAsessmentEmissionAsync`.
- **`DailyBriefingNarrator` does NOT write `appnotification`** — narration-only; appnotification writes are centralized in `NotificationService.CreateNotificationAsync` (`NotificationService.cs:55`). The FR-13/15 producer must write there/to the outbox explicitly.
- `PendingPlanManager` cites ADR-039/040 in-code; spec labels it the "ADR-041 gate" (ADR-041 is Proposed). The gate itself is confirmed present.
- `Notification` in `DispositionRoutability.cs:98-102` confirmed `Routable=false` (the FR-14 flip target).

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
- 2026-07-20: Wave order = gate-zero → Layer B+C+`communication-arrived` → Layer A+flip → comms-RI → suggestions — Reason: unblocks messaging-r3 (task 045) earliest per spec Assumptions §wave order — project-pipeline.
- 2026-07-20: ADR-047 authored main-session (sub-agent write boundary on `.claude/`) — Reason: FR-18 + root §3.

---

## Implementation Notes

*No notes yet* — Phase 0 (FR-01 spike) begins execution. Read `.claude/agent-memory/researcher/signalr-vs-sse-notification-fabric-2026-07-16.md` and `assistant-push-channel-2026-07-15.md` before the spike.

---

## Deferrals & Issues — tracking obligation

This project tracks deferred work + newly-discovered issues in TWO places, kept in sync:
1. **`notes/defer-issues.md`** — source of truth (full context, links, traceability).
2. **GitHub Issues** on the portfolio board (visibility).

File via `/project-defer-issue-tracking` (alias `/defer`) — it writes to BOTH in one step. NEVER add to `notes/defer-issues.md` and skip the GitHub Issue. Every entry must name a concrete behavior/contract that fails without the work (CLAUDE.md §11 — "for future flexibility" is refused).

---

## Resources

### Applicable ADRs
- **ADR-047** (NEW, this project) — the spine itself (FR-18).
- **ADR-013** — AI facade (Layer-A seam via `PublicContracts/`).
- **ADR-032** — Null-Object kill-switch (SignalR delivery, producers).
- **ADR-038** — testing strategy; seam tests as DoD.
- **ADR-039 / ADR-041** — grounded actions / ONE confirmation gate.
- **ADR-043** — `DispositionRoutability` source of truth (Notification flip goes through it).
- **ADR-015 / ADR-045 / ADR-046 / ADR-048** — communication architecture, privilege-flagged-not-decided, ACS channel, participant junction (FR-08 targeting).
- **ADR-024** — regarding family for envelope `regardingRecordId`.
- **ADR-027** — per-customer Azure provisioning orchestrator.
- **ADR-028** — auth v2 (client fetch, negotiate endpoint).
- **ADR-021** — Fluent v9 + dark mode (renderer/badge surfaces).

### Related Projects (hot-path coordination)
- `email-communication-solution-r4` (BFF; owns `Services/Communication/**` until W10 merges) — enrichment/persist-path touches land AFTER their W10.
- `messaging-communication-app-r1/r2/r3` — R3 consumes `communication-arrived` (its task 045 blocked on our FR-19 contract lock).
- `spaarkeai-assistant-enhancements-r1` — R1.5 proactive push absorbed here; closest design peer (avoid a second push channel).
- `spaarke-ai-architecture-redesign-r2` — owns `Services/Ai/` internals; consume `PublicContracts` seams, do NOT fork.
- `spaarke-daily-update-service-r5` — coordinate the Daily-Briefing suggestion producer.

### External Documentation
- Azure SignalR Service (mode/tier per FR-01; provisioning via ADR-027).
- `.claude/agent-memory/researcher/signalr-vs-sse-notification-fabric-2026-07-16.md` (FR-01 primary reference).

---

*This file should be kept updated throughout project lifecycle.*
