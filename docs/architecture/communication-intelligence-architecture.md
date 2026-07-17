# Communication Intelligence Architecture

> **Last Updated**: 2026-07-17
> **Last Reviewed**: 2026-07-17
> **Reviewed By**: email-communication-solution-r4 task 080 (W8 documentation); extended by messaging-communication-app-r1 task 081 (§9 — ACS messaging channel as-built)
> **Status**: Current — canonical
> **Canonical ADR**: [ADR-045 — Communication Architecture](../../.claude/adr/ADR-045-communication-architecture.md) (engine + seams) · [ADR-046 — ACS Messaging Channel](../../.claude/adr/ADR-046-acs-messaging-channel.md) (§9 — second channel + thread model)
> **Purpose**: The canonical architecture reference for the Communication Intelligence subsystem — the normalized-message envelope, the 6-rung Association Engine, the confidence→status ladder + auto-file kill-switch, direction-symmetric enrichment, channel seams, **the ACS messaging channel + first-class thread model (§9)**, the read-only suggestion endpoint, per-rung telemetry, and the shared inbound patterns (idempotency, `.eml`→SPE archival, attachment→Document, RAG indexing). Supersedes and absorbs `email-processing-architecture.md`, `email-to-document-architecture.md`, and `email-to-document-automation.md`.

---

## 1. Overview

Communication is 100% Microsoft Graph (email) plus Azure Communication Services (messaging, §9) over the first-class `sprk_communication` entity — **no Server-Side Sync, no OOB Dataverse `email`/activity dependency** on the go-forward path (both retired; see §13). The subsystem has two halves:

- **Send** — a canonical outbound path (Graph `sendMail`) with a single client composer; see [communication-service-architecture.md](communication-service-architecture.md) for the send/inbound service mechanics (accounts, subscriptions, dedup, verification).
- **Intelligence** — this document. Every communication (inbound *and* outbound) flows through one direction-symmetric enrichment pipeline whose centerpiece is a single **Association Engine** that matches the message to related Spaarke records over a **normalized envelope**, records per-attribute confidence + provenance, and maps confidence to a review status with a reversible auto-file kill-switch.

The design principle throughout: **matching is best-effort and non-fatal** — an enrichment or association failure must never fail the send or the inbound-capture path (NFR-06). Misfile is *re-file* (audited), never *delete*.

**Code entry points**

| Concern | Entry point |
|---|---|
| Enrichment pipeline | `Services/Communication/CommunicationEnrichmentService.cs` (`ICommunicationEnrichmentService`) |
| Association Engine | `Services/Communication/IncomingAssociationResolver.cs` |
| Rung ladder | `Services/Communication/Engine/Rungs/`, `Services/Communication/Engine/Detectors/` |
| Envelope boundary | `Services/Communication/Engine/GraphMessageNormalizer.cs`, `Services/Communication/Models/NormalizedMessage.cs` |
| Status mapping / auto-file | `Services/Communication/Engine/AssociationStatusMapper.cs`, `Services/Communication/Engine/AutoFileGate.cs` |
| Channel seams | `Services/Communication/Channels/` |
| Messaging channel (ACS Chat) — second channel | `Services/Communication/Channels/MessagingChannelSender.cs`, `MessagingArchiver.cs`, `MessagingIngestor.cs`, `Services/Communication/Acs/` (see §9) |
| Suggestion endpoint | `Api/CommunicationEndpoints.cs` (`POST /api/communications/{id}/suggest-associations`) |
| Inbound orchestration | `Services/Communication/IncomingCommunicationProcessor.cs` |

---

## 2. The normalized message envelope

The engine — and every rung — operates over a channel-neutral **`NormalizedMessage`** envelope, **never `Microsoft.Graph.Message`** (ADR-045 MUST). `GraphMessageNormalizer.Normalize(Message, CommunicationDirection)` is the *single* Graph→envelope boundary (pure mapping, no I/O). The envelope carries: `Direction`, `From`, `To[]`, `Cc[]`, `Subject`, `BodyText`/`BodyHtml`, `InternetMessageId`, `InReplyTo`, `References[]`, `ConversationId`, `SentAt`, and normalized `Attachments[]` (`Name`, `ContentType`, `SizeBytes`, `IsInline`).

**Why**: this is the extensibility seam. Adding a channel (Teams/Slack/Gmail/SMS) means adding a *normalizer* to this envelope plus a sender/archiver — with **no change** to the engine, the rungs, the regarding model, or the review UI. No rung may accept a channel-specific type as input.

---

## 3. Direction-symmetric enrichment

