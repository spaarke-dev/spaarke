# ADR-046: ACS Messaging Channel — Transport, First-Class Threads, Inbound Ingestor Seam

> **Status**: Accepted
> **Date**: 2026-07-16
> **Domain**: Communication (messaging channel over the ADR-045 seams)
> **Source project**: `messaging-communication-app-r1`
> **Concise version**: `.claude/adr/ADR-046-acs-messaging-channel.md`
> **Supersedes**: the reserved ADR-046 placeholder (number claimed 2026-07-16). Sibling ADR-047 reserved for `spaarke-notification-spine-r1`.

---

## Context

### Where the communication platform stood after R4

ADR-045 (`email-communication-solution-r4`) unified Spaarke's communication backbone: `sprk_communication` is the record, a single **association engine** matches each message to related records over a **normalized envelope**, one `ICommunicationEnrichmentService` runs on both directions, and two **channel seams** — `ICommunicationChannelSender` and `ICommunicationArchiver`, dispatched by `sprk_communicationtype` — were defined with email as the only implementation. `CommunicationService` is Graph-free; adding a channel was declared to be "register two impls + a `CommunicationType`."

Two things ADR-045 named but did not build:

1. **A second channel.** The seams were asserted but only ever exercised by email — the abstraction was unproven.
2. **An inbound seam.** Inbound capture (`IncomingCommunicationProcessor` + `Engine/`) was concrete and email-only. There was no `ICommunicationChannelIngestor`.

And one thing the platform never had: **a conversation grouping key**. "Threading" was ancestry-walking on `sprk_inreplyto`/`sprk_internetmessageid` — reconstructible by following pointers, but with no thread record and no grouped view. A matter showed a flat list of messages, not grouped conversations.

### The forces this ADR resolves

- Add real-time chat **without** a second communication pipeline, a second regarding mechanism, or a second channel-provider contract (owner-locked MUST-NOTs).
- Prove the ADR-045 seam abstraction against a genuinely different transport (ACS Chat — an external Azure service with its own identity plane, live delivery, and event stream).
- Complete the inbound seam so chat capture is not messaging-forked and future channels (SMS, portal) are additive.
- Give **all** channels a first-class, queryable conversation home so email reply-chains and chat threads render in one integrated view.
- Do all of this as a **usable async experience** in R1 (server plumbing + polling UI) while structuring the code so the live open channel, spine-pushed notifications, SMS, and Teams/portal surfaces add to the same component/engine later without re-opening it.

### Why ACS, and what "transport vs record" means here

Azure Communication Services Chat is a Microsoft-precedented "trusted service + client" model (the same pattern D365 Contact Center uses). The BFF **is** the trusted service: it mints identities and tokens and mutates participants; clients hold only short-lived user tokens (and in R1, none at all). Critically, an ACS chat message is a **transport artifact** — a JSON event — exactly as an email's `.eml` is a transport artifact. The BFF processes it into a durable `sprk_communication` record + a transcript archive in SPE. ACS threads are minimized (30-day retention) and reconstructible; **Dataverse is the system of record**.

---

## Decision

Messaging is a **provider on the ADR-045 process**, governed by the seven rules in the concise ADR (`.claude/adr/ADR-046-acs-messaging-channel.md`). In summary:

1. **ACS-as-transport / Dataverse-as-record** — every message persists as `sprk_communication` (type=`Message`=100000004) + SPE transcript via `ICommunicationArchiver`.
2. **Uniform server-side token minting** — the BFF mints `chat`-scoped ACS tokens for all participants (Entra→ACS exchange is VoIP-only); `communicationUserId` ↔ Dataverse identity mapping; no ACS capability reaches a client in R1.
3. **Inbound ingestor seam** — new `ICommunicationChannelIngestor` alongside sender/archiver; inbound = Event Grid → validated webhook → Service Bus job → normalizer → ingestor → persist → enrichment.
4. **Direction-symmetric persistence + idempotent capture** — outbound persist-on-send, inbound persist-on-event, both enriched; dedupe on ACS message id via `IIdempotencyService` (covers redelivery + duplicates + our own echo); DLQ from day one.
5. **First-class thread model** — `sprk_communicationthread` + `sprk_thread` lookup + thread↔channel child table, assigned by a direction-symmetric `IThreadResolver`; email conversations become grouped threads too (point-forward); record-anchored + 1:1 direct topologies.
6. **Access = Dataverse record security** — open membership via `MembershipResolverService` (ADR-034); private via per-record sharing grant; ACS membership is a reconciled projection, never a parallel ACL; privacy/internal-only/privilege enforced by BFF query-filters; point-forward switch; AI flags privilege, never decides.
7. **Retention-minimized + per-customer resource** — 30-day ACS auto-delete; per-residency-boundary ACS resource + Event Grid provisioned via the orchestrator (ADR-027).

The binding MUST / MUST NOT list is in the concise ADR's **Constraints** section.

---

## Alternatives Considered

