# Spaarke Notification & Action Spine — Architecture & Component Model

> **Status**: Shipped (Layers A–D on master, `spaarke-notification-spine-r1`, 2026-07-22).
> **⚠️ 2026-08-20 correction**: the SpaarkeAi proactive-**suggestion renderer** (tasks 051/052) was later **removed** by `spaarkeai-assistant-enhancements-r2` (FR-E1). The spine BACKEND is fully live and `suggestion` rows are still produced, but **nothing renders them** today; the one live UI consumer is `communication-arrived`. The suggestion surface is being rebuilt as **OOB Dataverse notifications** in [`spaarke-notification-spine-r2`](../../projects/spaarke-notification-spine-r2/README.md). See §Consumers + Flow B.
> **Decision record**: [ADR-047](../adr/ADR-047-notification-action-spine.md) — the binding MUST/MUST-NOT rules. **This doc is the component model** (what is actually built, how the pieces fit, how to consume/extend); it does not restate the ADR's rules.
> **Data model**: [`sprk_notificationoutbox`](../data-model/sprk_notificationoutbox.md).
> **User guide**: [NOTIFICATIONS-AND-SUGGESTIONS-USER-GUIDE.md](../guides/NOTIFICATIONS-AND-SUGGESTIONS-USER-GUIDE.md).

---

## 1. What it is (one paragraph)

The spine is the ONE server-initiated **typed-signal → grounded-action → delivery** path for every Spaarke client surface. A **producer** grounds and gates a typed signal, writes a durable `kind`-typed **outbox row** (the source of truth), then best-effort **pings** the recipient over Azure SignalR; a **host-agnostic client library** routes the ping by `kind` and the consumer **re-fetches the detail** through the auth-checked BFF. The spine is **dumb transport** — all judgment lives in producers, and there is no second push/delivery/action path anywhere in the platform.

```
 PRODUCER (Layer D)          LAYER B (durable)         LAYER C (delivery)              CONSUMER (client)
 ground + gate a signal  →   OutboxService.Write   →   SignalRDeliveryService.Ping  →  NotificationsClient
 (ADR-039 / ADR-041)         (sprk_notificationoutbox) (best-effort, may be null)      registerHandler(kind)
        │                          │  store BEFORE ping           │  signal-only (no body)      │
        │                          └──────────────────────────────┘                             │
        │                                    ▲                        GET /api/notifications/pending (poll fallback + re-fetch)
        └── may realize a domain action via Layer A (IActionSeam) ────────────────────────────────┘
```

---

## 2. The four layers + shipped components

### Layer A — session-agnostic action seam (`Services/Ai/PublicContracts/`)
Domain actions (create a notification, create a task, update a record) are realized through ONE seam extracted **behind** the chat node executors, so a comms-RI action or a suggestion action re-enters the SAME dispatch path chat uses (ADR-013 facade; no per-consumer action logic).

| Component | Responsibility |
|---|---|
| `Services/Ai/PublicContracts/IActionSeam.cs` / `ActionSeam.cs` | `CreateNotificationAsync` · `CreateTaskAsync(CreateTaskRequest)` · `UpdateRecordAsync`. Registered **Singleton**. |
| `Services/Ai/Nodes/ActionCore/{NotificationActionCore,TaskActionCore,UpdateRecordActionCore}.cs` | Session-agnostic cores the executors delegate to (extracted by task 031; executor ctors frozen). |

### Layer B — durable, kind-typed outbox (`Services/Notifications/`)
The source of truth. A row is written BEFORE any ping; expiry is a read-time filter (no sweep).

