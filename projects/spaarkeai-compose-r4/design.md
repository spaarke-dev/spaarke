# Spaarke Compose R4 — Design (Shadow Document Architecture) — MISSION CRITICAL

> **Status**: DRAFT for `/design-to-spec`. Owner-directed rip-and-replace (2026-07-22).
> **Codename**: Spaarke Compose (continuing R1 → R2 → R3)
> **Positioning**: AI-native legal drafting surface — **R4 makes the OOXML round-trip correct by construction.**
> **Project ID**: `spaarkeai-compose-r4`
> **R4 Theme**: **Rip and replace the translation/save layer with a Shadow Document Architecture.** OOXML is the single source of truth; TipTap is a lossy projection + controller; edits are ID-anchored operations applied surgically to the retained OOXML by a single backend Patch Engine. No text-search anywhere in the write path.
> **Owner**: Ralph Schroeder
> **Last updated**: 2026-07-22
> **Supersedes**: R3's dual-path save (`ComposeParagraphRedlineSynthesizer` paragraph-diff + `DocxAnnotationWriter` text-search). R3 machinery that survives is enumerated in `notes/as-built-inventory.md` (KEEP list).
> **Evidence base**: `notes/senior-reviews-2026-07-22.md` (two external reviews verbatim) · `notes/research-digest.md` · `notes/as-built-inventory.md` · R3 research `../spaarkeai-compose-r3/notes/tiptap-docx-fidelity-research-2026-07-16.md` · R3 clean-slate `../spaarkeai-compose-r3/notes/compose-clean-slate-architecture.md` · R3 rip-and-replace plan `../spaarkeai-compose-r3/notes/compose-shadow-document-RIP-AND-REPLACE-PLAN.md`.

> ### Constraints carried forward (BINDING — owner rules)
> - **NO commercial / per-seat / AGPL component.** MIT/permissive only: `DocumentFormat.OpenXml` (MIT, already a BFF dep), MIT TipTap base + `@tiptap/extension-*` only (never `@tiptap-pro/*`), PDF.js (Apache-2.0, later phase). No Syncfusion, no SuperDoc code, no TipTap Pro.
> - **We are NOT building Word.** "Full Word fidelity" is a non-goal. The goal is preservation fidelity + placement determinism.
> - **BFF publish-size ceiling ≤60 MB compressed** (root CLAUDE.md §10). Zero new runtime package expected for the docx core.

---

## 0. Locked Decisions (owner-directed 2026-07-22 — flag any that are wrong before spec)

| # | Decision | Rationale | Rejected alternative |
|---|---|---|---|
| **D1 — Delta model** | **Step-level operational deltas.** Capture ProseMirror transaction steps as operations (insertText / deleteRange / replaceRange / setMark / splitParagraph / mergeParagraph / insertParagraph / deleteParagraph / setBlockAttr). NOT `getHTML()`, NOT paragraph-granularity diff. | Durable, correct-by-construction; unifies the save path; the only model that supports structural edits; what all four reviews prescribe. | R3 paragraph-diff (`collectEditedParagraphs`) — coarse, re-diffs runs, no structural edits. |
| **D2 — Anchoring** | **`(paraId, runIndex, run-local-offset)` — REFINED 2026-07-22.** paraId (node) is the durable coarse anchor; runIndex + run-local-offset is the fine anchor. NEVER run-*ids*, NEVER absolute doc positions. Run boundaries resolved at patch time by Open XML SDK (split-run-at-offset). | paraId is Word-native + survives round-trips; a bare `(paraId, char-offset)` still drifts if an earlier edit lands in the same paragraph (CRDT/Peritext lesson — see `notes/bridge-prior-art.md` §4), so we anchor to run-index + run-local-offset and re-derive on apply. Cheap, no CRDT dependency. | Per-`w:r` *id* tagging (Review B) — run-ids don't survive Word. Bare `(paraId, char-offset)` — intra-paragraph drift. |
| **D3 — Format scope** | **docx built end-to-end now.** pdf/xlsx/pptx are explicit LATER phases; the architecture is designed to extend but they are not built in R4. | Mission-critical = docx bulletproof first. | Build all formats now — dilutes focus. |
| **D4 — SPE role** | **SPE stays the store + open-to-web/desktop launch surface** (`SpeDocumentViewer`). Versioning + lock(423) + co-authoring protocol is IN scope as its own phase (part of reliable save), NOT a WOPI-embedded editor. | Reliable save needs a defined concurrency/lock protocol; the launch surface already works. | WOPI-embed as the editor — can't control content inside it. |
| **D5 — One byte-author** | **A single unified `ComposeShadowPatchEngine`** replaces BOTH `DocxAnnotationWriter` (text-search) AND `ComposeParagraphRedlineSynthesizer` (paragraph-diff). Operations → native `w:ins`/`w:del`/`w:comment` on the shadow OOXML. | One byte-author eliminates the two-path drift that caused Bug A; unified anchor model. | Keep two writers — the split is exactly what we rip out. |

