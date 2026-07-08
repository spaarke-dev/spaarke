# Spaarke Compose R2 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-08
> **Source**: [`design.md`](./design.md) (working design, refined + code-grounded across the 2026-07-08 owner-review + verification sessions)
> **Project ID**: `spaarkeai-compose-r2`
> **Binding foundations**: [ADR-039 Grounded Execution & Closed Catalogs] · [ADR-040 Session Ledger] (both Accepted)
> **Charter input**: r2 CORE charter §8 (Compose handoff package)

---

## Executive Summary

R2 turns Spaarke Compose from a foundation editor into an **AI-native legal drafting workspace with Word-native interoperability and cross-session memory continuity**. It delivers five one-shot AI actions on document text (all as Action+Binding catalog rows dispatched through the *shipped* session-dispatch seam — **zero new dispatch endpoints**), inline redline editing with undo/replace, three fully-wired document entry paths, document creation at full ingestion parity (create-on-save), Word-native comment/track-change push+pull with return-from-Word re-anchoring, and rich session memory. It builds ON the R1 foundation and the redesign-r1 as-built platform; it reintroduces nothing PR #544 retired.

---

## Scope

### In Scope

**Entry paths & document lifecycle**
- Wire the two non-functional in-workspace entry buttons (1a): **Browse / open file** and **Search for Document**.
- Wire Assistant upload → Compose (1b): uploaded file **mounts transiently** into the editor (no SPE round-trip to render).
- **Draft-into-editor from chat** via the core's `compose` disposition (ledger-first).
- **Create-on-save at full ingestion parity** (container from business unit + optional parent-association prompt + `sprk_document` + profile analysis + indexing), upgrading the existing `PromoteIfEphemeralAsync`.

**AI actions (5 Action+Binding pairs)** — `compose-explain-clause`, `compose-compare-to-playbook`, `compose-draft-alternative`, `compose-summarize-word-changes`, `compose-defined-terms`. Eval cases per row.

**Inline editing UX** — TipTap `BubbleMenu` inline AI toolbar; custom ProseMirror marks (`insertion`, `deletion`, `commentAnchor`); pending track-changes materialized from the ledger with `{bindingId}@t{n}` provenance; **undo/replace of a prior AI edit** via ledger supersession; serial action queueing.

**LLM editing patterns (Phase 1)** — `IComposeEditValidator` (`match_mode` + structured ambiguity errors), `ComposeEditBatch` (4-phase atomic pipeline), `ComposeEditTransaction` (snapshot/rollback), `SemanticAppendixGenerator`, CriticMarkup read-direction, enriched JPS scope descriptions.

**Word interop (Phase 2)** — `DocxAnnotationWriter` / `DocxAnnotationReader` (Open XML SDK); push/pull endpoints; SPE webhook + delta + renewal; return-from-Word re-anchoring with confidence bands; conflict banner.

**Memory & provenance** — anchored annotations (Compose-domain), workspace-scope MemoryItems via gated `memory.write`, action history via ledger queries, always-visible Context-pane provenance + D-F4 trace hosting.

**Three-pane coordination** — activate the six coordinated flows (design §3) with the D-F3 UI-ack contract.

**Stretch** — Document Q&A over the session-mounted document (rides the agent loop; no new capability).

### Out of Scope

