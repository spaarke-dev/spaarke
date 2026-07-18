# ADR-046: ACS Messaging Channel — Transport, First-Class Threads, Inbound Ingestor Seam (Concise)

> **Status**: Accepted
> **Domain**: Communication (messaging channel over the ADR-045 seams) — ACS transport + first-class thread model
> **Last Updated**: 2026-07-16
> **Source project**: `messaging-communication-app-r1` (`projects/messaging-communication-app-r1/design.md` + `spec.md`)
> **Cross-references**: extends **ADR-045** (communication architecture / channel seams); **ADR-034** (user-record membership → open-thread membership); **ADR-028** (Auth v2 — server-side ACS token minting); **ADR-004/036** (job contract — Event Grid capture); **ADR-007** (SpeFileStore — transcript/attachment archive); **ADR-024** (regarding family — thread anchor); **ADR-027** (per-customer resource provisioning); **ADR-018/032** (kill-switch / Null-Object); **ADR-015** (AI flags, never decides — privilege). Sibling: **ADR-047** reserved for `spaarke-notification-spine-r1`.

---

## Context

ADR-045 established one communication platform: `sprk_communication` as the record, a normalized-envelope association engine, direction-symmetric enrichment, and **channel seams** (`ICommunicationChannelSender` / `ICommunicationArchiver`, dispatch by `sprk_communicationtype`) — with email as the sole implementation. It left two things stated-but-unbuilt: a second channel to prove the abstraction, and an **inbound** seam (inbound was concrete/email-only). It also left conversation grouping invisible — "threading" was ancestry-walking on `sprk_inreplyto`, with no thread record and no grouped view.

This ADR adds **messaging (real-time chat) as the second channel**. **Azure Communication Services (ACS) Chat is the transport; Dataverse `sprk_communication` is the system of record; the BFF is the sole policy-enforcement + token-minting point.** No native Teams chat, no Dataverse Activities, no portal comments. R1 is server plumbing + a first-class thread model + a usable **async (polling)** MDA experience — **no live client / no client-side ACS SDK**. The live open channel, spine-pushed notifications, SMS, and Teams/portal surfaces are the deferred upgrade / R2 / R3.

---

## Decision

Messaging is a **provider on the ADR-045 process**, governed by seven coupled rules:

1. **ACS-as-transport / Dataverse-as-record.** Each chat message is a JSON transport artifact the BFF processes into a `sprk_communication` (type=`Message`=100000004) record + a transcript archive in SPE via `ICommunicationArchiver` — the `.eml` analog. ACS threads are reconstructible projections; ACS is never a second system of record.

2. **Uniform server-side token minting.** The BFF (trusted service) creates ACS identities, maps `communicationUserId` ↔ the Dataverse user/contact, and mints `chat`-scoped ACS tokens **server-side for all participants** (the Entra→ACS exchange is VoIP-only, so even internal users are minted server-side). No ACS admin capability — and in R1 no ACS token at all — reaches a client.

3. **Inbound ingestor seam.** Add `ICommunicationChannelIngestor` alongside the shipped sender/archiver seams — completing ADR-045's stated intent so inbound is not messaging-forked. Inbound path = ACS → Event Grid → validated webhook → Service Bus job → ACS-event normalizer → `NormalizedMessage` → ingestor persists → enrichment.

4. **Direction-symmetric persistence + idempotent capture.** Outbound **persist-on-send** (ADR-045 rule 3); inbound **persist-on-event**; both invoke enrichment. Capture is **idempotent on ACS message id** via `IIdempotencyService` — this is the single mechanism that dedupes Event Grid's at-least-once redelivery, duplicate delivery, **and the echo of our own outbound message** (so outbound stays persist-on-send while inbound stays capture-on-event without collision). Dead-letter to Storage from day one.

5. **First-class thread model.** `sprk_communicationthread` entity (topic, anchor via the ADR-024 regarding family, thread-level privacy state, participant set) + `sprk_communication.sprk_thread` lookup + a thread↔channel child table (one row per `(thread, channel, external-ref)`; R1 populates the ACS `ChatThreadId`). Thread assignment is a direction-symmetric `IThreadResolver` (find-or-create, returns `sprk_thread`) invoked by both the inbound capture path and the outbound send path, for **every** channel — email uses `sprk_inreplyto` ancestry, chat uses ACS `ChatThreadId`. Email conversations become grouped threads too (**point-forward**; no historical backfill). Topologies: **record-anchored** and **1:1 direct**.

6. **Access = Dataverse record security.** Open-thread membership derives from `MembershipResolverService` (ADR-034); private threads add an explicit per-record sharing grant; 1:1 direct threads use an explicit two-participant list. **ACS thread membership is a reconciled projection of Dataverse-derived access — never a parallel ACL.** Message/thread privacy, internal-only, and privilege are enforced by BFF query-filters; the thread privacy switch is **point-forward**. AI may FLAG privilege, never decide (ADR-015).

7. **Retention-minimized + per-customer resource.** ACS threads carry 30-day auto-delete retention (or explicit delete-post-persist) so ACS never becomes a shadow store. Because the ACS data location is immutable at create time, residency = a **separate ACS resource per boundary**, provisioned with its Event Grid system topic/subscriptions via the orchestrator (ADR-027).

---

## Constraints

### ✅ MUST

