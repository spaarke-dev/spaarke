# Notification & Action Spine — Cross-Project Consumption Report

> **Status**: The spine (ADR-047, Layers A–D) is **merged to master** (`f1236e269`, 2026-07-22) — Phases 1–5 complete (tasks 001–052). Only the project wrap-up (090) remains.
> **Purpose**: What each related project needs to know now that the spine is on master, plus the runtime/ops prerequisites to light it up.
> **Author**: `spaarke-notification-spine-r1` (pre-090 coordination).
> **Companion**: [`notification-spine-contract-lock.md`](notification-spine-contract-lock.md) (the FR-19 envelope contract lock for messaging-r3).

---

## TL;DR per project

| Project | What changed | Action required |
|---|---|---|
| **messaging-communication-app-r3** | Its **FR-22 notification awareness (task 045) was ⛔ blocked on "spine not in master" — now UNBLOCKED.** | Consume `communication-arrived` via `@spaarke/notifications` (below). **Do NOT wire your own producer.** Verify your envelope type mirror matches the shipped 9-field `CommunicationEnvelope`. |
| **spaarkeai-assistant-enhancements-r1** | Its **R1.5 proactive push (designed, not decomposed) is now SHIPPED here** — suggestion renderer + suggestion action. | Do NOT build a second proactive-push channel. Produce proactive suggestions via the `kind=suggestion` producer pattern (grounded+gated → outbox). |
| **spaarke-daily-update-service-r5** | A `DailyBriefingSuggestionProducer` sibling now runs on the Daily-Briefing render leg. | If you touch `Services/Ai/Narrators/DailyBriefing*`, the producer is a **sibling** (narrator untouched, Null peer). Enable per-env with `Notifications:Suggestions:Enabled=true`. |
| **email-communication-solution-r4**, **messaging-r1/r2** | The comms producers (024/040/041/042) emit from the `Services/Communication` persist path. | Preserve the producer emit points on any `Services/Communication` change (the 5 `communication-arrived` emit sites are AFTER participant-index, not raw create). |
| **spaarke-ai-architecture-redesign-r2**, **spaarkeai-compose-r3/r4** | Layer-A `IActionSeam` (`Services/Ai/PublicContracts/`) on master; **SpaarkeAi builds clean from tip**. | Consume the seam, don't fork `Services/Ai/`. The flagged `@spaarke/notifications` build break is **not present** (verified `npm run build` exit 0) — SpaarkeAi is deployable from master. |
| **Any client surface** | `@spaarke/notifications` client + `INavigationService.openRecordModal` are shipped shared libs. | Reuse them (below) rather than building a per-surface subscriber or a navigate-away record open. |

---

## 1. How to CONSUME a notification (any client surface)

The spine is host-agnostic. A client subscribes through the ONE shared library — never a per-surface SignalR connection.

```ts
import { getNotificationsClient } from ".../services/notificationsBootstrap"; // one client per host

// Register a kind handler (returns an unregister fn). Do this BEFORE start().
getNotificationsClient().registerHandler("communication-arrived", (event) => {
  // event = { outboxRowId, kind, envelope?, source: "live" | "poll" }
  // LIVE push is signal-only (no envelope) — re-fetch detail from the BFF (below).
});
await getNotificationsClient().start(); // negotiate → connect → poll-fallback on failure
```

- **Kinds** (closed taxonomy): active = `suggestion` · `communication-assessed` · `communication-arrived`; reserved = `job-complete` · `share` · `system-alert`.
- **Live pushes are signal-only** (NFR-02/03 — no body/content/token on the wire). To get the envelope, either read `event.envelope` when `source === "poll"`, or re-fetch:
  ```ts
  // GET /api/notifications/pending[?kind=communication-arrived] → { items: [{ outboxRowId, kind, envelope }] }
  // oid-scoped, read-time expiry-filtered server-side. This is also the degrade path when SignalR is down.
  ```
- **Acting on a notification** MUST re-fetch/re-ground through the auth-checked BFF at action time — the envelope is never sufficient to perform the action.

## 2. Envelope contracts (the wire shapes on master)

