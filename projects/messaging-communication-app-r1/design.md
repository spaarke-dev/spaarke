# Messaging Communication App — R1 Design (Technical Review, Assessment & Plan)

> **Project**: `messaging-communication-app-r1`
> **Status**: Draft v2 (design) — feeds `/design-to-spec`. Revised 2026-07-16 after ACS + SignalR knowledge updates and R4-completion code review.
> **Source idea**: `spaarke-messaging-solution-synopsis.md` (Draft v2), folded into the real Spaarke platform
> **Owner clarifications applied**: 2026-07-16 (topline MUST-NOTs; points 1–7; §14 answers)
> **Builds on (complete)**: `email-communication-solution-r4` — ADR-045 channel seams shipped (`Services/Communication/Channels/`)
> **Coordinates with**: `spaarke-notification-spine-r1` (shared notification fabric — messaging is a consumer, R2)

---

## 0. Executive Summary

Spaarke has **one communication platform**: the `sprk_communication` entity and its send/capture/enrichment pipeline, governed by **ADR-045**. Email is the first channel. This project adds **messaging (real-time chat) as the second channel** — *not* a new module, app, or parallel pipeline. Azure Communication Services (ACS) is the message **transport**; **Dataverse is the system of record** (every message persists as a `sprk_communication` record); the **BFF is the sole policy-enforcement and token-minting point**.

**R1 delivers the messaging plumbing, the communication thread data model, and a working read/write message experience — async auto-sync, *not* a live open channel — inside the Spaarke model-driven app (MDA).** Concretely:

1. The **messaging channel provider** — a new `ICommunicationChannelSender` + `ICommunicationArchiver` (+ a net-new inbound ingestor) over the ADR-045 seams that email-r4 shipped. The second implementation, proving the abstraction.
2. **ACS integration (server-side)** — identity mapping, server-side token minting, Event Grid capture — reusing the existing job contract and association engine. **Like email's `.eml`, an ACS chat message is a transport artifact (a JSON event) that the BFF processes into a `sprk_communication` (type=Message) record + a transcript archive in SPE. This layer has no client-side ACS SDK, no React, no composites.**
3. **First-class thread data model + a polling timeline view** — a thin, queryable **thread grouping key** (recommended: `sprk_communicationthread` entity + `sprk_thread` lookup) that groups email reply-chains *and* chat conversations channel-agnostically, plus a **conversation/timeline component** (`@spaarke/ui-components`, Fluent v9) that renders it. **The grouping does not exist today** (email "threading" is invisible association-matching). R1's timeline is **polling-based** (auto-sync, no live channel).
4. **`sprk_communication` + thread schema extensions** (thread key/lookup, thread↔channel child table, privacy, internal-only, privilege, ACS correlation keys).
5. The **read/write message front-end inside the MDA** — an OOB or custom-code form/component with **automated send / receive / sync + a notification (unread) indicator**, so messages flow back and forth seamlessly. The UX model is **OOB Activities / portal comments**: async, auto-refreshing on a short poll, unread-badged — **not a live open channel**. Send → BFF → ACS; receive → Event Grid → persist → the front-end **polls the BFF** and updates. **No ACS client SDK / no live WebSocket.**
6. **Cross-channel content reuse** — share an email's content into a message and vice-versa, as inline quoted content, both directions (via the channel-agnostic `sprk_body`).

**R1 explicitly does NOT** build the **live open channel** (WebSocket real-time append, typing indicators, read-receipts), the **spine-pushed** cross-surface notification (that badge is polling-based in R1; push comes via `notification-spine-r1` in R2), SMS, the Teams tab, or the external-portal surface — those are the deferred upgrade / R2 / R3, and R1 is structured so they add to the same component/engine without re-opening it.

**Guiding principle (owner directive): build what is necessary and sets up R2+ — no shortcuts, no over-engineering.** R1 delivers a *usable* async messaging experience (Activities-style) on a thin thread key; the *live open channel* and *pushed* notifications are the layered-on upgrade.

---

## 1. Scope Boundaries — MUST / MUST NOT

### ❌ MUST NOT (platform-wide, owner-locked)

- **MUST NOT** use Dataverse/Power Apps **Activities**, OOB `email`/activity entities, or **portal comments** as any part of the communication model. (ADR-045: Graph + `sprk_communication` only; legacy OOB `email` subsystem is retired.)
- **MUST NOT** use or capture **native Teams chat** (Graph chat/channel-message APIs) — not exposed to a third-party ISV for this use, and it conflicts with record ownership. Teams participates later only as a **host surface** (tab), never as transport or system of record.
- **MUST NOT** introduce a second communication pipeline, a second regarding mechanism, or a second channel-provider contract. Messaging implements the **ADR-045** seams.

### 🎯 R1 IN SCOPE

- Messaging channel provider (ACS Chat transport, **server-side**): sender + archiver + **inbound ingestor** (net-new — no ingestor seam exists yet).
- ACS integration + Event Grid capture → existing job/engine pipeline. **No client-side ACS SDK in R1.**
- **First-class thread data model** — thin thread grouping key (recommended `sprk_communicationthread` + `sprk_thread` lookup) + **thread↔channel child table** (D-03), grouping email reply-chains and chat.
- **Read/write message front-end with automated send / receive / sync + unread indicator** — a **polling** conversation/timeline component (`@spaarke/ui-components`, Fluent v9) + PCF send/respond accessories, all calling the BFF. Activities / portal-comments UX; **no live channel**.
- `sprk_communication` extensions (privacy, internal-only, privilege, ACS keys).
- Privacy/internal-only/privilege composed from **existing Communication/Dataverse record security** (§5).
- **Bidirectional inline content quoting** (email↔message) via the channel-agnostic `sprk_body`.
- Attachments/documents on a message (reuse the SPE → `sprk_document` model).

