# Spaarke Notification & Action Spine — Architecture & Component Model

> **Status**: Shipped (Layers A–D on master, `spaarke-notification-spine-r1`, 2026-07-22).
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
| `Api/Notifications/NotificationsEndpoints.cs` | `POST /api/notifications/negotiate` (oid-scoped token from JWT) · `GET /api/notifications/pending[?kind=]` (oid-scoped, expiry-filtered; the poll fallback AND the re-fetch surface). |
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

### Consumers (SpaarkeAi reference implementation)
| Component | Responsibility |
|---|---|
| `src/solutions/SpaarkeAi/src/services/notificationsBootstrap.ts` | `getNotificationsClient()` singleton + `initNotificationsClient()` (registers handlers + `start()`; non-fatal). |
| `.../components/conversation/SuggestionCard.tsx` · `useSuggestionCards.tsx` (051/052) | Renders `kind=suggestion` as a compact card (mirrors `ConsumerChips` styling, ADR-021); pre-mount expiry filter; click → re-fetch/re-ground → **open the regarding record in a modal** (`INavigationService.openRecordModal`, task 052). |

---

## 3. End-to-end flows

### A. A communication arrives → a badge/list update
1. Email/message is captured or sent → the Communication persist path stamps thread + participant index.
2. `CommunicationArrivedProducer` builds a `CommunicationEnvelope` (IDs + `senderDisplay` + `badgeDelta`; no body) → `CommunicationFanOutTargetingService` computes recipients from record security → **per recipient**: `OutboxService.WriteAsync` (kind=`communication-arrived`) **then** best-effort `PingUserAsync`.
3. Client `registerHandler("communication-arrived")` fires → re-fetches via `GET /api/notifications/pending?kind=communication-arrived` → updates the unread badge / communications list.

### B. Daily briefing → a proactive suggestion → open the record
1. `DailyBriefingCompositeService.RenderAsync` collects high-priority items → `DailyBriefingSuggestionProducer.ProduceAsync`.
2. Per item: **ground** (real EntityType + parseable id + name) then **gate** (`SuggestionGateOptions.Enabled` AND confirm-worthy). Both pass → one `SuggestionEnvelope` (carries `regardingRecordType` + `regardingRecordId`) → `OutboxService.WriteAsync` (kind=`suggestion`) → best-effort ping.
3. SpaarkeAi `useSuggestionCards` receives the signal → re-grounds from `/pending` → renders `SuggestionCard` ("Review {Name}"), expired ones filtered out.
4. User clicks → the hook **re-fetches** to confirm the row is still pending (freshness + access re-check) → **opens the regarding record in a modal** (`openRecordModal`, Layout 1, `target: 2`, 85% × 85%). Stale/revoked → a stable local line, nothing opens.

---

## 4. How to extend

**Add a producer** (a new source of an existing kind, or a reserved kind going active): ground + gate the signal → `OutboxService.WriteAsync` (store) → best-effort `SignalRDeliveryService.PingUserAsync` (hint). Register the ping via the ADR-032 null-object; the write is unconditional. Fan-out from record security and **test the negative-access case**. Gate the change on a `tests/integration/seam/**` vertical-slice test (ADR-038 DoD).

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
| Reference consumer | `src/solutions/SpaarkeAi/src/components/conversation/useSuggestionCards.tsx` |
| Cross-project consumption | [`projects/spaarke-notification-spine-r1/notes/handoffs/CROSS-PROJECT-CONSUMPTION-REPORT.md`](../../projects/spaarke-notification-spine-r1/notes/handoffs/CROSS-PROJECT-CONSUMPTION-REPORT.md) |