**If any of D1–D5 is wrong, say so — everything downstream depends on them.** These D1–D5 mirror `notes/compose-shadow-document-RIP-AND-REPLACE-PLAN.md` §0.

---

## 1. Product Statement

Compose is a legal drafting workspace inside the Spaarke DMS: a lawyer opens a Word document (a `sprk_document` on SharePoint Embedded), edits it, gets AI-suggested redlines with rationale, accepts/rejects, and saves back to SPE as real `.docx` bytes with native tracked changes and comments — so the document opens in Word/Word-for-web with accept/rejectable redlines and threaded comments in the lawyer's native review workflow.

**The R4 problem, in one sentence**: *the current save is a `docx → editor-model → docx` pipeline — any edit re-derives the `.docx` (or text-searches to place annotations), so untouched content is silently reinterpreted and edits fail to place at interior locations (HTTP 422). The fix is to stop re-deriving and start patching: OOXML stays the source of truth; edits are ID-anchored operations applied surgically.*

This is **not a feature problem** — the R2/R3 feature set (AI redlines, comments, accept/reject, find/replace, tables, import round-trip) is right. It is a **translation/save-layer correctness problem**. R4 replaces that layer.

## 2. Current State — what exists to rip vs. keep (grounded 2026-07-22)

Full inventory with file:line in `notes/as-built-inventory.md`. Summary:

**KEEP + extend**: `ComposeDocxProjectionBuilder` (the custom `w:p`→HTML mapper that already replaced mammoth in Phase 1), `ParaIdPreParser`/`ExtractParaIds`, `ComposeBaselineParaIdStamper`, `AnnotationReanchorService` (fuzzy re-anchor with bands + ambiguity guard), client `paraIdExtension` + `stampParaIds`/`captureParaIdSnapshot`, SPE facade + `SpeDocumentViewer`, and the native-OOXML edge-case wisdom (EDGE-1…4) inside `DocxAnnotationWriter`.

**RIP OUT**: `DocxAnnotationWriter.LocateTarget` (whole-doc text-search — the 422 root cause), `DocxAnnotationWriter` as the write path, `ComposeParagraphRedlineSynthesizer` (paragraph-diff), client `collectEditedParagraphs`/`{paraId,text}` export, residual `mammoth` fallbacks, and the `DocxAnnotation.TargetText` text-anchor contract.

**The core defect removed**: two independent save writers (redlines via paragraph-diff synthesizer; comments/annotations via text-search writer). R4 unifies both into one operational Patch Engine (D5) with one anchor model (D2) and no text-search (invariant I-7).

## 3. Invariants (binding — every task inherits; from `compose-clean-slate-architecture.md`)

- **I-1 One authoritative model** = the real OOXML package (retained; never wholesale-regenerated for a loaded doc).
- **I-2 Server-authoritative** — the client never authors `.docx` bytes.
- **I-3 Stable addressing** — every editable node carries a `w14:paraId`; edits reference it, never text-search, never absolute position.
- **I-4 Edits are operations**, applied surgically; untouched XML subtrees are byte-identical after save.
- **I-5 One byte-author** — a single Patch Engine writes the package (D5).
- **I-6 Client is a view + controller** — TipTap renders the projection and emits operations.
- **I-7 No text-search anchoring** in the write path. Fuzzy content-match survives ONLY as a below-threshold "surface-as-comment" last resort on reload/cross-Word-session re-anchor.