### ⏭️ R1 OUT OF SCOPE → deferred upgrade / R2 / R3

- **Live open channel** — the *real-time* upgrade to the R1 timeline component: WebSocket live append, typing indicators, read-receipts. R1's timeline is polling-based; the live upgrade layers the headless `@azure/communication-chat` SDK onto the **same** component (**never the ACS composites**). This is where a client-side ACS SDK and the React-version question actually live — R1 doesn't touch them.
- **Spine-pushed cross-surface notification** — R1's unread indicator is polling-based; pushed badges/toasts across surfaces come via `notification-spine-r1` in R2 (§7).
- **SMS** channel — same ACS resource, but a **hard 5–6 week toll-free-verification regulatory gate** + TCPA/opt-out + number provisioning (§8.8). Deferred so it isn't on the critical path. Channel-ref table is SMS-ready.
- **Teams tab** app, **external-portal** messaging surface (BYOI participants) — R2/R3.
- **SignalR cross-surface notification fabric** — owned by `spaarke-notification-spine-r1`; messaging consumes it in R2 (§7).
- Historical email **backfill** into the thread model (R1 is point-forward); voice/video; retroactive privacy bulk-open tooling.

---

## 2. Current-State Truth (grounded against R4-complete code, 2026-07-16)

Not greenfield. Verified against the `email-communication-solution-r4` worktree (shipped) + main tree:

| Capability | Status | Detail |
|---|---|---|
| `sprk_communication` entity + send/capture/enrichment | **EXISTS** (R2+R4) | `Services/Communication/` — mature |
| **Channel seams (send + archive)** | ✅ **SHIPPED (R4 016)** | `Services/Communication/Channels/`: `ICommunicationChannelSender`, `ICommunicationArchiver`, `CommunicationChannelDispatcher` (keyed by `CommunicationType`), `EmailChannelSender`, `EmailArchiver`. `CommunicationService` is now Graph-free; dispatch by `sprk_communicationtype`. **A new channel = register 2 impls + a `CommunicationType`.** |
| **Inbound ingestor seam** | ❌ **NONE** | Inbound is concrete/email-only (`IncomingCommunicationProcessor` + `Engine/`). Messaging inbound is **net-new** (and R1 should abstract it as a seam — completes ADR-045's stated intent). |
| Association engine + normalized envelope + enrichment (both directions) | ✅ EXISTS (R4) | `Engine/`, `ICommunicationEnrichmentService` |
| Outbound send (persist-on-send) | ✅ EXISTS | `CommunicationService.SendAsync` |
| **Thread / conversation ENTITY** | ❌ **NONE** | No thread entity, no thread↔channel table, no `sprk_conversationid`. "Threading" = `ThreadContinuityRung` copying a parent's *regarding* onto a reply (via `sprk_internetmessageid` / `sprk_inreplyto` text fields). **A matter shows a flat list of messages, not grouped threads.** |
| **Thread / timeline VIEWER component** | ❌ **NONE** | No `*Timeline*`/`*Thread*`/`*Conversation*` component in `@spaarke/ui-components`. `<EmailComposer/>` is compose/send-only. Viewing = OOB form, one record at a time, + flat subgrid. |
| Communication UI surface | ✅ SHIPPED (R4 W4 pivot) | **OOB `sprk_communication` main form + PCFs** (ADR-026 Path-A exception). `CommunicationActions` PCF (compose/send via `SendEmailPage`) + `CommunicationConnections` PCF (regarding review). Code Page host was **superseded**. |
| Body content | ✅ EXISTS | `sprk_body` (multiline, ≤100000) + `sprk_bodyformat` on the record — channel-agnostic, readily BFF-retrievable; `.eml` in SPE is a full-fidelity *archive* in addition. |
| Attachments | ✅ EXISTS, channel-agnostic storage | attachment → SPE → `sprk_document` (`sprk_document.sprk_communication` lookup + `sprk_communicationattachment` intersection). **Materialization is email-specific + NOT seamed** — a new channel writes its own materialization feeding the same shape. |
| Service Bus job contract, `SpeFileStore`, regarding family (ADR-024), BFF auth/filters | ✅ EXISTS | Reuse verbatim |
| **ACS** | ❌ NONE | No `Azure.Communication.*` — entirely net-new |
| **Azure SignalR** | ❌ NONE (owned by `notification-spine-r1`) | Real-time today is SSE (ADR-033) |
| BFF publish baseline | ~**45.30 MB** post-R4 | R4 retired OOB email → smaller than the older 49.63 MB figure |

**Implication:** the send/archive rails, record model, body field, attachment model, and OOB-form+PCF surface pattern are reusable. The genuinely net-new work is: ACS integration, the **inbound path**, the **thread model + timeline component**, and privacy schema.

---

## 3. Architecture — Folding the Synopsis Into the Platform

### 3.1 Principles (inherited from ADR-045)

1. **One process; messaging is a provider.** New channel = new sender/ingestor/archiver + a normalizer to the envelope; the engine, enrichment, regarding model, and thread model do not change per channel.
2. **Dataverse is the record; ACS is the transport.** Every durable fact is a `sprk_communication` row; ACS threads are reconstructible projections.
3. **BFF is the sole policy-enforcement point (D-07).** Clients never hold ACS admin capability; the BFF mints scoped ACS tokens and owns thread/membership mutation.
4. **Authorization derives from existing Dataverse/UAC security (point 4-privacy).** ACS thread membership is a **reconciled projection** of Dataverse-derived access — never a separate ACL.
5. **Async by default.** Capture, membership sync, notification use the existing job contract.
6. **Direction-symmetric persistence (point 1).** Outbound persists on send; inbound persists on event; both invoke enrichment (§4).
7. **Threads are first-class and channel-agnostic (point 4).** One thread model + one timeline component serve every channel.

### 3.2 Reuse / Extend / Build map (§11 Component Justification)

| Category | Component | Justification |
|---|---|---|
| **Reuse as-is** | send/archive channel seams + dispatcher; association engine + envelope; enrichment; regarding family (ADR-024); Service Bus job contract; `SpeFileStore`; `sprk_communication` record + `sprk_body`; attachment storage model; BFF auth + endpoint filters; OOB-form+PCF surface pattern | These are the platform. |
| **Extend** | `CommunicationType` (+1 value for messaging); `ThreadContinuityRung` (also assign the new `sprk_thread` lookup); `CommunicationConnections`/actions PCF patterns; `sprk_communication` schema | Extension over new component per §11. |
| **Build (net-new, R1)** | ACS integration (identity map, server token minting, Event Grid ingress, **archiver → message-transcript artifact to SPE**); **inbound ingestor** (+ `ICommunicationChannelIngestor` seam); **thread grouping key** (`sprk_communicationthread` + `sprk_thread` + thread↔channel table); privacy/internal-only/privilege columns + BFF enforcement; **ACS attachment materialization**; **BFF thread-read + unread-count endpoints**; **polling conversation/timeline component** (Fluent v9, shared lib, PCF) + **PCF send/respond accessories** | Concrete cost-of-doing-nothing: no chat transport, no inbound capture, no thread grouping, no usable message UX. The component reads persisted records + polls — **no client-side ACS SDK**. |
| **Next project / R2 (not R1)** | **Live-channel upgrade** to the same component (headless `@azure/communication-chat` → WebSocket append/typing/receipts); **spine-pushed** cross-surface notification | Layered onto R1's component; R1's polling version is a usable experience on its own. |

### 3.3 Conceptual diagram (R1 — MDA surface)

```
        ┌───────────────────────────────────────────────────────┐
        │            Spaarke Model-Driven App (R1 host)          │
        │  OOB sprk_communication / thread main form:            │
        │   • Polling conversation/timeline component (Fluent v9)│
        │     email+chat, unread indicator, compose box          │
        │   • PCF accessories: send / respond (call the BFF)     │
        │   (live-channel upgrade = next project)                │
        └───────────────┬───────────────────────────────────────┘
     Entra token (MSAL) · polls BFF · no client-side ACS SDK in R1
                             ▼
   ┌───────────────────────────────────────────────────────────┐
   │              BFF / Spaarke API  (policy enforcement)       │
   │  Dataverse-derived authZ │ ACS token minting │ thread &    │
   │  membership mgmt │ CommunicationService (channel dispatch) │
   │  → messaging sender/archiver/ingestor over ADR-045 seams   │
   └───┬───────────────┬────────────────┬──────────────┬────────┘
       │ persist-      │ ACS Chat SDK    │ enrichment   │ emit
       │ on-send       │ (live thread)   │ (both dirs)  │ communication_assessed
       ▼               ▼                 ▼              ▼
  ┌──────────┐  ┌──────────────┐  ┌───────────┐  ┌────────────────────────┐
  │ Dataverse│  │ ACS Chat     │  │ Assoc.    │  │ spaarke-notification-  │
  │ thread + │  │ (transport)  │  │ Engine +  │  │ spine-r1 (separate     │
  │ sprk_comm│  └──────┬───────┘  │ RAG       │  │ project; messaging is  │
  │ + channel│         │ Event Grid └───────────┘  │ a consumer in R2)      │
  │ -ref+SPE │         ▼ (inbound only)             └────────────────────────┘
  └────▲─────┘  ┌────────────────────────────────┐
       │ persist│ Webhook ingress → Service Bus  │
       │ on-event│ → normalizer → ingestor →     │
       └─────────│ capture → enrichment          │
                 └────────────────────────────────┘
```

Live chat (typing/read-receipts/delivery) is ACS's own real-time. R1 builds **no** notification fabric; cross-surface fan-out is deferred to R2 as a consumer of `notification-spine-r1` (§7).

---

## 4. Core Flows — Outbound Pattern Corrected (point 1)

> **This resolves the synopsis's D-02.** Per owner direction and ADR-045 rule 3, R1 follows the platform's outbound pattern: **persist-on-send**. Only *inbound* persists on event. Both directions invoke enrichment — direction symmetry preserved.

- **Send (outbound — persist-on-send):**
  `PCF send/respond accessory → BFF (authZ) → CommunicationService dispatches to the messaging ICommunicationChannelSender → ACS SendMessage (server-side, using a server-minted token for the sending user) → persist sprk_communication (thread lookup set) → enrichment (association + RAG)`. ACS delivers live to any participant that has a live client (future surfaces); R1's MDA sender has no live client, so its own view updates on subgrid refresh.
  **Echo-dedup:** ACS emits an Event Grid `ChatMessageReceivedInThread` for our own outbound message; the inbound ingestor **dedupes on ACS message id** via the existing `IIdempotencyService`, so the echo is a no-op. This is the seam that lets outbound stay persist-on-send while inbound stays capture-on-event without collision.

- **Inbound (persist-on-event — net-new ingestor):**
  `participant sends → ACS → Event Grid → webhook ingress (validated) → Service Bus job → ACS-event normalizer → NormalizedMessage → ingestor persists sprk_communication (thread lookup set) → enrichment`. Reuses the engine + job contract; the ingress adapter + normalizer are net-new (no ingestor seam existed).

- **Thread creation:**
  `create/lookup sprk_communicationthread → compute participants from Dataverse record access (or named private list) → create ACS identities for participants (communicationUserId ↔ Dataverse user/contact) → create ACS thread (30-day retention or delete-post-persist, §8.7) → write channel-ref row (ACS ChatThreadId) → set ACS membership`. Chat tokens are minted **server-side** as needed (uniform for internal, later external — Entra→ACS exchange is VoIP-only, §8.2) and **used by the BFF to act as participants; no token reaches a browser in R1** (a browser token is only needed by the live client, next project).

- **Membership sync (job):**
  `Dataverse record-access change | privacy switch | participant edit → job recomputes authorized set → reconciles ACS participants (AddParticipants/RemoveParticipant) → audit entry`. Dataverse is authoritative; ACS membership is the projection. Eventually consistent — reconcile via event + periodic sweep (§8.4).

---

## 5. Privacy & Access — Compose From Existing Security (point 4)

**Owner directive: leverage Spaarke UAC + the Dataverse / Power Apps / Power Portal security model. Do not rebuild core access control.**

**The access boundary is the Dataverse record, exactly as with email.** The `sprk_communication` (and `sprk_communicationthread`) record *wraps* the message and **is** the authoritative access boundary — whoever can read the record can read the message. The archived artifact in SPE (the `.eml` for email, the chat transcript for messaging) does **not** hold a separate ACL we depend on; it inherits the matter container's governance, which aligns with the record. The one chat-specific addition: **ACS keeps its own thread membership** (it won't deliver *live* messages to a non-member), so we **reconcile ACS membership from Dataverse access** — a projection for the transport/live layer only, never a second source of truth.