| Component | Responsibility |
|---|---|
| `sprk_notificationoutbox` (Dataverse) | `sprk_kind` (text) · `sprk_envelope` (Memo 4000) · minimal ADR-024 regarding (`sprk_regardingrecordid` / `sprk_regardingrecordtype`) · native `ownerid` · `sprk_expireson`. |
| `Services/Notifications/OutboxService.cs` | `WriteAsync<TEnvelope>(userId, kind, envelope, regardingId, regardingType, expiresAt) → Guid` · `GetPendingAsync(userId)` (read-time expiry filter) · `DismissAsync`. Unconditional DI (no kill-switch on "persist the pending row"). |
| `Services/Notifications/Envelopes/NotificationKind.cs` (+ `NotificationKindJsonConverter`) | Closed enum, kebab-case wire; unknown token **fails to deserialize**. |
| `Services/Notifications/Envelopes/CommunicationEnvelope.cs` · `SuggestionEnvelope.cs` | The two active envelope shapes (9 fields each). `Validate()` enforces the kind. IDs + minimal display metadata only (NFR-02/03). |

### Layer C — delivery + the host-agnostic client (`Services/Notifications/`, `Api/Notifications/`, `@spaarke/notifications`)
Azure SignalR **Serverless** mode, hosted in the BFF (FR-01 spike: +0.30 MB, 0 new HIGH CVE). Signal-only pushes; a poll endpoint is the shared degrade path.

