# Notification-Spine Contract LOCK — communication-arrived (formal, post-implementation)

> **MIRROR** (canonical copy delivered to messaging-communication-app-r3). **Date**: 2026-07-21 · **From**: `spaarke-notification-spine-r1` (task 025 / FR-19)
> **Supersedes**: `notification-spine-contract-alignment.md` (2026-07-20, design-time). That note was the
> design-level promise; THIS note is the **formal lock citing the actually shipped code** on master
> (commit `1a5bc7d15`). Task 045's P1 gate is answered here.
> **Status**: all cited tasks (013/020/021/022/024) are **shipped + merged to master**.

This is the authoritative contract your task 045 is blocked on. Every claim below is cited to shipped
code, not to the spine's spec/plan. **⚠️ One additive discrepancy vs your alignment note — see §6.**

---

## 1. Trigger = persistence, all channels, capture + send identically ✅ (confirmed as shipped)

`communication-arrived` fires when an `sprk_communication` row is written — inbound capture AND outbound
send, email AND messaging, treated identically, **with no assessment/enrichment prerequisite**. The
single spine-owned producer is
[`Services/Communication/CommunicationArrivedProducer.cs`](/src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationArrivedProducer.cs)
(`EmitCommunicationArrivedAsync(Guid communicationId)`), wired at these **5 persist orchestration points**
(task 024):

| # | Channel / Direction | Wired at |
|---|---|---|
| 1 | email inbound | `IncomingCommunicationProcessor.ProcessAsync` — Step 4.8 (after participant index) |
| 2 | messaging inbound | `MessagingIngestor.IngestAsync` — after enrichment (post participant-index) |
| 3 | messaging outbound | `CommunicationService.SendMessageAsync` — after `WriteParticipantIndexAsync` |
| 4 | email outbound (app/shared) | `CommunicationService.SendAsync` — after `WriteParticipantIndexAsync` |
| 5 | email outbound (as user) | `CommunicationService.SendAsUserAsync` — after `WriteParticipantIndexAsync` |

> **Implementation note (why the emit is at the orchestration level, not the raw `CreateAsync`):** the
> emit runs AFTER the participant-index step because the fan-out targeting reads the
> `sprk_communicationparticipant` junction; at the raw record-create the junction is empty and the thread
> lookup / regarding are not yet stamped. This does not change your contract — it only means the signal
> reliably reaches the message's participants. The producer is **non-fatal** (NFR-05): a producer failure
> never fails the persist, so a communication is always saved regardless of signal outcome.

## 2. The spine emits; R3 consumes ONLY — **MUST NOT** wire your own producer

This is a **MUST NOT** rule (spec Owner Clarification + root CLAUDE.md), not a suggestion. There is exactly
ONE `communication-arrived` producer — the spine's, above. **Do not add a second emit** in R3 (no
`communication-arrived` write from your Phase-1 persist/read path, no parallel push). Your **task 045 step 2
becomes verify-only**: assert that the spine's emit fires for a message-persist and an email-persist — not
"emit from the BFF."

## 3. Envelope shape (task 013) — cite these exact fields

Shipped type:
[`Services/Notifications/Envelopes/CommunicationEnvelope.cs`](/src/server/api/Sprk.Bff.Api/Services/Notifications/Envelopes/CommunicationEnvelope.cs)
(a `sealed record`, **9 properties**, guarded by a reflection test). `kind` is the closed enum
`NotificationKind` ([`NotificationKind.cs`](/src/server/api/Sprk.Bff.Api/Services/Notifications/Envelopes/NotificationKind.cs)),
wire value **`"communication-arrived"`**.

| JSON field | C# type | Notes |
|---|---|---|
| `kind` | `NotificationKind` | wire `"communication-arrived"` (closed, fail-closed converter) |
| `communicationId` | `Guid` | the `sprk_communication` id |
| `threadId` | `Guid` | the `sprk_communicationthread` id (grouping key) |
| `channel` | `string` | `"email"` \| `"message"` \| `"sms"` |
| `direction` | `string` | `"inbound"` \| `"outbound"` |
| **`regardingRecordId`** | `string` | **⚠️ present in shipped code — NOT in your alignment note's list (see §6). Required; may be `""` when unassociated.** |
| `senderDisplay` | `string` | **display NAME only — never an address** (NFR-02/03). Currently a privacy-safe channel label (`"New email"`/`"New message"`); no display-name column exists yet. |
| `snippet` | `string?` | OPTIONAL; **currently always `null`** (privacy-conservative — content is never placed on the spine in R1). |
| `badgeDelta` | `int` | `+1` per arrival |

