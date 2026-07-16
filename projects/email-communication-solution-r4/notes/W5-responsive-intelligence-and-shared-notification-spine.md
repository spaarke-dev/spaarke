# Responsive Intelligence + the Shared Communication-Action / Notification Spine

> **Status**: Design-seed + handoff (authored 2026-07-16 by `email-communication-solution-r4` at W5 scoping).
> **Purpose**: Document *why* email-r4's Wave 5 (Responsive Intelligence, tasks 050–054) was **NOT built in this project**, and specify the **correct long-term architecture** so it can be set up as its own project (feeds `/design-to-spec`).
> **Owner directive (2026-07-16)**: "Close r4 at the engine/endpoint/PCF milestone; fully document this issue/requirement; we'll set it up as a project." + "Take into account we will also use this for the messaging (1.5) project and our chat capability."
> **Grounded against**: `projects/messaging-communication-app-r1/design.md` + `projects/spaarkeai-assistant-enhancements-r1/design.md` (both read 2026-07-16), and the live BFF `Services/Ai` + `Services/Communication` code.

---

## 1. TL;DR

email-r4 shipped the **entire server-side communication intelligence engine** (6-rung Association Engine, confidence→status ladder + auto-file, provenance, telemetry, a read-only suggestion endpoint) + the two OOB-form PCFs. **W5 — "Responsive Intelligence" (turn an assessed communication into auto-created Events / Tasks / Notifications) — cannot be correctly built inside email-r4** because it requires infrastructure that (a) doesn't fit the existing chat-oriented seams, and (b) is *already being designed by two sibling projects*. Building it here would fork that infrastructure.

**The right move**: build communication Responsive Intelligence as **one producer on a shared, `kind`-typed notification/action spine** that `spaarkeai-assistant-enhancements-r1.5` is standing up and `messaging-communication-app` will consume — coordinated so the spine is built **once**.

---

## 2. What email-r4 actually shipped (the coherent milestone)

| Capability | Status |
|---|---|
| Association Engine — rungs 0–3 (explicit-ref, thread, participant, structural detectors) | ✅ W1 (011–014) |
| Rung 4 — semantic record match (`IRecordMatchingAi` facade over `spaarke-records-index`) | ✅ 030 |
| Rung 5 — AI extract+classify (`ICommunicationClassificationAi` structured-output facade) | ✅ 031 |
| Confidence→status ladder + auto-file kill-switch (ADR-018) + provenance JSON | ✅ 015 |
| Per-rung telemetry + rung 4/5 + ladder invariant tests (349→352 Comm tests) | ✅ 032 / 074 |
| Read-only suggestion endpoint `POST /api/communications/{id}/suggest-associations` (evaluate-only engine path, never writes) | ✅ 074 (Path C) |
| Direction-symmetric enrichment (`ICommunicationEnrichmentService`) | ✅ 010 |
| Channel seams (`ICommunicationChannelSender`/`ICommunicationArchiver`) — the ADR-045 abstraction messaging-r1 builds on | ✅ 016 |

**The assessment signal already exists** — `CommunicationEnrichmentService` step 5 ("Responsive-Intelligence trigger") **emits a `communication_assessed` structured log today** (EMIT-ONLY, task 010). That log line is the exact publish point the RI project consumes. What's missing is the *consumer* (the fan-out), which is W5.

---

## 3. Why W5 cannot be built inside email-r4 (the finding)

Three hard blockers, verified in code 2026-07-16:

### 3.1 `EventRulesService.FireAsync` is chat-session/SSE-shaped — and its gates are semantically wrong for communications
- Signature: `IAsyncEnumerable<ChatSseEvent> FireAsync(SurfaceEventRequest request, …)` — **requires `SessionId` + `UserOid`** (throws without them), fetches the chat session ("Chat session not found"), and yields SSE events throughout.
- Its deterministic gates are **per-interactive-user / per-session by meaning**: "opt-out" is per user, "daily execution cap" is per user, "supersede" is per chat session, "M4 confidence" gates a live continuation.
- An **inbound email/communication has no interactive user and no session**. "Did the user opt out?" / "has this user hit their daily cap?" are meaningless for an auto-assessed inbound message. This is not a plumbing mismatch — the gate *semantics* don't apply.
- Task 010's **E5 escalation already flagged this**: the enrichment service *deliberately* only logs the assessment and does NOT call `FireAsync`, with a code comment stating the seam "does NOT fit a fire-and-forget `communication_assessed` emission."

