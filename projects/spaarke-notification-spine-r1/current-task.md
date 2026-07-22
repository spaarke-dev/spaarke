# Current Task State — spaarke-notification-spine-r1

> **Last Updated**: 2026-07-21 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarke-notification-spine-r1` @ `e5f3e2174` — **pushed + MERGED TO MASTER** (0 behind / 0 ahead / 0 unpushed). Phases 1-3 (tasks 001-033) are live on master. Task 024 investigation done; implementation pending (fresh context recommended).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **024 — communication-arrived producer (Phase 2, Wave 6) — IN PROGRESS: investigation done, implementation pending** |
| **Step** | Step 0-2 done (rigor FULL/opus; conflict-check clean; **persist-site sweep complete**). Next: Step 3 (implement producer) → 4 (wire 5 sites) → 5 (seam test). |
| **Status** | **UNBLOCKED** — email-r4 W10/11/12 merged to master; our branch synced (0 behind master @ e5f3e2174). messaging-r3 #664 touches 0 persist files → no overlap. Escalation trigger CLEARED. |
| **Next Action** | Implement `CommunicationArrivedProducer` then wire at the **5 enumerated persist sites** (below). Recommended: fresh context — this is a hot-path multi-file task and this session is very deep. |

### 024 investigation (DONE — the de-risking part)
- **5 `sprk_communication` row-create persist sites (merged HEAD, authoritative)**:
  1. `IncomingCommunicationProcessor.cs:576` — inbound capture (email)
  2. `Channels/MessagingIngestor.cs:220` — inbound capture (messaging)
  3. `CommunicationService.cs:775` — outbound send variant A (entity built at :735)
  4. `CommunicationService.cs:1670` — outbound send variant B (entity built at :1603)
  5. `CommunicationService.cs:1775` — outbound send variant C (entity built at :1732)
  - EXCLUDED (not communication rows): threads (DirectThreadAccessService:89, ThreadResolver:156/468, MessagingThreadKeyStrategy:71), participants (CommunicationParticipantIndexer:104), attachment docs (CommunicationService 388/1972/2095/2140, IncomingCommunicationProcessor 790/824/903, MessageAttachmentMaterializer 151/163), and the read/update site (CommunicationThreadReadService:771 — has an id, it's an update).
- **Producer design** (per POML + spec FR-09/NFR-05/08): build a **task-013 `CommunicationEnvelope`** (kind=communication-arrived; IDs + minimal display metadata only — communicationId/threadId/channel/direction/senderDisplay/optional privacy-gated snippet/badgeDelta; NO body) → **`OutboxService.WriteAsync`** (durable truth FIRST) → **`CommunicationFanOutTargetingService`** (task 023) to compute recipients → **`SignalRDeliveryService` ping** (best-effort, requires outboxRowId). Wrap the whole emit **fire-and-forget non-fatal**, mirroring `CommunicationEnrichmentService.RunAssessmentEmissionAsync` (~lines 216-238) — a producer exception MUST NOT fail the persist call (NFR-05).
- **Files to read before implementing**: `Services/Notifications/Envelopes/*` (013 CommunicationEnvelope shape), `OutboxService.cs` (WriteAsync signature), `SignalRDeliveryService.cs` (ping signature — bottom half), `CommunicationFanOutTargetingService.cs` (023 targeting API), `CommunicationEnrichmentService.cs:~216-238` (fire-and-forget precedent), and each of the 5 sites' surrounding method for available locals (communicationId, thread, direction, channel, sender).
- **Placement decision (POML step 3)**: producer needs Communication read (envelope fields) + Notifications (outbox + delivery) → likely `Services/Notifications/CommunicationArrivedProducer.cs` injected into the Communication persist path, OR `Services/Communication/`. Decide per dependency shape; state Placement Justification (§10).
- **DoD**: seam test under `tests/integration/seam/Communication/` proving BOTH channels (email + message) each yield an outbox row (kind=communication-arrived) + a ping, AND a producer exception does NOT fail the persist. Outbox-before-ping ordering asserted.

### 033 Quality Gates (Step 9.5) — both CLEAN
- **code-review**: 0 Critical / 0 Warning / 1 informational (OutputRouter.cs 523 lines >300 — cohesive Email-mirror leg, don't split). ADR-013 facade discipline confirmed; NFR-07 identifiers-only logging.
- **adr-check**: 0 violations. ADR-043 (through-the-registry Path C), ADR-013 (IActionSeam facade), ADR-040 (store-before-render), ADR-010 (no new interface; 5 ctor params <7), ADR-032, ADR-038 all compliant. §10 Placement Justification: existing-leg realization, no package, 46.06 MB, 0 new HIGH CVE.
- **Tests**: full BFF suite 8781 passed / 0 failed / 101 skipped. Targeted: 46 disposition/router + 81 dispatch/ActionSeam.

### Files Modified This Session (033)
- `src/server/api/Sprk.Bff.Api/Services/Ai/DispositionRoutability.cs` — Notification entry `Routable=false→true`, removed NotRoutableReason (+ audit-cite comment).
- `src/server/api/Sprk.Bff.Api/Services/Ai/OutputRouter.cs` — added `IActionSeam? actionSeam=null` ctor param (last, optional → DI auto-injects Singleton); `case BindingDisposition.Notification` → `CreateNotificationViaSeamAsync` (parses `notification` envelope, calls seam, loud on `!Success`/missing envelope, `Skipped`=no-op); updated 2 doc comments.
- `tests/integration/seam/Ai/DispositionRoutabilityNotificationSeamTests.cs` — NEW: 3 tests (admit⇔route⇔store happy path; seam-rejects-content→loud-after-store; missing-envelope→loud).
- `tests/integration/seam/Ai/DispositionRoutabilitySeamTests.cs` — removed Notification from not-routable Theory; added it to `Registry_RoutableSet_IsExactlyTheRealizedLegs`.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/OutputRouterTests.cs` — removed Notification from `RouteAsync_..._ThrowsLoudNotSupported` Theory (now routes).
- No DI edit needed (single ctor; Singleton IActionSeam auto-resolved). DispositionRoutability/OutputRouter were byte-identical to origin/master (conflict-check pass).

### 032 result (Phase 3, Wave 10 — ✅ DONE 2026-07-21)
- **Outcome: nothing lights up.** Live `spaarkedev1` `sprk_playbookconsumer` catalog has **0** rows with `sprk_disposition=notification` (targeted query `[]`; full census = 28 Bindings: 12 Informational + 7 null + 4 SurfaceLaunch + 3 Compose + 1 Email + 1 WorkProduct + **0 Notification**). Code/seed scan agrees (only enum def + registry entry + option-set def + test fixtures — no Binding).
- **Two notification mechanisms distinguished**: Path 1 = `CreateNotificationNodeExecutor` (ActionType 50, playbook-node, writes appnotification TODAY, NOT gated by registry — e.g. daily-update-service playbooks) is **out of scope**; Path 2 = Binding `disposition=notification` → OutputRouter (the flip target) has **zero rows**.
- **Both escalation triggers cleared** (no risky Binding; no per-Binding-granularity dilemma). No ADR-043 §6.5 tension raised.
- **Recommendation**: immediate flip SAFE; no Binding remediation. 033 MUST (1) land OutputRouter leg in same change as registry flip, (2) add `Notification` admit⇔route⇔store seam test. Forward-looking guard: the FIRST future notification Binding lights up with no further registry gate → re-run NFR-02/03 content check when authoring it (033 Trigger 1 already halts on any unanticipated routed capability).
- Deliverable: `notes/what-lights-up-audit.md` (STANDARD rigor; quality gates skipped — docs-only, no code/tests modified).

### 031 result (Phase 3, Wave 9 — ✅ DONE 2026-07-21)
- Session-agnostic Layer-A seam extracted behind the 3 node executors: 3 cores (`Services/Ai/Nodes/ActionCore/*`) + ADR-013 facade (`Services/Ai/PublicContracts/{IActionSeam,ActionSeam}.cs`) + DI (`AnalysisServicesModule.AddNodeExecutors`, unconditional Singleton). Executors refactored to delegate; **constructors byte-identical**.
- **All 8 criteria met**; 030 characterization + existing unit tests pass ZERO-edit; **full BFF suite 8780/0**; publish 46.05 MB; both Step 9.5 gates clean. Notes: `notes/031-layer-a-seam-notes.md`.
- **`IActionSeam` is now available** for Phase 4/5 producers (024/040/050) — they consume it, never the executors or a synthetic `NodeExecutionContext`.
- 033 will touch `DispositionRoutability.cs` + `OutputRouter.cs` (left untouched here per criterion 6).

### 031 design (locked — from POML prescriptive steps 2–4)
- Three `internal sealed` cores under `Services/Ai/Nodes/ActionCore/`: `NotificationActionCore` (+`BuildNotificationEntity`/idempotency moved; `context.RunId`→`correlationId` param, `"playbook"`→`source` param), `TaskActionCore`, `UpdateRecordActionCore` (+coercion helpers + `FieldCoercionException` moved). Each ctor: boundary service (`IGenericEntityService` / `IFieldMappingDataverseService`+`IServiceScopeFactory`) + `ILogger` (base). Input = typed request records; NO `NodeExecutionContext`.
- Executors keep template rendering + ConfigJson parse + NodeOutput wrapping; delegate build+create to a core built inline from their EXISTING injected fields → **constructors frozen** (criterion 4). `ResolveViaMatterMemberships` stays in the executor (reads `PreviousOutputs`).
- `IActionSeam`+`ActionSeam` in `PublicContracts/`; DI mirror the `IBriefingAi` site (`AnalysisServicesModule.cs`) but **unconditional** (record-create is not feature-gated → no NullActionSeam).
- **DoD / escalation**: 030 seam tests + `CreateTaskNodeExecutorTests`/`UpdateRecordNodeExecutorTests` pass with ZERO edits. If a test needs editing → extraction changed behavior → STOP/escalate (root §6), don't edit the test. If frozen-ctor is structurally impossible without dup → STOP (ADR-013/§11 extend-vs-fork tension).
- Conflict-check: master +57 commits but the 3 executors + PublicContracts + OutputRouter + DispositionRoutability + DispatchSessionEndpoint are byte-identical to our branch → no rebase needed; do NOT touch OutputRouter/DispositionRoutability/DispatchSessionEndpoint (task 033's scope).

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
