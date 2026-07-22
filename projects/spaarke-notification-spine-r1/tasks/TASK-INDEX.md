# Task Index — spaarke-notification-spine-r1

> **Generated**: 2026-07-20 by `/project-pipeline` (task-create)
> **Total tasks**: 22 (incl. mandatory wrap-up) across 6 phases
> **Status legend**: 🔲 not-started · 🔄 in-progress/needs-retry · ✅ completed · ⛔ blocked
> **Execution**: every task via `task-execute`. Phase 0 (001) is a HARD go/no-go gate — do NOT start Phase 2 until it resolves.
> **GATE 001 RESOLVED 2026-07-21**: ✅ **GO / Serverless mode** (`Microsoft.Azure.SignalR.Management` 1.33.1; +0.30 MB, 0 new HIGH CVE; Layer C stays in BFF). Awaiting human review of `notes/spikes/fr-01-signalr-footprint.md` before Phase 2 dispatch. CSP action item: verify Power Platform env `connect-src` at provisioning.

## Task Registry

| ID | Title | Phase | Status | Deps | Tier/Effort | Rigor | Parallel |
|----|-------|-------|--------|------|-------------|-------|----------|
| 001 | SignalR footprint spike (FR-01) | 0 | ✅ | none | opus/high | STANDARD | none (blocking gate) |
| 010 | Author ADR-047 (concise + full) | 1 | ✅ | 001 | opus/high | STANDARD | ❌ main-session (.claude/) |
| 011 | Outbox table schema (FR-02) | 1 | ✅ | none | sonnet/high | STANDARD | Group P1 |
| 012 | Outbox service (FR-02) | 1 | ✅ | 011 | sonnet/high | FULL | — |
| 013 | Envelope contract + kind taxonomy (FR-03/10) | 1 | ✅ | none | sonnet/high | FULL | Group P1 |
| 020 | SignalR delivery service + negotiate (FR-04) | 2 | ✅ | 001,012,013 | opus/high | FULL | — |
| 021 | Shared client subscriber library (FR-05) | 2 | ✅ | 020,013 | sonnet/high | FULL | — |
| 022 | Pending/poll fallback endpoint (FR-06) | 2 | ✅ | 012 | sonnet/high | FULL | Group Q |
| 023 | Fan-out targeting + negative-access (FR-08) | 2 | ✅⚠️ | 020 | opus/xhigh | FULL | Group Q |
| 024 | communication-arrived producer (FR-09) | 2 | ✅ | 012,013,020 | opus/high | FULL | — |
| 025 | R3 contract-lock note (FR-19) | 2 | 🔲 | 024,021,022 | sonnet/high | MINIMAL | — |
| 030 | Characterization tests — dispatch (FR-07 pre) | 3 | ✅ | none | sonnet/high | STANDARD | — |
| 031 | Layer-A action seam behind executors (FR-07) | 3 | ✅ | 030 | opus/xhigh | FULL | — |
| 032 | "What lights up" audit (FR-14 pre) | 3 | ✅ | 031 | opus/high | STANDARD | — |
| 033 | Notification leg flip (FR-14) | 3 | ✅ | 032 | opus/high | FULL | — |
| 040 | comms_assessed producer (FR-11) | 4 | 🔲 | 031,024 + email-r4-W10 | opus/high | FULL | — |
| 041 | Comms policy layer (FR-12) | 4 | 🔲 | 040 | opus/xhigh | FULL | — |
| 042 | RI actions via seam + mirror (FR-13) | 4 | 🔲 | 040,041,012,020 | opus/high | FULL | — |
| 050 | Suggestion producer — grounded+gated (FR-15) | 5 | 🔲 | 012,013,042 | opus/high | FULL | — |
| 051 | Suggestion renderer branch (FR-16) | 5 | 🔲 | 050,021 | sonnet/high | FULL | — |
| 052 | Suggestion dispatch parity (FR-17) | 5 | 🔲 | 051,031 | opus/high | FULL | — |
| 090 | Project wrap-up | 6 | 🔲 | ALL | opus/high | FULL | ❌ main-session |

## Dependency DAG (critical path)

```
001 (spike ─ HARD GATE for Phase 2)
 ├─ 010 (ADR-047)
 └─→ 020 ─┬─ 021 ─┐
          ├─ 023   ├─ 025 (R3 unblock)
011 ─ 012 ┼─ 022 ─┘
          └─ 024 ─┘
013 ──────┘

030 ─ 031 ─ 032 ─ 033              (Layer A + flip; 031 needs 030)
        └──────────────→ 040 (needs 031 + 024 + email-r4 W10) ─ 041 ─ 042
                                                                        └─ 050 ─ 051 ─ 052
090 ← everything
```

