# Email Communication Intelligence — R2 — AI Implementation Specification

> **Status**: Ready for Implementation (pending 2 deployment/runbook inputs + 2 spikes; see Unresolved Questions)
> **Created**: 2026-08-03
> **Source**: `projects/email-communication-intelligence-r2/design.md`
> **Builds on**: `email-communication-intelligence-r1` (shipped, deployed to `spaarke-bff-dev`, merged to master)

## Executive Summary

R2 hardens the **trusted-capture** layer under R1's association/triage engine: every email is matched to the right record **once** (not duplicated across mailboxes or users), filed through **one** intelligent path whether it arrives by mailbox or by hand, and tracked by a **transparent, signed reference footer** that triangulates the email→record mapping without any M365 configuration. It closes the four competitive gaps R1 left open (per-matter file-to address, cross-mailbox/user dedup, in-Outlook suggestions, optional send nudge) and the two structural gaps the code audit surfaced (the capture-vs-upload split; the absence of de-duplication). Every item **extends** the R1 engine, capture path, or Outlook add-in — no re-architecture.

## Scope

### In Scope
**Pillar A — Trusted threading & external matching**
- Signed tracking token delivered as a transparent disclosure footer; new `TrackingTokenRung` (corroborating, not load-bearing).
- Recipient-alias rung for per-record addresses (mail-flow-rule delivery); model Bcc.
- Formalized external-reply self-association (thread ancestry) with regression coverage.
- Deterministic confirmation learning loop (affinity table + rung); ML deferred.

**Pillar B — Unified filing surfaces**
- Realign the existing Outlook add-in to production-current (Entra NAA registration, Word manifest, `authenticatedFetch`, cosmetic).
- A real single "Spaarke" intake folder (shared mailbox+folder **and** add-in drag target) feeding the full engine.
- Drag-to-matter in the add-in with engine-predicted pre-selection; finish the stubbed ribbon quick-save.
- Unify the user-upload ("Save to Spaarke") path with the capture pipeline so both run the engine and both dedup.

**Pillar C — De-duplication (both layers)**
- Message layer: canonical internet-message-id key + Dataverse alternate key (race-proof) across capture + upload; context-merge.
- File/attachment layer: SPE content dedup (`quickXorHash` + `sprk_canonicalhash`, two-tier), absorbing `sdap-file-duplication-detector-r1`; extended to the email-attachment upload path.
- Cross-path reconciliation between `sprk_communication` and archived `sprk_document`.

