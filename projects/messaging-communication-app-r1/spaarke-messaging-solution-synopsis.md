# Spaarke Communication Platform — Messaging Channel Synopsis

**Status:** Draft v2 (refined per review) | **Date:** 2026-07-15
**Audience:** Claude Code (design/implementation input) and internal architecture review
**Scope note:** Component names, entity fields, and paths are [PROPOSED, VALIDATION NEEDED] unless tagged [EXISTS]. Phase 0 discovery against the actual codebase and Dataverse schema is required before any name is treated as final.

---

## 1. Solution Vision

Spaarke has **one communication platform**: the Communication entity and its process pipeline. Email, messaging (chat), and SMS are **channels** for exchanging information over that single platform — not separate modules or apps. A channel determines transport and UI affordances; it does not change the record model, the security model, the association model, or the processing pipeline.

Messaging is therefore introduced as **a new channel on the existing communication process**, aligned with and sharing the email process end to end: same Communication entity, same record/matter association pattern, same capture pipeline shape, same governance. Users experience one communication fabric — an All Communications timeline with channel-appropriate affordances — not an email app plus a chat app.

The transport inversion that makes messaging viable: **Spaarke owns the message transport (Azure Communication Services); external tools are hosts and projections, not systems of record.** Teams hosts the Spaarke experience as an embedded app — messages never live in Teams chat infrastructure, avoiding the governance, metering, and capture problems of Teams-native chat while keeping users in their existing tools.

