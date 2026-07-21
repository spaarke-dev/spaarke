# Current Task State — spaarke-notification-spine-r1

> **Last Updated**: 2026-07-21 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarke-notification-spine-r1` @ `c759dd4c8` (pushed, clean).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **031 — Layer-A action seam behind `*NodeExecutor.cs` (Phase 3, Wave 9)** — NOT started |
| **Step** | — (030 ✅ complete: 14 seam tests green, both quality gates CLEAN, zero production changes) |
| **Status** | ready-for-031 (opus/xhigh, FULL rigor, highest blast radius — brownfield extraction) |
| **Next Action** | **Dispatch 031** via `task-execute` on `tasks/031-layer-a-action-seam.poml`. Its DoD: the 14 task-030 seam tests pass **UNMODIFIED** after the extraction (if any must change → the extraction altered observable behavior → STOP/reconcile, don't "fix" the test). |

### Task 030 result (Phase 3, Wave 8 — ✅ DONE 2026-07-21)
- **14 seam tests** authored under `tests/integration/seam/Ai/Nodes/` (CreateNotification 8 / CreateTask 3 / UpdateRecord 3) — the behavior-neutrality safety net for 031. All green vs pre-031 code; `git diff` on the three executors empty.
- Both Step 9.5 gates CLEAN (code-review + adr-check — mandatory per TEST-MODIFYING override). ADR-038 seam KEEP-path, no banned shapes.
- Notes + escalation-trigger disposition: `notes/030-characterization-notes.md` (trigger correctly did NOT fire — 3 defect-adjacent paths are documented intentional contracts).
- ⚠️ **031 contract**: those 14 tests are 031's DoD — they must pass unmodified after the Layer-A extraction.

### Files Modified This Session
All committed + pushed (6 commits `672ec11f6`→`c759dd4c8`). Nothing uncommitted except this file.

### Critical Context (for the fresh session)
The delivery half of the notification spine is built + committed. **Two things need YOUR action, not code:** (1) a **named human R-5 security sign-off** for the fan-out (`notes/023-fanout-security-signoff.md` — fan-out is now correct/load-bearing); (2) the **read-path leak** (`CommunicationThreadReadService` hardcodes `IsInternalUser:true`) is handed to **messaging-r3 (issue #674)** — you said "that has been communicated." Backfill `systemuser.sprk_isexternal` for the 10 users (in progress, manual).

---

## Full State (Detailed)

### What's DONE (all committed + pushed)

| Task | What | Notes |
|---|---|---|
| **001** | FR-01 SignalR footprint spike | **GO / Serverless** (`Microsoft.Azure.SignalR.Management` 1.33.1, in-BFF; +0.30 MB, 0 new HIGH CVE). `notes/spikes/fr-01-signalr-footprint.md`. |
| **010** | ADR-047 (concise + full) | Notification & Action Spine; Proposed; claims the ADR-046/048-reserved number. |
| **011** | `sprk_notificationoutbox` table | Created **live in spaarkedev1** (Dataverse MCP). kind=text, envelope=Memo4000, ADR-024 minimal regarding, native ownerid. |
| **012** | `OutboxService` | Write/GetPending/Dismiss + read-time expiry; unconditional DI; seam test (interpreting-fake boundary). |
| **013** | Envelope contract | `NotificationKind` closed enum + Communication/Suggestion envelopes; 33 tests. |
| **020** | SignalR delivery + negotiate | Serverless `SignalRDeliveryService` (PingUser/PingGroup, signal-only), `POST /api/notifications/negotiate` (oid from JWT), ADR-032 null-object. Seam 8/8. |
| **021** | `@spaarke/notifications` client lib | negotiate→connect→kind-router→poll fallback; 26/26; SpaarkeAi wire-in. |
| **022** | `GET /api/notifications/pending` | SignalR-agnostic degrade path; oid→systemuserid via resolver. Seam 6/6. |
| **023** | `CommunicationFanOutTargetingService` | Recipients from record security (junction + access filter + fail-closed grant gate); **now uses authoritative `sprk_isexternal` flag**. Seam 7/7. |
| **identity fix** | `ISystemUserIdentityResolver` | systemuserid↔oid (cached, fail-open) + `IsExternalAsync` (sprk_isexternal, fail-closed). `Services/Identity/`. |

### Applicable ADRs / contracts the NEXT tasks must honor
- **Producers ping by Dataverse `systemuserid`** (outbox `OwnerId`); `PingUserAsync` resolves systemuserid→oid internally. Producers never touch oid.
- **Fan-out (023) fail-closes to ZERO** unless the producer projects `sprk_isinternalonly` + `createdon` (message) + `sprk_privacystate` (thread).
- **Outbox BEFORE ping** (store-before-render, ADR-041/043). Spine is dumb transport (IDs + display metadata only; NFR-02/03).
- **Layer A (Phase 3)**: extract the seam BEHIND `*NodeExecutor.cs` (ADR-013 PublicContracts); characterization tests pin the chat path FIRST (task 030). `031` = highest blast radius.
- **033 = ADR-043 Path C**: flip Notification `Routable=false`→true via `DispositionRoutability` + add the matching `OutputRouter` switch leg in the SAME change (both must land together or it throws).

### Runtime / ops still needed (not code)
- Azure SignalR resource + `Notifications:SignalR:ConnectionString` (Key Vault, ADR-027/028) in spaarkedev1 for LIVE push; absent ⇒ null-object/poll only.
- Verify `wss://*.service.signalr.net` in the target Power Platform env CSP (else silent poll fallback).
- Backfill `systemuser.sprk_isexternal` (10 users, manual — in progress).

