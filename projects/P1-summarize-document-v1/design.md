# P1 — Summarize a Document (v1)

> **Project ID**: `P1-summarize-document-v1`
> **Status**: Design — pre-pipeline
> **Owner**: Product
> **Created**: 2026-06-26
> **Estimated effort**: ~6-8 weeks (productization sprint)
> **Audience**: Product + sales + engineering. This is a PRD with engineering traceability — read sections 1-3 for product framing, 4-7 for build scope.
> **Sales-deck title**: *"Spaarke AI Document Summarization — legal-area-aware, in-place editable, saved to your matter, sent from the system."*

---

## 1. Project framing

### 1.1 Why this project exists (the honest assessment)

Spaarke has built substantial AI infrastructure — JPS playbook framework with scope/node/tool/knowledge/persona model, three-pane SpaarkeAi shell, ADR-030 v2 PaneEventBus, R6 9-pillar architecture convergence, R1 chat-routing redesign with intent-based dispatch. **What we have NOT done is surface that infrastructure as user-visible product features.**

The 2026 competitive landscape (Harvey, CoCounsel, Spellbook, Robin, Legora, Wordsmith) demonstrates that legal AI products live or die by **demoable user verbs**, not infrastructure. Today, a Spaarke sales demo can show: a chat shell, `/summarize` slash, pinned facts, structured outputs in workspace tabs. That is **not a credible corporate-SMB legal AI product**.

P1 is the first project in a **product-feature-led project sequence** (P1, P2, P3, ...). Each project ships a coherent product feature that a salesperson can demo end-to-end. P1 picks the closest-to-done verb — Summarize a Document — and **productizes it fully**: UAT-passed, demo-ready, documented, sales-trained.

### 1.2 Why "Summarize" first (and not Drafting / Intake / Outside Counsel)

Three reasons:

1. **Closest to done.** R6 P4 already routes `/summarize` through `PlaybookExecutionEngine`. R6 Hotfix #4 fixed the workspace-tab-open issue. The summarize playbook fires today. Productization is mostly **UAT + surface completion + the 4 multipliers**, not greenfield build.
2. **Lowest infrastructure risk.** No new external API surfaces (no DocuSign, no Teams bot, no e-billing AI). Reuses existing Graph MI for outbound email, existing SPE container infrastructure for save, existing matter management for association.
3. **Multiplier seeding for P2.** Drafting (P2) shares 4 of the 5 build items with Summarize. Building them in P1 with paragraph-structure + rich-text extensibility in mind means P2 inherits 60% of its scope from P1. Sequence: P1 → P2 → P3.

### 1.3 What this project explicitly does NOT ship

- **Open-ended drafting** (memo, brief, clause) — P2 scope
- **NDA / agreement drafting** — P2 scope
- **In-Word AI redline surface** — strategic bet, separate project (P-bet-1)
- **Outlook AI compose UX inside Outlook** — partial (we ship outbound send FROM Spaarke; in-Outlook composition is later)
- **Teams send / Teams app** — out of scope (manual link works for SMB)
- **E-signature integration** — out of scope (DocuSign/AdobeSign integration is P3 or later)
- **Multi-document tabular review** — P3 or later
- **Cross-session matter memory promotion workflow** — substrate inherited from R6, full workflow is post-P1
- **Outside counsel work-product summary intake automation** — overlaps but is P3 scope; P1 ships the summarize verb, not the auto-intake pipeline

---

## 2. User view — the 8 verbs

A corporate counsel user (in-house legal, SMB company) at their desk:

