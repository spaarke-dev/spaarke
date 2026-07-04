# Spaarke AI — Architecture and Component Design (Canonical)

> **Status**: DRAFT v0.1 (2026-07-04) — intro + use cases only; architecture and
> component sections deferred to subsequent iterations pending review.
>
> **Purpose**: single canonical reference for the Spaarke AI platform's target
> architecture and component design, anchored in real use cases. Consolidates
> and replaces scattered prior work (see §0.4 Prior work below).
>
> **Owner**: Spaarke Engineering
>
> **Audience**: architecture reviewers, feature-team leads, platform engineers,
> AI-Ops. Not intended as an implementation guide or a deployment runbook —
> those live in `docs/guides/`.
>
> **Contract**: this document defines WHAT the AI platform must do (use cases +
> capability contracts) and the HOW-shaped design of components + interactions.
> It does NOT define per-tenant configuration, per-consumer implementation
> details, or platform-specific deployment steps.

---

## 0. Introduction

### 0.1 What Spaarke AI is

Spaarke is an **enterprise AI-directed legal operations intelligence platform**
embedded in Power Apps + Dataverse and SharePoint Embedded, with a .NET 8 BFF
mediating between clients (PCF controls, Code Pages, ribbons, custom pages)
and backend services (Azure OpenAI, Azure AI Search, Document Intelligence,
Dataverse, Cosmos DB, Redis).

