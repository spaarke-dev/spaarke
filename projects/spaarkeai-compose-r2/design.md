# Spaarke Compose R2 — Design (Working Document)

> **Status**: DRAFT — refinement document. Not yet a committed spec.
> **Codename**: Spaarke Compose (continuing from R1)
> **Positioning**: AI-native legal drafting workspace
> **Project ID**: `spaarkeai-compose-r2`
> **R2 Theme**: **The differentiation layer activates.** R1 shipped the workspace foundation; R2 makes it AI-native and Word-interoperable. Compose now does the work the foundation was built for.
> **Owner**: Ralph Schroeder
> **Last updated**: 2026-06-29
> **R1 reference**: [`../spaarkeai-compose-r1/design.md`](../spaarkeai-compose-r1/design.md) + [`../spaarkeai-compose-r1/spec.md`](../spaarkeai-compose-r1/spec.md)

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
- **Workspace**: Selection highlighted; persistent annotation marker added (clickable to replay explanation); inline toolbar dismisses after click
- **Assistant**: Streams explanation; offers follow-up actions ("Compare to playbook?", "Draft alternative?")
- **Context**: **Sources surfaced (per §2.0 provenance pattern)** — related precedent clauses from matter; relevant golden references from `spaarke-rag-references` index; click-to-navigate to source