## 4. Features — what users do (unchanged intent; now correct by construction)

- **F1 — Edit a formatted contract, save, get it back intact.** 40-page contract with letterhead, numbered clauses, footnotes, signature table; she edits three clauses + accepts two AI redlines; the returned `.docx` is **byte-for-byte the original everywhere she didn't touch**, with her edits as tracked changes. *(Preservation fidelity — invariant I-4.)*
- **F2 — Every edit/redline/comment lands exactly right, at interior locations too.** No "can't find insertion point" (kills the 422 class). Anchors survive edits elsewhere in the doc. *(Placement determinism — I-3.)*
- **F3 — AI redline shows rationale + a grounding-tied confidence band.** No numeric false precision; no auto-accept of low-confidence (carried from R3 §6.2/E3).
- **F4 — Open a doc that already has Word revisions/comments and see them as first-class tracked changes/comment threads** (import round-trip; reader exists, mount is the work).
- **F5 — AI generation is drift-proof.** Highlight → Generate → (user keeps typing) → AI returns → the edit lands at the rebased original selection, via a ProseMirror bookmark. *(NEW in R4 — the generate-window state-drift fix.)*
- **F6 — Reliable save under concurrency + Office locks.** Stale-base save re-anchors instead of failing; create-on-save doesn't stale the eTag; a Word co-authoring lock (HTTP 423) surfaces a user-actionable state, not a 500.

## 5. Architecture — end to end

### 5.0 The shape in one paragraph (the clearest statement of why R4 is shaped this way)

**Fidelity lives on the server. Editing tools live in the editor. The bridge between them is the real engineering.**

- **Fidelity = server (essentially solved).** The `.docx` is held at near/actual 100% fidelity by `DocumentFormat.OpenXml` (MIT, already a BFF dep) + the retained original bytes. The SDK's object model is faithful to *all* of OOXML **by construction** (it *is* the XML as typed objects — it never has to "understand" a construct to preserve it); package parts we never open (styles, numbering, headers/footers, theme, embedded objects) are copied **verbatim**. Writing back to `.docx` is not a problem. **This is a cleaner fidelity guarantee than any browser editor model (incl. SuperDoc), because the SDK model is complete by definition rather than "faithful to what its authors chose to model."**
- **Editing tools = editor (largely solved).** TipTap/ProseMirror (MIT base) provides the surface + tools cold (typing, selection, undo/redo, marks, headings, lists, tables, links), and R2/R3 already built the legal-specific extensions on that base (track-changes `w:ins`/`w:del` marks, comments, find/replace, styles pane, paraId extension). Editing tools are **not** the gap.
- **The bridge = the real work.** The editor edits a lossy *view*; every edit must map deterministically to `(paraId, offset)` in the server's faithful model. This bridge — NOT display, NOT the toolbar — is where R4 succeeds or struggles. Its three hard pieces: **opaque atoms** (non-renderable constructs — fields, SDT/content controls, complex floats — shown as non-editable placeholders holding their paraId so order + patchability survive), **offset→run mapping** (editor-offset N in paraId X → the exact OOXML run split, accounting for formatted/split runs and existing tracked changes), and **structural ops** (split/merge/insert/delete paragraph). Phase 0's operation schema + the applier spike on the CIPO doc exist to nail this down before commit.

**Why the editor does NOT need to be OOXML-faithful:** because our editor is a *view*, not the source of truth. SuperDoc/"own-working-copy" must make the *browser editor* faithful (the multi-year build) because their editor model IS what gets serialized. R4 puts faithfulness on the server (SDK + retained bytes) and bridges to a lossy view via `paraId` — so the editor is allowed to be simple.

**Display bar (steering — do NOT chase pixel-perfect Word render):** the goal is *readable + editable with content/structure faithful* (bold stays bold, headings look like headings, tables look like tables, clauses show numbering) — NOT reproducing Word's exact pagination/margins/fonts/footnote layout. Chasing pixel-fidelity is the Word-rebuild trap and is unnecessary because fidelity is preserved server-side and "open in Word/Word-for-web" (`SpeDocumentViewer`, already wired) is the escape hatch. **Honest in-editor display limits (all preserved server-side, none lost):** multi-level clause numbering (`1.1(a)(iii)`) may render approximately (scheme lives in `numbering.xml`); fields/content controls render as opaque placeholders. These are display approximations, not fidelity losses — the saved `.docx` is correct.

