# Spaarke Notification & Action Spine — R1 Design (Working Document)

> **Project ID (proposed)**: `spaarke-notification-spine-r1`
> **Status**: DRAFT design-seed — feeds `/design-to-spec`. **For review alongside `messaging-communication-app-r1` + `spaarkeai-assistant-enhancements-r1`.**
> **Date**: 2026-07-16
> **Origin**: `email-communication-solution-r4` W5 scoping surfaced that communication Responsive Intelligence needs shared infrastructure that two sibling projects independently designed. Owner directive (2026-07-16): "close r4 at its milestone; set up this shared capability as its own project with a name that reflects it serves different purposes in other contexts."
> **Grounded against**: `email-communication-solution-r4/notes/W5-responsive-intelligence-and-shared-notification-spine.md` · `messaging-communication-app-r1/design.md` §7 · `spaarkeai-assistant-enhancements-r1/design.md` §14.1a/§14.1b · live BFF `Services/Ai` + `Services/Communication` code.

> **⚠️ Name is proposed, for review.** This capability is broader than "notifications" (it also carries shared *domain actions*) and broader than "communications" or "responsive intelligence" (which are consumers). Alternatives to weigh: `spaarke-signal-action-spine-r1`, `spaarke-action-notification-fabric-r1`, `spaarke-responsive-platform-r1`. Keeping `notification-spine` for continuity — both sibling designs already call it that.