**Longest chain (critical path)**: 001 → 020 → 024 → 040 → 041 → 042 → 050 → 051 → 052 → 090.
**Earliest R3 unblock**: 001 → {020,012,013} → 024 → 025 (Phase 2 delivers the `communication-arrived` contract before the heavier Layer-A extraction).

## Parallel Execution Plan

> MAX 6 agents/wave. `.claude/`-touching tasks (010, 090) are main-session-only. Build-verify between waves.

**Phase 0**
- Wave 0 (sequential, 1): **001** — HARD go/no-go gate. STOP after; human reviews the spike decision before Phase 2. — goal-eligible: **NO** (spike/decision, judgment gate).

**Phase 1** (Layer B + contract — Layer B is independent of the spike; ADR-047 needs the mode decision)
- Wave 1 (parallel, 2 agents): **011, 013** — prereq: none — goal-eligible: **NO** (only 2 tasks; 011 is Dataverse-schema/irreversible).
- Wave 2 (sequential, 1): **012** — prereq: 011.
- Wave 3 (sequential, 1): **010** — prereq: 001 — main-session (.claude/ + docs/adr/).

**Phase 2** (BLOCKED until 001 resolves go)
- Wave 4 (sequential, 1): **020** — prereq: 001,012,013 (SignalR infra + auth negotiate).
- Wave 5 (parallel, 3 agents): **021, 022, 023** — prereq: 020 (022 needs only 012; 023 needs 020) — files disjoint (client lib / poll endpoint / targeting) — goal-eligible: **NO** (023 is security-critical; must surface named sign-off, not auto-loop).
- Wave 6 (sequential, 1): **024** — prereq: 012,013,020 — coordinate email-r4/messaging-r3 merge-order.
- Wave 7 (sequential, 1): **025** — prereq: 024,021,022 (doc deliverable).

**Phase 3** (Layer A + flip — can begin in parallel with Phase 2 once Phase 1 done; 030 has no deps)
- Wave 8 (sequential, 1): **030** — characterization first.
- Wave 9 (sequential, 1): **031** — prereq: 030 — highest blast radius (brownfield extraction).
- Wave 10 (sequential, 1): **032** — prereq: 031 — audit doc, reviewed before flip.
- Wave 11 (sequential, 1): **033** — prereq: 032 — the ADR-043 Path-C flip.

**Phase 4** (gated on email-r4 W10 merge + Phase 3)
- Wave 12 (sequential, 1): **040** — prereq: 031,024 + email-r4-W10.
- Wave 13 (sequential, 1): **041** — prereq: 040 — rule-store decision (Binding vs table).
- Wave 14 (sequential, 1): **042** — prereq: 040,041,012,020.

**Phase 5**
- Wave 15 (sequential, 1): **050** — prereq: 012,013,042.
- Wave 16 (sequential, 1): **051** — prereq: 050,021 — frontend (ADR-021 dark mode).
- Wave 17 (sequential, 1): **052** — prereq: 051,031 — dispatch parity seam test.

**Phase 6**
- Wave 18 (sequential, 1): **090** — prereq: ALL — main-session (final gates + ADR-047 full + cleanup).

> **Why so few parallel waves**: the spine is a deeply sequential dependency chain (store → delivery → producers → consumers). The genuine parallelism is Wave 1 (011‖013) and Wave 5 (021‖022‖023). Everything else is critical-path serial — parallelizing would create file-overlap on `Services/Notifications/**` and the shared persist path.

## Cross-Project Coordination (from hot-path overlap analysis)

- **email-communication-solution-r4** — owns `Services/Communication/**` until W10 merges. Tasks **024, 040** are BLOCKED until email-r4 W10; run `/conflict-check` before every BFF PR.
- **messaging-communication-app-r3** — consumes `communication-arrived`; its task 045 unblocked by **025**. Coordinate merge-order at their Phase-1.
- **spaarkeai-assistant-enhancements-r1** — R1.5 absorbed here (Phase 5). Avoid a second push channel.
- **spaarke-ai-architecture-redesign-r2** — owns `Services/Ai/` internals; **031** consumes `PublicContracts` seams, does NOT fork.
- **spaarke-daily-update-service-r5** — coordinate the Daily-Briefing producer (**050**).

## How to Execute

1. Verify all prerequisites are ✅ before starting a wave.
2. For a parallel wave: ONE message with MULTIPLE `task-execute` Skill invocations (≤6).
3. Dispatch each task's subagent at its `<model-tier>` + `<effort>`.
4. Build-verify between waves (`dotnet build src/server/api/Sprk.Bff.Api/` for `.cs` changes).
5. `.claude/`-touching tasks (010, 090) run in the MAIN SESSION only.
6. **Do NOT auto-run past 001** — the FR-01 spike is a human go/no-go checkpoint.
