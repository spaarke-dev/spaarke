# Spaarke Notification & Action Spine — R1 Design (Working Document)

> **Project ID (proposed)**: `spaarke-notification-spine-r1`
> **Status**: DESIGN — combined-scope decision made (path iii, §6) + status verified against master 2026-07-20; feeds `/design-to-spec`. **Absorbs assistant R1.5 proactive-push scope (§4A). Remaining pre-spec items: §10 #3 (rule store), #4 (gate-zero spike), #5 (taxonomy lock — time-bound by messaging-r3 P1), #7 (routable audit), #8 (re-entry confirmation), plus the §6 follow-through note in the assistant project's docs.**
> **Date**: 2026-07-16
> **Origin**: `email-communication-solution-r4` W5 scoping surfaced that communication Responsive Intelligence needs shared infrastructure that two sibling projects independently designed. Owner directive (2026-07-16): "close r4 at its milestone; set up this shared capability as its own project with a name that reflects it serves different purposes in other contexts."
> **Grounded against**: `email-communication-solution-r4/notes/W5-responsive-intelligence-and-shared-notification-spine.md` · `messaging-communication-app-r1/design.md` §7 · `spaarkeai-assistant-enhancements-r1/design.md` §14.1a/§14.1b · live BFF `Services/Ai` + `Services/Communication` code.

> **⚠️ Name is proposed, for review.** This capability is broader than "notifications" (it also carries shared *domain actions*) and broader than "communications" or "responsive intelligence" (which are consumers). Alternatives to weigh: `spaarke-signal-action-spine-r1`, `spaarke-action-notification-fabric-r1`, `spaarke-responsive-platform-r1`. Keeping `notification-spine` for continuity — both sibling designs already call it that.