- **Grouping key: `sprk_conversationid` field (design option C) instead of a thread entity.** A single field on `sprk_communication` (email = ancestry-root id; chat = ACS `ChatThreadId`) groups conversations and drives the timeline with no new entity. **Rejected as the R1 base** because thread-level privacy state and the participant set have no home (privacy would be per-message only) and the ACS `ChatThreadId` + future channel refs have nowhere to live. The Phase-0 audit confirmed the field does not exist in the live schema, so there was no migration cost to choosing the entity. Option (A) `sprk_communicationthread` is upgradable-from-C and was locked. (Design option B — regarding + JSON — was rejected outright: wrong dimension and not queryable.)
- **Reuse `TeamsMessage` (100000001) as the chat channel value.** Rejected — messaging over ACS is not native Teams chat (which is an owner-locked MUST-NOT), and the Dataverse choice already carries a distinct `Message` (100000004) member. The C# enum is extended to match.
- **Client-side ACS SDK / ACS composites in R1.** Rejected for R1. The composites (`@azure/communication-react`) bundle Fluent v8, are unsupported on React 19, and drag in `@azure/communication-calling`. R1 has **no** client-side ACS SDK at all — the timeline polls the BFF for persisted records. The live-channel upgrade (next project) uses the **headless** `@azure/communication-chat` SDK on the same component. Recorded so the next project does not relitigate it.
- **Build a messaging notification fabric (SignalR) in R1.** Rejected — cross-surface fan-out is owned by `spaarke-notification-spine-r1`; messaging is its R2 consumer. R1's unread indicator is polling-based. Because messaging already persists + enriches (emitting `communication_assessed`), it is spine-ready for free with zero R1 fabric.
- **A separate messaging microservice.** Rejected per BFF governance (root §10) — the BFF is the sole policy-enforcement + token-minting + `sprk_communication` mutation point; a separate service would fork enforcement and the engine.

---

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Path | Rationale |
|---|---|---|---|
| **ADR-026** | "Code Pages are the default for new UI" | **A — project-scoped exception** | R1's message surface is the OOB `sprk_communication`/thread main form + PCFs, mirroring email-r4's shipped W4 pivot (the same ADR-026 Path-A exception). It avoids Code Page auth bootstrap + the FCC swap and is the proven lower-risk base. The timeline PCF is form-bound, so it stays within ADR-006 (PCF for form binding). Cited in every PR touching the UI surface. |
| **ADR-045** | Outbound persistence pattern | **C — comply** | The synopsis (D-02) floated capture-on-event for outbound. Resolved by following ADR-045 rule 3: outbound persist-on-send + direction-symmetric enrichment. Not a tension — compliance. |

All other applicable ADRs apply without exception; the net-new surface (ACS transport, thread model, ingestor seam) is codified in this ADR rather than tensioned against an existing one.

---

## Consequences

**Positive**
- Proves the ADR-045 channel abstraction against a genuinely different transport, and **completes** the inbound seam ADR-045 only stated — future channels (SMS, portal) become additive.
- The thread model gives email *and* chat a queryable, groupable conversation home. Email threading becomes a visible feature (grouped reply-chain threads in the same integrated view as chat), not just invisible association-matching.
- Because messaging persists as `sprk_communication` and runs enrichment (both already in scope), it is **spine-ready for R2 with zero added fabric** — R2 swaps the poll for spine push.
- ACS BFF SDKs (`Azure.Communication.Chat` 1.4.0 + `Azure.Communication.Identity` 1.3.1) are thin over `Azure.Core` — negligible against the 60 MB publish ceiling.

**Cost / risk**
- ACS is entirely net-new: per-customer provisioning, Event Grid ingress, an external identity plane. Mitigated by a Phase-0 spike (round-trip + latency + echo-dedup + publish-size) and the `communication-services-authentication-hero-csharp` reference sample.
- The `IThreadResolver` extension **edits shared `Services/Communication/` code** (`ThreadContinuityRung`, `CommunicationService`) that email-r4 shipped. Mitigated by preserving existing email matching under **characterization tests** before extending (point-forward; no historical backfill).
- Privacy / internal-only / privilege enforcement is R1's highest-risk surface (security-sensitive) — explicit `code-review` + `adr-check` gate; ACS membership must never exceed Dataverse-derived access.
- ACS↔Dataverse membership is eventually consistent — reconciled by event-driven + periodic sweep, with an audit entry per change.

**Coordination**
- Shares `Services/Communication/` with `email-communication-solution-r4`; coordinates the `threadId` contract + `kind` taxonomy (`communication-assessed` / `communication-arrived`) with `spaarke-notification-spine-r1`. Run `/conflict-check` before every BFF wave. Not an R1 blocker (R1 polls); the contract binds messaging R2.

---

## Related ADRs

| ADR | Relationship |
|---|---|
| ADR-045 (Communication architecture) | The spine this extends; adds the inbound ingestor seam + the first-class thread model |
| ADR-034 (User-record membership) | Open-thread membership derivation; ACS membership is its reconciled projection |
| ADR-028 (Auth v2) | Central `TokenCredential`; server-side ACS token minting; no `new` credential |
| ADR-004 / ADR-036 (Job contract / background jobs) | Event Grid capture + membership reconcile ride the existing job/DLQ/idempotency contract |
| ADR-007 (SpeFileStore) | Message-transcript + attachment archive to SPE |
| ADR-024 (Regarding family) | Thread anchor; never a second regarding mechanism |
| ADR-027 (Provisioning isolation) | Per-boundary ACS resource + Event Grid (immutable data location) |
| ADR-018 / ADR-032 (Kill-switch / Null-Object) | Feature-gated ACS services consumed by unconditional endpoints |
| ADR-015 (Privilege) | AI flags privilege, never decides |
| ADR-021 / ADR-022 / ADR-026 / ADR-006 / ADR-012 (Fluent v9 + PCF + Code Page) | Polling timeline component + PCF accessories on the OOB form (ADR-026 Path-A exception) |
| ADR-029 / ADR-038 (Publish hygiene / Testing) | ACS SDKs thin over `Azure.Core`; vertical-slice seam tests + email characterization |

---

## Revision Log

| Date | Change | By |
|---|---|---|
| 2026-07-16 | Number reserved as placeholder to prevent collision | `messaging-communication-app-r1` (main-session) |
| 2026-07-16 | Authored concise + full ADR; promoted to **Accepted**; INDEX updated placeholder → Accepted | `messaging-communication-app-r1` task 007 |