| Concern | Mechanism (reuse) | R1 net-new |
|---|---|---|
| **Record-scoped (open) threads** | Membership derives from **`MembershipResolverService` (ADR-034)** — Spaarke's canonical "who is associated with this matter/record" (owner, assigned attorney, assigned firm, …). No record access → no message access. Enforced at BFF. | Reuse `IMembershipResolverService`; reconcile job |
| **Private threads** | An explicit named-participant **grant** on the thread record, reusing Spaarke's existing per-record sharing (`PlaybookSharingService`-style `GrantAccess`, or the `sprk_externalrecordaccess` overlay — Phase 0 picks the detail). Thread-level privacy lives on the **thread entity**. | Thread privacy-state field + grant call + BFF filter |
| **Private messages** | Message-level privacy flag on `sprk_communication`. | Field + BFF filter |
| **Internal-only messages** | **User attribute (D-05)**; external participants (R2/R3) never see them. | Visibility flag + BFF query filter |
| **Privilege designation** | Classification metadata, **distinct from** privacy; AI may *flag* but never *decide* (ADR-015). | Field; composes with privacy |
| **Privacy switch (D-04)** | **Point-forward**: opening a private thread opens it from that moment; prior private messages stay restricted; retroactive open is a separate audited bulk action. | Point-forward logic + audit |
| **Ethical walls** | Record-level security **is** the wall; messaging inherits it via membership derivation. | Phase 0 confirms exact mechanism |

**Net:** a small number of `sprk_communication` + thread fields + BFF query filters + a reconcile job. No new authorization engine. ACS membership is always a reconciled projection of Dataverse access.

> **Security-sensitive (root §6):** the private-thread / privilege model is R1's highest-risk area. **Mechanism resolved:** open = ADR-034 `MembershipResolverService`; private = existing per-record sharing grant. Phase 0 verifies only the private-grant detail (`GrantAccess` vs `sprk_externalrecordaccess`); enforcement gets explicit code-review + `adr-check` at Step 9.5.

---

## 6. Communication Threads — the Integrated View (point 4, the headline)

**Threading has two orthogonal dimensions** — the owner's framing, made precise:

