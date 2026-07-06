# G-P1 UAT Round 1 — Findings, Operator Ruling, Fix Wave ("027-fix")

> **Date**: 2026-07-05 (UAT evening run) / 2026-07-06 (fix wave) · **Environment**: spaarkedev1
> **Scope**: P1 Event/Click path shipped in `bdfcb06ba..9a4270a5b` · **Gate**: G-P1 (browser UAT, NFR-11)

## 1. UAT round-1 findings

Core flow PASSED: single upload → classification + summary; dark mode; typed-command
supersede; doc Q&A. Three defects filed, one item deferred:

| # | Finding | Root cause (confirmed) |
|---|---|---|
| D1 | **Chips missing/inconsistent** — "Summarize again" rendered after the FIRST single-file upload only; subsequent uploads and multi-file flows showed none | Compound: (a) the Click-dispatch stream (`/sessions/{id}/dispatch`, AnalysisChunk vocabulary) emitted NO chips chunk and the client rendered NOTHING from a dispatch in the conversation — every chip click permanently emptied the strip (`ConversationPane.tsx` `handleConsumerChipClick` cleared on click; `dispatchConsumer.ts` `consumeChunk` had no chips leg); (b) with D2's per-file event POSTs, later streams ended in chip-less notices or errors before the `chips` event; (c) catalog data — only chat-summarize carried a `sprk_chiptransitions` entry, and the `ChipTransition → EventChip` mapping (`EventRulesService.cs`) dropped `requires_attachments`/`prefill_slots` |
| D2 | **Bulk top-1 bound did not hold** — 3 PDFs → per-file classifications + separate summaries + interleaved "No uploaded files were available yet…" notice | Client batching: the 250 ms post-202 debounce (`ConversationPane.tsx` task-022b queue) fired one event POST per file because real promotions land seconds apart; each server call saw a partial set. Aggravator: the auto-promote effect launched all `/documents` POSTs in PARALLEL; each handler read-modify-writes `session.UploadedFiles`, so concurrent writes could drop a manifest entry (last-writer-wins) — the source of the notice |
| D3 | **"Files not available yet" race** — the event can reach the server before the session manifest shows the file | `EventRulesService.ResolveBatchFiles` did a single manifest read and degraded straight to the `no-attachments` notice; no readiness re-check |
| — | Legacy NL dispatcher "Open Library" fallback wording | DEFERRED — the legacy no-match path dies at P2; not touched in this wave |

## 2. 2026-07-05 operator UX ruling (G-P1 UAT round 1) — BINDING

**The upload UX changes to "auto-classify, chip-offered summarize". Summarize NO
LONGER auto-runs on upload.** This is catalog-data-first (the second-product demo:
behavior change shipped primarily by editing Binding rows, not code):

- The `document_uploaded` rule is now **classify-only**: `document_uploaded → [chat-classify(1)]`.
- Summarization is reached ONLY via chips (Click path): single upload offers **[Summarize]**;
  multi-file offers **[Summarize all N files?]** (+ per-file chips for batches ≤ 3).
- The batch auto-runs classification for **ALL files** of the gesture (cheap Fast-tier calls);
  the old bulk top-1 auto-summarize bound is retired. Daily-cap accounting now counts
  `members × batch files`.
- The M4 classify-confidence gate becomes **latent** on this rule (no member follows
  classify); the code stays for multi-member rules.

**Spec supersession**: the FR-P1-03 acceptance line "upload, type nothing → classification +
summary + chips" is SUPERSEDED on the auto-summary point by this ruling. The post-ruling
acceptance is "upload, type nothing → classification(s) + summarize chips; summary on chip
click". GU-038 in the eval suite moved from the event channel to the click channel accordingly.

## 3. Dataverse rows changed (idempotent; verified by post-write read_query 2026-07-06)