**Spaarke AI** is the AI capability layer of that platform. It is not "a
summarize tool" or "a chat product" — it is a **portfolio of AI capabilities**
that can be invoked from any Spaarke surface (matter form, workspace, chat,
scheduled job, external integration) against any input (uploaded file,
Dataverse record, matter context, form field, another AI capability's output)
with results delivered to any destination (chat text, workspace widget, form
field write-back, email draft, dataverse record, notification).

### 0.2 What it is NOT

- ❌ Not a legal-research product (that market — case law, statutes, secondary
  sources — is served by Thomson Reuters CoCounsel, Casetext, Lexis, Vincent AI).
- ❌ Not a general-purpose chat assistant (that space — Claude, ChatGPT,
  Copilot — is served by frontier-model vendors directly).
- ❌ Not a standalone SaaS product; it lives inside the customer's Power
  Platform tenant and inherits Power Platform's identity, permission, and
  data-residency posture.

### 0.3 What this document does

1. Enumerates the **core use cases** the platform must support (§3), grouped by
   scenario category and each identified with a stable `UC-*` ID.
2. Provides **product context** relative to the broader legal-tech landscape
   (§1, §2) so architectural decisions can be evaluated against real competitive
   pressure.
3. Defines the **target architecture** across intent, capability, execution,
   and output-routing layers (§4 — deferred to v0.2 pending §3 review).
4. Catalogs **components** — Dataverse tables, BFF services, shared client
   libraries, widgets — with their contracts and interactions (§5 — deferred).
5. Defines the **configuration model** (Capability manifest, Action, Persona,
   Skill, Trigger, Output binding) — the vocabulary makers use to add or modify
   AI behavior without a code deploy (§6 — deferred).
6. Anchors the **intent and dispatch** design (§7 — deferred) — the layer that
   decides which capability runs when the user does or says something.
7. Sequences the **roadmap** (§8 — deferred) to migrate the current
   half-refactored implementation onto the target design.

### 0.4 Prior work this consolidates

Spaarke previously had NO canonical use-case document. Use-case thinking existed
scattered across four architecture references and embedded implicitly in the R4
through R7 project specs. This document adopts what was useful from each and
supersedes them for use-case purposes:

| Source | What it provided | This document's treatment |
|---|---|---|
| `docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` | 10 pre-built playbook concepts (NDA Review, Full Contract Analysis, Lease Review, Invoice Validation, etc.) | ✅ absorbed as UC-A-* (document intelligence) with formal IDs |
| `docs/architecture/AI-ARCHITECTURE.md` §Playbook-Driven LLM Output Pattern | Narrative-consumer scenarios (Daily Briefing, Insight Engine matter summaries, work assignment briefings) | ✅ absorbed as UC-D-* (proactive/scheduled) and UC-B-* (matter workflow) |
| `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` §6 | Three hypothetical widget scenarios (Risk Dashboard, PDF Viewer, Matter Snapshot) | ✅ preserved as illustrative user surfaces; use cases live in §3 |
| `projects/spaarke-ai-platform-chat-routing-redesign-r1/spec.md` §Phase 5R | Canonical chat user flow: upload → NL intent → playbook match → workspace → refinement | ✅ absorbed as UC-C-1 (chat-driven document workflow) — the flow shape is a use case; the specific mechanism is a design detail |
| `projects/spaarke-ai-platform-unification-r7/spec.md` + Waves 9-12 | Concrete scenarios: Daily Briefing 6-entity, Matter pre-fill, Project pre-fill, Assistant↔Workspace | ✅ absorbed as UC-B-* and UC-D-* |
| `projects/spaarke-ai-platform-unification-r7/notes/summarize-flow-2026-07-03.md` | Detailed component trace of the summarize flow after Wave 12.3 fixes | ✅ preserved as a diagnostic artifact; superseded architecturally by §4-7 once drafted |

None of these established a stable UC-ID vocabulary. This document does.

### 0.5 Living document contract

- Section IDs (UC-\*, §-numbering) are stable and **must not be renumbered** —
  external references will accumulate.
- New use cases append at the next available `UC-{category}-{n}` slot.
- Architecture and component sections (§4-8) will be added in subsequent
  revisions after this v0.1 (intro + use cases) is reviewed.
- Every substantive edit records the reviewer + date in §9 (revision log).

---

## 1. Product context

### 1.1 Where Spaarke AI fits in the customer's world

Spaarke targets the **corporate legal operations** function — in-house teams
managing matters, contracts, budgets, external counsel, and compliance across
the organization. This differs from three adjacent markets:

- **Law firm practice management** (Clio, MyCase, PracticePanther) — served by
  bespoke SaaS; Spaarke is not competing.
- **Legal research** (Thomson Reuters CoCounsel, LexisNexis+ AI, Vincent AI,
  Casetext) — case law, statutes, secondary sources; Spaarke may retrieve from
  these but does not replace them.
- **Ediscovery and litigation review** (CS Disco, Everlaw, Relativity aiR) —
  large-corpus classification for litigation; Spaarke shares underlying
  document-AI technology but is not positioned for that workload today.

Spaarke AI's **primary user** is a corporate legal operations professional:
in-house counsel, contract manager, compliance officer, paralegal, matter
coordinator. Their day is spent on matter creation, contract negotiation
oversight, obligation tracking, budgeting, external-counsel coordination, and
reporting to executives. The AI capabilities in this document are all in
service of shortening or automating steps in that day.

### 1.2 Constraints that shape the architecture

- **Tenant boundary is authoritative** — every AI call is scoped to a Power
  Platform tenant. No cross-tenant retrieval, no shared vector indices, no
  cross-tenant model fine-tuning. ADR-014 Redis + AI Search tenant-partitioning
  is binding.
- **Dataverse security applies to AI** — a user who cannot read a Dataverse
  record via the CRM permission model cannot read it through an AI capability
  either. The AI is not an authorization side-channel.
- **Human-in-the-loop for write-back** — no AI capability that writes to
  Dataverse, sends email, or otherwise leaves side-effects on the customer's
  data executes without an explicit user confirmation gesture (spec FR-11 in
  earlier R6 project; retained here as an architectural constraint).
- **AI cost is a first-class operational metric** — every capability has a
  budget (tokens per invocation, invocations per user per day). The platform
  must surface these to admins and enforce them.
- **Prompts and models are configurable, not code** — the platform's business
  value is that a legal ops admin can adjust the prompt for "NDA Review"
  without a code deploy. Every use case in §3 must be reachable through the
  configuration model (§6) — code changes are for capability *shape* (input
  type, output type), never capability *behavior*.

### 1.3 Non-goals

- Fine-tuning custom models per customer. Spaarke uses hosted foundation
  models (Azure OpenAI) with prompt engineering + RAG grounding. No fine-tuning
  pipeline is in scope through 2027.
- Multi-modal (image, audio, video) generation. Text and structured-JSON
  output only through 2026.
- Real-time collaborative co-authoring (Word.ai style). Content generation is
  request/response, not real-time synchronization.

---

## 2. Competitive landscape (quick reference)

This section exists so architecture decisions can be checked against real
customer alternatives. It is NOT a competitive strategy document.

| Product | Primary market | What they do well | Where Spaarke competes |
|---|---|---|---|
| **Harvey AI** | Law firms + in-house | Broad legal work-assistant chat; doc review; drafting | Contract review, drafting, doc summarize — but embedded in customer's Power Platform, not standalone SaaS |
| **Thomson Reuters CoCounsel** | Firms + in-house | Research, drafting, summarize, contract analysis, review | Doc summarize + extract + review; but tightly integrated with matter workflow, not a Q&A pane |
| **Spellbook** | Contract-heavy roles | Contract redlining + drafting, negotiation | Contract drafting via LLM + template; but positioned as end-to-end matter workflow, not just drafting |
| **Kira Systems / DFin AI** | M&A due diligence, contract analysis | Extract clauses from large contract sets | Contract-clause extraction; but as one of many capabilities, not a single-purpose tool |
| **Litera / DraftWise / iManage Insight+** | Document-heavy practice management | Document intelligence, knowledge management | Document intelligence layer of Spaarke; differentiated by matter-centricity |
| **CS Disco / Everlaw / Relativity aiR** | Ediscovery / litigation review | Massive corpus review, privilege detection | Similar underlying tech; Spaarke does not currently target this workload |
| **ChatGPT / Claude / Copilot** | General | Frontier chat + reasoning | Spaarke uses these models via Azure OpenAI — not a competitor at the model layer |

**Implications for architecture**:
- Spaarke MUST be embeddable in customer workflows (Power Platform surfaces).
- Spaarke MUST support matter-scoped context (a summarize on Contract A should
  not leak into Contract B).
- Spaarke SHOULD support write-back to Dataverse (unlike most competitors that
  return output for the human to re-enter manually).
- Spaarke NEED NOT compete on frontier reasoning quality — it uses the same
  models as CoCounsel etc.

---

## 3. Core use cases

Each use case has:
- **ID**: stable `UC-{category}-{n}`.
- **Actor**: who invokes it.
- **Trigger**: how it's invoked (menu, slash, NL, form action, schedule,
  external event).