- **MUST** implement messaging over the shipped ADR-045 seams (sender + archiver + the new ingestor) and dispatch by `sprk_communicationtype`; a new channel MUST NOT change the engine, enrichment, regarding model, thread model, or review surface.
- **MUST** persist every message as `sprk_communication`; ACS is transport only.
- **MUST** mint ACS tokens **server-side only**; in R1 no ACS SDK runs on any client (NFR-04).
- **MUST** make capture idempotent on ACS message id (covers redelivery, duplicates, and the outbound echo); DLQ from day one.
- **MUST** derive open-thread membership from `MembershipResolverService` (ADR-034); ACS membership is a reconciled projection; private via existing per-record sharing grant.
- **MUST** extend the ADR-024 regarding family for thread anchoring — never a second regarding mechanism.
- **MUST** inject the central `TokenCredential` + canonical Dataverse interfaces (ADR-028); keep enrichment + thread assignment best-effort and non-fatal.
- **MUST** measure BFF publish size on every BFF-touching task (baseline ~45.30 MB post-R4; ceiling ≤60 MB).

### ❌ MUST NOT

- **MUST NOT** use Dataverse/Power Apps Activities, OOB `email`/activity entities, or portal comments.
- **MUST NOT** capture native Teams chat (Graph chat/channel-message APIs). Teams participates later only as a *host*.
- **MUST NOT** introduce a live client / ACS client SDK / ACS composites (`@azure/communication-react`) in R1 — the live upgrade (next project) uses the **headless** `@azure/communication-chat` SDK, never the composites.
- **MUST NOT** let ACS membership exceed Dataverse-derived access; MUST NOT let AI decide privilege (flag only — ADR-015).
- **MUST NOT** build a messaging-only notification fabric — R1 polls; R2 consumes `spaarke-notification-spine-r1`.
- **MUST NOT** `new` a credential or `ConfidentialClientApplication` in the messaging stack (ADR-028).

---

## Consequences

- **Positive**: proves the ADR-045 abstraction with a genuinely different transport; completes the inbound seam so future channels (SMS, portal) are additive; the thread model gives email *and* chat a queryable, groupable conversation home (email threading becomes a visible feature); once messaging persists + enriches, it is spine-ready for R2 with zero added fabric.
- **Cost / risk**: ACS is entirely net-new (per-customer provisioning, Event Grid ingress, identity plane) — mitigated by a Phase-0 spike and the auth-hero reference sample. The `IThreadResolver` edits shared `Services/Communication/` code email-r4 shipped — mitigated by preserving existing email matching under characterization tests before extending. Privacy/privilege enforcement is the highest-risk surface — explicit code-review + `adr-check` gate. ACS↔Dataverse membership is eventually consistent — reconciled by event + periodic sweep.
- **Coordination**: shares `Services/Communication/` with `email-communication-solution-r4`; coordinates the `threadId` contract + `kind` taxonomy (`communication-assessed` / `communication-arrived`) with `spaarke-notification-spine-r1` (messaging is its R2 consumer). Run `/conflict-check` before every BFF wave.

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule | Path | Resolution |
|---|---|---|---|
| **ADR-026** | Code Pages are default for new UI | **A — project-scoped exception** | R1's message surface is the OOB main form + PCFs (mirrors email-r4's shipped W4 pivot): no Code Page auth bootstrap, no FCC swap, lowest-risk. The timeline PCF is form-bound → remains within ADR-006. |
| **ADR-045** | Outbound persistence pattern | **C — comply** | Outbound persist-on-send + direction-symmetric enrichment per ADR-045 rule 3. Not a tension. |

## Related

| ADR | Relationship |
|---|---|
| ADR-045 (Communication architecture) | The spine this extends — sender/archiver seams + normalized-envelope engine + direction-symmetric enrichment; adds the ingestor seam + thread model |
| ADR-034 (User-record membership) | Open-thread membership derivation; ACS membership is its projection |
| ADR-028 (Auth v2) | Central `TokenCredential`; server-side ACS token minting; no `new` credential |
| ADR-004 / ADR-036 (Job contract) | Event Grid capture + membership reconcile ride the existing job/DLQ/idempotency contract |
| ADR-007 (SpeFileStore) | Message-transcript + attachment archive to SPE |
| ADR-024 (Regarding family) | Thread anchor; never a second regarding mechanism |
| ADR-027 (Provisioning isolation) | Per-boundary ACS resource + Event Grid (immutable data location) |
| ADR-018 / ADR-032 (Kill-switch / Null-Object) | Feature-gated ACS services consumed by unconditional endpoints |
| ADR-015 (Privilege) | AI flags privilege, never decides |
| ADR-021/022/026/006/012 (Fluent v9 + PCF + Code Page) | Polling timeline component + PCF accessories on the OOB form (ADR-026 Path-A) |
| ADR-029 / ADR-038 (Publish hygiene / Testing) | ACS SDKs thin over `Azure.Core`; vertical-slice seam tests + email characterization |

## References

- Full ADR (rationale, alternatives, revision log): `docs/adr/ADR-046-acs-messaging-channel.md`
- Source: `projects/messaging-communication-app-r1/` (`spec.md` FR-17 + §Technical Constraints; `design.md` §3/§4/§5/§6/§8/§10)
- Depends on: ADR-045 (`.claude/adr/ADR-045-communication-architecture.md`)
