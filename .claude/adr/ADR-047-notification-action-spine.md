# ADR-047: Notification & Action Spine (Concise)

> **Status**: **Proposed** (2026-07-21, `spaarke-notification-spine-r1` Phase 1).
> **Promotion**: → **Accepted** at the project gate (Layers A–D shipped + `tests/integration/seam/**` green + one producer delivering typed-signal → outbox → push/poll end-to-end). Do NOT mark Accepted before the gate.
> **Domain**: Cross-surface delivery — the ONE server-initiated **typed-signal → grounded-action → delivery** spine, built once for every Spaarke client surface.
> **Builds on**: ADR-043 (dispatch/execution spine; the Layer-A seam re-uses its `PublicContracts` facade) · ADR-041 (confirmation gate; store-before-render) · ADR-039 (grounded actions, closed catalogs) · ADR-032 (null-object kill-switch) · ADR-024 (regarding family) · ADR-046/ADR-048 (communication record + participant index) · ADR-028 (auth v2 negotiate).
> **Why this ADR exists**: three sibling projects — `email-communication-solution-r4`, `messaging-communication-app-r3`, `spaarkeai-assistant-enhancements-r1` — each independently needed server→client push. Absent a claimed decision, each would fork its own delivery mechanism (per-consumer hub, ad-hoc envelope, second gate). That fork is the exact failure this spine exists to prevent. ADR-046 and ADR-048 reserved this number precisely because their authors recognized the spine as a distinct, adjacent decision.

---

## Decision

**ONE spine**: a producer *grounds + gates* a typed signal → writes a **durable, `kind`-typed outbox row** → best-effort **push (Azure SignalR)** with a **poll fallback** → a **host-agnostic client** routes it by `kind`. The spine is **dumb transport**; all judgment lives in producers; there is **no second push / delivery / action path** anywhere in the platform.

Four layers, built once:
- **Layer A** — a session-agnostic domain-action seam BEHIND the existing node executors (comms-RI + suggestion actions re-enter the same dispatch path chat uses).
- **Layer B** — the durable `kind`-typed outbox (`sprk_notificationoutbox`) + its write/read/dismiss/expire service. Source of truth.
- **Layer C** — Azure SignalR delivery + a host-agnostic shared client subscriber library + a poll fallback endpoint.
- **Layer D** — per-source producer policy (grounding + gating), never in the spine.

## Delivery mode (task 001 / FR-01 spike — authoritative, do not re-litigate)

Per [`projects/spaarke-notification-spine-r1/notes/spikes/fr-01-signalr-footprint.md`](../../projects/spaarke-notification-spine-r1/notes/spikes/fr-01-signalr-footprint.md): **GO / Azure SignalR *Serverless* mode** (`Microsoft.Azure.SignalR.Management`), with Layer-C delivery **hosted inside the BFF**. Measured on this BFF: **+0.30 MB** compressed publish, **0 new HIGH CVE**, ~12.6 MB under the 60 MB ceiling → no >60 MB breach, so the hub/negotiate stays in the BFF. Serverless is chosen because a **send-only** spine does not use Default mode's hosted-hub full-duplex capability, so FR-01's burden-of-proof-on-Default is **not met**. This measured outcome is authoritative and **supersedes** the two researcher-memory recommendations (which are prior art, not the decision).

## Constraints

### ✅ MUST

- **MUST** carry **typed signals** — a `kind`-discriminated envelope over a **closed taxonomy** (active `suggestion` | `communication-assessed` | `communication-arrived`; reserved `job-complete` | `share` | `system-alert`), never a free-form payload. An unknown `kind` fails to deserialize rather than silently reaching a consumer:

  ```csharp
  // NotificationKind is a closed set; the converter throws on any unknown token.
  public enum NotificationKind { Suggestion, CommunicationAssessed, CommunicationArrived,
                                 JobComplete, Share, SystemAlert /* reserved: no shape, no consumer */ }
  ```

- **MUST** realize domain actions through **ONE Layer-A action seam behind the existing `*NodeExecutor.cs`** (ADR-013 `PublicContracts` facade) — never per-consumer action logic. Comms-RI (`communication-assessed`) and suggestion actions re-enter the SAME dispatch path chat uses; characterization tests pin the chat path first.

- **MUST** keep **grounding + gating in the producers** (Layer D: the comms policy layer, the suggestion gate = ADR-039 grounding + ADR-041 `origin=proactive`). These run as the **input to** the outbox write, not after it.

- **MUST** write the **durable outbox row BEFORE** the best-effort SignalR ping (store-before-render, ADR-041/043):

  ```csharp
  await _outbox.WriteAsync(userId, kind, envelope, regarding, expiresAt); // truth, unconditional
  _signalR?.PingAsync(userId);                                            // hint, best-effort, may be null-object
  ```

- **MUST** register the **ping** via the ADR-032 null-object kill-switch (P1/P2/P3); the outbox **write** is unconditional (there is no kill switch for "persist the pending row") — only the delivery ping is the conditionally-registered leg.

- **MUST** derive fan-out **targeting from record security** (`sprk_communication`/thread + `sprk_communicationparticipant`, ADR-046/048) and **test the negative-access case** — a mis-targeted envelope is a compliance incident.

- **MUST** gate any dispatch-spine change on a **`tests/integration/seam/**` vertical-slice test** (ADR-038 DoD) — a passing contract-shape/unit test alone is not sufficient.

### ❌ MUST NOT

- **MUST NOT** add a **second push / delivery mechanism** — no per-consumer hub, no parallel proactive-action path, no second gate/decider. One spine, one negotiate endpoint, one client library.
- **MUST NOT** place **message bodies, privileged content, or pre-authorized action tokens** in an envelope — IDs + minimal display metadata + an access-gated OPTIONAL `snippet?` only. An envelope is never itself sufficient to perform the action; consumers re-fetch/re-ground through the auth-checked BFF at action time (NFR-02/03).
- **MUST NOT** rework SSE or promote it to a delivery channel — **SSE stays a chat-only presentation adapter**, conceptually demoted by this ADR. The spine does not touch chat's SSE and adds no SSE code.
- **MUST NOT** route comms-RI through `EventRulesService.FireAsync` — reuse gate *primitives* (cost cap, confidence) only, never chat's SSE user/session scoping.
- **MUST NOT** let a consuming project (e.g. `messaging-communication-app-r3`) wire its own `communication-arrived` producer — the spine emits; R3 consumes only (FR-19 contract lock).

## Integration

ADR-043 (Layer-A seam via `PublicContracts`; shared dispatch) · ADR-041/ADR-039 (producer grounding + gating; store-before-render) · ADR-032 (ping kill-switch, outbox write unconditional) · ADR-024 (envelope `regardingRecordId`) · ADR-046/ADR-048 (fan-out targeting from the communication record + participant junction) · ADR-028 (authenticated negotiate; the SignalR/WebSocket transport is the enumerated raw-fetch exception, `// Auth v2 (D-AUTH-7):`) · ADR-038 (seam test = DoD for spine changes). **ADR Tension (resolved in spec.md)**: ADR-043 `DispositionRoutability` marks Notification `Routable=false` — flipping it (FR-14) is **Path C (comply, sequenced)** via the "what lights up" audit, not an amendment. **FR-19**: messaging-r3's `communication-arrived` consumer is unblocked by this spine's contract lock; it never forks a producer.

**Full ADR**: [docs/adr/ADR-047-notification-action-spine.md](../../docs/adr/ADR-047-notification-action-spine.md)