```
              ┌──────────────────── BACKEND (source of truth) ─────────────────────┐
 SPE (.docx)  │  Shadow OOXML package (retained; w14:paraId on every editable w:p)   │
     │        │    │ Ingest+Tag (ProjectionBuilder+)         ▲                       │
     │download│    ▼                                         │ Patch Engine (D5)     │
     ▼        │  Projection = HTML/JSON + paraId map +        │ op → w:ins/w:del/      │
 ┌────────┐   │  intra-paragraph offset-addressing table     │ w:comment (surgical)  │
 │Endpoint│───┤    │                                          ▲                       │
 └────────┘   └────┼──────────────────────────────────────────┼───────────────────────┘
     │             │ projection (data-paraid + atoms)          │ operation log + base version
     ▼             ▼                                          │
 ┌──────────────────────── FRONTEND (view + controller) ──────┼───────────────┐
 │ TipTap/ProseMirror — renders projection; schema retains paraId              │
 │   • user edit  → intercept transaction steps → operations {paraId,offset}   │
 │   • AI generate→ Decoration/bookmark at selection; pass paraId as context;  │
 │                  resolve-on-return; AI returns JSON ops referencing paraId   │
 └──────────────────────────────────────────────────────────────────────────────┘
```

**Ingest (backend)**: parse `document.xml`; ensure every editable `w:p` has a `w14:paraId` (mint + **persist** if missing); project to HTML/JSON carrying `data-paraid`; emit the paraId map **+** an intra-paragraph offset-addressing table (paraId → run-boundary map). Non-renderable constructs (SDT/content controls, fields, complex/floating objects) become **opaque atom blocks** — visible, non-inline-editable, carrying their paraId so document order + patchability survive.

**Capture (frontend)**: extend the TipTap schema (already carries `data-paraid`) to intercept ProseMirror transaction steps and map each to an operation anchored `{paraId, offset|range}`. Maintain an ordered, rebased operation log per dirty session (ProseMirror position-mapping rebases as the user keeps editing). **Delete** the `getHTML`/paragraph-diff export path.

**AI (frontend + facade)**: on Generate, drop a `Decoration`/`Selection.getBookmark()` at the selection, tie it to the request id, pass the target `paraId` as context. The model returns **JSON operations referencing paraId** (not free text to search). On return, rebase the bookmark to its current position, validate every returned paraId/offset against the live doc, then apply. Unvalidatable ops surface as a review comment (fuzzy last resort), never silently placed.

**Patch (backend)**: `ComposeShadowPatchEngine(shadow bytes, operation log) → patched bytes`. Resolve node by paraId (O(1)); apply each op by splitting runs at offsets; emit native `w:ins`/`w:del`/`w:comment` (migrating EDGE-1…4 wisdom). Untouched subtrees untouched → byte-preserving by construction. Structural ops (split/merge/insert/delete paragraph) applied natively — the capability the paragraph-diff never had.

**Save + concurrency (backend)**: version-stamp every save (SPE eTag + projection schema version); assert before applying; if the base moved, re-anchor the op log via `AnnotationReanchorService` (AUTO apply / REVIEW+ORPHAN surface) instead of failing; sequence create-on-save → fresh eTag → conditional content write; map HTTP 423 lock to a user-actionable state.

## 5.6 Prior art & techniques for the bridge (permissive, studiable — full catalog in `notes/bridge-prior-art.md`)

The bridge is **not novel** — every sub-problem has strong MIT/Apache prior art. We reuse, we don't reinvent.

