# Spaarke Compose R4 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-22
> **Source**: `design.md` (+ `notes/senior-reviews-2026-07-22.md`, `notes/research-digest.md`, `notes/as-built-inventory.md`, `notes/bridge-prior-art.md`)
> **Codename**: Spaarke Compose R4 — Shadow Document Architecture (MISSION CRITICAL)
> **Owner**: Ralph Schroeder

## Executive Summary

R4 rips out the current Compose translation/save layer and replaces it with a **Shadow Document Architecture**: the OOXML `.docx` is the server-side source of truth (held at ~100% fidelity by `DocumentFormat.OpenXml` + retained bytes), TipTap/ProseMirror is a lossy *view + controller*, and every edit is captured as a **step-level operation anchored by `(paraId, runIndex, run-local-offset)`** and applied surgically to the retained OOXML by a single unified Patch Engine. This eliminates the two defect classes that made R1–R3 unshippable: **fidelity loss** on untouched content (from re-deriving the `.docx` from a lossy editor model) and **insertion-location failures** / HTTP 422 (from whole-document text-search anchoring). Fidelity lives on the server; editing tools live in TipTap; the **bridge** between them is the engineered core.

## Scope

### In Scope
- **Shadow-Document save layer for `.docx`** — server-authoritative OOXML, retained-original, surgical patch by stable ID.
- **Step-level operation capture** in the editor (ProseMirror steps → operations), replacing the `getHTML`/paragraph-diff export.
- **Unified Patch Engine** emitting native `w:ins`/`w:del`/`w:comment`, replacing BOTH current writers.
- **Structural operations** (paragraph insert / split / merge / delete) — in core, sequenced last in the applier.
- **Drift-proof AI anchoring** — bookmark on the generate window; AI returns paraId-anchored JSON operations; resolve-on-return + validate; fuzzy-as-comment last resort.
- **Reliable save under concurrency + Office locks** — version-stamp, re-anchor-on-stale, eTag sequencing, HTTP 423 lock protocol.
- **Born-in-editor unification** — new (source-less) documents feed the SAME operation/patch model.
- **Import round-trip** — pre-existing Word `w:ins`/`w:del`/`w:comment` render as first-class tracked changes/comment threads in the editor.
- **Phase 0 proof gate** — fidelity corpus + round-trip byte-diff harness + the operation schema + applier spike (the pre-commit safety net for the hard-replace cutover).
- **Hard-replace cutover** — remove the text-search writer, the paragraph-diff synthesizer, the paragraph-diff export, and residual `mammoth`.

