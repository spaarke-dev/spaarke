# Task Index — `email-communication-solution-r5`

> **Generated**: 2026-07-27 via `/project-pipeline`. Source: `plan.md`.
> **Status legend**: 🔲 not-started · 🔄 in-progress/needs-retry · ✅ completed · ⛔ blocked · ⏸ deferred
> **Execution**: every task via `task-execute` (see `../CLAUDE.md` §Task Execution Protocol). Run `/conflict-check` before any BFF / shared-lib PR.

---

## Tasks

| # | Task | Phase | Status | Rigor | Tier/Effort | Group | Safe | Deps |
|---|---|---|---|---|---|---|---|---|
| 001 | Shared hardened `sanitizeEmailHtml` + retrofit `MessageRow`/`MessageBubble` | 0 | ✅ | FULL | opus/high | P0 | ✅ | — |
| 002 | Archiving default-on (`ArchiveIncomingOptIn`, monitored accounts) | 0 | ✅ | FULL | opus/high | — | ❌ | — |
| 010 | `GET /api/documents/{id}/eml-render` (MimeKit + sanitize + cache + tests) | 1 | 🔲 | FULL | opus/xhigh | — | ❌ | 002 |
| 020 | Extract production `ConnectionsEditor` Layer-1 logic (additive write) | 2 | ✅ | FULL | opus/xhigh | — | ❌ | — |
| 021 | Extract `CommunicationAttachments` Layer-1 + promote `AttachmentList` | 2 | 🔲 | FULL | sonnet/high | P2 | ✅ | 020 |
| 022 | Extract `CommunicationActions` Layer-1 (action-bar logic) | 2 | 🔲 | FULL | sonnet/high | P2 | ✅ | 020 |
| 023 | Lift `TrackingFieldTrio` generic core → `@spaarke/ui-components` | 2 | 🔲 | FULL | sonnet/high | P2 | ✅ | 020 |
| 030 | `EmailCardList` flat card list + loading/empty states | 3 | 🔲 | FULL | sonnet/high | P3a | ✅ | 001 |
| 031 | `ViewSelector` over `sprk_communication` saved views + List/Thread toggle | 3 | 🔲 | STANDARD | sonnet/high | P3a | ✅ | — |
| 032 | Reading-pane shell (`PanelSplitter` 2-pane + full-width toolbar) | 3 | 🔲 | FULL | sonnet/high | — | ❌ | 030 |
| 033 | `.eml` render branch — sandboxed iframe + `sprk_body` degradation | 3 | 🔲 | FULL | opus/xhigh | P3b | ✅ | 010, 032 |
| 034 | Envelope header (`CommunicationHeader`) + attachments view | 3 | 🔲 | STANDARD | sonnet/high | P3b | ✅ | 021, 032 |
| 035 | Associations review (interactive/additive) + tracking view | 3 | 🔲 | FULL | sonnet/high | P3b | ✅ | 020, 023, 032 |
| 036 | Compose reuse (Reply/ReplyAll/Fwd/New) + "Open full form" modal | 3 | 🔲 | FULL | sonnet/high | P3b | ✅ | 022, 032 |
| 040 | Assemble shared `EmailWorkspace` component (Pattern D source of truth) | 4 | 🔲 | FULL | sonnet/high | — | ❌ | 033, 034, 035, 036 |
| 041 | `email` widget registration + section shim + `system-layouts.json` seed | 4 | 🔲 | STANDARD | sonnet/high | P4 | ✅ | 040 |
| 042 | Standalone Email code page `src/solutions/EmailPage/**` + auth bootstrap | 4 | 🔲 | FULL | sonnet/high | P4 | ✅ | 040 |
| 050 | Verification sweep — parity + OOB regression + XSS cases | 5 | 🔲 | FULL | opus/high | — | ❌ | 041, 042 |
| 051 | Deploy — BFF + code page + widget seed; publish-size report | 5 | 🔲 | FULL | sonnet/high | — | ❌ | 050 |
| 090 | Project wrap-up — README, lessons-learned, `/test-diet`, archive | 5 | 🔲 | MINIMAL | sonnet/medium | — | ❌ | 051 |

**Count**: 20 tasks across 6 phases (0–5).

---

## Dependency Graph (critical path)

```
002 ─► 010 ─────────────────────►┐
001 ─► 030 ─► 032 ─►(033/034/035/036)─► 040 ─► (041 ‖ 042) ─► 050 ─► 051 ─► 090
020 ─►(021‖022‖023) ──────────────┘         ▲
010 ────────────────► 033 ────────────────┘
```
**Longest chain**: `002 → 010 → 033 → 040 → 042 → 050 → 051 → 090` (the "email as sent" spine).

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| P0 | 001 | — | Sanitizer independent of BFF. 002 serial (shared `Services/Communication/`). |
| P2 | 021, 022, 023 | 020 landed | Distinct PCFs/cores; coordinate `@spaarke/*` barrel exports. |
| P3a | 030, 031 | 001 | Left-pane list + view selector; independent of shell internals. |
| P3b | 033, 034, 035, 036 | 032 landed | Distinct reading-pane sub-views wired into the shell. |
| P4 | 041, 042 | 040 landed | Widget mount ‖ code-page mount. |

**`parallel-safe:false`** (serial, main-session, `/conflict-check` first): 002, 010, 020, 032, 040, 050, 051, 090.
**Goal-eligibility**: P2 and P3b waves are candidates for `/goal` (machine-verifiable end-state, ≥3 low-ambiguity tasks, not deploy/irreversible). P0/P1/P4/P5 run task-by-task (security-critical or irreversible).

---

## High-Risk Items

- **010 / 033 (`.eml` render + XSS)** — security-critical; opus/xhigh; sandboxed iframe + server sanitize mandatory.
- **002 / 010 (shared `Services/Communication/` + BFF endpoint)** — Communication-cluster contention; `/conflict-check` before every PR; sequence merge after `email-communication-solution-r4`.
- **020 (production-vs-stale-stub `ConnectionsEditor`)** — must extract the PRODUCTION PCF logic, not the `CommunicationPage` stub.
- **050 (regression)** — OOB form + 4 PCFs must be regression-free after Layer-1 extraction (NFR-04).

---

*Maintained by task-execute (🔲→✅) + project transitions. History here; active state in `../current-task.md`.*