**Pillar D — R1 carry-overs**
- Fix the inert FR-06 RAG grounding (`ParentEntity` tagging at index time).
- Batched identifier-rung query (NFR-08 cost).
- Golden end-to-end regression tests from the R1 UAT emails.
- Job B allow-list (`sprk_emailupdatefield`) starter seed.
- Job C create-task apply endpoint (in scope — backs Pillar E's E3).

**Pillar E — Surface the intelligence (make it visible + actionable)** *(added 2026-08-04, owner Option A)*
- Triage display (category/priority/summary/RI-confidence/review-outcome — currently dark).
- Proposed-update card (Job B) + proposed-action/deadline card (Job C) consuming the queue-feed + apply endpoints.
- Exceptions Queue (ranked review inbox).
- New-vs-related intent card (renders R1's `NewRecordIntentDetector`).
- Built in shared `Spaarke.Communication.Components`, mounted in r5's `EmailWorkspace`; **supersedes r5's deferred states D/E/F + Exceptions Queue**.

### Out of Scope
- **IP docketing / email→dated-obligations** — recommended as a standalone **R3 flagship**; competitive-validation gate (R1 D-12) still open.
- **SprkChat over mail**, **Daily Briefing 7th channel**, **policy-based auto-apply of the confident band** — R1 P2 deferrals, unchanged.
- **M365 group-mailbox capture** — needs a forked pipeline + tenant-wide `Group.Read.All`; backlog.
- **Visible subject-line token** — deferred, opt-in last resort for hostile transport only.
- **ML ranker for the learning loop** — deferred; R2 ships the deterministic affinity table (its training corpus).
- **Rung-level RecordNameMatch/ContactNameMatch surface-only reclassification** — round-3 partially subsumed it; not revisited here.
- *(Moved INTO scope 2026-08-04: the r5 proposal-surface cards + Exceptions Queue are now Pillar E — R2 owns states D/E/F + Exceptions Queue.)*

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Services/Communication/Engine/Rungs/**` — new `TrackingTokenRung`, `RecipientAliasRung`, `AffinityRung` (additive to the ladder).
- `src/server/api/Sprk.Bff.Api/Services/Communication/Engine/AssociationStatusMapper.cs` + `AutoFileGate.cs` — token/affinity confidence handling (config-governed).
- `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingCommunicationProcessor.cs` + `Models/NormalizedMessage.cs` — internet-message-id dedup on create; add Bcc; footer/token extraction from body incl. quoted history.
- `src/server/api/Sprk.Bff.Api/Services/Communication/GraphSubscriptionManager.cs` + `CommunicationAccountService.cs` — intake-folder/account config.
- `src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs` + `OfficeDocumentPersistence.cs` — unify upload with capture; dedup on internet-message-id; attachment content-hash.
- `src/server/api/Sprk.Bff.Api/Services/CommunicationService.cs` (send path) — inject the disclosure footer + signed token on outbound.
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs` (+ new dedup/detector service) — SPE content dedup hooks (Assistant upload/persist, Compose save, **email-attachment**).
- `src/client/office-addins/**` — add-in realignment (B0), drag-to-matter + engine suggestions (B2), compose-time footer/token injection.
- **Dataverse**: `sprk_document.sprk_canonicalhash` (new indexed column), alternate key on `sprk_communication.sprk_internetmessageid`, affinity store, Job B allow-list seed.

## Requirements

### Functional Requirements

**Pillar A**
1. **FR-A1 — Signed tracking footer + `TrackingTokenRung`.** Outbound email regarding a record (Spaarke send path + add-in at compose) carries a transparent, human-readable footer — generic wording *"This message is tracked for document management. Ref: {record-ref} · {signed-token}"* — with an HMAC-signed opaque token (`recordType|recordId|tenantId|issued`). A new `TrackingTokenRung` reads it from the captured body **including quoted reply/forward history**. **Acceptance**: a present-and-signature-valid token yields a high-confidence, auto-file-eligible match; a bare/edited reference yields a medium corroborating match the ladder cross-checks; a deleted footer degrades to the other rungs with no error; a forged/tampered token fails signature validation and is ignored; a forwarded prior token that conflicts with other signals resolves to Ambiguous (never silent misfile). **Enablement + footer wording are a customer-editable Dataverse configuration** (on/off + message template, ADR-018-style, no redeploy); when disabled, no footer is injected and matching relies on the rest of the ladder.
2. **FR-A2 — `RecipientAliasRung` + Bcc.** Parse To/Cc/**Bcc** for a per-record address (`matter-12345@`) → `ExplicitReference`-tier match, delivered via a targeted Exchange mail-flow rule (not tenant-wide plus-addressing). **Acceptance**: an email addressed (incl. Bcc) to a configured per-record alias associates to that record deterministically; `NormalizedMessage` carries Bcc; no plus-addressing tenant setting is required.
3. **FR-A3 — External-reply self-association (formalize).** An external reply to any email Spaarke sent self-associates via `ThreadContinuityRung` on `In-Reply-To`/`References`. **Acceptance**: a regression test proves an external reply (custom headers stripped) still inherits the parent's regarding; documented as a first-class guarantee.
4. **FR-A4 — Deterministic learning loop.** Human confirmations record a per-tenant affinity (sender→record, sender-domain→record, subject-keyword→record, participant-set→record); a new `AffinityRung` surfaces the highest-affinity record as a candidate with an explainable reason. **Acceptance**: after N confirmed associations from a sender to a record, a subsequent untagged email from that sender surfaces that record as a Suggested candidate citing the confirmation count; the affinity store is per-tenant and inspectable; no ML/training infra is introduced.

**Pillar B**
5. **FR-B0 — Outlook add-in realignment.** Bring the (already ADR-028-migrated) add-in production-current. **Acceptance**: the add-in's Entra NAA app registration is verified/provisioned and sign-in succeeds against the BFF at runtime; the Word manifest is migrated to the parameterized unified form (no hardcoded SWA origin); JSON BFF calls route through `authenticatedFetch` (401-retry), with SSE/XHR keeping their documented D-AUTH-7 exceptions; dead scope args + stale version/build strings cleaned.
6. **FR-B1 — Real Spaarke intake folder (both mechanisms).** A shared "Spaarke" mailbox+folder **and** the add-in drag target push a dropped email into the **full** intelligence pipeline (association + triage + provenance). **Acceptance**: an email dragged into the intake folder (or the add-in drag target) produces a `sprk_communication` with association + triage + provenance identical to a mailbox-captured email; it is deduped like any capture.
7. **FR-B2 — Drag-to-matter + engine suggestions in the add-in.** The add-in surfaces the engine's top candidates (reuse `derivePrimaryReview`) pre-selected in the picker, and the stubbed ribbon quick-save is completed. **Acceptance**: opening "Save to Spaarke" shows the engine's predicted record pre-selected with alternates; one-click ribbon quick-save files to the predicted record; the candidate model matches the code-page review surface.
8. **FR-B3 — Unify user-upload with capture.** The "Save to Spaarke" path runs the same engine as capture and shares its dedup. **Acceptance**: a user-saved email is associated + triaged by the engine (not merely archived as a `sprk_document`), and is deduped against capture and other users via internet-message-id (see FR-C1).

**Pillar C**
9. **FR-C1 — Canonical internet-message-id dedup (structural).** Make `sprk_internetmessageid` the dedup key for `sprk_communication`, enforced by a Dataverse alternate (unique) key, across capture **and** user-upload; Service Bus idempotency keyed on message-id. **Acceptance**: the same email delivered to N monitored mailboxes and saved by M users yields exactly **one** `sprk_communication`; concurrent inserts of the same message id race-fail gracefully to a single row.
10. **FR-C2 — Context-merge on duplicate.** A detected duplicate records its delivery/recipient/uploader context on the single canonical row rather than being dropped. **Acceptance**: the canonical `sprk_communication` reflects all mailboxes/users that received/saved the message; no delivery fact is lost.
11. **FR-C3 — SPE content dedup (absorb `sdap-file-duplication-detector-r1`).** Indexed `sprk_document.sprk_canonicalhash` (`quickXorHash`) hooked at all upload paths **including the email-attachment path**; on a hit, notify (never silent), open-canonical / proceed-hash-linked; copy graduates to its own document on content divergence. **R2 core ships message-level dedup + Tier 1 (exact hash-equality); Tier 2 (near-dup, reusing `documentVector3072` "Find Similar" at a duplicate-tuned threshold) is a validated fast-follow** (owner Q2) gated on spike 2. **Acceptance**: uploading byte-identical content twice (any path) is detected at/before persist and does not create a second canonical document (Tier 1); spike 1 (quickXorHash post-upload timing/size) resolved before Tier-1 build; Tier 2 ships only after spike 2 validates the near-dup threshold + `documentVector3072` coverage (else deferred).
12. **FR-C4 — Cross-path reconciliation.** A captured `sprk_communication` and a user-saved `sprk_document` archive of the same email reconcile via internet-message-id. **Acceptance**: the two representations are linked (not duplicated) and the review surface shows one email, not two.

**Pillar D**
13. **FR-D1 — Fix FR-06 RAG grounding.** Tag communications with their regarding at RAG-index time (inbound + outbound), replacing the `ParentEntity: null` call sites; decide a backfill for already-indexed correspondence. **Acceptance**: a matter-scoped RAG query returns that matter's indexed correspondence (currently zero); backfill decision recorded.
14. **FR-D2 — Batched identifier query.** Replace `IdentifierReverseLookupRung`'s per-token/per-type queries with a batched `In`-filter query. **Acceptance**: per-message identifier-rung Dataverse calls drop from ≈175 to ≤7 with identical match results.
15. **FR-D3 — Golden regression suite.** Pin the R1 UAT misfile emails (PAT-942665 / PAT-942404 / REAL-2026-123456.02 + `Invoice-10044725.pdf`) as end-to-end regression tests. **Acceptance**: the suite reproduces and guards the round-1/2/2b/3 outcomes (core-only auto-association; no contact/invoice auto-file; Ambiguous on conflicting matters).
16. **FR-D4 — Job B allow-list seed.** Seed `sprk_emailupdatefield` with a starter allow-list. **Acceptance**: Job B can propose at least the seeded field updates in `spaarkedev1`.
17. **FR-D5 — Job C apply endpoint (in scope; backs FR-E3).** A confirm/apply path for `kind:"create-task"` proposals. **Acceptance**: a confirmed create-task proposal calls `IActionSeam.CreateTaskAsync` under audit (cited), exposed as an endpoint the proposed-action card (FR-E3) POSTs on Approve.

**Pillar E — Surface the intelligence** *(added 2026-08-04, owner Option A — supersedes r5's deferred states D/E/F + Exceptions Queue)*
18. **FR-E1 — Triage display.** Render `sprk_communication` triage fields (category / priority / summary / RI-confidence / review-outcome) on the email surface, in `Spaarke.Communication.Components` mounted in r5's `EmailWorkspace`. **Acceptance**: a triaged email shows its category/priority/summary/RI-confidence in the reading pane (currently rendered nowhere).
19. **FR-E2 — Proposed-update card (Job B, r5 state E).** A card consuming `GET /api/communications/queue-feed` renders each proposed field-update (old→new, citation, confidence); Approve → `POST /api/communications/proposals/{reviewLogId}/apply`. **Acceptance**: a user reviews a Job B proposal and, on Approve, the field is written under audit via the existing apply endpoint; Dismiss discards it.
20. **FR-E3 — Proposed-action / deadline card (Job C, r5 state F).** A card renders `kind:"create-task"` proposals; Approve → the FR-D5 apply endpoint. **Acceptance**: a user confirms a deadline/task proposal and the task is created under audit; nothing deadline-bearing auto-finalizes without confirmation (ADR-015).
21. **FR-E4 — Exceptions Queue.** A ranked review surface (dual-use: SpaarkeAi widget + code-page) consuming the queue-feed — the cross-cutting inbox of associations + Job B/C proposals needing a human. **Acceptance**: the queue lists ranked items, routes each to its confirm surface, and reflects resolution.
22. **FR-E5 — New-vs-related intent card (r5 state D).** Render R1's `NewRecordIntentDetector` output as a 3-way choice (Create-new-and-link / File-onto-X / Link-as-related). **Acceptance**: an email flagged with new-record intent shows the 3-way card; the chosen action drives the association write.
23. **FR-E6 — r5 coordination.** Update the r1↔r5 coordination contract to record R2 ownership of states D/E/F + Exceptions Queue; `/conflict-check` before every shared-lib PR. **Acceptance**: the coordination note reflects R2 ownership; no duplicate r5 build of the same cards.

### Non-Functional Requirements
- **NFR-01 — Zero-config primary tracking.** The primary token channel (body footer) requires **no** M365 configuration and works user-to-user. Mail-flow rules are needed only for per-record intake addresses (FR-A2).
- **NFR-02 — Race-proof dedup.** Message-level dedup is enforced structurally (Dataverse alternate key), not by app-level check-then-insert alone.
- **NFR-03 — Token is corroborating, never load-bearing.** Deletion/tampering of the footer degrades gracefully; the ladder never depends on the token alone.
- **NFR-04 — Best-effort / non-fatal.** Token stamping, dedup, learning, and SPE hashing MUST NOT fail the capture or send path (ADR-045 NFR-06 inheritance).
- **NFR-05 — AI facade discipline.** No `IOpenAiClient`/`IPlaybookService` injected into Communication code; AI reached only via `Services/Ai/PublicContracts/` (ADR-013).
- **NFR-06 — Publish size.** Report absolute + delta per BFF-touching task; ceiling ≤60 MB compressed (baseline ~47 MB); no new HIGH CVE.
- **NFR-07 — Token security.** HMAC signing key in Key Vault (ADR-028); signature verified before a token is trusted; footer is transparent (no hidden content).
- **NFR-08 — Spike-gated SPE dedup.** The two `sdap-r1` spikes (quickXorHash post-upload timing/size; Tier-2 near-dup threshold + `documentVector3072` coverage) MUST complete before building Tier-1/Tier-2.
- **NFR-09 — Extend never fork.** New rungs are additive to the single Association Engine (ADR-045/024); no parallel mechanism; regarding writes stay via `RegardingFieldMap`.

## Technical Constraints

### Applicable ADRs
- **ADR-045** — communication architecture: extend, never fork (add rungs, one engine).
- **ADR-024** — polymorphic regarding; resolver-only write (rungs write typed `sprk_regarding*`).
- **ADR-039** — grounded execution / closed catalogs / code-directed Action+Binding; node engine frozen.
- **ADR-013** — BFF AI facade (`PublicContracts/` only).
- **ADR-028** — Spaarke Auth v2: HMAC secret + `DefaultAzureCredential`/Key Vault; add-in NAA via `@spaarke/auth`; mail app-only.
- **ADR-010 / ADR-032** — DI minimalism; unconditional registration + Null-Object/kill-switch for feature-gated services.
- **ADR-018** — per-tenant kill-switch/config (token, affinity, core-set all config-governed, no redeploy).
- **ADR-007** — `SpeFileStore` facade (SPE dedup hooks route through it).
- **ADR-027** — subscription isolation / Dataverse solution management (new column, alternate key, seed into the managed solution).
- **ADR-038** — testing strategy (seam tests DoD; golden regression under a KEEP path).
- **ADR-004 / ADR-036** — job contract (capture + dedup jobs).

### MUST Rules
- ✅ MUST add new capability as additive rungs on the existing ladder; MUST NOT fork the engine or revive the frozen node-graph engine.
- ✅ MUST write regarding only via `RegardingFieldMap` typed lookups (ADR-024).
- ✅ MUST reach AI only via `Services/Ai/PublicContracts/`; MUST NOT inject AI-internal types into Communication.
- ✅ MUST verify the HMAC token signature before trusting it; MUST keep the footer transparent (no hidden/obfuscated content).
- ✅ MUST enforce message-level dedup with a Dataverse alternate key (structural), not only an app-level pre-check.
- ✅ MUST keep token stamping / dedup / learning / hashing non-fatal to capture + send.
- ✅ MUST register new rungs/services unconditionally; feature-gate via config (ADR-010/032/018).
- ✅ MUST run the two SPE spikes before building SPE Tier-1/Tier-2.

### Existing Patterns
- Rung pattern: `Services/Communication/Engine/Rungs/ExplicitReferenceRung.cs`, `ThreadContinuityRung.cs`, `IAssociationRung.cs`.
- Ladder + config: `AssociationStatusMapper.cs`, `AutoFileGate.cs`, `Configuration/AutoFileOptions.cs` (R2 core-set/kill-switch precedent).
- Capture: `GraphSubscriptionManager.cs`, `IncomingCommunicationProcessor.cs` (existing 4-layer dedup to extend).
- SPE: `Infrastructure/Graph/SpeFileStore.cs`; `Services/Office/OfficeDocumentPersistence.cs`.
- Add-in: `src/client/office-addins/**` + `@spaarke/auth` (`OfficeNaaStrategy`); `derivePrimaryReview` (candidate model reuse).
- SPE dedup design: `projects/sdap-file-duplication-detector-r1/{README,analysis}.md`; `.claude/agent-memory/researcher/spe-dedup-content-identity-2026-07.md`.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
**Placement Justification (BFF=Y, per `.claude/constraints/bff-extensions.md`)**: all new rungs, the dedup enforcement, the SPE content-hash detector, and the footer/token signing extend the **existing** `Services/Communication/**`, `Services/Office/**`, and `Infrastructure/Graph/SpeFileStore.cs` — the single backend for the capture/upload paths they govern. No new microservice; no CRUD→AI direct dependency (AI reached via `PublicContracts/`). Publish-size ≤60 MB verified per task. *(Note: the Outlook add-in lives in `src/client/office-addins/**` — not a formal hot-path watchlist surface, but its BFF Office endpoints are; `/conflict-check` before each BFF PR.)*
**SpaarkeAi=Y (added 2026-08-04, Pillar E)**: the Exceptions Queue + proposal cards are dual-use surfaces built in the shared `Spaarke.Communication.Components` library and mounted in r5's `EmailWorkspace` (a SpaarkeAi-hosted widget). **This is shared code with the active `email-communication-solution-r5` worktree — `/conflict-check` before EVERY shared-lib PR; `parallel-safe:false` on shared-lib writers.** R2 owns states D/E/F + Exceptions Queue (r5 deferred them); coordination contract updated per FR-E6.

### New Components (§11 three-question gate)
| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `TrackingTokenRung` | No token rung exists (`Engine/Rungs/*`) | No — no rung reads a signed footer | Threads we originate can't be recognized on return with high trust; every reply re-runs prediction from scratch |
| `RecipientAliasRung` | `ParticipantCorrelationRung` resolves recipients→contact, not per-record aliases | Partial — could extend ParticipantCorrelation; new rung keeps alias parsing isolated + testable | External systems (client billing, e-filing) can't file to a matter via a `matter-*@` address |
| `AffinityRung` + affinity store | No affinity/history store exists | No | Untagged cold inbound never gets easier; repeated manual filing of the same sender/pattern |
| `sprk_document.sprk_canonicalhash` (indexed) | No hash column on `sprk_document` (`Spaarke.Dataverse/Models.cs`) | No | Byte-identical files duplicate silently in SPE; no cross-container dedup authority |
| Alternate key on `sprk_communication.sprk_internetmessageid` | Only per-mailbox `sprk_graphmessageid` check | No | Same email in N mailboxes / M users → N/M duplicate rows (race-prone) |
| SPE content-dedup detector service | No dedup service; `sdap-r1` is code-free | No — absorb `sdap-r1` design | No file dedup anywhere in the upload pipeline |
| Footer/token HMAC signing helper | No token signer in Communication | Reuse platform crypto/Key Vault if present; else new | Token can't be made tamper-evident |
| Intake-folder account/config | `GraphSubscriptionManager` monitors per-account single folder | Extend (config + a shared intake account) | Email not sent to a monitored mailbox never enters the engine |
| Tracking-footer config (enable + message) | No communication-footer settings surface | Extend existing settings if one exists; else small config | Firm can't control whether tracking is on or what the footer says |
| Pillar E surfaces (proposed-update/action cards, Exceptions Queue, triage display, intent card) | r5 shell + association review exist; **states D/E/F + Exceptions Queue do NOT (r5 deferred)** | No — build in shared `Spaarke.Communication.Components`, mount in r5 shell (no fork) | Job B/C extraction + triage are stored-but-invisible; the whole intelligence layer is unusable |

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-028 | App-only/OBO auth model; secrets via MI/Key Vault | The tracking token needs an HMAC signing secret + rotation, and outbound footer injection touches the send path | **A** (project-scoped exception) | Standard Key Vault secret + rotation; document in design; no new auth *flow*, only a signing secret |
| ADR-024 | "One regarding mechanism" | New rungs (token/alias/affinity) — but they write the **same** typed `sprk_regarding*` fields via `RegardingFieldMap` | **C** (comply) | Additive rungs, not a second mechanism; no tension in practice |
| §10 BFF hygiene | Guard new BFF surface | Adds rungs + dedup + SPE detector to the BFF | **C** (comply) | Extends existing services in place; Placement Justification above; publish-size + CVE per task |
| ADR-040 | Session ledger owns "disposition" | The affinity store is new state | **A** (exception) | Affinity is filing-history metadata, not session disposition; keep it separate + documented |
| ADR-007 | SPE access via `SpeFileStore` facade | Dedup hooks read `quickXorHash`/hash before upload | **C** (comply) | Route hash read/dedup through the facade; no direct Graph in callers |

> Additional non-ADR policy note: injecting a disclosure footer into outbound (potentially privileged) mail is a firm-policy sensitivity; mitigated by making it **transparent + opt-out per firm** (not hidden), which also avoids DLP/anti-spam false positives.

## Success Criteria
1. [ ] Same email across N mailboxes + M users → exactly one `sprk_communication` (race-proof). Verify: integration test + alternate-key enforcement.
2. [ ] ~100% recognition of threads Spaarke originates (token + thread). Verify: seam test on tagged send → external reply → capture.
3. [ ] Drag-to-file and mailbox-capture produce identical engine output for the same email. Verify: parity test (association + triage + provenance).
4. [ ] Byte-identical file upload (any path incl. email-attachment) does not create a second canonical document. Verify: dedup seam test (post-spike).
5. [ ] Matter-scoped RAG query returns that matter's correspondence (currently zero). Verify: FR-D1 grounding test.
6. [ ] Baseline + trend for the **deterministic-resolution rate (T0/P0)** metric. Verify: telemetry/report.
7. [ ] Add-in signs in and files against the BFF at runtime. Verify: manual runtime UAT after Entra registration.
8. [ ] Golden R1 UAT regression suite green. Verify: CI.

## Dependencies

### Prerequisites
- **Two SPE spikes** (NFR-08) resolved before Pillar C Tier-1/Tier-2 build.
- **Entra NAA app registration** for the add-in provisioned/verified (FR-B0).
- **Mail topology decision per client**: a mail-flow rule where per-record intake addresses (FR-A2) are wanted — surgical, not tenant-wide.
- R1 formally closed (reconcile task 013, run 090 `/test-diet`, pin golden regression) — recommended before R2 start.

### External Dependencies
- **`sdap-file-duplication-detector-r1`** — absorbed; close/fold once R2 spec incorporates it.
- **"augment global Document search"** project — populates `sprk_globalsearchextender` (lights up F1 content matching; independent).
- **r5** (`email-communication-solution-r5`) — owns the `EmailWorkspace` shell + association review; **R2 supersedes its deferred states D/E/F + Exceptions Queue (Pillar E)**, built in the shared `Spaarke.Communication.Components` lib. **Active worktree — `/conflict-check` before every shared-lib PR; update the coordination contract (FR-E6).**
- **Exchange admin** (per enterprise client) — mail-flow rule for FR-A2 addresses (opt-in, only where a client-system integration exists).

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Token delivery | Plus-addressing vs mail-flow rule? | **Body footer is primary** (zero M365 config); mail-flow rules only for per-record addresses; plus-addressing **not** required | FR-A1 primary channel = body; FR-A2 uses mail-flow rules |
| Token form | Hidden vs explicit? | **Explicit, transparent, generic wording**: "This message is tracked for document management. Ref: …" | Removes DLP/hidden-content risk; normal footer |
| Token trust | Load-bearing? | **No — triangulation only**; tampering "not substantive or material" | `TrackingTokenRung` corroborates; signed-valid=high, bare=medium, deleted=graceful |
| User-to-user mail | How matched if no Spaarke send? | Tag at any Spaarke-aware surface (send/add-in compose); untouched → ladder + learning (DMS parity) | FR-A5 coverage model |
| Intake mechanism | Folder, add-in, or both? | **Both** | FR-B1 builds both |
| SPE dedup | Absorb `sdap-r1`? Cover email too? | **Absorb; cover both** message and file/attachment layers | Pillar C two-layer scope |
| Learning loop | Ship ML? | **Deterministic affinity now; defer ML** (deterministic table = future training corpus) | FR-A4 deterministic only |
| Outlook add-in | Rebuild? | **Realign + verify** (already ADR-028-migrated 2026-07-14); likely-broken part is Entra registration | FR-B0 scope |
| Dedup merge policy | Merge vs link? | **Hard-merge byte-identical; link (not merge) near-dups** | FR-C1/C2; near-dup linking |
| Visible subject token | Build now? | **Defer** (opt-in last resort for hostile transport) | Out of scope |
| **Per-record intake address (FR-A2)** (2026-08-04) | Build now or defer? | **Build now** — mail-flow-rule delivery, opt-in per client | FR-A2 in scope |
| **File dedup tiers (FR-C3)** (2026-08-04) | How aggressive? | **Message dedup + Tier-1 exact now; Tier-2 near-dup fast-follow** (spike-gated) | FR-C3 Tier-1 core, Tier-2 deferred |
| **Job C apply / Q3 surfacing** (2026-08-04) | Endpoint only, or build the surfaces? | **Option A — R2 builds the review surfaces (Pillar E)**; supersedes r5 states D/E/F + Exceptions Queue | New Pillar E (FR-E1–E6); FR-D5 in scope |
| **Footer config (Q4)** (2026-08-04) | Customer-controllable? | **Yes — Dataverse config**: on/off + message template | FR-A1 config point |
| **DLP check (Q4-adjacent)** (2026-08-04) | Design blocker? | **No — deployment-checklist validation + opt-out** | Execution-phase |

## Assumptions
- **Exchange mail-flow rule** for per-record intake addresses can be provisioned per enterprise client (FR-A2). If not, FR-A2 degrades and the body footer + ladder carry the load.
- **`quickXorHash`** is present and stable enough post-upload to gate dedup (pending SPE spike 1).
- **A shared "Spaarke" intake mailbox** can be provisioned and monitored (FR-B1).
- **`documentVector3072` coverage** is sufficient for Tier-2 near-dup (pending SPE spike 2); otherwise Tier-2 ships behind a scoped enhancement.

## Unresolved Questions
*(All owner scope decisions resolved 2026-08-04. The following are execution-phase spikes/checks, not open scope questions.)*
- [ ] **SPE spike 1 — quickXorHash post-upload timing/size.** Determines FR-C3 gate-before-vs-after-write. Phase-0 task within the project.
- [ ] **SPE spike 2 — Tier-2 near-dup threshold + `documentVector3072` coverage.** Gates whether Tier-2 ships in R2 or defers. Phase-0/1 task.
- [ ] **Mail-flow-rule feasibility per enterprise client (FR-A2).** Deployment/runbook per client; the body-footer primary path is unaffected.
- [ ] **Body-footer DLP/deliverability check.** Deployment-checklist validation (low risk; transparent footer + Dataverse opt-out).

---
*AI-optimized specification. Original design: `design.md`.*