- **Input**: what the capability consumes.
- **Behavior**: what the LLM (or deterministic step) does.
- **Output binding**: where the result goes.
- **Status**: `working`, `partial`, `planned`, `aspirational`.
- **Notes**: source, dependencies, references.

Categories:

- **A. Document intelligence** (UC-A-*): applying an AI operation to one or a
  few documents.
- **B. Matter workflow** (UC-B-*): AI accelerating specific steps in the
  matter lifecycle (intake, review, close-out).
- **C. Interactive Q&A** (UC-C-*): chat-style investigation over documents,
  records, and prior conversation.
- **D. Proactive and scheduled** (UC-D-*): AI capabilities that run without a
  user click — briefings, alerts, watchers.
- **E. Content generation** (UC-E-*): drafting, redlining, correspondence.
- **F. Data enrichment** (UC-F-*): auto-fill, categorization, tagging that
  runs against structured Dataverse data.
- **G. Cross-capability composition** (UC-G-*): use cases where one AI
  capability's output is another's input.

### 3.A Document intelligence

#### UC-A-1 · Document summarization

- **Actor**: legal ops user with a document to review.
- **Trigger**: (a) uploads a document in the Assistant chat pane; types
  "summarize this document" or `/summarize`. (b) selects "Summarize" from a
  document ribbon on the record form.
- **Input**: one or more session-uploaded files (PDF, DOCX, TXT, MD), or a
  Dataverse `sprk_document` record whose SPE file has been already indexed.