### Out of Scope
- **Multi-format** (pdf / xlsx / pptx) — deferred later phase; architecture designed to extend, not built in R4.
- **WOPI-embedded editor** — SPE stays store + open-in-Office launch surface only.
- **Any commercial / per-seat / AGPL component** — SuperDoc, Syncfusion, TipTap Pro all excluded.
- **"Full Word fidelity" / pixel-perfect in-editor render** — non-goal; display bar is "readable + editable with content/structure faithful," escape hatch is open-in-Word.
- **Runtime dependency on EigenPal** — official repo is a closed facade; the frozen Apache-2.0 fork is study-reference only (see Assumptions).

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Services/Compose/` — new `ComposeShadowPatchEngine`; extend `ComposeDocxProjectionBuilder`/`ComposeDocxProjection`/`ComposeBaselineParaIdStamper`; retire `DocxAnnotationWriter` + `ComposeParagraphRedlineSynthesizer`; keep `AnnotationReanchorService`/`ParaIdPreParser`.
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` — operation-log save contract (replaces text-anchored `DocxAnnotation` + `{paraId,text}` payloads).
- `src/client/shared/Spaarke.Compose.Components/src/` — ProseMirror step→operation interceptor; extend `paraIdExtension`; opaque-atom node; AI-generation bookmark; delete `collectEditedParagraphs`/paragraph-diff export; remove `mammoth`.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/` + `tests/integration/seam/**` — patch-engine unit tests + through-the-wire save/load seam slices + corpus round-trip harness.

## Requirements

### Functional Requirements

1. **FR-01 — Tagged shadow ingest.** On load, every editable `w:p` carries a persisted `w14:paraId` (minted + written into the retained package if absent); the projection emits the editor HTML/JSON **plus** an intra-paragraph offset-addressing table (paraId → run-boundary map). — Acceptance: every paragraph in the corpus docs resolves to a unique paraId present in the retained bytes; addressing table round-trips offsets to run splits deterministically.
2. **FR-02 — Opaque atoms.** Non-renderable constructs (SDT/content controls, fields, complex/floating objects) render as non-editable placeholder atom nodes that carry their paraId and preserve document order. — Acceptance: corpus docs containing fields/SDTs load without editor error; the constructs survive save byte-for-byte (they are never opened).
3. **FR-03 — Step-level operation capture.** Editor edits are captured as operations anchored `(paraId, runIndex, run-local-offset)` from ProseMirror transaction steps; the client sends an ordered, rebased operation log + base version. The `getHTML`/paragraph-diff export path is removed. — Acceptance: a typed edit at an interior location emits an operation with the correct anchor; no `{paraId,text}` payload or `docx.js` export remains.
4. **FR-04 — Unified Patch Engine.** `ComposeShadowPatchEngine(retained bytes, operation log) → patched bytes` resolves each node by `w14:paraId` (O(1)), splits runs at the offset, and emits native `w:ins`/`w:del`/`w:comment`; untouched subtrees are byte-identical (package parts never opened are verbatim; edited `document.xml` is structurally faithful, with an XML-splice hardening option for true byte-identity within it). — Acceptance: interior-location edit/redline/comment lands at the intended paraId with **zero** write-path text-search; corpus no-op save is byte-identical on untouched parts.
5. **FR-05 — Structural operations (in core, sequenced last).** The engine supports paragraph split / merge / insert / delete as operations. — Acceptance: each structural op round-trips correctly on the corpus (incl. paragraph-mark deletion via `w:pPr/w:rPr/w:del`).
6. **FR-06 — Retire both legacy writers.** All save/annotation writing routes through the Patch Engine; `DocxAnnotationWriter` (text-search) and `ComposeParagraphRedlineSynthesizer` (paragraph-diff) are deleted, with their EDGE-1…4 wisdom migrated. — Acceptance: `grep` shows no remaining call sites; both classes removed.
7. **FR-07 — Drift-proof AI anchoring.** On Generate, a ProseMirror Decoration/bookmark is dropped at the selection (rebased through concurrent edits) and the target paraId is sent to the model as context; the model returns JSON operations referencing paraId; on return the bookmark resolves to its current position and every returned anchor is validated before apply; unvalidatable ops surface as a review comment, never silently placed. — Acceptance: generation with concurrent user edits lands at the rebased selection; an out-of-range/unknown anchor is refused, not mis-placed.
8. **FR-08 — Concurrency + reliable save.** Every save is version-stamped (SPE eTag + projection schema version); a stale base triggers re-anchor via `AnnotationReanchorService` (AUTO apply / REVIEW+ORPHAN surface) instead of failing; create-on-save is sequenced so the content write uses the post-create eTag; HTTP 423 (Office lock) maps to a user-actionable state. — Acceptance: stale-base save re-anchors without eTag 500; create-on-save then content write does not throw the eTag mismatch; a locked item surfaces a ProblemDetails, not a 500.
9. **FR-09 — Born-in-editor unification.** Documents drafted from scratch feed the SAME operation/patch model (initial content = an insert-everything op set onto an empty shadow package). — Acceptance: a born-in-editor doc saves via the Patch Engine (no separate full-render export path remains).
10. **FR-10 — Import round-trip.** Pre-existing Word `w:ins`/`w:del`/`w:comment` render in the editor as first-class tracked changes + comment threads. — Acceptance: a doc redlined externally in Word opens with those revisions/comments visible and accept/reject-able (reader exists; the editor mount is the work).
11. **FR-11 — Operation schema (the spine contract).** A shared, versioned operation contract (op types: insertText, deleteRange, replaceRange, setMark/clearMark, splitParagraph, mergeParagraph, insertParagraph, deleteParagraph, setBlockAttr; each anchored `{paraId, runIndex, offset|range}`) implemented identically by client and server. — Acceptance: schema is the single source both ends compile against; round-trips validate.
12. **FR-12 — Hard-replace cutover (gated by Phase 0 proof).** The old paths are removed only after the Phase 0 gate passes (operation-schema + applier spike on the CIPO doc + corpus byte-diff harness green). Residual `mammoth` is removed once no projection-less mount remains. — Acceptance: Phase 0 gate green BEFORE any old-path deletion; post-cutover, one engine remains and `mammoth` is gone.

### Non-Functional Requirements

- **NFR-01 — Byte preservation.** Load → no-op save of every corpus doc → untouched OOXML subtrees byte-identical (package parts never opened verbatim; `document.xml` structurally faithful, with XML-splice hardening available if strict byte-identity within it is required). Verify: byte-diff harness.
- **NFR-02 — Placement determinism.** 100% of edits/redlines/comments (user + AI) land at the intended anchor; **zero text-search in the write path**. Verify: corpus + code audit + seam tests.
- **NFR-03 — Licensing.** MIT/permissive only; no commercial / per-seat / AGPL; no TipTap Pro; MIT TipTap base + `@tiptap/extension-*` only; no runtime dependency on EigenPal.
- **NFR-04 — Publish size.** BFF ≤60 MB compressed; zero new runtime package for the docx core (`DocumentFormat.OpenXml` already present); if Docxodus is adopted, verify its size delta per BFF task.
- **NFR-05 — Facade discipline.** `Services/Compose/` stays pure — no `IOpenAiClient`/executor/routing type (ADR-013 Tier-1 NetArchTest); no `Microsoft.Graph` type above `SpeFileStore` (ADR-007). Patch Engine is `byte[]`-in/`byte[]`-out.
- **NFR-06 — E2E DoD.** Every save/load/dispatch change carries a through-the-wire `WebApplicationFactory` seam slice (`tests/integration/seam/**`); no `Mock<HttpMessageHandler>`, no DI-registration/ctor-null tests (ADR-038).
- **NFR-07 — Word-native output.** Results open in Word / Word-for-web with real accept/reject redlines + threaded comments.
- **NFR-08 — Hard-replace safety.** Because cutover is hard-replace (no parallel-run A/B), the Phase 0 proof gate is a HARD prerequisite to old-path removal; the corpus harness is the acceptance evidence, run before and after cutover.

## Technical Constraints

### Applicable ADRs
- **ADR-013** — AI architecture / facade discipline (CRUD/Compose→AI via PublicContracts; no AI internals in `Services/Compose/`).
- **ADR-007** — Graph isolation (SPE hop via `SpeFileStore`; no `Microsoft.Graph` type in the engine).
- **ADR-009** — Redis-first (version/re-anchor summary state via `IDistributedCache`, not `IMemoryCache`).
- **ADR-010** — DI minimalism (concrete singletons; the Patch Engine is a stateless singleton).
- **ADR-028** — Spaarke Auth v2 client contract (`useAuth`/`authenticatedFetch`; no custom token props on the editor).
- **ADR-038** — Testing strategy (integration-heavy; seam DoD; banned mock/DI/ctor tests).
- **ADR-039 / ADR-040** — Grounded execution / session ledger (AI redline path stays envelope-only; no new dispatch endpoint, engine frozen).

### MUST Rules
- ✅ MUST hold the retained original OOXML as source of truth; MUST NOT re-derive the `.docx` from the editor model.
- ✅ MUST anchor by `(paraId, runIndex, run-local-offset)`; MUST NOT text-search in the write path; MUST NOT use absolute editor positions or `w:r`-ids as the durable anchor.
- ✅ MUST route all save/annotation writing through the single Patch Engine; MUST retire both legacy writers.
- ✅ MUST mutate the OpenXml DOM (never string-edit `document.xml`); MUST handle paragraph-mark deletion via `w:pPr/w:rPr/w:del`.
- ✅ MUST keep `AnnotationReanchorService` fuzzy match as the cross-Word-session / stale-base fallback (Word regenerates paraIds on external tracked-change saves).
- ✅ MUST pass the Phase 0 proof gate BEFORE any hard-replace deletion.
- ❌ MUST NOT add any commercial/per-seat/AGPL/TipTap-Pro component; MUST NOT take a runtime dependency on EigenPal.
- ❌ MUST NOT add a new AI dispatch endpoint or change AI catalog rows (engine frozen; ADR-039).

### Existing Patterns to Follow
- Custom `w:p`→HTML projection: `Services/Compose/ComposeDocxProjectionBuilder.cs` (extend, don't replace).
- Fuzzy re-anchor bands: `Services/Compose/AnnotationReanchorService.cs`.
- Native OOXML revision/comment edge-cases (EDGE-1…4): `Services/Compose/DocxAnnotationWriter.cs` (migrate wisdom, then retire).
- Bridge prior art (study references): `notes/bridge-prior-art.md` — ProseMirror `transform`/`changeset`, Slate op schema, Docxodus (server patch), frozen `sorenlouv/docx-editor` fork (projection reference).

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
BFF=Y → Placement Justification required per new component (below); ≤60 MB publish ceiling applies per task; run `/conflict-check` before any BFF PR (overlaps `spaarkeai-compose-r3`, `spaarke-ai-architecture-redesign-r2` on `Services/`).

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `ComposeShadowPatchEngine` (server) | `DocxAnnotationWriter` + `ComposeParagraphRedlineSynthesizer` (both retired) | No — it **replaces** both; consolidates, not adds | Text-search 422 class + two-path drift persist; interior-location edits fail |
| Operation schema (shared contract) | `DocxAnnotation` (text-anchored) + `ComposeEditedParagraph` `{paraId,text}` (both retired) | No — both are text-/paragraph-coarse; op schema is the paraId+offset spine | Client/server can't agree on ID-anchored edits; drift returns |
| ProseMirror step→operation interceptor (client) | `collectEditedParagraphs` (retired) | No — replaces paragraph-diff capture with step-level | No structural edits; coarse deltas that re-diff runs |
| Offset-addressing table (server, in projection) | `ComposeDocxProjection.ParaIdMap` (paragraph-level) | **Extend** `ComposeDocxProjectionBuilder`/`ComposeDocxProjection` | Ops can only address whole paragraphs — back to paragraph-granularity |
| Opaque-atom node (client + projection) | none | No — new node type for non-renderable OOXML | Fields/content-controls break the editor or drop on save |

Everything else is modify/extend of KEEP-list assets (`notes/as-built-inventory.md`), not new surface.

## ADR Tensions (per CLAUDE.md §6.5)

| ADR / prior decision | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **R3 project decision (FR-02)** | Paragraph-diff synthesizer is the delta engine | R4 replaces it with step-level operational deltas | **B (amendment)** | Paragraph-diff can't do structural edits and keeps a second text-search path alive; the operational model supersedes it. Amend the project-level decision in the R4 ADR (Phase 0). |
| **ADR-013** | `Services/Compose/` must not reach AI internals | AI returns operations | **C (comply)** | Op validation/apply is pure; AI dispatch stays behind the facade; Tier-1 NetArchTest enforces. |
| **ADR-007** | No `Microsoft.Graph` above `SpeFileStore` | Patch Engine + save touch SPE | **C (comply)** | Engine is `byte[]`-in/out; SPE hop stays in endpoint/facade. |
| **ADR-038** | Integration-heavy; seam DoD | New engine + save path | **C (comply)** | Seam slices for every save/load change + corpus round-trip harness. |
| **NFR-03 (no TipTap Pro / AGPL)** | MIT base only | Op capture needs raw ProseMirror step/decoration APIs | **C (comply)** | Raw ProseMirror APIs are MIT on the base editor; no Pro extension. |

## Success Criteria
1. [ ] **Byte-preserving** — no-op save byte-identical on untouched subtrees across the corpus. Verify: byte-diff harness.
2. [ ] **Placement determinism** — every edit/redline/comment (user + AI) lands at its anchor; zero write-path text-search. Verify: corpus + code audit + seam tests.
3. [ ] **AI drift-proof** — generation with concurrent edits lands at the rebased selection; bad anchors refused. Verify: ProseMirror test + CIPO UAT.
4. [ ] **Concurrency** — stale-base save re-anchors (no eTag 500); 423 surfaces cleanly. Verify: seam tests + UAT.
5. [ ] **Structural edits** — split/merge/insert/delete round-trip on the corpus. Verify: harness.
6. [ ] **Word-native output** — opens in Word-for-web with accept/reject redlines + threaded comments. Verify: manual + UAT.
7. [ ] **Hard-replace complete** — both legacy writers + paragraph-diff export removed; `mammoth` gone; publish ≤60 MB; ADR + tests green. Verify: grep + `dotnet publish` size + CI.
8. [ ] **Phase 0 gate passed before cutover** — operation schema + applier spike on CIPO + corpus harness green PRIOR to any old-path deletion. Verify: commit/PR ordering.

## Dependencies

### Prerequisites
- **New worktree** `spaarkeai-compose-r4`, registered in `projects/INDEX.md` (BFF=Y, SpaarkeAi=Y) — via `/project-pipeline`.
- **Fidelity corpus** assembled as LFS fixtures (Phase 0): CIPO patent letter (known 422 + empty-paragraph case) + owner-supplied worst-offenders.
- **Deployed base** = `spaarkeai-compose-r3` Phase-1 + Bug-A (the retained-projection groundwork R4 extends).

### External Dependencies
- `DocumentFormat.OpenXml` (MIT, already present). Optional: **Docxodus** (MIT) if the Phase-0 patch-engine A/B selects it.
- Resolve the cross-project **notifications build break** on tip-of-master (`spaarke-notification-spine-r1` `@spaarke/notifications` unwired dep) before any SpaarkeAi deploy from tip.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Architecture (D1–D5) | Confirm the five locked decisions? | **Confirm all D1–D5** | Step-level deltas, `(paraId,runIndex,offset)` anchor, docx-only core, SPE-not-WOPI, one Patch Engine — all binding. |
| Cutover | Hard replace vs. flagged parallel-run? | **Hard replace** | No parallel-run A/B; Phase 0 proof gate (schema+spike+corpus harness) becomes the pre-commit safety net; old paths removed only after gate green (FR-12, NFR-08). |
| Structural edits | R4 core or later stretch? | **In core, sequenced last** | Split/merge/insert/delete are FR-05, built last within the Patch Engine phase. |
| Born-in-editor | Same model or separate render path? | **Unify into one model** | New docs = insert-everything op set onto an empty shadow package (FR-09); no parallel full-render path. |

## Assumptions
- **Corpus sourcing**: assuming CIPO + ~6 owner-supplied (redacted) worst-offender documents covering tables, tabs, multi-level numbering, headers/footers, fields, content controls, pre-existing tracked changes, multi-section. Owner to supply the real documents in Phase 0.
- **Patch-engine A/B**: assuming a genuine Phase-0 evaluation of Docxodus (MIT, active) vs. build-on-OpenXML-SDK; no assumption of which wins.
- **EigenPal**: study-reference only (official repo = closed facade; frozen `sorenlouv/docx-editor` fork = Apache-2.0 parser/serializer reference, used to inform our own projection, not as a runtime dependency).
- **Display fidelity**: in-editor render is "readable + structure-faithful," not pixel-perfect Word; multi-level numbering may render approximately; open-in-Word is the exact-view escape hatch.

## Unresolved Questions
- [ ] **Corpus documents** — owner to supply the worst-offender set (Phase 0). Blocks: the byte-diff harness acceptance evidence + the hard-replace gate.
- [ ] **True byte-identity within `document.xml`** — is structural fidelity (SDK re-serialize) sufficient, or is the XML-splice hardening required for NFR-01? Decide from corpus results. Blocks: NFR-01 final acceptance bar.
- [ ] **Docxodus adoption** — decided by the Phase-0 patch-engine A/B (fidelity + fit + publish-size). Blocks: FR-04/FR-11 implementation choice.

---
*AI-optimized specification. Original design: `design.md`.*