**`CommunicationEnvelope`** (9 fields — used by `communication-arrived` + `communication-assessed`):
`kind, communicationId, threadId, channel, direction, regardingRecordId, senderDisplay, snippet?, badgeDelta`.

**`SuggestionEnvelope`** (9 fields — used by `suggestion`):
`kind, suggestionId, source, regardingRecordId, regardingRecordType, title, snippet?, actionHint, expiresAt`.
> `regardingRecordType` was **added by task 052** so acting on a suggestion can open the regarding record. **No impact on messaging-r3** (it mirrors `CommunicationEnvelope`, not `SuggestionEnvelope`).

TypeScript mirrors live in `@spaarke/notifications` `types.ts` — copy from there, do not re-derive.

## 3. messaging-communication-app-r3 — task 045 is unblocked

- The spine is on master; `communication-arrived` is emitted by the ONE spine-owned producer (task 024) from the Communication persist path. **R3 consumes only** — wiring your own `communication-arrived` producer is a hard MUST NOT (ADR-047 / FR-19).
- Subscribe via `getNotificationsClient().registerHandler("communication-arrived", …)`; degrade path `GET /api/notifications/pending?kind=communication-arrived`.
- Confirm your envelope type mirror = the shipped 9-field `CommunicationEnvelope` (the 025 contract-lock note already flagged the 9th field `regardingRecordId`).

## 4. Proactive suggestions — the producer pattern (assistant-enhancements-r1, daily-update-r5)

To emit a proactive suggestion, follow `DailyBriefingSuggestionProducer` (`Services/Ai/Narrators/`):
1. **Ground** each candidate (ADR-039 — real EntityType + parseable record id + name).
2. **Gate** (ADR-041 `origin=proactive`) — a declared-metadata admit decision (a `*GateOptions` policy dial), NOT the chat/Redis `PendingPlanManager` machinery.
3. Both pass → **one** `kind=suggestion` outbox row via `OutboxService.WriteAsync` (store BEFORE ping) → best-effort `SignalRDeliveryService.PingUserAsync`.
- Deny-by-default: ship with the gate `Enabled=false`.
- The suggestion renderer (SpaarkeAi) + "acting opens the regarding record in a modal" (task 052) are already built — you get the consumer for free.

## 5. Shared-lib reuse (all client projects)

- **`@spaarke/notifications`** — `NotificationsClient` + `getNotificationsClient()` singleton (negotiate → connect → kind-route → poll fallback). One client per host.
- **`INavigationService.openRecordModal(entityName, entityId)`** (NEW, optional) — opens a record as a **modal** (Layout 1: `navigateTo` entityrecord, `target: 2`, 85% × 85%), distinct from `openRecord` (`openForm`, navigate-away). Use it whenever a Fluent v9 surface opens a record without losing its own state.

## 6. Runtime / ops prerequisites (NOT code — needed to light up per environment)

| Prerequisite | Effect if absent |
|---|---|
| Azure SignalR resource (Serverless) + `Notifications:SignalR:ConnectionString` (Key Vault, ADR-027/028) | No live push — the client silently runs on the poll fallback (graceful, ADR-032 null-object). |
| CSP `connect-src` allows `wss://*.service.signalr.net` in the Power Platform env | Live SignalR silently fails → poll fallback. Verify at provisioning. |
| Backfill `systemuser.sprk_isexternal` (two-option, default No) | Fan-out targeting **fail-closes to zero recipients** for un-backfilled users (correct-but-silent). |
| `Notifications:Suggestions:Enabled=true` | No proactive suggestions are produced (deny-by-default, NFR-03 kill-switch). |

## 7. Open items handed off / tracked

- **023 R-5 fan-out security sign-off** — a NAMED HUMAN must sign `notes/023-fanout-security-signoff.md` before fan-out is trusted in production.
- **ISS-001 / #674** — read-path internal/external fix handed to messaging-r3 (`CommunicationThreadReadService` hardcoded `IsInternalUser:true`).
- **DEF-001 / #673** — consolidate the 6 ad-hoc oid↔systemuserid copies onto `ISystemUserIdentityResolver`.