### 3.2 OutputRouter's `record` + `notification` legs are unbuilt — and building them here forks the spine
- `DispositionRoutability.cs` (the ADR-043 single-source registry) marks `record` and `notification` as `Routable = false`, with reasons stating they need **net-new side-effect mechanisms** that are explicitly **"out of scope — land in a later wave."**
- So W5 051 ("remove the stub") is really "build a generic Dataverse-record-write leg + a notification-delivery leg inside the chat OutputRouter." The notification-delivery leg **is the notification spine** `assistant-r1.5` is already designing (§4). Building it here = a forked, communication-only hub — exactly what messaging-r1 §7 says **not** to do.

### 3.3 Gate 050 (the r2-core coordination gate the W5 POMLs depend on) never ran
- `notes/w5-ai-coordination.md` (the gate-050 output all four W5 POMLs cite) **does not exist** — 050 was blocked on `spaarke-ai-architecture-redesign-r2`, which is now **closed**. The "agreed boundary" the POMLs implement was never established. (email-r4 now *owns* `Services/Ai`, so there's no r2 to coordinate with — but the design was never settled either.)

---

## 4. The convergence — three projects, one spine (the decisive context)

W5's needs are **not email-specific**. Two sibling projects independently designed the *same* infrastructure, and both say to build it once, shared:

### 4.1 `spaarkeai-assistant-enhancements-r1` → R1.5 is building the spine
Per its design.md **§14.1a / §14.1b** (owner-ratified 2026-07-15), R1.5 delivers a **general server→client notification spine**, explicitly *not* suggestion-specific:
- **Typed envelope with a `kind` discriminator** (`suggestion | job-complete | share | system-alert | …`) on both the outbox row and the SignalR message.
- **Durable per-user outbox** = a thin new `sprk_` pending table (source of truth), with native `appnotification` as an optional mirror (renders in the MDA notification center + Daily Briefing).
- **Real-time delivery** = **Azure SignalR Service** (Default mode, hub-in-BFF, Standard tier; `Clients.User(oid)`), a best-effort accelerator over the durable outbox (degrades to next-load/poll).
- **Domain-action reuse** = `CreateNotificationNodeExecutor` already writes `appnotification`; `CreateTask`/`UpdateRecord` node executors already exist under `Services/Ai/Nodes/`.
- **Producer topology (verbatim)**: "in-BFF **background/hosted/Service-Bus jobs push directly via `IHubContext`**; the outbox is the durable source of truth; a producer that cannot reach SignalR is still correct — live push is acceleration, never a dependency."
- **§14.1b, verbatim intent**: "Proactive NBA suggestions are simply its **first (R1.5) consumer.** Design it general from day one to avoid a future second push mechanism... Job-completion, share, and system-alert kinds adopt the spine **incrementally**."

### 4.2 `messaging-communication-app-r1` → needs the same fabric, says coordinate it
Per its design.md **§7** (real-time & notifications):
- Three distinct real-time roles: (1) live chat = ACS transport, (2) AI streaming = SSE, (3) **cross-channel in-app fan-out (timeline/badge/toast when *any* communication persists) = SignalR candidate.**
- **Verbatim**: "SignalR has also been flagged as a candidate server-side notification provider for `spaarke-ai-assistant-enhancements-r1` (phase 1.5) — so a shared, reusable notification fabric may be desirable across projects rather than a messaging-only build... **R2 decides SignalR in coordination with `ai-assistant-enhancements-r1` so one fabric serves both — never a forked, messaging-only hub.**"
- Messaging is channel #2 on the *same* `sprk_communication` platform (ADR-045); it rides the same Association Engine + enrichment email-r4 built.

### 4.3 The synthesis
**Communication Responsive Intelligence is the third consumer of one shared spine:**

| Consumer | `kind` | Producer topology |
|---|---|---|
| Assistant proactive suggestions (`assistant-r1.5`, **consumer #1**) | `suggestion` | Daily-Briefing computed-state producer → grounded chip |
| Cross-channel comms fan-out (`messaging` R2) | `communication` / message-arrived | on-persist of any `sprk_communication` → timeline/badge/toast |
| **Communication Responsive Intelligence (this — the RI project)** | `communication` (+ auto-actions) | fire-and-forget `communication_assessed` producer → create Event/Task/Notification |

All three converge on the **same domain-action executors** (`CreateEvent`/`CreateTask`/`CreateNotification`) and the **same `kind`-typed outbox + SignalR delivery**. Building any one of them a bespoke way forks the other two.

---

## 5. Recommended architecture — "design it once" (the long-term vision)

The chat spine today **fuses four concerns** that must be layered so email, chat/messaging, and the assistant all reuse the core:

| Layer | What | Shared? | Notes |
|---|---|---|---|
| **A — Domain actions** | Create Event/Task/Notification → `sprk_event`/`task`/`appnotification` | ✅ **shared core** | Already exists as `Services/Ai/Nodes/*NodeExecutor`. Promote to a **session-agnostic seam** invokable without a chat session. |
| **B — Durable outbox** | `kind`-typed per-user pending table (source of truth) + `appnotification` mirror | ✅ shared | The `sprk_` outbox `assistant-r1.5` §14.1a layer 3 defines. RI writes `kind=communication` rows here. |
| **C — Real-time delivery** | Azure SignalR (Default mode, hub-in-BFF), `kind`-routed to the right client renderer | ✅ shared | `assistant-r1.5` §12.5 / §14.1a layer 4. Best-effort accelerator over B. |
| **D — Per-source policy** | *whether* to act | ⚠️ **share primitives, not scoping** | **chat** = user/session gates (EventRules — unchanged, *correct* for interactive chat); **comms RI** = **tenant/matter rules + confidence gate, fire-and-forget** (NEW, the right scoping for inbound); **assistant** = proactive gates (ADR-041 origin=proactive). |
| **Presentation** | SSE token/section streaming | ❌ chat-only | SSE stays a *chat presentation adapter*, not the spine everything threads through. |

**Communication Responsive Intelligence then = a bounded producer:**
1. A **fire-and-forget `communication_assessed` producer** — fires from `CommunicationEnrichmentService` step 5 (already emits the signal), best-effort/non-fatal (NFR-06). Runs in-BFF background/hosted/Service-Bus (matches `assistant-r1.5` producer-topology (a)).
2. A **communications policy layer** — tenant/matter-scoped rule config (Binding rows + match conditions) + cost/confidence gate, reusing gate *primitives* (NOT the chat user/session EventRules). Privilege is **flagged, never decided** (ADR-015).
3. Invokes **Layer A** domain actions (reuse `CreateEvent/Task/Notification` executors) + writes **Layer B** outbox (`kind=communication`) → **Layer C** pings live browsers.

This is exactly the "authoring-dominant, build the machine once" story both sibling designs already commit to — extended to a third source.

---

## 6. What the RI project builds (scope seed for `/design-to-spec`)

**Absorbs** email-r4 tasks **050–054** (re-homed — see §8). Proposed scope:

- **Coordinate with `assistant-enhancements-r1.5`** on the shared spine (Layers A–C). Two options for WBS to decide:
  - **(i) Co-design** — RI ships its `communication_assessed` producer + comms policy layer *onto* the r1.5-built spine (r1.5 owns A–C; RI is a fast follower). **Recommended** — avoids two spines, matches messaging-r1 §7's "one fabric" directive.
  - **(ii) Shared-spine project first** — a dedicated "notification/action spine" project builds Layers A–C; assistant-r1.5, messaging-R2, and RI all consume it. Cleaner ownership, more upfront sequencing.
- **Promote the domain-action executors** (`CreateEvent`/`CreateTask`/`CreateNotification`) to a session-agnostic seam (Layer A) — the reuse point for all three consumers.
- **Build the comms policy layer** (Layer D-comms): tenant/matter rule config + confidence gate; reuse gate primitives; privilege flag-only.
- **Wire the `communication_assessed` fire-and-forget producer** (replaces the EMIT-ONLY log in enrichment step 5).
- **`record` / `notification` dispositions**: realize them as the shared Layer-A action + Layer-B/C notification spine — **not** as bespoke OutputRouter legs (that was the fork risk in §3.2).
- **Reuse** `sprk_risk` gate semantics + the ONE confirmation gate (ADR-039/041) where an action is consequential/outward-facing.

**Do NOT**: route comms RI through `EventRulesService.FireAsync` (SSE/user/session-shaped — §3.1); build a communication-only notification hub (forks the shared spine — §3.2, messaging-r1 §7).

**New ADR recommended**: "Communication/notification action spine" (concise + full) codifying the four-layer separation, so assistant-r1.5, messaging, and RI build on the same layering by default. Author main-session (`.claude/` write boundary, root §3).

---

## 7. Cross-project dependencies & sequencing

| RI needs | From | Status |
|---|---|---|
| Association Engine + enrichment + `communication_assessed` signal | email-r4 (this) | ✅ shipped |
| Channel seams (ADR-045) | email-r4 task 016 | ✅ shipped |
| `kind`-typed outbox + Azure SignalR spine + domain-action executors | `assistant-enhancements-r1.5` | 🔜 designed (§14.1a/b), building in R1.5 |
| `CreateNotification`→`appnotification`→Daily Briefing | shipped (`NotificationService`) | ✅ exists |
| Messaging cross-channel fan-out (consumer of the same spine) | `messaging-communication-app` R2 | 🔜 designed (§7), deferred to R2, coordinate |

**Coordination imperative**: RI, `assistant-r1.5`, and `messaging` all touch the notification spine. Whoever builds Layers A–C first owns the contract; the others consume. Run `/conflict-check`; do NOT fork.

---

## 8. email-r4 close-out actions (this project)

1. Mark **050–054** in `TASK-INDEX.md` as **RE-HOMED** to the RI project (not "blocked" — the design decision is made). Cite this doc.
2. Update `spec.md` (FR-18/FR-19/FR-20 → note re-homed) + `plan.md` W5 section → pointer here.
3. Finish **W8 docs (080–082)** — and extend the communication-intelligence architecture doc (080) to **document this four-layer layering decision** so the next project inherits it (per owner: "fully documented").
4. Owner-side **043 deploy** remainder unchanged.
5. Close email-r4 at the engine/endpoint/PCF/docs milestone.

---

## 9. One-paragraph answer to "what is the best solution for our long-term vision?"

Build communication Responsive Intelligence as **one fire-and-forget producer on a single, `kind`-typed notification/action spine** — the same spine `spaarkeai-assistant-enhancements-r1.5` is already standing up (Azure SignalR + durable `sprk_` outbox + the existing `CreateEvent/Task/Notification` executors + Daily Briefing) and that `messaging` will consume for cross-channel fan-out. Separate the four fused concerns of today's chat spine — **shared domain-actions (A) + shared durable outbox (B) + shared real-time delivery (C) + per-source policy (D)**, with **SSE demoted to a chat-only presentation adapter**. This makes email, chat/messaging, and the assistant converge on one core with no forks, honors messaging-r1's explicit "one fabric, coordinated, never a forked hub" directive and assistant-r1.5's "general spine, suggestions are consumer #1" framing, and reduces every future trigger (a new communication rule, a new proactive producer, a new channel) to **authoring + one producer**, never new pipeline.
