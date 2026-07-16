# Spaarke Notification & Action Spine — R1 Design (Working Document)

> **Project ID (proposed)**: `spaarke-notification-spine-r1`
> **Status**: DRAFT design-seed — feeds `/design-to-spec`. **For review alongside `messaging-communication-app-r1` + `spaarkeai-assistant-enhancements-r1`.**
> **Date**: 2026-07-16
> **Origin**: `email-communication-solution-r4` W5 scoping surfaced that communication Responsive Intelligence needs shared infrastructure that two sibling projects independently designed. Owner directive (2026-07-16): "close r4 at its milestone; set up this shared capability as its own project with a name that reflects it serves different purposes in other contexts."
> **Grounded against**: `email-communication-solution-r4/notes/W5-responsive-intelligence-and-shared-notification-spine.md` · `messaging-communication-app-r1/design.md` §7 · `spaarkeai-assistant-enhancements-r1/design.md` §14.1a/§14.1b · live BFF `Services/Ai` + `Services/Communication` code.

> **⚠️ Name is proposed, for review.** This capability is broader than "notifications" (it also carries shared *domain actions*) and broader than "communications" or "responsive intelligence" (which are consumers). Alternatives to weigh: `spaarke-signal-action-spine-r1`, `spaarke-action-notification-fabric-r1`, `spaarke-responsive-platform-r1`. Keeping `notification-spine` for continuity — both sibling designs already call it that.

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
| Communication Responsive Intelligence | `communication` | **this (R1 proving producer)** | R1 |
| Proactive NBA suggestions | `suggestion` | `spaarkeai-assistant-enhancements-r1.5` | coordinate — see §6 |
| Cross-channel messaging fan-out | `communication`/message-arrived | `messaging-communication-app` R2 | R2 |
| Job-completion / share / system-alert | those kinds | later, incremental | later |

Each new consumer = a renderer branch (client) + a producer (server) + authoring — **never new spine**.

---

## 6. The key coordination decision (for owner review)

**Who builds Layers A–C?** `spaarkeai-assistant-enhancements-r1.5` is currently scoped to build the SignalR + outbox spine *inside its proactive-push work*. That would fork the platform capability into one consumer. Two paths:

- **(i) This project owns the shared spine (recommended).** Extract Layers A–C out of assistant-r1.5 into this project as **platform infrastructure**; assistant-r1.5 becomes a *consumer* (`kind=suggestion`) of it, messaging R2 another, comms-RI the R1 proving producer. Honors "design once"; matches messaging-r1 §7's "one fabric, coordinated." Cost: re-sequences a slice of assistant-r1.5.
- **(ii) assistant-r1.5 builds the spine; this project is the comms-RI consumer + the domain-action extraction.** Less re-sequencing; but the spine is born inside one consumer and must be generalized later (assistant-r1.5 §14.1b at least designs it general, mitigating this).

**Recommendation: (i)** — a thin, well-owned platform project prevents the exact "second push mechanism" root §11 warns against, and both sibling designs already point at a shared fabric. Decide at spec intake with all three project owners.

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

## 9. Risks
| # | Risk | Mitigation |
|---|---|---|
| R-1 | Spine ownership ambiguity across 3 projects → two hubs | §6 decision at spec intake with all owners; one project owns A–C |
| R-2 | Azure SignalR SDK breaches 60 MB BFF ceiling | Measure in a Phase-0 spike before committing placement |
| R-3 | Comms policy layer duplicates chat gate logic | Reuse gate *primitives*; do NOT reuse chat user/session scoping |
| R-4 | Domain-action extraction regresses chat dispatch | Session-agnostic seam behind the existing executors; keep chat path green (seam tests) |
| R-5 | Privilege auto-decided in fan-out | ADR-015 flag-only; explicit review gate (security-sensitive) |
| R-6 | Over-building real-time before consumers exist | R1 ships spine + ONE producer (comms-RI); other consumers adopt incrementally |

---

## 10. Decisions to resolve before `/design-to-spec`
1. **Spine ownership** (§6 — the big one): this project owns Layers A–C vs assistant-r1.5 builds them. Decide with all three owners.
2. **Project name** (header): confirm `spaarke-notification-spine-r1` vs an alternative.
3. **Comms rule config store** — reuse Binding (`sprk_playbookconsumer`) + match conditions vs a comms-specific rule table.
4. **Azure SignalR footprint** — Phase-0 spike vs the 60 MB ceiling.
5. **`kind` taxonomy** — lock the initial discriminator set (`suggestion`\|`communication`\|`job-complete`\|`share`\|`system-alert`).
6. **New ADR number** — confirm + author main-session.
