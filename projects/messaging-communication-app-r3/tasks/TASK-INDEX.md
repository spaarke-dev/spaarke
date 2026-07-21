# Task Index — Communication Workspace R3

> **Project**: messaging-communication-app-r3
> **Generated**: 2026-07-20 via `/project-pipeline` (task-create)
> **Total tasks**: 31 (30 work + 1 wrap-up) across 6 phases
> **Execution**: Sonnet 5 @ high default; `opus`/`xhigh` on the shared-backend + correctness-critical tasks (see `<model-tier>`/`<effort>` per POML).

Status legend: 🔲 not-started · 🔄 in-progress/retry · ✅ completed · ⛔ blocked · ⏸ deferred

---

## Task Registry

| ID | Title | Phase | Status | Rigor/Tier/Effort | Deps | Parallel |
|----|-------|-------|--------|-------------------|------|----------|
| 001 | Characterize existing communication read + send flows | 1 | 🔲 | STANDARD/sonnet/high | none | Wave 1 |
| 002 | FR-18 enrich single-thread read DTO (Direction + sender identity + Name) | 1 | 🔲 | FULL/opus/high | 001 | serial |
| 003 | FR-16 `GET /communications/threads` + `ListThreadsAsync` + seam tests | 1 | 🔲 | FULL/opus/**xhigh** | 002 | serial |
| 004 | FR-17 participant naming in `ThreadResolver` + BFF rename endpoint | 1 | 🔲 | FULL/opus/high | 001 | serial |
| 005 | FR-19 honor `ThreadId` on email send branch | 1 | 🔲 | FULL/opus/high | 001 | serial |
| 006 | Doc-drift fix `docs/data-model/sprk_communication.md` | 1 | 🔲 | MINIMAL/sonnet/high | none | Wave 1 (A) |
| 010 | Characterize `CommunicationTimeline` core + `SendEmailDialog` | 2 | 🔲 | STANDARD/sonnet/high | none | serial |
| 011 | FR-02/03 `ConversationView` bubbles (sender-id alignment) | 2 | 🔲 | FULL/sonnet/high | 010,002 | Wave 7 (B) |
| 012 | FR-01/10 two-pane shell + thread list | 2 | 🔲 | FULL/sonnet/high | 010,003 | Wave 7 (B) |
| 013 | FR-06 in-conversation compose (existing send path) | 2 | 🔲 | FULL/sonnet/high | 011 | serial |
| 014 | FR-09 in-conversation additive filters | 2 | 🔲 | FULL/sonnet/high | 011 | serial |
| 020 | FR-07 extend `SendEmailDialog`/`EmailComposer` (thread id + record link) | 3 | 🔲 | FULL/**opus**/high | 010 | serial |
| 021 | FR-04 email-in-flow compact block + open→modal | 3 | 🔲 | FULL/sonnet/high | 011,020 | serial |
| 022 | FR-08 forward action → email modal (forward mode) | 3 | 🔲 | FULL/sonnet/high | 020 | serial |
| 023 | FR-05 `MessageQuickView` popover (200-char, open→pin) | 3 | 🔲 | FULL/sonnet/high | 011 | Wave 11 (C) |
| 024 | FR-11 `NewThreadModal` (find-or-create `POST /threads/direct`) | 3 | 🔲 | FULL/sonnet/high | 012 | Wave 11 (C) |
| 025 | FR-12 conversation title → record-scoped modal link | 3 | 🔲 | FULL/sonnet/high | 011,012 | serial |
| 030 | FR-13 record right-pane conversation PCF | 4 | 🔲 | FULL/**opus**/high | 011,012,023 | Wave 15 (D) |
| 031 | FR-14a SpaarkeAI workspace widget (keep `communications-list`) | 4 | 🔲 | FULL/sonnet/high | 012 | Wave 15 (D) |
| 032 | FR-14b standalone Vite conversation code page | 4 | 🔲 | FULL/sonnet/high | 012 | Wave 15 (D) |
| 033 | FR-15 "Email & Messages" DataGrid record tab + `sprk_communicationspage` | 4 | 🔲 | FULL/sonnet/high | none | Wave 15 (D) |
| 034 | Deploy Phase 4 surfaces (Matter pilot) | 4 | 🔲 | STANDARD/sonnet/high | 030,031,032,033 | serial (prescriptive) |
| 040 | FR-24 `sprk_communicationthread` pin field schema | 5 | 🔲 | STANDARD/sonnet/high | none | Wave 17 (E) |
| 041 | FR-24 thread pin UI + persistence | 5 | 🔲 | FULL/sonnet/high | 040,012 | serial |
| 042 | FR-20 attachments open/preview/download + attach-on-compose (SPE) | 5 | 🔲 | FULL/**opus**/high | 011,020 | serial |
| 043 | FR-21 privilege/privacy accuracy (permitted recipients) | 5 | 🔲 | FULL/**opus**/**xhigh** | 002,011 | serial |
| 044 | FR-23 configure Dataverse Search for `sprk_communication` | 5 | 🔲 | STANDARD/sonnet/high | none | Wave 17 (E) |
| 045 | FR-22 notification awareness (`communication-arrived` → badge+toast) | 5 | ⛔ | FULL/**opus**/high | 003 (+spine) | serial (blocked) |
| 046 | FR-25 per-user read-state (best-effort) | 5 | ⏸ | FULL/opus/high | 003 | serial (deferred) |
| 050 | Deploy full solution + UAT across 11 entities | 6 | 🔲 | STANDARD/sonnet/high | 034,041,042,043,044,045 | serial (prescriptive) |
| 090 | Project wrap-up (code-review, adr-check, repo-cleanup, test-diet, lessons) | 6 | 🔲 | FULL/opus/high | all prior | serial (prescriptive) |

