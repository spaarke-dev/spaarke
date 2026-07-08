# Spaarke Compose R2 — Design (Working Document)

> **Status**: DRAFT — refinement document. Not yet a committed spec.
> **Codename**: Spaarke Compose (continuing from R1)
> **Positioning**: AI-native legal drafting workspace
> **Project ID**: `spaarkeai-compose-r2`
> **R2 Theme**: **The differentiation layer activates.** R1 shipped the workspace foundation; R2 makes it AI-native and Word-interoperable. Compose now does the work the foundation was built for.
> **Owner**: Ralph Schroeder
> **Last updated**: 2026-07-03 (revised for R7 W12 LinearConsumers + R1 AI dispatch retirement)
> **R1 reference**: [`../spaarkeai-compose-r1/design.md`](../spaarkeai-compose-r1/design.md) + [`../spaarkeai-compose-r1/spec.md`](../spaarkeai-compose-r1/spec.md)
> **Platform reference**: [`docs/architecture/AI-ARCHITECTURE.md`](../../docs/architecture/AI-ARCHITECTURE.md) (redesign-r1 canonical output — the 4-tier platform R2 consumes)
>
> ### Revision log
>
> **2026-07-03** — Design revised to reflect two platform changes landed after the original 2026-06-29 draft:
> 1. **R7 W12 LinearConsumers merged to master** (`Services/Ai/LinearConsumers/` — `IActionResolver` + `IActionRunner` + `IDocumentTextSource` + `sprk_analysisaction` table). This is now the canonical single-shot document-scoped AI action path, superseding `IConsumerRoutingService` + `sprk_playbookconsumer` + `POST /api/compose/action/{consumerType}` for one-shot actions.
> 2. **Compose R1 AI dispatch retired** (PR #544, 2026-07-02). Deleted: `ComposeEndpoints.DispatchAction` endpoint (~700 LOC); `IDocxTextExtractor` (R7 `ITextExtractor` covers `.docx`); `ComposeDocumentService` (superseded by `SpeFileStore.DownloadFileAsUserAsync` + new `ReplaceFileContentAsUserAsync`); `ComposeSessionService` (rebind logic inlined into `ComposeService`); `ConsumerTypes.ComposeSummarize` constant.
>
> Consequence: R2's three inline-toolbar actions (Explain / Compare / Draft Alternative) dispatch through the **Assistant pane via PaneEventBus → chat → LinearConsumer**, not via a new Compose-specific BFF endpoint. Every reference to `IConsumerRoutingService`, `IInvokePlaybookAi` (for one-shot doc actions), `sprk_playbookconsumer`, `POST /api/compose/action/*`, or `ConsumerTypes.*` in the AI-dispatch context has been re-scoped in this revision.
>
> Non-AI Word-native work (§6.2 DOCX shuttle, §12 push/pull-annotations endpoints, §8 session memory) is unaffected.

This document leads with **user features** — what users actually do — and then maps each feature to the technical architecture, the three-pane choreography, and the playbook/consumer-routing resources that power it. Design follows from value, not the other way around.

---

## 1. Product Statement

R2 turns Compose from a foundation editor into an **AI-native legal drafting workspace with Word-native interoperability**. Three differentiation pillars become real in this release:

1. **AI-coordinated three-pane experience** — Workspace + Assistant + Context act as one tool, not three. Selecting a clause lights up Context with playbook matches; Assistant offers actions on the same selection; results flow back into Workspace as track-change suggestions.
2. **Word-native interoperability** — AI suggestions and comments travel to Word as native `<w:ins>` / `<w:del>` / `<w:comment>` elements via Microsoft Open XML SDK. Round-trip back when Word saves.
3. **Memory continuity** — anchored annotations, action log, and derived insights persist across Word handoffs and matter sessions via the existing ChatSession three-tier infrastructure (R1 foundation).

Competitive position after R2: **"Highlight any clause. Get an explanation. Compare it to your firm's playbook. Draft an alternative. Push back to Word as native track changes. Spaarke remembers what you and the AI decided together — across this session, across your Word visits, across the entire matter lifecycle."** Each sentence is backed by a specific R2 feature.

---

## 2. R2 User Features — What Users Actually Do

Each feature defined by: user story, three-pane choreography, playbook used, resources hooked into.

### 2.0 Cross-Cutting UX Patterns (binding across all R2 features)

These patterns apply across all R2 user features. Each feature implements them; we don't ship a feature that violates them.

#### Inline AI toolbar on selection (Workspace pane)

When the user highlights any text in the Compose editor, a **discrete floating AI toolbar** appears near the selection. Standard buttons: **"Explain"**, **"Compare to playbook"**, **"Draft alternative"**, **"More actions…"** (overflow menu).

- Toolbar disappears when selection is cleared
- Toolbar position auto-adjusts to stay in viewport (above selection by default; below if no room above)
- Single-tap interaction — no modal disruption
- Implementation: **TipTap `BubbleMenu` extension** (MIT/OSS, ships with TipTap core — no commercial license)
- Toolbar contents are extensible: future R3+ features can register additional actions into the menu

This is the **primary discovery surface for AI features**. A user discovers what Compose can do by selecting text. Hidden in a top toolbar = invisible feature.

#### Provenance always visible (Context pane)

**Every AI-generated recommendation, suggestion, or annotation MUST surface its sources** in the Context pane. The Spaarke principle: AI recommendations are auditable.

Sources surfaced (when available):
- **Playbook entry** that matched (which `sprk_analysisplaybook` entry, which clause within it)
- **Golden reference** from the `spaarke-rag-references` AI Search index that informed the answer
- **Precedent matter / clause / document** cited
- **Prior session decision** the AI built on
- **LLM reasoning trace** (in audit-detail mode) — for compliance review

Sources are:
- **Clickable** (navigate to the source artifact in Spaarke)
- **Citable** (drag into the doc as an inline citation — a Compose annotation type)
- **Persistent** (saved to ChatSession derived insights — survives Word handoff)

This pattern is BINDING. Any AI action that produces a recommendation without source surfacing is a design defect. Reason: legal users will not trust AI recommendations without provenance; trust is the moat.

#### Tool descriptions surface as user hints

Per adeu's "tool descriptions ARE the prompt" insight, the same descriptions that prime the LLM also surface as **user-visible tooltips** on toolbar buttons and Assistant-pane affordances. Author the description once; it serves both the LLM behavioral prompt AND the user-facing help text. Cuts content surface area in half and keeps user/LLM understanding consistent.

---

### 2.1 Explain This Clause

**User story**: User selects a clause they don't fully understand. **Inline AI toolbar appears near the selection** (per §2.0); user clicks "Explain". Assistant returns a plain-language explanation with relevant legal context.

**Three-pane choreography**:
- **Workspace**: Selection highlighted; persistent annotation marker added (clickable to replay explanation); inline toolbar dismisses after click. **Toolbar click dispatches a PaneEventBus event** to the Assistant pane with `{actionKey, selection, jpsScopePayload}`.
- **Assistant**: Consumes the PaneEventBus event; runs the linear-consumer action through R7 W12's `IActionResolver` + `IActionRunner` + `IDocumentTextSource` path; streams the response into the Assistant's chat surface as an assistant message; offers follow-up actions ("Compare to playbook?", "Draft alternative?")
- **Context**: **Sources surfaced (per §2.0 provenance pattern)** — related precedent clauses from matter; relevant golden references from `spaarke-rag-references` index; click-to-navigate to source

**AnalysisAction row**: `compose-explain-clause` — NEW `sprk_analysisaction` record (R2 deliverable; carries SystemPrompt + OutputSchemaJson + Temperature + ModelDeploymentId)
**JPS scope**: `compose-selection` (defined in R1)
**Dispatch path**: PaneEventBus (`conversation` channel, discriminant `compose_action_request`) → Assistant pane → `IActionResolver.ResolveAsync("compose-explain-clause")` → `IActionRunner.RunAsync(action, documentText, context)` → streaming assistant message. **NO Compose-specific BFF endpoint** — dispatch goes through the Assistant, same code path as any other chat-driven linear consumer.

**Why it matters**: Lowest-effort AI action; universal use; demonstrates Workspace → Assistant flow cleanly.

---

### 2.2 Compare to Playbook

**User story**: User selects a clause (e.g., indemnification, governing law). **Inline AI toolbar appears** (per §2.0); user clicks "Compare to playbook". Assistant compares the selection against firm/matter playbook clauses; Context pane lights up with matches, deviations, and risk scores.

**Three-pane choreography**:
- **Workspace**: Selection highlighted; risk-level annotation marker added; inline toolbar dismisses after click. **Toolbar click dispatches a PaneEventBus event** with `{actionKey="compose-compare-to-playbook", selection, matterId}`.
- **Assistant**: Consumes the PaneEventBus event; dispatches through the linear-consumer path; streams analysis into chat surface; offers "Replace with standard?" or "Negotiate this?" follow-ups
- **Context**: **Lights up with full source attribution (per §2.0)** — exact playbook entry that matched (click to navigate); clause text comparison side-by-side; deviation summary; risk score with rationale; relevant golden references; prior negotiation history if available — all clickable sources

**AnalysisAction row**: `compose-compare-to-playbook` — NEW `sprk_analysisaction` record (R2 deliverable)
**JPS scope**: `compose-selection` + matter context (existing)
**Dispatch path**: PaneEventBus → Assistant → LinearConsumer (same as §2.1)
**Resources hooked into**:
- Matter playbook library (existing `sprk_analysisplaybook` entity — read as reference data by the action's LLM invocation, NOT as a routing target)
- Context pane section: new `compose-playbook-comparison` registration (Context-pane component registry)
- Optional: precedent doc retrieval (R3+ — defer)

**Why it matters**: **The Spaarke-exclusive flow.** Competitors don't have JPS playbooks as a first-class concept. This is where the three-pane coordination shines.

---

### 2.3 Draft Alternative

**User story**: User selects clause text. **Inline AI toolbar appears** (per §2.0); user clicks "Draft alternative". Assistant proposes alternative language; the suggestion appears in Workspace as a pending track-change (highlighted insertion + deletion). User accepts (becomes part of doc state) or rejects (suggestion disappears).

**Three-pane choreography**:
- **Workspace**: Selection becomes a pending **insertion/deletion pair** rendered as track-change marks; inline toolbar dismisses; accept/reject mini-controls appear inline near the suggestion. Toolbar click dispatches PaneEventBus event to Assistant.
- **Assistant**: Consumes event; dispatches through LinearConsumer path; streams alternative text with rationale; on completion, dispatches an **Assistant → Workspace** PaneEventBus event with the structured edit payload (see §6.1) so the Workspace can render the pending track-change; offers "Refine further?" follow-up
- **Context**: **Full source attribution (per §2.0)** — exact playbook clause that informed the draft; golden references / precedent matters cited; LLM rationale trace; all clickable + citable (drag a source into the doc as an inline citation if accepting)

**AnalysisAction row**: `compose-draft-alternative` — NEW `sprk_analysisaction` record (R2 deliverable). OutputSchemaJson enforces the structured edit-payload shape (`target_text` / `new_text` / `comment` per adeu Pattern §6.1) so the LLM's response is directly consumable by the Workspace edit applicator.
**JPS scope**: `compose-selection` (defined in R1)
**Dispatch path**: PaneEventBus → Assistant → LinearConsumer → PaneEventBus (Assistant → Workspace edit-payload)
**Critical UX detail**: Suggestion is **pending** — not auto-applied. User explicitly accepts. Aligns with adeu's pattern: LLM proposes, user controls.

**Why it matters**: Demonstrates the full Workspace ↔ Assistant ↔ Workspace round-trip. Provenance trail is Spaarke-unique.

---

### 2.4 Push Annotations to Word

**User story**: User has Compose-native annotations (AI suggestions accepted as pending track-changes, user-added comments). Toolbar → "Push to Word" (or implicit on Save). Word for Web / Desktop now shows annotations natively — `<w:comment>` for comments, `<w:ins>` / `<w:del>` for track changes, with proper author/timestamp metadata.

**Three-pane choreography**:
- **Workspace**: Confirmation banner before push; shows what will appear in Word vs what stays in Compose only
- **Assistant**: Confirms action; explains what's being pushed (counts of comments / track changes)
- **Context**: Shows action log entry (timestamped, reproducible)

**No new playbook** — purely deterministic operation. Uses Open XML SDK in BFF.

**Resources hooked into**:
- Microsoft Open XML SDK 3.x ([`DocumentFormat.OpenXml`](https://github.com/dotnet/Open-XML-SDK))
- Codeuctivity.OpenXmlPowerTools (MIT fork, for diff/redline support)
- SPE check-out / check-in (existing R1 plumbing)
- SPE write with `If-Match` etag (existing R1 plumbing extended)

**Why it matters**: **Competitive parity.** Without this, every AI suggestion is locked inside Compose. Word add-ins (Harvey, Spellbook) do this natively; we must too.

---

### 2.5 Return from Word

**User story**: User opens Compose doc in Word, makes edits, saves. Hours later, returns to Compose. Compose detects the new SPE version, reloads doc, **re-anchors prior Compose annotations** to the updated text, surfaces a banner: "Document updated in Word — 4 annotations re-anchored, 1 needs your review."

**Three-pane choreography**:
- **Workspace**: Banner with summary of changes; re-anchored annotations visible inline; ambiguous anchors flagged for review
- **Assistant**: Offers "Walk through the changes?" guided review; ready to help with conflict resolution
- **Context**: Shows diff summary; lists comments added in Word; surfaces structural changes

**No new playbook for detection itself** — uses SPE webhooks + Open XML SDK reader.
**Optional playbook**: `compose-summarize-word-changes` (R2 stretch) — uses LLM to summarize what changed in human-friendly terms.

**Resources hooked into**:
- SPE webhook subscription (`drives/{containerId}/root`, `changeType: "updated"`, 4230-min lifespan; renewal cron)
- SPE delta query (`/drives/{id}/root/delta`) to enumerate changed driveItems
- Open XML SDK parser for incoming `<w:comment>`, `<w:ins>`, `<w:del>` extraction
- ChatSession persistence (existing) for re-anchoring metadata

**Why it matters**: **The memory continuity moat.** Competitors lose all context when the user closes Word. Compose remembers.

---

### 2.6 Session Memory — "Pick Up Where You Left Off"

**User story**: User opens a doc they worked on last week. Compose surfaces prior sessions ("3 prior sessions, last 2 days ago"). User chooses to bring forward; prior session's compacted summary + key decisions + anchored annotations appear in Context. Assistant has the prior conversation context immediately.

**Three-pane choreography**:
- **Workspace**: Doc opens with prior annotations intact (within drift tolerance)
- **Assistant**: "Welcome back. Last session you compared clause 4.2 to the IP playbook and drafted an alternative for clause 7. Continue?"
- **Context**: Prior derived insights (defined terms, playbook deviations, decision log) restored

**No new playbook** — uses ChatSession three-tier persistence (R1 foundation; R2 fills with rich content).

**Resources hooked into**:
- ChatSession (R1 — existing)
- Compaction (R1 — existing, 15-msg LLM summarization)
- Archival (R1 — existing, 50-msg threshold)
- **R2 additions**: anchored annotation persistence in ChatSession payload; action log; derived-insight pointers

**Why it matters**: **The differentiator we explicitly designed for in R1.** R2 fills it with content.

---

### 2.7 (Stretch) Document Q&A

**User story**: User asks Assistant "what's the indemnification cap?" — Assistant answers from the document content without the user needing to find the clause.

**Three-pane choreography**:
- **Workspace**: Answer references appear as ephemeral highlights ("found in §7.3")
- **Assistant**: Direct answer with citation
- **Context**: Section navigated to; relevant playbook entry surfaced

**Playbook**: existing `Document Summary` (id `47686eb1-9916-f111-8343-7c1e520aa4df`, R1 wired) plus possibly a new `compose-document-qa` playbook
**JPS scope**: `compose-document` (defined in R1)

**Why it matters**: Lowest-friction AI feature. Stretch because Q&A benefits from semantic retrieval over the document, which R2 may or may not include depending on retrieval-infrastructure availability.

---

## 3. Three-Pane Coordination — From Wire-Only to Activated

R1 wired the six coordinated flows with stub receivers. R2 fills them with real behavior:

| Flow | R1 status | R2 activates |
|---|---|---|
| **Workspace → Context** | Wire only | Selection → Context surfaces playbook matches, precedent, prior negotiation history; all entries source-attributed (per §2.0 provenance) |
| **Workspace → Assistant** | Wire only | Selection → **inline AI toolbar appears** (per §2.0); click dispatches PaneEventBus `compose_action_request` on `conversation` channel with `{actionKey, selection, jpsScopePayload}`. Assistant consumes + routes to R7 LinearConsumer path. NO Compose-specific BFF endpoint. |
| **Context → Workspace** | Wire only | Drag precedent clause / golden reference from Context → drops into editor as inline citation; click on Context entry navigates Workspace |
| **Context → Assistant** | Wire only | "Use this precedent" → Assistant takes Context entry as input to next action |
| **Assistant → Workspace** | Wire only | AI draft (from `compose-draft-alternative` action's structured edit-payload output — §2.3) inserts into editor as pending track-change **with provenance link** (clickable to source per §2.0). Dispatched via PaneEventBus `compose_edit_apply_request` on `workspace` channel. |
| **Assistant → Context** | Wire only | AI-derived insight persists to session memory; surfaces in Context **with full source attribution** (AnalysisAction row, playbook entry, golden reference, precedent — clickable) |

**Binding architectural rule**: every R2 feature lights up at least two of these six flows. Features that don't are flagged for redesign — three-pane is the differentiator, not an optional layer.

**AI-dispatch invariant (added in this revision)**: no R2 feature introduces a new Compose-specific AI dispatch endpoint. All AI actions flow: **Workspace toolbar → PaneEventBus → Assistant pane → R7 LinearConsumer (`IActionResolver` + `IActionRunner`)**. This preserves the "Assistant is the single AI-response surface" principle and avoids reintroducing the parallel path retired in Compose R1 PR #544.

---

## 4. Supersession Map (carry forward + amend)

| Retired / superseded | Current | Project relationship |
|---|---|---|
| (from R1) `AnalysisWorkspace` solution | SpaarkeAi three-pane shell | Compose builds ON the shell |
| (from R1) `SprkChat` | `ConversationPane` | R2 extends `ConversationPane` with new playbook integrations |
| (from R1) `sprk_analysis.sprk_chathistory` | `ChatSession` three-tier (Redis/Cosmos/Dataverse) | R2 extends `ChatSession` payload with rich content |
| **(amended in R2)** R1 spec.md non-goal "Tracked changes round-trip with Word — never" | **R2 ships it** via Open XML SDK in BFF | R1 non-goal was over-pruned; R2 amends |
| **(amended in R2)** R1 spec.md non-goal "Comments stored as `<w:comment>` — never" | **R2 ships it** via Open XML SDK in BFF | R1 non-goal was over-pruned; R2 amends |

---

## 5. R1 → R2 Progression — What Was Wired, What R2 Fills In

| R1 wired (foundation) | R2 fills in (content + features) |
|---|---|
| TipTap OOB editor shell with three-pane mount | + Custom marks: `insertion`, `deletion`, `commentAnchor` (R2 schema additions) |
| `ChatSession` binding via `DocumentId` | + Rich payload: anchored annotations, action log, derived-insight pointers |
| Two JPS scopes: `compose-selection`, `compose-document` | + Three new AnalysisAction rows consuming those scopes (in R7 LinearConsumers path) |
| ~~One consumer type wired E2E: `compose-summarize` → Document Summary~~ **Retired in PR #544** (Compose R1 AI dispatch removed) | + 3 new `sprk_analysisaction` rows: `compose-explain-clause`, `compose-compare-to-playbook`, `compose-draft-alternative` — consumed via R7 LinearConsumer path through the Assistant pane |
| Open-in-Word handoff via existing `/api/documents/{id}/open-links` | + Push-to-Word annotation infrastructure (NEW BFF service) |
| SPE plumbing (load, save, promote-on-Save) — `ComposeService` reworked in PR #544 to inject `SpeFileStore` directly; added `SpeFileStore.ReplaceFileContentAsUserAsync` for item-based content replacement | + SPE webhook subscription + delta query for return-from-Word detection |
| Three-pane coordination wire-only | + Activated flows per §3 (PaneEventBus discriminants `compose_action_request` + `compose_edit_apply_request` added by R2) |
| Modal entry, single-session lock, etc. | + Conflict UX banner for return-from-Word edits |

R2 does NOT redo any R1 work. R2 layers on top. **R2 also does NOT re-introduce** any component retired by PR #544 — no Compose-specific AI dispatch endpoint, no `IDocxTextExtractor` (use R7 `IDocumentTextSource` + `ITextExtractor`), no `sprk_playbookconsumer` rows for Compose actions (use `sprk_analysisaction` rows).

---

## 6. Two-Phase Architecture

R2's risk and effort are concentrated in two distinct phases with different value sources and reference materials.

### 6.1 Phase 1 — LLM Editing Patterns (the highest-leverage work)

**Where adeu's value is concentrated.** Months of empirical LLM-regression iteration baked into their codebase. We adopt patterns, not code.

**Adoptable patterns** (per [`research/adeu-architecture-study.md`](./research/adeu-architecture-study.md)):

| Pattern | What it does | Where it applies in R2 |
|---|---|---|
| **Structured edit payloads** (LLM emits `target_text` / `new_text` / `comment`, NOT free-form markup) | Collapses LLM job to find-and-replace (which LLMs do reliably) | `compose-draft-alternative` playbook output contract; BFF `IComposeEditApplicator` interface |
| **CriticMarkup-as-display** for LLM read direction | LLM sees existing track changes inline as `{++/--/>>/<<}` markers in rendered Markdown | JPS scope payload generator for `compose-selection` and `compose-document` |
| **`match_mode` validator** (`strict` / `first` / `all`) | LLM specifies match precision; engine refuses ambiguity with actionable error | `IComposeEditValidator` in BFF |
| **Structured ambiguity errors with recovery paths** | Error includes match count, 5 examples with context, copy-pasteable resolution | Error response shape on validation failure |
| **4-phase atomic batch pipeline** (resolve → sort descending → skip overlap → apply bottom-up) | Edits apply in order; earlier edits don't shift later offsets | `ComposeEditBatch` class in BFF |
| **Snapshot / rollback** | Atomic suggest-or-fail; if any edit in batch fails validation, none applied | `ComposeEditTransaction` wrapper |
| **Pattern-based text anchoring** (content-match + structural hint) | Drift-resistant anchors that survive document edits | `TextAnchor` value object — used for both LLM-proposed and human-created annotations |
| **Tool descriptions ARE the prompt** | Behavioral guidance + recovery paths embedded in tool/scope descriptions, not just metadata | JPS scope `description` fields; consumer-routing entry descriptions |
| **Semantic Appendix** in scope payload | LLM sees defined terms, cross-references, structural metadata to reduce hallucination | `compose-document` scope payload generator |
| **Coordination-prompt pattern** | Tool outputs end with suggested next actions | `ConversationPane` Assistant response formatter |
| **`// EDGE-N:` numbered comments** | Hard-won edge cases captured at the line they apply | Code-quality standard for R2 implementation |

**Phase 1 work surface**:
- `src/server/api/Sprk.Bff.Api/Services/Compose/` — new directory
  - `IComposeEditValidator` (ambiguity + match_mode)
  - `ComposeEditBatch` (4-phase pipeline)
  - `ComposeEditTransaction` (snapshot/rollback)
  - `SemanticAppendixGenerator`
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/` — Compose UI gains custom ProseMirror marks (`insertion`, `deletion`, `commentAnchor`)
- JPS scope updates (R1 scopes get richer `description` fields per "tool descriptions = prompt" pattern)

### 6.2 Phase 2 — DOCX Shuttle (well-trodden engineering)

**Where Microsoft Open XML SDK + Microsoft Learn cover 90%.** Adeu's value here is narrow — specific edge-case wisdom from their bug-numbered comments. The bulk is documented territory.

**Primary references**:
- Microsoft Learn "Insert a comment into a word processing document"
- Microsoft Learn "Accept all revisions in a word processing document"
- [`github.com/dotnet/Open-XML-SDK`](https://github.com/dotnet/Open-XML-SDK) typed classes: `Comments`, `Comment`, `CommentReference`, `InsertedRun`, `DeletedRun`
- [`drpedapati/docx-review`](https://github.com/drpedapati/docx-review) (.NET 8 + Open XML SDK reference impl — MIT)

**Adoptable adeu wisdom** (narrow but high-value):
| Edge case | Source | Application |
|---|---|---|
| Comments-before-track-changes ordering rule | Adeu pattern + pandoc issue #9833 | Pipeline order in `DocxAnnotationWriter` |
| Paragraph-boundary `<w:del>` requires deleting paragraph mark via `w:pPr/w:rPr/w:del` | Adeu BUG-23-3 | Edge case handler in writer |
| Anchored-regex traps | Adeu `_nearest_match_hint` | Validator fallback for "did you mean?" suggestions |
| Revision metadata uniqueness (author/date/id) | General OOXML knowledge | Revision-id generator (monotonic per doc) |

**Phase 2 work surface**:
- `src/server/api/Sprk.Bff.Api/Services/Compose/DocxAnnotationWriter` — writes `<w:comment>` and `<w:ins>`/`<w:del>` from Compose state via Open XML SDK
- `src/server/api/Sprk.Bff.Api/Services/Compose/DocxAnnotationReader` — parses `<w:comment>`, `<w:ins>`, `<w:del>` from incoming DOCX
- `src/server/api/Sprk.Bff.Api/Services/Compose/SpeSyncOrchestrator` — etag/checkout state machine; SPE webhook subscription management
- BFF endpoint: `POST /api/compose/document/{id}/push-annotations`

---

## 7. AI Action Authoring — R7 W12 LinearConsumers Path

R2's AI actions are one-shot, document-scoped, single-LLM-call actions — the exact shape R7 W12's **LinearConsumers** architecture was built for. R2 authors **`sprk_analysisaction` rows** consumed by `IActionResolver` + `IActionRunner`; NOT `sprk_playbookconsumer` rows or new playbooks.

### Why LinearConsumers, not Playbooks?

R7 W12 introduced LinearConsumers (`Services/Ai/LinearConsumers/`) as the canonical path for one-shot AI actions on a document — one LLM call, no orchestration graph, no multi-node workflow. The three R2 actions (Explain / Compare / Draft) each fit this shape exactly: take a selection, invoke an LLM, return structured output. Using a playbook here would add nothing but overhead.

Full-workflow playbooks (multi-node orchestration) remain the right tool for the Daily Briefing / Insight Engine / matter-summary shapes documented in [`SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`](../../docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md). Compose R2's actions are not that shape.

### R2 Action Roster

| Action Key | AnalysisAction row | JPS Scope Consumed | Output Schema Shape |
|---|---|---|---|
| ~~`compose-summarize`~~ | ~~R1 wired E2E~~ **RETIRED PR #544** — the R1 dispatch endpoint is gone. Future summarize functionality flows through the Assistant chat surface (user asks "summarize this document" — chat agent invokes an existing Document Summary tool). | — | — |
| **`compose-explain-clause`** (R2) | NEW `sprk_analysisaction` record — plain-language explanation of a clause | `compose-selection` | `{explanation: string, keyConcepts: string[], relatedPlaybookIds: string[]}` |
| **`compose-compare-to-playbook`** (R2) | NEW `sprk_analysisaction` record — matches selection to matter/firm playbook clauses | `compose-selection` + matter context | `{matches: [{playbookEntryId, clauseText, deviations, riskScore, rationale}], overallRisk: string}` |
| **`compose-draft-alternative`** (R2) | NEW `sprk_analysisaction` record — proposes structured edit payload (adeu Pattern §6.1) | `compose-selection` | `{target_text, new_text, match_mode: "strict"\|"first"\|"all", rationale, sources: [{type, id, snippet}]}` |

### R2 Action-Authoring Deliverables

- **3 new `sprk_analysisaction` records** deployed to Dataverse (each carries `sprk_systemprompt`, `sprk_outputschemajson`, `sprk_temperature`, `sprk_modeldeploymentid`)
- **No new `sprk_playbookconsumer` rows** — that table belongs to the legacy `IConsumerRoutingService` path (still supported for existing consumers like Daily Briefing narrate, but Compose R2 doesn't add to it)
- **No new `ConsumerTypes.cs` constants** — action keys are string identifiers on AnalysisAction rows, not compile-time constants in the BFF. This aligns with R7 W12's "configuration-driven consumer" pattern.
- **No new BFF endpoint** — dispatch flows through the Assistant pane's existing chat pipeline (see §7.2 below)
- **Rich JPS scope descriptions** — per adeu Pattern "tool descriptions ARE the prompt", each scope's description field carries behavioral guidance for the LLM (recovery paths + critical gotchas + example inputs/outputs)

### 7.2 Dispatch Mechanism — PaneEventBus → Assistant → LinearConsumer

The Compose toolbar dispatches an AI-action request via PaneEventBus; the Assistant pane consumes it and invokes the LinearConsumer path. **This is the load-bearing architectural decision of R2's AI surface** — it eliminates the parallel dispatch path that Compose R1 tried to build (retired PR #544).

**Sequence**:

```
User clicks "Explain" in Compose toolbar (BubbleMenu)
    ↓
ComposeToolbar dispatches PaneEventBus event on `conversation` channel:
    { type: 'compose_action_request',
      actionKey: 'compose-explain-clause',
      selection: {...},
      jpsScopePayload: {...},
      documentContext: { documentId, driveId, tenantId },
      correlationId: '...' }
    ↓
Assistant pane's ConversationPane subscribes to this event
    ↓
ConversationPane invokes IActionResolver.ResolveAsync(actionKey)
    → returns AnalysisAction { SystemPrompt, OutputSchemaJson, Temperature, ModelDeploymentId }
    ↓
ConversationPane invokes IActionRunner.RunAsync(action, documentText, context)
    where documentText comes from IDocumentTextSource.ExtractFromDocumentIdAsync(documentId)
    ↓
IActionRunner returns JsonElement (validated against OutputSchemaJson)
    ↓
ConversationPane renders the response as an assistant chat message
    ↓ (for Draft Alternative only)
ConversationPane emits a second PaneEventBus event on `workspace` channel:
    { type: 'compose_edit_apply_request',
      editPayload: { target_text, new_text, ... },
      correlationId: '...' }
    ↓
Compose Workspace consumes this event and renders the pending track-change
```

**Endpoint surface**: **ZERO new Compose endpoints for AI dispatch.** The dispatch happens client-side via the existing `IActionRunner` server call — likely surfaced through the existing chat SSE endpoint or a small addition to it (Open Item — see §15).

### 7.3 R2 Playbook-Authoring — What's NOT in Scope

Distinct from AnalysisAction authoring:

- R2 does NOT author new `sprk_analysisplaybook` records for Compose actions. Playbooks are multi-node orchestrations; Compose actions are single-LLM-call.
- R2 does NOT add to the legacy consumer-routing table.
- R2 DOES read the existing playbook library (`sprk_analysisplaybook` matter/firm playbooks) as **reference data** in the `compose-compare-to-playbook` action — the LLM's prompt includes the matter's playbook entries as context. That's data consumption, not routing configuration.

### 7.4 Authoring Skills

- **AnalysisAction authoring**: use [`jps-action-create`](../../.claude/skills/jps-action-create/SKILL.md) skill for each new action (skill exists; supersedes `jps-playbook-design` for the linear-action case)
- **Validation**: [`jps-validate`](../../.claude/skills/jps-validate/SKILL.md)
- **Scope description discipline** (per adeu Pattern "tool descriptions ARE the prompt"): each JPS scope's `description` field is treated as LLM behavioral guidance, not metadata. Includes recovery paths and critical gotchas.

---

## 8. Session Memory — Rich Content Expansion

R1 wired `ChatSession.DocumentId` binding; R2 fills the payload with rich content.

**Payload additions** (R2):

```typescript
type ComposeSessionPayload = {
  // R1 fields (existing — unchanged)
  documentId: string;
  hostContext: ChatHostContext;
  // ... existing fields

  // R2 additions
  anchoredAnnotations: AnchoredAnnotation[];  // R2
  actionLog: ComposeAction[];                  // R2
  derivedInsights: DerivedInsight[];           // R2
  definedTermsTracking: DefinedTerm[];         // R2 stretch
};

type AnchoredAnnotation = {
  id: string;
  type: "comment" | "insertion-suggestion" | "deletion-suggestion" | "explanation";
  anchor: { textPattern: string; paragraphHint: number; spanId: string };
  body: string;
  author: string;
  timestamp: string;
  source: "human" | "ai-playbook";
  playbookSource?: { consumerType: string; playbookId: string; actionId: string };
};

type ComposeAction = {
  actionId: string;
  timestamp: string;
  consumerType: string;
  inputs: { selection: string; scope: string };
  outputs: { summary: string; insightIds: string[]; annotationIds: string[] };
  userOutcome: "accepted" | "rejected" | "deferred";
};

type DerivedInsight = {
  insightId: string;
  type: "clause-classification" | "risk-score" | "deviation" | "defined-term";
  body: string;
  surfacedInContextPane: boolean;
  sourceActionId: string;
};
```

**Persistence**: extends existing `ChatSession` payload — same Redis/Cosmos/Dataverse three-tier. No new entity.

**Compaction**: same 15-msg LLM summarization (R1 existing) handles long sessions. Derived insights survive compaction (kept in summary).

**Cross-version persistence**: bound to `DocumentId + MatterId`, NOT to a specific DOCX version. Survives Word handoffs (R1 design principle, R2 fulfilled).

---

## 9. Resources We Hook Into

Every external/cross-cutting resource R2 depends on, mapped to where it's used:

| Resource | Layer | R2 use |
|---|---|---|
| **Microsoft Open XML SDK 3.x** | BFF (NuGet) | DOCX read/write; comments + track changes |
| **Codeuctivity.OpenXmlPowerTools** | BFF (NuGet) | Diff/redline computation between document versions |
| **SharePoint Embedded** | Existing (R1) | Document storage; webhook source; checkout/checkin |
| **SPE Webhook subscriptions** | Graph API (NEW R2) | `drives/{containerId}/root` subscriptions with renewal cron (<4230 min) |
| **SPE Delta query** | Graph API (NEW R2) | Enumerate changed driveItems on webhook fire |
| **`ChatSession` three-tier** | Existing (R1) | Session memory persistence; rich payload in R2 |
| **`IActionResolver`** | R7 W12 (2026-07-02) | Resolves R2 action keys (`compose-explain-clause` etc.) to `sprk_analysisaction` rows |
| **`IActionRunner`** | R7 W12 (2026-07-02) | Executes resolved AnalysisAction against extracted document text; returns structured JSON |
| **`IDocumentTextSource`** | R7 W12 (2026-07-02) | Extracts document text (supports `.docx` via R7 `ITextExtractor` — no Compose-specific extractor needed) |
| ~~`IConsumerRoutingService`~~ | Existing (legacy) | **NOT used by R2 Compose actions.** Still used by other consumers (Daily Briefing narrate etc.); R2 stays on the LinearConsumer path. |
| ~~`IInvokePlaybookAi`~~ | Existing (widened facade retained per ADR-013 Path B) | **NOT used by R2 Compose actions.** Facade signature still present as no-harm additive; reserved for future non-linear playbook consumers. |
| **JPS scope catalog** | Existing | `compose-selection`, `compose-document` (R1); descriptions enriched in R2 per "prompt = description" pattern |
| **`sprk_analysisaction` table** | Existing (R7 W12 canonical) | **3 new rows for R2 actions** (see §7) |
| ~~`sprk_playbookconsumer` table~~ | Existing (legacy) | **R2 adds NO rows here.** R1's `compose-summarize` row is orphaned by PR #544; R7 team owns cleanup decision. |
| **`sprk_analysisplaybook` table** | Existing | R2 adds NO new playbook records. Existing matter/firm playbook entries are **read as reference data** by the `compose-compare-to-playbook` action, not routed to. |
| **TipTap ProseMirror** | UI | Custom marks for insertion/deletion/commentAnchor |
| **TipTap `BubbleMenu` extension** | UI (OSS/MIT — ships with TipTap core) | **Inline AI toolbar** on selection (per §2.0); buttons for Explain / Compare / Draft / More |
| **`spaarke-rag-references` AI Search index** | Existing | **Golden references** source for Context-pane provenance (per §2.0); use existing `add-reference-to-index` skill to maintain |
| **Existing `useDocumentActions` shared lib** | UI (R1 deliverable) | Open-in-Word reuse |
| **Spaarke Auth v2** | Existing | All R2 endpoints `RequireAuthorization()` |
| **`ConversationPane`** | UI (Existing) | Extended with new playbook integrations; coordination-prompt pattern in responses |

**NEW resources introduced in R2** (zero-license-fee verification):
- Microsoft Open XML SDK 3.x — **MIT, .NET Foundation, no fee**
- Codeuctivity.OpenXmlPowerTools — **MIT, no fee**
- SPE Webhook subscriptions — no fee (part of Graph API)

---

## 10. ADR Tensions (per CLAUDE.md §6.5)

Anticipated conflicts between R2's design and existing ADR rules. Surfaced at design time per the ADR Conflict Resolution Protocol.

| Topic | ADR / non-goal | Path | Rationale |
|---|---|---|---|
| **R1 non-goal "Tracked changes round-trip — never"** | R1 spec.md (not an ADR; project-level non-goal) | **Path B — R1 spec amendment** | Competitive necessity surfaced post-R1; without Word-native track changes, Compose cannot replace Word add-in workflows. R1 spec.md should be amended to "deferred to R2" rather than "never." |
| **R1 non-goal "Comments stored as `<w:comment>` — never"** | R1 spec.md | **Path B — R1 spec amendment** | Same as above — over-pruned at R1; needed for parity. R2 ships it. |
| **Embedded license fees** | Portfolio policy (planned but not yet codified — see CLAUDE.md update we'll do separately) | **Path C — Comply** | R2 uses ONLY MIT-licensed runtime dependencies (Open XML SDK, OpenXmlPowerTools, TipTap OSS). Zero commercial license fees. Verified per §9. |
| **ADR-013 AI facade discipline (refined 2026-05-20)** | ADR-013 (refined) | **Path C — Comply** | R2 consumes AI through R7 W12 LinearConsumers facades (`IActionResolver`, `IActionRunner`, `IDocumentTextSource`) — all resident in `Services/Ai/LinearConsumers/`. No direct injection of `IOpenAiClient` / `IPlaybookService` into Compose CRUD code. The legacy `IConsumerRoutingService` + `IInvokePlaybookAi` facades are not used by R2 Compose actions (see §7 for why LinearConsumers is preferred for one-shot document actions). |
| **CLAUDE.md §11 "no parallel dispatchers"** (implicit from R1 cleanup outcome) | CLAUDE.md §11 + Compose R1 PR #544 lessons-learned | **Path C — Comply** | R2 MUST NOT re-introduce a Compose-specific AI dispatch endpoint. All AI actions flow through the Assistant pane via PaneEventBus → `IActionResolver`/`IActionRunner`. This is the load-bearing architectural constraint of R2's AI surface; violating it undoes the R1 cleanup. |

**No new ADR tensions discovered.** R1 spec amendments are non-ADR Path B work; the licensing concern resolves cleanly with our editor + DOCX library choices; the "no parallel dispatcher" constraint is Path C compliance with an existing lesson.

**Actions**:
- File R1 spec.md amendment as part of R2 closeout (or earlier) — for the two Word-native non-goals.
- Reference PR #544 lessons-learned in R2 code review guidance so reviewers reject any re-introduction of a Compose-specific AI dispatch endpoint.

---

## 10.5 Placement Justification (per CLAUDE.md §10)

All R2 endpoints belong in `Sprk.Bff.Api`. No new microservice. No Dataverse plugin handlers.

**Justification**:
1. **All R2 endpoints touch SPE (Graph API) and Dataverse** — both require BFF infrastructure (OBO/app-only auth, Graph client factory, Dataverse SDK).
2. **Open XML SDK runs server-side** — DOCX manipulation in browser is infeasible at our scope (file sizes, dependencies, security). BFF is the natural host.
3. **AI dispatch uses AI PublicContracts facade** per refined ADR-013. Consumer-routing + invoke-playbook services are BFF-resident; R2 consumes them as facade clients.
4. **SPE webhook subscriptions terminate on BFF** — only stable inbound surface; not a separate service.
5. **Publish-size impact estimate**: +3-5 MB compressed (Open XML SDK + OpenXmlPowerTools). Current baseline post-R1 will be ~46-48 MB (R1 pre-publish). R2 ceiling check: 50-53 MB compressed, well under 60 MB CLAUDE.md §10 ceiling. Will measure per-task.
6. **No new HIGH-severity CVE expected** from MIT NuGet packages — verify at task close.
7. **Test obligation**: every new service in `Services/Compose/` requires matching unit tests in `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/`.

**Hot-Path Declaration**: BFF=Y · SpaarkeAi=Y · ci-workflows=N · skill-directives=N · root-CLAUDE.md=N.

---

## 11. Component Reuse Map (per CLAUDE.md §11)

| Need | Reuse from | Net-new in R2 |
|---|---|---|
| Three-pane shell | SpaarkeAi `ThreePaneShell` | — |
| Editor framework | TipTap OSS (R1) | + Custom ProseMirror marks: `insertion`, `deletion`, `commentAnchor`; + TipTap `BubbleMenu` extension wired for inline AI toolbar (per §2.0) |
| Workspace pane host | `WorkspaceLayoutWidget` + Compose section (R1) | — |
| Assistant pane | `ConversationPane` (R1) | + New playbook integrations + coordination-prompt response formatter |
| Context pane | `@spaarke/legal-workspace` panes | + New section: `compose-playbook-comparison` |
| Auth | `@spaarke/auth` (R1) | — |
| BFF | `Sprk.Bff.Api` (R1) | + `Services/Compose/` directory (extends existing R1 `ComposeService` + `StaleCheckoutSweeperHostedService`); new endpoints (§12) — none for AI dispatch |
| ChatSession persistence | Three-tier (R1) | + Rich payload schema (R2) |
| SPE facade | `SpeFileStore` + `ISpeFileOperations` (extended in PR #544 with `ReplaceFileContentAsUserAsync`) | Reused by R2's DOCX writer (annotation apply → save via `ReplaceFileContentAsUserAsync`) |
| AI action resolution | `IActionResolver` (R7 W12) | + 3 new `sprk_analysisaction` rows |
| AI action execution | `IActionRunner` (R7 W12) | — (reused verbatim) |
| Document text extraction | `IDocumentTextSource` + `ITextExtractor` (R7 W12) | — (reused; supports `.docx` via Document Intelligence) |
| DOCX engine (annotation writer/reader) | NET-NEW: Open XML SDK 3.x + Codeuctivity.OpenXmlPowerTools | NEW (R2) — both MIT. Distinct from `ITextExtractor`: this SDK is for **writing** track changes / comments back into DOCX, whereas `ITextExtractor` is for **extracting plain text** as LLM input. |
| Open-in-Word | `useDocumentActions` shared lib (R1 extracted to `@spaarke/document-operations`) | — |
| SPE access | Existing Graph + R1 plumbing | + Webhook subscriptions + delta query handler |
| JPS scopes | `compose-selection`, `compose-document` (R1) | + Enriched `description` fields per "prompt = description" pattern |
| AnalysisAction authoring | `jps-action-create` / `jps-validate` skills (existing) | Author 3 new `sprk_analysisaction` rows |
| Playbook authoring | `jps-playbook-design` / `jps-playbook-audit` skills (existing) | NOT used by R2 Compose actions (see §7 — R2 uses AnalysisActions, not Playbooks) |
| LLM editing patterns | adopt from adeu (reference only, NOT code dependency) | NEW (R2) — `ComposeEditValidator`, `ComposeEditBatch`, `ComposeEditTransaction` in BFF |

---

## 12. BFF Surface (R2)

### 12.1 AI dispatch — ZERO new Compose endpoints

R2 introduces **no new Compose-specific AI dispatch endpoint**. AI actions dispatch through the Assistant pane via PaneEventBus → R7 LinearConsumer path (see §7.2). The `POST /api/compose/action/{consumerType}` endpoint that this design document previously planned to extend was **retired in Compose R1 PR #544** (2026-07-02) as part of the parallel-dispatcher cleanup; R2 does NOT restore it.

Server-side, the LinearConsumer path may surface through the existing chat SSE endpoint or a small new "direct action invocation" chat endpoint — **this is Open Item #7 (§15)** and resolves in Spike 0.

### 12.2 Word-native annotation endpoints (NEW in R2)

| Endpoint | Purpose |
|---|---|
| `POST /api/compose/document/{spe-id}/push-annotations` | **NEW** — applies pending Compose annotations to DOCX as `<w:comment>` and `<w:ins>`/`<w:del>` via Open XML SDK; saves to SPE via `SpeFileStore.ReplaceFileContentAsUserAsync` (added in PR #544) with `If-Match` etag |
| `POST /api/compose/document/{spe-id}/pull-annotations` | **NEW** — parses incoming DOCX from SPE via Open XML SDK; extracts annotations; returns structured annotation payload to Compose UI for re-anchoring |
| `POST /api/compose/webhooks/spe-doc-changed` | **NEW** — SPE webhook receiver; enqueues delta query and downstream re-anchor work |
| `POST /api/compose/document/{spe-id}/check-changes` | **NEW** — explicit poll variant (in case webhook fails or for testing); BFF compares stored etag vs current SPE etag |
| `POST /api/compose/edit-batch/validate` | **NEW** — validates LLM-proposed edit batch against current document state (ambiguity check, match_mode per adeu Pattern §6.1); returns structured errors with recovery paths. Called by the Assistant pane BEFORE dispatching an Assistant → Workspace edit-apply event, so the client can present recovery UX if validation fails. |
| `GET /api/compose/session/{matter-id}/{thread-id}/derived-insights` | **NEW** — extended ChatSession query returning derived insights for Context pane rendering |

### 12.3 Reuse from R1 (post-cleanup)

- `GET /api/documents/{id}/open-links` — open-in-Word (existing endpoint, no R2 changes)
- `GET /api/compose/documents/{documentSpeId}` — load DOCX (existing R1; unchanged by PR #544)
- `POST /api/compose/documents/{documentSpeId}/save` — save DOCX (existing R1; internally uses `SpeFileStore.ReplaceFileContentAsUserAsync` post PR #544; **R2 extends only the Compose-side annotation-apply orchestration, NOT the endpoint contract**)
- `POST /api/compose/documents/{documentSpeId}/promote` — first-save promotion (existing R1)
- `POST /api/compose/documents/{documentId}/checkout` + `/checkin` — Phase-5 stubs from R1 (return 501; callers use `/api/documents/{id}/checkout` from `DocumentCheckoutService`)
- `POST /api/compose/document/{documentId}/heartbeat` — checkout heartbeat (existing R1)

### 12.4 Explicitly retired (do NOT restore in R2)

| Retired surface | Retired in | Rationale |
|---|---|---|
| `POST /api/compose/action/{consumerType}` | PR #544 | Superseded by Assistant-pane → R7 LinearConsumer dispatch |
| `POST /api/compose/upload` (R2-reserved stub) | Existing R1 stub, unchanged | Compose upload continues to route through the Assistant upload pipeline |

---

## 13. Spike Plan

Phase 0 (dispatch mechanism) + Phase 1 (LLM patterns) + Phase 2 (DOCX shuttle) spikes + benchmark integration. ~4 days total.

### Phase 0 spike — Dispatch mechanism (BLOCKER for Phase 1)

| # | Spike | Days | Decision unlocked |
|---|---|---|---|
| 0 | **Toolbar → Assistant → LinearConsumer dispatch path**: prototype the client-side flow — Compose toolbar dispatches `compose_action_request` on PaneEventBus; Assistant subscribes; Assistant invokes `IActionResolver` + `IActionRunner` via what server endpoint (existing chat SSE with a direct-action-invocation param? new small `/api/ai/action/execute` endpoint? extend `/api/ai/analysis/execute` from R7's LinearConsumer wiring?). Pick one; document why the other paths were rejected. | 0.5 | The load-bearing R2 architectural decision. Everything else builds on this. |

### Phase 1 spikes — LLM patterns (priority)

| # | Spike | Days | Decision unlocked |
|---|---|---|---|
| 1 | Author one R2 AnalysisAction row (e.g., `compose-explain-clause`) with adeu-style behavioral prompts + structured OutputSchemaJson; verify `IActionRunner` returns validated JSON reliably | 0.5 | Prompt-pattern validation; JPS scope description format; AnalysisAction schema shape |
| 2 | Implement `IComposeEditValidator` with `match_mode` + structured ambiguity errors; test against 5 representative LLM-proposed edits | 0.5 | Validator design + error UX |
| 3 | Implement `ComposeEditBatch` 4-phase pipeline + snapshot rollback; verify atomicity on intentionally-failing batch | 0.5 | Atomic-transaction model |
| 4 | Build `SemanticAppendixGenerator` for `compose-document` scope; measure LLM hallucination delta with vs without appendix | 0.5 | Hallucination-reduction validation |

### Phase 2 spikes — DOCX shuttle

| # | Spike | Days | Decision unlocked |
|---|---|---|---|
| 5 | Open XML SDK writes test DOCX with `<w:ins>` + `<w:comment>` → SPE upload → Word for Web renders both natively | 0.5 | Forward path validity |
| 6 | Reverse: Word for Web user adds comment + track-change → SPE webhook fires → BFF SDK reads with correct author/date | 0.5 | Round-trip validity |
| 7 | SPE checkout collides with Word for Web open session — document expected UX | 0.5 | Concurrency UX |

### Quality validation

| # | Spike | Days | Decision unlocked |
|---|---|---|---|
| 8 | Wrap BFF endpoints as MCP server stub; integrate `dealfluence/docx-benchmark` (AGPL — used externally only) as benchmark harness; measure baseline | 0.5 | Quality regression gate |

**Total spikes: 9 × half-day = 4.5 days.**

---

## 14. Q&A Resolutions (locked from R2 design discussion)

| Q | Resolution |
|---|---|
| **Editor framework** | TipTap OSS (R1 carry-forward; no Pro extensions per portfolio licensing policy) |
| **DOCX engine** | Microsoft Open XML SDK 3.x + Codeuctivity.OpenXmlPowerTools (both MIT, both active) |
| **Adeu integration** | Patterns only (Level 2 per IP discipline) — read source for understanding, port to .NET with vendor-neutral primitives. NO runtime dependency on adeu. |
| **CriticMarkup role** | Read direction only — LLM consumes documents rendered with inline `{++/--/>>}` markers. LLM does NOT produce CriticMarkup; produces structured `{target_text, new_text, comment}` payloads instead. (Adeu's asymmetric design.) |
| **Wire format (LLM → BFF)** | Structured JSON edit payloads with `match_mode` parameter; validator-enforced (no markup in LLM output) |
| **Wire format (BFF → LLM, read direction)** | Markdown + CriticMarkup inline annotations + Semantic Appendix |
| **Editor in-memory format** | ProseMirror state with custom marks (`insertion`, `deletion`, `commentAnchor`) |
| **Anchoring strategy** | Hybrid — TipTap span IDs (R1) for in-editor stability; content-match + paragraph hint (R2 addition) for drift resistance through Word round-trip |
| **Phase priority** | Phase 1 (LLM patterns) > Phase 2 (DOCX shuttle) — Phase 1 is where adeu's lessons-learned are most valuable; Phase 2 is largely Microsoft-documented |
| **Three new R2 AnalysisAction rows** | `compose-explain-clause`, `compose-compare-to-playbook`, `compose-draft-alternative` — deployed to `sprk_analysisaction` table (NOT `sprk_analysisplaybook` — R2 uses R7 W12 LinearConsumers, not multi-node playbooks; see §7) |
| **AI actions in R2** | 3 (Explain, Compare, Draft Alternative); Document Q&A is stretch goal |
| **AI dispatch path** | R7 W12 LinearConsumers (`IActionResolver` + `IActionRunner` + `IDocumentTextSource`) — NOT legacy `IConsumerRoutingService` + `IInvokePlaybookAi`. Rationale: LinearConsumers is R7's canonical single-shot document-action path; matches R2's action shape exactly; legacy path is retained only for existing playbook-driven consumers. |
| **Consumer routing (legacy)** | R2 does NOT add to `sprk_playbookconsumer` or `ConsumerTypes.cs`. R1's `compose-summarize` row is orphaned by PR #544; R7 team owns the cleanup decision. |
| **AI dispatch endpoint** | ZERO new Compose-specific endpoint. Dispatch goes through Assistant pane → LinearConsumer path. Spike 0 selects the specific server surface (existing chat SSE vs new `/api/ai/action/execute` vs extending `/api/ai/analysis/execute`). |
| **DOCX text extraction** | R7 W12 `IDocumentTextSource` + `ITextExtractor` — NOT a Compose-specific extractor. R1's `IDocxTextExtractor` was retired in PR #544 as redundant with R7 platform coverage. |
| **Word-native annotations** | YES in R2 (amends R1 non-goals; competitive necessity) |
| **Round-trip from Word** | YES in R2 (annotation re-anchoring + conflict UX banner) |
| **Memory richness** | Rich `ChatSession` payload (anchored annotations, action log, derived insights) — extends R1 binding |

---

## 15. Open Items for Next Discussion

These need user decision or further investigation before `spec.md`:

1. **Document Q&A stretch goal**: include in R2 scope, or pure R3+ deferral? (Depends on whether semantic retrieval over document content is in R2 budget.)
2. **Defined-terms surface**: include as Context pane addition in R2 (parity feature with Legora), or R3 deferral?
3. **Action log retention policy**: how long does the action log persist in Cosmos / Dataverse? Same TTL as ChatSession (90 days warm, indefinite cold)?
4. **`compose-summarize-word-changes` action** for return-from-Word: include as R2 deliverable (NEW `sprk_analysisaction` row `compose-summarize-word-changes`), or just show diff?
5. **Anchored-annotation re-anchoring confidence threshold**: at what fuzzy-match confidence do we flag an annotation as "needs review" vs auto-anchor? (Spike #6 informs.)
6. **Multiple-action concurrency**: if user invokes "Compare to playbook" and "Draft alternative" rapidly, do they queue serially or run in parallel in the Assistant pane? Design implication for `ConversationPane`.
7. **[NEW] Server-side dispatch surface for LinearConsumer invocation** (Spike 0 unlock — see §13 Phase 0): pick ONE of:
    - (A) Reuse existing chat SSE endpoint with a direct-action-invocation payload param
    - (B) New small `POST /api/ai/action/execute` endpoint dedicated to Compose-toolbar-triggered actions
    - (C) Extend R7's existing `/api/ai/analysis/execute` LinearConsumer wiring
   Each has trade-offs on: contract coupling, discoverability by future MCP/agent consumers, streaming semantics, and observability. Resolves in Spike 0.
8. **[NEW] R7 coordination — `sprk_playbookconsumer` `compose-summarize` row cleanup**: R1 deployed this row to Dataverse; PR #544 removed the BFF code that consumed it. Should R7 delete it, migrate its `Document Summary` playbook binding to an `sprk_analysisaction` `document-summarize` row (LinearConsumer path), or leave it orphaned? Coordinate with R7 team.
9. **[NEW] AnalysisAction row deployment mechanism**: R2's 3 new AnalysisAction rows need a deployment path. R7 W12 likely has a script or seeding pattern — identify + reuse (do NOT invent a new deployment mechanism).

---

## 16. Vision Roadmap (post-R2)

| Release | Theme | Headline deliverables |
|---|---|---|
| **R2 (this project)** | AI actions + Word-native interop + memory continuity | 3 AI actions; Word-native annotation push/pull; round-trip; rich session memory |
| **R3** | Word add-in entry + defined terms + cross-refs | "Open in Spaarke Compose" add-in for Word; defined-terms management in Context; cross-reference validation |
| **R4** | Multi-artifact | PDF artifact (viewer + extracted-text editor); email artifact (Outlook MIME via Graph); transcript artifact |
| **R5+** | Co-editing | Real-time multi-user editing (CRDT); comparison/redline artifact (two-document compare); multi-document Q&A across matter |

The **Artifact Surface** abstraction (R1-defined, R4-activated) lets new artifact types register into the same workspace shell without rearchitecting Compose.

---

## Footer

This is a working document. Edit in place as we refine. When stable, it informs `spec.md` (the committed spec) and the task plan.

**Companion docs**:
- [`research/adeu-architecture-study.md`](./research/adeu-architecture-study.md) — adeu pattern study (~3400 words)
- [`research/openxml-docx-research.md`](./research/openxml-docx-research.md) — Open XML SDK + SPE + editor-Word patterns research
- `spec.md` — TBD, written after spikes
- `plan.md` — TBD, written after spec
- [`../spaarkeai-compose-r1/design.md`](../spaarkeai-compose-r1/design.md) — R1 design (foundation R2 builds on)
- [`../spaarkeai-compose-r1/spec.md`](../spaarkeai-compose-r1/spec.md) — R1 spec (carries the non-goals R2 amends)