| Row | Column | New value |
|---|---|---|
| `sprk_playbookconsumer` `651194cd-3670-f111-ab0e-70a8a590c51c` (chat-summarize) | `sprk_oneventbindings` | `[]` (membership removed; was order 2) — chipTransitions "Summarize again" KEPT |
| `sprk_playbookconsumer` `5f3898d8-db78-f111-ab0e-7ced8ddc4cc6` (chat-classify) | `sprk_chiptransitions` | `[{"target_binding_id":"651194cd-3670-f111-ab0e-70a8a590c51c","chip_label":"Summarize","requires_attachments":true}]` (was `[]`) |

## 4. Fix summary (code)

### D2 — count-complete gesture batching (client)
`ConversationPane.tsx`: the Event batch now fires when EVERY chip of the attach gesture
has settled (documentId received / permanently failed / removed) — exactly one
`POST /events/document-uploaded` per gesture, however far apart promotions land. A **30 s
fallback timer** (anchored at the first settled promotion) bounds stuck promotions.
Membership: every non-error, un-accounted chip in the strip (including `extracting`).
The auto-promote effect now runs promotions **sequentially**, removing the client-side
manifest read-modify-write clobber.

### D3 — server manifest readiness probe
`EventRulesService.cs` + `EventRulesOptions`: when requested fileIds are not all visible
in the manifest, the service re-reads the session up to `EventRules:ReadinessProbeAttempts`
(default 5) × `ReadinessProbeDelayMs` (default 1000 ms) before degrading — wait-briefly-or-
degrade (~5 s bound). It proceeds with whatever resolved; the `no-attachments` notice only
fires at zero. Semantics note: inline `ExtractedText` is written atomically WITH the manifest
entry (`ChatDocumentEndpoints`), so a present entry never has pending text on the current
write path — the probe covers manifest visibility only; the legacy RAG fallback still
degrades with its existing message. Per-file execution failures now skip THAT file (rendered
error line) and continue the batch.

### D1 — chips end-to-end
- `EventRulesService.cs`: chips carry `requires_attachments` + pre-filled `fileIds`; bulk
  chip "…all N files?" targets the classify transitions' target; per-file chips for batches ≤ 3.
  Bounded-out (opt-out/daily-cap) manual-run chips target the transition (summarize), not classify.
- `SessionDispatchOrchestrator.cs` + `AnalysisChunk.FromChips`: the Click-dispatch stream
  emits a `chips` chunk (unified EventChip wire shape) AFTER the terminal `complete` chunk.
- `dispatchConsumer.ts`: returns `{result, chips}`; `ConversationPane` renders the dispatched
  STORED output as an assistant message (ADR-040) and re-arms the strip.
- Chip persistence rule: a non-empty chip set REPLACES the strip; empty/malformed payloads
  never blank it; chips clear only on click consumption or session change.
- `ChipTransition` contract gains optional `requires_attachments` + `prefill_slots` members.

## 5. Eval suite

GU-037 = classify-only event leg; GU-038 = click leg (chip target `binding_id` → by-id
resolution → SUM-CHAT@v1); GU-040 notes flag the latent M4 gate. Live assertion renamed to
`P1LiveDispatch_DocumentUploadedEventAndChipClickCases_ResolveCatalogRoutes`. Suite 11/11 green.

## 6. What the operator should SEE at re-UAT (per defect)

- **D1**: after EVERY completed upload flow: classification line(s) + a `[Summarize]` chip
  (multi-file: `[Summarize all N files?]` + per-file chips for ≤3). Clicking Summarize →
  streamed summary rendered in the chat + a `[Summarize again]` chip. Chips never vanish
  wholesale; they are replaced by the latest output's next steps.
- **D2**: 3 PDFs attached together → NO auto-summaries; exactly one event flow with THREE
  classification lines (one per file) + the bulk chip set; no interleaved notice.
- **D3**: the "No uploaded files were available yet" notice should not appear in normal
  flows (probe covers propagation lag; count-complete + sequential promotion remove the
  causes). If a promotion is genuinely stuck, the batch fires after ≤30 s with the settled
  subset.