- **Dimension 1 — "regarding" (what a communication is *about*):** the matter / project / etc. a message relates to, via the ADR-024 regarding family. `ThreadContinuityRung` inherits this. Understood + shipped.
- **Dimension 2 — reply-chain / conversation (which *conversation* a message belongs to):** the Gmail-style grouping users expect. For **email**, the reply-chain **linkage data already exists** — every message stores `sprk_inreplyto` (parent's Internet-Message-Id) + `sprk_internetmessageid`, and the rung walks that ancestry — **but there is no thread record grouping them and no grouped view**, so today you can only *reconstruct* a chain by following pointers. For **chat**, ACS's `ChatThreadId` natively groups messages.

R1 gives **dimension 2 a first-class home for all channels** — a thread record that reply-chains (email) and ACS threads (chat) both map onto — so a conversation can be grouped and, later, viewed as one timeline. Per owner Q4, **R1 builds the data structures; the view is a subgrid**; the rich live timeline component is the next project.

### 6.1 Thread model — a thin, *queryable* grouping key (why, and the alternatives considered)

The grouping key must be **queryable** — the timeline component (and any subgrid) renders "the messages in this conversation" by *filtering* on it. That **rules out a JSON blob** (Dataverse can't filter inside JSON) and **rules out leaning on *regarding*** (regarding is the orthogonal "about" dimension — a matter has many conversations, so `regarding=matter` can't separate them). Two queryable options; R1 recommends (A):

- **(A, recommended) `sprk_communicationthread` entity + `sprk_communication.sprk_thread` lookup** `[NEW]`: the entity is **thin** — topic, anchor (ADR-024 regarding family, not a new mechanism), thread-level privacy state, participant set — and is the home for the **ACS `ChatThreadId`** + the D-03 channel-ref rows. Gives thread-level privacy (D-04) and participants a real home; timeline query = `filter sprk_thread = X`.
- **(C, lighter fallback) a `sprk_conversationid` field** on `sprk_communication` (email = ancestry-root id; chat = ACS `ChatThreadId`; R4 already anticipated this field): groups + drives the timeline with no new entity, but thread-level privacy/participants have **no home** (privacy applied per-message). Upgradeable to (A) later.
- **(B, rejected) regarding + JSON** — wrong dimension + not queryable.

- **Thread↔channel child table** `[NEW]` (D-03): one row per `(thread, channel, external-ref)`. R1 populates the **ACS `ChatThreadId`**; email/SMS refs attach later with no schema change — "channel is an attribute."
- **Reply-chain fine-structure** (which message replies to which, *within* a thread) uses the existing `sprk_inreplyto` parent-pointer — no JSON needed there either.
- **Email retrofit (point-forward):** extend `ThreadContinuityRung` so that when it walks RFC-2822 ancestry it also **assigns the thread** (sets `sprk_thread` / conversation id). New replies join their parent's thread automatically. Historical backfill is **out of R1 scope**. No new matching logic — reuses the ancestry the rung already computes.

### 6.2 R1 view = a polling conversation/timeline component; live-channel upgrade = next project

- **R1:** a **polling conversation/timeline component** in `@spaarke/ui-components` (Fluent v9), packaged as a PCF, renders a thread's messages — email and chat interleaved, channel-badged, ordered, with reply nesting from `sprk_inreplyto` — plus a **compose/send box** and an **unread indicator**. It **reads persisted `sprk_communication` rows from the BFF** (Dataverse is the record) and **polls** a BFF thread-read endpoint on a short interval so new inbound/outbound messages appear automatically (the Activities / portal-comments UX). Reuses `<EmailComposer/>` sub-components where sensible. **No client-side ACS SDK** — viewing history and syncing are pure Dataverse/BFF reads.
- **Next project / R2 — the live-channel upgrade:** layer the headless `@azure/communication-chat` SDK onto the **same** component for real-time append, typing indicators, and read-receipts (replacing the poll with a live subscription), and adopt `notification-spine-r1` push for cross-surface badges. **This — not R1 — is where a client-side ACS SDK lives; ACS composites are explicitly not used.**
- Read model: the BFF returns a thread's `sprk_communication` rows (`sprk_body`, channel type, sender, attachments) + an unread count from Dataverse — no ACS call needed to view/sync history.

### 6.3 Cross-channel content reuse (point 5 — inline quoted, both directions, R1)

- Because `sprk_body` is the **channel-agnostic body** on every message, sharing content across channels is a read-format-prefill operation, not an extraction:
  - **Email → message:** read source `sprk_body`/`sprk_bodyformat`, format as inline quote, prefill the message composer.
  - **Message → email:** read message `sprk_body`, quote into the email composer.
- Both directions ship in R1. Net-new = a "quote into…" action + quote formatting; no `.eml` parsing needed (only required for byte-exact forwarding with original attachments — out of R1 scope).

### 6.4 Attachments on a message (point 6)

- Reuse the channel-agnostic storage model: **attachment → SPE → `sprk_document` → `sprk_document.sprk_communication` lookup + `sprk_communicationattachment` intersection**. A chat file share uploads to SPE, creates the doc, links it, and the ACS message carries a **reference** (not the binary — SPE is the store). Rendered as a file card in the timeline.
- Net-new = the messaging **attachment materialization** step (ACS/file → SPE → doc), since materialization is not behind the archive seam. Storage schema is unchanged.

### 6.5 Email-process impact of the thread model (Q2) — an extension, and a feature

**Grouping key is LOCKED to (A): `sprk_communicationthread` entity + `sprk_thread` lookup.** Introducing it **does touch the shared email process** — email must be assigned to threads too (that is the point: *one* integrated thread model for all channels). The change is an *extension* of what already exists, not a rewrite:

- **Thread assignment is direction-symmetric and channel-agnostic** — a small `IThreadResolver` (find-or-create `sprk_communicationthread`, return `sprk_thread`) invoked by **both** the inbound capture path and the outbound send path, for every channel. Email uses the `sprk_inreplyto` ancestry the engine already computes (reply-chain root → thread); messaging uses the ACS `ChatThreadId`. Mirrors the direction-symmetric enrichment pattern (ADR-045 rule 3).
- **Inbound email** (`IncomingCommunicationProcessor` / `ThreadContinuityRung`): today it inherits *regarding* from the parent; it now **also** sets `sprk_thread` (join the parent's thread, or create one). The ancestry walk is reused — no new matching logic.
- **Outbound email** (`CommunicationService` send path): a reply **joins** the replied-to message's thread; a fresh email **creates** a thread. One added resolver call.
- **Config / setup:** add `sprk_communicationthread` + `sprk_thread` lookup + the D-03 child table to the solution; the email OOB form optionally surfaces a thread column / the timeline component (additive). **Point-forward:** historical emails have no thread and remain ungrouped until an optional backfill (deferred, out of R1).
- **This is a feature for email, not just a side effect** — once the thread model lands, **email conversations become grouped reply-chain threads** in the same integrated view as chat. That *is* the "integrated communication capability."

**Coordination:** this modifies shared `Services/Communication/` code that email-r4 shipped, and the same area `notification-spine-r1` touches (its envelope carries `threadId`, §5A). Run `/conflict-check`; align the thread contract with both at joint intake.

---

## 7. Real-Time & Notifications — Consume the Notification Spine (point 3)

> ✅ Knowledge update complete (2026-07-16). **The bespoke `INotificationPublisher` seam from v1 is scrapped** — the shared fabric is its own project, `spaarke-notification-spine-r1`, and messaging is a **consumer**, not a builder.

**Three real-time planes, three owners (not over-engineering):**

| Plane | Owner | Job | R1 |
|---|---|---|---|
| (a) **Live chat thread** | ACS WebSocket | messages/typing/read-receipts to *participants* | Bought with ACS |
| (b) **AI token streaming** | SSE (ADR-033) | per-request stream | Unchanged |
| (c) **Cross-channel fan-out** | **`notification-spine-r1`** (Azure SignalR) | timeline/badge/toast when any comm persists, to all a user's surfaces | **Consumed in R2** |

**Why messaging builds no notification *fabric* in R1:** messaging messages already flow `persist → CommunicationEnrichmentService → emit communication_assessed`. The notification spine's R1 proving producer *consumes* `communication_assessed`. So once messaging (a) persists as `sprk_communication` and (b) runs enrichment — **both already in R1 scope** — it is spine-ready for free. R1 stands up **zero** SignalR: its unread indicator + timeline sync are **polling-based** (the component polls the BFF thread-read / unread-count endpoints on a short interval — the Activities / portal-comments model). R2 (multi-surface) swaps the poll for **spine push** (`communication-arrived`), with no messaging-side fabric.

**Coordination note for the spine team:** their `kind` taxonomy should distinguish **"communication-assessed"** (Responsive-Intelligence *action*) from **"message-arrived"** (lightweight UI *refresh*) — messaging's fan-out wants the latter and should not require a full RI assessment to fire a badge. Raise at joint spec intake. I concur with spine §6 **path (i)** (spine as owned platform infra; assistant-r1.5, messaging-R2, comms-RI as consumers).

*(When R2 adopts: Azure SignalR Serverless + Management SDK, publish from the fan-out job; `Clients.Group(matterId)` / `Clients.User(oid)`; Entra JWT auth; ~1–2 MB SDK; ~$49/mo/unit Standard. Details in the spine project.)*

---

## 8. ACS Integration — Deployment in Spaarke

> ✅ Knowledge update complete (researcher, 2026-07-16; `.claude/agent-memory/researcher/acs-chat-integration-2026-07-16.md`). Transport-vs-record is a confirmed, Microsoft-precedented pattern (D365 Contact Center). The BFF maps onto ACS's "trusted service" role.

### 8.1 Architecture fit
- **Trusted service + client** = documented ACS model. The BFF *is* the trusted service (mints identities+tokens, adds/removes participants); clients hold only short-lived user tokens. **0–250 participants/thread**, **~28 KB/message**.

### 8.2 Identity & tokens — uniform server-side minting
- **Entra→ACS token exchange is VoIP-only; Chat is NOT available via it.** → **even internal Entra users get chat tokens minted server-side by the BFF** — one uniform path (`createUser`→`getToken(["chat"])` or `createUserAndToken`; 1–24h, default 24h; client refresh callback). Same path serves BYOI externals in R2.
- **Identity mapping:** persist ACS `communicationUserId` on the Dataverse user/contact (sender resolution + token re-issue).

### 8.3 Event capture (Event Grid — current recommended path)
- Events: `ChatMessageReceivedInThread`, `...Edited/Deleted`, `ParticipantAdded/RemovedToThread`, etc. Webhook subscription-validation handshake (echo `validationCode`).
- **At-least-once, unordered, may duplicate → ingestor MUST be idempotent** (dedupe on ACS message id — same mechanism as §4 echo-dedup). Exponential-backoff retry + **dead-letter to Storage from day one**.

### 8.4 Membership sync
- Server-side `ChatThreadClient.AddParticipants` / `RemoveParticipant` from the BFF. Rate limits: 10/10s + 30/min per thread; 3000/min per resource. **Eventually consistent** with Dataverse → event-driven reconcile + periodic sweep. **Threads >20 participants lose read receipts + typing** (fine for R1 matter threads; note for R2).

### 8.5 UI decision — R1 has NO client-side ACS SDK (the composites/React question is deferred)
- Because R1 builds **no live-channel client** (the R1 timeline component is *polling*, reading persisted records from the BFF; §6.2), **there is no client-side ACS SDK in R1 at all** — the BFF is the only thing that talks to ACS. The composites-vs-headless and React-version questions therefore **do not arise in R1**; they belong to the next project (the live-channel upgrade).
- **When the live UI is built (next project):** use the **headless `@azure/communication-chat` SDK (no React peer dep) + `@spaarke/ui-components` Fluent v9**, packaged as a PCF. **The ACS composites (`@azure/communication-react`) are explicitly NOT used** — they bundle Fluent v8, are unsupported on React 19, and drag in `@azure/communication-calling`; the headless SDK avoids all three and stays on the Spaarke design system. (Recorded here so the next project doesn't relitigate it.)
- **Leverage Microsoft samples** for R1's server-side work: `Azure-Samples/communication-services-authentication-hero-csharp` (BFF trusted-service token minting + identity mapping — the blueprint) and `...-dotnet-quickstarts` (thread/participant/send server code).

### 8.6 UI host — OOB main form + polling timeline PCF + accessories (point 7)
- Mirror email-r4's shipped W4 pivot (ADR-026 Path-A exception): **keep the OOB main form, enhance with PCFs.** R1's message surface = the `sprk_communication` / thread OOB main form + the **polling conversation/timeline PCF** (§6.2) + **PCF send/respond accessories**, alongside the reused `CommunicationActions` / `CommunicationConnections` PCF patterns. No Code Page host, no FCC swap, **no live channel** — lowest risk, proven pattern.

### 8.7 Provisioning & retention
- **Resources:** 1 ACS resource + Event Grid **system topic** + subscriptions → BFF webhook (+ dead-letter Storage).
- **Per-customer isolation (D-01):** ACS **data location is immutable at create time** → residency = a **separate ACS resource per boundary**, via the provisioning orchestrator (ADR-027).
- **Retention minimization:** 30-day auto-delete **or** explicit Delete-Chat-Thread post-persist — keep ACS from becoming a shadow record store.

### 8.8 SMS — deferred to R2 (regulatory gate)
- SMS is the **same ACS resource** and the .NET footprint is trivial (`Azure.Communication.Sms`), **but** it adds: number provisioning; a **mandatory ~5–6 week toll-free verification** (unverified toll-free is *blocked*, not throttled, in US/CA since 2024-01-31) **or** 10DLC brand+campaign registration; TCPA/STOP opt-out obligations (ACS auto-enforces STOP for toll-free/short-code, relays via Event Grid, but you must honor them); and a second inbound Event Grid path (`SMSReceived`). The **regulatory verification is a hard external dependency** that would bottleneck an otherwise-shippable chat R1. **→ R2.**

### 8.9 Footprint & cost
- BFF SDKs: `Azure.Communication.Chat` **1.4.0** + `Azure.Communication.Identity` **1.3.1** — thin over `Azure.Core` (present). **Negligible** vs the 60 MB ceiling (**~45.30 MB** baseline post-R4); the real bundle risk is client-side, avoided by §8.5 headless. Measure the `dotnet publish` delta anyway (§9).
- Chat **$0.0008/message**; no monthly/per-identity fee. Confirm in the pricing calculator at contract.

> **Re-check before token-design lock:** the Entra→ACS *chat*-scope exchange is preview-gated to VoIP as of this pull; uniform server-minting works regardless.

---

## 9. BFF Governance (root §10)

### 9.1 Placement Justification
- **Messaging endpoints + ACS integration + inbound ingestor live in the existing BFF** (D-07) — sole policy-enforcement + token-minting + `sprk_communication` mutation point; a separate service would fork enforcement and the engine. Cite `.claude/constraints/bff-extensions.md`.
- **Measure publish-size** on every BFF-touching task; report absolute + delta vs **~45.30 MB**. ≥+5 MB single-task → justify; ≥55 MB → architecture review; ≥60 MB → HARD STOP. ACS BFF SDKs are thin (§8.9).
- No AI-internal types in CRUD (route via `Services/Ai/PublicContracts/`, ADR-013). New conditional services use ADR-032 Null-Object.

### 9.2 Hot-Path Declaration (§10 / FR-C04)
```xml
<hot-path-declaration>
  <bff>Y</bff>                  <!-- endpoints, ACS integration, ingestor, capture job -->
  <spaarke-ai>N</spaarke-ai>    <!-- MDA PCFs, not the SpaarkeAi code page -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
Register in `projects/INDEX.md` at start; run `/conflict-check` (overlap with `Services/Communication/` — email-r4 is complete, but coordinate with `notification-spine-r1`, which also touches this area).

---

## 10. ADR Posture

- **ADR-045 compliance, not tension.** Outbound persist-on-send (point 1) *aligns* with ADR-045 rule 3. The synopsis's D-02 is superseded; no §6.5 escalation on that point.
- **New ADR: ADR-046 — ACS transport / messaging channel** (placeholder **reserved** main-session; `notification-spine-r1` takes ADR-047). ACS is a first-of-kind Azure resource (external transport, BYOI identity plane, per-customer provisioning, Event Grid ingress, ingestor seam). Mirror how R4 authored ADR-045; full concise+full ADR authored during the project. Capture: ACS-transport/Dataverse-record, BFF-mints-tokens, membership-as-projection (open via ADR-034, private via record-sharing grant), retention-minimized, **and the thread model + ingestor seam** as the reusable additions.
- **Ingestor seam:** R1 should add `ICommunicationChannelIngestor` alongside the existing sender/archiver seams — completes ADR-045's stated (but unbuilt) intent and keeps inbound from being messaging-forked.
- No new regarding mechanism, no Activities, no SSS — inherited MUST-NOTs.

---

## 11. Sequencing & Dependencies

- **email-r4 is complete** — the send/archive seams, engine, enrichment, `sprk_body`, attachment model, and OOB-form+PCF pattern are **available now**. The prior "task-016 blocker" is resolved.
- **R1 modifies the shared email capture + send path** — the `IThreadResolver` extension (§6.5) edits email-r4-shipped `Services/Communication/` code. It's additive (thread assignment) and benefits email, but it is a **shared-code edit** → coordinate + `/conflict-check`.
- **Coordinate with `spaarke-notification-spine-r1`** — it also touches `Services/Communication`/`Services/Ai`; messaging is its R2 consumer. Align the `kind` taxonomy + the `threadId` contract (§7/§5A/§6.5) at joint intake. Run `/conflict-check` at start + before each BFF wave.
- **No hard upstream blocker remains** — R1 can proceed once the §14 decisions are locked.

---

## 12. Phase 0 — Lean Delta Validation

1. **Confirm live `sprk_communication` schema** — which columns exist vs. must be added (thread lookup, privacy, internal-only, privilege, ACS keys).
2. **Confirm the Dataverse security mechanism for private-thread membership derivation** (BU / team / access team / sharing) — the one open architectural question (§5). Security-sensitive.
3. **ACS spike:** thread + server-minted token + Event Grid capture round-trip; measure **send→persist latency**, **echo-dedup**, **BFF publish-size delta**. Use the auth-hero-csharp sample as the starting point.
4. **Send/capture + polling spike:** PCF send accessory → BFF → ACS → persist; inbound via Event Grid → persist; the **polling timeline component** reads the BFF and reflects both within the poll interval — confirm the no-live-channel R1 surface works end-to-end + the poll interval that feels "seamless" (§6.2/§8.6). No client-side ACS SDK.
5. **Thread-model spike:** confirm the grouping-key choice (A `sprk_communicationthread` + lookup vs C `sprk_conversationid`), the `ThreadContinuityRung` thread-assignment extension, and the grouped read model for both email reply-chains and chat.
6. **Unread-indicator spike:** confirm a BFF unread-count endpoint + polling drives the indicator acceptably — evidence the polling UX meets expectations and SignalR is genuinely a next/R2 concern (§7).
7. **Re-check** Entra→ACS chat-scope exchange status before token-design lock (§8.2).

---

## 13. Risks

| # | Risk | Mitigation |
|---|---|---|
| R-1 | ACS entirely net-new; per-customer provisioning (D-01) | Spike first; ACS-transport ADR; ADR-027 alignment; leverage MS auth-hero sample |
| R-2 | Thread data model + polling timeline component is net scope | Keep minimal (thin thread key, point-forward, **polling** — no live channel); reuse `<EmailComposer/>` sub-components; tune poll interval to bound BFF load; bounded in §6 |
| R-3 | Inbound has no seam — messaging builds the capture path | Add `ICommunicationChannelIngestor`; reuse engine + job contract downstream |
| R-4 | Privacy/privilege enforcement correctness (security-sensitive) | Compose from Communication/thread record security (§5); Phase 0 mechanism confirmation; explicit review gate |
| R-5 | ACS composites unsupported on React 19 | **Moot in R1** (no client-side ACS SDK at all); the next-project live UI uses the headless SDK + Fluent v9, not composites (§8.5) |
| R-6 | Membership eventual-consistency drift (Dataverse↔ACS) | Event-driven reconcile + periodic sweep (§8.4); audit each change |
| R-7 | Coordination drift with `notification-spine-r1` | Joint intake; `/conflict-check`; align `kind` taxonomy |
| R-8 | Over-engineering real-time | No notification fabric in R1; ride enrichment→spine; SignalR is R2 |

---

## 14. Decisions to Resolve Before `/design-to-spec`

1. ✅ **ACS deployment** (§8) — *resolved (Q1 yes).* Server-side integration; the `.eml`-analogous capture model; uniform server-side token minting; Event Grid capture w/ idempotent dedupe; 30-day/delete-post-persist retention; per-boundary ACS resource; leverage MS samples.
2. ✅ **Notifications** (§7) — *resolved: messaging consumes `notification-spine-r1`; no fabric in R1.* The communication-wide consumer spec is now written into that project's design (**§5A**). Open coordination: confirm `assistant-r1.5` push direction + the `communication-assessed` vs `communication-arrived` `kind` split at joint intake.
3. ✅ **UI approach** (§8.5/§8.6) — *resolved (Q1 + Q7): R1 has **no live channel**, but delivers a usable async experience.* OOB form + a **polling conversation/timeline component** (Fluent v9, shared lib, PCF) with auto send/receive/sync + unread indicator (Activities/portal-comments UX) + PCF accessories. The **live-channel upgrade** (headless `@azure/communication-chat`; real-time/typing/receipts; **not** composites) layers onto the *same* component next project.
4. ✅ **Thread model** (§6) — *resolved (Q2 + Q4):* R1 builds the thread **data structures** (grouping key **LOCKED = (A)** `sprk_communicationthread` + `sprk_thread`) + the polling timeline component, covering email reply-chains and chat, point-forward. The thread model **extends the shared email inbound + outbound path** via a direction-symmetric `IThreadResolver` (§6.5) — email conversations become grouped threads too (a feature). Coordination with email-r4 code + `notification-spine-r1` required.
5. ✅ **Access model** (§5) — *resolved:* boundary = Communication/thread Dataverse record security. **Open** threads derive membership from **`MembershipResolverService` (ADR-034)**; **private** threads add an explicit per-record sharing grant (existing `GrantAccess` / `sprk_externalrecordaccess` pattern). Archived artifact holds no separate ACL; ACS membership is a reconciled projection. Phase 0 verifies only the private-grant detail.
6. ✅ **`CommunicationType` value** — *resolved:* **`Message = 100000004`** (the Dataverse choice **already exists**; the C# `CommunicationType` enum must be extended to match). Not `TeamsMessage`.
7. ✅ **New ACS-transport ADR** — *resolved: **ADR-046*** (placeholder reserved main-session; `notification-spine-r1` takes ADR-047). Full ADR authored during the project; includes the thread model + ingestor seam.
8. ✅ **Ingestor seam** — *resolved:* add `ICommunicationChannelIngestor` in R1.
9. ✅ **`kind` taxonomy + `threadId` contract** (§7/§5A) — *resolved (ratified):* two kinds (`communication-assessed` / `communication-arrived`) + `threadId` envelope + Dataverse-security-derived targeting. Not a messaging-R1 blocker (R1 polls); binds messaging R2.

**Remaining before `/design-to-spec`:** only **Phase 0 verification** of the private-grant detail (`GrantAccess` vs `sprk_externalrecordaccess`, §5) — not a blocker for spec authoring. **All design decisions are locked.**

---

## 15. R1 → R2/R3 Roadmap (context, not R1 scope)

- **R1:** messaging plumbing + thread data model + a **usable async message experience** in the MDA — one channel (ACS Chat, server-side), OOB form + **polling conversation/timeline component** + send/respond accessories + unread indicator, first-class thread data, bidirectional content reuse, attachments. **No live channel.**
- **Next project:** the **live-channel upgrade** to the same timeline component (Fluent v9 + headless `@azure/communication-chat`) — real-time append, typing/receipts. The first client-side ACS SDK.
- **R2:** **SMS** channel (after toll-free verification); **external-access portal** messaging (BYOI); **SignalR cross-surface fan-out** as a consumer of `notification-spine-r1`.
- **R3:** **Teams tab** host (unified manifest, NAA); voice/video (same ACS identity plane).

Each later addition is a **host**, a **channel provider**, or a **UI over the same data** — never a new engine, regarding model, thread model, or enforcement point. The thread data model built in R1 serves every later surface unchanged. That is the payoff of building R1 correctly.
