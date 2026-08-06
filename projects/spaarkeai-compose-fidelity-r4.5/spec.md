# Spaarke Compose — Legal Fidelity (R4.5) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-28
> **Source**: `design.md` (authored 2026-07-28)
> **Relationship**: Priority interstitial between R4 (Shadow Document Architecture — shipped, merged to master 2026-07-24 as `a58c0b5cc`) and R5 (Editing Completeness — backlog). R4.5 finishes R4's **read + reference** promise and absorbs R5 **G6**; the rest of R5 stays deferred.

## Executive Summary

Spaarke Compose is the editor surface of a **legal** document platform, where the words and the numbering of a contract *are* the contract. R4 built the high-fidelity server-side projection (`ComposeDocxProjectionBuilder`, `paraId`-anchored) but wired it into only **one** doorway (stored-doc Load), leaving uploads/browse/open-in-compose on the lossy client `mammoth` reader, and never computed or persisted the **displayed numbering** or a `paraId → legal-number` reference. R4.5 closes those gaps across five workstreams: route every entry path through the single server reader (WS-1), harden that reader against silent text drops (WS-2), reconstruct clause/section/heading/list numbering deterministically from the OOXML numbering model (WS-3), persist and expose a `paraId → {computed number, level}` citation map (WS-4), and spike the page/line-numbering pagination question to a licensed, measured decision (WS-5). The thesis: **read a legal document with perfect fidelity and make it referenceable.**

## Scope

### In Scope

- **WS-1 — One reader everywhere** (absorbs R5 **G6**): route upload / browse / open-in-compose through the server projection; delete the client `mammoth` fallback and `docxToTipTapHtml` (`docxBridge.ts`) from the Compose path.
- **WS-2 — Harden the projection read** (F-1): fix silent drops (`w:sym`, `w:cr`), emit `w:ind` indentation, apply `white-space:pre-wrap` for preserved whitespace, warn-don't-drop, and complete a full OOXML run/block construct audit with tests.
- **WS-3 — Deterministic numbering reconstruction** (F-3): compute clause/section/heading/list numbers 100% from the numbering model (direct `w:numPr` + style-linked), render them as an **explicit non-editable number-atom** per paragraph.
- **WS-4 — Reference / citation layer** (F-4): extend the projection with per-paragraph `computedNumber`, `numberingLevel`, `listPath`, `headingLevel`; persist the `paraId → legal-number` map **both** in the projection payload **and** the document session ledger; expose to the analysis/citation tool, including **sub-item depth (`4.2(b)(iii)`) and contiguous ranges (`Sections 4–7`)**.
- **WS-5 — Page/line numbering spike + decision** (F-5): prototype LibreOffice-headless pagination, evaluate a Word-rendering-service path, measure Word-divergence on the corpus, resolve the NFR-03 licensing path, and deliver a **go/defer decision** — implementation of pagination is an explicit possible fast-follow, not committed in R4.5.

### Out of Scope (stays in R5)