> **Assistant-side technical review incorporated (2026-07-16, `spaarkeai-assistant-enhancements-r1`).** Because that project owns the live AI **dispatch spine** (`DispositionRoutability`, `OutputRouter`, `SessionDispatchOrchestrator`, ADR-039/040/041) and is the R1.5 proactive-push consumer, its edits appear as **§5B** (assistant-side consumer spec, mirroring messaging's §5A), the **§3 dispatch-spine convergence** note, the **§6 doctrine** sharpening, the **§8A gate-zero spike** (Serverless-vs-Default), and **§10 #4/#7**. Recurring assistant-side theme: **the spine is dumb transport — every "should we act/push?" decision stays grounded + gated in per-source policy, and acting on a delivered signal re-enters the *shipped* dispatch path, never a parallel one.**

---

## 0. Executive Summary

Three Spaarke initiatives independently need the **same server-initiated, typed-signal → grounded-action → delivery infrastructure**:

1. **`spaarkeai-assistant-enhancements-r1.5`** — proactive push: surface a grounded suggestion while the user is idle (Azure SignalR + durable outbox + Daily-Briefing producer). Its design (§14.1b) *already* frames this as a **general `kind`-typed notification spine, "suggestions are consumer #1."**
2. **`messaging-communication-app` (R2)** — cross-channel in-app fan-out (timeline/badge/toast when any `sprk_communication` persists). Its design (§7) says the fabric must be built **"in coordination with `ai-assistant-enhancements-r1` so one fabric serves both — never a forked, messaging-only hub."**
3. **Communication Responsive Intelligence** (email-r4's re-homed W5) — turn an assessed communication into auto-created Events / Tasks / Notifications.

**This project builds that shared spine ONCE** — the typed delivery channel + durable outbox + the shared domain-action layer + the per-source policy pattern — so all three (and future consumers: job-completion, share, system-alert) ride it instead of forking. **Communication Responsive Intelligence is this project's proving first-class producer** (it has a live signal today — `communication_assessed` — and a clear value story).

**Guiding principle**: build the platform capability once, deliberately; every future trigger is then *authoring + one producer*, never new pipeline.

---

## 1. Why this is a project, not an email-r4 wave

email-r4's W5 (auto-create Event/Task/Notification from `communication_assessed`) could not be built inside r4 (full detail: the W5 notes doc). Three blockers, all pointing here:

1. **`EventRulesService.FireAsync` is chat-SSE/user/session-shaped** — requires `SessionId`+`UserOid`; its gates are per-interactive-user (opt-out, daily cap) / per-session (supersede). An inbound communication has no interactive user/session, so the gate *semantics* don't apply. (email-r4 task 010's **E5 escalation** already flagged this; enrichment step 5 only *logs* `communication_assessed` today.)
2. **OutputRouter's `record`/`notification` legs are unbuilt** (`Routable=false`, "net-new side-effect mechanism, out of scope, later wave") — and the notification leg **IS** the spine assistant-r1.5 is building. Building it in the chat router = the forked hub messaging-r1 §7 forbids.
3. **The infrastructure is shared** — building it inside any one consumer (email RI, or the assistant, or messaging) forks it for the other two.

---

## 2. Current-state truth (grounded 2026-07-16)

| Capability | Status | Where |
|---|---|---|
| `communication_assessed` signal (emit point) | **EXISTS (emit-only log)** | `CommunicationEnrichmentService` step 5 |
| Domain-action executors: `CreateNotification` / `CreateTask` / `UpdateRecord` | **EXIST as node executors** (chat-coupled) | `Services/Ai/Nodes/*NodeExecutor.cs` |
| `CreateNotification` → native `appnotification` → Daily Briefing | **EXISTS** | `NotificationService.CreateNotificationAsync`; `useBriefingNotifications` reader |
| `sprk_risk` + the ONE confirmation gate (ADR-039/041) | **EXISTS** (chat dispatch) | `PendingPlanManager.RequiresConfirmation` |
| Deterministic rule/gate primitives (cost cap, opt-out, confidence) | **EXIST but user/session-scoped** | `EventRulesService` |
| Durable per-user `kind`-typed outbox table | **NONE** (assistant-r1.5 designs a thin `sprk_` table) | — |
| Azure SignalR real-time channel | **NONE** (assistant-r1.5 §12.5 resolves → Azure SignalR) | grep: zero `SignalR`/`WebSocket` in the SPA |
| Association Engine + enrichment (the RI producer's upstream) | **EXISTS (email-r4 ✅)** | `Services/Communication/Engine/` |
| ADR-045 channel seams (messaging builds on) | **EXISTS (email-r4 016 ✅)** | `Services/Communication/Channels/` |

**Implication**: ~most substrate exists but is *fused into the chat dispatch spine*. This project's job is to **unbundle the shared layers and add the one genuinely-new piece (SignalR delivery + the outbox)** — not rebuild.

---

## 3. Architecture — the four-layer separation

Today's chat spine fuses four concerns. Separate them so email, chat/messaging, and the assistant reuse the core:

| Layer | What | Shared? | R1 work |
|---|---|---|---|
| **A — Domain actions** | Create Event/Task/Notification → `sprk_event`/`task`/`appnotification` | ✅ shared core | **Promote** the existing node executors to a **session-agnostic seam** invokable without a chat session. |
| **B — Durable `kind`-typed outbox** | per-user pending row (source of truth): `kind` (`suggestion`\|`communication`\|`job-complete`\|`share`\|`system-alert`) + grounded payload + delivered/dismissed/expiry | ✅ shared | **Build** the thin `sprk_` outbox table (the one assistant-r1.5 §14.1a layer 3 specs). `appnotification` stays an optional mirror. |
| **C — Real-time delivery** | Azure SignalR (Default mode, hub-in-BFF, Standard tier), `Clients.User(oid)`, `kind`-routed to the right client renderer | ✅ shared | **Build** the hub + `IHubContext` producer entrypoint + one client subscriber that routes by `kind`. Best-effort accelerator over B (degrades to next-load/poll). |
| **D — Per-source policy** | *whether* to act | ⚠️ share primitives, not scoping | **chat** = user/session gates (`EventRules`, unchanged); **comms RI** = tenant/matter rules + confidence gate, fire-and-forget (NEW); **assistant** = proactive gates (ADR-041 origin=proactive). |
| **Presentation** | SSE token/section streaming | ❌ chat-only | Demote SSE to a chat presentation adapter; it is not the spine. |

**Producer topology** (from assistant-r1.5 §14.1a, verbatim intent): SignalR delivers to live browsers only; a producer writes Dataverse + the outbox through normal server code, then *optionally* pings via `IHubContext`. A producer that can't reach SignalR is still correct — the outbox is the durable truth; live push is acceleration.

**Dispatch-spine convergence (assistant-side, 2026-07-16).** Layers A–B are not merely *beside* the chat dispatch spine — they **complete** it, and the spec should treat that as a first-class goal. In the live `DispositionRoutability` registry (ADR-043 §3 — the ONE disposition source of truth for admit ⇔ route ⇔ ledger), the **`Notification` and `Record` legs are `Routable=false`** with the literal note *"side-effect leg not yet built — lands in a later wave."* **This project IS that wave.** Realizing the Layer-A action seam + the Layer-B outbox makes `Notification` (and later `Record`) routable, so a *reactive* chat capability that emits a notification and a *proactive* idle-time producer converge on **one** notification-delivery mechanism — reactive and proactive never fork. Concretely: the spine's Layer A/B is the realization of the two deferred registry legs, not a parallel structure beside them. Two consequences to sequence deliberately: (a) flipping `Notification` to routable is a **behavior-surface change** to the shipped dispatch catalog — see the §10 #7 audit; (b) the Layer-A seam MUST stay behind the *existing* node executors so the chat dispatch path stays green (seam tests) — the same discipline assistant-r1's dispatch-spine changes carry.

---

## 4. R1 scope (proving the spine with the comms-RI producer)

**Build (the shared spine — Layers A–C + the general `kind` contract):**
- Promote domain-action executors to the session-agnostic action seam (A).
- The `sprk_` `kind`-typed durable outbox table + write/read/expiry (B).
- Azure SignalR hub-in-BFF + `IHubContext` producer entrypoint + one `kind`-routing client subscriber (C).
- The typed envelope contract (`kind` discriminator on outbox row + SignalR message).

**Build (the proving producer — communication Responsive Intelligence, Layer D-comms):**
- Replace enrichment step 5's emit-only log with a **fire-and-forget `communication_assessed` producer** (best-effort/non-fatal, NFR-06).
- A **communications policy layer**: tenant/matter-scoped rule config (Binding rows + match conditions) + confidence gate, reusing gate *primitives*; privilege flagged-not-decided (ADR-015).
- Producer creates Event/Task/Notification via the Layer-A seam + writes `kind=communication` outbox rows → Layer C pings live browsers; surfaces in Daily Briefing via the `appnotification` mirror.

**Absorbs email-r4 tasks 050–054** (re-homed) as the comms-RI slice.

**R1 does NOT**: route comms RI through `EventRulesService.FireAsync`; build a comms-only hub; build the assistant proactive producer or messaging fan-out (those are the *other* consumers — this project makes the spine they'll ride).

**New ADR** (concise + full): "Notification & action spine — typed signals, shared domain actions, per-source policy, SSE-as-presentation." Author main-session (`.claude/` write boundary).

---

## 5. Consumers roadmap (context, not all R1)

| Consumer | `kind` | Owner project | When |
|---|---|---|---|
| Communication Responsive Intelligence | `communication-assessed` | **this (R1 proving producer)** | R1 |
| Proactive NBA suggestions | `suggestion` | `spaarkeai-assistant-enhancements-r1.5` | coordinate — see §6 |
| Email unread/badge | `communication-arrived` | email (opt-in) | after R1 |
| Cross-channel messaging fan-out | `communication-arrived` | `messaging-communication-app` R2 | R2 |
| Job-completion / share / system-alert | those kinds | later, incremental | later |

Each new consumer = a renderer branch (client) + a producer (server) + authoring — **never new spine**.

> **See [§5A](#5a-communication-wide-consumer-specification-email--messaging)** for the full communication-side spec (the two communication `kind`s, the privacy-safe envelope, and the reuse-Dataverse-security fan-out requirement), contributed by `messaging-communication-app-r1`.

---

## 5A. Communication-wide consumer specification (email + messaging)

> **Contributed by `messaging-communication-app-r1` (2026-07-16).** This section is written from the *consumer* side by the project that sees both the communication pipeline and this spine. It specifies exactly how **all of Communication — email today, messaging next, SMS later — uses this service**, so nothing is assumed or missed. Treat it as a requirements input to this project's spec.

### 5A.1 The key structural fact: one producer integration point covers every channel

Email and messaging are **not two producers** — they are one. Both persist as `sprk_communication` and both flow through the **same `ICommunicationEnrichmentService`** (direction-symmetric, per ADR-045). That service already emits `communication_assessed` at step 5 (today: emit-only log — this project's proving producer). Therefore:

**The spine gets communication events for *every channel* from a single integration point — enrichment — not from per-channel code.** Messaging adds no new producer; when a chat message persists + enriches, it emits the same signal email does. SMS later is identical. This is the cleanest possible fit and the reason the comms producer belongs in the spine, not in any one channel.

### 5A.2 Two distinct communication `kind`s — please separate them

The current §5 roadmap collapses `communication` / `message-arrived` into one row. From the consumer side these are **two different signals with different gates, payloads, and latency needs**, and conflating them will force messaging to fire expensive assessments just to light a badge:

| Signal | `kind` (proposed) | Meaning | Gate | Consumer(s) |
|---|---|---|---|---|
| **Assessed → act** | `communication-assessed` | A communication was analyzed and *may warrant a domain action* (create Event/Task/Notification) | Tenant/matter rules + confidence gate (Layer D-comms, this project) | Comms Responsive Intelligence (R1) |
| **Arrived → refresh** | `communication-arrived` | A new communication persisted; *surfaces should update* (timeline row, unread badge, toast) | None beyond access — lightweight, always fire | Messaging cross-surface fan-out (R2); email badges (opt-in, any time) |

**Requirement:** the `kind` taxonomy (spine §10 decision #5) MUST distinguish these. `communication-arrived` MUST NOT require an RI assessment to fire — it is a persistence-time event, emitted right after the record is written, independent of whether enrichment later assesses it.

### 5A.3 Envelope contract for communication events (privacy-safe by construction)

Both kinds share one envelope shape. **It carries identifiers + minimal display metadata only — never message bodies, never privileged content:**

```
{
  kind: "communication-arrived" | "communication-assessed",
  communicationId,          // sprk_communication GUID
  threadId,                 // sprk_communicationthread GUID (the grouping — see messaging-r1 §6)
  channel: "email" | "message" | "sms",
  direction: "inbound" | "outbound",
  regardingRecordId,        // the matter/project/etc. this thread is about (ADR-024)
  senderDisplay,            // display name only
  snippet?,                 // OPTIONAL, short, and only when the record is non-private/non-privileged
  badgeDelta                // e.g. +1 unread
}
```

Clients receive the envelope and **re-fetch details through the BFF**, which re-checks access. The spine is an *accelerator*, not an authorization bypass: it must never deliver content a recipient couldn't already read via the BFF.

### 5A.4 Privacy & targeting — reuse the Communication entity's access boundary (hard requirement)

Communication access is governed by **Dataverse record security on `sprk_communication` / `sprk_communicationthread`** (record-scoped access, plus record-level sharing for private threads, plus an internal-only user attribute). The spine's fan-out **MUST** honor that same boundary:

- **Group membership = Dataverse-derived access**, computed from the *same* record/thread security the BFF already enforces (D-07). Target `Clients.Group(threadId)` (or `matterId`) for a thread's authorized participants; `Clients.User(oid)` for personal badges.
- A **private** thread's `communication-arrived` event MUST reach only its shared participants; an **internal-only** message's event MUST NOT reach external/portal users (R2+). Because targeting derives from the record's own security, this is correct by construction — but it is a **MUST-verify** in the spine's tests, since a fan-out leak is a privilege/ethical-wall breach (ties to spine R-5).
- No privileged content in the envelope (§5A.3); privilege is flagged, never decided (ADR-015).

### 5A.5 Timing — who consumes when

| Consumer | Consumes | When | Note |
|---|---|---|---|
| Comms Responsive Intelligence | `communication-assessed` | **spine R1** | This project's proving producer — email + messaging both, via enrichment |
| Email unread/badge | `communication-arrived` | opt-in, any time after spine R1 | Low-effort win; email gets live badges "for free" |
| **Messaging cross-surface fan-out** | `communication-arrived` | **messaging R2** | Messaging **R1 does NOT consume the spine** — its single MDA surface uses **BFF polling** (client-pull) in its own timeline component, *deliberately not* SignalR. R2 (portal + Teams + AI code page) swaps the poll for spine **push** (server-initiated) once multi-surface fan-out actually bites. |

**Ask of this project:** reserve the `communication-arrived` kind + the §5A.3 envelope **now**, and emit it at persistence time for all channels, even though the first *consumer* (messaging R2) lands later. That way messaging R2 and email badges bind to an existing contract instead of reopening the spine. This is the "design once" payoff messaging-r1 §7 depends on.

### 5A.6 Requirements checklist for the spine (from the communication side)

1. `kind` set includes **both** `communication-assessed` and `communication-arrived`, semantically separated (§5A.2).
2. `communication-arrived` fires at **persistence time**, no assessment prerequisite, all channels, via the shared enrichment/persist path (§5A.1).
3. Envelope carries **IDs + minimal metadata only**; clients re-fetch via BFF (§5A.3).
4. Fan-out targeting **derives from `sprk_communication`/thread Dataverse security**; private/internal-only respected and **test-verified** (§5A.4).
5. Envelope includes `threadId` so cross-channel timeline consumers can group by thread (the messaging-r1 thread model).
6. Spine degrades gracefully (ADR-032 Null-Object): if SignalR is off, the durable outbox + next-load/poll still deliver — messaging R1 relies on exactly this (BFF polling, no server push in R1).

---

## 5B. Assistant-side consumer specification (R1.5 proactive suggestions)

> **Contributed by `spaarkeai-assistant-enhancements-r1` (2026-07-16).** Written from the *consumer* side by the project that owns the live AI **dispatch spine** — so the spine is specified against the mechanisms a proactive suggestion must actually re-enter, not an idealized push. Treat as a requirements input to this project's spec, the assistant-side peer of §5A.

### 5B.1 What R1.5 becomes under path (i) — smaller and better-defined

Under path (i), assistant-R1.5 **stops building infrastructure** and becomes a pure consumer of this spine: (a) the `kind=suggestion` **producer** (server), (b) the **proactive-policy gates** (Layer D-assistant — ADR-041 `origin=proactive`), (c) the **Assistant renderer branch** (client — routes `kind=suggestion` to the suggestion card). It **drops "build SignalR + the outbox" entirely.** This is strictly better for R1.5 — it rides a proven, shared spine instead of birthing infra — but it makes R1.5 **depend on the spine landing** (see §5B.5 sequencing). Flag to owners: choosing (i) *re-sequences a slice of R1.5 out*, which is a feature, not a cost.

### 5B.2 Hard invariant — the spine is dumb transport; grounding + gating stay in the producer

A proactive suggestion MUST be **grounded** (ADR-039 — removes-the-impossible; only offer what the catalog can actually do) and **confirmation-gated** (ADR-041, `origin=proactive`) **BEFORE** it is written to the outbox or pushed. The spine MUST NEVER be a path that delivers **ungrounded or ungated** content. This mirrors the reactive invariants assistant-R1 already enforces and *tests*: task 031 pins that the User Model cannot become a second decider; task 052 pins that profile content cannot escape its access boundary. **Requirement:** the outbox write is the *output* of the producer's grounding + gate, never a bypass around them. The spine carries an already-decided, already-grounded signal — **it does not decide.**

### 5B.3 Acting on a delivered signal RE-ENTERS the shipped dispatch path — no parallel action path (the load-bearing assistant requirement)

This is the constraint the notification project cannot see from its side. When a user acts on a delivered suggestion (clicks "Create the matter" on a proactive card), that action MUST re-enter the **existing** dispatch + hand-off mechanisms assistant-R1 shipped — **never a parallel proactive-action path**:

- A **"create X" suggestion**, when acted on, dispatches through the shipped `POST /api/ai/chat/sessions/{id}/dispatch` seam onto the **`SurfaceLaunch` disposition** → the same pre-seeded wizard/OOB-form hand-off (handoff-id + `sessionStorage`, per assistant-r1 `notes/012-assistant-surface-handoff-design.md`) that a *reactive* "create X" uses.
- The action's **"✅ done" claim is ack-gated** (P5 truthfulness invariant, assistant-r1 task 020) — a proactive action that doesn't complete **fails honestly**, exactly as reactive.
- **Domain actions** (Create Event/Task/Notification) invoked via the spine's Layer-A seam are the **same executors** the chat path uses (now session-agnostic) — so a proactive "remind me" and a chat "remind me" produce **identical records + gating**.

**Requirement:** Layer A's session-agnostic seam MUST preserve the ack-contract (P5) *and* the `SurfaceLaunch`/dispatch entry points, so a proactive action is **behaviorally identical** to a reactive one. The spine delivers the *signal*; acting on it is the shipped assistant, not a new engine. (This is the assistant-side statement of §6's "no forked hub": no forked *action* path either.)

### 5B.4 The `kind=suggestion` envelope — identifiers + re-ground via BFF (mirrors §5A.3)

Like the communication envelope, a suggestion envelope carries **identifiers + minimal display metadata ONLY** — never the full grounded payload:

```
{
  kind: "suggestion",
  suggestionId,
  source: "daily-briefing" | "event" | "insight",
  regardingRecordId,        // the matter/project/etc. the suggestion is about (ADR-024)
  title,                    // short display
  snippet?,                 // OPTIONAL, short, access-checked
  actionHint,               // "create-matter" | "review" | … (drives the renderer + the dispatch it will re-enter)
  expiresAt
}
```

The client renders a compact card and **re-fetches / re-grounds the actionable detail through the BFF at action time**, which re-checks grounding *and* access. Two reasons this is a hard requirement, not an optimization: **(a) parity** — the spine is an accelerator, never an authorization *or grounding* bypass (same principle as §5A.3); **(b) freshness** — a suggestion drafted at idle-time *T* must re-ground at action-time *T′*; the catalog/record state may have changed, and a stale, ungrounded push is worse than none. **The envelope never carries a pre-authorized action token.**

### 5B.5 Reactive-first ordering is a HARD dependency, not a preference

Assistant-R1 (reactive) must ship **working create flows** before proactive suggestions surface them (owner decision, assistant-r1 §14.1a). Rationale: because acting on a `create-*` suggestion re-enters the `SurfaceLaunch`/wizard hand-off (§5B.3), a proactive suggestion that launches into a *broken* create flow **amplifies** the failure — worse than no suggestion. Therefore the **suggestion consumer is blocked on assistant-R1's create-flow vertical landing**:

```
assistant-R1 (reactive create flows)  →  notification-spine R1 (shared delivery, comms-RI proving producer)  →  assistant-R1.5 (suggestion consumer)
```

Note this does **not** block *this project*: the comms-RI proving producer creates records **server-side** (Layer A), not via the Assistant surface, so the spine can prove itself on comms-RI while assistant-R1 finishes its create-flow vertical. Only the *suggestion* consumer waits.

### 5B.6 Presence / idle detection is the consumer's concern, NOT the spine's

"Surface a suggestion while the user is **idle**" needs presence/idle detection (is the user active in the Assistant? on which surface?). That is a **consumer** responsibility (the producer + the client renderer decide *when* to surface), **not** a spine responsibility. The spine delivers `kind=suggestion` to `Clients.User(oid)`; the client renderer decides toast-now / badge / hold-to-next-Assistant-open based on presence. Keep presence **out** of the spine — baking it in would couple the transport to one consumer's UX and re-fork the fabric §6 is trying to unify.

### 5B.7 Daily Briefing is the first suggestion producer — `appnotification` is its degraded path

R1.5's first `kind=suggestion` source is the **Daily-Briefing producer**, which already writes native `appnotification` (read by `useBriefingNotifications`). Under the spine, that producer writes the outbox (`kind=suggestion`) + optionally pings SignalR; the **`appnotification` mirror stays as the graceful-degradation path** (next-Assistant-open) exactly as §3 / §5A.6 specify. So R1.5's Daily-Briefing consumer gets **live push "for free"** once it writes outbox rows — no new render path, and the existing surface is the fallback. This is the assistant-side mirror of email's "badges for free" (§5A.5).

### 5B.8 Requirements checklist for the spine (from the assistant side)

1. `kind` set includes **`suggestion`** (§10 #5); **reserve it now**, even though the consumer (R1.5) lands after the comms-RI proving producer — so R1.5 binds to an existing contract, not a reopened spine (the "design once" payoff, assistant §14.1b).
2. Layer A's session-agnostic action seam **preserves the ack-contract (P5)** + the **`SurfaceLaunch`/dispatch entry points**, so a proactive action is behaviorally identical to a reactive one (§5B.3).
3. The spine **NEVER** pushes ungrounded/ungated content — the outbox write is the *output* of the producer's grounding + ADR-041 gate, never a bypass (§5B.2).
4. Suggestion envelope carries **IDs + minimal metadata**; the client **re-grounds via BFF at action time**; no pre-authorized action token in the envelope (§5B.4).
5. **Presence/idle detection stays in the consumer**, not the spine (§5B.6).
6. **Graceful degradation** (ADR-032 Null-Object): SignalR off → outbox + next-Assistant-open (`appnotification` mirror) still delivers — §5A.6 parity.
7. Realizing the **`Notification` disposition leg** (Layer A/B) routes reactive chat notifications *and* proactive suggestions through **ONE** mechanism (§3 convergence) — no fork; sequence the behavior-surface change (§10 #7).

---

## 6. The key coordination decision (for owner review)

**Who builds Layers A–C?** `spaarkeai-assistant-enhancements-r1.5` is currently scoped to build the SignalR + outbox spine *inside its proactive-push work*. That would fork the platform capability into one consumer. Two paths:

- **(i) This project owns the shared spine (recommended).** Extract Layers A–C out of assistant-r1.5 into this project as **platform infrastructure**; assistant-r1.5 becomes a *consumer* (`kind=suggestion`) of it, messaging R2 another, comms-RI the R1 proving producer. Honors "design once"; matches messaging-r1 §7's "one fabric, coordinated." Cost: re-sequences a slice of assistant-r1.5.
- **(ii) assistant-r1.5 builds the spine; this project is the comms-RI consumer + the domain-action extraction.** Less re-sequencing; but the spine is born inside one consumer and must be generalized later (assistant-r1.5 §14.1b at least designs it general, mitigating this).

**Recommendation: (i)** — and the argument is **doctrinal, not merely reuse**. This platform runs a consistent **spine-singularity doctrine** through every subsystem: *no second decider* (ADR-039), *no second dispatch protocol / no routing outside the Binding table* (compose invariant), *no second memory store* (ADR-042), *no second routing surface* (the Binding table is the only one). A shared delivery spine is that same doctrine applied to push: **no second push/delivery mechanism.** Building SignalR + the outbox inside assistant-R1.5 would be the *first* violation of a principle the platform otherwise holds everywhere — a forked capability born inside one consumer. Path (i) keeps the platform consistent with itself. And it is not an override: R1.5's §14.1b *already* designed this general ("suggestions consumer #1"), so path (i) **extracts a capability its own author intended to be shared.** Decide at spec intake with all three project owners.

---

## 7. Dependencies & sequencing

- **Upstream ready**: Association Engine + enrichment + `communication_assessed` signal + channel seams (email-r4 ✅).
- **Coordinate**: assistant-r1.5 (spine ownership §6), messaging R2 (consumer). Run `/conflict-check`; the spine touches `Services/Ai` (email-r4 now owns it; r2-core closed) + a new hub + a new Dataverse table.
- **Azure**: Azure SignalR Service (Default mode, Standard tier, ~$49/mo/unit) — per-customer provisioning wired into the provisioning orchestrator (ADR-027). Verify target-env CSP `connect-src` before design freeze (assistant-r1.5 open item).

---

## 8. BFF governance (root §10)
- **Placement**: spine + producer live in the existing BFF (sole policy + token-minting point). Hot-path `<bff>Y`, `<spaarke-ai>` touches `Services/Ai/Nodes` (domain-action seam).
- **Publish-size**: the **Azure SignalR SDK footprint** vs the ≤60 MB ceiling is an open risk (assistant-r1.5 flags ~40 KB client SDK; server SDK footprint to measure in a spike). Baseline ~45.30 MB (post-r4).
- **CVE** scan; **`/conflict-check`** before PRs; register in `projects/INDEX.md` at start.
- New services use ADR-032 Null-Object (the spine degrades to next-load when SignalR is off).

---

## 8A. Phase-0 SignalR footprint spike — GATE-ZERO (assistant-side, 2026-07-16)

R-2 (Azure SignalR SDK vs the 60 MB BFF ceiling) is a **go/no-go gate that must run BEFORE Layer-C placement is committed** — not a mitigation alongside design. Assistant-R1 enforces publish-size on *every* BFF task (baseline ~45.30–49.63 MB compressed incl. PDBs; **architecture-review at 55 MB, hard stop at 60 MB**). The **server** Azure SignalR SDK footprint is unmeasured and could move the BFF into the review band or over the ceiling — and if it does, the entire "hub-in-BFF" placement is invalid, so this cannot be discovered mid-build.

**The spike MUST compare the two Azure SignalR modes** — the decisive dimension the current "Default mode, hub-in-BFF" assumption skips:

| Mode | BFF footprint | Fit with the §3 producer topology |
|---|---|---|
| **Default** (hub hosted in the BFF; `IHubContext` send) | **Heavier** — the full `Microsoft.Azure.SignalR` hosting SDK + hub runtime carried in the BFF | Works, but hosts a bidirectional hub the topology barely uses |
| **Serverless** (no hosted hub; BFF = a `negotiate` endpoint that mints a client token + the Management/REST SDK to **send**) | **Lighter** — send-only; clients connect to the SignalR *service* directly, not to the BFF | **Better fit.** The design's own topology (§3) is *"producer writes Dataverse + outbox, then optionally pings"* = a **send-only** pattern — exactly what Serverless is for. `Clients.User(oid)` / `Group(threadId)` targeting is fully supported via the Management API. |

**Recommendation: default the spike toward Serverless mode.** Every producer in this design is a *sender*, not a bidirectional-hub consumer, so Serverless likely sidesteps the footprint risk **and** matches the producer topology better than hub-in-BFF. Measure both, but put the **burden of proof on Default mode** to justify hosting a hub the topology doesn't need.

**Go/no-go criteria** (decide at gate-zero, before the spine is designed around a placement):
1. Add the chosen mode's server SDK to the BFF; measure **compressed publish size**. If it crosses **55 MB** → architecture review; **60 MB** → hub/negotiate **must** move out of the BFF into a separate lightweight service (Layer-C placement rejected).
2. Measure **App Service cold-start delta** (a large hosting SDK can regress cold start on the per-customer plan).
3. Run the **CVE scan** on the added transitive graph (`Microsoft.Azure.SignalR` + deps) — no new HIGH.
4. Confirm the target-env **CSP `connect-src`** allows the SignalR service endpoint (an assistant-r1.5 open item, §7) — a client that can't open the socket silently falls back to the outbox, which hides the misconfig.

---

## 9. Risks
| # | Risk | Mitigation |
|---|---|---|
| R-1 | Spine ownership ambiguity across 3 projects → two hubs | §6 decision at spec intake with all owners; one project owns A–C |
| R-2 | Azure SignalR SDK breaches 60 MB BFF ceiling | **GATE-ZERO spike (§8A) — go/no-go BEFORE Layer-C placement**, not a mitigation alongside design. Compare **Serverless (send-only, recommended) vs Default (hub-in-BFF)** modes against the 55/60 MB bands; if it breaches, the hub/negotiate moves out of the BFF. |
| R-3 | Comms policy layer duplicates chat gate logic | Reuse gate *primitives*; do NOT reuse chat user/session scoping |
| R-4 | Domain-action extraction regresses chat dispatch | Session-agnostic seam behind the existing executors; keep chat path green (seam tests) |
| R-5 | Privilege auto-decided in fan-out | ADR-015 flag-only; **named security sign-off gate** (like assistant-r1's profile-security gate) + **test-verified targeting** (§5A.4) — a fan-out leak is a compliance incident, not a defect, so it cannot rest on correct-by-construction alone. |
| R-6 | Over-building real-time before consumers exist | R1 ships spine + ONE producer (comms-RI); other consumers adopt incrementally |

---

## 10. Decisions to resolve before `/design-to-spec`
1. **Spine ownership** (§6 — the big one): this project owns Layers A–C vs assistant-r1.5 builds them. Decide with all three owners.
2. **Project name** (header): confirm `spaarke-notification-spine-r1` vs an alternative.
3. **Comms rule config store** — reuse Binding (`sprk_playbookconsumer`) + match conditions vs a comms-specific rule table.
4. **Azure SignalR footprint + MODE** — the §8A **gate-zero** spike: **Serverless vs Default mode** (Serverless recommended — send-only, matches the §3 producer topology, lighter footprint) measured against the 55/60 MB BFF bands + cold-start + CVE, **before** Layer-C placement is committed. If it breaches, the hub/negotiate moves out of the BFF.
5. **`kind` taxonomy** — lock the initial discriminator set (`suggestion`\|`communication`\|`job-complete`\|`share`\|`system-alert`).
6. **New ADR number** — confirm + author main-session.
7. **"What lights up when `Notification` becomes routable" audit** (assistant-side, §3 convergence) — realizing the Layer-A/B legs flips the live `DispositionRoutability` `Notification` leg from `Routable=false` to routable. Enumerate every shipped chat capability that would then be able to emit a notification, and **sequence that behavior-surface change deliberately** — it is a change to the existing dispatch catalog's behavior, to be planned, not discovered post-merge. (Assistant-r1 can produce this audit from the dispatch side.)
8. **Suggestion action re-entry (assistant-side, §5B.3)** — confirm the Layer-A seam preserves the `SurfaceLaunch`/dispatch entry points + the P5 ack-contract, so acting on a proactive suggestion is behaviorally identical to a reactive dispatch (no parallel action path).