| Sub-problem | Borrow from | License |
|---|---|---|
| Client op capture + position rebasing (incl. AI-generation bookmark) | **ProseMirror** `transform` (Step/StepMap/**Mapping**), `prosemirror-changeset` | MIT |
| Operation-schema shape (`{op, paraId, runIndex, offset}`) | **Slate** op discriminated-union + `Path.transform` | MIT |
| **OOXML-as-truth / editor-as-projection** (reference only) | ⚠️ **CORRECTED 2026-07-22**: official EigenPal `docx-editor` = **closed facade** (contract-only stubs that throw; engine proprietary). Real Apache-2.0 code survives only in the **frozen fork `sorenlouv/docx-editor`** (`@sqren/docx-editor@1.0.3`) — study-reference / vendor-and-own, unmaintained | Apache-2.0 (fork) |
| Anchor-survives-concurrent-edits theory | **Yjs `RelativePosition`** / Automerge `Cursor` / **Peritext** essay | MIT |
| Editor↔canonical incremental-sync shape (+ version guard = our eTag) | **LSP `textDocument/didChange`** | spec |
| Server-side .NET surgical patch (split-run + `w:ins`/`w:del`) | **Open-XML-SDK** + **Docxodus** (MIT, .NET redline engine; may already implement our patch path) + PowerTools `WmlComparer` (live forks) | MIT |

**Build-vs-vendor, corrected (Phase 0):**
1. **Projection layer = build-our-own** (extend the shipped Phase-1 `ComposeDocxProjectionBuilder`). The "vendor a maintained Eigenpal library" option is **dead** — the official repo is a closed facade. Optionally **study/vendor the frozen Apache-2.0 fork `sorenlouv/docx-editor`** as a reference/seed for the parser/serializer — but only after a build spike confirms it's complete + buildable, and we'd own all its maintenance. No runtime dependency on anything EigenPal ships.
2. **Patch engine = genuine A/B**: **Docxodus (MIT, active)** as the server redline engine vs. building directly on Open-XML-SDK. **Spike**: does Docxodus already do split-run + `w:ins`/`w:del` at an offset? This is the one surviving real vendor option.

**Pitfalls the prior art flags (bake into Phase 0):** (a) mutate the OpenXml DOM, **never** string-edit `document.xml`; (b) paragraph-mark deletion (merging paragraphs) = `w:del` on the para-mark glyph in `w:pPr/w:rPr` — the hardest edge; (c) numbering lives in a **separate** `numbering.xml` part, not inline; (d) ProseMirror repos showing "archived" = forge migration, NOT abandonment (NPM still MIT + maintained).

## 6. Phasing (WBS sketch — full task decomposition is `/project-pipeline`'s job)

Mirrors `notes/compose-shadow-document-RIP-AND-REPLACE-PLAN.md` §4. Front-load the two hard surfaces (operation schema + step→OOXML applier) and spike them on the CIPO doc before committing Phase 3.

- **Phase 0 — Gate**: spec; the **Shadow-Document ADR**; the **fidelity corpus + round-trip byte-diff harness**; the **operation schema** (the spine both ends implement).
- **Phase 1 — Backend ingest**: offset-addressing table; persist paraId on ingest; opaque atoms for SDT/fields.
- **Phase 2 — Frontend capture**: ProseMirror step→operation interceptor; delete paragraph-diff export; rebased op log.
- **Phase 3 — Patch Engine**: unified `ComposeShadowPatchEngine`; retire both old writers (migrate EDGE-1…4); structural ops.
- **Phase 4 — AI anchoring**: bookmark on generate window; paraId context; resolve-on-return; validate; fuzzy-as-comment fallback.
- **Phase 5 — Concurrency + save**: version-stamp; re-anchor-on-stale; eTag sequencing; 423 lock protocol.
- **Phase 6 — Hardening + cutover**: corpus proof (byte-diff untouched parts); delete dead code; remove `mammoth`; publish-size; deploy + operator UAT.
- **Later (deferred, NOT R4 DoD)**: L1 multi-format (pdf: PDF.js view/annotate + Azure Document Intelligence extract; xlsx/pptx: same OOXML SDK + shadow pattern). L2 SPE deepening (version-history UI, richer co-authoring/lock UX).

## 7. Non-functional requirements (seed)

- **NFR-01 Byte preservation**: load → no-op save of every corpus doc → untouched OOXML subtrees byte-identical (harness-verified).
- **NFR-02 Placement determinism**: 100% of edits/redlines/comments land at the intended paraId+offset; **zero text-search in the write path**.
- **NFR-03 Licensing**: MIT/permissive only; no commercial/per-seat/AGPL; no TipTap Pro; MIT TipTap base + `@tiptap/extension-*` only.
- **NFR-04 Publish size**: BFF ≤60 MB compressed; zero new runtime package for the docx core (verify per BFF task).
- **NFR-05 ADR-013 facade**: `Services/Compose/` stays pure — no `IOpenAiClient`/executor/routing type (Tier-1 NetArchTest enforces); no `Microsoft.Graph` type above `SpeFileStore` (ADR-007).
- **NFR-06 E2E DoD**: every save/load/dispatch change carries a through-the-wire `WebApplicationFactory` seam slice (`tests/integration/seam/**`); no `Mock<HttpMessageHandler>`, no DI-registration/ctor-null tests (ADR-038).
- **NFR-07 Word-native output**: results open in Word/Word-for-web with real accept/reject redlines + threaded comments.
- **NFR-08 Concurrency**: stale-base save re-anchors (no eTag 500); 423 lock surfaces cleanly.

## 8. Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>            <!-- Services/Compose/ (Patch Engine, ingest, save) -->
  <spaarkeai>Y</spaarkeai> <!-- Compose widget is hosted in the SpaarkeAi surface -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

BFF=Y → Placement Justification required per new component; ≤60 MB publish ceiling applies per task. Run `/conflict-check` before any BFF PR (overlaps `spaarkeai-compose-r*`, `spaarke-ai-architecture-redesign-r2` on `Services/`).

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `ComposeShadowPatchEngine` (server) | `DocxAnnotationWriter` + `ComposeParagraphRedlineSynthesizer` (both to be retired) | No — it **replaces** both; the two-path split is the defect. It consolidates, not adds. | Without it the text-search 422 class and the two-path drift persist; interior-location edits fail. |
| Operation schema (shared contract) | `DocxAnnotation` (text-anchored, to be retired); `ComposeEditedParagraph` `{paraId,text}` (to be retired) | No — both existing contracts are text-/paragraph-coarse; the op schema is the new spine anchored by paraId+offset. | Without a stable op contract, client and server can't agree on ID-anchored edits; drift returns. |
| ProseMirror step→operation interceptor (client) | `collectEditedParagraphs` (to be retired) | No — replaces the paragraph-diff capture with step-level capture. | Without it, no structural edits + coarse deltas that re-diff runs. |
| Intra-paragraph offset-addressing table (server, in projection) | `ComposeDocxProjection.ParaIdMap` (paragraph-level only) | **Extend** `ComposeDocxProjectionBuilder`/`ComposeDocxProjection`. | Without offsets, ops can only address whole paragraphs — back to paragraph-granularity. |
| Opaque atom node (client + projection) | none | No — new node type for non-renderable OOXML (SDT/fields). | Without it, fields/content-controls either break the editor or get dropped on save. |

Everything else is **modify/extend** of KEEP-list assets (`notes/as-built-inventory.md`), not new surface.

## 9. ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR / prior decision | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **R3 project decision (FR-02)**: "dirty save = paragraph-diff synthesizer onto retained original" | R3 codified the paragraph-diff (`ComposeParagraphRedlineSynthesizer`) as the delta engine | R4 replaces it with step-level operational deltas | **B (amendment)** | Context changed: paragraph-diff can't do structural edits and keeps a second text-search path alive; the operational model supersedes it. Amend the project-level decision in the R4 ADR (Phase 0 T00.2); this is a project-decision amendment, not an ADR-doc change unless a doc ADR codified it. |
| **ADR-013 (AI facade)** | `Services/Compose/` must not reach AI internals | AI returns operations → still routed via facade/PublicContracts | **C (comply)** | Op validation + apply is pure; AI dispatch stays behind the facade. Tier-1 NetArchTest enforces. |
| **ADR-007 (Graph isolation)** | No `Microsoft.Graph` type above `SpeFileStore` | Patch Engine + save touch SPE | **C (comply)** | Patch Engine is `byte[]`-in/`byte[]`-out; the SPE hop stays in the endpoint/facade layer. |
| **ADR-038 (testing)** | Integration-heavy pyramid; seam DoD | New engine + save path | **C (comply)** | Seam slice tests (`tests/integration/seam/**`) for every save/load change; corpus round-trip harness. |
| **No-TipTap-product / no-AGPL (NFR-03)** | MIT base only | Op capture needs raw ProseMirror step/decoration APIs | **C (comply)** | Raw ProseMirror APIs are MIT and available on the base editor; no Pro extension needed. |

> Additional tensions may surface during Phase 0; handle via §6.5 (surface as A/B/C, don't silently comply/violate).

## 10. Success Criteria (graduation)

1. [ ] **Byte-preserving** — no-op save byte-identical on untouched subtrees across the fidelity corpus. Verify: byte-diff harness.
2. [ ] **Placement determinism** — every edit/redline/comment (user + AI) lands at its paraId+offset; zero write-path text-search. Verify: corpus + code audit + seam tests.
3. [ ] **AI drift-proof** — generation with concurrent edits lands at the rebased selection. Verify: automated ProseMirror test + UAT on CIPO.
4. [ ] **Concurrency** — stale-base save re-anchors (no eTag 500); 423 surfaces cleanly. Verify: seam tests + UAT.
5. [ ] **Word-native output** — opens in Word-for-web with accept/reject redlines + threaded comments. Verify: manual + UAT.
6. [ ] **Dead code gone**; `mammoth` removed; publish ≤60 MB; ADR + tests green. Verify: grep + `dotnet publish` size + CI.

## 11. Dependencies / Prerequisites
- **New worktree**: `spaarkeai-compose-r4` (this project). Register in `projects/INDEX.md` (hot-path BFF=Y, SpaarkeAi=Y) via `/project-pipeline`.
- **Fidelity corpus**: CIPO patent letter (known 422 + empty-paragraph case) + N real-world worst-offenders (tables, tabs, lists, headers/footers, fields, content controls, pre-existing tracked changes, multi-section). Store as LFS fixtures (Phase 0 T00.3).
- **Cross-project coordination**: `Services/Compose/` overlaps `spaarkeai-compose-r3` (predecessor — its Phase-1 + Bug-A are the deployed base) and `spaarke-ai-architecture-redesign-r2` (`Services/Ai/` owner — consume PublicContracts only). Resolve the **notifications build break** on tip-of-master (`spaarke-notification-spine-r1` `@spaarke/notifications` unwired dep) before any SpaarkeAi deploy from tip.
- Zero new runtime package expected (`DocumentFormat.OpenXml` already present).

## 12. Owner Clarifications / Open Questions for `/design-to-spec`

These are the gaps `/design-to-spec` should interview on (seeded so the interview is fast):

- **Q1 (BLOCKING)**: Confirm D1–D5 as locked (esp. D1 step-level deltas over paragraph-diff, and D2 paraId+offset over run-ids).
- **Q2 (BLOCKING)**: Cutover strategy — hard replace (rip R3 paths in the same PR series) vs. feature-flag parallel-run (new engine behind a flag, compare against old on the corpus, then flip)? *Recommendation: flagged parallel-run through Phase 3–5, hard-remove in Phase 6, so we can A/B on the corpus.*
- **Q3 (IMPORTANT)**: Structural-edit scope for R4 — is paragraph insert/split/merge/delete in R4 core, or is R4 core = text+format edits with structural ops as a Phase-6 stretch? *Recommendation: in core (it's the main thing the operational model unlocks), but sequence last within the applier.*
- **Q4 (IMPORTANT)**: Fidelity corpus size/sourcing — how many + which real documents can the owner supply (redacted)? Determines corpus coverage. *Assumption: CIPO + ~6 owner-supplied worst-offenders.*
- **Q5 (IMPORTANT)**: Born-in-editor documents (no source `.docx`) — confirm they feed the SAME operation/patch model (full render as an initial "insert everything" op set) rather than a parallel export path. *Recommendation: unify.*

---
*Design document for `/design-to-spec`. Research base preserved in `notes/`. Original owner directive: rip-and-replace, mission-critical, 2026-07-22.*