---

## Dependency graph (critical path)

```
001 ─┬─ 002 ── 003 ──────────────┐
     ├─ 004                       │
     └─ 005                       │
006 (independent, docs)           │
010 ─┬─ 011 ─┬─ 013               │
     │        ├─ 014               │
     │        ├─ 021 ── (needs 020)│
     │        ├─ 023 ──┐           │
     │        └─ 025    │           │
     ├─ 012 ─┬─ 024     │           │
     │        └─ (031/032 surfaces) │
     └─ 020 ─┬─ 021 / 022 / 042     │
011+012+023 ─── 030 (PCF) ──┐       │
012 ─── 031, 032            │       │
033 (independent grid tab)  │       │
                030,031,032,033 ── 034 (deploy P4)
040 ── 041                                   │
011+020 ── 042                                │
002+011 ── 043                                │
044 (search config, independent)              │
003(+spine) ── 045 ⛔                          │
                034,041,042,043,044,045 ── 050 ── 090
```

**Critical path**: `001 → 002 → 003 → 012 → (surfaces) → 034 → 050 → 090`. FR-18 DTO enrichment (002) and FR-16 list endpoint (003) gate the entire UI stack.

---

## Parallel Execution Plan

> Backend Phase 1 (002–005) is **essentially serial** — all edit shared `Services/Communication/` files (`CommunicationThreadReadService.cs`, `ThreadResolver.cs`, `CommunicationService.cs`, `CommunicationEndpoints.cs`). `parallel-safe:false` is set on each with a file-overlap reason. Run `/conflict-check` before **every** Phase 1/5 BFF wave (shared with r1/r2/email-r4).
> **MAX CONCURRENCY: 6 agents/wave.** Build-verify between waves (`dotnet build` for `.cs`; `npm run build:prod` for PCF, `npm run build` for other TS pkgs).

