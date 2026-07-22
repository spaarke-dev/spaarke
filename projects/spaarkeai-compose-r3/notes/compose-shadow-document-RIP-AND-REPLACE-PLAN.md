# Compose — Shadow Document Architecture: Rip-and-Replace Plan (MISSION CRITICAL)

> **Created**: 2026-07-22
> **Status**: Plan of record. Supersedes the incremental `compose-narrow-fix-plan.md` (patch approach — retired; we are NOT keeping the current translation/save layer).
> **Decision**: Rip out the current OOXML↔editor translation + save layer entirely and replace it with a **Shadow Document Architecture** (step-level operational deltas anchored to stable `w14:paraId` + offset, applied surgically to retained OOXML). Three independent senior reviews converged on this model.
> **Scope guard (unchanged)**: This is the *translation/save layer*. We KEEP TipTap (it provides editing), KEEP the Compose feature set (it's right), KEEP SPE as the store + launch surface. We are NOT building Word, NOT using WOPI as the fix, NOT using any commercial/AGPL/per-seat component (NFR-03).

---

## 0. Architecture decisions made (flag to owner — redirect if wrong)

| # | Decision | Rationale | Alternative (rejected) |
|---|---|---|---|
| **D1** | **Step-level operational deltas** (capture ProseMirror transaction steps → operations: insertText/deleteRange/setMark/splitNode/etc.), NOT `getHTML()`, NOT paragraph-granularity diff. | Durable, correct-by-construction; enables structural edits; unifies the save path; what all 3 reviews prescribe. | Keep paragraph-diff (`collectEditedParagraphs`) — can't represent structural edits; re-diffs run structure on every edited paragraph. |
| **D2** | **Anchor by `paraId` (node) + intra-node offset. NEVER run-ids, NEVER absolute doc positions.** | paraId is Word-native + survives round-trips; run boundaries are volatile in Word; absolute positions drift. | Per-`w:r` tagging (reviewer 3 suggested) — run-ids don't survive Word; less stable. |
| **D3** | **docx is the mission-critical core, built end-to-end now.** pdf/xlsx/pptx are explicit LATER phases; architecture is designed to extend but they are not built in this project. | Mission-critical = docx bulletproof first. Multi-format is additive once the core pattern is proven. | Build all formats now — dilutes focus, delays the thing that's on fire. |
| **D4** | **SPE stays the store + the open-to-web/desktop launch surface** (existing `SpeDocumentViewer`). Versioning + lock(423) + co-authoring is IN scope but as its own phase (part of "reliable save"), not a WOPI-embedded editor. | Reliable save requires a defined concurrency/lock protocol; the launch surface already works and isn't the fidelity problem. | Embed WOPI as the editor — can't control content inside it; not the fix. |
| **D5** | **Single unified Patch Engine** replaces BOTH `DocxAnnotationWriter` (text-search) AND `ComposeParagraphRedlineSynthesizer` (paragraph-diff). One applier: operations → native `w:ins`/`w:del`/`w:comment` on the shadow OOXML. | One byte-author; eliminates the two-path drift that caused Bug A. | Keep two writers — the split path is exactly what we're ripping out. |

**If any of D1–D5 is wrong, say so before Phase 0 completes — everything downstream depends on them.**

---

## 1. Mission & non-negotiables

**Mission**: A Compose document round-trips through edit + AI-redline + save with **zero fidelity loss on untouched content** and **deterministic, drift-proof placement of every edit** — proven on a fidelity corpus, not anecdotes.

**Non-negotiable invariants** (carried from `compose-clean-slate-architecture.md` I-1…I-7):
- **I-1 One authoritative model** = the real OOXML package (retained, never wholesale-regenerated for a loaded doc).
- **I-2 Server-authoritative** — the client never authors `.docx` bytes.
- **I-3 Stable addressing** — every editable node carries a `w14:paraId`; edits reference it, never text-search, never absolute position.
- **I-4 Edits are operations**, applied surgically; untouched XML subtrees are byte-identical after save.
- **I-5 One byte-author** — a single Patch Engine writes the package.
- **I-6 Client is a view + controller** — TipTap renders the projection and emits operations.
- **I-7 No text-search anchoring** anywhere in the write path (fuzzy match survives ONLY as a below-threshold "surface-as-comment" last resort on reload re-anchor).

---

## 2. What we RIP OUT (explicit kill list)

| Component | Why it dies |
|---|---|
| `DocxAnnotationWriter.LocateTarget` (whole-doc text-search) | Root cause of interior-location 422s. |
| `ComposeParagraphRedlineSynthesizer` paragraph-diff save path | Superseded by operational patch (D5). Can't do structural edits; re-diffs runs. |
| Client `collectEditedParagraphs` / paragraph-granularity `{paraId,text}` export | Superseded by step-level operation capture (D1). |
| Any remaining `mammoth` reference (fallback mounts) | Already removed on stored-load path (Phase 1); remove residual fallbacks — the projection builder is the only mapper. |
| The `DocxAnnotation.TargetText` text-anchored contract | Replaced by operation + `paraId`+offset anchor. |
| Two-path annotation writing (comments via writer, redlines via synthesizer) | Unified into one Patch Engine (D5). |

**What we KEEP + build on**: `ComposeDocxProjectionBuilder` (the custom XML→HTML mapper — vindicated), `paraIdExtension`, `stampParaIds`/`captureParaIdSnapshot`, `AnnotationReanchorService` (reload re-anchor with bands/ambiguity/ORPHAN — becomes the last-resort fuzzy layer), `ParaIdPreParser`/`ExtractParaIds`, SPE facade + `SpeDocumentViewer`.

---

## 3. Target architecture (end to end)

```
                    ┌───────────────────────── BACKEND (source of truth) ─────────────────────────┐
  SPE (.docx bytes) │  Shadow OOXML package (retained, tagged w14:paraId on every w:p)             │
        │           │      │                                    ▲                                  │
        │  download │      │ Ingest+Tag (ProjectionBuilder+)    │  Patch Engine (D5)               │
        ▼           │      ▼                                    │  op → w:ins/w:del/w:comment      │
  ┌──────────┐      │  Projection: HTML/JSON + paraId map + offset-addressing table                │
  │ Endpoint │──────┤      │                                    ▲                                  │
  └──────────┘      └──────┼────────────────────────────────────┼──────────────────────────────────┘
        │                  │ projection (data-paraid)           │ operation log (paraId+offset ops)
        ▼                  ▼                                    │
  ┌─────────────────────────────── FRONTEND (view + controller) ┼──────────────┐
  │ TipTap/ProseMirror  ── renders projection, schema retains paraId            │
  │   • user edits  → intercept transaction steps → operations (D1/D2)          │
  │   • AI generate → Decoration/bookmark at selection; pass paraId as context; │
  │                    resolve-on-return; AI returns JSON ops referencing paraId │
  └─────────────────────────────────────────────────────────────────────────────┘
```

**Save**: client sends the operation log + the base document version. Server validates every op's `paraId` exists (reject+re-anchor if stale, D-Phase 5), applies ops to the shadow package via the Patch Engine, writes back to SPE with a correct eTag precondition. Untouched subtrees never change → byte-preserving by construction.

---

## 4. Phased WBS (the task set)

> Rigor/model hints follow repo conventions (CLAUDE.md §8.5): server OOXML surgery + architecture = **opus/xhigh**; well-specified client wiring = **sonnet/high**. Formalize into POML via `task-create` (assigns parallel-safety + final tiers).

### Phase 0 — Spec, ADR, and fidelity harness (gate before any code)
- **T00.1** Author project spec (`design-to-spec`): mission, FRs/NFRs, invariants I-1…I-7, ADR Tensions, hot-path + placement justification (BFF-touching). `opus/high`.
- **T00.2** Write **ADR: Compose Shadow Document Architecture** (operational deltas, paraId addressing, single Patch Engine, no text-search). Supersede/relate the R3 FR-02 synthesizer decision. `opus/high`.
- **T00.3** Assemble a **fidelity corpus**: CIPO patent letter + N real-world worst-offenders (tables, tabs, lists, headers/footers, fields, content controls, tracked changes present on load, multi-section). Store as test fixtures (LFS). Define the **round-trip byte-diff harness** (load→no-op save must be byte-identical on untouched parts). `sonnet/high`.
- **T00.4** Define the **operation schema** (the wire contract): op types (insertText, deleteRange, replaceRange, setMark/clearMark, splitParagraph, mergeParagraph, insertParagraph, deleteParagraph, setBlockAttr), each anchored by `{paraId, offset|range}`. This is the spine contract both ends implement. `opus/high`.

### Phase 1 — Backend ingest: tagged shadow model + addressing
- **T01.1** Extend `ComposeDocxProjectionBuilder` to also emit the **intra-paragraph offset-addressing table** (paraId → run boundary map) alongside the HTML + paraId map. `opus/xhigh`.
- **T01.2** Guarantee **every editable `w:p` has a `w14:paraId`** on ingest — mint + **persist** into the shadow package for id-less paragraphs (so ids survive the session and next load). Extend `ComposeBaselineParaIdStamper`. `opus/high`.
- **T01.3** Represent **non-editable constructs** (SDT/content controls, fields, complex/floating objects) as **opaque atom blocks** in the projection — visible, non-inline-editable, carrying their paraId so document order + patchability are preserved. `opus/xhigh`.

### Phase 2 — Frontend: operation capture (replaces export path)
- **T02.1** Build the **ProseMirror step → operation** interceptor: map each transaction step to an operation in the Phase-0 schema, resolving positions to `{paraId, offset}` via the schema's paraId attribute. `sonnet/xhigh`.
- **T02.2** **Delete** `collectEditedParagraphs` / `buildContentModel` export path and the `{paraId,text}` save payload; the operation log is the only thing the client sends. `sonnet/high`.
- **T02.3** Maintain a client-side **operation log per dirty session** (ordered, rebased through ProseMirror position mapping as the user keeps editing). `sonnet/high`.

### Phase 3 — Backend: the unified Patch Engine (replaces both writers)
- **T03.1** Build **`ComposeShadowPatchEngine`**: `(shadow OOXML bytes, operation log) → patched bytes`. Locate node by `paraId` (O(1)), apply each op by splitting runs at offsets, emitting native `w:ins`/`w:del`/`w:comment`. Untouched subtrees untouched. `opus/xhigh`.
- **T03.2** **Retire** `DocxAnnotationWriter` + `ComposeParagraphRedlineSynthesizer`; route all save/annotation writing through the Patch Engine. Migrate their still-valid edge-case wisdom (EDGE-1…4: comment-before-trackchange ordering, `w:delText`, paragraph-mark deletion, monotonic revision ids). `opus/xhigh`.
- **T03.3** Structural ops (splitParagraph/merge/insert/delete) applied to OOXML — the capability the paragraph-diff never had. `opus/xhigh`.

### Phase 4 — AI anchoring (drift-proof generation)
- **T04.1** On **generate**, drop a ProseMirror `Decoration`/`Selection.getBookmark()` at the selection; tie it to the request id; pass the target **`paraId` as context** to the model. `sonnet/high`.
- **T04.2** AI returns **JSON operations referencing paraId** (not free-text-to-search). **Resolve-on-return**: rebase the bookmark to its current position; validate every returned `paraId`/offset against the live doc before applying. `opus/high`.
- **T04.3** **Fuzzy fallback only as last resort**: an op whose anchor can't be validated is surfaced as a **comment/suggestion for review**, never silently placed (reuse `AnnotationReanchorService` bands). `sonnet/high`.

### Phase 5 — Concurrency + reliable save (kills Bug B family)
- **T05.1** **Version-stamp** every save (base SPE eTag + projection schema version); server asserts before applying. `opus/high`.
- **T05.2** **Re-anchor-on-stale**: if the base version moved, re-anchor the operation log against current paragraphs via `AnnotationReanchorService` (AUTO apply / REVIEW+ORPHAN surface) instead of failing. `opus/high`.
- **T05.3** **eTag sequencing**: order create-on-save → capture fresh eTag → conditional content write, so the follow-up write can't stale the precondition (the observed Bug B). `opus/high`.
- **T05.4** **Office-lock (HTTP 423) protocol**: detect lock/checkout from Word co-authoring; surface a user-actionable state ("open in Word / close the Word session"), never a raw 500. Define interaction with the open-to-web/desktop launch. `opus/high`.

### Phase 6 — Hardening + cutover
- **T06.1** Run the **fidelity corpus** through the full pipeline: byte-diff untouched parts (must be identical); verify every edit/redline/comment lands; Word opens the result with real accept/reject redlines + threaded comments. `opus/xhigh`.
- **T06.2** **Delete all dead code** on the kill list (§2); remove `mammoth` dependency entirely if no consumer remains. `sonnet/high`.
- **T06.3** Test diet + ADR-038 KEEP-path classification; `code-review` + `adr-check`; BFF publish-size verification (≤60 MB). `sonnet/high`.
- **T06.4** Deploy (BFF + SpaarkeAi via the full git flow) + operator UAT on the corpus. `sonnet/high`.

### Later phases (in the plan, explicitly deferred — NOT this project's DoD)
- **L1 — Multi-format**: pdf (PDF.js view/annotate + Azure Document Intelligence extract; no permissive full-edit — annotate/redline model), xlsx/pptx (same OOXML SDK + shadow-document pattern, different part). Architecture from Phase 0–5 is designed to extend to these.
- **L2 — SPE deepening**: version history UI, co-authoring/lock UX beyond the 423 protocol, richer open-to-desktop round-trip re-anchor.

---

## 5. Acceptance / definition of done (this project)
1. **Byte-preserving**: load → no-op save of every corpus doc → untouched OOXML subtrees byte-identical.
2. **Placement determinism**: 100% of edits/redlines/comments (user + AI) land at the intended `paraId`+offset; zero text-search in the write path.
3. **AI drift-proof**: generation with concurrent user edits places at the rebased selection; no "can't find insertion point."
4. **Concurrency**: stale-base save re-anchors (no eTag 500); 423 lock surfaces cleanly.
5. **Word-native output**: results open in Word/Word-for-web with real accept/reject redlines + threaded comments.
6. **Dead code gone**; `mammoth` removed; publish ≤60 MB; ADR + tests green.

## 6. Risks
- **R1** Step-level operation mapping (ProseMirror step → OOXML op) is the hardest surface — front-load the schema (T00.4) and spike the applier on the corpus early.
- **R2** Structural ops on OOXML (split/merge across run/section boundaries) have edge cases — Phase 3.3 is opus/xhigh for a reason; corpus-drive it.
- **R3** Opaque atoms (fields/SDT) must round-trip untouched — verify in the byte-diff harness explicitly.
- **R4** Cross-project **notifications build break** on tip-of-master still blocks SpaarkeAi deploy from tip — resolve/coordinate before Phase 6.4.

## 7. Governance
- New project worktree recommended (e.g. `compose-shadow-document-r1`) so this mission-critical work is isolated + tracked in `projects/INDEX.md`. BFF-touching → hot-path declaration + Placement Justification required.
- ADR Tensions: this supersedes R3 FR-02 (paragraph-diff synthesizer) — handle via CLAUDE.md §6.5 path B (amendment) in T00.2.
- Zero new runtime package expected (`DocumentFormat.OpenXml` already present; PDF.js/Doc-Intelligence are later-phase). Verify publish size per BFF task.
