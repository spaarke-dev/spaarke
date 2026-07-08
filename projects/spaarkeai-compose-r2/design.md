# Spaarke Compose R2 — Design (Working Document)

> **Status**: DRAFT — refinement document. Not yet a committed spec.
> **Codename**: Spaarke Compose (continuing from R1)
> **Positioning**: AI-native legal drafting workspace
> **Project ID**: `spaarkeai-compose-r2`
> **R2 Theme**: **The differentiation layer activates.** R1 shipped the workspace foundation; R2 makes it AI-native and Word-interoperable. Compose now does the work the foundation was built for.
> **Owner**: Ralph Schroeder
> **Last updated**: 2026-07-08 (alignment revision — re-based on redesign-r1 as-built + r2 core charter v0.3)
> **R1 reference**: [`../spaarkeai-compose-r1/design.md`](../spaarkeai-compose-r1/design.md) + [`../spaarkeai-compose-r1/spec.md`](../spaarkeai-compose-r1/spec.md)
> **Charter input (formal)**: [`../spaarke-ai-architecture-redesign-r2/design.md`](../spaarke-ai-architecture-redesign-r2/design.md) **§8 Compose handoff package** (r2 CORE charter, DRAFT v0.3) — this project's charter baseline per the ratified platform-core + satellites re-cut (D-C1..C5, ingestion-parity ruling R-2, fidelity ruling R-4, transferred G-R2-C gate)
> **Platform reference (canonical as-built)**: [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) v0.5. Companion: [`docs/architecture/AI-ARCHITECTURE.md`](../../docs/architecture/AI-ARCHITECTURE.md) (reference overview; carries a superseded-soon banner — check the canonical doc first)
> **Binding foundations**: [ADR-039 Grounded Execution & Closed Catalogs](../../docs/adr/ADR-039-grounded-execution-closed-catalogs.md) · [ADR-040 Session Ledger](../../docs/adr/ADR-040-session-ledger.md) — both **Accepted**
>
> ### Revision log
>
> **2026-07-08** — **Alignment revision per redesign-r1 close + r2 core charter v0.3 (operator-ratified gap→recommendation chart, 16 items A1–E5).** The 2026-07-03 revision pre-dated the redesign-r1 close and the r2 core charter; its AI plumbing is re-based here onto the ratified architecture: dispatch = Action + Binding resolved via the shipped Click-path session-dispatch seam (ADR-039); the Draft-Alternative edit flow is ledger-first via the core's `compose` disposition (ADR-040); confirmation/completion/trace follow the core's Policy v2 + OutcomeCard + D-F4 contracts; session memory splits onto the ledger + workspace-scope MemoryItems; document creation at FULL ingestion parity becomes a first-class feature (ruling R-2); the fidelity boundary table (ruling R-4) and the transferred G-R2-C flagship gate are added. Product features, §2.0 UX patterns, and the §6.2/§12.2 DOCX-shuttle engineering are unchanged.
>
> **2026-07-03** — Design revised to reflect two platform changes landed after the original 2026-06-29 draft:
> 1. **LinearConsumers merged to master** (`Services/Ai/LinearConsumers/` — `IActionResolver` + `IActionRunner` + `IDocumentTextSource` + `sprk_analysisaction` table).
> 2. **Compose R1 AI dispatch retired** (PR #544, 2026-07-02). Deleted: `ComposeEndpoints.DispatchAction` endpoint (~700 LOC); `IDocxTextExtractor` (platform `ITextExtractor` covers `.docx`); `ComposeDocumentService` (superseded by `SpeFileStore.DownloadFileAsUserAsync` + new `ReplaceFileContentAsUserAsync`); `ComposeSessionService` (rebind logic inlined into `ComposeService`); `ConsumerTypes.ComposeSummarize` constant.
>
> ⚠️ *Correction (2026-07-08)*: this entry's original framing — "LinearConsumers supersede `IConsumerRoutingService` + `sprk_playbookconsumer`" — was **wrong** and is retracted by the 2026-07-08 revision. ADR-039 (Accepted) makes the Binding table the ONLY routing configuration surface, and redesign-r1 task 040 moved ALL consumers onto Bindings; `IConsumerRoutingService` is the **canonical** Binding resolver, not legacy. The PR #544 retirement facts above stand (the parallel Compose dispatch endpoint is gone and stays gone); what changed is the replacement model — see §7.
>
> Non-AI Word-native work (§6.2 DOCX shuttle, §12 push/pull-annotations endpoints) is unaffected by either revision.

This document leads with **user features** — what users actually do — and then maps each feature to the technical architecture, the three-pane choreography, and the Action/Binding catalog resources that power it. Design follows from value, not the other way around.

---

## 1. Product Statement

R2 turns Compose from a foundation editor into an **AI-native legal drafting workspace with Word-native interoperability**. Three differentiation pillars become real in this release:

1. **AI-coordinated three-pane experience** — Workspace + Assistant + Context act as one tool, not three. Selecting a clause lights up Context with playbook matches; Assistant offers actions on the same selection; results flow back into Workspace as track-change suggestions.
2. **Word-native interoperability** — AI suggestions and comments travel to Word as native `<w:ins>` / `<w:del>` / `<w:comment>` elements via Microsoft Open XML SDK. Round-trip back when Word saves.
3. **Memory continuity** — anchored annotations, the session ledger's action history, and workspace-scope memory items persist across Word handoffs and matter sessions (ADR-040 session ledger + the core's Memory Service — see §8).

**The persistence standard (binding, prominent by design — ruling R-2)**: Compose's persistence unit is the **Spaarke Document** — SPE file + `sprk_document` row + parent association + document profile analysis + indexing. The core enforces this invariant (gate policy + `JobAwareCompletionState`); **Compose r2 implements the pipeline**. A bare `sprk_document` row is **never** success — UAT finding R5-E (the broken-widget fileless orphan) is the bar. Every path in this document that creates or saves a document meets this standard (§2.7).

Competitive position after R2: **"Highlight any clause. Get an explanation. Compare it to your firm's playbook. Draft an alternative. Push back to Word as native track changes. Spaarke remembers what you and the AI decided together — across this session, across your Word visits, across the entire matter lifecycle."** Each sentence is backed by a specific R2 feature.

### 1.5 Delivered product (user synopsis)