`ICommunicationEnrichmentService` (impl `CommunicationEnrichmentService`) is invoked by **both** the inbound path (`IncomingCommunicationProcessor`) and the outbound path (`CommunicationService`) — sent mail receives the same treatment as received mail *by construction* (ADR-045 rule 3). It owns a fixed-order pipeline; each step is wrapped in a try/catch that logs and continues (best-effort / non-fatal, NFR-06):

1. **Association** — run the Association Engine (§4–§7).
2. **Categorization** — signal categorization.
3. **AI analysis** — enqueue document/message analysis.
4. **RAG indexing** — index message + attachment text into the knowledge index (§12).
5. **Responsive-Intelligence trigger** — emit the `communication_assessed` structured signal (§14).

`EnrichAsync(communicationId, direction, NormalizedMessage, archivedDocumentId?, ct)` is the entry signature. In the shipped R4 state several legs are wired at their more-specific call sites rather than centrally (e.g. inbound RAG indexing runs inline in `IncomingCommunicationProcessor`; the outbound RAG leg runs here); step 5 is **emit-only** (a structured log line, no consumer in R4 — see §13). The invariant that matters architecturally is the ordering and the direction symmetry, not which method physically hosts each leg.

---

## 4. The 6-rung Association Engine

`IncomingAssociationResolver` is the Association Engine. (The class name still reads "Incoming" for historical reasons, but it now serves both directions and the read-only evaluate path.) It injects `IEnumerable<IAssociationRung>` and partitions them into a **deterministic tier (rungs 0–3)** and an **AI tier (rungs 4–5)**, each ordered by the rung's `Order`. Each rung implements `IAssociationRung` — `RungKind Kind`, `int Order`, `Task<IReadOnlyList<RungMatch>> EvaluateAsync(NormalizedMessage, AssociationContext, ct)`. A rung that throws is treated as a non-match (NFR-06).

Two public modes:

- **`ResolveAsync(...)`** — evaluate **and write** the decision to the record (inbound/outbound enrichment path).
- **`EvaluateAsync(...)`** — evaluate **only**, no write (powers the suggestion endpoint, §11).

The engine runs the deterministic tier first, asks the status mapper to `Decide`, and **only if that did not auto-file** runs the AI tier and re-`Decide`s. This is what makes deterministic matches cheap and keeps AI cost bounded (ADR-016).

### 4.1 The rung ladder

| Rung | `RungKind` | Class (`Engine/Rungs/`) | What it matches | Dependency |
|---|---|---|---|---|
| **0** | `ExplicitReference` | `ExplicitReferenceRung` | (a) caller-supplied regarding (confidence 1.0) across all mapped targets; (b) matter-reference regex in subject (`MAT-…`, `Matter #…`, `SPRK-…`, `[MATTER:…]`) → `sprk_matter` (0.9) | `ICommunicationDataverseService` |
| **1** | `ThreadContinuity` | `ThreadContinuityRung` | Walks `In-Reply-To` then `References` (newest→oldest); nearest ancestor that exists as a `sprk_communication` → copies its regarding lookups verbatim (1.0) | `ICommunicationDataverseService` |
| **2** | `ParticipantCorrelation` | `ParticipantCorrelationRung` | Sender email → contact (`sprk_regardingperson`); user-entity memberships; sender domain → `sprk_organization` **and** `account` as distinct lookups. Skips common providers; caps recipients | `ICommunicationDataverseService` |
| **3** | `StructuralDetector` | `StructuralDetectorRung` | Runs the structural detectors (§4.2); lifts each `StructuralMatch` into a `RungMatch` carrying category + obligations (metadata signals, usually no target) | `IEnumerable<IStructuralDetector>` |
| **4** | `SemanticMatch` | `SemanticMatchRung` | Hybrid (vector + keyword, RRF) semantic record match over the records index for matter/project/invoice | `IRecordMatchingAi` (facade) |
| **5** | `AiClassification` | `AiClassificationRung` | LLM extract + classify → metadata-only signals (category, obligations, rationale) + a separate privilege-flag signal | `ICommunicationClassificationAi` (facade) |

The AI rungs (4/5) reach AI **only through the `Services/Ai/PublicContracts/` facades** (`IRecordMatchingAi`, `ICommunicationClassificationAi`) per ADR-013 — never internal AI types. Both are individually feature-gated (`Communication:SemanticMatch:Enabled`, `Communication:AiClassification:Enabled`). Rung 5 may **flag** privilege but never **decides** it (ADR-015).

### 4.2 Structural detectors