**Playbook**: `compose-explain-clause` — NEW playbook (R2 deliverable)
**JPS scope**: `compose-selection` (defined in R1)
**Consumer type**: `compose-explain-clause` (new entry in [`ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs))

**Why it matters**: Lowest-effort AI action; universal use; demonstrates Workspace → Assistant flow cleanly.

---

### 2.2 Compare to Playbook

**User story**: User selects a clause (e.g., indemnification, governing law). **Inline AI toolbar appears** (per §2.0); user clicks "Compare to playbook". Assistant compares the selection against firm/matter playbook clauses; Context pane lights up with matches, deviations, and risk scores.

**Three-pane choreography**:
- **Workspace**: Selection highlighted; risk-level annotation marker added; inline toolbar dismisses after click
- **Assistant**: Streams analysis; offers "Replace with standard?" or "Negotiate this?" follow-ups
- **Context**: **Lights up with full source attribution (per §2.0)** — exact playbook entry that matched (click to navigate); clause text comparison side-by-side; deviation summary; risk score with rationale; relevant golden references; prior negotiation history if available — all clickable sources

**Playbook**: `compose-compare-to-playbook` — NEW playbook (R2 deliverable)
**JPS scope**: `compose-selection` + matter context (existing)
**Consumer type**: `compose-compare-to-playbook`
**Resources hooked into**:
- Matter playbook library (existing `sprk_analysisplaybook` entity)
- Context pane section: new `compose-playbook-comparison` registration (Context-pane component registry)
- Optional: precedent doc retrieval (R3+ — defer)

**Why it matters**: **The Spaarke-exclusive flow.** Competitors don't have JPS playbooks as a first-class concept. This is where the three-pane coordination shines.

---

### 2.3 Draft Alternative

**User story**: User selects clause text. **Inline AI toolbar appears** (per §2.0); user clicks "Draft alternative". Assistant proposes alternative language; the suggestion appears in Workspace as a pending track-change (highlighted insertion + deletion). User accepts (becomes part of doc state) or rejects (suggestion disappears).

**Three-pane choreography**:
- **Workspace**: Selection becomes a pending **insertion/deletion pair** rendered as track-change marks; inline toolbar dismisses; accept/reject mini-controls appear inline near the suggestion
- **Assistant**: Streams alternative text with rationale; offers "Refine further?" follow-up
- **Context**: **Full source attribution (per §2.0)** — exact playbook clause that informed the draft; golden references / precedent matters cited; LLM rationale trace; all clickable + citable (drag a source into the doc as an inline citation if accepting)

**Playbook**: `compose-draft-alternative` — NEW playbook (R2 deliverable)
**JPS scope**: `compose-selection` (defined in R1)
**Consumer type**: `compose-draft-alternative`
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
| **Workspace → Assistant** | Wire only | Selection → **inline AI toolbar appears** (per §2.0); offers contextual playbook actions (Explain, Compare, Draft) |
| **Context → Workspace** | Wire only | Drag precedent clause / golden reference from Context → drops into editor as inline citation; click on Context entry navigates Workspace |
| **Context → Assistant** | Wire only | "Use this precedent" → Assistant takes Context entry as input to next action |
| **Assistant → Workspace** | Wire only | AI draft inserts into editor as pending track-change **with provenance link** (clickable to source per §2.0) |
| **Assistant → Context** | Wire only | AI-derived insight persists to session memory; surfaces in Context **with full source attribution** (playbook entry, golden reference, precedent — clickable) |

**Binding architectural rule**: every R2 feature lights up at least two of these six flows. Features that don't are flagged for redesign — three-pane is the differentiator, not an optional layer.

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
| Two JPS scopes: `compose-selection`, `compose-document` | + Three new playbook actions consuming those scopes |
| One consumer type wired E2E: `compose-summarize` → Document Summary | + 3 new consumer types: `compose-explain-clause`, `compose-compare-to-playbook`, `compose-draft-alternative` |
| Open-in-Word handoff via existing `/api/documents/{id}/open-links` | + Push-to-Word annotation infrastructure (NEW BFF service) |
| SPE plumbing (load, save, promote-on-Save) | + SPE webhook subscription + delta query for return-from-Word detection |
| Three-pane coordination wire-only | + Activated flows per §3 |
| Modal entry, single-session lock, etc. | + Conflict UX banner for return-from-Word edits |

R2 does NOT redo any R1 work. R2 layers on top.

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

## 7. Playbook + Consumer-Routing Expansion

R2 introduces three new Compose-specific consumer types, each linked to a dedicated playbook. Following the R1 pattern (one wired E2E in R1 — `compose-summarize` → Document Summary playbook), R2 expands to four total active consumer types.

| Consumer Type | Playbook | New or Existing? | JPS Scope Consumed |
|---|---|---|---|
| `compose-summarize` (R1) | Document Summary (id `47686eb1-9916-f111-8343-7c1e520aa4df`) | Existing playbook (R1 reused) | `compose-document` |
| **`compose-explain-clause`** (R2) | `Compose Explain Clause` — NEW playbook | NEW | `compose-selection` |
| **`compose-compare-to-playbook`** (R2) | `Compose Playbook Comparison` — NEW playbook | NEW | `compose-selection` + matter context |
| **`compose-draft-alternative`** (R2) | `Compose Draft Alternative` — NEW playbook | NEW | `compose-selection` |

**R2 playbook-authoring deliverables**:
- 3 new `sprk_analysisplaybook` records (NEW playbooks designed + deployed)
- 3 new `sprk_playbookconsumer` rows (linking consumer types to playbooks)
- 3 new constants in [`ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs)
- 1 expanded BFF endpoint: `POST /api/compose/action/{consumerType}` (R1 wired the smoke test; R2 extends to all four consumer types)

**Playbook authoring approach**: use [`jps-playbook-design`](../../.claude/skills/jps-playbook-design/SKILL.md) skill for each new playbook. Verify via [`jps-validate`](../../.claude/skills/jps-validate/SKILL.md). Audit existing patterns via [`jps-playbook-audit`](../../.claude/skills/jps-playbook-audit/SKILL.md).

**Tool-description discipline** (per adeu Pattern "tool descriptions ARE the prompt"): each new playbook's JPS scope `description` field is treated as LLM behavioral guidance, not metadata. Includes recovery paths and critical gotchas.

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
| **`IConsumerRoutingService`** | Existing (R1) | Dispatches Compose consumer types to playbooks |
| **`IInvokePlaybookAi`** | Existing (R1) | Non-streaming playbook execution facade |
| **JPS scope catalog** | Existing | `compose-selection`, `compose-document` (R1); descriptions enriched in R2 per "prompt = description" pattern |
| **`sprk_playbookconsumer` table** | Existing (R1) | New rows for R2 consumer types |
| **`sprk_analysisplaybook` table** | Existing | New playbook records for R2 (3 new) |
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
| **ADR-013 AI facade discipline (refined 2026-05-20)** | ADR-013 (refined) | **Path C — Comply** | R2 consumes AI through `IConsumerRoutingService` + `IInvokePlaybookAi` facades only. No direct injection of `IOpenAiClient` etc. into Compose CRUD code. |

**No new ADR tensions discovered.** R1 spec amendments are non-ADR Path B work; the licensing concern resolves cleanly with our editor + DOCX library choices.

**Action**: file R1 spec.md amendment as part of R2 closeout (or earlier).

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
| BFF | `Sprk.Bff.Api` (R1) | + `Services/Compose/` directory; new endpoints (§12) |
| ChatSession persistence | Three-tier (R1) | + Rich payload schema (R2) |
| Consumer routing | `IConsumerRoutingService` (R1) | + 3 new `ConsumerTypes` constants |
| Playbook execution | `IInvokePlaybookAi` (R1) | — |
| DOCX engine | NET-NEW: Open XML SDK 3.x + Codeuctivity.OpenXmlPowerTools | NEW (R2) — both MIT |
| Open-in-Word | `useDocumentActions` shared lib (R1 extracted to `@spaarke/document-operations`) | — |
| SPE access | Existing Graph + R1 plumbing | + Webhook subscriptions + delta query handler |
| JPS scopes | `compose-selection`, `compose-document` (R1) | + Enriched `description` fields per "prompt = description" pattern |
| Playbook authoring | `jps-playbook-design` / `jps-validate` / `jps-playbook-audit` skills (existing) | — |
| LLM editing patterns | adopt from adeu (reference only, NOT code dependency) | NEW (R2) — `ComposeEditValidator`, `ComposeEditBatch`, `ComposeEditTransaction` in BFF |

---

## 12. BFF Surface (R2)

| Endpoint | Purpose |
|---|---|
| `POST /api/compose/action/{consumerType}` | Extended to handle `compose-explain-clause`, `compose-compare-to-playbook`, `compose-draft-alternative` (R1 already handles `compose-summarize`) |
| `POST /api/compose/document/{spe-id}/push-annotations` | **NEW** — applies pending Compose annotations to DOCX as `<w:comment>` and `<w:ins>`/`<w:del>`; saves to SPE with `If-Match` |
| `POST /api/compose/document/{spe-id}/pull-annotations` | **NEW** — parses incoming DOCX from SPE; extracts annotations; returns structured annotation payload to Compose UI for re-anchoring |
| `POST /api/compose/webhooks/spe-doc-changed` | **NEW** — SPE webhook receiver; enqueues delta query and downstream re-anchor work |
| `POST /api/compose/document/{spe-id}/check-changes` | **NEW** — explicit poll variant (in case webhook fails or for testing); BFF compares stored etag vs current SPE etag |
| `POST /api/compose/edit-batch/validate` | **NEW** — validates LLM-proposed edit batch against current document state (ambiguity check, match_mode); returns structured errors with recovery paths |
| `GET /api/compose/session/{matter-id}/{thread-id}/derived-insights` | **NEW** — extended ChatSession query returning derived insights for Context pane rendering |

**Reuse from R1**:
- `GET /api/documents/{id}/open-links` — open-in-Word (existing endpoint, no R2 changes)
- `POST /api/compose/document/upload` — Assistant upload path (existing)
- `GET /api/compose/document/{spe-id}` — load DOCX (existing)
- `PUT /api/compose/document/{spe-id}` — save DOCX (extended in R2 to apply annotations before save)
- SPE checkout/checkin endpoints (existing)

---

## 13. Spike Plan

Phase 1 (LLM patterns) + Phase 2 (DOCX shuttle) spikes + benchmark integration. ~3 days total.

### Phase 1 spikes — LLM patterns (priority)

| # | Spike | Days | Decision unlocked |
|---|---|---|---|
| 1 | Author one R2 playbook (e.g., `compose-explain-clause`) with adeu-style behavioral prompts; verify LLM produces structured payloads reliably | 0.5 | Prompt-pattern validation; JPS scope description format |
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

**Total spikes: 8 × half-day = 4 days.**

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
| **Three new R2 playbooks** | `Compose Explain Clause`, `Compose Playbook Comparison`, `Compose Draft Alternative` (all new) |
| **AI actions in R2** | 3 (Explain, Compare, Draft Alternative); Document Q&A is stretch goal |
| **Word-native annotations** | YES in R2 (amends R1 non-goals; competitive necessity) |
| **Round-trip from Word** | YES in R2 (annotation re-anchoring + conflict UX banner) |
| **Memory richness** | Rich `ChatSession` payload (anchored annotations, action log, derived insights) — extends R1 binding |

---

## 15. Open Items for Next Discussion

These need user decision or further investigation before `spec.md`:

1. **Document Q&A stretch goal**: include in R2 scope, or pure R3+ deferral? (Depends on whether semantic retrieval over document content is in R2 budget.)
2. **Defined-terms surface**: include as Context pane addition in R2 (parity feature with Legora), or R3 deferral?
3. **Action log retention policy**: how long does the action log persist in Cosmos / Dataverse? Same TTL as ChatSession (90 days warm, indefinite cold)?
4. **`compose-summarize-word-changes` playbook** for return-from-Word: include as R2 deliverable, or just show diff?
5. **Anchored-annotation re-anchoring confidence threshold**: at what fuzzy-match confidence do we flag an annotation as "needs review" vs auto-anchor? (Spike #6 informs.)
6. **Multiple-action concurrency**: if user invokes "Compare to playbook" and "Draft alternative" rapidly, do they queue serially or run in parallel? Design implication for `ConversationPane`.

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