What a Spaarke user can DO when Compose r2 ships (mirrors the core charter's §4 acceptance-backbone style):

1. **Open an existing document in a Compose workspace tab** — the `compose-editor` workspace layout, reached from chat via the layout-tab bridge (**SHIPPED** in redesign-r1).
2. **Have the assistant pre-seed a real document** — "open the engagement letter in Compose" resolves the real `sprk_document`'s SPE pointer under user OBO and loads it, refresh-surviving (**SHIPPED**, redesign-r1 R4-2).
3. **Draft into the editor from chat** — assistant-drafted content lands in the Compose editor as a transient working draft via the core's `compose` disposition (ledger-first).
4. **Use the inline AI toolbar on any selection** — Explain / Compare to playbook / Draft alternative, every result with visible provenance in the Context pane.
5. **Accept or reject pending track-changes** — AI proposals render as pending insertions/deletions with `{bindingId}@t{n}` provenance; the user controls what becomes document state.
6. **Save back with provenance — and first save of a chat-drafted document runs full-parity document creation** (SPE file + `sprk_document` + association + profile analysis + indexing) with job-aware per-step status — **and opens it in Compose — no manual lookup**: the assistant facilitates the entire chain end-to-end, chaining creation into the shipped chat→Compose bridge + pre-seed leg (r1 R4-2), so one conversation turn takes the user from "make this a document" to the document open in the editor.
7. **Push annotations to Word** — Compose comments and track-changes become native `<w:comment>` / `<w:ins>` / `<w:del>`, visible in Word for Web/Desktop.
8. **Return from Word** — Compose detects the new version and re-anchors prior annotations, flagging ambiguous ones for review.
9. **Pick up where they left off** — prior sessions' compacted digest, decisions, and anchored annotations restore across days and Word handoffs.
10. *(stretch)* **Ask questions about the open document** — Q&A over the document rides the existing agent loop, no new capability.

Where a feature creates a document (⑥, and the §2.7(d) "create it?" affordance), the chain ends with **the document open in Compose — never with a record id the user has to go find**.

**Flagship gate (transferred G-R2-C — verbatim from the core charter)**: the assistant-driven document lifecycle — **open → pre-seed → draft-into-editor → AI edit rounds → save-back with provenance** — executed by the operator on spaarkedev1 **in one conversation**, browser-verified. A passing curl or green test never satisfies it. This gate transferred unchanged in content from the r2 core charter (its former G-R2-A/B/C table) and is THIS project's flagship gate. **Gate script refinement (operator-ratified)**: the create leg ends with the newly created document **OPEN in Compose** — creation completion chains into the chat→Compose bridge + pre-seed automatically; a gate run where the operator has to manually open Compose and look up the new document FAILS the leg.

### 1.6 Fidelity boundary (ruling R-4 — binding product-trust table)

Compose must not be judged against Word fidelity before it is ready. This table declares which editing needs the Compose (TipTap) surface serves and which REQUIRE Word — "push to Word" is the pressure valve, not a failure:

| Editing need | Surface |
|---|---|
| AI first-draft generation | **Compose editor** |
| Plain-text revision | **Compose editor** |
| Clause-level rewrite | **Compose editor** |
| Selection-aware AI refinement | **Compose editor** |
| Tracked-changes authoring UX | **Word for Web/Desktop REQUIRED** |
| Comments authoring at parity | **Word for Web/Desktop REQUIRED** |
| Footnotes / cross-references | **Word for Web/Desktop REQUIRED** |
| Complex / final legal formatting | **Word for Web/Desktop REQUIRED** |
| Redline comparison | **Word for Web/Desktop REQUIRED** |

Compose *round-trips* Word-native annotations (§2.4/§2.5 push/pull via Open XML SDK) — it renders and transports them; it does not attempt Word's authoring UX for them in r2.

---

## 2. R2 User Features — What Users Actually Do

Each feature defined by: user story, three-pane choreography, catalog rows used (Action + Binding), resources hooked into.

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
- **Execution trace** (in audit-detail mode) — for compliance review. Rendered by the **core's D-F4 decision-traceability view** over the trace ledger (`ToolChain` entries + the `TraceEvent v1` contract); the Context pane **hosts** that view — Compose does NOT invent its own "LLM reasoning trace" rendering

Sources are:
- **Clickable** (navigate to the source artifact in Spaarke)
- **Citable** (drag into the doc as an inline citation — a Compose annotation type)
- **Persistent** (survive Word handoff — anchored annotations in Compose session state; insights in workspace-scope memory per §8)

This pattern is BINDING. Any AI action that produces a recommendation without source surfacing is a design defect. Reason: legal users will not trust AI recommendations without provenance; trust is the moat.

#### Tool descriptions surface as user hints

Per adeu's "tool descriptions ARE the prompt" insight, the same descriptions that prime the LLM also surface as **user-visible tooltips** on toolbar buttons and Assistant-pane affordances. Author the description once; it serves both the LLM behavioral prompt AND the user-facing help text. Cuts content surface area in half and keeps user/LLM understanding consistent.

---

### 2.1 Explain This Clause

**User story**: User selects a clause they don't fully understand. **Inline AI toolbar appears near the selection** (per §2.0); user clicks "Explain". Assistant returns a plain-language explanation with relevant legal context.

**Three-pane choreography**:
- **Workspace**: Selection highlighted; persistent annotation marker added (clickable to replay explanation); inline toolbar dismisses after click. **Toolbar click dispatches a PaneEventBus event** to the Assistant pane with `{bindingId, selection, args}` — a Click-path invocation by construction (ADR-039).
- **Assistant**: Consumes the PaneEventBus event and dispatches through the **shipped session-dispatch seam**: `dispatchConsumer(bindingId, args)` → `POST /api/ai/chat/sessions/{sessionId}/dispatch` → `SessionDispatchOrchestrator` → Binding resolution (`IConsumerRoutingService`) → prompted executor. The response **streams via SSE** into the Assistant's chat surface as an assistant message (ledger-written before render, ADR-040); offers follow-up actions ("Compare to playbook?", "Draft alternative?")
- **Context**: **Sources surfaced (per §2.0 provenance pattern)** — related precedent clauses from matter; relevant golden references from `spaarke-rag-references` index; click-to-navigate to source; execution trace hosted via the core's D-F4 view

**Catalog rows**: `compose-explain-clause` — NEW `sprk_analysisaction` Action row (carries SystemPrompt + OutputSchemaJson + Temperature + ModelDeploymentId) **+ one NEW `sprk_playbookconsumer` Binding row** targeting it (the invocation config the toolbar's Click dispatch resolves)
**JPS scope**: `compose-selection` (defined in R1)
**Dispatch path**: PaneEventBus (`conversation` channel, discriminant `compose_action_request`) → Assistant pane → session-dispatch seam → Binding → executor → streaming assistant message. **NO Compose-specific BFF endpoint** — same code path as every other Click-path capability (chips, ribbons, wizards).

**Why it matters**: Lowest-effort AI action; universal use; demonstrates Workspace → Assistant flow cleanly.

---

### 2.2 Compare to Playbook

**User story**: User selects a clause (e.g., indemnification, governing law). **Inline AI toolbar appears** (per §2.0); user clicks "Compare to playbook". Assistant compares the selection against firm/matter playbook clauses; Context pane lights up with matches, deviations, and risk scores.

**Three-pane choreography**:
- **Workspace**: Selection highlighted; risk-level annotation marker added; inline toolbar dismisses after click. **Toolbar click dispatches a PaneEventBus event** with `{bindingId, selection, matterId}`.
- **Assistant**: Consumes the PaneEventBus event; dispatches through the session-dispatch seam (same as §2.1); streams analysis into chat surface; offers "Replace with standard?" or "Negotiate this?" follow-ups
- **Context**: **Lights up with full source attribution (per §2.0)** — exact playbook entry that matched (click to navigate); clause text comparison side-by-side; deviation summary; risk score with rationale; relevant golden references; prior negotiation history if available — all clickable sources

**Catalog rows**: `compose-compare-to-playbook` — NEW `sprk_analysisaction` Action row **+ one NEW Binding row**
**JPS scope**: `compose-selection` + matter context (existing)
**Dispatch path**: PaneEventBus → Assistant → session-dispatch seam → Binding → executor (same as §2.1)
**Resources hooked into**:
- Matter playbook library (existing `sprk_analysisplaybook` entity — read as reference data by the action's LLM invocation, NOT as a routing target)
- Context pane section: new `compose-playbook-comparison` registration (Context-pane component registry)
- Optional: precedent doc retrieval (R3+ — defer)

**Why it matters**: **The Spaarke-exclusive flow.** Competitors don't have JPS playbooks as a first-class concept. This is where the three-pane coordination shines.

---

### 2.3 Draft Alternative

**User story**: User selects clause text. **Inline AI toolbar appears** (per §2.0); user clicks "Draft alternative". Assistant proposes alternative language; the suggestion appears in Workspace as a pending track-change (highlighted insertion + deletion). User accepts (becomes part of doc state) or rejects (suggestion disappears).

**Three-pane choreography**:
- **Workspace**: The pending **insertion/deletion pair** renders as track-change marks, **materialized FROM the stored ledger entry** (see the ledger-first rule below); accept/reject mini-controls appear inline near the suggestion. Toolbar click dispatches PaneEventBus event to Assistant.
- **Assistant**: Consumes event; dispatches through the session-dispatch seam; streams alternative text with rationale; the structured edit payload is **ledger-written as a `SessionOutput` with the `compose` disposition** (storage precedes rendering — ADR-040) and the SSE frame notifies the client; offers "Refine further?" follow-up
- **Context**: **Full source attribution (per §2.0)** — exact playbook clause that informed the draft; golden references / precedent matters cited; execution trace via the core's D-F4 view; all clickable + citable (drag a source into the doc as an inline citation if accepting)

**Ledger-first edit flow (binding — ADR-040)**: the Draft-Alternative edit payload is a **`SessionOutput` with the `compose` disposition FIRST**; the Workspace **materializes the pending track-change from the stored ledger entry**, carrying `{bindingId}@t{n}` provenance on the rendered suggestion. The core publishes the `compose` disposition member + its SSE frame shape in its **Phase A0** — Compose r2 CONSUMES that seam and never invents a client-side-only payload path. *Why*: a client-only payload dies on refresh — the user would lose a pending suggestion mid-review (the refresh-loses-the-suggestion failure mode); the ledger entry makes the suggestion durable, addressable, and provenance-carrying.

**Catalog rows**: `compose-draft-alternative` — NEW `sprk_analysisaction` Action row **+ one NEW Binding row**. OutputSchemaJson enforces the structured edit-payload shape (`target_text` / `new_text` / `comment` per adeu Pattern §6.1) so the LLM's response is directly consumable by the Workspace edit applicator.
**JPS scope**: `compose-selection` (defined in R1)
**Dispatch path**: PaneEventBus → Assistant → session-dispatch seam → Binding → executor → **compose-disposition ledger write** → SSE frame → Workspace materializes from the stored entry
**Critical UX detail**: Suggestion is **pending** — not auto-applied. User explicitly accepts. Aligns with adeu's pattern: LLM proposes, user controls.

**Why it matters**: Demonstrates the full Workspace ↔ Assistant ↔ Workspace round-trip. Provenance trail is Spaarke-unique.

---

### 2.4 Push Annotations to Word

**User story**: User has Compose-native annotations (AI suggestions accepted as pending track-changes, user-added comments). Toolbar → "Push to Word" (or implicit on Save). Word for Web / Desktop now shows annotations natively — `<w:comment>` for comments, `<w:ins>` / `<w:del>` for track changes, with proper author/timestamp metadata.

**Confirm vs completion vs trace — three moments, three surfaces (operator-ratified)**: push-annotations and save-back are **Policy v2 Tier 2c side effects** (document versioning) → confirmation is the **gate's ONE dialog**, and the preview content — "what will appear in Word vs what stays in Compose only" — lives **INSIDE that gate dialog**; that IS Tier 2c's preview/confirm behavior. There is **no bespoke confirmation banner** (bespoke confirm UX is exactly the R3-1 friction class Policy v2 kills — one dialog, one modality, never a second ask). Completion evidence is a **job-aware OutcomeCard in the transcript** (`JobAwareCompletionState` for the multi-step push/save pipeline). The **Context pane is the AUDIT surface** — it hosts the trace + provenance (D-F4 view); it never captures decisions.

**Three-pane choreography**:
- **Workspace**: Initiates the push; pending-annotation state clears when the OutcomeCard reports completion
- **Assistant**: Hosts the gate's ONE confirmation dialog (Tier 2c preview: counts of comments / track changes, what appears in Word vs stays in Compose); then the ✅/❌ **job-aware OutcomeCard** in the transcript with per-step status
- **Context**: AUDIT surface — trace + provenance for the push (timestamped, reproducible, via the D-F4 view); never a decision surface

**No new AI capability** — purely deterministic operation. Uses Open XML SDK in BFF.

**Resources hooked into**:
- Microsoft Open XML SDK 3.x ([`DocumentFormat.OpenXml`](https://github.com/dotnet/Open-XML-SDK))
- Codeuctivity.OpenXmlPowerTools (MIT fork, for diff/redline support)
- SPE check-out / check-in (existing R1 plumbing)
- SPE write with `If-Match` etag (existing R1 plumbing extended)
- Core seams consumed: gate engine (Policy v2 Tier 2c), `OutcomeCard` + `JobAwareCompletionState v1`, D-F4 trace view

**Why it matters**: **Competitive parity.** Without this, every AI suggestion is locked inside Compose. Word add-ins (Harvey, Spellbook) do this natively; we must too.

---

### 2.5 Return from Word

**User story**: User opens Compose doc in Word, makes edits, saves. Hours later, returns to Compose. Compose detects the new SPE version, reloads doc, **re-anchors prior Compose annotations** to the updated text, surfaces a banner: "Document updated in Word — 4 annotations re-anchored, 1 needs your review."

**Three-pane choreography**:
- **Workspace**: Banner with summary of changes; re-anchored annotations visible inline; ambiguous anchors flagged for review
- **Assistant**: Offers "Walk through the changes?" guided review; ready to help with conflict resolution
- **Context**: Shows diff summary; lists comments added in Word; surfaces structural changes

**No new AI capability for detection itself** — uses SPE webhooks + Open XML SDK reader.
**Optional capability (R2 stretch)**: `compose-summarize-word-changes` — an Action + Binding pair that uses the LLM to summarize what changed in human-friendly terms (NOT a playbook — the engine is frozen; see §7.3).

**Resources hooked into**:
- SPE webhook subscription (`drives/{containerId}/root`, `changeType: "updated"`, 4230-min lifespan; renewal cron)
- SPE delta query (`/drives/{id}/root/delta`) to enumerate changed driveItems
- Open XML SDK parser for incoming `<w:comment>`, `<w:ins>`, `<w:del>` extraction
- Compose session state (existing) for re-anchoring metadata

**Why it matters**: **The memory continuity moat.** Competitors lose all context when the user closes Word. Compose remembers.

---

### 2.6 Session Memory — "Pick Up Where You Left Off"

**User story**: User opens a doc they worked on last week. Compose surfaces prior sessions ("3 prior sessions, last 2 days ago"). User chooses to bring forward; prior session's compacted digest + key decisions + anchored annotations appear in Context. Assistant has the prior conversation context immediately.

**Three-pane choreography**:
- **Workspace**: Doc opens with prior annotations intact (within drift tolerance)
- **Assistant**: "Welcome back. Last session you compared clause 4.2 to the IP playbook and drafted an alternative for clause 7. Continue?"
- **Context**: Prior insights (defined terms, playbook deviations, decision history) restored — from workspace-scope memory items + ledger queries (§8)

**No new AI capability** — rides the session ledger (ADR-040) + the core's Memory Service (R2 fills them with rich Compose content; see §8 for the memory split).

**Resources hooked into**:
- Session ledger over the ChatSession three-tier stack (ADR-040 — existing; conversation memory IS the ledger)
- Compacted digest over ledger outputs (redesign-r1 task 002 — existing)
- Archival (existing, 50-msg threshold)
- **R2 additions**: anchored-annotation persistence (Compose-domain document-adjacent state, §8); workspace-scope MemoryItems written via the gated `memory.write` tool (core D-M3)

**Why it matters**: **The differentiator we explicitly designed for in R1.** R2 fills it with content.

---

### 2.7 Document Creation at Full Ingestion Parity (NEW first-class feature)

**User story**: Chat-drafted content becomes a REAL Spaarke Document on first save — not a bare row, not an orphan. And any file that isn't a Spaarke document yet gets an honest, one-click path to become one.

**The persistence standard (restated — ruling R-2)**: Compose's persistence unit is the **Spaarke Document** — SPE file + `sprk_document` row + parent association + document profile analysis + indexing. The **core enforces the invariant** (gate policy + `JobAwareCompletionState` minimum-parity checklist); **Compose r2 implements the pipeline**. A bare `sprk_document` row is never success — UAT finding R5-E is the bar.

**The four paths (resolving the naked-file question explicitly)**:

| Path | Behavior |
|---|---|
| **(a) Existing documents** | Open / pre-seed directly into Compose — **SHIPPED** (redesign-r1 R4-2): real `sprk_document` rows resolve SPE pointers under user OBO, refresh-surviving |
| **(b) Chat-DRAFTED content** | Lands in the editor as a **TRANSIENT working draft** via the `compose` disposition (§2.3 ledger-first seam) — **no document yet**; an in-editor buffer is allowed transiently |
| **(c) FIRST SAVE of a transient draft** | The **document-creation capability runs at full ingestion parity** — this is compose-r1's promotion-on-first-Save, bar-raised to the R-2 standard (SPE storage + `sprk_document` + association + profile analysis + indexing). Tier 2c write under Policy v2 (preview/confirm in r2) |
| **(d) Chat-UPLOADED files** | Can **NEVER** load into Compose directly (no SPE pointer exists). The honest path is a **D-F0(d) affordance**: *"this file isn't a Spaarke document yet — create it?"* — which runs the SAME ingestion-parity capability, **then automatically opens the resulting document in Compose** |

**End-to-end chain (operator-ratified refinement)**: for path (d) — and for any "save this as a document" flow — the ASSISTANT facilitates the **entire chain end-to-end**: create the Document at full ingestion parity **AND THEN automatically open/pre-seed it into the Compose workspace tab**. The user must NEVER have to manually open Compose and look up the newly created document. Concretely: the document-creation capability's completion (OutcomeCard) **chains into the shipped chat→Compose-layout bridge + pre-seed leg** (the r1 R4-2 mechanism) — one conversation turn takes the user from "make this a document" to the document open in the editor. The flagship gate's create leg (§1.5) verifies exactly this.

**Completion evidence**: creation renders as a **per-step `JobAwareCompletionState` OutcomeCard** — the user can distinguish "the record exists" from "profile analysis / indexing finished" (queued / running / partial / completed / failed states per step, integrating the existing Job Contract / `ServiceBusJobProcessor` status) — and its completion hands off to the open-in-Compose leg above.

**Why it matters**: this converts redesign-r1's honest refusal (the R5-E hard block) into the real thing — the single most-requested blocked action in r1 UAT — and it is the transfer target of core charter §10 row 2.

---

### 2.8 (Stretch) Document Q&A

**User story**: User asks Assistant "what's the indemnification cap?" — Assistant answers from the document content without the user needing to find the clause.

**Three-pane choreography**:
- **Workspace**: Answer references appear as ephemeral highlights ("found in §7.3")
- **Assistant**: Direct answer with citation
- **Context**: Section navigated to; relevant playbook entry surfaced

**No new playbook and no new capability**: Q&A over the open document rides the **Text path / bounded agent loop** with the document in session context (a `Doc` ledger entry) — the agent answers with citations per the ADR-039 grounded-output invariant. The playbook engine is **FROZEN — no new playbooks ever**. Likely nearly free once the document is session-mounted; kept as stretch with that framing (validation + highlight-UX polish is the only real work).

**JPS scope**: `compose-document` (defined in R1) — as session context, not a new dispatch target

**Why it matters**: Lowest-friction AI feature — and a proof that the platform's composition model makes features free.

---

## 3. Three-Pane Coordination — From Wire-Only to Activated

R1 wired the six coordinated flows with stub receivers. R2 fills them with real behavior:

| Flow | R1 status | R2 activates |
|---|---|---|
| **Workspace → Context** | Wire only | Selection → Context surfaces playbook matches, precedent, prior negotiation history; all entries source-attributed (per §2.0 provenance); Context also HOSTS the core's D-F4 decision-traceability view (trace ledger + `TraceEvent v1`) |
| **Workspace → Assistant** | Wire only | Selection → **inline AI toolbar appears** (per §2.0); click dispatches PaneEventBus `compose_action_request` on `conversation` channel with `{bindingId, selection, args}`. Assistant consumes + dispatches through the **shipped session-dispatch seam** (`dispatchConsumer` → `POST /api/ai/chat/sessions/{id}/dispatch` → `SessionDispatchOrchestrator` → Binding → executor). NO Compose-specific BFF endpoint. |
| **Context → Workspace** | Wire only | Drag precedent clause / golden reference from Context → drops into editor as inline citation; click on Context entry navigates Workspace |
| **Context → Assistant** | Wire only | "Use this precedent" → Assistant takes Context entry as input to next action |
| **Assistant → Workspace** | Wire only | AI draft (from `compose-draft-alternative`'s structured edit-payload output — §2.3) is **ledger-written as a `SessionOutput` with the `compose` disposition FIRST** (ADR-040 storage-precedes-rendering); the Workspace **materializes** the pending track-change from the stored entry **with `{bindingId}@t{n}` provenance** (clickable to source per §2.0). PaneEventBus `compose_edit_apply_request` on `workspace` channel remains the client choreography signal — it references the ledger entry, never carries the payload as the source of truth. |
| **Assistant → Context** | Wire only | AI-derived insight persists to **workspace-scope memory via the gated `memory.write` tool** (core D-M3); surfaces in Context **with full source attribution** (Binding, playbook entry, golden reference, precedent — clickable) |

**Binding architectural rule**: every R2 feature lights up at least two of these six flows. Features that don't are flagged for redesign — three-pane is the differentiator, not an optional layer.

**AI-dispatch invariant**: no R2 feature introduces a new AI dispatch endpoint — Compose-specific or otherwise. All AI actions flow: **Workspace toolbar → PaneEventBus → Assistant pane → session-dispatch seam → Binding resolution → executor** (ADR-039's Click path). See §7.2 for why this is closed, not open.

**UI-action acknowledgment (core D-F3 seam — adopted)**: UI-affecting tool results (open Compose tab, apply-edit rendering, navigation) complete only on a **client acknowledgment event referencing the emitted frame id**, or **fail honestly on timeout**. The PaneEventBus `correlationId`s upgrade to that contract — a correlationId is no longer just a trace key; it is the ack token the server waits on. *Why*: r1 finding R2-D — the model claimed UI actions that never happened; the ack makes UI claims structurally truthful.

---

## 4. Supersession Map (carry forward + amend)

| Retired / superseded | Current | Project relationship |
|---|---|---|
| (from R1) `AnalysisWorkspace` solution | SpaarkeAi three-pane shell | Compose builds ON the shell; AnalysisWorkspace retires at feature-parity per core charter D-C5 (freeze enforced by the core; retirement executes here) |
| (from R1) `SprkChat` | `ConversationPane` | R2 extends `ConversationPane` with new capability integrations |
| (from R1) `sprk_analysis.sprk_chathistory` | Session ledger (ADR-040) over the ChatSession three-tier stack (Redis/Cosmos/Dataverse) | R2 rides the ledger + the core's workspace-scope memory (§8) |
| (2026-07-03 revision's framing) "LinearConsumers supersede `IConsumerRoutingService` + `sprk_playbookconsumer`" | **Actions + Bindings resolved via `IConsumerRoutingService` (canonical, ADR-039)**; ActionRunner/PromptSchemaRenderer = the prompted executor | Corrected in this revision — see §7 |
| **(amended in R2)** R1 spec.md non-goal "Tracked changes round-trip with Word — never" | **R2 ships it** via Open XML SDK in BFF | R1 non-goal was over-pruned; R2 amends |
| **(amended in R2)** R1 spec.md non-goal "Comments stored as `<w:comment>` — never" | **R2 ships it** via Open XML SDK in BFF | R1 non-goal was over-pruned; R2 amends |
| `docs/architecture/AI-ARCHITECTURE.md` as platform baseline | [`SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) v0.5 (canonical as-built) + the r2 core charter §8 | AI-ARCHITECTURE.md is a companion overview carrying a superseded-soon banner |

---

## 5. R1 → R2 Progression — What Was Wired, What R2 Fills In

| R1 wired (foundation) | Status | R2 fills in (content + features) |
|---|---|---|
| TipTap OOB editor shell with three-pane mount | wired | + Custom marks: `insertion`, `deletion`, `commentAnchor` (R2 schema additions) |
| `ChatSession` binding via `DocumentId` | wired | + Session ledger content: anchored annotations (Compose-domain), ledger action history, workspace-scope memory pointers (§8) |
| Two JPS scopes: `compose-selection`, `compose-document` | wired | + Three new Action + Binding pairs consuming those scopes (§7) |
| `compose-summarize` consumer | **Binding row LIVE** (catalog governance) | R1's `POST /api/compose/action/*` endpoint died in PR #544, but the `compose-summarize / default` Binding row survives under redesign-r1 catalog governance (name-keyed mirror; targets the Document Summary playbook). R2 adds 3 new Action + Binding pairs: `compose-explain-clause`, `compose-compare-to-playbook`, `compose-draft-alternative` — dispatched via the session-dispatch seam |
| **Compose entry: the `compose-editor` WORKSPACE LAYOUT** (core D-C1 — not modal-first) + chat→layout-tab bridge | ✅ **SHIPPED** (redesign-r1) | — (done; R2 consumes) |
| **Real-document pre-seed** — assistant resolves the `sprk_document`'s SPE pointer under user OBO, loads into Compose, refresh-surviving | ✅ **SHIPPED** (redesign-r1 R4-2) | — (done; session-UPLOADED files intentionally excluded until §2.7(d) lands) |
| Open-in-Word handoff via existing `/api/documents/{id}/open-links` | wired | + Push-to-Word annotation infrastructure (NEW BFF service) |
| SPE plumbing (load, save, promote-on-Save) — `ComposeService` reworked in PR #544 to inject `SpeFileStore` directly; added `SpeFileStore.ReplaceFileContentAsUserAsync` for item-based content replacement | wired | + SPE webhook subscription + delta query for return-from-Word detection; + promotion-on-first-Save bar-raised to full ingestion parity (§2.7) |
| Three-pane coordination wire-only | wired | + Activated flows per §3 (PaneEventBus discriminants `compose_action_request` + `compose_edit_apply_request` added by R2, upgraded to the D-F3 ack contract) |
| Single-session lock (`DocumentCheckoutService`), heartbeat | wired | + Conflict UX banner for return-from-Word edits |

**R2 starts at draft-into-editor.** The open and pre-seed lifecycle legs are DONE (shipped in redesign-r1); R2's first new user-visible behavior is chat-drafted content landing in the editor via the `compose` disposition.

R2 does NOT redo any R1 work. R2 layers on top. **R2 also does NOT re-introduce** any component retired by PR #544 — no Compose-specific AI dispatch endpoint (see §7.2 for the binding explanation), no `IDocxTextExtractor` (use the platform `IDocumentTextSource` + `ITextExtractor`). Per ADR-039, every new capability is an **Action row + Binding row pair** — the Binding is the routing config, the Action carries the prompt.

---

## 6. Two-Phase Architecture

R2's risk and effort are concentrated in two distinct phases with different value sources and reference materials.

### 6.1 Phase 1 — LLM Editing Patterns (the highest-leverage work)

**Where adeu's value is concentrated.** Months of empirical LLM-regression iteration baked into their codebase. We adopt patterns, not code.

**Adoptable patterns** (per [`research/adeu-architecture-study.md`](./research/adeu-architecture-study.md)):

| Pattern | What it does | Where it applies in R2 |
|---|---|---|
| **Structured edit payloads** (LLM emits `target_text` / `new_text` / `comment`, NOT free-form markup) | Collapses LLM job to find-and-replace (which LLMs do reliably) | `compose-draft-alternative` Action output contract; BFF `IComposeEditApplicator` interface |
| **CriticMarkup-as-display** for LLM read direction | LLM sees existing track changes inline as `{++/--/>>/<<}` markers in rendered Markdown | JPS scope payload generator for `compose-selection` and `compose-document` |
| **`match_mode` validator** (`strict` / `first` / `all`) | LLM specifies match precision; engine refuses ambiguity with actionable error | `IComposeEditValidator` in BFF |
| **Structured ambiguity errors with recovery paths** | Error includes match count, 5 examples with context, copy-pasteable resolution | Error response shape on validation failure |
| **4-phase atomic batch pipeline** (resolve → sort descending → skip overlap → apply bottom-up) | Edits apply in order; earlier edits don't shift later offsets | `ComposeEditBatch` class in BFF |
| **Snapshot / rollback** | Atomic suggest-or-fail; if any edit in batch fails validation, none applied | `ComposeEditTransaction` wrapper |
| **Pattern-based text anchoring** (content-match + structural hint) | Drift-resistant anchors that survive document edits | `TextAnchor` value object — used for both LLM-proposed and human-created annotations |
| **Tool descriptions ARE the prompt** | Behavioral guidance + recovery paths embedded in tool/scope descriptions, not just metadata | JPS scope `description` fields; Binding tool-description fields |
| **Semantic Appendix** in scope payload | LLM sees defined terms, cross-references, structural metadata to reduce hallucination | `compose-document` scope payload generator |
| **Coordination-prompt pattern** | Tool outputs end with suggested next actions | `ConversationPane` Assistant response formatter (aligns with the core's OutcomeCard next-step chips) |
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

## 7. AI Action Authoring — Actions + Bindings (ADR-039)

R2's AI actions are one-shot, document-scoped, single-LLM-call capabilities. Per **ADR-039 (Accepted)**, every capability on the platform is an **Action row** (`sprk_analysisaction` — carries the prompt: SystemPrompt + OutputSchemaJson + Temperature + ModelDeploymentId) **paired with a Binding row** (`sprk_playbookconsumer` — the invocation config: routing, chips, event rules, tool description). R2 authors **3 Action rows + 3 Binding rows**.

### Why Action + Binding — and why the resolution model changed in this revision

The 2026-07-03 revision of this document treated `IConsumerRoutingService` + `sprk_playbookconsumer` as legacy and planned to resolve Compose actions by string action keys against `sprk_analysisaction` directly. **That was wrong**, for reasons that are now binding platform law:

- **ADR-039 (Accepted)**: routing configuration lives **ONLY in the Binding table**. String-key resolution outside it would be a **second dispatch mechanism** — precisely the class of drift the ADR exists to ban (the ten-mechanism census that motivated it).
- **redesign-r1 task 040 moved ALL consumers onto Bindings** — there is no non-Binding resolution path left on the platform. `IConsumerRoutingService` is the **canonical Binding resolver** (Click path resolves by Binding id via `GetBindingByIdAsync`), not legacy.
- **What survives from the 2026-07-03 framing**: `ActionRunner` / `PromptSchemaRenderer` ("LinearConsumers") remains the **prompted EXECUTOR** — the thing that renders the Action's prompt, calls the model once, and validates against OutputSchemaJson. Only the **RESOLUTION** step changed: the executor is reached through a Binding, never through a string action key.

Full-workflow playbooks (multi-node orchestration) remain what the EXISTING Daily Briefing / Insight Engine shapes use — and the engine is **frozen** (no new playbooks ever; see §7.3). Compose R2's actions are prompted Actions, not playbooks.

### R2 Catalog Roster

| Capability | Action row (`sprk_analysisaction`) | Binding row (`sprk_playbookconsumer`) | JPS Scope Consumed | Output Schema Shape |
|---|---|---|---|---|
| `compose-summarize` (R1) | — (targets the existing Document Summary playbook) | **LIVE** — `compose-summarize / default`, governed by redesign-r1 catalog governance (name-keyed mirror). Not an R2 deliverable. | — | — |
| **`compose-explain-clause`** (R2) | NEW — plain-language explanation of a clause | **NEW** — Click-path Binding, toolbar-invoked | `compose-selection` | `{explanation: string, keyConcepts: string[], relatedPlaybookIds: string[]}` |
| **`compose-compare-to-playbook`** (R2) | NEW — matches selection to matter/firm playbook clauses | **NEW** — Click-path Binding, toolbar-invoked | `compose-selection` + matter context | `{matches: [{playbookEntryId, clauseText, deviations, riskScore, rationale}], overallRisk: string}` |
| **`compose-draft-alternative`** (R2) | NEW — proposes structured edit payload (adeu Pattern §6.1) | **NEW** — Click-path Binding, toolbar-invoked; declares the `compose` disposition | `compose-selection` | `{target_text, new_text, match_mode: "strict"\|"first"\|"all", rationale, sources: [{type, id, snippet}]}` |

### R2 Catalog-Authoring Deliverables

- **3 new `sprk_analysisaction` Action rows** deployed to Dataverse (each carries `sprk_systemprompt`, `sprk_outputschemajson`, `sprk_temperature`, `sprk_modeldeploymentid`)
- **3 new `sprk_playbookconsumer` Binding rows** — one per Action; the Binding is the ONLY routing config surface (ADR-039); toolbar buttons resolve through it (Click path)
- **Eval cases ship WITH each row** (NFR-06 obligation — a catalog row without eval coverage does not merge)
- **No new BFF endpoint** — dispatch flows through the shipped session-dispatch seam (see §7.2)
- **Rich JPS scope descriptions** — per adeu Pattern "tool descriptions ARE the prompt", each scope's description field carries behavioral guidance for the LLM (recovery paths + critical gotchas + example inputs/outputs)

### 7.2 Dispatch Mechanism — PaneEventBus → Assistant → session-dispatch seam

The Compose toolbar dispatches an AI-action request via PaneEventBus; the Assistant pane consumes it and invokes the **shipped Click-path session-dispatch seam**. The server surface question is **ANSWERED, not open**: the seam exists, shipped in redesign-r1, and provides SSE streaming, ledger write, and gate integration for free.

**Sequence**:

```
User clicks "Explain" in Compose toolbar (BubbleMenu)
    ↓
ComposeToolbar dispatches PaneEventBus event on `conversation` channel:
    { type: 'compose_action_request',
      bindingId: '<compose-explain-clause Binding GUID>',
      selection: {...},
      args: { jpsScopePayload, documentContext: { documentId, driveId, tenantId } },
      correlationId: '...' }        // upgrades to the D-F3 ack contract (§3)
    ↓
Assistant pane's ConversationPane subscribes to this event
    ↓
ConversationPane calls the shared client helper:
    dispatchConsumer(bindingId, args)
    ↓
POST /api/ai/chat/sessions/{sessionId}/dispatch          (existing endpoint — Click path)
    ↓
SessionDispatchOrchestrator
    → IConsumerRoutingService.GetBindingByIdAsync(bindingId)   (Binding resolution — ADR-039)
    → prompted executor (ActionRunner / PromptSchemaRenderer renders the Action's
      prompt, one LLM call, output validated against OutputSchemaJson)
    → OutputRouter ledger write (storage precedes rendering — ADR-040)
    → SSE stream to the client (same loop as every other capability)
    ↓
ConversationPane renders the streamed response as an assistant chat message
    ↓ (for Draft Alternative only)
The output is a SessionOutput with the `compose` disposition (already in the ledger);
its SSE frame signals the client; ConversationPane emits PaneEventBus
`compose_edit_apply_request` on `workspace` channel REFERENCING the ledger entry
    ↓
Compose Workspace materializes the pending track-change FROM the stored ledger entry
(with {bindingId}@t{n} provenance) and acks the frame id (D-F3)
```

**Endpoint surface: ZERO new endpoints — and this is closed, permanently.** Read this before writing any Compose dispatch code:

> **Why no new Compose dispatch endpoint can ever be added.** The 2026-07-03 draft of this document left the server surface as an open item with three options, including "(B) a new small `POST /api/ai/action/execute` endpoint". Option (B) is **exactly the thing PR #544 deleted** — Compose R1's `ComposeEndpoints.DispatchAction` was a parallel dispatch endpoint, ~700 LOC of duplicated auth/streaming/session plumbing that drifted from the chat path and had to be killed. ADR-039 (Accepted) then made the ban structural: the platform has **one dispatch protocol — three entry paths (Event / Click / Text) — over two closed catalogs**; a new dispatch endpoint is a fourth entry path, i.e., the "eleventh mechanism" the ADR exists to make a violation. The Click path's session-dispatch seam (`dispatchConsumer` → `POST /api/ai/chat/sessions/{id}/dispatch` → `SessionDispatchOrchestrator`) already provides everything a new endpoint would be built for — SSE streaming, ledger write-before-render, gate/Policy-v2 integration, eval coverage — for free. Any future "but Compose needs its own endpoint because X" proposal must instead express X as: a Binding config field, a disposition member, or a gate policy — or go through CLAUDE.md §6.5 as an explicit ADR-039 challenge. It does not get merged as an endpoint.

**PaneEventBus stays** — it is client-side pane choreography (which pane reacts to what), not dispatch. Dispatch is the server seam above.

### 7.3 What's NOT in Scope — playbooks and the frozen engine

Distinct from Action + Binding authoring:

- R2 does NOT author new `sprk_analysisplaybook` records — for Compose actions or anything else. **The playbook engine is FROZEN: no new playbooks ever** (redesign-r1 posture; existing playbook-backed consumers keep working, new capabilities are prompted Actions).
- R2 DOES read the existing playbook library (`sprk_analysisplaybook` matter/firm playbooks) as **reference data** in the `compose-compare-to-playbook` action — the LLM's prompt includes the matter's playbook entries as context. That's data consumption, not routing configuration.

### 7.4 Authoring Pipeline (binding — matches §15 and the deliverables list)

- **Action rows** author via the [`jps-action-create`](../../.claude/skills/jps-action-create/SKILL.md) skill + the **BA catalog editor** (PlaybookBuilder web resource) or **Dataverse MCP** — **mirror-first** under `infra/dataverse/inputschemas/` (the seed mirror is the authored source of record)
- **Binding rows** author the same way — mirror-first, name-keyed upsert (the redesign-r1 catalog-governance pattern)
- **`Seed-JpsActions.ps1` is RETIRED** (deleted by redesign-r1 task 051) — there is no old seeding script to find; do not go looking for one
- **Every new row SHIPS WITH eval cases** (NFR-06 obligation) — golden-utterance/dispatch families for each Binding
- **Schemas must satisfy `OpenAiFunctionSchemaValidator`** — property-level boolean `required` is **BANNED** (the H1 platform-outage lesson: one bad catalog row 400'd the entire loop)
- **Sequencing — the core's triple-twin hoist lands FIRST**: core charter §10 row 15 hoists the three hand-maintained description twins (live catalog row ↔ handler metadata ↔ seed mirror) to one authored source with validated mirrors, in core **Phase A, BEFORE any catalog-row task**. Compose r2's rows author **THROUGH the hoisted source**; coordinate timing with the core so our rows are among its consumers, not a fourth hand-maintained twin.
- **Validation**: [`jps-validate`](../../.claude/skills/jps-validate/SKILL.md)
- **Scope description discipline** (per adeu Pattern "tool descriptions ARE the prompt"): each JPS scope's `description` field is treated as LLM behavioral guidance, not metadata. Includes recovery paths and critical gotchas.

---

## 8. Session Memory — Rich Content Expansion (re-based on the core memory model)

R1 wired `ChatSession.DocumentId` binding. R2 fills sessions with rich Compose content — but the three payload structures the 2026-07-03 draft proposed are **re-based onto the core's memory architecture** (charter §7.2 D-M1/D-M3). The split:

| 2026-07-03 proposal | Disposition | Why |
|---|---|---|
| `actionLog: ComposeAction[]` | **DELETED as a stored structure — it IS the session ledger.** The ledger's `ToolChain` entries + `SessionOutput` refs (`{bindingId}@t{n}`) already carry every action, its inputs, and its outputs. Compose **queries** the ledger; it never duplicates it. | A duplicate action log diverges from the ledger after a crash (ledger write succeeded, payload copy didn't — or vice versa) — then two sources of truth disagree about what the AI did. ADR-040 exists so that cannot happen. |
| `derivedInsights: DerivedInsight[]` | **Become workspace-scope `MemoryItem`s** written via the **gated `memory.write` tool** (core D-M3), carrying the full governance envelope (tenant, scope, provenance, trust level, expiration). | Their proposed `source: "ai-playbook"` shape is exactly what D-M3's untrusted-origin write rules exist to gate — un-gated, AI-derived content writing itself into persistent memory is the memory-poisoning vector. The gated tool makes each insight-persist a governed, Policy-v2-visible side effect. |
| `anchoredAnnotations: AnchoredAnnotation[]` | **MAY remain Compose-domain document-adjacent state** (kept — see the explicit argument below). | Anchors are editor-positional state bound to a document's text, not recallable knowledge; forcing them through the MemoryItem contract would bloat it with span/paragraph mechanics that no other consumer needs. |

**Explicit deviation note (charter §3.4)**: the core's contract-first rule says satellites consume published contracts and **may not invent local MemoryItem variants**. `AnchoredAnnotation` is argued as **not a MemoryItem variant**: it is document-adjacent UI state (like a cursor or a fold), persisted with the Compose session, never retrieved as "memory" by the Context Binder, never written via `memory.*`, and never surfaced in the user's memory review/delete view. It carries tenant + matter scoping (annotations are matter work product and honor matter/ethical-wall authorization). If spec review rejects this argument, the fallback is a `MemoryItem` sub-type negotiated WITH the core — never a silent local variant.

**Payload additions** (R2):

```typescript
type ComposeSessionPayload = {
  // R1 fields (existing — unchanged)
  documentId: string;
  hostContext: ChatHostContext;
  // ... existing fields

  // R2 additions
  anchoredAnnotations: AnchoredAnnotation[];  // R2 — Compose-domain document-adjacent state (see deviation note)
  definedTermsTracking: DefinedTerm[];        // R2 stretch
  // actionLog        — REMOVED: query the session ledger (ToolChain + SessionOutput refs)
  // derivedInsights  — REMOVED: workspace-scope MemoryItems via the gated memory.write tool
};

type AnchoredAnnotation = {
  id: string;
  type: "comment" | "insertion-suggestion" | "deletion-suggestion" | "explanation";
  anchor: { textPattern: string; paragraphHint: number; spanId: string };
  body: string;
  author: string;
  timestamp: string;
  source: "human" | "ai";
  provenance?: { bindingId: string; ledgerRef: string };  // {bindingId}@t{n} — ADR-040 source ref
};
```

(The 2026-07-03 draft's `ComposeAction` and `DerivedInsight` local types are **deleted** — replaced by ledger refs and the core's `MemoryItem v1` contract respectively.)

**Persistence**: `anchoredAnnotations` extends the existing Compose session payload — same three-tier stack the ledger rides. No new entity. Insights live in the core Memory Service's workspace scope (Cosmos); action history lives in the ledger.

**Compaction**: the **compacted digest over ledger outputs** (redesign-r1 task 002 — digest compaction generalized to cover outputs, keys verbatim in digest) handles long sessions. Workspace-scope memory items are unaffected by compaction (they live outside the conversation window by design).

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
| **Session ledger (ADR-040)** over the ChatSession three-tier stack | Existing | Conversation memory + action history + `compose`-disposition outputs; Compose queries it, never duplicates it (§8) |
| **Session-dispatch seam** — `dispatchConsumer` client helper + `POST /api/ai/chat/sessions/{id}/dispatch` + `SessionDispatchOrchestrator` | Existing (shipped, redesign-r1) | THE dispatch path for all R2 AI actions (Click path); provides SSE streaming, ledger write, gate integration for free (§7.2) |
| **`IConsumerRoutingService`** | Existing — **CANONICAL Binding resolver (ADR-039)** | Resolves R2's Binding rows (`GetBindingByIdAsync`) inside the session-dispatch seam. redesign-r1 task 040 moved ALL consumers onto Bindings — this is the platform's one routing surface, not legacy. |
| **`ActionRunner` / `PromptSchemaRenderer`** (LinearConsumers) | Existing | The **prompted executor** — renders the resolved Action's prompt, one LLM call, validates against OutputSchemaJson. Reached only via Binding resolution, never by string action key. |
| **`IDocumentTextSource` + `ITextExtractor`** | Existing platform | Extracts document text (supports `.docx`) — no Compose-specific extractor (the R1 `IDocxTextExtractor` stays dead, PR #544) |
| **`sprk_analysisaction` table** (Actions) | Existing | **3 new Action rows for R2** (see §7) |
| **`sprk_playbookconsumer` table** (Bindings) | Existing — **CANONICAL routing config (ADR-039)** | **3 new Binding rows for R2** (one per Action). The R1 `compose-summarize / default` row is LIVE under redesign-r1 catalog governance (name-keyed mirror; targets Document Summary) — owned by catalog governance, not by this project. |
| **`sprk_analysisplaybook` table** | Existing — **engine FROZEN** | R2 adds NO playbook records (no one does — no new playbooks ever). Existing matter/firm playbook entries are **read as reference data** by the `compose-compare-to-playbook` action, not routed to. |
| **Core A0 contracts (consumed, never forked)**: `ComposeDisposition v1` (+ SSE frame shape) · `OutcomeCard v1` · `JobAwareCompletionState v1` · `TraceEvent v1` | r2 core Phase A0 (charter §3.4) | Draft-into-editor seam (§2.3); completion evidence for push/save/create (§2.4/§2.7); Context-pane trace hosting (§2.0/§3). Satellites may only consume these contracts — no local variants. |
| **`memory.write` gated tool + Memory Service workspace scope** | r2 core (D-M1/D-M3) | Persisting Compose-derived insights as governed workspace-scope MemoryItems (§8) |
| **Gate engine + Confirmation Policy v2 (Tier 2c)** | r2 core (D-F1) | The ONE confirmation dialog for push-annotations / save-back / document creation (§2.4/§2.7) |
| **JPS scope catalog** | Existing | `compose-selection`, `compose-document` (R1); descriptions enriched in R2 per "prompt = description" pattern |
| **TipTap ProseMirror** | UI | Custom marks for insertion/deletion/commentAnchor |
| **TipTap `BubbleMenu` extension** | UI (OSS/MIT — ships with TipTap core) | **Inline AI toolbar** on selection (per §2.0); buttons for Explain / Compare / Draft / More |
| **`spaarke-rag-references` AI Search index** | Existing | **Golden references** source for Context-pane provenance (per §2.0); use existing `add-reference-to-index` skill to maintain |
| **Existing `useDocumentActions` shared lib** | UI (R1 deliverable) | Open-in-Word reuse |
| **Spaarke Auth v2** | Existing | All R2 endpoints `RequireAuthorization()` |
| **`ConversationPane`** | UI (Existing) | Extended to consume `compose_action_request` events + render OutcomeCards; coordination-prompt pattern in responses |

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
| **ADR-039 one dispatch protocol / closed catalogs** | ADR-039 (**Accepted**) | **Path C — Comply** | R2 adds Action + Binding rows resolved via the shipped session-dispatch seam (Click path). No new dispatch endpoint, no string-key resolution outside the Binding table, no new intent mechanism (§7.2 explanation block is the enforcement text for reviewers). |
| **ADR-040 storage-precedes-rendering / no parallel session cache** | ADR-040 (**Accepted**) | **Path C — Comply** | The Draft-Alternative edit payload is a ledger `SessionOutput` (`compose` disposition) BEFORE any rendering (§2.3); the action log is the ledger, not a duplicate structure (§8); anchored annotations are document-adjacent UI state, not a session cache. |
| **ADR-013 AI facade discipline (refined 2026-05-20)** | ADR-013 (refined) — **now a Tier-1 CI-blocking NetArchTest rule, merged 2026-07-08** | **Path C — Comply** | Compose code never injects AI internals (`IOpenAiClient`, executor types, routing service) directly — it reaches AI only through the session-dispatch HTTP seam and published core contracts. The NetArchTest rule makes a violation a build failure, not a review catch. |
| **Core charter §3.4 contract-first (no local MemoryItem variants)** | r2 core charter (operator-ratified) | **Path A — narrow documented deviation, argued in §8** | `AnchoredAnnotation` stays Compose-domain document-adjacent state — argued as NOT a MemoryItem variant (positional UI state, never retrieved as memory, never written via `memory.*`); tenant/matter scoped. Fallback if rejected at spec review: negotiate a MemoryItem sub-type WITH the core. |
| **CLAUDE.md §11 "no parallel dispatchers"** | CLAUDE.md §11 + Compose R1 PR #544 lessons-learned + ADR-039 | **Path C — Comply** | R2 MUST NOT re-introduce a Compose-specific AI dispatch endpoint. All AI actions flow through the Assistant pane via the session-dispatch seam. This is the load-bearing architectural constraint of R2's AI surface; §7.2's explanation block exists so no developer regresses it. |

**Actions**:
- File R1 spec.md amendment as part of R2 closeout (or earlier) — for the two Word-native non-goals.
- Reference PR #544 lessons-learned + the §7.2 explanation block in R2 code review guidance so reviewers reject any re-introduction of a Compose-specific AI dispatch endpoint.
- Carry the §8 AnchoredAnnotation deviation (Path A) into spec.md's ADR Tensions section for explicit reviewer sign-off.

---

## 10.5 Placement Justification (per CLAUDE.md §10)

All R2 endpoints belong in `Sprk.Bff.Api`. No new microservice. No Dataverse plugin handlers.

**Justification**:
1. **All R2 endpoints touch SPE (Graph API) and Dataverse** — both require BFF infrastructure (OBO/app-only auth, Graph client factory, Dataverse SDK).
2. **Open XML SDK runs server-side** — DOCX manipulation in browser is infeasible at our scope (file sizes, dependencies, security). BFF is the natural host.
3. **AI dispatch = Binding resolution via the shipped session-dispatch seam** (§7.2) — `SessionDispatchOrchestrator` → `IConsumerRoutingService` → prompted executor, all core-owned `Services/Ai/` internals that Compose consumes at the HTTP seam. Compose services in `Services/Compose/` never inject AI internals directly — the ADR-013 facade boundary is now a **Tier-1 CI-blocking NetArchTest rule** (merged 2026-07-08), so a violation fails the build.
4. **SPE webhook subscriptions terminate on BFF** — only stable inbound surface; not a separate service.
5. **Publish-size impact estimate**: baseline is **49.63 MB compressed incl. PDBs** (2026-07-08, ADR-029 re-baseline). +3–5 MB (Open XML SDK + OpenXmlPowerTools) ≈ **53–55 MB** — under the 60 MB hard ceiling but **brushing the ≥55 MB architecture-review trigger** (CLAUDE.md §10). Mitigation lever if triggered: the PDB-exclusion option (−3.76 MB) exists and is the first move before any architecture review. Will measure per-task.
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
| Assistant pane | `ConversationPane` (R1) | + `compose_action_request` consumption + OutcomeCard rendering + coordination-prompt response formatter |
| Context pane | `@spaarke/legal-workspace` panes | + New section: `compose-playbook-comparison`; + hosting the core's D-F4 decision-traceability view |
| Auth | `@spaarke/auth` (R1) | — |
| BFF | `Sprk.Bff.Api` (R1) | + `Services/Compose/` directory (extends existing R1 `ComposeService` + `StaleCheckoutSweeperHostedService`); new endpoints (§12) — none for AI dispatch |
| Session persistence | Session ledger (ADR-040) over the three-tier stack | + `anchoredAnnotations` document-adjacent state (§8); action history = ledger queries (no new structure) |
| Insight persistence | Core Memory Service workspace scope + gated `memory.write` tool (core D-M1/D-M3) | Compose-derived insights as governed MemoryItems (consumed contract — no local variant) |
| SPE facade | `SpeFileStore` + `ISpeFileOperations` (extended in PR #544 with `ReplaceFileContentAsUserAsync`) | Reused by R2's DOCX writer (annotation apply → save via `ReplaceFileContentAsUserAsync`) |
| AI dispatch | **Session-dispatch seam** (`dispatchConsumer` + `POST /api/ai/chat/sessions/{id}/dispatch` + `SessionDispatchOrchestrator`) — shipped, redesign-r1 | — (reused verbatim; §7.2) |
| AI routing | **Binding rows + `IConsumerRoutingService`** (canonical resolver, ADR-039) | + 3 new `sprk_playbookconsumer` Binding rows |
| AI action prompts | `sprk_analysisaction` Action rows + prompted executor (`ActionRunner`/`PromptSchemaRenderer`) | + 3 new Action rows (executor reused verbatim) |
| Document text extraction | `IDocumentTextSource` + `ITextExtractor` (platform) | — (reused; supports `.docx`) |
| Completion / confirmation / trace UX | **Core A0 contracts (consumed)**: `OutcomeCard v1` · `JobAwareCompletionState v1` · `GateDecision v2` (Policy v2 Tier 2c) · `TraceEvent v1` · `ComposeDisposition v1` + SSE frame | Compose-side rendering only — contracts are consumed, never forked (charter §3.4) |
| DOCX engine (annotation writer/reader) | NET-NEW: Open XML SDK 3.x + Codeuctivity.OpenXmlPowerTools | NEW (R2) — both MIT. Distinct from `ITextExtractor`: this SDK is for **writing** track changes / comments back into DOCX, whereas `ITextExtractor` is for **extracting plain text** as LLM input. |
| Open-in-Word | `useDocumentActions` shared lib (R1 extracted to `@spaarke/document-operations`) | — |
| SPE access | Existing Graph + R1 plumbing | + Webhook subscriptions + delta query handler |
| Document creation at ingestion parity | Existing Document Upload pipeline stages (SPE storage, profile analysis, indexing) + Job Contract / `ServiceBusJobProcessor` | NEW (R2) — the capability that composes them behind one Tier-2c gate (§2.7); core enforces the R-2 invariant |
| JPS scopes | `compose-selection`, `compose-document` (R1) | + Enriched `description` fields per "prompt = description" pattern |
| Catalog authoring | `jps-action-create` / `jps-validate` skills + BA catalog editor / Dataverse MCP, mirror-first under `infra/dataverse/inputschemas/` (§7.4) | Author 3 Action rows + 3 Binding rows, each with eval cases |
| Playbook authoring | — | NOT applicable — engine frozen, no new playbooks ever (§7.3) |
| LLM editing patterns | adopt from adeu (reference only, NOT code dependency) | NEW (R2) — `ComposeEditValidator`, `ComposeEditBatch`, `ComposeEditTransaction` in BFF |

---

## 12. BFF Surface (R2)

### 12.1 AI dispatch — ZERO new Compose endpoints (ANSWERED, permanently)

R2 introduces **no new AI dispatch endpoint**. The server dispatch surface is the **shipped Click-path session-dispatch seam**: client `dispatchConsumer(bindingId, args)` → `POST /api/ai/chat/sessions/{sessionId}/dispatch` → `SessionDispatchOrchestrator` → Binding resolution → prompted executor — with SSE streaming, ledger write, and gate integration built in (§7.2). This question is **answered, not open** — the former Open Item #7 is deleted; Spike 0 (§13) is a half-day *validation* of the shipped path, not a selection exercise. The `POST /api/compose/action/{consumerType}` endpoint retired in PR #544 stays retired; the §7.2 explanation block is binding review guidance against any regression.

### 12.2 Word-native annotation endpoints (NEW in R2)

| Endpoint | Purpose |
|---|---|
| `POST /api/compose/document/{spe-id}/push-annotations` | **NEW** — applies pending Compose annotations to DOCX as `<w:comment>` and `<w:ins>`/`<w:del>` via Open XML SDK; saves to SPE via `SpeFileStore.ReplaceFileContentAsUserAsync` (added in PR #544) with `If-Match` etag |
| `POST /api/compose/document/{spe-id}/pull-annotations` | **NEW** — parses incoming DOCX from SPE via Open XML SDK; extracts annotations; returns structured annotation payload to Compose UI for re-anchoring |
| `POST /api/compose/webhooks/spe-doc-changed` | **NEW** — SPE webhook receiver; enqueues delta query and downstream re-anchor work |
| `POST /api/compose/document/{spe-id}/check-changes` | **NEW** — explicit poll variant (in case webhook fails or for testing); BFF compares stored etag vs current SPE etag |
| `POST /api/compose/edit-batch/validate` | **NEW** — validates LLM-proposed edit batch against current document state (ambiguity check, match_mode per adeu Pattern §6.1); returns structured errors with recovery paths. Called before the Workspace materializes the pending track-change from the stored compose-disposition ledger entry, so the client can present recovery UX if validation fails. |
| `GET /api/compose/session/{matter-id}/{thread-id}/derived-insights` | **NEW** — queries workspace-scope memory items + ledger output refs for Context pane rendering |

### 12.3 Reuse from R1 (post-cleanup)

- `GET /api/documents/{id}/open-links` — open-in-Word (existing endpoint, no R2 changes)
- `GET /api/compose/documents/{documentSpeId}` — load DOCX (existing R1; unchanged by PR #544)
- `POST /api/compose/documents/{documentSpeId}/save` — save DOCX (existing R1; internally uses `SpeFileStore.ReplaceFileContentAsUserAsync` post PR #544; **R2 extends only the Compose-side annotation-apply orchestration, NOT the endpoint contract**)
- `POST /api/compose/documents/{documentSpeId}/promote` — first-save promotion (existing R1; **R2 raises its bar to full ingestion parity per §2.7(c)** — the promotion becomes the document-creation capability)
- `POST /api/compose/documents/{documentId}/checkout` + `/checkin` — Phase-5 stubs from R1 (return 501; callers use `/api/documents/{id}/checkout` from `DocumentCheckoutService`)
- `POST /api/compose/document/{documentId}/heartbeat` — checkout heartbeat (existing R1)

### 12.4 Explicitly retired (do NOT restore in R2)

| Retired surface | Retired in | Rationale |
|---|---|---|
| `POST /api/compose/action/{consumerType}` | PR #544 | Parallel dispatch endpoint — superseded by the session-dispatch seam (§7.2); ADR-039 bans its return |
| `POST /api/compose/upload` (R2-reserved stub) | Existing R1 stub, unchanged | Compose upload continues to route through the Assistant upload pipeline; chat-uploaded files reach Compose only via the §2.7(d) ingestion-parity affordance |

---

## 13. Spike Plan

Phase 0 (dispatch validation) + Phase 1 (LLM patterns) + Phase 2 (DOCX shuttle) spikes + benchmark integration. ~4 days total.

### Phase 0 spike — Dispatch-path validation (fast, unblocks Phase 1)

| # | Spike | Days | Decision unlocked |
|---|---|---|---|
| 0 | **Validate the shipped session-dispatch path with one Compose Binding**: author a throwaway `compose-explain-clause` Action + Binding pair (mirror-first, §7.4); wire a stub toolbar button → PaneEventBus `compose_action_request` → `ConversationPane` → `dispatchConsumer(bindingId, args)` → `POST /api/ai/chat/sessions/{id}/dispatch`; confirm SSE streaming into the chat surface, ledger `SessionOutput` write, and gate non-interference for a Tier-0/1 action. This is a **validation of the shipped path, not a selection exercise** — the server surface is answered (§12.1); the spike proves the Compose-specific legs (event payload shape, scope payload assembly, selection args). | 0.5 | Confirms the end-to-end seam before Phase 1 builds on it |

### Phase 1 spikes — LLM patterns (priority)

| # | Spike | Days | Decision unlocked |
|---|---|---|---|
| 1 | Author one R2 Action row + Binding (e.g., `compose-explain-clause`) with adeu-style behavioral prompts + structured OutputSchemaJson; verify the dispatch seam returns schema-valid JSON reliably | 0.5 | Prompt-pattern validation; JPS scope description format; Action schema shape |
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

## 14. Q&A Resolutions (locked from R2 design discussion; dispatch rows re-based 2026-07-08)

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
| **Three new R2 capabilities** | `compose-explain-clause`, `compose-compare-to-playbook`, `compose-draft-alternative` — each an `sprk_analysisaction` Action row + `sprk_playbookconsumer` Binding row pair (NOT playbooks — the engine is frozen; see §7) |
| **AI actions in R2** | 3 (Explain, Compare, Draft Alternative); Document Q&A is stretch goal (§2.8 — no new capability, rides the agent loop) |
| **AI dispatch path** | **Binding resolution via the shipped session-dispatch seam** (`dispatchConsumer` → `POST /api/ai/chat/sessions/{id}/dispatch` → `SessionDispatchOrchestrator` → `IConsumerRoutingService` → prompted executor). Rationale: ADR-039 (Accepted) — the Binding table is the ONLY routing config surface; redesign-r1 task 040 moved ALL consumers onto Bindings; the seam provides SSE + ledger + gate for free. `ActionRunner`/`PromptSchemaRenderer` remains the prompted executor. |
| **Consumer routing** | **R2 ADDS 3 Binding rows** (one per Action) — the Binding IS the dispatch config (ADR-039), not legacy. The R1 `compose-summarize / default` row is LIVE under redesign-r1 catalog governance (name-keyed mirror, targets Document Summary) — governed there, not an R2 dependency. |
| **AI dispatch endpoint** | ZERO new endpoints — **answered, not open**. Dispatch goes through the shipped session-dispatch seam; Spike 0 validates it (no selection to make). See §7.2's binding explanation block for why a new endpoint can never be added. |
| **DOCX text extraction** | Platform `IDocumentTextSource` + `ITextExtractor` — NOT a Compose-specific extractor. R1's `IDocxTextExtractor` was retired in PR #544 as redundant with platform coverage. |
| **Word-native annotations** | YES in R2 (amends R1 non-goals; competitive necessity) |
| **Round-trip from Word** | YES in R2 (annotation re-anchoring + conflict UX banner) |
| **Memory richness** | Ledger + workspace-scope memory + Compose-domain anchored annotations (§8): action history = session ledger queries (never duplicated); insights = governed MemoryItems via gated `memory.write`; anchored annotations = document-adjacent Compose state (explicit §8 deviation note) |
| **Confirmation / completion / trace** | Three moments, three surfaces (§2.4): gate's ONE dialog (Policy v2 Tier 2c, preview inside it) · job-aware OutcomeCard in the transcript · Context pane as audit surface (D-F4 trace view). No bespoke confirm banners anywhere. |
| **Document creation** | First-class R2 feature at FULL ingestion parity (§2.7, ruling R-2) — first save of a transient draft runs the capability; chat-uploaded files get the D-F0(d) "create it?" affordance; bare `sprk_document` rows are never success (R5-E bar) |

---

## 15. Open Items for Next Discussion

These need user decision or further investigation before `spec.md`:

1. **Document Q&A stretch goal**: include in R2 scope, or pure R3+ deferral? (Cheap under the §2.8 framing — rides the agent loop with the document session-mounted; the real work is highlight-UX polish.)
2. **Defined-terms surface**: include as Context pane addition in R2 (parity feature with Legora), or R3 deferral?
3. **`compose-summarize-word-changes` capability** for return-from-Word: include as R2 deliverable (NEW Action + Binding pair), or just show diff?
4. **Anchored-annotation re-anchoring confidence threshold**: at what fuzzy-match confidence do we flag an annotation as "needs review" vs auto-anchor? (Spike #6 informs.)
5. **Multiple-action concurrency**: if user invokes "Compare to playbook" and "Draft alternative" rapidly, do they queue serially or run in parallel in the Assistant pane? Design implication for `ConversationPane`.
6. **Core seam timing**: confirm with the core project when Phase A0 publishes `ComposeDisposition v1` + `JobAwareCompletionState v1` + the triple-twin hoist (charter §10 row 15) — Compose's catalog rows and draft-into-editor leg sequence behind those; Phase 0/1 spikes do not.

**Resolved since 2026-07-03** (kept for audit trail):

- ~~Action-log retention policy~~ — moot: the action log IS the session ledger (§8); retention follows ADR-040 / ADR-015 Tier 3 semantics, owned by the core.
- ~~Server-side dispatch surface (former Open Item #7)~~ — **DELETED as an open item**: answered by the shipped session-dispatch seam (§7.2, §12.1). Option (B) "new `/api/ai/action/execute`" was the parallel-endpoint anti-pattern PR #544 deleted and ADR-039 bans.
- ~~`compose-summarize` row cleanup / R7 coordination (former Open Item #8)~~ — no R7 coordination exists (R7 is closed; its work was absorbed by redesign-r1). The `compose-summarize / default` Binding row is LIVE and governed by redesign-r1 **catalog governance** (name-keyed mirror reconciliation; targets the Document Summary playbook — see `projects/spaarke-ai-architecture-redesign-r1/notes/catalog-governance.md`). It belongs to catalog governance, not to this project — **not an R2 dependency**.
- ~~AnalysisAction row deployment mechanism (former Open Item #9)~~ — resolved by §7.4: `jps-action-create` + BA catalog editor / Dataverse MCP, mirror-first under `infra/dataverse/inputschemas/`, eval cases per row, `OpenAiFunctionSchemaValidator` compliance; `Seed-JpsActions.ps1` is retired (deleted); rows author THROUGH the core's hoisted source once the triple-twin hoist lands (core Phase A, before catalog rows).

---

## 16. Vision Roadmap (post-R2)

| Release | Theme | Headline deliverables |
|---|---|---|
| **R2 (this project)** | AI actions + Word-native interop + memory continuity | 3 AI actions; Word-native annotation push/pull; round-trip; document creation at full ingestion parity; rich session memory |
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
- [`../spaarkeai-compose-r1/design.md`](../spaarkeai-compose-r1/design.md) — R1 design (foundation R2 builds on; absorbed/superseded per core charter D-C4)
- [`../spaarkeai-compose-r1/spec.md`](../spaarkeai-compose-r1/spec.md) — R1 spec (carries the non-goals R2 amends)
- [`../spaarke-ai-architecture-redesign-r2/design.md`](../spaarke-ai-architecture-redesign-r2/design.md) — r2 CORE charter v0.3 (§8 = this project's formal charter input)