- **Behavior**: LLM produces a strict-schema structured output: `tldr` (3 bullet
  points), `summary` (multi-sentence narrative), `keywords` (comma-separated
  terms), `entities` (organizations, people, amounts, dates, references).
- **Output binding**: Workspace pane "Summary" tab, rendered by
  `StructuredOutputStreamWidget` with per-field sections.
- **Status**: **working** as of R7 Wave 12.3 Phase 12.3a (2026-07-03) —
  end-to-end verified via curl + browser UAT.
- **Notes**: acts as reference implementation for the Linear-consumer +
  workspace-widget pattern. See `notes/summarize-flow-2026-07-03.md` for the
  detailed component trace.

#### UC-A-2 · Contract analysis (full review)

- **Actor**: contract manager or in-house counsel reviewing an inbound contract.
- **Trigger**: user selects "Full Contract Review" from a menu on a document
  record OR from the Assistant pane.
- **Input**: one contract document.
- **Behavior**: structured extraction across: parties, term (start/end),
  payment terms, termination clauses, IP assignment, indemnity, warranty
  disclaimers, non-compete, non-solicit, choice of law, dispute resolution,
  assignment, change of control, and a risk-flag summary.
- **Output binding**: Workspace pane multi-section widget (one section per
  clause type) with links back to the original text.
- **Status**: aspirational (referenced in `SPAARKE-AI-STRATEGY-AND-ROADMAP.md`
  as "Full Contract Analysis" playbook).

#### UC-A-3 · NDA review (fast targeted subset of UC-A-2)

- **Actor**: user reviewing a Non-Disclosure Agreement.
- **Trigger**: same as UC-A-2 but selects "NDA Review".
- **Input**: one NDA document.
- **Behavior**: extract: disclosing party, recipient, permitted use, term of
  confidentiality, return-of-materials clause, jurisdiction, plus a
  risk-classification for common problem clauses (unlimited term, mutual vs
  one-way asymmetry, injunctive relief).
- **Output binding**: Workspace pane NDA-specific widget.
- **Status**: aspirational.

#### UC-A-4 · Invoice validation

- **Actor**: matter coordinator or billing admin.
- **Trigger**: (a) user uploads an outside-counsel invoice; (b) inbound
  invoice email routes through processing pipeline.
- **Input**: invoice document.
- **Behavior**: extract line items, categorize by matter, validate against
  matter budget and outside-counsel guidelines, flag anomalies.
- **Output binding**: (a) chat: line-item table + flags for user review;
  (b) form write-back: creates `sprk_invoice_line_item` records under
  requires-user-Proceed gate.
- **Status**: aspirational.

#### UC-A-5 · Clause extraction (targeted, single question)

- **Actor**: user with a specific clause type in mind.
- **Trigger**: user types NL question like "what are the payment terms?" in
  chat with a document attached.
- **Input**: one document.
- **Behavior**: LLM extracts the specific clause text + surrounding context.
- **Output binding**: chat text response with a citation.
- **Status**: **partial** — currently works as generic LLM Q&A via
  SprkChatAgent tool loop. Not yet a distinct capability.

#### UC-A-6 · Multi-document comparison

- **Actor**: user comparing two versions of a contract or an inbound draft to
  a template.
- **Trigger**: user uploads two documents and asks "compare these" or
  selects a menu action.
- **Input**: two documents.
- **Behavior**: LLM identifies material differences by clause, categorizes
  each change (favorable, neutral, unfavorable), summarizes the diff.
- **Output binding**: Workspace pane comparison widget (two-column diff with
  categorized deltas).
- **Status**: planned (not yet implemented; referenced obliquely in earlier
  strategy docs).

#### UC-A-7 · Document classification (type / practice area)

- **Actor**: matter coordinator uploading unknown documents.
- **Trigger**: (a) on upload, auto-run; (b) explicit "classify this" action.
- **Input**: one document.
- **Behavior**: LLM classifies document type (NDA, MSA, SOW, invoice, order
  form, employment agreement, ...) and practice area (commercial, employment,
  IP, corporate governance, ...) with confidence scores.