| Wave | Tasks | Agents | Prereq | goal-eligible |
|------|-------|--------|--------|---------------|
| 1 | 001, 006 | 2 | none | NO (2 tasks) |
| 2 | 002 | 1 | 001 | NO (serial) |
| 3 | 003 | 1 | 002 | NO (serial, correctness-critical) |
| 4 | 004 | 1 | 001 | NO (serial) |
| 5 | 005 | 1 | 001 | NO (serial) |
| 6 | 010 | 1 | none (client; may overlap Phase 1) | NO |
| 7 | 011, 012 | 2 | 010 + 002/003 | NO (2 tasks) |
| 8 | 013 | 1 | 011 | NO |
| 9 | 014 | 1 | 011 | NO |
| 10 | 020 | 1 | 010 | NO (opus, shared send UI) |
| 11 | 023, 024 | 2 | 011 / 012 | NO (2 tasks) |
| 12 | 021 | 1 | 011,020 | NO |
| 13 | 022 | 1 | 020 | NO |
| 14 | 025 | 1 | 011,012 | NO |
| 15 | 030, 031, 032, 033 | 4 | 011,012,023 (033 none) | **YES** |
| 16 | 034 | 1 | 030–033 | NO (deploy, prescriptive) |
| 17 | 040, 044 | 2 | none | NO (2 tasks; 044 prescriptive) |
| 18 | 041 | 1 | 040,012 | NO |
| 19 | 042 | 1 | 011,020 | NO (opus) |
| 20 | 043 | 1 | 002,011 | NO (opus/xhigh, correctness-critical) |
| 21 | 045 ⛔ | 1 | 003 + spine | NO (blocked on notification spine) |
| 22 | 046 ⏸ | 1 | 003 | NO (deferred/best-effort) |
| 23 | 050 | 1 | 034,041,042,043,044,(045) | NO (deploy, prescriptive) |
| 24 | 090 | 1 | all | NO (wrap-up, prescriptive) |

### Wave 15 — `/goal` condition (only goal-eligible wave)
```
All of the following hold in this session:
(1) Tasks 030, 031, 032, 033 each show their acceptance criteria passing via transcript output —
    the shared-lib/PCF/code-page builds succeed (npm run build:prod for the PCF; npm run build for the
    code page + shared libs) and component/ui checks run clean;
(2) Each task's Step 9.5 gates (code-review + adr-check) have been RUN and their full findings surfaced;
(3) git status shows only the four surfaces' expected file changes.
OR: a BLOCKED.md exists under projects/messaging-communication-app-r3/ documenting a root-CLAUDE.md §6
    escalation, shown in transcript.
Stop after 24 turns if neither state is reached.
```
> Evaluator is transcript-only and a **stopping-condition check, not a quality gate** — Step 9.5 + orchestrator authority are unchanged; tasks are never auto-completed on goal achievement.

---

## Coordination notes (hot-path)

- **`/conflict-check` before every Phase 1 / Phase 5 BFF wave** — `Services/Communication/**` is shared with active worktrees `messaging-communication-app-r1/r2` + `email-communication-solution-r4`.
- **Phase 5 FR-22 (task 045) is ⛔ blocked** — the notification spine (`communication-arrived`) is not in master; it lives in `email-communication-solution-r4/projects/spaarke-notification-spine-r1`. Confirm the producer/consumer contract at P1 (`plan.md` Risk R1); do not unblock by inventing a contract.
- **SpaarkeAi hot-path (task 031)** — the `communications-list` widget upgrade is in-place; keep the type string + section id (NFR-06). Coordinate the shared-lib bundle republish with peers per `projects/INDEX.md`.
- **ADR tensions** (both Path C, cite in the deploy PRs): ADR-006 (DataGrid web-resource, task 033) · ADR-026 (Path-A PCF, task 030).

---

## How to execute
- **Serial task**: `work on task NNN` → invokes `task-execute` with full context loading.
- **Parallel wave**: ONE message, MULTIPLE `task-execute` Skill invocations (one per task in the wave). Never parallelize `parallel-safe:false` tasks or any `.claude/`-touching task.
- Build-verify between waves; checkpoint after each wave.