| # | Verb | What the user does | What the user sees |
|---|---|---|---|
| 1 | **Open + select document** | Opens SpaarkeAi from a matter (or starts fresh). Drops a file in chat, OR selects from matter docs. | Three-pane shell. Chat session active. Attachment indicator shows file name + size. |
| 2 | **Summarize** | Types `/summarize`, OR types "summarize this document", OR types "summarize this vendor contract" — natural language allowed. | Workspace pane opens a summary tab. Streaming TL;DR / Summary / Key Takeaways / Entities renders progressively. |
| 3 | **Get a summary that's legal-area-aware** | (Implicit — happens automatically) | Summary respects the document type: a vendor contract gets payment terms, indemnity, liability cap, term, renewal. An employment agreement gets compensation, benefits, term, termination, non-compete. A lease gets premises, term, rent, options. |
| 4 | **Ask follow-up questions** | Continues chat: "what's the cap on liability?" "is there a non-compete?" "what does the renewal clause say?" | Chat answers with citation back to the source document section. |
| 5 | **Update the summary via Assistant** | Says: "make the TL;DR shorter" / "add the indemnity terms to the summary" / "rephrase point 3" | Existing summary tab updates in place (no new tab). Visual indicator shows "updated by Assistant just now." |
| 6 | **Update the summary directly** | Clicks on summary text. Edits TL;DR. Edits Key Takeaways list. Adds a note. | In-place rich text editor. Save-on-blur. "Edited by you" indicator. |
| 7 | **Save as a Document** | Clicks "Save Summary." Picks: associate to (a) this matter, OR (b) a project, OR (c) a work assignment, OR (d) an invoice, OR (e) an event. | Generates a Word doc artifact. Uploads to matter's SPE container. Creates `sprk_document` Dataverse row with `regardingobjectid` to chosen record. Shows confirmation with link to the saved doc. |
| 8 | **Send via email** | Clicks "Email this." Pick recipients (Spaarke contacts or external). System composes: original doc as attachment + summary Word doc as attachment + a system deep-link to the summary. | Email composer shows preview. User can add a message. Sends via Graph (from user's mailbox via OBO, OR from MI service mailbox). Audit log records the send. |

**One scenario, end-to-end** (the sales demo): *Sarah, in-house counsel at MidCorp, gets an inbound MSA from a new vendor. She opens SpaarkeAi from the "MidCorp ↔ Acme Vendor" matter, drops the MSA in chat, says "summarize this." Workspace pane fills with vendor-contract-shaped summary in 8 seconds. She asks "what's the liability cap?" — chat answers, citing § 12.3. She says "add the renewal terms to the summary" — the tab updates. She clicks TL;DR, edits it slightly. Hits Save Summary → associates to the MidCorp ↔ Acme Vendor matter. Clicks Email this → recipient: her CFO, subject auto-filled from matter name, body has the summary + deep link, original MSA attached. Done in under 90 seconds.*

---

## 3. Competitive context — what users expect

### 3.1 What every competitor has (universal table stakes)

From research (Harvey, CoCounsel, Spellbook, Robin AI, Legora, Wordsmith — June 2026):

- **Chat over a document with RAG retrieval** — every product
- **Document summarization with structured output** — every product
- **Subject-area / document-type awareness** — every product positions this (the precision varies)
- **Follow-up Q&A with citation back to source** — every product
- **Save output to a system of record** — every product (Word, matter system, vault)
- **Share output via email or link** — every product
- **Workspaces / matter-scoped sessions** — CoCounsel 2026 flagship; spreading fast

### 3.2 What our corporate-SMB segment specifically expects

(Per our positioning: corporate counsel, SMB, Microsoft-native, balance of in-house work + outside-counsel management.)

- **Outlook integration** — corporate counsel lives in Outlook. Sending FROM Spaarke into Outlook (and vice versa) is table stakes.
- **SharePoint / OneDrive document storage** — corporate IT runs on Microsoft. Saving to SharePoint is the expected default, not an integration to negotiate.
- **Matter-scoped everything** — summaries belong to a matter. Documents belong to a matter. Emails reference a matter. Spaarke's strong matter management is the wedge.
- **Built on Microsoft** as a trust signal — corporate IT trusts MS-native more than yet-another-SaaS data store.
- **Audit + privilege protection** — corporate counsel work is privileged. ADR-015 tier-1 telemetry safety + audit container + MI auth align with this.

### 3.3 Where we can differentiate

| Differentiator | Our advantage | Competitor weakness |
|---|---|---|
| **Legal-area-aware via vector-matched playbooks** (not a separate classifier step) | JPS playbook framework + `PlaybookDispatcher` Phase B vector match + `IntentRerankerService` already ship. Authoring 4-6 area-specific summarize playbooks gets us area-awareness with zero new BFF code. | Most competitors hard-code area detection in their prompts. We can ship new area playbooks as content, not code. |
| **First-class matter + SPE integration** | `PlaybookChatContextProvider` carries matter context. `sprk_document` polymorphic `regardingobjectid` to matter/project/work-assignment/invoice/event already exists. Save-summary-to-matter is a UX add, not a system to build. | Spellbook, Harvey, Robin run on top of customer Word/SharePoint with weak structure. Our matter system IS the structure. |
| **Three-pane shell as the canonical AI surface** | Chat (input) + Workspace (output) + Context (memory) is a coherent UX model. Summary lives in the Workspace tab as a first-class object with edit + save + email actions. | Competitors mostly run inside Word or as a side panel. Our shell is purpose-built for AI conversations with persistent artifacts. |
| **Subject-matter awareness without LLM hallucination of structure** | Each playbook has a defined output schema. The vendor-contract summary playbook will ALWAYS produce {parties, payment, indemnity, liability_cap, term, renewal} — no LLM drift. | Competitors that rely on prompt-only structure get inconsistent outputs as model versions change. Our `outputSchema` per playbook is contractually enforced. |

### 3.4 Where we are NOT differentiated (so don't oversell)

- We have nothing better than competitors on general open-ended drafting (P2 scope, partially addressable)
- We are weaker on in-Word AI experience (Spellbook lives in Word; we don't)
- We have no legal research / Westlaw / Lexis integration (not in positioning)
- We have no portable AI (mobile, voice) — desktop Microsoft-native is our lane

---

## 4. What we already have (existing Spaarke components)

### 4.1 Already shipped and working in this code path

| Component | Location | What it does for P1 |
|---|---|---|
| **SpaarkeAi three-pane shell** | `src/solutions/SpaarkeAi/` | The canvas. Chat + Workspace + Context panes. |
| **Chat session lifecycle** | `Api/Ai/ChatEndpoints.cs` + `ChatSessionManager.cs` | Session create / switch / message send with SSE streaming. |
| **`/summarize` slash + intent classification** | R6 P4 `SessionSummarizeOrchestrator` + R1 `IntentRerankerService` + `PlaybookDispatcher.RunPhaseBManifestAbsentAsync` | Verb #2. Natural language "summarize this" routes the same way. |
| **`PlaybookExecutionEngine` running playbooks** | `Services/Ai/Playbooks/` | Engine that runs the summary playbook end-to-end. |
| **`sprk_playbookconsumer` Dataverse routing** | R1 Phase 1R | Maps intent → playbook GUID. Already deployed with 6 consumers. |
| **`PlaybookDispatcher` Phase B vector match** | `Services/Ai/Chat/PlaybookDispatcher.cs` | Matches user intent + file content to playbook descriptions. **THIS IS WHAT MAKES AREA-AWARENESS WORK** — we author multiple summarize playbooks; the dispatcher picks the right one. |
| **`PlaybookCandidateSelector`** | `Services/Ai/Chat/PlaybookCandidateSelector.cs` | Ranks playbook candidates. Composes with vector match. |
| **`PlaybookChatContextProvider`** | `Services/Ai/Chat/PlaybookChatContextProvider.cs` | Provides matter context to playbooks. **FR-45 invariant locked at line 679** — do not regress. |
| **File upload to chat** | `ChatDocumentEndpoints.cs` + R1 Phase 5R Track 1 | Verb #1. 25 MB binary cap per FR-04. |
| **`StructuredOutputStreamWidget`** | `@spaarke/ai-widgets` (Spaarke.AI.Widgets) | Renders playbook structured output as a workspace tab. Verb #2. |
| **SPE container infrastructure** | `Infrastructure/Graph/` + `SpeFileStore` facade | Storage substrate for Verb #7 save. |
| **`sprk_document` Dataverse entity + `regardingobjectid` polymorphic relationship** | Dataverse schema | Save-summary-to-record substrate. Already powers DocumentRelationshipViewer. |
| **Office Add-ins (Outlook + Word)** | `src/client/office-addins/` | Currently saves docs INTO Spaarke. We will extend the pattern for outbound. |
| **`PaneEventBus` (ADR-030 v2)** | `@spaarke/ai-widgets/events/` | `workspace`, `context`, `conversation`, `safety`, `memory` channels. We dispatch updates here. |
| **Graph MI for outbound** | `GraphClientFactory.cs` per ADR-028 | `DefaultAzureCredential` resolves the App Service's MI. `Mail.Send` permission needed for Verb #8. |
| **Cosmos `audit` container + ADR-015 tier-1 telemetry** | `Services/Telemetry/` | Save / email events get audit logged with deterministic IDs only — privilege-safe. |
| **`sprk_aichatmessage` write-only** (per R1 FR-25) | Dataverse | Audit trail for chat turns. |
| **Pinned Memory CRUD UI** | R6 P7 Q7 expansion | Allows user to pin facts that influence future summaries (e.g., "our standard indemnity is mutual"). |
| **Daily Briefing infrastructure** (recently rewritten) | `DailyBriefingEndpoints.cs` task-031 | Reference pattern for thin-dispatch endpoints via `IConsumerRoutingService` + `IInvokePlaybookAi`. P1 endpoints follow this pattern. |
| **`IInvokePlaybookAi` facade (R6 P3)** | `Services/Ai/PublicContracts/` | The canonical way to invoke playbooks from non-chat endpoints. Used for save/email endpoint integrations. |

### 4.2 Built but currently blocked (infrastructure dependencies)

| Component | Blocker | Mitigation |
|---|---|---|
| **RAG retrieval** (verb #4 follow-up Q&A) | AI Search service deleted in dev — `spaarke-search-dev.search.windows.net` NXDOMAIN | Sister project: `spaarke-ai-azure-setup-dev-r1`. Hard dependency for P1 graduation. Single-document Q&A (LLM with file in context) still works without AI Search; matter-RAG does not. |
| **Distributed cache** (chat-session continuity, tool-list resolution cache) | `Redis__Enabled=false` in App Settings; `IConnectionMultiplexer` not registered | Sister project: `spaarke-redis-cache-remediation-r1` (closeout) + `-r2` (spec just landed). CacheModule in-memory fallback works for single-instance dev; multi-instance scale requires Redis. |

### 4.3 Built but silently broken (R6 surface-completion gaps relevant to P1)

| Component | Symptom | Fix |
|---|---|---|
| **`update_workspace_tab` chat-tool handler** | LLM tool call fails silently — Dataverse `sprk_analysistool` row missing | Run `scripts/Seed-TypedHandlers.ps1 -OnlyHandler UpdateWorkspaceTab` (R6 Tier C). 1 day. Verb #5 depends on this. |
| **`send_workspace_artifact` chat-tool handler** | Same as above | Same fix. Used for initial summary tab creation when summarize is invoked from chat (vs slash). |
| **`close_workspace_tab` chat-tool handler** | Same as above | Same fix. Used for "close the summary" voice flow. |
| **`/save-to-matter` slash command** | Not wired in `CommandRouter.ts` | R6 Tier B. Verb #7 has UI button equivalent, but slash is sales-demo-friendly. |
| **"Add to Assistant" per-tab toggle** | Toggle UI not built | R6 Tier E. Verb #6 (in-place edit) intersects this — user needs to control what LLM can see/edit. |

### 4.4 Inherited substrate from R6 Pillar 7 (memory)

R6 P7 shipped the memory substrate that influences summarize quality:

- `MatterMemoryService` activation in factory — pinned-context lookup happens automatically when matter is in scope
- `MemoryCompositionService` — though FR-27 found it has 0 production callers; structurally available
- Token budget tracker — keeps summarize within cost envelope
- Pinned Memory UI — user pins ("our standard indemnity is mutual" → summary highlights deviations from this)

P1 leverages this passively. We don't extend it; we surface its effect (pinned facts visibly influence summary output, with affordance to "show what's pinned that affected this answer").

---

## 5. What we need to build (needed components)

### 5.1 The 4 area-specific summarize playbooks (THE feature core)

| Playbook | Document types it handles | Output schema (illustrative) |
|---|---|---|
| **`summarize-vendor-contract@v1`** | MSA, SOW, vendor agreement, services agreement, procurement contract | parties, scope, payment_terms, indemnity, liability_cap, term, renewal, termination, governing_law, key_risks |
| **`summarize-employment-agreement@v1`** | Offer letter, employment agreement, separation agreement, NDA-employment | parties, role, compensation, equity, benefits, term, termination, non_compete, non_solicit, IP_assignment, key_risks |
| **`summarize-real-estate-lease@v1`** | Commercial lease, sublease, license-to-occupy | parties, premises, term, rent, escalation, options, permitted_use, alterations, assignment, key_risks |
| **`summarize-ip-agreement@v1`** | License agreement, assignment, NDA-IP, technology transfer | parties, IP_scope, grant, exclusivity, term, royalty, termination, audit_rights, key_risks |
| **`summarize-generic@v1` (refresh)** | Fallback when vector match doesn't lock to one of above | parties, TL;DR, key_terms, term, key_risks |

**Authoring approach** (per JPS playbook framework):
- Use `/jps-playbook-design` skill to author each
- Each playbook is a multi-node JPS definition: extract → classify → generate summary per output schema → assemble
- Each binds to `sprk_playbookconsumer` row with `consumertype="document-summarize"` + `consumercode={area}` so the dispatcher routes correctly
- Output schema enforced via `outputSchema` field (no LLM drift)
- Persona: corporate counsel + matter-scope (uses `PlaybookChatContextProvider`)

**Effort**: ~3-5 days per playbook (authoring + tests + Dataverse deploy + UAT). 4 playbooks = ~3 weeks of focused playbook work.

### 5.2 The 4 multipliers (designed to serve P2 drafting too)

These are the components that, if built right, also unlock P2 (Draft an NDA / general drafting) with minimal additional work.

#### Multiplier A — Workspace tab update handler (1 day)

- **What**: Seed the `sprk_analysistool` Dataverse rows for `update_workspace_tab`, `send_workspace_artifact`, `close_workspace_tab` so the LLM tool-call surface works.
- **Why**: Verb #5 (update via Assistant). Today the handlers are coded but silently broken (R6 Tier C).
- **How**: `scripts/Seed-TypedHandlers.ps1 -OnlyHandler {handler}` for each.
- **Designed for P2**: No design constraint — same handlers serve drafting tab updates.

#### Multiplier B — Editable workspace tab widget (rich text from day 1)

- **What**: New widget variant: `EditableStructuredOutputWidget`. Renders the structured output (TL;DR, Summary, Key Takeaways, Entities) with each field independently editable. Rich text where appropriate (Summary is multi-paragraph). Save-on-blur dispatches `workspace.widget_update` event (ADR-030 compliant).
- **Why**: Verb #6 (update directly). Today `StructuredOutputStreamWidget` is read-only.
- **How**: Fluent v9 component. Lives in `@spaarke/ai-widgets`. Uses `RichTextEditor` or `Textarea` per field schema. Wires into existing `workspace` channel for change events.
- **Designed for P2**: **CRITICAL** — must handle paragraph structure, not just bullets, because P2 NDAs are paragraph documents. Spec'd from day 1 with `EditableRichTextSection` variant for paragraph content. P2 reuses this with extended section schema for NDA clauses.
- **Effort**: ~3-4 weeks (single significant component, but pattern-setting for all future editable AI output).

#### Multiplier C — Word/PDF generation + SPE upload + `sprk_document` creation

- **What**: New BFF service `IDocumentArtifactService`: takes a structured output (summary OR draft), generates a Word doc (OpenXML SDK) and optionally PDF, uploads to the matter's SPE container, creates `sprk_document` Dataverse row with `regardingobjectid` to chosen record.
- **Why**: Verb #7 (save as Document).
- **How**: OpenXML SDK is already a BFF dependency for document downloads. New service composes XML from structured output → uploads via `SpeFileStore` facade → creates Dataverse row via `IDataverseClient`. Endpoint: `POST /api/ai/summaries/{tabId}/save` with body `{ targetRecordType, targetRecordId, format: "docx" | "pdf" }`.
- **Designed for P2**: **CRITICAL** — must handle paragraph structure (sections, clauses) not just bullets / TL;DR. Spec'd day 1 with section/clause emit. P2 NDA save reuses this directly.
- **Effort**: ~2-3 weeks.

#### Multiplier D — Outbound email send via Graph MI

- **What**: New BFF endpoint `POST /api/ai/summaries/{tabId}/email`. Body: `{ to, cc, bcc, subject, body, attachOriginal, attachSummary, includeDeepLink }`. Server constructs Graph `sendMail` request with attachments + deep link + audit.
- **Why**: Verb #8 (send via email).
- **How**: `Microsoft.Graph` SDK already in BFF. Use OBO (send from user's mailbox) by default — falls back to MI sending if OBO scope denied. Deep link uses existing `sprk_document` record URL pattern. Audit via Cosmos `audit` container.
- **Designed for P2**: Same endpoint serves any future "email this" verb (NDAs, briefs, anything in workspace tab). Spec endpoint as generic "email artifact" with artifact-type discriminator.
- **Effort**: ~2-3 weeks.

### 5.3 Polish + UAT + sales-ready surface (2 weeks)

- **Save Summary action UI** on workspace tab (button + record-picker modal)
- **Email This action UI** (button + Outlook-like compose modal)
- **Routing observability**: when the dispatcher picks `summarize-vendor-contract@v1` over generic, show the user a small chip "Summarized as: Vendor Contract" so they understand
- **Empty/error states**: AI Search down → graceful "summarize works; cross-matter search unavailable"
- **Demo script + sales training**
- **End-user help doc** (1 page)
- **UAT regression** for the 8 verbs + the legal-area routing matrix

---

## 6. Folded-in deferred items (R1 / R6 / R7-backlog / sister projects)

P1 picks up the following items from prior project backlogs because they're load-bearing for the user verbs:

### 6.1 From R1 Phase 4 deferred (chat-routing-redesign-r1 MVP cut)

| Item | What | Why P1 needs it | Folded into |
|---|---|---|---|
| **Task 084 `GetFileManifestHandler`** (T2) | LLM tool: "what's in this file?" — returns sections, tables, page count, language | Verb #4 follow-up Q&A: user asks "what does the appendix contain?" without forcing a full re-read | Wave 2 (R1 4d closure) |
| **Task 083 `ListSessionFilesHandler`** (T2) | LLM tool: "what files are in this session?" | Verb #4 follow-up Q&A: "summarize the second file too" | Wave 2 (R1 4d closure) |
| **Task 087 `RetrieveMatterMemoryHandler`** (T3) | LLM tool: recall facts/decisions from prior matter sessions | Verb #3 area-awareness: matter context (e.g., "our standard cap is $5M") flows into summary | Wave 2 (R1 4d closure) |
| **Task 076 `LayeredContextCardBuilder`** | Per-file ~150-250 tok structured card per architecture §4.4 | Improves summarize prompt quality + cost. Direct cost benefit on every summary. | Wave 3 (polish) — could defer if effort budget tight |

NOT folded in: `WriteSessionMemoryHandler` (T2), promotion workflow (T3 → matter memory), enrichment pipeline (`FileClassificationService` etc.) — **the latter was misframed earlier; subject-area routing via playbook vector match makes a separate classifier unnecessary for P1.**

### 6.2 From R6 surface-completion gaps

| Item | What | Why P1 needs it | Folded into |
|---|---|---|---|
| **R6 Tier C: `update_workspace_tab` Dataverse row** | Currently silently broken — handler exists but no row | **Multiplier A** — verb #5 |
| **R6 Tier C: `send_workspace_artifact` Dataverse row** | Same — for tab creation from chat | **Multiplier A** — verb #2 (when summarize invoked from natural language vs slash) |
| **R6 Tier C: `close_workspace_tab` Dataverse row** | Same — for "close summary" | **Multiplier A** |
| **R6 Tier B: `/save-to-matter` slash** | Not wired in CommandRouter | **Polish** — verb #7 has a button equivalent, slash is demo-friendly |
| **R6 Tier E: "Add to Assistant" per-tab toggle** | Visibility control: user decides if summary content goes to LLM context for follow-up Q&A | Verb #6 intersects (in-place edit) + verb #4 (follow-up Q&A) | **Polish** |
| **R6 Audit-item-01: JSON schema validation guardrails** | Defensive — if playbook output drifts from schema, fail soft not 500 | Already partly handled by `ProjectPlaybookResultToNarrateResponse` pattern (today's daily-briefing-r4 task-031 fix). Apply same pattern to P1 summarize projection. | Built into Multiplier C design |

### 6.3 From R7-backlog (just filed this session)

| Item | What | Why P1 needs it | Folded into |
|---|---|---|---|
| **DEF-003 `OrchestratorPromptContext.MatterName` always null** | Decision: prune dead code OR wire matter name into prompts | Matter-area-aware summary benefits from matter context in prompt. Decision needs to happen IN P1. | Open question Q4 (see §13) |
| **ISS-001 user message verbatim embed** | LLM prompt construction doesn't escape `"` in user messages | If P1 endpoint constructs any prompts, apply defensive escape | Spec section in Multiplier C / D code review |
| **DEF Chat ↔ workspace write-side unification** | Chat-tool tab creation feels slower (~500ms-2s poll) vs playbook-output tab (instant SSE) | Verb #2 + verb #5 latency. Affects perceived product quality. | **Conditional escalation gate** — if P1 UAT surfaces summary-tab-mount latency as a quality complaint, escalate this into P1 scope. Otherwise stays in R7-backlog. |

### 6.4 From sister projects (hard dependencies)

| Sister project | What it delivers | P1 dependency |
|---|---|---|
| **`spaarke-ai-azure-setup-dev-r1`** (TBD owner) | AI Search restoration in dev | **HARD** — verb #4 follow-up Q&A across matter docs requires RAG retrieval. Without it, follow-up Q&A is limited to "what's in the file I just summarized" (LLM-in-context only). Sales demo works with single doc; full pitch requires AI Search. |
| **`spaarke-redis-cache-remediation-r1` (closeout)** + **`-r2` (just spec'd)** | Redis flip-on + IConnectionMultiplexer registration + tool-list cache + token cache | **SOFT** — P1 functions on in-memory CacheModule fallback for single-instance dev. Multi-tenant production scale needs r2 to land. P1 graduation criteria: "works in dev" — Redis-required scale is post-P1. |

---

## 7. Functional Requirements

### FR-1: Document summarization via slash command and natural language

- `/summarize` slash AND natural language ("summarize this", "give me a summary") both route through `IntentRerankerService` → `PlaybookDispatcher.RunPhaseBManifestAbsentAsync`.
- Vector match selects from area-specific playbooks (`summarize-vendor-contract@v1` etc.) based on file content + user message context.
- Falls back to `summarize-generic@v1` if no area playbook scores above match threshold.

### FR-2: File upload to chat session

- Inherits R1 FR-04 chat attachment policy (25 MB binary cap, MIME allow-list, single-LLM-call invariant).
- Sub-FR: support summarize from a matter document selector (not only uploaded file). UX: dropdown of matter docs from `sprk_document` query.

### FR-3: Area-aware summary output

- Vendor contract → `summarize-vendor-contract@v1` output schema
- Employment agreement → `summarize-employment-agreement@v1` output schema
- Real estate lease → `summarize-real-estate-lease@v1` output schema
- IP agreement → `summarize-ip-agreement@v1` output schema
- Other → `summarize-generic@v1` output schema (fallback)
- **Routing observability**: user sees which playbook fired (small chip on the tab)

### FR-4: Streaming structured output rendering

- Output renders progressively in workspace tab via `StructuredOutputStreamWidget` (existing) — until widget upgraded to editable variant per Multiplier B.

### FR-5: Follow-up Q&A in chat

- User can ask questions about the summarized document; LLM has document in context.
- Questions about citation get section/paragraph references when AI Search is up; same-file Q&A works without AI Search.
- `GetFileManifestHandler` + `ListSessionFilesHandler` available to LLM (R1 4d folded in).

### FR-6: Update summary via Assistant

- User can chat: "update the TL;DR" / "add indemnity terms" / "rephrase point 3".
- Existing summary tab updates IN PLACE (no new tab created).
- Mechanism: LLM calls `update_workspace_tab` tool with patched widget data.
- Depends on Multiplier A (Dataverse row seed).

### FR-7: Direct in-place editing

- User can click any field in the summary widget and edit (rich text where appropriate).
- Save-on-blur. Dispatches `workspace.widget_update` event (ADR-030 compliant).
- LLM-visible state reflects the edit (so subsequent follow-up Q&A respects user changes).
- Depends on Multiplier B (editable widget variant).

### FR-8: Save summary as Document

- User clicks "Save Summary" → record picker modal → Word/PDF format choice → save.
- Backend: `POST /api/ai/summaries/{tabId}/save` body `{ targetRecordType, targetRecordId, format }`.
- Server: `IDocumentArtifactService.GenerateAsync(structuredOutput, format)` → `SpeFileStore.UploadAsync(...)` → `IDataverseClient.CreateAsync<sprk_document>` with `regardingobjectid`.
- Audit event written to Cosmos `audit` container (ADR-015 tier-1 safe).
- Depends on Multiplier C.

### FR-9: Associate to record types

Supported record types for "associate to" picker:
- `sprk_matter` (always available when matter context in chat)
- `sprk_project`
- `sprk_workassignment`
- `sprk_invoice`
- `sprk_event`

Picker UX: tabbed by record type, search within each. Default selection: current matter (if matter in chat context).

### FR-10: Send via email

- User clicks "Email this" → email composer modal → recipient picker (Spaarke contacts + free-text external) → send.
- Backend: `POST /api/ai/summaries/{tabId}/email` body `{ to, cc, bcc, subject, body, attachOriginal, attachSummary, includeDeepLink }`.
- Server: Graph `sendMail` via OBO from user's mailbox (preferred), MI fallback for service-account scenarios.
- Subject auto-populated from matter name + doc type ("MidCorp ↔ Acme Vendor — MSA Summary").
- Body includes summary text (option) + deep link to `sprk_document` record.
- Attachments: original document (option), summary Word/PDF (option).
- Audit event written.
- Depends on Multiplier D.

### FR-11: Routing observability

- Workspace tab header shows: "Summarized as: Vendor Contract" (or the matched playbook's display name).
- Hover/click reveals match confidence + alternative candidates (for QA, hidden behind a feature flag in prod).

### FR-12: Pinned Memory influence

- User's pinned facts (R6 P7) flow into summarize prompts via existing `MatterMemoryService` activation.
- UX: small badge "Influenced by 3 pinned facts" on summary tab (clickable to see which pins applied).

### FR-13: Graceful degradation when AI Search down

- Single-document summarize works (LLM has doc in context).
- Follow-up Q&A across matter docs returns: "Cross-matter document search is temporarily unavailable; I can answer about [current file] only."
- No 500. No crash. Workspace tab still renders.

### FR-14: Audit + privilege protection

- Save summary, email send, in-place edit, LLM tool calls — all audit-logged with deterministic IDs + actor + timestamp + matter ID.
- NEVER log summary content body, attachment content, recipient email addresses in body — only counts + IDs (ADR-015 tier-1).
- Email recipient list is logged as count + hashed addresses, not plaintext.

---

## 8. Non-Functional Requirements

### NFR-1: Latency

| Operation | Target | Notes |
|---|---|---|
| Summarize a 10-page document end-to-end (first byte) | ≤4s | LLM streaming starts within 4s of user pressing Enter |
| Summarize full 10-page render complete | ≤15s | Including all structured fields populated |
| Save summary as Word + SPE upload + Dataverse create | ≤5s | Cold cache; warmer faster |
| Email send | ≤3s | Graph send + audit write |
| Direct in-place edit save-on-blur | ≤500ms | Local state update + ADR-030 event dispatch + workspace state persist |

### NFR-2: BFF publish-size ceiling

- ≤60 MB (CLAUDE.md §10 binding). Current baseline 46.67 MB. P1 budget: +5 MB cumulative (multipliers C + D combined ≤+5 MB).

### NFR-3: ADR compliance

- **ADR-001 Minimal API**: New endpoints `/api/ai/summaries/{tabId}/save`, `/api/ai/summaries/{tabId}/email`
- **ADR-008 Endpoint filters**: Tenant scoping on all new endpoints
- **ADR-010 DI minimalism**: No `IServiceProvider.GetService<T>()`
- **ADR-013 AI facade boundary**: Summarize → playbook via `IInvokePlaybookAi`; save/email don't cross AI facade (they consume the output)
- **ADR-015 Tier-1 telemetry**: No content in logs
- **ADR-018 Typed options**: No raw configuration indexers
- **ADR-019 ProblemDetails**: 404/400/503 use ProblemDetails
- **ADR-028 Auth v2**: OBO for save/email (user-initiated); MI for service-account paths
- **ADR-029 Publish hygiene**: Per-task publish-size measurement
- **ADR-030 v2 PaneEventBus**: New events on `workspace` channel are additive (`tab_saved`, `tab_emailed` if needed)
- **ADR-032 Null-Object kill-switch**: Email + save endpoints respect feature flags

### NFR-4: Accessibility (Fluent v9 + ADR-021)

- Editable widget keyboard-navigable
- Screen-reader labels on all interactive surfaces
- Dark mode conformance (rolls into R6 Tier G)
- 200% zoom + reduced motion preference respected

### NFR-5: Security

- File upload size limits enforced server-side (not just client trust)
- MIME type allow-list enforced (no executable upload)
- Save action requires user is authorized on target record (existing endpoint filters)
- Email send respects user's Graph scope (OBO confirms send permission before composing)
- No CORS wildcards (already enforced per CorsModule)

### NFR-6: Observability

- OpenTelemetry instrumentation on all new endpoints
- Counters: `summarize.dispatched`, `summarize.area_matched.{area}`, `summary.saved.{format}`, `summary.emailed`
- Tier-1 dimensions only (matter ID, user OID, decision discriminator) — never content

### NFR-7: Test coverage

- Unit tests on each new service (`IDocumentArtifactService`, email composer, area-routing assertions)
- Integration tests on save flow + email flow (with mocked Graph)
- Editable widget tests (Spaarke.UI.Components test patterns)
- UAT regression script for the 8 user verbs

---

## 9. Differentiated capabilities (Spaarke-unique within this product feature)

### 9.1 Vector-matched area awareness (vs. classifier-step competitors)

Competitors typically: detect area via a separate classifier step (model call) → route to one big prompt with area context injected.

Spaarke P1: vector-match against playbook descriptions → run the right area-specific playbook with its own output schema. **Result**: schema-stable output (no LLM drift across model versions), authorable as content (no code changes to add a 5th or 10th area playbook), and inherently composable with the `IntentRerankerService` work already shipped in R1.

Demo angle: "When we add a new document type, we ship a new JSON playbook file. We don't ship a code release."

### 9.2 First-class workspace tab as the AI artifact

Most competitors: AI output lives in chat history (Harvey/CoCounsel), or in a Word doc (Spellbook), or in a side panel (Robin).

Spaarke P1: The workspace tab IS the artifact. Edit in place. Save to system as a real Dataverse record. Email with deep link back to the record. Tab persists across chat sessions for the same matter.

Demo angle: "Summary in your matter. Not a chat scrollback. Not a Word doc you'll lose. A first-class object in your system of record."

### 9.3 Matter + SPE storage is the default (not an integration)

Competitors: integrate with customer's SharePoint/OneDrive separately. Trust friction, IT review, SSO config.

Spaarke P1: SPE container is OUR container, per matter. Save-to-matter is one click. No integration needed.

Demo angle: "Your IT team doesn't need to approve a new storage system. We're already there."

### 9.4 Pinned facts visibly influence area-aware summaries

Competitors: have pinned context but it's invisible — user can't tell if/how it affected an answer.

Spaarke P1: badge on summary tab "Influenced by N pinned facts." Click to see which pins applied. Closes the trust loop.

Demo angle: "When you tell Spaarke 'our standard indemnity is mutual,' you SEE that fact shaping every vendor contract summary. Trust through visibility."

### 9.5 Audit-by-design

Every save / email / edit is logged tier-1 safely. Privileged content never appears in logs. Audit is the substrate, not an add-on.

Demo angle: "Show your CISO the audit log. No content. Deterministic IDs. Privilege-safe by design."

---

## 10. Dependencies

### 10.1 Sister projects we depend on

| Project | Status | What we get | Hard / Soft |
|---|---|---|---|
| `spaarke-ai-azure-setup-dev-r1` (TBD) | Not started | AI Search dev restoration → RAG retrieval works | **HARD** for follow-up Q&A across matter docs |
| `spaarke-redis-cache-remediation-r2` (spec just landed) | Spec done | Redis flip-on + IConnectionMultiplexer + cache layer | **SOFT** for P1 dev graduation; HARD for prod scale |

### 10.2 Predecessor projects we inherit from

| Project | What we inherit | Constraint |
|---|---|---|
| `spaarke-ai-platform-unification-r6` (closeout) | 9 pillars — especially P2 tool registry, P4 `/summarize` via engine, P5 output schema, P6 workspace tools, P7 memory substrate | Don't regress any pillar. Pinned Memory UI stays as-is. |
| `spaarke-ai-platform-chat-routing-redesign-r1` (this branch) | Intent classification, dispatch, routing table, CapabilityRouter retirement | Don't reintroduce CapabilityRouter. Preserve FR-45 invariant at `PlaybookChatContextProvider.cs:679`. Preserve ADR-030 v2 channel union. |

### 10.3 Coordination obligations to other projects

| Other project | Obligation |
|---|---|
| Future P2 (Draft a Document) | P1's Multipliers B + C designed for paragraph structure. P2 inherits, doesn't rewrite. |
| Future P3+ | Pattern this project establishes (product-feature project framing, multiplier identification, deferred-item folding) becomes template. |
| R6 closeout (UAT) | Don't merge P1 PRs until R6 closes — minor scope guard. |

---

## 11. Out of scope (deferred to later projects)

| Item | Why deferred | Where it goes |
|---|---|---|
| Open-ended drafting (memo, brief, clause) | Separate product verb. Reuses P1 multipliers. | **P2** |
| NDA / agreement drafting | Same. | **P2** |
| In-Word AI redline surface | Strategic bet — biggest single competitive gap but biggest single effort. Separate project. | **P-bet-1** (strategic bet, not in numbered P sequence yet) |
| Outlook AI compose UX inside Outlook | P1 ships outbound send FROM Spaarke. In-Outlook compose is a separate add-in scope. | **P3 or later** |
| Teams send / Teams app | SMB segment uses Outlook; manual link works | **Deferred indefinitely** |
| E-signature integration (DocuSign/AdobeSign) | New external integration. Significant effort. Doesn't gate summarize feature. | **P3 or later** |
| Multi-document tabular review | Distinct verb. Substantial UX surface. | **P3 or later** |
| Cross-session matter memory promotion workflow | Substrate inherited; full workflow is the R1 Phase 4e deferred bundle. Not summarize-gating. | **R7 / future memory project** |
| Outside counsel work-product summary intake automation | Overlap with summarize verb but different scope (event-driven, not user-triggered). | **P-outside-counsel-1** |
| Voice / multi-modal / mobile | Not in Microsoft-native-corporate-SMB positioning. | **Deferred indefinitely** |
| Conflict-of-interest checking | Lighter need for corporate counsel; not in scope. | **Deferred** |

---

## 12. Success criteria

P1 graduates when ALL of the following are true:

### 12.1 Functional graduation
- [ ] All 8 user verbs work end-to-end in dev (verbs #1-8)
- [ ] All 14 functional requirements satisfied (FR-1 through FR-14)
- [ ] All 4 area-specific playbooks authored, deployed to spaarkedev1, tested via UAT (`summarize-vendor-contract@v1`, `summarize-employment-agreement@v1`, `summarize-real-estate-lease@v1`, `summarize-ip-agreement@v1`) + `summarize-generic@v1` refresh
- [ ] Multiplier A: 3 chat-tool Dataverse rows seeded + verified
- [ ] Multiplier B: `EditableStructuredOutputWidget` ships in `@spaarke/ai-widgets` with test coverage ≥80%
- [ ] Multiplier C: `IDocumentArtifactService` ships; Word + PDF generation work for both summary (P1) and section-structured (P2 readiness) outputs
- [ ] Multiplier D: outbound email endpoint ships; OBO + MI paths both tested

### 12.2 Quality graduation
- [ ] BFF publish size ≤ 60 MB (binding); P1 delta ≤ +5 MB cumulative
- [ ] All ADRs honored (verified via `/adr-check`)
- [ ] Code review pass via `/code-review` — 0 CRITICAL, ≤3 MAJOR (with mitigation plan or R-backlog file)
- [ ] All new endpoints have unit + integration test coverage
- [ ] UAT regression script (8 verbs + 5 area-routing matrix) passes

### 12.3 Productization graduation
- [ ] 15-minute sales demo script written + recorded
- [ ] End-user help doc (1 page) written
- [ ] Sales team trained (1 hour walkthrough)
- [ ] In-product help / tooltips for the major UI elements (Save, Email, Edit, Routing chip)
- [ ] Observability dashboard live: dispatch counts, area-match distribution, save success rate, email success rate

### 12.4 Dependency graduation
- [ ] AI Search dev restored (sister project complete) OR explicit waiver from product owner that demo-without-AI-Search is acceptable
- [ ] Redis flip-on landed in dev (sister project complete) OR explicit acceptance of in-memory CacheModule for P1 graduation

---

## 13. Resolved owner decisions (made during this design discussion)

These decisions are LOCKED for P1. Recording them here so `/design-to-spec` doesn't re-raise them and so the trail is auditable.

| Decision ID | Decision | Rationale | Date |
|---|---|---|---|
| D-1 | Adopt **product-feature project framing** (P1, P2, P3…) replacing technical-project framing (Rn) | Sales-driven prioritization; productization in-scope by definition | 2026-06-26 |
| D-2 | **Summarize first** (P1), then Drafting (P2), then Intake/Outside-counsel | Closest to done; multipliers seed P2; lowest infra risk | 2026-06-26 |
| D-3 | **Subject-area awareness via playbook vector match, NOT a separate classifier** | `PlaybookDispatcher` Phase B + `IntentRerankerService` already ship; authoring N area-specific playbooks is content-not-code | 2026-06-26 (corrected mid-discussion — initial framing had a `FileClassificationService` that turned out unnecessary) |
| D-4 | **4 area-specific playbooks + 1 generic refresh** for P1 v1 (vendor-contract, employment-agreement, real-estate-lease, IP-agreement, generic) | Covers ~80% of corporate-SMB in-house work without combinatorial explosion | 2026-06-26 |
| D-5 | **Multipliers B + C designed for paragraph structure from day 1** | Required for P2 NDA drafting reuse; trivial to spec day-1, expensive to retrofit | 2026-06-26 |
| D-6 | **Editable widget = real rich text**, NOT plain textarea | P2 paragraph editing depends on it; Fluent v9 RichTextEditor preferred, community lib fallback acceptable | 2026-06-26 |
| D-7 | **Email send from user's mailbox via OBO (preferred), MI fallback** | Trust + reply-to routing + audit attribution | 2026-06-26 |
| D-8 | **Save format default = Word (.docx)**, PDF opt-in toggle | Best for further editing; PDF for archival/sharing as user choice | 2026-06-26 |
| D-9 | **Routing observability visible to user** (chip on tab "Summarized as: Vendor Contract") | Trust signal; closes the loop on "why this output" | 2026-06-26 |
| D-10 | **Pinned Memory influence visibly badged** ("Influenced by N pinned facts") | Trust signal; differentiator vs invisible pin-context competitors | 2026-06-26 |
| D-11 | **Graceful degradation when AI Search down**: single-doc Q&A works; matter-RAG returns clear unavailability message | Sister project ETA unknown; don't block P1 demo | 2026-06-26 |
| D-12 | **Wire `OrchestratorPromptContext.MatterName`/`ActivePlaybookName`** in P1 (R7-DEF-003 closed) | Small effort; real area-aware prompt quality benefit | 2026-06-26 |
| D-13 | **Out of scope for P1**: drafting (P2), in-Word AI surface (P-bet-1), Teams send, e-signature, multi-doc tabular review, voice/mobile | Scope discipline; each is its own project | 2026-06-26 |

## 14. Applicable ADRs

Per the existing chat-routing-redesign-r1 project pattern, these ADRs are BINDING for P1. `/design-to-spec` and `/task-execute` will auto-load these as constraints.

| ADR | Why P1 touches it |
|---|---|
| **ADR-001 Minimal API** | New endpoints `/api/ai/summaries/{tabId}/save` + `/api/ai/summaries/{tabId}/email` |
| **ADR-008 Endpoint filters** | Tenant scoping on new endpoints (`DocumentAuthorizationFilter` pattern) |
| **ADR-010 DI minimalism** | No `IServiceProvider.GetService<T>()`; concrete-class registration for new services |
| **ADR-013 AI facade boundary** | Save + email endpoints CONSUME playbook output but do NOT cross `Services/Ai/PublicContracts/` (they're orchestration, not AI capability). Summarize itself goes through `IInvokePlaybookAi` facade. |
| **ADR-014 AI caching** | If any new caching introduced, follow tenant-scoped TTL pattern |
| **ADR-015 AI Data Governance — Tier 1 telemetry** | NEVER log summary content, attachment content, recipient email plaintext, or LLM-supplied widget data. Audit events use deterministic IDs + counts + decision discriminators only. |
| **ADR-018 Typed options** | New `SummarizeOptions` (or extension of existing options) for area-routing thresholds, email defaults, save defaults. No raw `IConfiguration` indexers. |
| **ADR-019 ProblemDetails** | 404 on missing tab, 400 on bad save request, 503 on AI Search unavailable, 503 on email send failure — all ProblemDetails-shaped |
| **ADR-021 Fluent v9 Design System** | Editable widget (Multiplier B) MUST use Fluent v9 components + semantic tokens; dark mode parity |
| **ADR-028 Spaarke Auth v2** | OBO for save + email (user-initiated); MI for service-account paths (deep-link service mailbox); `Mail.Send` Graph scope required for email endpoint |
| **ADR-029 BFF Publish Hygiene** | Per-task `dotnet publish` measurement; P1 cumulative budget ≤+5 MB; current baseline 46.67 MB; ceiling 60 MB |
| **ADR-030 v2 PaneEventBus** | Multiplier B dispatches `workspace.widget_update` (existing event type — additive). MAY introduce new event types `workspace.tab_saved`, `workspace.tab_emailed` (additive per ADR-030 v2 rules — does NOT require channel addition). |
| **ADR-032 BFF Null-Object Kill-Switch** | Save + email endpoints respect feature flags via P1/P2/P3 pattern; never throw on disabled feature; return 503 with ErrorCode |
| **ADR-033 Streaming chat-tool side channel** | Multiplier A handlers (update/send/close tab) respect existing side-channel pattern; no new SSE channel for P1 |

## 15. Affected code areas (file-path inventory for `/design-to-spec` Affected Areas section)

### 15.1 BFF API (`src/server/api/Sprk.Bff.Api/`)

| Path | Change type | Purpose |
|---|---|---|
| `Api/Ai/SummariesEndpoints.cs` | **NEW** | `POST /api/ai/summaries/{tabId}/save` + `POST /api/ai/summaries/{tabId}/email` |
| `Services/Ai/Summaries/IDocumentArtifactService.cs` + `DocumentArtifactService.cs` | **NEW** | OpenXML Word + PDF generation from structured output |
| `Services/Ai/Summaries/ISummaryEmailService.cs` + `SummaryEmailService.cs` | **NEW** | Graph `sendMail` composition with attachments + deep links |
| `Services/Ai/Chat/PlaybookDispatcher.cs` | MODIFY | Add observability hook (which area playbook matched + confidence) |
| `Services/Ai/Chat/OrchestratorPromptContext.cs` + `DirectOpenAiAgent.cs:235-240` + `BuildPromptContext` | MODIFY | Wire `MatterName` + `ActivePlaybookName` (D-12 closes R7-DEF-003) |
| `Models/Ai/Summaries/*.cs` | **NEW** | Request/response shapes for save + email |
| `Program.cs` + new `SummariesModule.cs` | MODIFY (1 line) + NEW | Module registration per ADR-010 |
| `Infrastructure/Telemetry/SummariesTelemetry.cs` | **NEW** | Counter + Meter definitions (Tier-1 dimensions only) |

### 15.2 Frontend — SpaarkeAi (`src/solutions/SpaarkeAi/`)

| Path | Change type | Purpose |
|---|---|---|
| `src/components/workspace/SummaryTabActions.tsx` | **NEW** | Save + Email + Edit buttons on workspace tab |
| `src/components/workspace/SaveSummaryModal.tsx` | **NEW** | Record-picker modal (matter/project/work-assignment/invoice/event) |
| `src/components/workspace/EmailSummaryModal.tsx` | **NEW** | Recipient picker + compose preview |
| `src/components/workspace/RoutingObservabilityChip.tsx` | **NEW** | "Summarized as: Vendor Contract" chip (D-9) |
| `src/components/workspace/PinnedInfluenceBadge.tsx` | **NEW** | "Influenced by N pinned facts" badge (D-10) |
| `src/services/summariesApi.ts` | **NEW** | TypeScript client for save + email endpoints |
| `src/hooks/useSaveSummary.ts` + `useEmailSummary.ts` | **NEW** | React hooks |

### 15.3 Shared widget library (`src/client/shared/Spaarke.AI.Widgets/`)

| Path | Change type | Purpose |
|---|---|---|
| `src/widgets/EditableStructuredOutputWidget/*.tsx` | **NEW** | Multiplier B — editable variant of `StructuredOutputStreamWidget` |
| `src/widgets/EditableStructuredOutputWidget/EditableRichTextSection.tsx` | **NEW** | Rich-text field variant (designed for P2 paragraph support — D-5, D-6) |
| `src/widgets/index.ts` | MODIFY | Export new widget |
| `test/EditableStructuredOutputWidget.*.test.tsx` | **NEW** | Test coverage ≥80% |

### 15.4 Dataverse + Playbooks (content, not code)

| Path | Change type | Purpose |
|---|---|---|
| `infra/dataverse/playbooks/summarize-vendor-contract-v1.json` | **NEW** | JPS playbook authoring (per `/jps-playbook-design`) |
| `infra/dataverse/playbooks/summarize-employment-agreement-v1.json` | **NEW** | " |
| `infra/dataverse/playbooks/summarize-real-estate-lease-v1.json` | **NEW** | " |
| `infra/dataverse/playbooks/summarize-ip-agreement-v1.json` | **NEW** | " |
| `infra/dataverse/playbooks/summarize-generic-v1.json` | MODIFY | Refresh existing generic per D-4 |
| `infra/dataverse/sprk_playbookconsumer/*.json` | **NEW** (5 rows) | Routing rows wiring `consumertype="document-summarize"` + `consumercode={area}` → playbook GUIDs |
| `infra/dataverse/sprk_analysistool/*.json` | **NEW** (3 rows for Multiplier A) | `update_workspace_tab`, `send_workspace_artifact`, `close_workspace_tab` (folds in R6 Tier C) |

### 15.5 Tests

| Path | Change type | Purpose |
|---|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Summaries/*.cs` | **NEW** | Unit tests for `DocumentArtifactService`, `SummaryEmailService`, dispatcher area-routing assertions |
| `tests/integration/Sprk.Bff.Api.Tests/Api/Ai/SummariesEndpointsTests.cs` | **NEW** | Integration tests with mocked Graph + mocked Dataverse |
| `tests/unit/Spaarke.AI.Widgets.Tests/EditableStructuredOutputWidget.test.tsx` | **NEW** | Widget test pattern |

### 15.6 Documentation

| Path | Change type | Purpose |
|---|---|---|
| `docs/guides/P1-SUMMARIZE-USER-GUIDE.md` | **NEW** | 1-page end-user help doc (graduation criterion) |
| `docs/guides/P1-SUMMARIZE-DEMO-SCRIPT.md` | **NEW** | 15-minute sales demo script (Appendix promoted to file) |
| `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` | MODIFY | Add summary tab as canonical example of structured-output-with-actions |
| `.claude/CHANGELOG.md` | MODIFY | Entry per CLAUDE.md procedure-surface change rule |

## 16. Technical constraints (distinct from NFRs)

These are HARD rules `/task-execute` will enforce.

- **MUST use `IInvokePlaybookAi` facade** for any new server-side playbook invocation; NEVER directly inject `IOpenAiClient` / `IPlaybookService` (ADR-013)
- **MUST use `IConsumerRoutingService.ResolveAsync` with compile-time `ConsumerTypes` constants** (no literal strings) for new routing entries — follow the daily-briefing-r4 task-031 pattern
- **MUST NOT** introduce a 6th PaneEventBus channel; new event types are additive on `workspace` channel (ADR-030 v2)
- **MUST NOT** regress FR-45 invariant at `PlaybookChatContextProvider.cs:679` (R1 binding)
- **MUST NOT** reintroduce `CapabilityRouter` references (R1 Phase 7 retired it)
- **MUST** measure publish size on every BFF-touching task; budget ≤+5 MB cumulative for P1
- **MUST** apply ADR-015 Tier-1 telemetry safety on every audit + telemetry write — deterministic IDs + counts only
- **MUST** use Fluent v9 + ADR-021 semantic tokens for all new UI (no inline colors, no fixed pixel values for theme-affected properties)
- **MUST** use OBO for user-initiated email send (Mail.Send scope); MI fallback only for service-account paths
- **MUST** clear Vite cache before every SpaarkeAi build (`rm -rf dist/ node_modules/.vite/ .vite/`) — code-page-deploy skill rule
- **MUST NOT** use `npm ci` for Vite solutions (CLAUDE.md §12)
- **MUST** include `<knowledge><files>` with applicable ADRs in every POML task (per `task-create` Step 3.5)
- **MUST** declare rigor level at task start (FULL / STANDARD / MINIMAL per CLAUDE.md §8)

## 17. Assumptions

P1 assumes:

- **AI Search restoration** lands before P1 graduation (sister project `spaarke-ai-azure-setup-dev-r1` — TBD). If not, D-11 graceful degradation applies; matter-RAG demo deferred until restoration.
- **Redis flip-on** is acceptable to defer past P1 dev graduation; multi-instance prod scale requires sister project `spaarke-redis-cache-remediation-r2`.
- **R6 closeout completes** during P1 design + early execution; P1 PRs do NOT merge to master until R6 is closed (per R7 backlog coordination rule).
- **R1 (current branch) closeout** completes before P1 PRs land — task 146 UAT regression + task 150 wrap-up.
- **JPS playbook framework + scope catalog** are stable through P1 — no breaking changes to playbook schema during execution.
- **Spaarke Auth v2 (ADR-028)** is fully provisioned in spaarkedev1 — MI Graph permission grants in place, including `Mail.Send` for email feature (verify per `auth-deployment-setup.md` §5 before P1 wave 2).
- **Existing `sprk_document` polymorphic `regardingobjectid`** supports all 5 target record types (matter, project, work-assignment, invoice, event) — to be verified in design review.
- **OpenXML SDK** is already a BFF dependency — Multiplier C adds no new NuGet (verify; if false, +~3 MB publish impact).
- **Fluent v9 RichTextEditor** is production-ready at our quality bar OR a community lib (TipTap / Slate) integrates cleanly with semantic tokens — spike required in P1 wave 1.

## 18. Risk register

Top risks ranked by composite (probability × impact):

| # | Risk | Probability | Impact | Mitigation |
|---|---|---|---|---|
| 1 | AI Search sister project slips → P1 demo can't show matter-RAG | Medium | High | D-11 graceful degradation; demo with single-doc Q&A as primary, matter-RAG as v1.1 |
| 2 | Fluent v9 RichTextEditor doesn't exist / not production-ready → Multiplier B technology choice forced | Medium | High | Spike in P1 wave 1; community lib (TipTap / Slate) fallback list pre-evaluated |
| 3 | OpenXML doc generation produces ugly output for complex sections | Medium | Medium | Style guide for templated XML; reference customer-approved Word template |
| 4 | Graph `Mail.Send` scope provisioning blocked by tenant policy | Low | High | Verify pre-P1 wave 2; MI fallback path exists |
| 5 | Area-playbook vector match doesn't crisply select right area (vendor MSA borderlines employment agreement) | Medium | Medium | Tunable threshold; routing-observability chip lets user see decision; per-document-type training prompts |
| 6 | Editable widget conflicts with structured-streaming progress (race condition between stream update + user edit) | Medium | Medium | Lock edits during streaming; resume editable mode on stream-complete event |
| 7 | Save action conflicts (concurrent user + AI updates of same tab) | Low | Medium | R6 Q8 USER WINS pattern; conflict logging |
| 8 | Email send abuse (spam vector) | Low | High | Rate limit per user; audit + alerting on >N sends/hour |
| 9 | Publish size delta exceeds +5 MB budget (OpenXML or PDF lib heavier than expected) | Low | Medium | Per-task ADR-029 measurement; PDF generation can defer to v1.1 if needed |
| 10 | Multiplier B widget scope creep absorbs Wave 2 (1-2 month component) | Medium | Medium | Strict scope: TL;DR + Summary + Key Takeaways + Entities sections only; defer extensibility hooks to P2 |

## 19. Suggested work packages (input for `/project-pipeline` plan generation)

Suggested decomposition. `/project-pipeline` may revise.

| Wave | Name | Tasks | Effort | Critical-path gate |
|---|---|---|---|---|
| **W0** | Pre-flight | (a) Verify Graph `Mail.Send` scope provisioning; (b) Spike Fluent v9 RichTextEditor or pick lib; (c) Verify OpenXML SDK already in BFF deps; (d) Verify `sprk_document.regardingobjectid` supports all 5 record types | ~2-3 days | Gates W1+ design freeze |
| **W1** | Multiplier A + R6 surface fixes | Seed 3 chat-tool Dataverse rows; verify dispatch end-to-end | 1-2 days | Quick win; unblocks verb #5 |
| **W2** | Playbook authoring (parallel) | Author 4 area-specific summarize playbooks via `/jps-playbook-design`; deploy to spaarkedev1; UAT each | ~3 weeks (parallel-safe) | Gates verb #2-3 demo |
| **W3** | Multiplier C — Doc generation + save endpoint | `IDocumentArtifactService`, OpenXML composition, SPE upload, Dataverse create, `POST /save` endpoint | 2-3 weeks | Gates verb #7 |
| **W4** | Multiplier D — Email send | `ISummaryEmailService`, Graph send via OBO + MI fallback, `POST /email` endpoint, audit | 2-3 weeks (parallel with W3) | Gates verb #8 |
| **W5** | Multiplier B — Editable widget | `EditableStructuredOutputWidget` + `EditableRichTextSection` + Spaarke.UI.Components integration + tests | 3-4 weeks (parallel with W3+W4) | Gates verb #6 |
| **W6** | R1 Phase 4d folded handlers | `ListSessionFiles`, `GetFileManifest`, `RetrieveMatterMemory` handlers + Dataverse rows | 1-2 weeks | Gates verb #4 follow-up Q&A breadth |
| **W7** | Frontend wiring | `SummaryTabActions`, `SaveSummaryModal`, `EmailSummaryModal`, `RoutingObservabilityChip`, `PinnedInfluenceBadge`; React hooks | 2-3 weeks (after W3 + W4 endpoints stable) | Gates demo flow |
| **W8** | R7-DEF-003 close | Wire `OrchestratorPromptContext.MatterName` + `ActivePlaybookName` | <1 week | Quality polish for area-aware prompts |
| **W9** | Polish | Routing observability chip + Pinned influence badge + graceful AI-Search-down + audit dashboard | 1-2 weeks | Demo-quality polish |
| **W10** | Quality gates | `/code-review` + `/adr-check` + publish-size verification + ADR-021 dark-mode pass | 1 week | Gates UAT |
| **W11** | UAT regression | Run UAT script for 8 verbs + 5 area-routing matrix; fix-forward | 1-2 weeks | Gates graduation |
| **W12** | Productization | Demo script + recording; user guide; sales training (1hr session); observability dashboard live | 1 week | Gates project close |
| **W13** | Project wrap-up | `/repo-cleanup`, lessons-learned for P2 template, archive | <1 week | — |

**Total**: ~6-8 weeks if W2/W3/W4/W5 run with substantial parallelism. Sequential bound: ~10-12 weeks.

**Critical path**: W0 → W2 (playbook authoring) → W7 (frontend wiring depends on endpoints) → W11 (UAT) → W12 → W13.

## 20. Open owner questions (need decisions before pipeline)

> **Status as of 2026-06-26**: All design-time questions originally surfaced during the product-led discussion have been resolved into §13 (D-1 through D-13). The Q-list below is preserved as a discussion log — each Q now maps to a resolved D. New questions surfaced during execution should be recorded in `notes/` (per CLAUDE.md proactive-checkpoint pattern), not back-patched into this design.

### Q1: Subject-area routing fallback behavior → resolved by D-1/D-4 (always fall back to `summarize-generic@v1`)

When no area playbook scores above match threshold, do we:
- (a) Always fall back to `summarize-generic@v1` (recommended)
- (b) Offer the user a manual area picker
- (c) Show no fallback and prompt user to re-phrase

**Recommendation: (a)** — fall back transparently; show routing chip so user knows.

### Q2: Save-summary format default

For verb #7, default format:
- (a) Word (.docx) — best for further editing
- (b) PDF — best for sharing
- (c) Both — uploads two `sprk_document` rows
- (d) Let user choose, no default

**Recommendation: (a)** Word, with Both as opt-in toggle.

### Q3: Email send identity

For verb #8 send:
- (a) Always send from user's mailbox via OBO (requires `Mail.Send` scope)
- (b) Send from MI service mailbox (`do-not-reply@spaarke.com` or similar)
- (c) User chooses per email

**Recommendation: (a)** OBO default for trust + Reply-To routing; (b) fallback if user's OBO scope denies.

### Q4: `OrchestratorPromptContext.MatterName`/`ActivePlaybookName` dead fields (R7-DEF-003)

These fields are always null today. P1 area-aware summarize would benefit from matter name + active playbook name in prompt construction. Two options:
- (a) Wire them in `AgentRequest` → `BuildPromptContext` pathway (~1 day in P1 scope)
- (b) Leave deferred to R7; document workaround (template parameter passing in playbook)

**Recommendation: (a)** wire it in P1 — small effort, real prompt-quality benefit.

### Q5: Editable widget — rich text library

Multiplier B needs rich text. Options:
- (a) Fluent v9 `RichTextEditor` if it exists at quality level we need
- (b) Existing community lib (TipTap, Slate, ProseMirror)
- (c) Plain `Textarea` per field (degrades P2 paragraph-edit experience)

**Recommendation: investigate (a) first, (b) as fallback. Do NOT pick (c)** — P2 paragraph editing depends on this being real rich text.

### Q6: Save-to-record types — which entities?

Currently spec'd: matter, project, work-assignment, invoice, event. Should we also support:
- (a) `sprk_contract` (if exists as separate entity from matter)
- (b) `sprk_legalentity` (party-level association)
- (c) Email thread association (for "this summary is for the Q3 vendor review thread")

**Recommendation: spec'd list for v1.** Add others in v2 based on user feedback. Don't over-scope.

### Q7: Routing observability — how visible?

- (a) Always show "Summarized as: Vendor Contract" chip (user sees the area)
- (b) Show only in QA/internal mode
- (c) Show on hover only

**Recommendation: (a)** — visibility is a trust signal. Hide details (confidence scores) behind QA flag.

### Q8: Pinned Memory influence badge

- (a) Always show when pins applied
- (b) Show only first time per session
- (c) Hide; only available via "show provenance" action

**Recommendation: (a)** — visible trust loop.

### Q9: P1 dependency on AI Search restoration — block or proceed?

- (a) Block P1 until sister project lands
- (b) Proceed; ship single-doc Q&A; matter-RAG Q&A is post-restoration
- (c) Decide at Wave 3 based on sister project ETA

**Recommendation: (b)** — single-doc Q&A is the headline demo. Matter-RAG follow-up is value-add. Don't block on infra.

---

## 21. Appendix — 15-minute sales demo script (draft)

*To be refined during productization phase. This is the rough flow.*

**Setup (1 min)**: "Sarah is in-house counsel at MidCorp. She just received a new MSA from Acme Vendor. Watch how Spaarke handles this."

**Open SpaarkeAi from the matter (1 min)**: Click MidCorp ↔ Acme Vendor matter → Open SpaarkeAi. Three-pane shell loads. Chat ready.

**Upload + Summarize (2 min)**: Drop MSA PDF in chat. Type "summarize this." Workspace tab opens. Show: "Summarized as: Vendor Contract" routing chip. Watch structured output stream in: parties, payment terms, indemnity, liability cap (highlight: cap is $10M), term, renewal, key risks. Total time ~8 seconds.

**Pinned Memory influence (1 min)**: Click "Influenced by 2 pinned facts" badge. Show: "MidCorp's standard liability cap is $5M (pinned by Sarah, 3 months ago)" and "Mutual indemnification required for vendors above $1M ARR (pinned by GC)." Show how summary's "Key Risks" section flagged "Cap is 2x our standard."

**Follow-up Q&A (2 min)**: "What does the termination clause say?" → answers with § citation. "Is there a non-compete?" → answers. "What's the renewal mechanism?" → answers.

**Update via Assistant (1 min)**: "Add the audit rights to the summary." Tab updates in place. Show indicator.

**Direct edit (1 min)**: Click TL;DR. Edit. "Sarah's review notes: cap needs negotiation." Save-on-blur.

**Save Summary (1 min)**: Click Save Summary. Picker: defaults to MidCorp ↔ Acme Vendor matter. Word format. Click Save. Confirmation with link to sprk_document record. Click → opens in SharePoint Embedded.

**Email This (2 min)**: Click Email this. Recipients: CFO + paralegal. Subject auto-filled. Body: summary text + deep link. Attachments: original MSA + summary Word doc. Send.

**Audit walkthrough (1 min)**: Show audit log. Every action: timestamp, user OID, matter ID, action type. No content. "This is what your CISO sees."

**Wrap (1 min)**: "Total time for Sarah: 90 seconds for what used to be a 30-minute review-and-summarize cycle. Built on your Microsoft stack. Audit-by-design. And we just shipped: a vendor contract summary. Tomorrow: an employment agreement. We add document types as content, not code."

---

## 22. Project lifecycle expectations

- **Design** (this document) — review + approve
- **Spec** (`spec.md`) — generated via `/design-to-spec` skill once design approved
- **Plan** (`plan.md`) — generated via `/project-pipeline` after spec
- **Tasks** (`tasks/*.poml`) — generated via `/task-create` from plan
- **Execute** — `/task-execute` per task; CLAUDE.md productization rigor levels apply
- **Graduate** — per §12 success criteria
- **Close out** — wrap-up task; portfolio status updated; lessons captured for P2/P3 templates

---

## Document changelog

| Date | Change | Reviewer |
|---|---|---|
| 2026-06-26 | Initial draft from product-led discussion + competitive research + deferred-items survey | Author |
