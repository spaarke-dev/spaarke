# ADR-047: Notification & Action Spine

> **Status**: **Proposed** (2026-07-21) — promotes to **Accepted** at the `spaarke-notification-spine-r1` project gate (Layers A–D shipped, `tests/integration/seam/**` green, one producer delivering typed-signal → outbox → push/poll end-to-end).
> **Deciders**: project owner; `spaarke-notification-spine-r1`. Concise version: [`.claude/adr/ADR-047-notification-action-spine.md`](../../.claude/adr/ADR-047-notification-action-spine.md).
> **Supersedes / claims**: the ADR-047 number reserved by ADR-046 (`messaging-communication-app-r1`) and ADR-048 (`messaging-communication-app-r2`).

---

## Context

By mid-July 2026 **three separate projects each needed a server-initiated push to a client surface**, and each was on a path to build its own:

- **`email-communication-solution-r4`** — after enrichment assesses an inbound communication, it needs to surface an Event/Task/Notification to the user's app surface.
- **`messaging-communication-app-r3`** — needs to notify participants when a `communication-arrived` (new ACS message / email persisted, ADR-046).
- **`spaarkeai-assistant-enhancements-r1`** — needs a proactive, unsolicited push channel for Daily-Briefing suggestions into the Assistant pane (its "phase 1.5" push provider).

Spaarke today has **no server→client push at all**: the only real-time is request-scoped SSE (chat streaming + the ADR-033 document-stream side channel), which is per-request, per-instance, and cannot fan-out server→many-hosts. Left unmanaged, each project would independently pick a channel, define its own envelope shape, and wire its own gate — **three forked delivery mechanisms** with subtly different privacy invariants and no shared client. That is precisely the "each consumer forks its own X" failure mode root [CLAUDE.md §6/§11](../../CLAUDE.md) exists to prevent, and the failure the spec names explicitly ("no second push/delivery mechanism"). The authors of ADR-046 and ADR-048 anticipated this and **reserved ADR-047** for a notification spine rather than letting messaging absorb it.

Two researcher spikes (2026-07-15/16) recommended Azure SignalR in general but (a) did not measure this BFF's publish-size delta, and (b) disagreed on mode (Serverless-first for MDA-only scope vs. Default + hub-in-BFF for the assistant channel). Task 001 (FR-01) resolved that empirically against this BFF's actual dependency graph — see the Delivery-mode decision below.

## Decision

Build **ONE Notification & Action Spine**, once, for every Spaarke client surface. A producer **grounds + gates** a typed signal, writes a **durable `kind`-typed outbox row**, then best-effort **pings via Azure SignalR**; a **host-agnostic client** subscribes and routes by `kind`, with a **poll fallback** when the live connection is down. The spine is **dumb transport** — it carries IDs + minimal display metadata only, and never makes an authorization, grounding, or gating decision.

Four layers:

1. **Layer A — shared domain-action seam.** One session-agnostic seam BEHIND the existing `*NodeExecutor.cs` (ADR-013 `PublicContracts` facade). Comms-RI and suggestion actions re-enter the SAME dispatch path chat uses; characterization tests pin the chat path before extraction.
2. **Layer B — durable outbox.** `sprk_notificationoutbox` (a `kind`-typed per-user pending store with envelope JSON + delivered/dismissed/expiry lifecycle) + its write/read/dismiss/expire service. The source of truth; SignalR only accelerates it.
3. **Layer C — delivery.** Azure SignalR (Serverless mode, in-BFF per task 001) + a host-agnostic shared client subscriber library (`@spaarke/notifications`) + a poll fallback endpoint (FR-06).
4. **Layer D — per-source producer policy.** Grounding + gating (comms policy layer; suggestion gate = ADR-039 grounding + ADR-041 `origin=proactive`) live in the producers, as the INPUT to the outbox write.

The six architectural commitments (stated as MUST/MUST NOT rules in the concise version): **typed signals · shared domain actions · per-source policy · SSE-as-presentation · outbox-before-ping · dumb-transport.**

### Delivery-mode decision (task 001 / FR-01 — authoritative)

Task 001 measured both SignalR modes against this BFF ([`notes/spikes/fr-01-signalr-footprint.md`](../../projects/spaarke-notification-spine-r1/notes/spikes/fr-01-signalr-footprint.md)): baseline 47.08 MB incl-PDB; **Serverless** (`Microsoft.Azure.SignalR.Management`) +0.30 MB; **Default** (`Microsoft.Azure.SignalR`) +0.23 MB; both **0 new HIGH CVE**, both ~12.6 MB under the 60 MB ceiling. **Decision: GO / Serverless, Layer C hosted in the BFF.** Serverless wins because a send-only spine (outbox → ping → client fetches) never uses Default mode's hosted-hub full-duplex capability, so FR-01's burden-of-proof-on-Default is not met, and Serverless holds no boot-time hub/service-connection. Because no mode breached 60 MB, the delivery layer stays in the BFF (the escalation to move hub/negotiate out-of-BFF did not fire). This measured outcome supersedes the researcher-memory recommendations.

## Consequences

**Positive**
- The three would-be forks collapse into one spine, one envelope contract, one client library, one gate discipline. A fourth future consumer builds against a documented decision instead of re-litigating spine ownership.
- The durable-outbox-before-ping rule makes producers correct under SignalR outage by construction; the poll fallback + ADR-032 null-object give a real degrade path (NFR-04).
- Privacy invariant is structural: envelopes carry no content a recipient couldn't already re-fetch through the auth-checked BFF (NFR-02/03), so the spine can never become an authorization bypass.

**Costs / tensions**
- **ADR-043 tension (resolved, Path C).** `DispositionRoutability` currently marks Notification `Routable=false`. Flipping it (FR-14) is **Path C — comply, sequenced**: gated behind the "what lights up" audit (task 032) so the registry-wide flip is proven safe before it lands, with the matching `OutputRouter` switch leg added in the same change. This ADR documents that resolution; it does not reopen it.
- **FR-19 R3 contract-lock dependency.** `messaging-communication-app-r3`'s `communication-arrived` consumer (its task 045) is unblocked by this spine's contract lock. R3 consumes the spine's emitted signal; it MUST NOT wire its own producer. Merge-order coordination is required at R3's Phase 1.
- **Cross-project sequencing.** `email-communication-solution-r4` owns `Services/Communication/**` until its W10 merges; the comms-assessed producer (Phase 4) lands after that. The spine's SignalR SDK addition is a BFF hot-path change — `/conflict-check` before every BFF PR.
- A new Dataverse table (`sprk_notificationoutbox`) and a new shared client package are net-new surface; both are justified under CLAUDE.md §11 (no existing store carries `kind` + envelope + lifecycle; no push client exists anywhere).

## Integration

Realized by the project's waves: Layer B (tasks 011/012), envelope contract (013), Layer C (020–022), `communication-arrived` producer (024), Layer A extraction + Notification flip (030–033), comms-assessed producer + policy (040–042), suggestions (050–052). Cross-references: ADR-043 (dispatch spine / `PublicContracts`), ADR-041/039 (gate + grounding), ADR-032 (kill-switch), ADR-024 (regarding), ADR-046/048 (targeting), ADR-028 (negotiate auth), ADR-038 (seam-test DoD).