- **Output binding**: Dataverse field write-back to `sprk_document.sprk_type`
  under requires-user-Proceed gate. Also chat surfaces the classification.
- **Status**: planned. Related to
  `chat-routing-redesign-r1` file-classification service (task 067).

### 3.B Matter workflow

#### UC-B-1 · Matter intake pre-fill

- **Actor**: user creating a new matter, having uploaded documents.
- **Trigger**: user launches Matter Creation wizard AND has document(s) in
  session context.
- **Input**: session-uploaded document(s).
- **Behavior**: LLM proposes values for matter fields — parties, matter type,
  practice area, external counsel, description, key dates — based on
  document contents.
- **Output binding**: form field pre-population in the Matter Creation wizard
  (client-side; not committed until user submits the form).
- **Status**: **partial** — R7 Wave 12.1 targets. Consumer key
  `matter-pre-fill` exists in the routing table; Action definition exists in
  Dataverse; end-to-end wiring incomplete.

#### UC-B-2 · Project setup pre-fill

- **Actor**: user creating a new project within a matter.
- **Trigger**: user launches Project Creation wizard.
- **Input**: parent matter context + optionally session-uploaded document(s).
- **Behavior**: proposes project name, description, key milestones, resource
  assignments derived from matter + docs.
- **Output binding**: form field pre-population.
- **Status**: **partial** — R7 Wave 12.2; consumer key `project-pre-fill`.

#### UC-B-3 · Matter briefing on demand

- **Actor**: legal ops user needing to prepare for a matter meeting.
- **Trigger**: user opens a matter form and clicks "Prepare briefing" (or
  similar). Also runs automatically nightly (see UC-D-1).
- **Input**: matter record + recent activity (child records: documents added,
  emails, notes, tasks, obligations, deadlines).
- **Behavior**: narrative briefing: what's new since last review, upcoming
  deadlines, open questions, recommended next actions.
- **Output binding**: Workspace pane "Briefing" tab OR daily-briefing email
  (see UC-D-1).
- **Status**: partial — the on-demand path is planned; the scheduled variant
  is the current R7 Wave 12.0 target.

#### UC-B-4 · Contract obligation extraction and tracking

- **Actor**: contract manager after finalizing a contract.
- **Trigger**: user selects "Extract obligations" on a finalized contract.
- **Input**: contract document.
- **Behavior**: LLM extracts recurring and one-time obligations (payment
  deadlines, renewal windows, notice periods, deliverables) with due dates.
- **Output binding**: Dataverse writes to `sprk_obligation` under user-Proceed
  gate. Populates the matter's obligation tracker; triggers notification
  cadence.
- **Status**: aspirational.

#### UC-B-5 · Matter close-out summary

- **Actor**: matter owner or partner reviewing a matter for archive.
- **Trigger**: user selects "Generate close-out summary" or matter state
  transitions to "Closed".
- **Input**: matter record + all child activity (documents, tasks, notes,
  emails, financials).
- **Behavior**: narrative summary suitable for archive / audit / handoff:
  what the matter was, what happened, outcome, financial summary, lessons
  learned, key documents.
- **Output binding**: creates `sprk_document` record on the matter of type
  "Close-out Report"; also renders in a Workspace tab for user edit.
- **Status**: aspirational.

### 3.C Interactive Q&A

#### UC-C-1 · Chat over uploaded documents

- **Actor**: user reviewing one or more documents interactively.
- **Trigger**: user has one or more documents in the Assistant chat session
  and asks natural-language questions.
- **Input**: session-uploaded documents (via ExtractedText inline or RAG chunks
  when the question requires retrieval across a corpus).
- **Behavior**: LLM answers with grounded citations to the source document(s).
- **Output binding**: chat text response with inline citation markers linking
  to source passages.
- **Status**: **working** — SprkChatAgent path; the ordinary chat conversation
  loop.

#### UC-C-2 · Chat over matter (records, not files)

- **Actor**: user asking about a matter's state without a specific document
  in mind.
- **Trigger**: chat opened on a matter form context; user asks "what obligations
  are coming due in the next 30 days" or "who's assigned to this matter" etc.