**Critical for your renderer:** the envelope is **NOT on the live SignalR wire.** The live push carries
only `NotificationSignal { outboxRowId, kind }` (signal-only, NFR-02/03 —
[`SignalRDeliveryService.cs`](/src/server/api/Sprk.Bff.Api/Services/Notifications/SignalRDeliveryService.cs)).
You get the full envelope from the **poll endpoint** (§4), keyed by `outboxRowId`.

## 4. Consumer API (tasks 021 + 022) — use the shared client, do NOT hand-roll

**Client library** — [`@spaarke/notifications`](/src/client/shared/Spaarke.Notifications/README.md)
(`file:` dependency; peer-deps `@spaarke/auth` + `@microsoft/signalr`). This is the ONE client for all
three R3 hosts — do NOT hand-roll a second connection/poll implementation (CLAUDE.md §11).

```typescript
import { NotificationsClient } from '@spaarke/notifications';
const client = new NotificationsClient();
const unregister = client.registerHandler('communication-arrived', (event) => {
  // event: { outboxRowId, kind, envelope?, source: 'live' | 'poll' }
  // envelope is ABSENT on live pushes, PRESENT on poll — do not assume it's populated.
});
await client.start();   // negotiates + connects; poll-fallback starts automatically on failure
// ... on unmount: unregister(); await client.stop();
```

Public surface (from the README's locked API table): `NotificationsClient(options?)`,
`registerHandler(kind, cb) → unregister`, `start(): Promise<void>`, `stop(): Promise<void>`,
`connectionState` getter. Handler payload `NotificationEvent = { outboxRowId, kind, envelope?, source: 'live' | 'poll' }`.

**BFF endpoints** (shipped in
[`Api/Notifications/NotificationsEndpoints.cs`](/src/server/api/Sprk.Bff.Api/Api/Notifications/NotificationsEndpoints.cs)):
- `POST /api/notifications/negotiate` (task 020) — SignalR connection info, oid-scoped server-side.
- `GET /api/notifications/pending` (task 022) — returns `{ "items": [ { "outboxRowId", "kind", "envelope" }, ... ] }`.

The client wraps both; register handlers before `start()`.

## 5. Degrade semantics (ADR-032 null-object) — no signal loss when SignalR is off

When Azure SignalR is not configured, `POST /api/notifications/negotiate` returns **HTTP 503**
(`NullSignalRDeliveryService`, ADR-032 P3). The client catches this, `start()` re-throws a typed
`SignalRUnavailableError`, and **poll-fallback (`GET /api/notifications/pending`) has already started in the
background** — so signals are NOT dropped, they arrive with `source: 'poll'` (and a full `envelope`). The
durable `sprk_notificationoutbox` row is the source of truth; SignalR is only an accelerator. Your host may
render a degraded-connection indicator, but functionality is intact.

## 6. ⚠️ DISCREPANCY vs your alignment note (flagged, not silently reconciled)

Your `notification-spine-contract-alignment.md` (§Answers #3) listed the envelope as **8 fields**:
`kind, communicationId, threadId, channel, direction, senderDisplay, snippet, badgeDelta`.

**The shipped envelope has 9 fields** — it adds **`regardingRecordId`** (a required `string`, the ADR-024
regarding record id; `""` when the communication has no association yet). **This is ADDITIVE and
non-breaking**: all 8 fields you assumed are present with the exact names/types you expected; you simply
receive one more. Action for R3: if your `NotificationEvent`/envelope TypeScript type mirrors the 8-field
list, add `regardingRecordId: string`. No field you expected is missing or renamed.

Two other shipped realities worth knowing (not contradictions, just current state): `senderDisplay` is a
generic channel label (no per-message display name in R1) and `snippet` is always `null` in R1 — both are
deliberate NFR-02/03 privacy choices, and both are forward-compatible (a future task can populate them
without a contract change).

---

## What task 045 does now

1. Remove any plan to emit `communication-arrived` from R3 (MUST NOT — §2).
2. Consume via `@spaarke/notifications` `NotificationsClient.registerHandler('communication-arrived', …)` (§4).
3. On a live event, re-fetch envelope detail via the poll endpoint using `outboxRowId` (envelope absent on live push — §3/§4).
4. Add `regardingRecordId` to your envelope type mirror (§6).
5. Verify (not emit): a message-persist and an email-persist each produce a pending outbox row + a live/poll event (§1).

Authoritative API reference: the [`@spaarke/notifications` README](/src/client/shared/Spaarke.Notifications/README.md) "Contract lock" section. Questions → `spaarke-notification-spine-r1`.
