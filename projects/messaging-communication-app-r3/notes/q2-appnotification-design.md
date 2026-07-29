# Q2 Design — Communication-arrived → Dataverse app-notification (clickable, centralized)

> **Status**: Design for operator sign-off (2026-07-28). Scoped into messaging-communication-app-r3 (no r4 queued).
> **Goal**: When a communication arrives, ALSO create a persistent, clickable Dataverse `appnotification` (the model-driven-app notification center / bell), deep-linked to the conversation — in addition to the existing transient SignalR toast + unread badge.

## Approach (reuse, not new plumbing)

Insert an app-notification "mirror" into the **existing** `CommunicationArrivedProducer.EmitCommunicationArrivedAsync` per-recipient loop (`Services/Communication/CommunicationArrivedProducer.cs:185-197`), right after `PingUserAsync`. That loop already runs for **all 5 persist points** (email/messaging inbound + email/messaging/as-user outbound) and already has in scope: `recipientSystemUserId`, `communicationId`, `threadId`, regarding, `envelope.Channel`.

Create the notification through the **`IActionSeam.CreateNotificationAsync`** Layer-A facade (`Services/Ai/PublicContracts/IActionSeam.cs:35`) — ADR-013 mandates non-AI/CRUD callers use this facade, never the node executors. Inject `IActionSeam` (singleton) into the producer ctor.

**This mirrors the established `CommunicationRiActionService` pattern** (task 042 — already listed in the spine's producers as "→ outbox → ping → appnotification mirror"). Our feature generalizes its single-owner mirror to the fan-out recipient set — which the spine arch doc explicitly calls "a deliberate future enhancement."

### Clickable deep-link
`NotificationActionCore` emits `data.actions[]: [{ "title":"Open", "data":{ "url": actionUrl } }]` when `ActionUrl` is set AND `ToastType != Hidden` — this is the Power Platform app-notification navigation-action schema that renders a clickable "Open" in the bell. So: set `ToastType = Timed (200000000)`, `ActionUrl = ` an MDA record URL.

### Idempotency
Built-in: `NotificationActionCore` dedups on `(ownerid + sprk_category + sprk_regardingid, active)`. Set `Category = "communication"` + `RegardingId = <threadId>` so a recipient never gets duplicate unread bell notifications for the same thread. TTL is 7 days (`ttlinseconds=604800`) by default.

## §10 Placement Justification (BFF addition — MANDATORY)

- **Where**: `Sprk.Bff.Api/Services/Communication/CommunicationArrivedProducer.cs` (+ ctor inject `IActionSeam`).
- **Why BFF**: the appnotification is a Dataverse domain side-effect that must fire at the server-side arrival choke point, where recipient fan-out + regarding are already resolved. No client can (or should) author per-recipient notifications.
- **No new endpoint/service/interface** — reuses `IActionSeam.CreateNotificationAsync` (already registered, already the one dispatch path). **No new AI-internal injection** (uses the public facade per refined ADR-013).
- **No new package** → publish-size delta ≈ 0 (a ctor param + a method call). No new CVE. Will still run the `dotnet publish` size check pre-merge (baseline ~47.5 MB, ceiling 60 MB).

## §11 Component Justification

1. **Existing**: `IActionSeam.CreateNotificationAsync` already creates `appnotification` records (used by Analysis, Work Assignments, Daily Briefing, playbook CreateNotification).
2. **Extension**: inject it into the existing producer — no new service/abstraction.
3. **Cost-of-doing-nothing**: arrivals produce only a **transient** toast + a badge that resets; a user not on the workspace has **no persistent, clickable, centralized** record of a new message — they miss it. The bell notification is the durable, cross-surface awareness.

## ADR-047 compliance + tension

Compliant: (a) NOT a new producer / second push path — inserted inside the existing spine producer; (b) the appnotification is a **domain side-effect mirror**, separate from the dumb-transport outbox row (sanctioned `CommunicationRiActionService` pattern); (c) **non-fatal** — the producer already swallows exceptions (NFR-05), so a notification failure never breaks the persist path; (d) deep-link is a **plain MDA URL, no pre-authorized token** — the click re-enters an access-checked surface (NFR-02/03).

🔔 **ADR tension to confirm at code-review (§6.5)**: ADR-047 FR-19 says "R3 consumes the spine's signal; it MUST NOT wire its own producer." **Path A (project-scoped, documented)**: we do NOT create a new producer or push path — we add a domain-side-effect mirror to the *existing* producer, byte-identical to the already-shipped `CommunicationRiActionService` mirror (task 042). The appnotification is not a spine push. Cited here + in the PR for adr-check to approve explicitly.

## Recipient resolution (already done)

The producer's `recipients` set comes from `CommunicationFanOutTargetingService.GetEligibleRecipientsAsync` — message participants (ADR-048 grain), internal-only filtered (fail-closed), private-thread gated, systemuser-grain. We loop the SAME set → security-correct targeting for free, identical to who gets the toast/ping.

## Open decisions (operator) — see AskUserQuestion

- **D1 Deep-link target** — regarding record (matter, hosts the conversation PCF) vs the thread record.
- **D2 Which arrivals** — all (inbound + outbound-to-others) vs inbound-only.
- **D3 Recipient scope** — mirror the fan-out (same as toast) vs narrower.
- **D4 Lifecycle** — 7-day TTL + manual dismiss (default) vs also auto-clear when the thread is opened.

## Task breakdown (once decisions land)

1. Inject `IActionSeam` into `CommunicationArrivedProducer` (DI already singleton-safe).
2. Build the deep-link `ActionUrl` (resolve regarding entity logical name from the typed ADR-024 lookup / thread — NOT the string `sprk_regardingrecordtype`, which is the lookup we fixed earlier; or link to the thread record for reliability).
3. Call `IActionSeam.CreateNotificationAsync` per recipient (Category="communication", RegardingId=threadId, ToastType=Timed, ActionUrl, Title/Body signal-only).
4. Tests: seam/unit mirroring `CommunicationRiActionService` tests (fan-out → N appnotifications, idempotency skip, non-fatal on seam failure, deep-link URL shape).
5. Publish-size check; adr-check (cite the FR-19 Path-A exception); code-review.
6. Deploy BFF + UAT.