Reference precedents: Dynamics 365 Contact Center (ACS transport + Dataverse conversation record — Microsoft's own production use of this exact pattern) and Microsoft's published pattern for embedding ACS chat in a custom Teams app.

**Positioning:** incumbents treat chat as the venue and the record as an afterthought. Legal work needs it inverted: communications are governed records on the matter, in every channel, surfaced wherever participants work.

---

## 2. Key Features & Capabilities

### 2.1 One platform, channel-aware surfaces

- **All Communications view** — unified timeline of email, messages, and SMS for a matter, contact, or the user's workload; filterable by channel, participant, privacy state. The primary surface.
- **Channel-focused views** — Email view and Messaging view as filtered presentations of the same Communication records for users who work channel-first. Views, not modules.
- **In-context communication pane** — thread surfaced on the matter (or other record) workspace.
- **Portable messaging surface** — one React messaging component surfaced in: the Spaarke workspace, the **Spaarke AI code page**, the **external access portal**, and the **Spaarke Teams app** (tab). Same component, four hosts.
- **External participants** — clients, outside counsel, and business users join threads via the portal without Teams licenses or Entra accounts in the customer tenant (ACS BYOI identity).

### 2.2 Messaging model

- **Topologies:** 1:1, group (named N:N participant set), and record-scoped.
- **Anchoring:** threads anchor either to people (user/contact-level direct and group conversations) or to a record (matter, request, project) — the record is the conversation's context and access boundary. Same association pattern as email.
- **Threading:** threads with replies; each thread carries one or more channel references (see thread/channel model, §4); every message persists as a Communication record.
- **Cross-channel continuity:** a record-level thread timeline can interleave chat, filed email, and SMS; channel is an attribute of the message, not a silo.
- **Content:** attachments filed to the record's SPE container as governed documents; @mentions; sender-only edit/delete (ACS rule) with history retained in Dataverse.

### 2.3 Privacy and access control

Authorization **derives from Dataverse, is enforced at the BFF, and is materialized into ACS thread membership** (a sync job reconciles membership whenever access changes). Follows the existing Communication security model.

- **Record-scoped (open) threads:** visible to anyone with access to the associated record; no record access → no message access. Access revocation removes membership.
- **Private messages and threads:** restricted to explicitly named users regardless of record access.
  - Privacy applies at the **individual message level** and at the **thread level**.
  - Thread privacy is switchable. **Switch semantics [RECOMMENDED, CONFIRM]: point-forward** — opening a private thread opens it from that moment; previously private messages remain restricted. Retroactive opening of history is a separate explicit, audited bulk action. Nothing restricted ever becomes visible as a side effect.
- **Internal-only messages:** visible only to users tagged internal (user attribute — D-05 locked); external/portal participants never see them. Supports internal deliberation alongside a client-visible thread.
- **Privilege designation:** metadata flag (attorney–client, work product) distinct from privacy — privacy is access control, privilege is classification for reporting and export marking. They compose.
- **Ethical walls:** record-level security is the wall; messaging inherits it by construction. Phase 0 confirms the exact mechanism so membership derivation matches it.

### 2.4 Platform expectations

- **System of record:** Dataverse Communication records, full audit of message lifecycle, privacy transitions, and membership changes. Retention, legal hold, and eDiscovery are **not messaging-specific features** — Communication records participate in Spaarke's standard Dataverse/Power Platform data management like any other entity.
- **Legal front door:** intake requests arrive as (or convert to) record-level threads; message-to-task/request conversion on the matter; triage from the All Communications view. Messaging is the front door's conversational channel.
- **Notifications:** in-app real-time via two planes — ACS events for the live thread, Azure SignalR Service for cross-channel timeline/badge/toast updates; Teams activity feed via Graph `sendActivityNotification` from the installed Spaarke app (no bot); email fallback for unread messages; SMS for portal-only participants where enabled.
- **UX table stakes:** typing indicators, read receipts, delivery status (ACS-native); unread counts across surfaces; search within authorized scope.
- **External participant lifecycle:** invite, identity-map, off-board portal participants; consistent identity display (Entra user vs. mapped external contact).

### 2.5 Channels (provider model)

Channels implement one provider contract over the Communication pipeline. The existing email path refactors into the first provider — the test that the abstraction is right.

| Channel | Delivery | Capture |
|---|---|---|
| Messaging (ACS Chat) | ACS Chat SDK via BFF | Event Grid → webhook → job → Communication record |
| Email | Graph send from user mailbox [EXISTS — validate] | Existing email capture pattern [EXISTS] |
| SMS | ACS SMS | ACS inbound events via Event Grid |
| Teams | Host surface (tab) + activity feed notifications — not a transport | n/a by design |
| Slack [DEFERRED] | Projection only: post + deep link | Not in scope |

Note (D-06 locked): ACS Email is transactional service-domain mail — not a replacement for Graph user correspondence. Optional future use for system notifications only.

---

## 3. Conceptual Architecture

### 3.1 Principles

1. **One communication process; channels are providers.** Adding a channel means implementing the provider contract, never a parallel pipeline or module.
2. **Dataverse is the record; ACS is the transport.** Every durable fact lives in Dataverse; ACS threads are reconstructible projections.
3. **BFF is the sole policy enforcement point (D-07 locked).** Clients never hold ACS admin capability; the BFF mints scoped ACS user tokens and owns thread/membership mutation.
4. **Authorization derives from record security.** Membership is computed (or an explicit private participant list), never independently administered.
5. **One React messaging surface, many hosts.** Code-page-hosted React component (ACS UI Library composites + Fluent UI v9) — not a PCF; PCF is reserved for field/form-bound controls. Hosts: workspace, Spaarke AI code page, external portal, Teams tab.
6. **Async by default.** Capture, membership sync, and notification fan-out run through the existing job contract pattern (Service Bus, idempotent handlers, DLQ) [EXISTS pattern].

### 3.2 Conceptual diagram

```
              ┌────────────────────────────────────────────────────┐
              │                   Hosting surfaces                 │
              │  Spaarke workspace │ Spaarke AI code page          │
              │  External portal (BYOI) │ Teams tab (NAA)          │
              └──────────────────────┬─────────────────────────────┘
                                     │ React messaging surface
                                     │ (code page component; ACS UI Library + Fluent v9)
                    Entra token (NAA/MSAL) │ ACS user token
                                     ▼
   ┌─────────────────────────────────────────────────────────────┐
   │                 BFF / Spaarke API  (policy enforcement)     │
   │  Dataverse-derived authZ │ ACS token minting │ thread &     │
   │  membership mgmt │ Communication ops │ notification API     │
   └────────┬────────────────┬──────────────────┬────────────────┘
            │                │                  │
            ▼                ▼                  ▼
   ┌───────────────┐  ┌──────────────┐  ┌──────────────────────┐
   │ Dataverse     │  │ ACS          │  │ Graph                │
   │ Communication │  │ Chat / SMS   │  │ activity feed notif. │
   │ + channel refs│  │ (Email opt.) │  │ email send [EXISTS]  │
   │ + SPE docs    │  └──────┬───────┘  └──────────────────────┘
   └───────▲───────┘         │ Event Grid (message / SMS events)
           │                 ▼
           │      ┌──────────────────────────────┐
           └──────│ Webhook ingress → Service Bus│
    persist as    │ → job handlers (capture,     │
    Communication │ membership sync, fan-out)    │
    records       └──────────────────────────────┘
```

### 3.3 Core flows

- **Send (D-02 locked: capture-on-event):** client → BFF (authZ) → ACS SendMessage → Event Grid event → job → persist Communication record → notification fan-out. One persistence pipeline for every message origin (internal, portal, system). Live chat delivery is real-time via ACS; Dataverse-driven surfaces reflect the message after pipeline latency (target sub-second; Phase 0 spike measures it).
- **Thread creation:** create/lookup thread record → compute participants from record access (or named private list) → create ACS thread → write channel reference row.
- **Membership sync:** record access change, privacy switch, or participant edit → job recomputes → reconciles ACS participants → audit entry.
- **Notification:** capture job evaluates recipient presence/preferences → in-app, Teams activity feed, or email fallback.
- **Real-time planes (two, deliberately distinct):** (1) the live conversation — ACS Chat's own WebSocket delivers messages/typing/read receipts to thread participants; no SignalR involved. (2) Dataverse-driven surfaces — the capture/fan-out job publishes to an **Azure SignalR Service** hub after persisting; all hosts subscribe, so timelines, unread badges, and toasts update live for *any* channel arrival (chat, email, SMS). SignalR is the cross-channel in-app notification fabric, not a chat transport; it can land with the fan-out job rather than blocking the first thread round-trip.

---

## 4. Component Model

| Component | Status | Notes |
|---|---|---|
| `sprk_communication` entity + related schema | [EXISTS — validate] | Extend for: anchoring (user vs. record), privacy state (message + thread), internal-only flag, privilege designation, channel type |
| Thread channel reference table (D-03 locked: child table) | [NEW] | One row per (thread, channel, external ref): ACS ChatThreadId now; email conversation ID, SMS, future channels later. The schema expression of "channel is an attribute" |
| BFF messaging endpoints | [NEW] | In existing BFF (D-07). Thread ops, send, ACS token minting, membership ops |
| ACS identity mapping (Entra user / portal contact ↔ ACS identity) | [NEW] | Server-side only; per-customer ACS resource (D-01) |
| React messaging surface | [NEW] | Code-page-hosted React component (not PCF); ACS UI Library composites themed to Spaarke design system; single component across workspace / AI code page / portal / Teams tab |
| Teams app package (tab, unified manifest) | [NEW] | M365 Agents Toolkit + Teams SDK (`@microsoft/teams.client`), NAA auth; **not TeamsFx (deprecated)**. Tab hosts the same messaging surface |
| Event Grid webhook ingress | [NEW] | Same validation/idempotency posture as email webhook [EXISTS] |
| Job handlers: capture, membership sync, notification fan-out, SMS delivery | [NEW] | Existing job contract pattern [EXISTS pattern] |
| Channel provider contract | [NEW] | Email (Graph) refactors to first provider; messaging (ACS) second; SMS third |
| Provisioning additions | [NEW] | Per-customer ACS resource + Event Grid subscriptions + Teams app catalog upload as provisioning-orchestrator jobs (D-01) |
| All Communications view + channel views | [NEW] | Query-driven presentations over Communication records — views, not modules |

---

## 5. Microsoft Services / Resources Map

| Service | Role | Flags |
|---|---|---|
| **ACS — Chat** | Message transport, real-time delivery, typing/read receipts, ≤250 participants/thread | Per-message pricing — pull current numbers at commit (Phase 0 task). Thread retention defers to Dataverse |
| **ACS — SMS** | SMS channel for portal participants | Number provisioning region-limited; per-segment pricing |
| **ACS — Identities & UI Library** | BYOI identities for portal users; production React chat composites (Fluent-based) | Theme to Spaarke design system |
| **ACS — Email** | Optional, system notifications only (D-06) | Not for user correspondence |
| **Azure Event Grid** | ACS event delivery to webhook ingress | Retry/dead-letter config |
| **Azure Service Bus** | Job transport | [EXISTS] |
| **Azure SignalR Service** | Cross-channel in-app real-time plane: capture/fan-out job → hub → subscribed hosts (timeline, unread badges, toasts) for any channel arrival | Not used for the chat thread itself (ACS provides that); can ship with fan-out job, v1-optional |
| **Dataverse** | System of record: Communication entity, channel refs, audit; standard data management (retention/hold/export handled platform-wide, not messaging-specific) | [EXISTS] |
| **SharePoint Embedded** | Attachments as governed matter documents | [EXISTS] |
| **Microsoft Graph** | `sendActivityNotification` (Teams activity feed, app permission, no bot); email send [EXISTS]; Teams app catalog deployment | Activity feed requires installed Teams app |
| **Microsoft Entra ID** | NAA (tab SSO) + OBO at BFF [EXISTS posture]; per-customer app registrations [EXISTS decision] | NAA fallback for down-level clients; conditional-access testing on Teams mobile |
| **Teams platform** | Tab hosting (personal + channel), unified manifest (Teams/Outlook/M365), org app catalog distribution | Build with M365 Agents Toolkit + Teams SDK; TeamsFx deprecated (community support ends Sept 2026) |
| **Power Apps code pages** | Host for the React messaging surface (workspace + Spaarke AI page) | Aligns with existing code-page direction; PCF not used here |
| **Azure Cache for Redis** | Webhook idempotency, notification debounce | [EXISTS] |
| **Application Insights** | Telemetry across send/capture/sync flows | [EXISTS] |

---

## 6. Deferred / Out of Scope (explicit)

- **AI/Foundry integration** — not a near-term priority. Messages are governed Dataverse records and therefore AI-ready by construction; no AI components in this scope.
- **Retention policy, legal hold, eDiscovery tooling** — handled by Spaarke's overall Dataverse/Power Platform data management; Communication is just an entity within it. No messaging-specific build.
- **Slack** — projection-only pattern reserved for future.
- **Teams-native chat capture** (Graph chat/channel message APIs) — deliberately avoided: metered/protected APIs, conflicts with record-ownership model.
- **Voice/video** — same ACS identity plane; natural future extension, out of scope.

---

## 7. Decisions

| # | Decision | Status |
|---|---|---|
| D-01 | ACS resource per customer, provisioned as part of per-customer environment setup | **LOCKED** |
| D-02 | Persistence: capture-on-event (single pipeline); live chat unaffected, Dataverse surfaces reflect after pipeline latency; Phase 0 spike measures | **LOCKED** (revisit only if latency spike fails target) |
| D-03 | Thread ↔ channel mapping via child reference table (multi-channel by design) | **LOCKED** |
| D-04 | Privacy at individual message level + thread level; thread switch to open applies **point-forward**; retroactive history opening is an explicit audited bulk action | **LOCKED** |
| D-05 | Internal tag = user attribute | **LOCKED** |
| D-06 | Graph for user email; ACS Email optional for system notifications only | **LOCKED** |
| D-07 | Messaging endpoints in existing BFF (the Spaarke API) | **LOCKED** |
| D-08 | Compliance positioning statement | Owned by product/sales, outside this workstream |

---

## 8. Phase 0 Validation Tasks (Claude Code)

1. Inventory actual `sprk_communication` schema, security model, and existing views over it; document gaps vs. §2.2–2.3, including where thread/message-level privacy and internal-only flags belong.
2. Inventory the existing Graph email send path and email capture pipeline; assess fit against the channel provider contract (§2.5) and specify the email-provider refactor.
3. Confirm the record-level security mechanism (BU/team/access team/sharing) and specify the membership-derivation algorithm against it.
4. Review Dynamics 365 Omnichannel conversation schema as reference for Communication extensions (transcript persistence pattern).
5. Spike: ACS thread + Event Grid capture + job persistence round-trip in dev; measure send→persist latency (validates D-02).
6. Spike: minimal Teams tab (Agents Toolkit, NAA) hosting a stub of the messaging surface against the existing BFF; validate Teams desktop/web/mobile including a conditional-access tenant.
7. Spike: host the same React surface in a Power Apps code page and the portal shell; confirm auth model in each host (NAA in Teams, MSAL in code page, BYOI token flow in portal).
8. Pull current ACS chat/SMS pricing and retention API surface; attach to this doc.