### Open items / tracked
- **023 R-5 sign-off**: NAMED HUMAN required — `notes/023-fanout-security-signoff.md` (fan-out resolved; read-path pending r3).
- **ISS-001 / #674**: read-path internal-only fix — handed to messaging-r3; `notes/HANDOFF-messaging-r3-internal-only-readpath.md`. CI guard test (ban literal `IsInternalUser:true`) deferred to that hand-off.
- **DEF-001 / #673**: consolidate 6 ad-hoc oid↔systemuserid copies onto `ISystemUserIdentityResolver`.

### Blockers
- **024** (communication-arrived producer) + **040** (comms-assessed) BLOCKED on **email-r4 W10 merge** (owns `Services/Communication/**` persist path). **025** (R3 contract-lock) needs 024.
- Phase 3 (030→033) is **NOT** blocked by email-r4 → the unblocked path forward.

### Cross-project coordination
- **messaging-r3 (PR #664)**: consumes `communication-arrived` (its task 045 needs our FR-19 contract lock via 025); ALSO owns `CommunicationThreadReadService.cs` (read-path hand-off #674). `/conflict-check` before editing any `Services/Communication/**` file.
- **email-r4**: owns `Services/Communication/**` until W10.

### Decisions Made (this session)
- 2026-07-21: GATE 001 = GO/Serverless; Wave 1/Phase 1/Phase 2 Layer C executed via parallel sub-agents; identity keyed systemuserid (producers) ↔ oid (SignalR) via shared resolver; **internal/external now via authoritative `systemuser.sprk_isexternal` (two-option, default No), NOT the systemuser proxy** (owner: external users can be licensed systemusers); read-path fix handed to messaging-r3 (conflict with PR #664).

---

## Recovery Instructions
1. Read Quick Recovery above.
2. To continue: **"work on task 030"** (Phase 3 start) → invokes `task-execute` on `tasks/030-characterization-tests-dispatch.poml`.
3. Reference: `tasks/TASK-INDEX.md` (status), `plan.md` (waves), `CLAUDE.md` (constraints).
4. Before any `Services/Communication/**` edit: `/conflict-check` (messaging-r3 active).
