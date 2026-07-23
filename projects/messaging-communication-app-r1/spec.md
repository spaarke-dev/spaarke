# Messaging Communication App — R1: AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-16
> **Source**: `projects/messaging-communication-app-r1/design.md` (Draft v2)
> **Builds on (complete)**: `email-communication-solution-r4` (ADR-045 channel seams shipped)
> **Coordinates with**: `spaarke-notification-spine-r1` (shared notification fabric; messaging consumes in R2)

---

## Executive Summary

Add **messaging (real-time chat) as the second channel** on Spaarke's existing communication platform — not a new module or pipeline. **Azure Communication Services (ACS) Chat is the transport; Dataverse `sprk_communication` is the system of record; the BFF is the sole policy-enforcement and token-minting point.** R1 delivers the server-side plumbing (channel provider + inbound ingestor + ACS integration), a **first-class communication thread data model** that groups email reply-chains *and* chat conversations, and a **usable async message experience** inside the Spaarke model-driven app (MDA) — a polling read/write conversation component with automated send/receive/sync and an unread indicator (the OOB Activities / portal-comments UX model). The **live open channel** (WebSocket real-time), **spine-pushed** cross-surface notifications, **SMS**, and the **Teams/portal** surfaces are explicitly deferred to a later phase / R2 / R3.

---

## Scope

### In Scope

