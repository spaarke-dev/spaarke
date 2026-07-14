# ADR-045: Communication Architecture — Canonical Send + Association Engine + Enrichment + Channel Seams

> **Status**: Accepted
> **Date**: 2026-07-14
> **Domain**: Communication (email today; channel-extensible) — client send UX + server intelligence layer
> **Source project**: `email-communication-solution-r4` (unified project; absorbs the designed-but-never-executed `x-email-communication-solution-r3` send-side scope)
> **Supersedes**: the never-written R3 "ADR-033" plan. ADR-033 is an unrelated, already-accepted ADR (Streaming chat-tool side channel); R4 took the next free number (045) and broadened the scope from R3's client-only send consolidation to the full server intelligence layer + channel seams.

---

## Context

### Where Spaarke's communication backbone stood before R4

- **Inbound (live, R2)**: 100% Microsoft Graph change-notification subscriptions (`GraphSubscriptionManager`) → HMAC webhook (202 → Service Bus) → `IncomingCommunicationProcessor` → `IncomingAssociationResolver` (3 deterministic rungs, inbound-only, over a `Microsoft.Graph.Message`) → `sprk_communication` + `.eml`-to-SPE + RAG indexing. **No Server-Side Sync anywhere; no dependency on OOB `email` activities on this path.**
- **Outbound (live, R2 server + R3 client design)**: `CommunicationService.SendAsync` (app-only) / `SendAsUserAsync` (OBO). Outbound got **caller-supplied regarding only** — no auto-association, no categorization, and **no RAG indexing**.
- **Client send UX (fragmented)**: six ad-hoc send implementations, LegalWorkspace forks, and a ~1,150-LOC × 2 `sprk_communication_send.js` webresource. R3 designed a canonical `<EmailComposer />` engine to consolidate these but was never executed (zero code landed).
- **A legacy subsystem bound to OOB `email` activities** (`Services/Email/`, `EmailAssociationService`, `EmailToEmlConverter`, a `PrimaryEntityName=="email"` webhook, `/api/v1/emails/*`) was dead in production because Spaarke produces no `email` activities — but still present, and carrying an ADR-028 auth-drift point (self-built `ConfidentialClientApplication`).

### The forces this ADR resolves

1. **Fragmentation** — six send surfaces are a correctness and maintenance liability (e.g., `DocumentEmailWizard` shipping `sprk_document` GUIDs where Graph `driveItem` IDs are required).
2. **Asymmetry** — inbound and outbound diverge on association + RAG, so "sent email" is a second-class citizen of the intelligence layer.
3. **Channel lock-in** — the data model is already multi-type (`sprk_communicationtype` = Email/TeamsMessage/SMS/Notification) but the service layer is email-hardcoded, so any future channel would force an engine rewrite.
4. **Legacy debt** — the OOB-`email` subsystem must be retired, and its confidence-scoring *design* preserved and reimplemented against Graph/`sprk_communication`.

---

## Decision

Communication — send and receive, email today, any channel later — is governed by four coupled rules.

### 1. Client canonical send
One `<EmailComposer />` engine in `@spaarke/ui-components` (modes `compose|view|reply|forward|draft`; mounts `inline|dialog|page`; React 18; Fluent v9; injected `authenticatedFetch`), exposed through three thin semantic wrappers — `SendEmailStep` (inline), `SendEmailDialog` (dialog), `SendEmailPage` (Code Page). All programmatic (no-UI) send flows through the `sendCommunication()` typed wrapper, which parses ProblemDetails into a single `SendCommunicationError` and uses the canonical `attachmentDriveItemIds` field. No ad-hoc/inline `fetch` to the send endpoint; no per-caller composer forks.

### 2. Server Association Engine over a normalized envelope
A single engine (generalized from `IncomingAssociationResolver`) matches each communication — inbound **and** outbound — to related records, operating on a **normalized message envelope** `{ direction, from, to[], cc[], subject, bodyText, bodyHtml, internetMessageId, inReplyTo, references[], conversationId, sentAt, attachments[] }`, **never** a `Microsoft.Graph.Message`. It resolves eight targets (matter, project, invoice, `sprk_servicerequest`, work assignment, event, contact, `sprk_organization`) via a deterministic-first rung ladder — rung 0 explicit-ref/caller-supplied, rung 1 thread continuity, rung 2 participant correlation, rung 3 structural detectors — then rung 4 semantic (`RecordSearchService`) and rung 5 AI (JPS action → `AppOnlyAnalysisService`). Every match records per-attribute confidence + provenance.

### 3. Direction-agnostic enrichment
One `ICommunicationEnrichmentService` — signature `(communicationId, direction, NormalizedMessage, archivedDocumentId?)` — is invoked by BOTH `IncomingCommunicationProcessor` (inbound) and the `CommunicationService` outbound creators. It owns, in order: association (the engine) → categorization (content class + urgency) → AI analysis → RAG indexing (adding the previously-missing outbound half) → Responsive-Intelligence trigger. Sent mail gets the same treatment as received mail by construction.

### 4. Channel seams (email-only implementation)
`ICommunicationChannelSender` (dispatch `SendAsync` by `sprk_communicationtype`; email = Graph impl) and `ICommunicationArchiver` (`.eml`/`GenerateEml` = one impl) are **defined but not implemented beyond email**. Adding a channel later = new sender/archiver/ingestor adapters + a normalizer to the envelope, with **no change** to the engine, the enrichment service, the regarding model, or the review UI.

