# ADR-045: Communication Architecture — Canonical Send + Association Engine + Enrichment + Channel Seams (Concise)

> **Status**: Accepted
> **Domain**: Communication (email today; channel-extensible) — client send UX + server intelligence
> **Last Updated**: 2026-07-14
> **Source project**: `email-communication-solution-r4` (unified; absorbs the never-executed R3 send-side scope)
> **Cross-references**: extends ADR-024 (regarding family), ADR-028 (Auth v2), ADR-018 (kill switches), ADR-032 (Null-Object), ADR-013 (AI facade); relates to ADR-037/016/014/015 (AI), ADR-006/026/021/022/012 (Code Page + Fluent v9), ADR-029 (publish hygiene), ADR-038 (testing).
> **Supersedes**: the never-written R3 "ADR-033" plan (client-only send consolidation). ADR-033 is an unrelated accepted ADR — this decision took the next free number (045) and broadened scope to the full server intelligence layer + channel seams.

---

## Context

Spaarke's email backbone is 100% Microsoft Graph + `sprk_communication` (no Server-Side Sync, no OOB `email` activities on the go-forward path). R2 delivered the server foundation (Graph change-notification subscriptions → `IncomingCommunicationProcessor` → `IncomingAssociationResolver` → `.eml`-to-SPE + RAG). Two structural gaps remained: (1) the send-side client surface was fragmented across 6 ad-hoc implementations, and (2) inbound and outbound enrichment were asymmetric (outbound got no auto-association and no RAG indexing). R4 unifies both under one architectural decision, retires the legacy OOB-`email` subsystem (`Services/Email/`), and designs channel seams so Teams/Slack/Gmail/SMS can slot in later without re-opening the engine.

---

## Decision

Communication (send and receive, email today, any channel later) is governed by four coupled rules:

1. **Client canonical send.** All email-send UX flows through ONE `<EmailComposer />` engine in `@spaarke/ui-components` (modes `compose|view|reply|forward|draft`; mounts `inline|dialog|page`) exposed via three thin semantic wrappers (`SendEmailStep`/`SendEmailDialog`/`SendEmailPage`). All programmatic (no-UI) send flows through the `sendCommunication()` typed wrapper. No ad-hoc/inline `fetch` to the send endpoint; no per-caller composer forks.

2. **Server Association Engine over a normalized envelope.** Matching each communication to related records is done by a single engine operating on a **normalized message envelope** — never `Microsoft.Graph.Message`. It resolves eight targets (matter, project, invoice, service request, work assignment, event, contact, organization) via a deterministic-first rung ladder (0 explicit-ref → 1 thread → 2 participant → 3 structural detectors), then semantic (4) and AI (5). Every match records per-attribute confidence + provenance.

3. **Direction-agnostic enrichment.** One `ICommunicationEnrichmentService` is invoked by BOTH inbound (`IncomingCommunicationProcessor`) and outbound (`CommunicationService`) paths, owning in order: association → categorization → AI analysis → RAG indexing → Responsive-Intelligence trigger. Sent mail gets the same treatment as received mail by construction.

4. **Channel seams (email-only impl).** `ICommunicationChannelSender` (dispatch by `sprk_communicationtype`) and `ICommunicationArchiver` (`.eml`) are defined so a new channel = new sender/archiver/ingestor adapters + a normalizer to the envelope, with **no change** to the engine, enrichment service, regarding model, or review UI.

---

## Constraints

### ✅ MUST