- **Messaging channel provider** (server-side) over the shipped ADR-045 seams: `ICommunicationChannelSender` + `ICommunicationArchiver` implementations dispatched by `CommunicationType.Message` (= 100000004).
- **Inbound ingestor** — a new `ICommunicationChannelIngestor` seam + ACS Event Grid capture pipeline (webhook → Service Bus job → normalizer → persist → enrichment), idempotent on ACS message id.
- **ACS integration (server-side only)**: identity creation + `communicationUserId ↔ Dataverse` mapping, server-side chat-token minting, thread create, membership set/reconcile.
- **Outbound send** — persist-on-send via `CommunicationService` channel dispatch (ADR-045 rule 3), with echo-dedup.
- **First-class thread data model** — `sprk_communicationthread` entity + `sprk_communication.sprk_thread` lookup + thread↔channel child table (D-03); `IThreadResolver` (direction-symmetric thread assignment) extending the shared inbound + outbound path.
- **Thread topologies**: **record-anchored** (matter/project/etc. via the ADR-024 regarding family) **and 1:1 direct** (person-to-person, explicit two-participant membership).
- **Privacy / internal-only / privilege** — schema columns + BFF enforcement; thread-level privacy on the thread entity; point-forward privacy switch; composed from existing Dataverse record security.
- **Polling conversation/timeline component** — `@spaarke/ui-components` (Fluent v9), packaged as a PCF: renders a thread (email + chat interleaved, reply nesting), compose/send box, unread indicator, ~5s poll.
- **BFF thread-read + unread-count endpoints** driving the poll.
- **PCF send/respond accessories** on the OOB main form (mirroring email-r4's CommunicationActions/Connections pattern).
- **Bidirectional inline content quoting** (email↔message) via the channel-agnostic `sprk_body`.
- **Message attachments** — materialization (ACS/file → SPE → `sprk_document` → link), reusing the channel-agnostic storage model; ACS message references the SPE doc; governed by the existing chat-attachment policy.
- **ACS retention** — 30-day auto-delete on thread create.
- **Message transcript archive** to SPE via `ICommunicationArchiver` (the `.eml` analog).
- **ADR-046** (ACS messaging channel) — full concise+full authoring (placeholder reserved).
- **Per-customer ACS resource provisioning** + Event Grid subscriptions (provisioning orchestrator, ADR-027).

### Out of Scope (deferred upgrade / R2 / R3)

- **Live open channel** — WebSocket real-time append, typing indicators, read-receipts. The upgrade layers the headless `@azure/communication-chat` SDK onto the *same* component; **the ACS composites (`@azure/communication-react`) are never used.** (This is where a client-side ACS SDK first appears — not R1.)
- **Spine-pushed cross-surface notification** — R1's unread indicator is polling-based; pushed badges/toasts come via `spaarke-notification-spine-r1` in R2.
- **Ad-hoc named group threads** (N participants, not record-anchored) — later phase.
- **SMS** channel — hard 5–6 week toll-free-verification regulatory gate; R2. (Channel-ref table is SMS-ready.)
- **Teams tab**, **external-access portal** messaging surface (BYOI participants) — R2/R3.
- **Historical email backfill** into the thread model (R1 is point-forward); voice/video; retroactive privacy bulk-open tooling.

### Explicitly Prohibited (owner-locked MUST NOT)

- Dataverse/Power Apps **Activities**, OOB `email`/activity entities, or **portal comments** as any part of the model.
- **Native Teams chat** capture (Graph chat/channel-message APIs) — not exposed to a third-party ISV for this use; conflicts with record ownership. Teams participates later only as a *host*.
- A second communication pipeline, a second regarding mechanism, or a second channel-provider contract.

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/Communication/Channels/` — new `MessagingChannelSender`, `MessagingArchiver`, **new** `ICommunicationChannelIngestor` + `MessagingIngestor`.
- `src/server/api/Sprk.Bff.Api/Services/Communication/` — new `IThreadResolver`; **extend** `IncomingCommunicationProcessor`, `Engine/ThreadContinuityRung`, `CommunicationService` (thread assignment on both directions).
- `src/server/api/Sprk.Bff.Api/Services/Communication/Models/CommunicationType.cs` — add `Message = 100000004`.
- `src/server/api/Sprk.Bff.Api/Services/Communication/Acs/` (new) — ACS identity, token minting, thread/membership ops.
- `src/server/api/Sprk.Bff.Api/Api/` — messaging endpoints (thread-read, unread-count, send, thread/membership ops) + Event Grid webhook ingress.
- **Dataverse** — `sprk_communicationthread` entity, `sprk_thread` lookup, thread↔channel child table, new `sprk_communication` columns (privacy, internal-only, privilege, ACS `messageId`/`chatThreadId`, `communicationUserId` on user/contact); `sprk_communicationtype` `Message` choice (already exists).
- `src/client/shared/Spaarke.UI.Components/` — polling conversation/timeline component.
- `src/client/pcf/` — Messaging timeline PCF + send/respond accessory PCF(s).
- `.claude/adr/ADR-046-acs-messaging-channel.md` (placeholder → full) + `docs/adr/ADR-046-acs-messaging-channel.md`.
- Provisioning/solution — per-customer ACS resource + Event Grid subscriptions.

---

## Requirements

### Functional Requirements

1. **FR-01 — Messaging channel sender + archiver.** Implement `ICommunicationChannelSender` + `ICommunicationArchiver` for `CommunicationType.Message`, registered in the channel dispatcher. *Acceptance*: sending a message dispatches through `CommunicationChannelDispatcher.ResolveSender(Message)`; `CommunicationService` remains Graph-free; the archiver writes a message-transcript artifact to SPE.
2. **FR-02 — Inbound ingestor + capture pipeline.** Add `ICommunicationChannelIngestor` seam; implement ACS Event Grid ingress: validated webhook → Service Bus job → ACS-event normalizer → `NormalizedMessage` → ingestor persists `sprk_communication` → enrichment. *Acceptance*: an inbound ACS `ChatMessageReceivedInThread` event results in exactly one persisted `sprk_communication` (idempotent on ACS message id across at-least-once + duplicate delivery); handler dead-letters to Storage on repeated failure.
3. **FR-03 — ACS identity + server-side token minting.** BFF (trusted service) creates ACS identities, persists `communicationUserId` on the Dataverse user/contact, and mints `chat`-scoped tokens server-side for all participants. *Acceptance*: no ACS admin capability or token reaches any client in R1; a participant without an ACS identity gets one created + mapped on first use.
4. **FR-04 — Outbound persist-on-send + echo-dedup.** Outbound message: BFF authZ → dispatch to messaging sender → ACS SendMessage (server-side, sender's minted token) → persist `sprk_communication` (thread set) → enrichment. The Event Grid echo of our own message is a no-op via idempotency on ACS message id. *Acceptance*: sending once yields exactly one persisted record despite the echo.
5. **FR-05 — Thread data model.** Create `sprk_communicationthread` entity (topic, anchor via regarding family, thread-level privacy state, participant set), the `sprk_communication.sprk_thread` lookup, and the thread↔channel child table (one row per `(thread, channel, external-ref)`; R1 populates ACS `ChatThreadId`). *Acceptance*: every persisted message (email + chat) carries a `sprk_thread`; the child table holds the ACS thread id; a query "messages where `sprk_thread` = X" returns the conversation.
6. **FR-06 — `IThreadResolver` (direction-symmetric).** A shared resolver find-or-creates the thread and returns `sprk_thread`, invoked by both the inbound capture path and the outbound send path, for all channels. Email uses `sprk_inreplyto` ancestry (reply-chain root → thread); chat uses ACS `ChatThreadId`. Extends `ThreadContinuityRung` (inbound) + `CommunicationService` (outbound). *Acceptance*: an email reply joins its parent's thread; a chat message joins its ACS thread; a fresh outbound message/email creates a thread; **existing email flows continue to pass** (characterization tests green); point-forward (no historical backfill).
7. **FR-07 — Membership derivation + reconcile.** Open (record-anchored) thread membership derives from `MembershipResolverService` (ADR-034); 1:1 direct threads use an explicit two-participant list; private threads add an explicit per-record sharing grant. A job reconciles ACS thread participants from Dataverse-derived access (event-driven + periodic sweep). *Acceptance*: a Dataverse access change reconciles ACS membership within the sweep window; audit entry recorded per change.
8. **FR-08 — Privacy / internal-only / privilege.** Add columns + BFF query-filter enforcement: message-level + thread-level privacy, internal-only (D-05 user attribute), privilege classification (composes with privacy; AI may flag never decide — ADR-015). Thread privacy switch is **point-forward** (prior private messages stay restricted; retroactive open is a separate audited action, out of R1). *Acceptance*: a private thread's messages are invisible to users without an explicit grant; an internal-only message is invisible to non-internal users; flipping a thread to open exposes only messages from that point forward.
9. **FR-09 — 1:1 direct threads.** Support person-to-person direct threads not anchored to a record, with explicit two-participant membership. *Acceptance*: a user can start a 1:1 thread with another user; membership is exactly the two participants; it appears in each participant's authorized threads.
10. **FR-10 — Polling conversation/timeline component.** A Fluent v9 component in `@spaarke/ui-components`, packaged as a PCF: renders a thread (email + chat interleaved, channel-badged, ordered, reply nesting from `sprk_inreplyto`), a compose/send box, and an unread indicator; polls the BFF every **~5s**. Reads persisted records only (no client-side ACS SDK). Reuses `<EmailComposer/>` sub-components where sensible. *Acceptance*: the component displays a thread's messages, sends via the BFF, and reflects a newly-captured inbound message within one poll cycle; contains no ACS SDK import.
11. **FR-11 — BFF thread-read + unread-count endpoints.** Read a thread's `sprk_communication` rows (`sprk_body`, channel type, sender, attachments) + an unread count, access-filtered. *Acceptance*: the endpoint returns only messages the caller may read (per FR-08); unread count reflects messages since the caller's last-seen.
12. **FR-12 — PCF send/respond accessories.** Send/respond accessory PCF(s) on the OOB `sprk_communication`/thread main form calling the BFF (mirroring `CommunicationActions`). *Acceptance*: compose/send/respond work from the OOB form with no Code Page host and no FCC swap.
13. **FR-13 — Bidirectional inline content quoting.** "Quote into message" and "quote into email" actions read the source `sprk_body`/`sprk_bodyformat` and prefill the target composer as inline quoted content, both directions. *Acceptance*: an email's body can be quoted into a new message and a message's body into an email, without `.eml` parsing.
14. **FR-14 — Message attachments.** Materialize a message attachment (ACS/file → SPE → `sprk_document` → `sprk_document.sprk_communication` + `sprk_communicationattachment` intersection); the ACS message carries a reference. Enforce the existing chat-attachment policy. *Acceptance*: attaching a file to a message creates a governed `sprk_document` linked to the message; oversize/disallowed types are rejected per `docs/standards/CHAT-ATTACHMENT-POLICY.md`.
15. **FR-15 — ACS retention.** Create ACS threads with 30-day auto-delete retention. *Acceptance*: created threads carry the 30-day retention setting.
16. **FR-16 — `CommunicationType` extension.** Add `Message = 100000004` to the C# enum to match the existing Dataverse choice. *Acceptance*: enum + Dataverse option-set agree on 100000004.
17. **FR-17 — ADR-046 authoring.** Author the concise + full ADR-046 (ACS transport / messaging channel), superseding the reserved placeholder. *Acceptance*: `.claude/adr/ADR-046-*.md` (concise) + `docs/adr/ADR-046-*.md` (full) authored; INDEX updated from placeholder to Accepted.
18. **FR-18 — ACS provisioning.** Add per-customer ACS resource + Event Grid system topic/subscriptions to the provisioning orchestrator (ADR-027); document data-residency (immutable location → per-boundary resource). *Acceptance*: provisioning creates/wires the ACS resource + Event Grid subscription to the BFF webhook + dead-letter Storage.

### Non-Functional Requirements

- **NFR-01 — Publish size.** BFF compressed publish ≤ 60 MB (baseline **~45.30 MB** post-R4); measure + report absolute + delta on every BFF-touching task (ACS BFF SDKs are thin over `Azure.Core`).
- **NFR-02 — Best-effort enrichment/thread-assignment.** Enrichment and thread assignment MUST NOT fail the send or inbound-capture path.
- **NFR-03 — Idempotent capture.** Capture MUST be idempotent (Event Grid at-least-once + duplicates), deduping on ACS message id.
- **NFR-04 — No client-side ACS SDK.** R1 client code MUST NOT import any ACS SDK; all ACS interaction is server-side.
- **NFR-05 — Auth.** Server-side ACS token minting only; central `TokenCredential`/canonical Dataverse interfaces (ADR-028); MUST NOT `new` a credential/ConfidentialClientApplication.
- **NFR-06 — Privacy correctness (security-sensitive).** BFF reads MUST enforce message/thread privacy + internal-only; no privileged content leaks; ACS membership never exceeds Dataverse-derived access. Explicit code-review + `adr-check` at Step 9.5.
- **NFR-07 — Poll load.** ~5s poll; bound BFF load (lightweight read + unread-count; tune/monitor; escalate to spine push if load warrants).
- **NFR-08 — Tests (ADR-038).** Integration-heavy; vertical-slice seam tests for the new channel (send/archive/ingest), the `IThreadResolver`, and privacy enforcement; preserve existing email inbound matching under characterization tests before extending.

---

## Technical Constraints

### Applicable ADRs

- **ADR-045** — Communication architecture / channel seams (the spine this extends).
- **ADR-046** — ACS messaging channel (NEW; placeholder reserved; authored in FR-17).
- **ADR-034** — User-record membership (`MembershipResolverService`) → open-thread membership.
- **ADR-028** — Auth v2: server-side token minting; central credential; no `new` credential.
- **ADR-004 / ADR-036** — Job contract / background-job infrastructure (Event Grid capture).
- **ADR-007** — SpeFileStore facade (transcript + attachment archive).
- **ADR-024** — Polymorphic regarding family (thread anchor); MUST NOT add a second regarding mechanism.
- **ADR-027** — Subscription isolation / per-customer resource provisioning (ACS).
- **ADR-018 / ADR-032** — Kill-switch / Null-Object for feature-gated services + unconditional endpoints.
- **ADR-008 / ADR-003 / ADR-010 / ADR-019** — Endpoint filters, authorization seams, DI minimalism, ProblemDetails.
- **ADR-021 / ADR-022 / ADR-026 / ADR-006 / ADR-012** — Fluent v9; PCF platform libraries (React 16/17); Code Page standard; UI surface architecture; shared component library.
- **ADR-038 / ADR-029** — Testing strategy; BFF publish hygiene.
- **ADR-013** — AI facade (only if enrichment/AI is touched → `Services/Ai/PublicContracts/`).

### MUST Rules

- ✅ MUST implement messaging over the shipped ADR-045 seams (sender/archiver + new ingestor); MUST dispatch by `sprk_communicationtype`.
- ✅ MUST persist every message as `sprk_communication`; ACS is transport only (Dataverse is the record).
- ✅ MUST mint ACS tokens **server-side only**; clients hold no ACS admin capability (no client-side ACS SDK in R1).
- ✅ MUST derive open-thread membership from `MembershipResolverService` (ADR-034); private via existing per-record sharing grant; ACS membership is a reconciled **projection** of Dataverse access.
- ✅ MUST extend the ADR-024 regarding family for thread anchoring (no second mechanism).
- ✅ MUST keep enrichment + thread assignment best-effort and non-fatal.
- ✅ MUST make capture idempotent (dedupe on ACS message id).
- ✅ MUST measure BFF publish size per BFF-touching task (NFR-01).
- ❌ MUST NOT use Activities / OOB email / portal comments / native Teams chat.
- ❌ MUST NOT introduce a live client / ACS client SDK / ACS composites in R1.
- ❌ MUST NOT let AI decide privilege (flag only).
- ❌ MUST NOT build a messaging-only notification hub (R1 polls; R2 consumes `notification-spine-r1`).

### Existing Patterns to Follow

- `src/server/api/Sprk.Bff.Api/Services/Communication/Channels/EmailChannelSender.cs` + `EmailArchiver.cs` + `CommunicationChannelDispatcher.cs` — the seam + dispatch pattern to mirror.
- `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingCommunicationProcessor.cs` + `Engine/` — inbound capture + normalizer + rung pattern to mirror/extend.
- `src/server/api/Sprk.Bff.Api/Services/Jobs/` — job contract (idempotency, DLQ, retry).
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs` + `Services/Communication/GraphAttachmentAdapter.cs` — attachment → SPE → `sprk_document`.
- `IMembershipResolverService` (ADR-034) — open-thread membership.
- `PlaybookSharingService` / `GrantExternalAccessEndpoint` / `sprk_externalrecordaccess` — private-grant precedents.
- email-r4 PCFs `CommunicationActions` / `CommunicationConnections` + `<EmailComposer/>` in `@spaarke/ui-components` — the OOB-form + PCF surface pattern.
- Reference implementations: `Azure-Samples/communication-services-authentication-hero-csharp` (BFF trusted-service token minting + identity mapping); `...-dotnet-quickstarts` (thread/participant/send server code).

---

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-026** | "Code Pages are default for new UI" | R1's message surface is the **OOB main form + PCFs**, not a Code Page | **A (project-scoped exception)** | Mirrors email-r4's shipped W4 pivot (same ADR-026 Path-A exception): the OOB 66/34 form + accessories column is the proven, lower-risk base (no Code Page auth bootstrap, no FCC swap). The timeline PCF is form-bound, so it remains within ADR-006 (PCF for form binding). |
| **ADR-045** (D-02 in synopsis) | Outbound persistence pattern | Synopsis proposed capture-on-event for outbound | **C (comply)** | Resolved by following ADR-045 rule 3: outbound persist-on-send + direction-symmetric enrichment. Not a tension — compliance. |

> All other listed ADRs apply without exception. New surface (ACS transport, thread model, ingestor seam) is codified in **ADR-046** (FR-17) rather than tensioned against an existing ADR.

---

## Success Criteria

1. [ ] A user sends a message from the MDA; it persists once as `sprk_communication` (type=Message), is threaded, and appears in the polling timeline within ~5s — **Verify by**: manual send + subgrid/component check + DB row count (echo-dedup).
2. [ ] An inbound ACS message is captured via Event Grid and appears in the thread; duplicate delivery yields one record — **Verify by**: Event Grid replay + idempotency test.
3. [ ] An email reply and a chat message in the same conversation both carry a `sprk_thread`; the timeline renders them grouped — **Verify by**: thread-resolver seam test + component render.
4. [ ] Existing email inbound association still passes after the `IThreadResolver` extension — **Verify by**: characterization tests green.
5. [ ] A private thread's messages are invisible to a user without a grant; an internal-only message is invisible to non-internal users — **Verify by**: privacy enforcement integration tests (security-sensitive review).
6. [ ] A 1:1 direct thread works with explicit two-participant membership — **Verify by**: 1:1 flow test.
7. [ ] An email's content quotes into a new message and vice-versa — **Verify by**: bidirectional-quote test.
8. [ ] A message attachment lands in SPE as a linked `sprk_document`, policy-enforced — **Verify by**: attachment flow test + policy rejection test.
9. [ ] BFF publish size ≤ 60 MB, delta reported — **Verify by**: `dotnet publish -c Release` measurement.
10. [ ] ADR-046 authored (concise + full); INDEX updated to Accepted — **Verify by**: file + INDEX check.
11. [ ] No client-side ACS SDK import in R1 client code — **Verify by**: dependency scan.

---

## Dependencies

### Prerequisites

- **email-r4 complete** — ADR-045 seams, association engine, enrichment, `sprk_body`, attachment model, OOB-form+PCF pattern (available now).
- **`MembershipResolverService` (ADR-034)** — available.
- **Phase 0 verification** — private-grant primitive (`GrantAccess` vs `sprk_externalrecordaccess`); ACS spike (thread/token/Event Grid round-trip + latency + publish-size); confirm live `sprk_communication` schema + the `Message` choice integer.

### External Dependencies

- **Azure Communication Services** resource (per-customer; immutable data location) + **Event Grid** system topic/subscriptions + dead-letter Storage.
- **`Azure.Communication.Chat` 1.4.0 + `Azure.Communication.Identity` 1.3.1** (BFF).
- **Coordination**: `spaarke-notification-spine-r1` (shared `Services/Communication`; messaging is its R2 consumer; align `kind` taxonomy + `threadId` contract at joint intake; it should claim **ADR-047**). Run `/conflict-check`.

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Outbound pattern | Capture-on-event vs persist-on-send? | **Persist-on-send** (ADR-045 rule 3) | Outbound persists directly; inbound persists on event; echo-dedup on ACS message id |
| Live vs async | Live open channel in R1? | **No** — async polling (Activities/portal-comments model) | No client-side ACS SDK; polling component; live upgrade is next project |
| Client SDK / composites | Use ACS composites? | **No** — headless SDK only, and only in the next-project live upgrade | R1 has no ACS client SDK; React-version issue moot |
| Thread grouping key | Thread entity vs regarding+JSON? | **(A)** `sprk_communicationthread` entity + `sprk_thread` lookup | Queryable grouping; home for thread privacy/participants/ACS thread id |
| Thread topologies | Which topologies in R1? | **Record-anchored + 1:1 direct** | Two membership paths (ADR-034-derived + explicit 2-party); ad-hoc groups deferred |
| Access model | How to secure private threads? | **Dataverse record security**: open via ADR-034, private via record-sharing grant | No new ACL; ACS membership is a reconciled projection |
| Notifications | Build SignalR in R1? | **No** — consume `notification-spine-r1` in R2; R1 polls | Zero notification fabric in R1; unread indicator via ~5s poll |
| ACS retention | How long to retain ACS history? | **30-day auto-delete** | Thread created with 30-day retention; Dataverse is the record |
| Poll cadence | Refresh interval? | **~5s** | Snappier feel; monitor BFF load |
| Attachments | Reuse chat-attachment policy? | **Yes** — `docs/standards/CHAT-ATTACHMENT-POLICY.md` | No new policy surface |
| UI host | Code Page vs OOB form? | **OOB main form + PCFs** (email-r4 W4 pivot) | ADR-026 Path-A exception |
| Channel type | New value vs reuse TeamsMessage? | **New `Message = 100000004`** (Dataverse choice exists) | C# enum extended to match |
| ADR number | Which ADR? | **ADR-046** (spine takes 047) | Placeholder reserved main-session |
| SMS | In R1? | **No** — R2 (toll-free verification gate) | Channel-ref table SMS-ready |
| Teams/portal | In R1? | **No** — R2/R3 | R1 is MDA-only |

---

## Assumptions

- **Thread topic**: derived from the first message's subject (email) or a default/participant-based label (chat), editable later — no dedicated topic-authoring UI in R1.
- **Regarding anchor targets**: the same target set the association engine already supports (matter, project, invoice, service request, work assignment, event, contact, organization).
- **Ad-hoc named group threads** (N-party, not record-anchored) are deferred to a later phase (only record-anchored + 1:1 direct in R1).
- **Point-forward only**: historical email is not backfilled into the thread model in R1.
- **Poll interval** ~5s is a configurable default, tunable per NFR-07.

---

## Unresolved Questions

- [ ] **Private-grant primitive** — `GrantAccess` (POA) vs `sprk_externalrecordaccess` overlay for private-thread participants — **Blocks**: private-thread schema/enforcement detail. Resolve in **Phase 0** (verification, not a design fork).
- [ ] **`kind` taxonomy + `threadId` contract** joint intake with `notification-spine-r1` + `assistant-r1.5` — **Blocks**: messaging **R2** consumption only (not R1). Ratified direction: two kinds (`communication-assessed` / `communication-arrived`) + `threadId` envelope + Dataverse-security-derived targeting.
- [ ] **Poll-load ceiling at scale** (many concurrent open forms at ~5s) — **Blocks**: nothing in R1; monitor; may motivate earlier spine-push adoption in R2.

---
*AI-optimized specification. Original design: `design.md`.*