- **G1** cross-session authored-doc clean lifecycle; **G2** clean apply mode.
- **G3** `setBlockAttr` applier (headings/lists/alignment as tracked **edits**) — *coupled to WS-3* (shares the numbering model; see FR-14).
- **G4** table op; **G5** hyperlink op (reading tables/hyperlinks already works via the projection).
- **G7** Save-Version/Save-New UX; **G8** external-change refresh banner; **G9** comment scroll-sync; **G10** Document Profile re-run (but WS-4's reference layer is what makes G10 profile citations precise — note the dependency).
- **Pagination implementation** — WS-5 delivers a decision + spike only; shipping page/line numbering is a possible fast-follow.
- **Edit-time live renumber** (insert/delete a clause → downstream numbers shift, reflected in redline) — R5 **G3** territory; R4.5 guarantees read-time numbering correctness only.
- **Byte-authoring changes** — R4.5 is read/reference only; the R4 two-author (create/edit) split stands untouched.
- **`mammoth` repo removal** — `mammoth` remains a repo dependency (used by SprkChat + Notepad); only its **Compose** call sites are removed.

### The scope boundary (one sentence)

**R4.5 is about *reading* a legal document with perfect fidelity and making it *referenceable*; R5 is about *editing* it with full formatting fidelity.** WS-1..WS-4 are read/reference; the deferred G-items are edit/UX/lifecycle.

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs` — WS-2 (harden read), WS-3 (numbering engine).
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjection.cs` — WS-4 (reference fields on projection / `ParaIdMapEntry`).
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` — WS-1 (upload returns a projection; new stateless `project` endpoint for browse).
- `src/server/api/Sprk.Bff.Api/Services/Compose/IComposeService.cs` / `ComposeService.cs` — WS-1 (upload projection wiring), WS-4 (session-ledger persistence of the reference map).
- `src/server/api/Sprk.Bff.Api/Services/Compose/ParaIdPreParser.cs` / `AnnotationReanchorService.cs` / `DocxAnnotationReader.cs` — WS-4 (reference-map ↔ `paraId` re-anchor integration; read-only awareness).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` (~`:1667`/`:1718`) — WS-1 (delete mammoth fallback branch), WS-3 (render number-atom).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` (~`:1891-1983`) — WS-1 (upload/browse effects hydrate `projection`).
- `src/client/shared/Spaarke.Compose.Components/src/**/docxBridge.ts` — WS-1 (delete `docxToTipTapHtml`).
- `tests/fixtures/compose-corpus/` + projection test suite (`ComposeDocxProjectionBuilderTests.cs`) — WS-2/WS-3/WS-5 corpus + assertions.

## Requirements

### Functional Requirements

**WS-1 — One reader everywhere**
1. **FR-01**: `POST /api/compose/upload` runs uploaded bytes through `ComposeDocxProjectionBuilder` and returns a `ComposeServerProjection` (same shape as the Load path, `IComposeService.cs:356-362`), not raw bytes. — Acceptance: upload response carries a projection; client upload effect hydrates `projection` and takes the projection branch identical to stored-doc Load.
2. **FR-02**: Open-in-Compose transient drafts are projected server-side before mount (no `projection: null` in `mountTransient`). — Acceptance: transient mount renders via projection; no mammoth branch reached.
3. **FR-03**: Browse-local-`.docx` renders via the server reader through a **read-only, stateless** `POST /api/compose/project` (bytes → projection, no persistence, no authoring). — Acceptance: browsed docx renders via projection; endpoint persists nothing; client authors no bytes (I-2 preserved). Resolves ADR Tension **T-2**.
4. **FR-04**: The `mammoth` fallback branch in `ComposeEditor.tsx` and `docxToTipTapHtml` in `docxBridge.ts` are deleted; `mammoth` has **zero Compose call sites** (grep-proven). — Acceptance: `grep` finds no `mammoth` / `docxToTipTapHtml` reference under `Spaarke.Compose.Components`; `mammoth` remains for SprkChat/Notepad only.

**WS-2 — Harden the projection read**
5. **FR-05**: `w:cr` (`CarriageReturn`) is represented (`<br>` or preserved separator), never silently dropped. — Acceptance: corpus doc with `w:cr` renders the break; construct test asserts emission.
6. **FR-06**: `w:sym` (`SymbolChar`) maps the symbol-font code point to its Unicode equivalent where known (e.g. Symbol/Wingdings → Unicode, **§**), else emits a visible placeholder **and** a warning — never a silent drop. — Acceptance: corpus doc using `w:sym` **§** renders the section mark or a warned placeholder; no glyph vanishes.
7. **FR-07**: `w:ind` (left / first-line / hanging) is emitted as `style="margin-left/…"` (or an indent class). — Acceptance: indented paragraphs render at authored indentation; test asserts the emitted style.
8. **FR-08**: The editor surface applies `white-space:pre-wrap` (or equivalent) so `xml:space="preserve"` runs and consecutive spaces render as authored. — Acceptance: preserved-whitespace corpus doc renders spacing verbatim.
9. **FR-09**: A full OOXML run/block construct audit is completed; every construct is either represented or warned (`w:noBreakHyphen`, `w:tab`, fields RESULT-not-CODE, `w:br type=page`, `w:softHyphen` correctly dropped, etc.), with a test per construct. — Acceptance: construct-audit note enumerates the set with disposition; projection test suite adds alignment, ordered-list, and symbol tests (absent today).
10. **FR-10**: No glyph is dropped without a warning; the warning mechanism observes **intra-run glyphs**, not just paragraph counts (today's F-03 alignment guard counts paragraphs). — Acceptance: dropping/placeholdering a `w:sym`/`w:cr` raises a surfaced warning.

**WS-3 — Deterministic numbering reconstruction**
11. **FR-11**: The projection computes displayed clause/section/heading/list numbers 100% server-side by replaying Word's numbering algorithm over the OOXML numbering model — a single document-order walk maintaining a counter per `(abstractNumId, level)`, honoring `w:start`, `w:lvlRestart`, `w:lvlOverride`/`w:startOverride`, `w:numFmt` (decimal/lowerLetter/upperLetter/lowerRoman/upperRoman/bullet/…), `w:lvlText` template, and `w:isLgl` (legal → decimal). — Acceptance: interrupted, multi-level, and style-linked numbering all compute correctly; no "1." collapse; no dropped heading numbers.
12. **FR-12**: Style-linked numbering is resolved — a paragraph style (e.g. `Heading2`) that carries the `w:numPr` numbers its clauses by style, not just direct `w:numPr`. — Acceptance: heading-style corpus doc ("4.2 Confidentiality") renders the style-derived number.
13. **FR-13**: The editor renders the computed label as an **explicit non-editable number-atom** prefix on the paragraph node (not via browser `<ol>` CSS auto-count), so the number is a fixed computed artifact of the source and does not silently re-flow. — Acceptance: numbers render identical to Word for letters/roman/"Article I"/style-linked schemes; interrupting a numbered run (heading/body/table) does not restart the count at 1; no CSS-counter dependency for a legal number.
14. **FR-14**: Read-side numbering does **not** auto-renumber on edit within R4.5. Live renumber-on-insert/delete (reflected in redline) is documented as R5 **G3**; WS-3's numbering engine is the shared model G3 will build on — the coupling is recorded for R5. — Acceptance: spec + task notes state the read-time-only guarantee and the G3 dependency explicitly.
15. **FR-15**: Read-side computation agrees with the write-side mirror `ComposeDocumentRenderer.cs` (which authors the `%N.` style-linked cascade + `w:start` into `numbering.xml`). — Acceptance: a round-trip test (author → read → identical labels) passes.

**WS-4 — Reference / citation layer**
16. **FR-16**: `ComposeDocxProjection` / `ParaIdMapEntry` carry per-paragraph `computedNumber` (e.g. `"4.2"`), `numberingLevel`, `listPath` (ordinal chain `[4,2]`), `headingLevel`, plus existing `docOrderIndex` + `paraId`. — Acceptance: projection payload exposes all fields per paragraph.
17. **FR-17**: The `paraId → legal-number` map is persisted **both** in the projection payload **and** with the document session ledger, so it survives edits (`paraId` stays stable per R4; new/split paragraphs re-anchor per R4). — Acceptance: reloading a session resolves the map without recompute divergence; edited docs keep stable `paraId → number` for unchanged paragraphs.
18. **FR-18**: The analysis/citation tool can cite a human reference and resolve it to the exact paragraph `paraId` (and vice versa), covering single labels ("Section 4.2"), **sub-item depth** ("4.2(b)(iii)"), and **contiguous ranges** ("Sections 4–7"). — Acceptance: citation resolution returns the correct `paraId`(s) for all three reference shapes on the corpus.

**WS-5 — Page/line numbering spike + decision**
19. **FR-19**: WS-5 delivers a written decision record containing: (a) a LibreOffice-headless pagination prototype producing a page/line map with **measured divergence from Word** on the corpus; (b) an evaluation of a Word-rendering-service (Graph/Office) path with ops/licensing/latency cost; (c) the NFR-03 licensing analysis (permissive-only; LibreOffice invoked as a **separate process/service**, not linked); (d) a **ship-in-R4.5 vs fast-follow** recommendation. — Acceptance: decision record exists; no commercial/AGPL paginator linked into the BFF; the product makes no page/line "100%" claim beyond what the chosen engine guarantees.

### Non-Functional Requirements

- **NFR-01 (Text exactness / F-1)**: The reader emits run text **verbatim** — character-for-character — the only permitted transform being lossless HTML-structural encoding (`&`/`<`/`>`). No trimming, collapsing, smart-quote rewriting, or silent glyph drops. Every corpus doc passes a character-for-character text-equality assertion (source runs == projected text, per paragraph); any ❌ is a release blocker.
- **NFR-02 (Numbering exactness / F-3)**: Every corpus doc's clause/section/heading/list numbers render **identical to Word**, captured as a golden value per doc; any ❌ is a release blocker.
- **NFR-03 (Permissive licensing)**: MIT/permissive only — no commercial (Aspose, GemBox, Syncfusion) or AGPL paginators linked into the BFF. LibreOffice (MPL-2.0/LGPL) is permitted only as a **separate process/service**. Governs WS-5 (ADR Tension **T-1**).
- **NFR-04 (Publish size / BFF Hygiene §10)**: WS-1..WS-4 add **no runtime package** (pure OOXML computation on the existing `DocumentFormat.OpenXml` dependency) — expected ~0 MB delta; ceiling **≤60 MB compressed**. WS-5's pagination engine, if pursued, is a **sidecar/container** with its own size + ops budget and MUST NOT be added to the BFF publish. Measure + report compressed size + diff vs baseline (~49.63 MB incl. PDBs) on every BFF-touching task.
- **NFR-05 (Single auditable reader / F-2)**: Exactly one docx→editor reader exists (the server projection). No second code path may re-introduce a client-side docx parser for Compose.
- **NFR-06 (Determinism / F-3)**: Numbering computation is deterministic — identical inputs produce identical labels across runs and match Word's algorithm; no reliance on browser render order.

## Technical Constraints

### Applicable ADRs

- **ADR-040**: Browse-local docs are client-only (no BFF round-trip for authoring). WS-1's browse path must not author bytes server-side — see Tension **T-2** (read-only stateless projection is the resolution).
- **ADR-013**: No AI-internal type injection into CRUD/read code; use `Services/Ai/PublicContracts/` facade if AI capability is needed. WS-4 exposes data **to** the analysis layer via the projection contract — it does not inject `IOpenAiClient`/`IPlaybookService` into Compose.
- **ADR-007**: No `Microsoft.Graph` usage above `SpeFileStore`. The projection stays `byte[]`-in / projection-out.
- **ADR-038**: Testing strategy — the fidelity harness (text-exact + numbering-exact golden assertions) is an integration/seam vertical slice; add tests at KEEP paths, no `Mock<HttpMessageHandler>` / DI-registration / ctor-null tests.

### MUST Rules

- ✅ MUST emit run text verbatim; MUST surface any unrepresentable construct as a **warning**, never a silent drop (F-1).
- ✅ MUST route every Compose entry path (stored-doc, upload, browse, open-in-compose) through `ComposeDocxProjectionBuilder` (F-2).
- ✅ MUST compute displayed numbers server-side from the OOXML numbering model; MUST NOT rely on browser `<ol>` auto-count for a legal number (F-3).
- ✅ MUST persist `paraId` + computed legal number + level in the projection so citations survive edits (F-4).
- ❌ MUST NOT fabricate page/line numbers from OOXML alone; MUST NOT link a commercial/AGPL paginator into the BFF (F-5 / NFR-03).
- ❌ MUST NOT author bytes on the browse path (I-2 / ADR-040).
- ❌ MUST NOT add a runtime package for WS-1..WS-4; MUST NOT add the WS-5 pagination engine to the BFF publish.

### Existing Patterns to Follow

- `ComposeDocxProjectionBuilder.cs` — single document-order walk invariant (`:18-26`); `ListInfo` (`:779-789`), `ResolveOrdered` (`:791-814`), list emit (`:268-280`); `AppendAlignment` (`:816-830`), atoms (`:615-670`), `AppendEscaped` (`:689-693`), `w:noBreakHyphen` (`:702-703`), `w:tab` (`:695-698`).
- `ComposeDocumentRenderer.cs` — write-side numbering mirror (`LevelText` / `StartNumberingValue`) that WS-3 read-side must agree with (FR-15).
- `ParaIdPreParser.cs` (`:114-143`), `AnnotationReanchorService.cs` (`:14-22`), `DocxAnnotationReader.cs` `ParagraphHint` (`:323-333`) — the `paraId` + doc-order-index re-anchor layer WS-4 extends.
- `IComposeService.cs:356-362` — the projection shape the Load path returns and the upload path (FR-01) must match.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- Services/Compose projection + upload/project endpoints -->
  <spaarkeai>Y</spaarkeai>    <!-- Compose hosted in sprk_spaarkeai; client mount path (mammoth removal) -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Placement Justification (BFF=Y)**: All server work stays inside `Services/Compose/` — no new top-level surface, no AI-internal injection (ADR-013), `byte[]`-in/projection-out (ADR-007). WS-1..WS-4 **extend** `ComposeDocxProjectionBuilder` + `ComposeDocxProjection` — the component that should own docx-read fidelity. Per `.claude/constraints/bff-extensions.md`, the ≤60 MB publish ceiling applies per task; WS-1..WS-4 expect ~0 MB delta, WS-5's engine is an out-of-publish sidecar. Coordinate deploys on the shared `sprk_spaarkeai` dev web resource; run `/conflict-check` before every PR (overlaps prior compose projects + active SpaarkeAi work).

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| Numbering engine (WS-3) | `ComposeDocumentRenderer.cs` authors numbering (write-side); `ComposeDocxProjectionBuilder.cs` `ResolveOrdered` reads a single bullet-vs-ordered bit | No new abstraction — a new *capability inside* `ComposeDocxProjectionBuilder` (read-side computation of the same model) | Numbered clauses render "1." repeated on every interruption; heading-style numbers ("4.2") dropped entirely |
| Reference map fields (WS-4) | `ParaIdMapEntry` / `ComposeDocxProjection` already carry `paraId` + `docOrderIndex` | Yes — add fields (`computedNumber`, `numberingLevel`, `listPath`, `headingLevel`) to existing types | The analysis/citation tool cannot cite a section by number; anchors only on opaque `paraId` + raw text + doc-order index |
| `POST /api/compose/project` (WS-1 browse) | `POST /api/compose/upload` exists (persists) | No — browse is client-only per ADR-040; needs a **stateless, no-persist** read-only projection endpoint distinct from upload | Browse-local `.docx` keeps falling back to lossy `mammoth`, breaking F-2 (single reader) and legal read-fidelity |
| Pagination pipeline (WS-5, spike-only) | none | No — page/line numbering requires a Word-compatible layout engine absent from the codebase | Page/line references impossible; a legal pleading's line citations cannot be delivered |

## ADR Tensions (per CLAUDE.md §6.5)

| # | ADR / rule | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|---|
| **T-1** | NFR-03 (permissive-only; no commercial/AGPL) | "MUST NOT link a commercial/AGPL paginator" | Page/line numbering (WS-5) needs a layout engine; the accurate commercial paginators are barred | **A (project exception, documented) + spike** | Use LibreOffice (MPL-2.0) as a **separate process** (not linked) or a Word-rendering service; record the licensing analysis in the WS-5 decision. No commercial lib linked into the BFF. |
| **T-2** | ADR-040 / R4 I-2 | "client never authors bytes; browse is client-only, no BFF round-trip" | WS-1 wants browse-local docs to render via the *server* projection, implying sending browsed bytes to the server | **A (documented exception)** | A **read-only, stateless** `project` round-trip (bytes → projection, no persistence, no authoring) does not violate "client never authors bytes" — the server only *reads*. Alternative (client-side projection) rejected: breaks F-2 (single auditable reader). |
| **T-3** | R4 §6.5 two-author decision | create/edit byte-author split | None — R4.5 does not touch byte authoring | **C (comply)** | R4.5 is read/reference only; the create/edit split stands. Note for reviewers. |

## Success Criteria

1. [ ] **One reader** — `mammoth` has zero Compose call sites; upload/browse/open-in-compose all render via `ComposeDocxProjectionBuilder`. Verify by: grep + manual render on each entry path.
2. [ ] **Text-exact** — every corpus doc passes character-for-character text equality (source runs == projected text); `w:sym`/`w:cr` represented-or-warned; indentation preserved. Verify by: text-exactness harness assertion (release blocker on ❌).
3. [ ] **Numbering-exact** — every corpus doc's clause/section/heading/list numbers render identical to Word (golden-value harness), including interrupted, multi-level, and style-linked — 100%, no "1." collapse, no dropped heading numbers. Verify by: numbering-exactness golden assertion (release blocker on ❌).
4. [ ] **Referenceable** — projection exposes `paraId → {computed number, level}`; citation layer resolves "Section 4.2", "4.2(b)(iii)", and "Sections 4–7" to exact paragraph(s) and survives edits. Verify by: citation-resolution test on corpus + reload/edit test.
5. [ ] **Page/line honest** — WS-5 delivers a decision + (if chosen) a pagination pipeline with measured Word-divergence; the product claims no page/line "100%" beyond the engine's guarantee. Verify by: WS-5 decision record + divergence measurement.
6. [ ] **Hygiene** — BFF build + Compose suite + new fidelity harness green; publish ≤60 MB (WS-1..4 ~0 delta; WS-5 sidecar out-of-publish); Tier-1 NetArch green; no new HIGH CVE; `/conflict-check` clean. Verify by: CI + `dotnet publish` size report + `dotnet list package --vulnerable`.

## Dependencies

### Prerequisites

- **R4 on master** — R4.5 lands on top of R4 (merged 2026-07-24, `a58c0b5cc`): the projection, `paraId` machinery, and byte-diff/seam harness are the foundation.
- **R4 fidelity corpus** — `tests/fixtures/compose-corpus/` exists and is extensible.

### External Dependencies

- **WS-5 only**: LibreOffice-headless (MPL-2.0) as a separate process, **or** a Word-rendering service (Graph/Office) — evaluation + provisioning decision inside the WS-5 spike. Not required for WS-1..WS-4.
- Corpus additions authored: NDA-style interrupted-clause doc, heading-style-numbering doc, multi-level (1 / 1.1 / 1.1.1) doc, `w:sym` **§** doc, line-numbered pleading doc.

### Coupling to R5

- **G3** (edit-path `setBlockAttr` headings/lists/alignment) will build on WS-3's numbering engine — the read-side model and the edit-side renumber must share one implementation. Recorded so R5 does not fork the model (FR-14).
- **G10** (Document Profile re-run) benefits from WS-4's reference layer for precise profile citations.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| WS-3 render | Explicit number-atom vs `<ol start>` + TipTap extension? | Explicit number-atom — **and** the key invariant: numbering must not change unless the user explicitly edits (e.g. removes a section → renumber, reflected in redline) | Adopt (a) explicit non-editable number-atom (FR-13). The number is a fixed computed artifact of the source, not a live CSS auto-count → won't silently re-flow. Edit-triggered renumber-with-redline is R5 **G3** (FR-14); R4.5 = read-time correctness only. |
| WS-4 store | Where does the `paraId → number` map persist? | Both — projection payload + session ledger | FR-17: emit in projection for immediate render/citation **and** persist with the session so it survives edits. |
| WS-5 scope | Ship pagination in R4.5, or spike + decision only? | Spike + decision only | FR-19: WS-5 is a decision record (LibreOffice vs Word-service, measured divergence, licensing); pagination implementation is a possible fast-follow, not committed in R4.5. |
| Citation depth | How deep does the citation model go? | Include ranges too | FR-18: WS-4 resolves single labels, **sub-item depth ("4.2(b)(iii)")**, and **contiguous ranges ("Sections 4–7")**. Sets WS-3 label granularity accordingly. |

## Assumptions

- **Edit-time renumber**: Assuming live renumber-on-insert/delete (with redline reflection) is R5 **G3** and out of R4.5 scope — R4.5 guarantees only read-time numbering correctness (confirmed by owner's WS-3 clarification; FR-14).
- **`mammoth` retention**: Assuming `mammoth` stays in the repo for SprkChat + Notepad; only Compose call sites are removed (per design §4 WS-1).
- **Corpus authorship**: Assuming the new legal-numbering exemplar docs are authored within this project (WS-2/WS-3/WS-5 tasks), reusing the R4 corpus harness.
- **Number-atom editability**: Assuming the number-atom is non-editable and does not participate in the tracked-edit stream in R4.5 (edit interaction is G3).

## Unresolved Questions

- [ ] **Citation-model API shape** — the exact contract the analysis/citation tool consumes for ranges + sub-items ("4.2(b)(iii)", "Sections 4–7"): does the projection expose a resolver, a flat map the tool ranges over, or both? Blocks: WS-4 final interface (resolve during WS-4 design, after WS-3 label granularity is fixed).
- [ ] **`w:sym` mapping coverage** — which symbol fonts get first-class Unicode mapping (Symbol, Wingdings, …) vs. warned placeholder? Blocks: WS-2 FR-06 acceptance breadth (enumerate during the construct audit).
- [ ] **WS-5 engine choice** — LibreOffice-headless vs Word-rendering-service, and ship-vs-defer — resolved **by** the WS-5 spike, not before. Blocks: any page/line delivery claim.

---
*AI-optimized specification. Original design: `design.md`.*