| Component | Responsibility |
|---|---|
| `Services/Notifications/SignalRDeliveryService.cs` | `PingUserAsync(outboxRowId, recipientSystemUserId, kind)` (virtual; outbox-before-ping structural) · `PingGroupAsync`. Signal-only. ADR-032 null-object when SignalR disabled. |
| `Api/Notifications/NotificationsEndpoints.cs` | `POST /api/notifications/negotiate` (oid-scoped token from JWT) · `GET /api/notifications/pending[?kind=]` (oid-scoped, expiry-filtered; the poll fallback AND the re-fetch surface) · `POST /api/notifications/{outboxRowId:guid}/dismiss` (owner-scoped; stamps `sprk_dismissed` on ONE of the caller's own pending rows; 404 on not-owned/already-dismissed — ADR-028 no cross-user writes). |
| `Services/Identity/SystemUserIdentityResolver.cs` (`ISystemUserIdentityResolver`) | systemuserid ↔ oid (cached, fail-open) + `IsExternalAsync` (authoritative `sprk_isexternal`, fail-closed). Producers key by systemuserid; SignalR resolves oid internally. |
| `@spaarke/notifications` (`src/client/shared/Spaarke.Notifications/`) | `NotificationsClient` (negotiate → connect → kind-route → poll fallback) · `types.ts` (wire mirrors) · `negotiate.ts` · `kindRouter.ts` · `pollFallback.ts`. The ONE client, host-agnostic. |

### Layer D — per-source producers (grounding + gating; NEVER in the spine)
Each producer owns its judgment and writes to Layer B. Fan-out targeting derives from record security.

| Producer / helper | Signal | Notes |
|---|---|---|
| `Services/Communication/CommunicationArrivedProducer.cs` (024) | `communication-arrived` | Emits at 5 orchestration points AFTER participant-index; per-recipient outbox + ping. |
| `Services/Communication/ICommunicationAssessedProducer.cs` + `CommunicationEnrichmentService` step-5 emit (040) | `communication-assessed` | Fire-and-forget non-fatal seam; interim logging default. |
| `Services/Communication/CommunicationRuleGate.cs` (041) | — (policy) | Reads `sprk_communicationrule`; authorize ⇔ confidence ≥ threshold; privilege FLAGGED never auto-decided (ADR-015); fail-closed DENY. |
| `Services/Communication/CommunicationRiActionService.cs` (042) | `communication-assessed` action | On authorize: Layer-A `CreateTaskAsync` → outbox → ping → appnotification mirror. Non-fatal. |
| `Services/Ai/Narrators/DailyBriefingSuggestionProducer.cs` (050) | `suggestion` | Sibling of the narrator (Null peer). Grounding (ADR-039) + proactive gate (ADR-041, `SuggestionGateOptions`, deny-by-default). |
| `Services/Communication/CommunicationFanOutTargetingService.cs` (023) | — (targeting) | Recipients from `sprk_communication`/thread + `sprk_communicationparticipant` + access filter; **fail-closes to zero** without the authoritative `sprk_isexternal` flag. |

### Consumers (current reality — corrected 2026-08-20)

> **⚠️ The 051/052 suggestion renderer was removed.** `spaarkeai-assistant-enhancements-r2` (FR-E1) DELETED `useSuggestionCards.tsx` and reduced the `suggestion` handler in `notificationsBootstrap.ts` to a **log-only stub**. So the spine's `suggestion` outbox rows are **produced but not rendered** in SpaarkeAi today (Flow B). The ONE live UI consumer is `communication-arrived`. The suggestion surface is rebuilt as OOB Dataverse notifications in [`spaarke-notification-spine-r2`](../../projects/spaarke-notification-spine-r2/README.md).

| Component | Kind consumed | Responsibility |
|---|---|---|
| `src/client/shared/Spaarke.Notifications/src/NotificationsClient.ts` | (all) | The ONE host-agnostic client — negotiate → connect → kind-route → poll fallback; per-`outboxRowId` dispatch dedup (#688). |
| `src/solutions/SpaarkeAi/src/services/notificationsBootstrap.ts` | (all — **log-only**) | `getNotificationsClient()` singleton + `initNotificationsClient()`; registers **log-only** proof-of-wiring handlers for the three active kinds — **no UI**. Non-fatal. |
| `src/client/shared/Spaarke.Communication.Components/.../CommunicationsWorkspaceWidget/useCommunicationArrivals.ts` (+ `CommunicationsWorkspaceWidget`) | `communication-arrived` | **The one live UI consumer** — updates the Communications-widget unread badge / list on arrival; re-fetches detail via `/pending`. |
| `.../components/conversation/SuggestionCard.tsx` | — (none) | **Shared presentational primitive only — NOT a spine consumer.** Reused by `useRerunFullAnalysisCard.tsx` (a client-local rerun card: no outbox row, no expiry, no dismiss endpoint, no BFF). Retained after `useSuggestionCards.tsx` was deleted. |

---

## 3. End-to-end flows

### A. A communication arrives → a badge/list update
1. Email/message is captured or sent → the Communication persist path stamps thread + participant index.
2. `CommunicationArrivedProducer` builds a `CommunicationEnvelope` (IDs + `senderDisplay` + `badgeDelta`; no body) → `CommunicationFanOutTargetingService` computes recipients from record security → **per recipient**: `OutboxService.WriteAsync` (kind=`communication-arrived`) **then** best-effort `PingUserAsync`.
3. Client `registerHandler("communication-arrived")` fires → re-fetches via `GET /api/notifications/pending?kind=communication-arrived` → updates the unread badge / communications list.

### B. Daily briefing → a proactive suggestion (⚠️ produced, currently unrendered)
1. `DailyBriefingCompositeService.RenderAsync` collects high-priority items → `DailyBriefingSuggestionProducer.ProduceAsync`. **This runs ONLY on the interactive `POST /api/ai/daily-briefing/render`** — if the user never opens the briefing, nothing is produced (the gap `spaarke-notification-spine-r2` fixes with a scheduled job).
2. Per item: **ground** (real EntityType + parseable id + name) then **gate** (`SuggestionGateOptions.Enabled` AND confirm-worthy). Both pass → one `SuggestionEnvelope` (carries `regardingRecordType` + `regardingRecordId`) → `OutboxService.WriteAsync` (kind=`suggestion`, **idempotent** per `(owner, kind, regardingRecordId)` — UAT 2026-07-22) → best-effort ping.
3. **⚠️ No renderer today.** The SpaarkeAi renderer (`useSuggestionCards`) that formerly displayed these rows was **removed** by `spaarkeai-assistant-enhancements-r2` (FR-E1); the `suggestion` handler is now log-only. So these outbox rows persist and are pollable via `/pending`, but **nothing surfaces them to the user**. The producer is left in place (harmless; re-gating it was out of r2's scope).
4. **Planned replacement** ([`spaarke-notification-spine-r2`](../../projects/spaarke-notification-spine-r2/README.md)): a **scheduled job** produces the same grounded+gated items into **OOB Dataverse notifications** (the native bell), deduped against the outbox on a **7-day** re-notify window, whose action opens the regarding record in a **modal** (`appnotification` action `navigationTarget:"dialog"` + same-origin `?pagetype=entityrecord` URL). This decouples production from the interactive render and restores a visible, dismiss-durable surface.

---

## 4. How to extend

**Add a producer** (a new source of an existing kind, or a reserved kind going active): ground + gate the signal → `OutboxService.WriteAsync` (store) → best-effort `SignalRDeliveryService.PingUserAsync` (hint). Register the ping via the ADR-032 null-object; the write is unconditional. Fan-out from record security and **test the negative-access case**. Gate the change on a `tests/integration/seam/**` vertical-slice test (ADR-038 DoD). **Be idempotent**: a producer that runs on a repeatable trigger (e.g. a briefing that re-renders on every load/refresh) MUST dedupe before writing — read `OutboxService.GetPendingAsync` (already undismissed + unexpired) and skip a candidate whose `(owner, kind, regardingRecordId)` already has a live row, so re-runs don't accumulate duplicate outbox rows. A dismissed or expired row correctly re-proposes. (`DailyBriefingSuggestionProducer` is the reference — UAT 2026-07-22.)

**Add a consumer** (a new client surface): call `getNotificationsClient().registerHandler(kind, cb)` on the ONE host client, then `start()`. Treat live pushes as signal-only (re-fetch via `/pending`); act only after re-grounding through the BFF. Do NOT open a second SignalR connection or a second negotiate.

**Activate a reserved kind** (`job-complete` / `share` / `system-alert`): define its envelope in `Services/Notifications/Envelopes/`, add its wire mirror to `@spaarke/notifications` `types.ts`, then add exactly one producer + at least one consumer. No client library version bump is needed to stop it being "unknown" — the taxonomy already recognizes it.

---

## 5. Invariants (see ADR-047 for the binding rules)

- **Dumb transport**: no body / privileged content / pre-authorized action token on the wire — IDs + minimal display metadata + an access-gated optional `snippet?` only (NFR-02/03).
- **Outbox BEFORE ping** (store-before-render). The write is truth; the ping is a best-effort hint that may be a null-object.
- **No second path**: one spine, one negotiate endpoint, one client library, one dispatch path (Layer A). SSE stays a chat-only presentation adapter.
- **Grounding + gating in producers** (Layer D), as the input to the outbox write — never in the spine.
- **Fan-out from record security**, fail-closed; internal/external via authoritative `sprk_isexternal` (never a "is a systemuser" proxy).

## 6. Entry points (where to start reading)

| Concern | Start here |
|---|---|
| Outbox contract | `Services/Notifications/OutboxService.cs` |
| Envelope shapes | `Services/Notifications/Envelopes/` |
| Delivery + endpoints | `Services/Notifications/SignalRDeliveryService.cs` · `Api/Notifications/NotificationsEndpoints.cs` |
| Client library | `src/client/shared/Spaarke.Notifications/src/NotificationsClient.ts` |
| Action seam | `Services/Ai/PublicContracts/IActionSeam.cs` |
| Reference consumer (live) | `src/client/shared/Spaarke.Communication.Components/.../CommunicationsWorkspaceWidget/useCommunicationArrivals.ts` (`communication-arrived`) · client wiring: `src/solutions/SpaarkeAi/src/services/notificationsBootstrap.ts` (log-only) |
| Cross-project consumption | [`projects/spaarke-notification-spine-r1/notes/handoffs/CROSS-PROJECT-CONSUMPTION-REPORT.md`](../../projects/spaarke-notification-spine-r1/notes/handoffs/CROSS-PROJECT-CONSUMPTION-REPORT.md) |