> **2026-07-20 — SCOPE DECISION + verified status refresh (owner directive).** A four-track investigation (email-r4, assistant-r1, messaging-r1/r2/r3, live-master ground-truth) confirmed **every §2 current-state claim still holds on master**. **§6 is RESOLVED as path (iii) — combined project**: this project owns Layers A–D **and absorbs the assistant R1.5 proactive-push scope** (assistant design §14.1a / §12.5 / §14.1b / §7 — fully designed there, never spec'd/tasked) as its **second proving consumer**; `spaarkeai-assistant-enhancements-r1` closes at its reactive milestone. No separate r1.5 project is created. Verified deltas folded into this doc: the **§5B.5 create-flow dependency is SATISFIED** (assistant create-flow vertical shipped + merged to master, two UAT rounds); **ADR-047 is reserved for this project** (messaging-r2 deliberately took ADR-048); **messaging-r3's spec FR-22 is a COMMITTED consumer** of `communication-arrived` ("the spine will be made available" — contract lock needed at r3's P1); BFF publish baseline is now **~46.24 MB** (was ~45.30); email-r4 **W10 (UAT round 2) is authored-but-unstarted** — sequence the enrichment-producer touch after it merges. See **§4A** (absorbed R1.5 scope), **§6** (resolution), **§10** (updated register).

> **Assistant-side technical review incorporated (2026-07-16, `spaarkeai-assistant-enhancements-r1`).** Because that project owns the live AI **dispatch spine** (`DispositionRoutability`, `OutputRouter`, `SessionDispatchOrchestrator`, ADR-039/040/041) and is the R1.5 proactive-push consumer, its edits appear as **§5B** (assistant-side consumer spec, mirroring messaging's §5A), the **§3 dispatch-spine convergence** note, the **§6 doctrine** sharpening, the **§8A gate-zero spike** (Serverless-vs-Default), and **§10 #4/#7**. Recurring assistant-side theme: **the spine is dumb transport — every "should we act/push?" decision stays grounded + gated in per-source policy, and acting on a delivered signal re-enters the *shipped* dispatch path, never a parallel one.**

> **🔑 Owner decision (2026-07-20) — R1.5 folds into THIS project. §6 ownership RESOLVED to path (i).** The proactive-push release previously sequenced as `spaarkeai-assistant-enhancements-r1.5` is **NOT a standalone project**; its requirements are **combined into `spaarke-notification-spine-r1`**. This project therefore owns Layers A–C (domain-action seam + durable outbox + Azure SignalR delivery) **and** the R1.5 proactive-suggestion **consumer** (the `kind=suggestion` producer, proactive gates, and the Assistant render/subscriber slot). Consequence: (a) `spaarkeai-assistant-enhancements-r1` (R1, reactive-first) is the **upstream prerequisite only** — its shipped create-flow hand-offs are the thing R1.5 surfaces proactively — and carries **no** R1.5 build work; (b) when this project runs `/design-to-spec`, pull the R1.5 requirements from `spaarkeai-assistant-enhancements-r1/design.md` §1.5, §7, §12.5, §14.1a (five-layer target), §14.1b (`kind`-typed spine), and §15.4 into this project's spec; (c) the §8A gate-zero SignalR-footprint spike + target-env CSP (`connect-src`) check remain the design-freeze gates before build.

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

## 2. Current-state truth (grounded 2026-07-16; **all rows re-verified against live master 2026-07-20** — every claim CONFIRMED: emit-only log intact, `Notification`/`Record` legs still `Routable=false`, zero SignalR references/packages, no outbox table, `FireAsync` still requires SessionId+UserOid, `appnotification` path intact)

| Capability | Status | Where |
|---|---|---|
| `communication_assessed` signal (emit point) | **EXISTS (emit-only log)** | `CommunicationEnrichmentService` step 5 |
| Domain-action executors: `CreateNotification` / `CreateTask` / `UpdateRecord` | **EXIST as node executors** (playbook-run-coupled via `NodeExecutionContext` — RunId/PlaybookId/TenantId/UserId; precision refined 2026-07-20, the extraction claim is unchanged: no session-agnostic seam exists) | `Services/Ai/Nodes/*NodeExecutor.cs` |
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

**R1 does NOT**: route comms RI through `EventRulesService.FireAsync`; build a comms-only hub; build the messaging fan-out (messaging consumes in its own R3+). ~~build the assistant proactive producer~~ — **superseded 2026-07-20 (path iii)**: the proactive suggestion producer + renderer ARE this project's later waves; see §4A.

**New ADR** (concise + full): "Notification & action spine — typed signals, shared domain actions, per-source policy, SSE-as-presentation." Author main-session (`.claude/` write boundary). **Number confirmed: ADR-047** — the gap between ADR-046 and ADR-048 was explicitly held open for this project by messaging-r2 (its task 004 was instructed not to claim it).

---

## 4A. Absorbed R1.5 scope — proactive suggestions as the SECOND proving consumer (added 2026-07-20, path iii)

> Source: `spaarkeai-assistant-enhancements-r1/design.md` §14.1a (five-layer target architecture), §12.5 (SignalR resolution), §14.1b (general `kind`-typed spine framing), §7. R1.5 was carved out of assistant-R1 by the 2026-07-15 "reactive-first" owner decision and was **fully designed but never spec'd or tasked** — so absorption is a design-merge, with zero task rework. The assistant project closes at its reactive milestone (shipped + merged; §5B.5 dependency satisfied).

**What R1.5 is**: the full proactive-push capability — the Assistant surfaces a **grounded, gated** suggestion while the user is idle (not in response to something they just did). The proving flip: **Daily-Briefing sensor → "let's review them" → pre-seeded flow** (one reactive→proactive flip of an already-shipped reactive capability).

**Five-layer R1.5 architecture (assistant §14.1a) mapped onto this spine** — the mapping is near-1:1, which is why absorption is cheap:

| # | Assistant R1.5 layer (§14.1a) | Spine mapping | Wave |
|---|---|---|---|
| 1 | **Server-fireable Event-path producer** — Daily-Briefing is R1.5's ONE producer | A `kind=suggestion` producer over the Layer-A/B seam (grounding + ADR-041 `origin=proactive` gate applied BEFORE the outbox write, per §5B.2) | Suggestion wave (after spine + comms-RI proving waves); coordinate `Services/Ai/Narrators/DailyBriefing*` with active `spaarke-daily-update-service-r5` |
| 2 | **Durable pending-suggestion outbox** — thin new `sprk_` table, **payload authority**; `appnotification` kept only as an optional MDA mirror | **IS Layer B**, verbatim — one `kind`-typed table serves all consumers; suggestion rows are `kind=suggestion` | Core spine wave |
| 3 | **Azure SignalR Service** — "suggestions changed → fetch" **signal only; at-most-once; durability lives in the outbox** | **IS Layer C**. The signal-only/fetch-on-ping semantics generalize to every `kind` (identical to the §5A.3/§5B.4 envelope-then-refetch contract). ⚠️ The assistant design assumed **Default mode (hub-in-BFF, Standard tier, ~$49/mo/unit)** — that assumption is **superseded by the §8A gate-zero spike** (Serverless recommended; the spike decides, burden of proof on Default) | Gate-zero spike, then core spine wave |
| 4 | **Client subscriber / render slot in the Assistant** — a **chip source reusing the shipped dispatch + ack-gate: no new pipeline, no second gate** | The `kind=suggestion` renderer branch of the one kind-routing subscriber. Satisfies §5B.3 re-entry + the P5 ack-contract **by construction** — acting on a suggestion re-enters the shipped dispatch path | Suggestion wave |
| 5 | **Polling fallback to the same pending-suggestions endpoint** | The degraded path (§3 / §5A.6 #6 / §5B.8 #6, ADR-032 Null-Object). Concretizes generic "next-load/poll": a **pending read endpoint over the Layer-B outbox — build it `kind`-generic**, so every consumer (suggestions, comms, badges) polls the same surface when SignalR is off | Core spine wave |

**Absorption consequences for R1 scope (§4)**: the "R1 does NOT build the assistant proactive producer" line is **superseded** — this project now builds it, as **later waves after the comms-RI proving producer**. The wave order preserves the original proving logic (spine proves itself on comms-RI first) while removing the cross-project contract seam entirely. Natural mid-point milestone: spine + comms-RI live — the project can pause there if priorities shift before the suggestion waves.

---

## 5. Consumers roadmap (context, not all R1)

| Consumer | `kind` | Owner project | When |
|---|---|---|---|
| Communication Responsive Intelligence | `communication-assessed` | **this (R1 proving producer #1)** | R1 |
| Proactive NBA suggestions | `suggestion` | **this (R1 proving consumer #2 — absorbed R1.5, §4A; path iii resolved 2026-07-20)** | R1 later waves |
| Email unread/badge | `communication-arrived` | email (opt-in) | after R1 |
| Cross-channel messaging awareness (badge + toast) | `communication-arrived` | `messaging-communication-app-r3` — **COMMITTED consumer (its spec FR-22, 2026-07-20: "the spine will be made available"); contract lock (kind + envelope + persistence-time trigger) needed at r3's P1.** R2 closed reserve-only (code-complete 2026-07-19, stayed BFF-polling per its Q-E). | messaging R3 |
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

### 5A.7 Messaging-R3 consumer verification (added 2026-07-20 — R3 is EXECUTING and release-gates on this spine)

> Verified against `messaging-communication-app-r3` spec + tasks (31 tasks via `/project-pipeline` 2026-07-20; Wave 1 complete same day). Its **task 045** (FR-22: `communication-arrived` → unread badge + toast, awareness only, content stays ~5s polling per its NFR-03) is `status=blocked` on this spine's contract, with a correct escalation trigger ("degrade to polling-only; do NOT invent a contract") — and its **deploy/UAT task 050 depends on 045**, so **this spine is on R3's release-critical path**.

**Verification result: the §5A contract fully covers R3's requirements** — kind separation (§5A.2), persistence-time trigger, envelope fields R3 needs (`threadId`, `badgeDelta`, `communicationId`, `channel`, `direction`, `senderDisplay`, privacy-gated `snippet?`), Dataverse-security targeting (§5A.4 — now buildable on the shipped `sprk_communicationparticipant` junction), and ADR-032 degradation matching R3's polling fallback. Three items need explicit resolution at the R3-P1 contract lock:

1. **Producer ownership — spine emits; R3 consumes only.** R3's task 045 step 2 currently plans to *emit* `communication-arrived` from the BFF itself. Per §5A.5's "ask" (accepted): **this spine's R1 emits at persistence time for ALL channels** from the shared persist path — R3 should not wire a producer. At R3's P1, reconcile: 045's emit step becomes "verify the spine's emit fires for message + email persistence"; 045 keeps only the client consume (badge + toast). This prevents double-wiring and keeps the single-integration-point guarantee of §5A.1. Answer to R3's open "on-capture vs on-send" question: **both — the trigger is persistence** (`sprk_communication` row written), which covers inbound capture and outbound send identically.
2. **The client subscriber MUST be a shared library, not a SpaarkeAi-shell-only component.** R3 consumes from three hosts: the SpaarkeAi workspace widget, a **record-form PCF**, and a **standalone Vite code page**. The §3 "one `kind`-routing client subscriber" must therefore ship as a host-agnostic shared package (e.g., under `@spaarke/ui-components` or a thin `@spaarke/notifications` client lib) with the negotiate/connection handling reusable outside the workspace shell — plus the `kind`-generic pending/poll endpoint (§4A layer 5) as the no-SignalR fallback all three hosts share. This is a NEW requirement surfaced by R3; fold into the spine spec.
3. **Contract-lock contents + coordination.** The R3-P1 lock must cover: kind name (`communication-arrived` — locked), envelope shape (§5A.3), trigger point (persistence — above), **consumer API surface** (subscriber package + negotiate endpoint + poll fallback endpoint), and degrade semantics. ⚠️ Sequencing: R3's Phase-1 backend wave (tasks 002–005, serial, active NOW) edits the same `Services/Communication/` persist/read path this spine's arrived-producer touches — `/conflict-check` + merge-order with R3, in addition to the email-r4 W10 constraint (§7). Note the **wave-ordering opportunity**: the `communication-arrived` producer needs only Layers B+C (outbox + delivery), NOT Layer A or the comms policy layer — so it can land in the core spine wave, ahead of the comms-RI producer, unblocking R3's release gate earlier.

(Minor, R3-side, non-blocking: task 045's POML labels its dep 003 as "participant junction" — in R3's numbering, 003 is the list-threads endpoint; the junction was R2's 003. Surface at R3's P1. Also R3's references point at the email-r4 worktree *copy* of this design — the authoritative copy is `projects/spaarke-notification-spine-r1/design.md` in the main repo; the copy is being kept in sync.)

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

### 5B.5 Reactive-first ordering is a HARD dependency, not a preference — ✅ SATISFIED 2026-07-20

> **Status update (2026-07-20 investigation)**: the dependency below is **satisfied**. Assistant-R1's create-flow vertical (tasks 002 SurfaceLaunch, 010 constrained-field resolver, 012/013 wizard handoff + pre-seed, 014, 020 P5 ack-contract, 031, 052 security sign-off) is shipped, **merged to master**, deployed to dev, and hardened through two UAT rounds (R3/R4). The suggestion waves (§4A) are no longer blocked on it; assistant-R1's remaining tail is owner-facing UAT items only.

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

## 6. The key coordination decision — ✅ RESOLVED 2026-07-20: path (iii), combined project

> **Owner decision (2026-07-20)**: neither (i) nor (ii) as originally framed — **path (iii): R1.5 and this project are COMBINED into one project.** This project owns Layers A–D AND the absorbed R1.5 proactive-suggestion scope (§4A) as its second proving consumer. `spaarkeai-assistant-enhancements-r1` closes at its reactive milestone (already shipped + merged); no separate r1.5 project is ever created. Rationale over path (i): the two-project split would put the contract (`kind` taxonomy, envelope, outbox schema) on a project boundary during its formative phase — coordination tax (drift-watches, joint reviews, the very ownership ambiguity R-1 warned about, which the investigation found live: the assistant's ratified design still claimed the spine for R1.5, and `spaarke-notification-spine-r1` appeared nowhere in its docs). Path (iii) also *strengthens* the doctrinal argument below: the spine is proven against **two consumers of deliberately different shapes** (server-side fire-and-forget comms-RI; user-targeted, grounded+gated suggestions) inside one project — the strongest generality guarantee available. R1.5 had zero decomposed tasks, so absorption cost = a design merge (§4A).
>
> **Follow-through required**: (a) record the scope move in `spaarkeai-assistant-enhancements-r1`'s design/notes so its §14.1a/§14.1b stop claiming the R1.5 build items (their docs are currently the only place still assigning the spine to R1.5); (b) confirm at r3/email intake touchpoints — messaging-r3 and email-r4 already reference this project by name, so no re-pointing is needed on their side.

**Original analysis (retained for the record) — Who builds Layers A–C?** `spaarkeai-assistant-enhancements-r1.5` is currently scoped to build the SignalR + outbox spine *inside its proactive-push work*. That would fork the platform capability into one consumer. Two paths:

- **(i) This project owns the shared spine (recommended).** Extract Layers A–C out of assistant-r1.5 into this project as **platform infrastructure**; assistant-r1.5 becomes a *consumer* (`kind=suggestion`) of it, messaging R2 another, comms-RI the R1 proving producer. Honors "design once"; matches messaging-r1 §7's "one fabric, coordinated." Cost: re-sequences a slice of assistant-r1.5.
- **(ii) assistant-r1.5 builds the spine; this project is the comms-RI consumer + the domain-action extraction.** Less re-sequencing; but the spine is born inside one consumer and must be generalized later (assistant-r1.5 §14.1b at least designs it general, mitigating this).

**Recommendation: (i)** — and the argument is **doctrinal, not merely reuse**. This platform runs a consistent **spine-singularity doctrine** through every subsystem: *no second decider* (ADR-039), *no second dispatch protocol / no routing outside the Binding table* (compose invariant), *no second memory store* (ADR-042), *no second routing surface* (the Binding table is the only one). A shared delivery spine is that same doctrine applied to push: **no second push/delivery mechanism.** Building SignalR + the outbox inside assistant-R1.5 would be the *first* violation of a principle the platform otherwise holds everywhere — a forked capability born inside one consumer. Path (i) keeps the platform consistent with itself. And it is not an override: R1.5's §14.1b *already* designed this general ("suggestions consumer #1"), so path (i) **extracts a capability its own author intended to be shared.** Decide at spec intake with all three project owners.

---

## 7. Dependencies & sequencing

- **Upstream ready** (re-verified on master 2026-07-20): Association Engine + enrichment + `communication_assessed` signal + channel seams (email-r4 ✅). Also now shipped: **`IThreadResolver` + the `threadId` contract** (messaging-r1 task 040, extended additively by r2 — stable in master, grounding the §5A.3 envelope) and the **`sprk_communicationparticipant` junction (ADR-048, messaging-r2)** — directly relevant to §5A.4 Dataverse-derived fan-out targeting.
- **Coordinate** (refreshed 2026-07-20):
  - **email-r4** — NOT closed: W10 (UAT round 2, tasks 101–105) authored-but-unstarted; branch ahead of master. The proving producer edits `CommunicationEnrichmentService` (their file) — **sequence that touch after W10 merges**; `/conflict-check` before the producer wave.
  - **assistant-r1** — UAT tail (R4-6/11/12) still touches `Services/Ai` + the SpaarkeAi page; §6 follow-through note in their docs; they produce the §10 #7 routable audit from the dispatch side.
  - **messaging-r3** — committed consumer (spec FR-22), **EXECUTING as of 2026-07-20** (31 tasks; Wave 1 done; its task 045 is `blocked` on this spine and its deploy task 050 depends on 045 → **this spine is on r3's release-critical path**). **Lock the contract at r3's P1** (kind + envelope + persistence trigger + consumer-API surface — full list §5A.7); resolve producer ownership (spine emits, r3 consumes — §5A.7 #1); its Phase-1 backend wave edits the same `Services/Communication/` path serially right now — `/conflict-check` + merge-order. Wave-ordering opportunity: the arrived-producer needs only Layers B+C — land it in the core spine wave to unblock r3 early (§5A.7 #3).
  - **daily-update-r5** — active on `Services/Ai/Narrators/DailyBriefing*` (prod-fix mode); coordinate the §4A suggestion-producer wave's merge order.
  - **ai-architecture-redesign** — r1 COMPLETE + archived 2026-07-08; r2 uninitialized (design-stage). No live collision; consume published `PublicContracts` seams, no fork.
  - Run `/conflict-check`; the spine touches `Services/Ai` + `Services/Communication` + a new hub/negotiate + a new Dataverse table; register in `projects/INDEX.md` with hot-path declaration at start.
- **Azure**: Azure SignalR Service — **mode decided by the §8A gate-zero spike** (Serverless recommended; the absorbed R1.5 design's Default-mode assumption is superseded pending the spike, §4A layer 3). Standard tier ~$49/mo/unit either way; per-customer provisioning wired into the provisioning orchestrator (ADR-027). Verify target-env CSP `connect-src` before design freeze (inherited open item).

---

## 8. BFF governance (root §10)
- **Placement**: spine + producer live in the existing BFF (sole policy + token-minting point). Hot-path `<bff>Y`, `<spaarke-ai>` touches `Services/Ai/Nodes` (domain-action seam).
- **Publish-size**: the **Azure SignalR SDK footprint** vs the ≤60 MB ceiling is an open risk (absorbed R1.5 design flags ~40 KB client SDK; server SDK footprint to measure in the §8A spike). **Baseline ~46.24 MB** (latest recorded, messaging-r2 2026-07-19; supersedes the 45.30 figure — headroom to the 55 MB review band is ~8.8 MB).
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
| R-1 | ~~Spine ownership ambiguity across 3 projects → two hubs~~ **RETIRED 2026-07-20** — §6 resolved path (iii); residual: the assistant project's docs still claim R1.5 builds the spine until the §6 follow-through note lands there | §6 resolution recorded; follow-through note in assistant docs |
| R-2 | Azure SignalR SDK breaches 60 MB BFF ceiling | **GATE-ZERO spike (§8A) — go/no-go BEFORE Layer-C placement**, not a mitigation alongside design. Compare **Serverless (send-only, recommended) vs Default (hub-in-BFF)** modes against the 55/60 MB bands; if it breaches, the hub/negotiate moves out of the BFF. |
| R-3 | Comms policy layer duplicates chat gate logic | Reuse gate *primitives*; do NOT reuse chat user/session scoping |
| R-4 | Domain-action extraction regresses chat dispatch | Session-agnostic seam behind the existing executors; keep chat path green (seam tests) |
| R-5 | Privilege auto-decided in fan-out | ADR-015 flag-only; **named security sign-off gate** (like assistant-r1's profile-security gate) + **test-verified targeting** (§5A.4) — a fan-out leak is a compliance incident, not a defect, so it cannot rest on correct-by-construction alone. |
| R-6 | Over-building real-time before consumers exist | R1 ships spine + ONE producer (comms-RI); other consumers adopt incrementally |

---

## 10. Decisions to resolve before `/design-to-spec` (register updated 2026-07-20)
1. ~~**Spine ownership**~~ — ✅ **RESOLVED 2026-07-20: path (iii), combined project** (§6). This project owns Layers A–D + the absorbed R1.5 suggestion consumer (§4A); assistant-r1 closes at reactive. Remaining follow-through: record the scope move in the assistant project's docs.
2. **Project name** (header): confirm `spaarke-notification-spine-r1` vs an alternative. **Leaning KEEP** — messaging-r2/r3 and email-r4 already bind to this name in their specs, and ADR-047 is reserved under it; renaming now has real drift cost. The absorbed suggestion scope is recorded here (§4A) rather than in the name.
3. **Comms rule config store** — reuse Binding (`sprk_playbookconsumer`) + match conditions vs a comms-specific rule table.
4. **Azure SignalR footprint + MODE** — the §8A **gate-zero** spike: **Serverless vs Default mode** (Serverless recommended — send-only, matches the §3 producer topology, lighter footprint; the absorbed R1.5 design assumed Default — superseded pending the spike) measured against the 55/60 MB BFF bands + cold-start + CVE, **before** Layer-C placement is committed. Updated baseline **~46.24 MB**. If it breaches, the hub/negotiate moves out of the BFF.
5. **`kind` taxonomy** — lock the initial discriminator set (`suggestion`\|`communication-assessed`\|`communication-arrived`\|`job-complete`\|`share`\|`system-alert`; the two communication kinds per §5A.2). ⏰ **Now time-bound**: messaging-r3's spec FR-22 binds to `communication-arrived` + the §5A.3 envelope and needs the producer-trigger question (on capture vs on send → answer: persistence-time, §5A.6 #2) confirmed **at r3's P1**.
6. ~~**New ADR number**~~ — ✅ **RESOLVED: ADR-047** (gap held open by messaging-r2, which took ADR-048). Author main-session.
7. **"What lights up when `Notification` becomes routable" audit** (assistant-side, §3 convergence) — realizing the Layer-A/B legs flips the live `DispositionRoutability` `Notification` leg from `Routable=false` to routable. Enumerate every shipped chat capability that would then be able to emit a notification, and **sequence that behavior-surface change deliberately** — it is a change to the existing dispatch catalog's behavior, to be planned, not discovered post-merge. (Assistant-r1 can produce this audit from the dispatch side.)
8. **Suggestion action re-entry (assistant-side, §5B.3)** — confirm the Layer-A seam preserves the `SurfaceLaunch`/dispatch entry points + the P5 ack-contract, so acting on a proactive suggestion is behaviorally identical to a reactive dispatch (no parallel action path).