- **MUST** route all email-send UX through `<EmailComposer />` via the three wrappers, and all programmatic send through `sendCommunication()`. Shared-lib components inject `authenticatedFetch` — no direct `@spaarke/auth` import inside the engine.
- **MUST** operate the Association Engine over the **normalized envelope** only. No engine rung may take a `Microsoft.Graph.Message` (or any channel-specific type) as input.
- **MUST** invoke `ICommunicationEnrichmentService` from **both** inbound and outbound paths (direction symmetry), including the outbound RAG-indexing leg that was previously missing.
- **MUST** record confidence + provenance (JSON in `sprk_associationprovenance`) on every association decision, and map confidence to `sprk_associationstatus` per the ladder: ≥0.85 deterministic (rungs 0–3) → `Resolved`; 0.50–0.85 or ANY AI rung → `Suggested`; <0.50/none → `Pending Review`; conflicting high-confidence → `Ambiguous`.
- **MUST** ship **auto-file ON for deterministic rungs 0–3 at ≥0.85**, gated behind a per-tenant **ADR-018 kill-switch** that flips the engine to suggest-only WITHOUT redeploy (this is the auto-file owner decision recorded below as **ADR Tension Path A**).
- **MUST** use `sprk_organization` as the organization association target (Spaarke's first-class org entity), never OOB `account` or OOB `organization`.
- **MUST** extend the ADR-024 regarding family (`RegardingLookupMap` / `TODO_REGARDING_CATALOG` / `RegardingFieldPriority`) — never introduce a second regarding mechanism.
- **MUST** inject the central `TokenCredential` + `IGraphClientFactory` + canonical Dataverse interfaces (ADR-028) on the server; client uses `@spaarke/auth` only (`OfficeNaaStrategy` for the Outlook add-in).
- **MUST** keep enrichment/association best-effort and non-fatal — a failure MUST NOT fail the send or inbound-capture path.

### ❌ MUST NOT

- **MUST NOT** re-introduce Server-Side Sync or any dependency on OOB Dataverse `email`/activity entities. The legacy `Services/Email/` subsystem is retired and MUST NOT be revived.
- **MUST NOT** auto-file on a semantic (rung 4) or AI (rung 5) match — those always land as `Suggested`/`Ambiguous` regardless of score.
- **MUST NOT** `new` a credential or `ConfidentialClientApplication` anywhere in the communication stack (ADR-028).
- **MUST NOT** build Teams/Slack/Gmail/SMS channel implementations in R4 — define the seams only; email is the sole implementation.
- **MUST NOT** add a 6th client-side send-email implementation. If a new mount emerges, add a new thin wrapper over the one engine.
- **MUST NOT** let AI decide privilege — AI may FLAG privilege, never decide it (ADR-015).

---

## Consequences

- **Positive**: outbound mail gains auto-association + RAG for free; one send surface eliminates 6 divergent implementations + a ~2.3K-LOC webresource; the envelope + seams make future channels additive; provenance + the kill-switch make auto-file safe and reversible (misfile is re-file, audited, never delete).
- **Cost / risk**: auto-file-ON carries a misassociation risk mitigated by the deterministic-only threshold + per-tenant kill-switch + audited re-file (R-1). The engine refactor risks regressing existing inbound matching — mitigated by preserving rungs 0–2 under characterization tests before extending (R-7).
- **Coordination**: the Responsive-Intelligence leg edits `Services/Ai/` internals owned by `spaarke-ai-architecture-redesign-r2`; it MUST consume that project's `Services/Ai/PublicContracts/` seams and coordinate via `/conflict-check` (no forking internals).

## ADR Tension — Auto-file (Path A, per CLAUDE.md §6.5)

The R4 design (DEC-4) recommended "suggest-only first, enable auto-file after measuring." The product owner directed **auto-file ON at launch** for deterministic rungs ≥0.85. This is a project-scoped exception (Path A), not an ADR violation — §5.4 of the design already sanctions ≥0.85 → `Resolved`, and the deviation stays inside the ADR-018 kill-switch guardrail with AI rungs excluded. Recorded here and in `spec.md` ADR Tensions; the kill-switch is the escape hatch.

## Related

| ADR | Relationship |
|---|---|
| ADR-024 (Polymorphic Resolver) | Engine extends the regarding family; never replaces it |
| ADR-028 (Auth v2) | Central `TokenCredential` + `IGraphClientFactory`; no `new` credential; `OfficeNaaStrategy` client-side |
| ADR-018 (Kill switches) | Per-tenant auto-file enable/disable without redeploy |
| ADR-032 (Null-Object) | Feature-gated communication services consumed by unconditional endpoints |
| ADR-013 (AI facade) | AI rung + Triage action reach AI via `PublicContracts`, not internal types |
| ADR-016 / ADR-014 (AI budget / cache) | Bound the rung-5 AI cost |
| ADR-015 (Privilege) | AI flags privilege, never decides |
| ADR-037 (DeliverComposite) | Section-name-keyed streaming for the Triage summary/checklist |
| ADR-006/026/021/022/012 (Code Page + Fluent v9) | Channel-aware Communication Code Page + `<EmailComposer />` |
| ADR-029 (Publish hygiene) | Net BFF size (retiring `Services/Email/` offsets additions) |
| ADR-038 (Testing) | KEEP-path categories; direction-symmetry + per-rung tests |

## References

- Full ADR (rationale, alternatives, revision log): `docs/adr/ADR-045-communication-architecture.md`
- Source: `projects/email-communication-solution-r4/` (`spec.md` FR-05 + §Technical Constraints; `design.md`; `reference/r3-send-side-design.md`)