- **Input**: matter record + related records (Dataverse queries via tools).
- **Behavior**: LLM invokes tools to retrieve the requested data and answers
  in chat with links back to the records.
- **Output binding**: chat text with inline record links.
- **Status**: **partial** — SprkChatAgent has the tool infrastructure; specific
  record-retrieval tools exist but not exhaustively.

#### UC-C-3 · Refinement of previous AI output

- **Actor**: user reviewing a previous AI capability's result.
- **Trigger**: user selects text in a Workspace widget result and asks a
  clarifying / refining question in chat.
- **Input**: the selected text + surrounding widget context.
- **Behavior**: LLM answers relative to the selected passage; can propose an
  edit that gets applied back to the widget.
- **Output binding**: chat text + optional widget-content edit via a
  "field_write" back to the widget.
- **Status**: **partial** — the highlight-and-refine flow exists in SprkChat
  (`SprkChatHighlightRefine`); the widget-write-back path is scaffolded but
  not universally supported across widgets.

#### UC-C-4 · Suggested follow-ups

- **Actor**: user who just received an AI answer.
- **Trigger**: automatic after any AI response.
- **Input**: the conversation up to that point.
- **Behavior**: LLM proposes 1-3 relevant follow-up questions.
- **Output binding**: chat "Suggestions" chip strip.
- **Status**: **working** — SprkChat suggestion feature.

### 3.D Proactive and scheduled

#### UC-D-1 · Daily briefing email

- **Actor**: legal ops user subscribed to daily briefings.
- **Trigger**: scheduled job runs daily at a per-user time.
- **Input**: user's matters + recent activity across 6 entity types
  (documents, emails, tasks, notes, deadlines, obligations).
- **Behavior**: narrative briefing summarizing what changed overnight, what
  needs attention today, and what's coming up.
- **Output binding**: email delivery via Communication Service +
  Dashboard tile on next login.
- **Status**: **partial** — R7 Wave 12.0 targets this; narrative playbook
  pattern (Wave 11 POC) validated. 6-entity aggregation service being built.

#### UC-D-2 · Deadline / obligation reminders

- **Actor**: matter owner and assignees.
- **Trigger**: scheduled job runs periodically (daily / hourly).
- **Input**: Dataverse `sprk_obligation` and `sprk_deadline` records due within
  a configurable window.
- **Behavior**: LLM personalizes reminder text based on obligation type,
  matter context, and recipient role.
- **Output binding**: email, Teams notification, Dataverse notification record.
- **Status**: aspirational; the deterministic reminder path (no LLM
  personalization) is built.

#### UC-D-3 · Inbound email triage and matter routing

- **Actor**: shared "legal" inbox monitored by the platform.
- **Trigger**: new email arrives.
- **Input**: email content + attachments.
- **Behavior**: LLM classifies email (new matter inquiry, existing matter
  correspondence, invoice, junk), extracts matter reference or party names,
  routes to correct matter and assignee.
- **Output binding**: Dataverse writes creating `sprk_email` record under
  correct matter, notifications to assignee, optionally auto-creates matter
  record for new inquiries.
- **Status**: **partial** — Communication Service exists; routing rules exist;
  LLM classification integration is next.

#### UC-D-4 · Anomaly / risk watch

- **Actor**: legal ops leadership.
- **Trigger**: scheduled scans across matter portfolio.
- **Input**: portfolio-level state (budgets, deadlines, workload,
  external-counsel spend, matter status).
- **Behavior**: LLM identifies anomalies (matter runs 40% over budget, deadline
  cluster next Friday, one attorney assigned to 30% of open matters) and
  proposes actions.
- **Output binding**: Executive dashboard tile + weekly briefing email to
  leaders.
- **Status**: aspirational.

### 3.E Content generation

#### UC-E-1 · Draft a document from a template + instructions

- **Actor**: attorney or contract manager drafting.
- **Trigger**: user selects "Draft ..." action on a matter or from Assistant.
- **Input**: template document (from Dataverse `sprk_template`) + user's
  instructions + matter context (parties, subject).