- **Clause library** (pick/insert standard clauses) and the **cursor-position insertion toolbar** — deferred to a follow-on project; R2 preserves extensibility so they can be added without rearchitecting.
- **New `sprk_analysisplaybook` records** — the playbook engine is FROZEN (no new playbooks ever). `compose-compare-to-playbook` *reads* existing playbooks as reference data.
- **New AI dispatch endpoint** — ADR-039 bans it; the retired `POST /api/compose/action/{consumerType}` stays retired.
- **Word's authoring UX at parity** (tracked-changes authoring, comments authoring, footnotes/cross-refs, complex/final formatting, redline comparison) — per the §1.6 fidelity boundary these REQUIRE Word; Compose round-trips them, it does not reproduce them.
- **Interactive input surfaces in the Context pane** — it stays audit-only.

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/Compose/` — NEW: `IComposeEditValidator`, `ComposeEditBatch`, `ComposeEditTransaction`, `SemanticAppendixGenerator`, `DocxAnnotationWriter`, `DocxAnnotationReader`, `SpeSyncOrchestrator`; EXTEND: `ComposeService` (`PromoteIfEphemeralAsync` → full parity; upload/transient support).
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` — NEW annotation/webhook/validate/insights routes; activate `POST /api/compose/upload`.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/` — `ComposeEditor` (marks, BubbleMenu toolbar, undo/replace), `ComposeWorkspace` (entry-path callbacks, transient mount), `ComposeEmptyState` (wire Browse/Search).
- `src/solutions/SpaarkeAi/src/components/conversation/` — `ConversationPane` (`compose_action_request` consumption, OutcomeCard rendering, serial queue, coordination-prompt formatter); `useAttachments` / `SendWorkspaceArtifactHandler` (allow upload → transient Compose mount).
- `@spaarke/legal-workspace` Context pane — `compose-playbook-comparison` section, defined-terms display, D-F4 trace hosting.
- `infra/dataverse/inputschemas/` — 5 Action rows + 5 Binding rows (mirror-first) + eval cases.

---

## Requirements

### Functional Requirements

**Entry paths & document lifecycle**

1. **FR-01 — 1a Browse**: The empty-state "Browse / open file" button opens a local file picker; the chosen `.docx` mounts into the editor as a transient working draft (via the `docxBytes` mount seam). *Acceptance*: clicking Browse → selecting a `.docx` → content renders in TipTap; no `sprk_document` created until save.
2. **FR-02 — 1a Search**: The "Search for Document" button opens a Spaarke Document lookup; selecting a real `sprk_document` loads it via the existing `GET /api/compose/documents/{speId}` path (reuses 1c). *Acceptance*: search → select → file mounts, refresh-surviving, identical to 1c.
3. **FR-03 — 1b upload transient mount**: An Assistant-uploaded file, on "open in Compose", mounts its retained bytes into the editor transiently. The `send_workspace_artifact` refusal of session-upload ids is removed for the Compose-mount path; `POST /api/compose/upload` (currently 501) is activated as needed. *Acceptance*: upload a `.docx` in the Assistant → "open in Compose" → the file's content appears in the editor (no longer an empty tab).
4. **FR-04 — Draft-into-editor from chat**: Assistant-drafted content lands in the editor as a transient working draft via the core `compose` disposition (ledger-written before render, ADR-040). *Acceptance*: "draft an engagement letter into Compose" → content materializes in the editor from the stored ledger entry.
5. **FR-05 — Create-on-save at full ingestion parity**: First Save of a transient draft runs the document-creation capability (upgrade of `PromoteIfEphemeralAsync`): resolve an **SPE container from the user's business unit** (required), create `sprk_document`, run **document profile analysis**, run **indexing**, and **prompt (optional) to associate** to matter/project/invoice/work-assignment/none — a standalone Document is valid. Rendered as a per-step `JobAwareCompletionState` OutcomeCard. *Acceptance*: save a transient draft → OutcomeCard shows container/record/profile/indexing steps reaching completed; a bare `sprk_document` with no SPE file/profile/index is NEVER reported as success (R5-E bar).
6. **FR-06 — Save-back + open-in-Compose chain**: For uploaded/mounted-then-saved files the document is already open (no re-open); for chat-originated "save as document" flows, creation completion chains into the shipped chat→Compose bridge + pre-seed leg. *Acceptance*: the flagship gate's create leg ends with the document OPEN in Compose, never a bare record id.
7. **FR-06a — Upload fidelity on first save**: If an uploaded file is saved **unedited**, the pristine original bytes persist to SPE; once **edited**, the regenerated `.docx` (`tipTapToDocxBytes`) persists. *Acceptance*: upload → save without editing → SPE file byte-identical to upload; upload → edit → save → SPE file reflects edits.

**AI actions (catalog)**

8. **FR-07 — `compose-explain-clause`**: Action row (SystemPrompt + OutputSchemaJson `{explanation, keyConcepts[], relatedPlaybookIds[]}` + Temperature + ModelDeploymentId) + Click-path Binding; consumes `compose-selection`. *Acceptance*: select clause → "Explain" → schema-valid explanation streams into the Assistant; sources in Context.
9. **FR-08 — `compose-compare-to-playbook`**: Action+Binding; reads existing `sprk_analysisplaybook` entries as reference data (NOT a routing target); output `{matches[], overallRisk}`. *Acceptance*: select clause → "Compare to playbook" → matched playbook entry (clickable), deviations, risk score in Context.
10. **FR-09 — `compose-draft-alternative`**: Action+Binding declaring the `compose` disposition; output enforces `{target_text, new_text, match_mode, rationale, sources[]}` (adeu Pattern §6.1). *Acceptance*: select text → "Draft alternative" → structured edit payload ledger-written; Workspace materializes a pending track-change from the stored entry.
11. **FR-10 — `compose-summarize-word-changes`**: Action+Binding invoked by the return-from-Word flow; consumes `compose-document`; output `{summary, changes[]}`. *Acceptance*: after a Word round-trip, the flow produces a human-friendly change summary.
12. **FR-11 — `compose-defined-terms`**: Action+Binding; **triggered from the popover "More actions…" overflow (or a small doc-level command)** — NOT selection-based; output `{terms[], inconsistencies[]}` rendered **read-only in the Context pane**. *Acceptance*: trigger from overflow → term list + inconsistency flags appear in Context (read-only); Context remains a non-input surface.
13. **FR-12 — Catalog quality**: Each of the 5 rows ships **eval cases**: a golden-utterance family + a dispatch family, **≥5 cases each**. Schemas satisfy `OpenAiFunctionSchemaValidator`; property-level boolean `required` is BANNED. Rows author **mirror-first** under `infra/dataverse/inputschemas/`, THROUGH the core's hoisted description source. *Acceptance*: no row merges without eval coverage + schema validation.
14. **FR-13 — Dispatch path**: All 5 actions dispatch via **PaneEventBus → ConversationPane → `dispatchConsumer(bindingId, args)` → `POST /api/ai/chat/sessions/{id}/dispatch` → `SessionDispatchOrchestrator` → `IConsumerRoutingService.GetBindingByIdAsync` → prompted executor**. NO new endpoint; NO string-key resolution outside the Binding table. *Acceptance*: dispatch code adds zero new routes; a code-review checklist item enforces §7.2.

**Inline editing UX**

15. **FR-14 — Inline AI toolbar**: A TipTap `BubbleMenu` appears on selection with Explain / Compare to playbook / Draft alternative / "More actions…" (extensible overflow). Toolbar dismisses on clear; repositions to stay in viewport. *Acceptance*: selecting text shows the toolbar; the overflow is registration-extensible (clause actions can be added later without rearchitecting).
16. **FR-15 — Custom marks**: `insertion`, `deletion`, `commentAnchor` ProseMirror marks added to the editor schema. *Acceptance*: pending edits render as redline insertion/deletion; comments anchor to spans.
17. **FR-16 — Pending track-change materialization**: The Draft-Alternative edit renders as a pending insertion/deletion pair **materialized from the stored `compose`-disposition ledger entry**, carrying `{bindingId}@t{n}` provenance, with inline accept/reject. Refresh-durable. *Acceptance*: a pending suggestion survives a page refresh (materialized from the ledger, not a client-only buffer).
18. **FR-17 — Undo/replace a prior AI edit**: The Assistant supports "undo that / try another approach" — retracting the last AI-applied redline as a **ledger supersession** (supersede the prior `SessionOutput`, re-materialize from current ledger state), NOT a client-side DOM undo. *Acceptance*: "undo that and try another approach" removes the prior redline and applies a fresh proposal; both operations are ledger-durable.
19. **FR-18 — Serial action queueing**: Rapidly-invoked actions run one at a time in dispatch order; each streams to completion before the next starts. *Acceptance*: firing Compare then Draft in quick succession produces two ordered, non-interleaved streams and ordered ledger writes.

**LLM editing patterns (Phase 1)**

20. **FR-19 — Edit validator**: `IComposeEditValidator` supports `match_mode` (`strict`/`first`/`all`) and returns structured ambiguity errors (match count, ≤5 examples with context, copy-pasteable resolution). Exposed at `POST /api/compose/edit-batch/validate`. *Acceptance*: an ambiguous `target_text` returns an actionable error, not a silent wrong-match.
21. **FR-20 — Atomic batch pipeline**: `ComposeEditBatch` applies edits via resolve → sort-descending → skip-overlap → apply-bottom-up. *Acceptance*: multi-edit batches apply without offset drift.
22. **FR-21 — Snapshot/rollback**: `ComposeEditTransaction` — if any edit in a batch fails validation, none apply. *Acceptance*: an intentionally-failing batch leaves the document unchanged.
23. **FR-22 — Semantic appendix + CriticMarkup read direction**: `SemanticAppendixGenerator` enriches the `compose-document` scope payload (defined terms, cross-refs, structural metadata); existing track-changes render to the LLM inline as `{++/--/>>/<<}` CriticMarkup. *Acceptance*: measured hallucination reduction with appendix (Spike 4); LLM never emits CriticMarkup (produces structured payloads).
24. **FR-23 — Scope descriptions as prompt**: `compose-selection` and `compose-document` scope `description` fields carry behavioral guidance (recovery paths, gotchas, examples) and double as user-visible tooltips. *Acceptance*: the same description text primes the LLM and renders as the toolbar tooltip.

**Word interop (Phase 2)**

25. **FR-24 — Push annotations**: `DocxAnnotationWriter` writes `<w:comment>` + `<w:ins>`/`<w:del>` (correct author/date/id, comments-before-track-changes ordering, paragraph-boundary deletion handling) via Open XML SDK; `POST /api/compose/document/{spe-id}/push-annotations` saves to SPE via `ReplaceFileContentAsUserAsync` with `If-Match`. *Acceptance*: Word for Web renders pushed comments + track changes natively.
26. **FR-25 — Pull annotations**: `DocxAnnotationReader` parses incoming `<w:comment>`/`<w:ins>`/`<w:del>`; `POST /api/compose/document/{spe-id}/pull-annotations` returns a structured payload. *Acceptance*: a Word-added comment + track-change round-trips back with correct author/date.
27. **FR-26 — SPE change detection**: SPE webhook subscription on `drives/{containerId}/root` (`updated`, <4230-min lifespan + renewal cron) + delta query; `POST /api/compose/webhooks/spe-doc-changed`; `POST /api/compose/document/{spe-id}/check-changes` poll variant. *Acceptance*: a Word save fires the webhook → BFF enumerates changed driveItems.
28. **FR-27 — Return-from-Word re-anchoring**: On detected new version, reload + re-anchor prior Compose annotations using confidence bands **≥0.85 auto-anchor / 0.6–0.85 flag-for-review / <0.6 orphan** (bands tuned by Spike 6); surface a summary banner + conflict UX. *Acceptance*: banner reports "N re-anchored, M need review"; ambiguous anchors are flagged, not silently dropped.
29. **FR-28 — Confirm/completion/trace three surfaces**: Push-annotations and save-back are Policy v2 **Tier 2c** side effects → **one gate dialog** (preview inside it: what appears in Word vs stays in Compose); completion = **job-aware OutcomeCard** in the transcript; the **Context pane is audit-only** (D-F4 trace). No bespoke confirmation banners. *Acceptance*: exactly one confirmation dialog; completion is an OutcomeCard; Context never captures a decision.

**Memory & provenance**

30. **FR-29 — Anchored annotations**: Persist `anchoredAnnotations` in the Compose session payload (document-adjacent state; tenant+matter scoped); NOT a MemoryItem, never written via `memory.*`, never surfaced in the memory review/delete view. *Acceptance*: annotations restore across sessions + Word handoffs within drift tolerance.
31. **FR-30 — Workspace-scope insights**: AI-derived insights persist as workspace-scope `MemoryItem`s via the **gated `memory.write` tool** (core D-M3) with the full governance envelope; no local MemoryItem variant. *Acceptance*: an insight-persist is a governed, Policy-v2-visible side effect.
32. **FR-31 — Action history via ledger**: Action history is **queried from the session ledger** (`ToolChain` + `SessionOutput` refs), never duplicated in a parallel structure. *Acceptance*: no `actionLog`/`derivedInsight` stored structures exist; Compose reads the ledger.
33. **FR-32 — Provenance always visible**: Every AI recommendation surfaces sources in the Context pane (playbook entry, golden reference from `spaarke-rag-references`, precedent, prior decision, D-F4 execution trace) — clickable, citable, persistent. *Acceptance*: no AI recommendation renders without source surfacing.
34. **FR-33 — Compaction + cross-version persistence**: Long sessions use the compacted digest over ledger outputs; session state binds to `DocumentId + MatterId` (survives Word handoffs), not to a DOCX version. *Acceptance*: reopening after a Word round-trip restores prior decisions + annotations.

**Three-pane coordination**

35. **FR-34 — Activate coordinated flows + UI ack**: Activate the six flows (design §3); UI-affecting tool results (open tab, apply-edit render, navigation) complete only on a **client ack referencing the emitted frame id** (core D-F3), or fail honestly on timeout; PaneEventBus `correlationId`s carry the ack token. *Acceptance*: a claimed UI action that didn't happen fails honestly (no false "done").

**Stretch**

36. **FR-35 (stretch) — Document Q&A**: With the document session-mounted (a `Doc` ledger entry), the agent answers questions with citations per the ADR-039 grounded-output invariant; answers surface ephemeral highlights ("found in §7.3"). *Acceptance*: "what's the indemnification cap?" → cited answer + navigated highlight. No new capability/playbook.

### Non-Functional Requirements

- **NFR-01 — Publish size**: BFF publish ≤ **60 MB compressed** (HARD). Baseline 49.63 MB incl. PDBs (2026-07-08); Open XML SDK + OpenXmlPowerTools est. +3–5 MB → 53–55 MB (brushes the ≥55 MB architecture-review trigger; PDB-exclusion −3.76 MB is the first mitigation lever). Measure per BFF-touching task; report absolute + delta.
- **NFR-02 — CVE**: No new HIGH-severity CVE from `dotnet list package --vulnerable --include-transitive`.
- **NFR-03 — Latency (defaults, tunable in spikes)**: AI action first-token < 3 s / complete < 15 s; DOCX push/pull < 10 s for typical documents; return-from-Word re-anchor < 5 s.
- **NFR-04 — Auth**: All new endpoints `RequireAuthorization()` (except health/ping).
- **NFR-05 — AI facade (ADR-013)**: `Services/Compose/` NEVER injects `IOpenAiClient`, executor types, or `IConsumerRoutingService` directly — AI reached only via the session-dispatch HTTP seam + published core contracts. Enforced by the Tier-1 CI-blocking NetArchTest rule (merged 2026-07-08).
- **NFR-06 — Eval coverage**: Every catalog row ships golden + dispatch eval families, ≥5 cases each (gate to merge).
- **NFR-07 — Licensing**: Only MIT runtime deps (Open XML SDK, OpenXmlPowerTools, TipTap OSS incl. `BubbleMenu`). Zero commercial license fees.
- **NFR-08 — Test obligation**: Every new/modified `Services/Compose/` service has matching unit tests in `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/`.
- **NFR-09 — Schema validation**: All Action `OutputSchemaJson` pass `OpenAiFunctionSchemaValidator`; property-level boolean `required` BANNED (H1 outage lesson).

---

## Technical Constraints

### Applicable ADRs

- **ADR-039** (Accepted) — Grounded Execution & Closed Catalogs. One dispatch protocol, three entry paths (Event/Click/Text), two closed catalogs. Central to every AI action.
- **ADR-040** (Accepted) — Session Ledger. Storage precedes rendering; no parallel session cache. Governs draft-into-editor, edit payloads, undo/replace, action history.
- **ADR-013** (refined; Tier-1 NetArchTest) — AI facade discipline.
- **ADR-028** — Spaarke Auth v2.
- **ADR-038** — Testing strategy (integration-heavy pyramid; eval obligation; banned test shapes).
- **ADR-029** — Publish-size baseline/ceiling.
- **ADR-015** — Memory tiers (ledger retention semantics).
- **ADR-005 / 007 / 009** — SPE storage (webhooks, delta, checkout/checkin); Graph isolation; Redis-first caching.
- **ADR-032** — Null-Object Kill-Switch (if any Compose service is feature-gated).
- **ADR-001** — Minimal API + `BackgroundService` (no Azure Functions) — the SPE webhook renewal cron is a hosted service.
- **ADR-010** — DI minimalism (≤15 non-framework registrations) — consolidate `Services/Compose/` helpers.
- **ADR-021** — Fluent UI v9 + dark mode for all new UI.
- **ADR-028** — Spaarke Auth v2 client contract for all new client fetches.
- **ADR-030** — PaneEventBus (typed discriminants; no `any` payloads; four channels) — the `compose_action_request` / `compose_edit_apply_request` events.
- **ADR-031** — Stage lifecycle (`determineStage()`) — entry-path mount transitions respect it.
- **ADR-033** — Streaming chat-tool side channel — the `compose`-disposition SSE frame rides `ChatInvocationContext.DocumentStreamWriter`, not a new interface.

### Implementation-time ADR constraints (surfaced by `/adr-check`, 2026-07-08 — carry into tasks)

These are not spec violations; they are MUST-follow rules the plan/tasks apply once code is written:

- **ADR-007**: webhook/delta/`DocxAnnotationWriter`/`SpeSyncOrchestrator` MUST NOT leak `Microsoft.Graph` types above the `SpeFileStore`/Infrastructure facade.
- **ADR-001**: SPE webhook renewal (FR-26) is a `BackgroundService` (cf. `StaleCheckoutSweeperHostedService`), NOT a Function.
- **ADR-009**: webhook-subscription state / etag tracking / re-anchor metadata → Redis, not `IMemoryCache`, when cross-request.
- **ADR-010**: keep `Services/Compose/` DI additions minimal — internal helpers need not be DI-registered.
- **ADR-021**: BubbleMenu toolbar, empty-state buttons, OutcomeCard, Context sections, conflict banner → Fluent v9 + dark mode, no v8/hardcoded colors.
- **ADR-028**: entry-path + dispatch client fetches via `@spaarke/auth`, never raw `Bearer`.
- **ADR-031**: entry-path mount transitions honor `determineStage()`.
- **ADR-033**: `compose`-disposition SSE via the established `DocumentStreamWriter` side channel.
- **ADR-032**: any feature-gated Compose service consumed by an unconditional endpoint → P1/P2/P3 Null-Object.
- **ADR-029**: update the publish-size baseline ratchet when Open XML SDK + OpenXmlPowerTools land.

### MUST Rules

- ✅ MUST express every AI capability as an **Action row + Binding row** pair; the Binding is the ONLY routing config surface.
- ✅ MUST dispatch through the shipped session-dispatch seam; **MUST NOT** add any AI dispatch endpoint or string-key resolution outside the Binding table.
- ✅ MUST write edit payloads to the ledger (`compose` disposition) **before** rendering; undo = ledger supersession.
- ❌ MUST NOT inject AI internals into `Services/Compose/`.
- ❌ MUST NOT author new `sprk_analysisplaybook` records (engine frozen).
- ❌ MUST NOT make the Context pane an interactive input surface.
- ✅ MUST keep BFF publish ≤ 60 MB and verify per BFF-touching task.
- ✅ MUST ship eval cases + pass schema validation for every catalog row.

### Existing Patterns to Follow

- Mount seam: `ComposeEditor` `docxBytes` → `docxToTipTapHtml` → `setContent` (source-agnostic — reuse for transient upload mount).
- Reverse seam: `tipTapToDocxBytes(editor)` (save regeneration).
- Create-on-save backbone: `ComposeService.PromoteIfEphemeralAsync` (idempotent; extend to full parity).
- Load path: `GET /api/compose/documents/{speId}` → `ComposeService.LoadAsync` (OBO SPE).
- Dispatch seam: `dispatchConsumer` → `POST /api/ai/chat/sessions/{id}/dispatch` → `SessionDispatchOrchestrator`.
- Real-document pre-seed bridge: `SendWorkspaceArtifactHandler` (`workspace_open_tab` → `ComposeLaunchContext`).

---

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR / non-goal | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| R1 spec.md non-goal "Tracked changes round-trip — never" | project-level non-goal | Word-native track changes are a competitive necessity surfaced post-R1 | **B — amend R1 spec** | Amend "never" → "deferred to R2"; over-pruned at R1. |
| R1 spec.md non-goal "Comments as `<w:comment>` — never" | project-level non-goal | Needed for Word parity | **B — amend R1 spec** | Same; R2 ships it. |
| ADR-039 one dispatch protocol / closed catalogs | "routing config lives ONLY in the Binding table" | none — design complies | **C — comply** | Action+Binding via the shipped seam; no new endpoint (§7.2 is the reviewer enforcement text). |
| ADR-040 storage-precedes-rendering | "no parallel session cache" | none — design complies | **C — comply** | Edit payload = ledger `SessionOutput` first; action log = ledger; undo = supersession. |
| ADR-013 AI facade discipline | "CRUD/Compose code must not inject AI internals" | none — design complies | **C — comply** | Reached only via HTTP seam + core contracts; Tier-1 NetArchTest enforces. |
| Core charter §3.4 contract-first (no local MemoryItem variants) | "satellites may not invent local MemoryItem variants" | `AnchoredAnnotation` is Compose-domain state | **A — narrow documented deviation** | Argued NOT a MemoryItem variant (positional UI state, never retrieved as memory, never via `memory.*`); tenant/matter scoped. Fallback if rejected: negotiate a MemoryItem sub-type WITH the core. |
| CLAUDE.md §11 "no parallel dispatchers" | §11 + PR #544 lessons + ADR-039 | none — design complies | **C — comply** | MUST NOT re-introduce a Compose-specific dispatch endpoint; §7.2 explanation block is review guidance. |

**Actions**: file the R1 spec.md amendment (two Word non-goals) at R2 closeout; carry the `AnchoredAnnotation` Path-A deviation into code-review sign-off.

---

## Success Criteria

1. [ ] **Flagship gate (transferred G-R2-C)** — the assistant-driven lifecycle **open → pre-seed → draft-into-editor → AI edit rounds → save-back with provenance**, executed by the operator on spaarkedev1 **in one conversation**, browser-verified; the create leg ends with the document **OPEN in Compose** (not a record id). *Verify by*: live browser run; a passing curl/green test does NOT satisfy it.
2. [ ] All three entry paths mount a file (1a Browse, 1a Search, 1b upload, 1c). *Verify by*: browser click-through per path.
3. [ ] 5 Action+Binding rows deployed with eval cases + schema validation; all dispatch through the seam (zero new endpoints). *Verify by*: catalog mirror diff + route audit.
4. [ ] Create-on-save produces a full-parity Document (container from BU + `sprk_document` + profile + indexing + optional-association prompt); no fileless orphan reported as success. *Verify by*: OutcomeCard per-step states + Dataverse/SPE inspection.
5. [ ] Word push renders natively in Word for Web; pull round-trips with correct author/date; return-from-Word re-anchors with confidence bands + banner. *Verify by*: Word for Web round-trip (Spikes 5/6).
6. [ ] Inline redline + undo/replace are ledger-durable (survive refresh). *Verify by*: refresh mid-suggestion.
7. [ ] BFF publish ≤ 60 MB; no new HIGH CVE; NetArchTest facade rule green. *Verify by*: `dotnet publish` measure + `dotnet list package --vulnerable` + CI.

---

## Dependencies

### Prerequisites (sequencing)

- **Core Phase A0 contracts** (consumed, never forked): `ComposeDisposition v1` (+ SSE frame), `JobAwareCompletionState v1`, `OutcomeCard v1`, `TraceEvent v1`, `GateDecision v2` (Policy v2 Tier 2c), the `memory.write` gated tool, and the **triple-twin description hoist** (core Phase A, before catalog rows). **The catalog-row tasks + the draft-into-editor leg sequence behind these.** Spikes (Phase 0/1/2), the Word DOCX shuttle, and entry-point wiring (1a/1b/1c) do NOT — they proceed immediately.
- R1 foundation + redesign-r1 as-built (shipped): `compose-editor` layout, chat→Compose bridge, real-document pre-seed (1c), `ComposeService` + `SpeFileStore.ReplaceFileContentAsUserAsync`, session ledger + compacted digest.

### External Dependencies

- **Microsoft Open XML SDK 3.x** (MIT) · **Codeuctivity.OpenXmlPowerTools** (MIT) · **SPE Webhook subscriptions** (Graph API, no fee) · **TipTap `BubbleMenu`** (OSS/MIT).

---

## Owner Clarifications

*Captured across the 2026-07-08 owner-review + design-to-spec sessions:*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Doc Q&A | In R2 or defer? | **In R2 (stretch)** | FR-35; rides agent loop |
| Defined terms | Include? build? | **In R2, FULL extraction capability** | FR-11; 5th catalog pair |
| Word-change summary | New capability or diff-only? | **New Action+Binding** | FR-10; 4th catalog pair |
| Defined-terms UX | Trigger + output surface? | **Overflow-menu trigger → Context-pane read-only output** | FR-11 |
| Concurrency | Serial or parallel? | **Serial queue in ConversationPane** | FR-18 |
| Re-anchor threshold | Value? | **≥0.85 / 0.6–0.85 / <0.6; Spike 6 tunes** | FR-27 |
| Output staging | Assistant `insert>` vs inline redline? | **Inline redline (Word-style); confirmation in Assistant** | FR-16 |
| Undo/replace | Needed? | **Yes — Assistant must undo/replace a prior AI edit** | FR-17 |
| Context pane | Input surface? | **No — audit-only** | FR-11, FR-28, FR-32 |
| Clause library / cursor toolbar | In R2? | **OUT; extensibility preserved for a follow-on** | Out of scope |
| Upload → Compose | Create-first or mount-then-save? | **Mount transient; create-on-save** | FR-03, FR-05 |
| Create-on-save parent | Required parent? | **No required parent (standalone OK); PROMPT optional association; SPE container REQUIRED, from user's business unit** | FR-05 |
| Upload fidelity | Which bytes persist? | **Original if unedited; regenerate if edited** | FR-06a |
| Save-time bytes | Open? | **Resolved by as-built `tipTapToDocxBytes` (regenerate)** | FR-06a |
| NFR latency | Set targets? | **Accept defaults** | NFR-03 |
| Eval coverage | Bar? | **Golden + dispatch families, ≥5 each** | FR-12, NFR-06 |

---

## Assumptions

- **Spikes as early tasks**: the §13 spike plan is folded into the plan as Phase-0/1/2 early tasks (not a separate pre-spec phase) — per owner steer to run design-to-spec now.
- **Business-unit → container resolution** reuses the same mechanism as new matter/project record creation (assumed to exist as a platform primitive; confirm exact API in plan/Step-2 discovery).
- **Defined-terms is a read action** (Tier 0/1, no gate).
- **Return-from-Word** ships both webhook (FR-26) and poll variant in R2.

---

## Unresolved Questions

- [ ] **Core Phase A0 timing** — confirm with the core project when `ComposeDisposition v1` + `JobAwareCompletionState v1` + the triple-twin hoist publish. *Blocks*: catalog-row tasks + draft-into-editor leg only (not spikes/Word/entry-points).
- [ ] **Business-unit→container API** — confirm the exact platform call used by matter/project creation. *Blocks*: FR-05 create-on-save implementation (resolve in project-pipeline Step 2 resource discovery).
- [ ] **`AnchoredAnnotation` Path-A deviation** — confirm code-review sign-off (or negotiate a core MemoryItem sub-type) before FR-29 merges.

---

*AI-optimized specification. Original design: [`design.md`](./design.md). Generated by `design-to-spec` 2026-07-08.*