### Confidence → status ladder

| Confidence / source | `sprk_associationstatus` | Behavior |
|---|---|---|
| ≥0.85 deterministic (rungs 0–3) | `Resolved` | **Auto-file** (per-tenant ADR-018 kill-switch; default-on) |
| 0.50–0.85, OR any AI rung (4–5) | `Suggested` | 1-click confirm in review UI |
| <0.50 / none | `Pending Review` | Manual |
| Conflicting high-confidence | `Ambiguous` | Disambiguate |

---

## Alternatives Considered

1. **Two ADRs — a client-only send ADR (R3's "ADR-033" plan) + a separate server-engine ADR.** Rejected: R4 absorbs R3 into one project; the client send contract, the server engine, and the channel seams are one coupled decision (the composer's `AssociationChips` render engine output; the enrichment service triggers on both send and receive). Splitting would create a cross-ADR seam with no benefit. One ADR, fresh number.
2. **Per-channel ADRs (an email ADR now, a Teams ADR later).** Rejected: the whole point of the normalized envelope + seams is that channels are additive *adapters*, not architectural decisions. A per-channel ADR would invite per-channel engine divergence — the exact lock-in this ADR removes.
3. **Keep inbound/outbound enrichment separate (patch outbound to add association + RAG in place).** Rejected: duplicates the ladder + RAG logic across two call sites and re-opens the asymmetry every time a rung changes. A single direction-agnostic service makes symmetry true by construction.
4. **Engine over `Microsoft.Graph.Message` (extend the existing resolver in place).** Rejected: bakes email lock-in into the engine and defeats channel extensibility. The normalized envelope is the single most important extensibility decision in R4.
5. **Suggest-only at launch (no auto-file), per design DEC-4.** Not taken — see the ADR Tension below.

---

## ADR Tension — Auto-file at launch (Path A, per CLAUDE.md §6.5)

- **Rule challenged**: the R4 design (DEC-4) recommended "suggest-only first; enable auto-file per deterministic rung only after measuring on real volume."
- **Conflict**: the product owner directed **auto-file ON at launch** for deterministic rungs 0–3 at ≥0.85, to avoid a manual-confirm bottleneck on high-confidence deterministic matches from day one.
- **Path chosen**: **A (project-scoped exception)**. This is not an ADR violation — the design's own §5.4 ladder already sanctions ≥0.85 → `Resolved`. The deviation is from the *conservative default*, and it stays inside three guardrails: (a) auto-file is limited to **deterministic** rungs (AI rungs never auto-file), (b) it is gated behind the per-tenant **ADR-018 kill-switch** that flips to suggest-only without redeploy, and (c) misfile is **re-file** (audited), never delete.
- **Recorded**: here + in `spec.md` ADR Tensions + `projects/email-communication-solution-r4/CLAUDE.md`. Reviewers accept this as a cited exception at code-review time (does not re-flag as Critical).

---

## Consequences

**Positive**
- Outbound mail gains auto-association + RAG indexing with no extra call-site logic.
- One send surface eliminates six divergent implementations, LegalWorkspace forks, and the ~2.3K-LOC `sprk_communication_send.js` webresource; the `attachmentDriveItemIds` latent bug is closed.
- The normalized envelope + seams make Teams/Slack/Gmail/SMS additive (adapters), not architectural.
- Provenance + the ADR-018 kill-switch make auto-file safe and reversible.
- Retiring `Services/Email/` removes an ADR-028 auth-drift point and reduces BFF publish size.

**Negative / risk**
- Auto-file-ON carries a misassociation risk (R-1), mitigated by the deterministic-only threshold + kill-switch + audited re-file.
- Generalizing the resolver risks regressing live inbound matching (R-7), mitigated by characterization tests on rungs 0–2 before extension.
- The Responsive-Intelligence leg edits `Services/Ai/` internals owned by `spaarke-ai-architecture-redesign-r2`; it MUST consume that project's `Services/Ai/PublicContracts/` seams and coordinate via `/conflict-check` (no internal fork). This is an execution-time coordination cost, not an architectural one.

**Neutral**
- Channel seams add two interfaces with a single (email) implementation each — deliberate extension points, not speculative code.

---

## Related ADRs

ADR-024 (regarding family — extend, never replace) · ADR-028 (Auth v2 — central credential/factory) · ADR-018 (kill switches — auto-file gate) · ADR-032 (Null-Object — feature-gated communication services) · ADR-013 (AI facade / PublicContracts) · ADR-016 + ADR-014 (AI budget + cache) · ADR-015 (privilege flag-only) · ADR-037 (DeliverComposite streaming) · ADR-006/026/021/022/012 (Code Page + Fluent v9 + shared-lib) · ADR-029 (publish hygiene) · ADR-038 (testing strategy).

---

## Revision Log

| Date | Change |
|---|---|
| 2026-07-14 | Accepted. Authored by `email-communication-solution-r4` task 005 (FR-05). Supersedes the never-written R3 "ADR-033" plan; broadens scope to the full server intelligence layer + channel seams. Records the auto-file ADR Tension (Path A). |