`Engine/Detectors/` holds pure, I/O-free `IStructuralDetector`s (`StructuralMatch? Detect(NormalizedMessage)`). R4 ships four, all producing category/obligation signals (no target in R4): `CalendarInviteDetector` (`text/calendar`/`.ics` → `event`), `ESignCompletionDetector` (DocuSign/Adobe Sign completion → `esign-completion`), `InvoiceNumberDetector` (`INV-…` → `invoice`), `CourtEFilingDetector` (e-filing/court-notice → `court-notice`). New detectors register additively.

### 4.3 Targets (the ADR-024 regarding family)

The engine writes to the existing ADR-024 regarding family — it **never introduces a second regarding mechanism**. `Engine/RegardingFieldMap.cs` is the ordered entity→field map: `sprk_matter`→`sprk_regardingmatter`, `sprk_project`, `sprk_invoice`, `sprk_servicerequest` (R4 eighth primary target), `sprk_workassignment`, `sprk_event`, `sprk_budget`, `sprk_analysis`, `sprk_organization`→`sprk_regardingorganization`, `account`, `contact`→`sprk_regardingperson`. The org target is `sprk_organization` (Spaarke's first-class org entity), **never** OOB `account`/`organization` (ADR-045 MUST). On write, the engine also populates the ADR-024 polymorphic resolver fields (`sprk_regardingrecordtype/id/name/url`).

---

## 5. Confidence → status ladder

`AssociationStatusMapper.Decide(matches, direction, tenantKey)` collapses rung matches into a single `AssociationDecision { Status, RegardingWrites, AutoFiled, Provenance }`. Status option-set values are in `Engine/AssociationStatusCodes.cs` and mirror the `sprk_associationstatus` choice (see [sprk_communication.md](../data-model/sprk_communication.md)):

| Status | Value | When |
|---|---|---|
| **Resolved** | 100000000 | Deterministic (rungs 0–3) reinforced confidence ≥ threshold **and** the auto-file kill-switch is ON — writes deterministic winners, `AutoFiled=true` |
| **Pending Review** | 100000001 | No field winner, or top confidence < `0.50` |
| **Unresolved** | 100000002 | Legacy value; never written by R4, treated as Pending Review |
| **Suggested** | 100000003 | Has winners but not auto-file-eligible (includes any AI-derived field, or kill-switch OFF) — writes suggested fields for review |
| **Ambiguous** | 100000004 | 2+ distinct targets on the same field each ≥ the resolved threshold (conflict) — **no writes** |

### 5.1 Noisy-OR reinforcement

A field's confidence is combined across the *distinct rung kinds* that voted for it via **Noisy-OR** (`1 − Π(1−cᵢ)`, clamped [0,1]). Per-target contributors are first collapsed to the max per rung kind, so a single rung cannot inflate its own confidence by voting twice. Independent rungs agreeing reinforce; a lone rung does not.

### 5.2 Auto-file kill-switch (ADR-018)

`AutoFileGate.Resolve(tenantKey)` returns `(Enabled, Threshold)` read from `IOptionsMonitor` **on every call** — so the switch flips **without redeploy**, with per-tenant overrides. R4 ships **auto-file ON for deterministic rungs ≥ 0.85** (owner decision, recorded as ADR-045 Path-A exception). When the switch is OFF, a deterministic match that would have been `Resolved` is **demoted to `Suggested`** (suggest-only mode).

### 5.3 AI rungs never auto-file (invariant)

The auto-file test uses `topDet` = Noisy-OR over **deterministic** contributors only; the Resolved branch writes **deterministic winners only**. AI rungs (4/5) contribute to the *full* confidence (so they can lift Pending Review → Suggested) but **can never reach Resolved** (ADR-045 MUST NOT). Auto-filing is a property of deterministic evidence exclusively.

---

## 6. Provenance

Every decision serializes an `AssociationProvenance` document (`Engine/AssociationProvenance.cs`) to `sprk_associationprovenance` (camelCase JSON, capped at 10,000 chars with a compact fallback). It records the decision trace (`Status`, `AutoFiled`, `KillSwitchEnabled`, `AutoFileThreshold`, `TopDeterministicConfidence`, `TopConfidence`, `AiInvolved`, `Reason`), the rungs fired, per-candidate traces (field, target, reinforced + deterministic confidence, written?, conflict?, per-rung contributors), and metadata signal traces (category, confidence, obligations). This is the audit trail that makes auto-file reversible and review defensible.

---

## 7. Per-rung telemetry (EventId 4501 / 4502)

`IncomingAssociationResolver` emits two structured telemetry events (DEC-8 / NFR-05):

- **4501 `AssociationRungTelemetry`** — one record per rung attempt (`Rung`, `RungOrder`, `IsAiRung`, `Outcome` = fired/skipped/error, `MatchCount`, `ElapsedMs`). Warning level on error, else Information.
- **4502 `AssociationResolvingRung`** — one summary per envelope (`Tier` status label, `AutoFiled`, the rungs that contributed to written targets, rungs fired).

These make the ladder observable per message without exposing message content.

---

## 8. Channel seams (ADR-045 rule 4)

`Services/Communication/Channels/` defines the abstraction that keeps the engine channel-agnostic:

- **`ICommunicationChannelSender`** (`SupportedType`, `SendAsync`) — email impl `EmailChannelSender` wraps Graph `sendMail`.
- **`ICommunicationArchiver`** (`SupportedType`, `GenerateEml`) — email impl `EmailArchiver` over `EmlGenerationService`.
- **`CommunicationChannelDispatcher`** resolves sender/archiver by `sprk_communicationtype`; an unregistered channel returns `CHANNEL_NOT_SUPPORTED` (400).

**Email was the only implementation through R4** (ADR-045 MUST NOT build other channels *in R4*). **Messaging (ACS Chat) is the second implementation, shipped by `messaging-communication-app-r1`** — see §9. The NFR-04 extensibility contract held: the new channel is a new sender + archiver + ingestor + normalizer, with **no change** to the engine, enrichment, regarding model, or review UI.

---

## 9. The messaging channel (ACS Chat) — second channel as-built

**Codified in [ADR-046](../../.claude/adr/ADR-046-acs-messaging-channel.md)** (concise; full: [`docs/adr/ADR-046-acs-messaging-channel.md`](../adr/ADR-046-acs-messaging-channel.md)). Messaging is not a new subsystem — it is the second provider on the ADR-045 process (§8), proving the channel-seam abstraction against a genuinely different transport and completing the **inbound ingestor seam** ADR-045 asserted but left unbuilt. It also adds the platform's first **first-class thread model**, which email retroactively adopts (§9.2). **Azure Communication Services (ACS) Chat is the transport; `sprk_communication` remains the sole system of record.**

**Code entry points**

| Concern | Entry point |
|---|---|
| Thread resolver (direction-symmetric find-or-create) | `Services/Communication/IThreadResolver.cs`, `ThreadResolver.cs`, `Threads/IThreadKeyStrategy.cs`, `Threads/EmailThreadKeyStrategy.cs`, `Threads/MessagingThreadKeyStrategy.cs` |
| Messaging channel seam impls | `Services/Communication/Channels/MessagingChannelSender.cs`, `MessagingArchiver.cs`, `MessagingIngestor.cs`, `Channels/ICommunicationChannelIngestor.cs` |
| ACS identity + server-side token minting | `Services/Communication/Acs/AcsIdentityService.cs`, `IAcsIdentityService.cs` |
| ACS thread lifecycle (create, retention, participants) | `Services/Communication/Acs/AcsThreadService.cs`, `IAcsThreadService.cs` |
| Membership derivation + reconcile (ADR-034 projection) | `Services/Communication/Membership/ThreadMembershipDerivationService.cs`, `IThreadMembershipDerivationService.cs`, `MembershipReconciler.cs`, `MembershipReconcileSweepService.cs` |
| Event Grid ingress (webhook) | `Api/AcsEventGridEndpoints.cs`, `Services/Communication/Acs/AcsEventGridIngressService.cs` |
| Inbound normalizer + job handler | `Services/Communication/Engine/AcsEventNormalizer.cs`, `Services/Jobs/Handlers/IncomingMessagingJobHandler.cs` |
| Impersonated read + access filter (privacy enforcement) | `Services/Communication/IImpersonatedCommunicationQuery.cs`, `CommunicationThreadReadService.cs`, `Access/CommunicationAccessFilter.cs`, `Access/ICommunicationAccessFilter.cs` |
| Read/compose UI | `src/client/pcf/CommunicationTimeline/` (polling timeline), `src/client/pcf/CommunicationMessageActions/` (send/respond accessories) |

### 9.1 The first-class thread data model

`sprk_communicationthread` is a thin, first-class thread record — the queryable grouping key the platform never had (previously, "threading" was ancestry-walking on `sprk_inreplyto`, reconstructible but not queryable or groupable). It carries: `sprk_threadtype` (`Record-Anchored = 100000000`, `Direct 1:1 = 100000001`), `sprk_privacystate` (`Open = 100000000`, `Private = 100000001`), `sprk_privacyeffectivefrom` (the **point-forward** switch — prior messages keep their prior visibility when the thread flips Open↔Private), and the reused ADR-024 regarding pointer fields (`sprk_regardingrecordid/type/name/url`) as the thread's anchor — **the same fields and resolver `sprk_communication` already uses, not a second regarding mechanism**.

`sprk_communication.sprk_communicationthread` is the lookup that groups messages onto their thread (OData `_sprk_communicationthread_value`). A separate child table, `sprk_communicationchannelref`, holds one row per `(thread, channel, external-ref)`: `sprk_thread` → the parent thread, `sprk_channeltype` = the same global `sprk_communicationtype` choice the engine already uses (`Message = 100000004`), and `sprk_externalref` carries the channel's external key — for R1 the ACS `ChatThreadId`. This indirection is what lets a future channel (SMS, portal) attach with no schema change ("channel is an attribute" of the ref row, not the thread). `MessagingThreadKeyStrategy` (§9.2) looks up an existing channel-ref row by `sprk_externalref` + `sprk_channeltype` to join a thread, and creates the row when a new thread is created.

**Access is enforced by Dataverse impersonation at read time, not recomputed in code.** The BFF thread-read path (`CommunicationThreadReadService` → `IImpersonatedCommunicationQuery`) issues the Dataverse query with the `MSCRMCallerID` header set to the caller's `systemuserid` (not the AAD `oid`), so Dataverse's native security engine (ownership + role depth + teams + sharing + hierarchy) returns exactly the rows that caller may see — there is no hand-computed access union. `CommunicationAccessFilter` layers on top of the already-impersonated rows to apply only the two rules impersonation does not cover: `sprk_isinternalonly` (hide from non-internal callers, fail-closed on an unreadable flag) and `sprk_privilegeclassification` (metadata only — it never gates a read; AI may flag privilege but never decide it, per ADR-015). See `projects/messaging-communication-app-r1/notes/access-model-decision.md` for the full rationale, including the open finding on Open-thread message ownership that the decision record flags for follow-up.

### 9.2 The IThreadResolver — direction-symmetric find-or-create

`IThreadResolver` (impl `ThreadResolver`) is the thread analog of the direction-symmetric enrichment invocation (§3 / ADR-045 rule 3): **one** resolver, invoked from **both** the inbound capture path and the outbound send path, for **every** channel — mirroring how `ICommunicationEnrichmentService` is invoked symmetrically. It dispatches to a per-channel `IThreadKeyStrategy`: `EmailThreadKeyStrategy` resolves the thread from the `sprk_inreplyto`/`sprk_internetmessageid` ancestry the engine already walks (§4.1 rung 1); `MessagingThreadKeyStrategy` resolves it from the ACS `ChatThreadId` via the `sprk_communicationchannelref` lookup (§9.1). A `Message`-channel request with no ACS thread id yet (e.g. outbound send before the inbound echo carries the authoritative id) is a no-op rather than creating an ungroupable orphan thread.

Call sites: inbound email extends `ThreadContinuityRung` (§4.1 rung 1) so the ancestry walk it already performs also assigns the thread — no new matching logic; inbound chat runs through `MessagingIngestor` / `IncomingMessagingJobHandler`; outbound (both channels) runs through the `CommunicationService` send path. This is the concrete mechanism behind the design's "email conversations become grouped threads" feature (design.md §6.5) — an extension of shipped email matching, not a rewrite. Resolution is **best-effort and non-fatal** (NFR-02): a failure logs and returns `null`; the message still persists without a thread.

### 9.3 ACS as transport, `sprk_communication` as record

Per ADR-046 rule 1, an ACS chat message is a transport artifact exactly as an email's `.eml` is — the BFF processes it into a durable `sprk_communication` record; ACS threads are reconstructible and never a second system of record. `AcsIdentityService` (`IAcsIdentityService`) maps the ACS `communicationUserId` to the Dataverse `systemuser`/`contact` via the `sprk_communicationuserid` column present on both entities, and mints `chat`-scoped tokens server-side from a `CommunicationIdentityClient` rooted in the central `TokenCredential` (ADR-028 / NFR-05 — no inline credential). `AcsThreadService` (`IAcsThreadService`) creates ACS threads with a 30-day auto-delete retention policy set at create time (design §8.7), so ACS never becomes a shadow record store.

Tokens are minted server-side only, and **R1 has no client-side ACS SDK at all** — the polling timeline PCF (`CommunicationTimeline`) and the send/respond accessories PCF (`CommunicationMessageActions`) read persisted `sprk_communication` rows through the BFF's impersonated thread-read + unread-count endpoints (§9.1), never calling ACS directly. The live-channel upgrade (headless `@azure/communication-chat`, no composites) is explicitly deferred to the next project (design §8.5).

ACS thread **membership is a reconciled projection of Dataverse-derived access, never a parallel ACL** (ADR-034; ADR-046 rule 6). `ThreadMembershipDerivationService` (`IThreadMembershipDerivationService`, `Services/Communication/Membership/`) derives open-thread membership from the thread's anchor record the same way the platform's other membership derivation does; `MembershipReconciler` and `MembershipReconcileSweepService` push that derived set onto the ACS thread's participant list event-driven plus a periodic sweep, since ACS membership is eventually consistent with Dataverse. ACS membership must never exceed what the derived Dataverse access already grants.

### 9.4 The inbound ingestor seam

`ICommunicationChannelIngestor` (`Services/Communication/Channels/`) is the net-new third leg of the channel-seam triad ADR-045 stated but left unbuilt — the inbound counterpart to `ICommunicationChannelSender` / `ICommunicationArchiver` (§8), resolved by the same `CommunicationChannelDispatcher`. `MessagingIngestor` is its first (R1's only) implementation: given an already-normalized `NormalizedMessage`, it persists a `sprk_communication` (`sprk_communicationtype = Message = 100000004`, `Direction = Incoming`) via `IGenericEntityService` and invokes shared enrichment best-effort (NFR-02) — reusing the same persist + enrichment services email inbound uses, so inbound capture is not forked per channel.

The capture pipeline: **Event Grid webhook** (`Api/AcsEventGridEndpoints.cs`, `AllowAnonymous` but authenticity-checked via the subscription-validation handshake + a fail-closed topic allow-list + optional shared secret, delegated to `AcsEventGridIngressService`) → an `IncomingMessaging` Service Bus job → **`IncomingMessagingJobHandler`** → **`AcsEventNormalizer`** (the ACS analog of `GraphMessageNormalizer`, §2) maps the raw event to a `NormalizedMessage` → **`MessagingIngestor`** persists.

**Idempotent, echo-deduping capture (NFR-03).** `IncomingMessagingJobHandler.IdempotencyKeyFor(acsMessageId)` (`"acs-msg:{id}"`) is the single dedupe key, checked/set through the existing `IIdempotencyService` — the same idempotency contract email inbound uses. It collapses three duplicate sources into one persisted record: Event Grid's at-least-once redelivery, genuine duplicate delivery, **and the Event Grid echo of the messaging channel's own outbound send** (outbound persist-on-send marks the same key so the inbound echo is a no-op). The handler acquires a processing lock, re-checks under the lock, persists, then marks processed — persist-before-mark so a crash between the two re-processes on redelivery rather than silently dropping the message. Terminal failures dead-letter via `JobOutcome.Poisoned` (ADR-004/036 job contract) rather than looping or dropping.

The `sprk_communicationthread` lookup is **not** set by this seam — `MessagingIngestor` persists only the denormalized `sprk_acsthreadid` transport id; the thread lookup is assigned afterward by `IThreadResolver` (§9.2) on the shared resolver path.

---

## 10. Review + compose surface — OOB form + PCFs

The `sprk_communication` record surface is the **OOB model-driven form**, enhanced with PCF controls in the form's native 66/34 layout — **not** a full Code Page (this is the W4 architecture pivot; see `projects/email-communication-solution-r4/notes/W4-architecture-pivot-oob-form-pcf.md`). The platform owns field layout, the rich-text body editor, the attachment subgrid, save, audit, and navigation; two PCFs add the design-novel value:

- **Connections PCF** — multi-connection association review in the form's right "accessories" column, over the ADR-024 regarding family (reuses `PolymorphicResolverService.applyResolverFields`).
- **Communication Actions PCF** — Reply / Forward / Send / Save Draft actions calling the existing `/api/communications/send`. This **replaces the retired ~1,150-LOC ribbon web resource `sprk_communication_send.js`** (see §13).

> The Outlook add-in save-pane consumes the same server capabilities via `@spaarke/auth` `OfficeNaaStrategy` (ADR-028). The messaging channel's analogous review/compose surface is the `sprk_communicationthread` OOB form + the `CommunicationTimeline` / `CommunicationMessageActions` PCFs — see §9.3.

---

## 11. Read-only suggestion endpoint

`POST /api/communications/{id}/suggest-associations` (`Api/CommunicationEndpoints.cs`, name `SuggestCommunicationAssociations`) evaluates the rungs **on demand without writing**. The handler calls `CommunicationService.ReconstructEnvelopeAsync(id)` (a Dataverse Retrieve that rebuilds the `NormalizedMessage` from stored columns) then `IncomingAssociationResolver.EvaluateAsync(...)` — the evaluate-only path that never calls Dataverse write. It returns `SuggestAssociationsResponse` (status, auto-file eligibility, candidate + signal traces). This is the surface a review UI or add-in calls to preview what the engine *would* suggest.

---

## 12. Shared inbound patterns (absorbed)

These patterns (previously in the retired `email-processing-architecture.md`) remain in force for the R4 **Graph** inbound flow, orchestrated by `IncomingCommunicationProcessor`. The messaging channel's inbound patterns (idempotency, DLQ) are the ACS analog documented in §9.4 — they share the *contract* (`IIdempotencyService`, the job/DLQ infrastructure) but not this code path:

- **Idempotency (4 layers)** — (1) in-memory dedup at the webhook endpoint; (2) Service Bus `IdempotencyKey`; (3) Dataverse query on `sprk_graphmessageid`; (4) Dataverse duplicate-detection rule. Guarantees exactly-once processing when the webhook + polling-backup fire for the same message.
- **`.eml` → SPE archival** — the message is rendered to RFC-2822 `.eml` (`GraphMessageToEmlConverter`; outbound via `EmailArchiver`/`EmlGenerationService`), uploaded to SPE, and captured as a parent `sprk_document`.
- **Attachment → Document** — each retained attachment becomes a child `sprk_document`, linked on the `sprk_communicationattachment` intersection record via `sprk_document`. Attachment filtering excludes blocked extensions and tiny signature images.
- **RAG indexing** — the `.eml` body and attachment text are enqueued for indexing into the knowledge index via `IPostUploadIndexingEnqueuer` (app-only / managed-identity writer), source `InboundEmail` / `OutboundEmail`, so communication content becomes searchable knowledge without manual action.
- **Auth** — inbound webhooks and background processing use **app-only** auth (no user context); user-mode send uses OBO (ADR-028). See [communication-service-architecture.md](communication-service-architecture.md).

> These patterns pre-date R4 and are stable; the R4 change is that they now run over the Graph-native `sprk_communication` flow (never OOB `email` activities) and feed the Association Engine over the normalized envelope.

---

## 13. Retirements

- **OOB `email` activity subsystem — RETIRED** (task 007). The legacy webhook/poll-over-Dataverse-`email` path, `Services/Email/EmailPollingBackupService`, `EmailAssociationService`, the `sprk_email`-activity model, and `MapEmailEndpoints` are gone. Inbound email is 100% Graph via `IncomingCommunicationProcessor`. *(A few shared helpers under `Services/Email/` — attachment filtering, `.eml` conversion — survive as live infra consumed by the Graph path; they are not the retired subsystem.)*
- **Server-Side Sync (SSS) — RETIRED / never used.** Send is 100% Graph `sendMail`; there is no Exchange SSS dependency.
- **Ribbon send web resource `sprk_communication_send.js` — RETIRED** (tasks 044c/062), replaced by the Communication Actions PCF (§10).
- **Hand-computed union access filter / `IThreadPrivateGrantProvider` deny-all path — RETIRED for reads** (messaging-communication-app-r1, 2026-07-16). Record-level messaging access is now Dataverse impersonation (§9.1); see `notes/access-model-decision.md`.

Retiring the OOB subsystem offsets the R4 additions against the BFF publish-size ceiling (ADR-029).

---

## 14. Responsive Intelligence (downstream, re-homed — out of R4 scope)

Turning an assessed communication into an **auto-created Event / Task / Notification** (the "Responsive Intelligence" leg) was **NOT built in R4**. R4 ships the *producer signal*: enrichment step 5 emits a `communication_assessed` structured log (emit-only) — the exact publish point a consumer would subscribe to. The consumer (fan-out) was deliberately **re-homed to `spaarke-notification-spine-r1`** because it requires infrastructure that must be built **once, shared** across three converging consumers (assistant proactive suggestions, messaging cross-channel fan-out, communication RI) rather than forked into a communication-only hub.

The target architecture is a **4-layer notification/action spine**: (A) session-agnostic domain-action executors (`CreateEvent`/`CreateTask`/`CreateNotification`), (B) a `kind`-typed durable per-user outbox, (C) Azure SignalR real-time delivery, (D) per-source policy — with **SSE demoted to a chat-only presentation adapter**. Communication RI becomes one fire-and-forget producer on that spine. It must **not** route through the chat-session/SSE-shaped `EventRulesService.FireAsync` (semantically wrong for a session-less inbound message).

> **This is out-of-R4-scope, not shipped.** See `projects/email-communication-solution-r4/notes/W5-responsive-intelligence-and-shared-notification-spine.md` and `projects/spaarke-notification-spine-r1/design.md` for the full finding, the three hard blockers, and the shared-spine design. The messaging channel (§9) already persists + enriches, so it is spine-ready for R2 with zero added fabric (design §7); R1's unread indicator (§9.3) is polling-based.

---

## 15. ADR cross-references

| ADR | Relationship |
|---|---|
| [ADR-045](../../.claude/adr/ADR-045-communication-architecture.md) | **Canonical** — the four coupled rules (canonical send, engine over envelope, direction-symmetric enrichment, channel seams) |
| [ADR-046](../../.claude/adr/ADR-046-acs-messaging-channel.md) (ACS Messaging Channel) | **Canonical for §9** — the second channel: ACS-as-transport, server-side token minting, the inbound ingestor seam, the first-class thread model, membership-as-projection, per-boundary provisioning |
| ADR-024 (Polymorphic Resolver) | Regarding family — the engine extends it, never replaces it; §9.1 threads reuse the same fields/resolver as the thread anchor |
| ADR-034 (User-record membership) | Open-thread membership derivation (§9.3); ACS membership is a reconciled projection of it, never a parallel ACL |
| ADR-028 (Spaarke Auth v2) | Central `TokenCredential` + `IGraphClientFactory`; `OfficeNaaStrategy` client-side; no `new` credentials — governs both Graph and ACS server-side token minting (§9.3) |
| ADR-027 (Provisioning isolation) | Per-boundary ACS resource + Event Grid system topic/subscriptions (ACS data location is immutable at create time) |
| ADR-004 / ADR-036 (Job contract) | Event Grid capture → Service Bus job → DLQ (§9.4) rides the existing job/idempotency/DLQ infrastructure |
| ADR-007 (SpeFileStore) | Message-transcript + attachment archive to SPE (messaging analog of `.eml` archival, §12) |
| ADR-018 (Kill switches) | Per-tenant auto-file kill-switch without redeploy |
| ADR-013 (AI facade) | AI rungs reach AI via `Services/Ai/PublicContracts/` facades, not internal types |
| ADR-037 (DeliverComposite) | Section-name-keyed streaming (relevant to the downstream Triage summary) |
| ADR-015 (Privilege) | AI may flag privilege, never decide it — governs both the association engine's rung 5 and the messaging access filter (§9.1) |

---

## 16. Related

- [ADR-045 — Communication Architecture (concise)](../../.claude/adr/ADR-045-communication-architecture.md) · full: [`docs/adr/ADR-045-communication-architecture.md`](../adr/ADR-045-communication-architecture.md)
- [ADR-046 — ACS Messaging Channel (concise)](../../.claude/adr/ADR-046-acs-messaging-channel.md) · full: [`docs/adr/ADR-046-acs-messaging-channel.md`](../adr/ADR-046-acs-messaging-channel.md) — the second-channel decision (§9)
- [communication-service-architecture.md](communication-service-architecture.md) — send/inbound service mechanics (accounts, subscriptions, dedup, verification, error codes)
- [sprk_communication.md](../data-model/sprk_communication.md) — entity schema (columns + option-set values); does not yet document the messaging-channel columns (`sprk_communicationthread`, `sprk_acsmessageid`, `sprk_acsthreadid`, `sprk_isprivate`, `sprk_privilegeclassification`) or the new `sprk_communicationthread`/`sprk_communicationchannelref` entities — see `projects/messaging-communication-app-r1/notes/messaging-schema-spec.md` for the as-built schema pending a data-model page
- [SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md](SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md) — the narrative-output pattern the downstream Triage summary would use
- `projects/email-communication-solution-r4/notes/W5-responsive-intelligence-and-shared-notification-spine.md` — the re-homed Responsive Intelligence design seed
- `projects/spaarke-notification-spine-r1/design.md` — the shared notification/action spine
- `projects/messaging-communication-app-r1/design.md` §6/§8, `spec.md` FR-17, `notes/access-model-decision.md`, `notes/messaging-schema-spec.md` — the messaging-channel source project

---

*Last Updated: 2026-07-17 — email-communication-solution-r4 W8 (task 080); extended by messaging-communication-app-r1 task 081 (§9).*