- **Behavior**: LLM fills the template's variable regions using matter
  context; flags places where more input is required.
- **Output binding**: new draft document uploaded to SPE + record in Dataverse.
- **Status**: aspirational.

#### UC-E-2 · Redline / revise an existing document

- **Actor**: attorney reviewing a counterparty draft.
- **Trigger**: user selects "Redline based on ..." on a document.
- **Input**: current document + firm's playbook / preferred positions.
- **Behavior**: LLM proposes tracked-change edits, categorizes each change
  (must-have, nice-to-have, negotiable), with rationale.
- **Output binding**: new document version in SPE with tracked changes; diff
  summary in chat.
- **Status**: aspirational.

#### UC-E-3 · Draft correspondence (email, memo, letter)

- **Actor**: user needing to send a piece of correspondence.
- **Trigger**: user selects "Draft email to ..." or types NL intent.
- **Input**: recipient, subject, key points, matter context.
- **Behavior**: LLM drafts correspondence in requested tone and format.
- **Output binding**: draft email in Outlook (via Graph); draft memo document
  in Word; chat-visible preview.
- **Status**: aspirational; overlaps with existing Office Add-in scope.

### 3.F Data enrichment

#### UC-F-1 · Auto-tag / auto-categorize records

- **Actor**: legal ops admin managing metadata quality.
- **Trigger**: (a) scheduled sweep; (b) on-save trigger for new records.
- **Input**: record content (document text, note body, task description).
- **Behavior**: LLM proposes tags / categories / practice-area assignments
  from a configurable taxonomy.
- **Output binding**: Dataverse field write-back under batch-approval gate.
- **Status**: aspirational.

#### UC-F-2 · Party / entity de-duplication

- **Actor**: legal ops admin.
- **Trigger**: (a) scheduled; (b) on-save when a new party is created.
- **Input**: party records + string similarity + LLM canonicalization.
- **Behavior**: LLM identifies likely duplicate parties across matters
  (Company A LLC vs Company A, LLC vs Company A Limited Liability Company),
  proposes canonical form.
- **Output binding**: Dataverse merge action (deterministic once approved).
- **Status**: aspirational.

### 3.G Cross-capability composition

#### UC-G-1 · Matter intake pipeline (compound)

- **Actor**: user creating a new matter with source documents.
- **Trigger**: user drops documents into "New matter" wizard.
- **Behavior**: chain of capabilities runs:
  1. UC-A-7 classify each document
  2. UC-A-1 summarize each document
  3. UC-B-1 propose matter fields
  4. UC-A-4 or UC-B-4 extract obligations if a contract is present
  5. UC-D-3 route related email if any
- **Output binding**: matter created with all associated records populated;
  user confirms and submits.
- **Status**: aspirational; each individual capability is at various maturity
  (UC-A-1 working, others partial/planned).

#### UC-G-2 · Contract negotiation cycle (compound)

- **Actor**: attorney managing an outbound negotiation.
- **Behavior**: iterative loop of UC-A-6 (compare inbound vs prior version),
  UC-A-1 (summarize what changed), UC-E-2 (draft counter-proposal),
  UC-C-3 (refine with user).
- **Output binding**: version history in SPE, negotiation state on the matter.
- **Status**: aspirational.

---

## 4. Architecture overview

*Deferred to v0.2. Will draw on `notes/summarize-flow-2026-07-03.md` and the
component trace already done today; extends it to the general N-capability case.*

## 5. Component model

*Deferred to v0.2.*

## 6. Configuration model (Capability manifest)

*Deferred to v0.2.*

## 7. Intent and dispatch

*Deferred to v0.2. Will resolve the four-intent-mechanism drift discussed in
2026-07-04 review with the operator.*

## 8. Roadmap

*Deferred to v0.2.*

---

## 9. Revision log

| Version | Date | Author | Notes |
|---|---|---|---|
| v0.1 | 2026-07-04 | Claude (with operator direction) | Initial draft. §0-3 (intro, product context, competitive landscape, use case catalog) drafted for review. Architecture and component sections deferred pending §3 approval. |
