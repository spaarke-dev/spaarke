# Spaarke Compose — Legal Fidelity (R4.5) — Design

> **Project**: `spaarkeai-compose-fidelity-r4.5`
> **Status**: 📐 DESIGN (pre-spec) — authored 2026-07-28
> **Relationship**: A **priority interstitial** between R4 (Shadow Document Architecture — shipped) and R5 (Editing Completeness — backlog). R4.5 finishes R4's core promise for the **read + reference** side, which is load-bearing for a legal tool. It absorbs R5 **G6** and the newly-surfaced numbering/reference work; the rest of R5 stays deferred.
> **Origin**: dev UAT of R4 (2026-07-23 → 07-28). Uploading a real NDA surfaced that numbered sections render as "1." repeated, the centered title goes left-aligned, and — on investigation — that there is **no faithful, referenceable numbering model at all**. Evidence + root-cause in the conversation and in `../spaarkeai-compose-r4/notes/uat-feedback-2026-07-23.md` and `../spaarkeai-compose-r5/README.md`.

---

## 1. Why this project exists

Spaarke is a **legal** document platform. For legal work, four things are non-negotiable and are the thesis of this project:

1. **Text is exact.** No introduced, dropped, or altered characters — ever. A contract's words are the contract.
2. **Section / clause / paragraph numbering is 100% correct.** "Section 4.2(b)" must render exactly as Word shows it, on **every** entry path (upload, browse, stored-doc).
3. **Every paragraph/section is stably referenceable** so the AI analysis + citation layer can say "per Section 4.2" and resolve it to the exact place in the document, surviving edits.
4. **Page and line references are handled honestly** — delivered where technically possible, and explicitly scoped where they are not derivable from the file.

R4 built the machinery that makes #1–#3 *achievable* — the **server-side projection** (`ComposeDocxProjectionBuilder`) as a high-fidelity, `paraId`-anchored reader — but only wired it into **one** of the doorways (stored-doc Load), left the lossy client reader (**mammoth**) in place for uploads/browse, and never reconstructed or stored the **displayed numbering** or a **paraId→legal-number** reference. R4.5 closes those gaps.

### The provoking evidence (code-grounded)

- **Two readers, one lossy.** The editor mount is `if (projection) { render server projection } else { mammoth }` (`src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx:1667` / `:1718`). Stored docs get the faithful projection; **uploads/browse/open-in-compose fall back to mammoth** because `POST /api/compose/upload` returns raw bytes with **no projection** (`src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs:913-920`, record `:1797-1804`) and `mountTransient` sets `projection: null`.
- **Numbering is not modeled.** The projection reads `w:numPr` for a **single bit** — bullet-vs-ordered — then leaves the number to the browser's `<ol>` auto-count (`ComposeDocxProjectionBuilder.cs` `ListInfo` `:779-789`, `ResolveOrdered` `:791-814`, list emit `:268-280`). A new `<ol>` opens whenever the run of numbered paragraphs is interrupted (heading, body text, table) → **every clause restarts at 1**. Multi-level is discarded to a warning count (`:271`). Heading-style numbers ("4.2") are **dropped** (`:762-775`, `:781-782`). **No displayed number is ever computed or stored** anywhere on the read path.
- **No reference layer.** Per-paragraph identity is the opaque, random `w14:paraId` only (`ParaIdPreParser.cs:114-143`; projection `:135-167`). There is **no `paraId`→section-number map**; the annotation/re-anchor layer works purely on `paraId` + raw text + document-order index (`AnnotationReanchorService.cs:14-22`; `DocxAnnotationReader` `ParagraphHint` = doc-order index `:323-333`).
- **A few silent text drops.** On the projection path, `w:sym` (symbol glyphs — e.g. the **§** section mark, custom bullets) and `w:cr` are **dropped with no warning** (absent from the run switch; `w:t`/`DeletedText` are emitted verbatim via `AppendEscaped`, `:689-693`, `:944-957`). `w:softHyphen` is dropped (correct). Paragraph indentation (`w:ind`) is not emitted.
- **Page/line numbering is nowhere** — `w:sectPr` is skipped for output (`:247-251`) and `w:lnNumType` is never read. This is not a bug; those numbers **do not exist in the file** (see §5.5).

---

## 2. Principles / invariants (R4.5 adds these to the R4 set)

- **F-1 (Text exactness).** The reader emits document run text **verbatim** — character-for-character — with the only permitted transform being lossless HTML-structural encoding (`&`/`<`/`>`). No trimming, collapsing, smart-quote rewriting, or silent glyph drops. Any construct that cannot be represented is **surfaced as a warning**, never silently dropped.
- **F-2 (One reader).** Exactly **one** docx→editor reader exists — the server projection. Every entry path (stored-doc, upload, browse, open-in-compose) renders through it. The client `mammoth` fallback is deleted.
- **F-3 (Deterministic numbering).** Displayed clause/section/heading/list numbers are **computed server-side** by replaying Word's numbering algorithm over the OOXML numbering model — deterministic, identical to what Word renders. The editor never relies on the browser's `<ol>` auto-count for a legal number.
- **F-4 (Stable reference).** Every paragraph carries a stable `paraId` **and** its computed legal number + level, persisted in the projection, so the analysis/citation layer can reference "Section 4.2" ↔ the exact paragraph and have it survive edits (edits are `paraId`-anchored per R4).
- **F-5 (Honest layout numbering).** Page and line numbers are treated as **rendering artifacts** (§5.5): delivered via an explicit pagination capability where in scope, and never fabricated from OOXML alone.

---

## 3. Scope

### In scope (R4.5 workstreams)

| WS | Title | Absorbs |
|---|---|---|
| **WS-1** | **One reader everywhere** — route upload / browse / open-in-compose through the server projection; delete mammoth | R5 **G6** |
| **WS-2** | **Harden the projection read** — fix silent drops (`w:sym`, `w:cr`), emit `w:ind` indentation, warn-don't-drop, audit remaining constructs | new (from this investigation) |
| **WS-3** | **Deterministic numbering reconstruction** — compute clause/section/heading/list numbers 100% from the numbering model; render them exactly | new + subsumes the read-side of the "numbering" concern |
| **WS-4** | **Reference / citation layer** — persist `paraId → {computed number, level, list path, doc-order index}`; expose to the analysis/citation tool | new |
| **WS-5** | **Page/line numbering — research + decision (spike)** — determine the pagination pipeline (Word-compatible layout engine) + NFR-03 licensing path; decide deliver-now vs follow-on | new |

### Out of scope (stays in R5 — see §6 for rationale)

G1 (cross-session authored-doc clean lifecycle), G2 (clean apply mode), **G3/G4/G5** (edit-path formatting: `setBlockAttr` headings/lists/alignment, table op, hyperlink op), G7 (Save-Version/Save-New UX), G8 (external-change refresh banner), G9 (comment scroll-sync), G10 (Document Profile re-run).

### The scope boundary (one sentence)

**R4.5 is about *reading* a legal document with perfect fidelity and making it *referenceable*; R5 is about *editing* it with full formatting fidelity.** WS-1..WS-4 are read/reference; the deferred G-items are edit/UX/lifecycle.

---

## 4. Architecture & approach

R4.5 builds entirely on R4's existing projection + `paraId` machinery — no new architectural paradigm, no byte-author changes, no re-litigation of the two-author decision (that stands, R4).

### WS-1 — One reader everywhere (absorbs G6)

**End state:** the projection is the sole mapper; `docxToTipTapHtml` (`docxBridge.ts`) and the `ComposeEditor.tsx` mammoth fallback branch are deleted.

- **Upload path:** extend `POST /api/compose/upload` to run the uploaded bytes through `ComposeDocxProjectionBuilder` and return a `ComposeServerProjection` (the same shape the Load path already returns, `IComposeService.cs:356-362`), not raw bytes. The client upload effect (`ComposeWorkspace.tsx` ~1891-1983) then hydrates `projection` and takes the projection branch — identical to stored-doc Load.
- **Open-in-Compose transient drafts:** same — project server-side before mount.
- **Browse-local-`.docx`:** client-only by design (ADR-040 — no BFF round-trip). Decision required (ADR Tension T-2): either (a) a **lightweight projection round-trip** for browse (send bytes to a stateless `POST /api/compose/project` that returns a projection, no persistence), or (b) a **client-side projection** path. Recommendation: (a) — reuse the exact server reader; keeps a single auditable code path (the whole point of F-2). The client never authors bytes (I-2) and this is read-only projection, not a save.
- **`mammoth` dependency:** remove from `Spaarke.Compose.Components` once no Compose caller remains. (Note: `mammoth` is also used by SprkChat + Notepad — out of scope; the dependency stays in the repo, only the Compose usage is removed.)

**Immediate wins from WS-1 alone:** centered titles, alignment, tabs, comments/tracked-change recovery, opaque-atom handling, and save-stability all become correct on upload/browse (the projection already does these; `AppendAlignment` `:816-830`, atoms `:615-670`, revisions/comments recovery via `DocxAnnotationReader`). Numbering is **not** fixed by WS-1 alone — that is WS-3.

### WS-2 — Harden the projection read (F-1)

- **Stop the silent drops.** Add `CarriageReturn` (`w:cr`) → `<br>` (or a preserved separator) and handle `w:sym` (`SymbolChar`) — map the symbol-font code point to its Unicode equivalent where known (e.g. Symbol/Wingdings → Unicode), else emit a visible placeholder + a warning. **Never drop a glyph without a warning** (today the F-03 alignment guard counts paragraphs, not intra-run glyphs, so a dropped `w:sym`/`w:cr` is invisible).
- **Preserve indentation.** Emit `w:ind` (left/first-line/hanging) as `style="margin-left/…"` (or an indent class) — today it is dropped.
- **Whitespace display.** `xml:space="preserve"` runs and consecutive spaces are stored faithfully but collapse under default CSS `white-space:normal`; apply `white-space:pre-wrap` (or equivalent) on the editor surface so legal spacing renders as authored.
- **Construct audit.** Enumerate the full OOXML run/block construct set (`w:noBreakHyphen` — already `U+2011` `:702-703`; `w:tab` — already `compose-tab` `:695-698`; fields — already RESULT-not-CODE; `w:br type=page` — currently a line break) and either represent or warn on each. Add tests for each (the projection test suite has **no** alignment or ordered-list or symbol tests today — `ComposeDocxProjectionBuilderTests.cs`).

### WS-3 — Deterministic numbering reconstruction (F-3) — the heart of R4.5

**Principle:** the displayed number is a **computation over the file's numbering model**, not stored text. We reproduce Word's algorithm exactly, server-side, from the full OOXML we already have.

**Inputs (all present in the docx):** each paragraph's `w:numPr` (`w:numId` + `w:ilvl`); `numbering.xml` — `w:num` → `w:abstractNumId` → `w:abstractNum` with per-level `w:numFmt` (decimal / lowerLetter / upperLetter / lowerRoman / upperRoman / bullet / …), `w:lvlText` template (e.g. `%1.%2`), `w:start`, `w:lvlRestart`, `w:isLgl`, and `w:lvlOverride`/`w:startOverride`; plus **style-linked numbering** (a paragraph style — e.g. `Heading2` — that carries the `w:numPr`, so heading clauses are numbered by their *style*).

**Algorithm (deterministic, single document-order walk — reuses the projection's existing single-walk invariant `:18-26`):**
1. Maintain a counter per `(abstractNumId, level)`.
2. For each numbered paragraph (direct `w:numPr` **or** style-linked via `pStyle`), increment its level's counter, apply `w:start`/restart/override rules, and reset deeper levels per `w:lvlRestart`.
3. Format each level via `w:numFmt`, compose the label from `w:lvlText` (multi-level "4.2.1"), honoring `w:isLgl` (legal numbering forces decimal).
4. Emit the computed label **explicitly** (not via browser auto-count) and attach it to the paragraph node for both display and the reference map (WS-4).

**Rendering:** because a legal number must equal Word's exactly, the editor renders the **computed** label rather than relying on `<ol>` CSS counters. Options (spec to choose): (a) emit the number as an explicit, non-editable prefix atom on the paragraph; (b) emit `<ol start=…>` + a TipTap ordered-list extension that honors `start` and continues across interruptions. (a) is more faithful for arbitrary legal schemes (letters, roman, "Article I", style-linked headings) and decouples from CSS list quirks; (b) is lighter but limited. **Recommendation: (a).** Note the edit-time recompute consideration: on a loaded doc, edits are tracked (R4) and numbering is a display of the source; live renumber-on-insert is an **edit-side** concern that pairs with R5 **G3** and is out of R4.5 read scope — R4.5 guarantees the number is correct **as read**.

**Reference authoring parity:** `ComposeDocumentRenderer.cs` (the born-in-editor *write* path) already authors a `%N.` style-linked cascade + `w:start` into `numbering.xml` (`LevelText`/`StartNumberingValue`) — it is the write-side mirror. WS-3 is the **read-side** computation of the same model; the two must agree (a round-trip test: author → read → identical labels).

### WS-4 — Reference / citation layer (F-4)

- **Extend the projection output** (`ComposeDocxProjection` / `ParaIdMapEntry`) with per-paragraph: `computedNumber` (e.g. `"4.2"`), `numberingLevel`, `listPath` (the ordinal chain `[4,2]`), `headingLevel`, and the existing `docOrderIndex` + `paraId`.
- **Persist the `paraId → legal-number` map** with the document session so it survives edits (edits keep `paraId` stable; new/split paragraphs re-anchor per R4).
- **Expose to the analysis/citation tool:** the AI layer today anchors on opaque `paraId` + raw text + doc-order index (`ComposeService.cs` paraId ×47; reanchor by Levenshtein + position). WS-4 gives it the **human citation** ("Section 4.2") ↔ `paraId`, so retrieval/citations are legally precise and stable. This is the single most valuable output for the analysis product: a citation that resolves to the exact clause and renders exactly as the source.

### WS-5 — Page & line numbering: research + decision (F-5)

**Hard truth (must be stated in the spec):** page numbers and line numbers **are not in the .docx**. They are computed at **layout/render time** by the rendering engine from page size, margins, fonts, image/table flow, etc. `w:lnNumType` only turns line-numbering *display* on; the content→line mapping is still rendered. Therefore:

- **Paragraph / clause / section numbering → 100% guaranteed** (WS-3, deterministic from the file).
- **Page / line numbering → requires a Word-compatible layout engine.** Even then, "100% identical to Word" is only guaranteed by **Word's own layout** (headless Word / Office rendering); open engines (e.g. **LibreOffice headless**) paginate *closely* but can differ from Word at the margins. This is a genuine fidelity ceiling to surface, not hide.
- **NFR-03 constraint (ADR Tension T-1):** MIT/permissive only — no commercial (Aspose, GemBox, Syncfusion) or AGPL paginators. **LibreOffice** (MPL-2.0/LGPL) invoked as a **separate process/service** (not linked) is the leading NFR-03-compatible option; a Word-rendering service (Graph/Office) is the only true-Word-identical path and carries ops/licensing/latency cost.

**WS-5 deliverable is a spike + decision**, not necessarily implementation in R4.5: (a) prototype LibreOffice-headless pagination → page/line map, measure divergence from Word on the corpus; (b) evaluate a Word-rendering-service path; (c) decide: ship page/line via the chosen engine in R4.5, or scope it as a fast-follow with the decision recorded. R4.5 must not *promise* page/line 100% until the engine is chosen.

---

## 5. Corpus & verification

- **Fidelity corpus:** reuse `tests/fixtures/compose-corpus/` (R4) + add the legal-numbering exemplars that break today: an NDA-style doc with **interrupted** numbered clauses, a doc with **heading-style** numbering ("4.2 Confidentiality"), a **multi-level** scheme (1 / 1.1 / 1.1.1), a doc using **`w:sym` §**, and a **line-numbered pleading** doc for WS-5.
- **Read-fidelity harness:** extend the R4 byte-diff/seam harness with a **text-exactness** assertion (source run text == projected text, character-for-character, per paragraph) and a **numbering-exactness** assertion (computed label == the label Word displays — captured as a golden value per corpus doc).
- **Success is measured, not asserted:** every corpus doc reports text-exact ✅/❌ and numbering-exact ✅/❌; any ❌ is a release blocker.

---

## 6. Related R5 items — folded in vs. kept deferred

| R5 item | Disposition | Rationale |
|---|---|---|
| **G6** transient-mount projection unification | **Folded into R4.5 (WS-1)** | It *is* the "one reader everywhere" foundation; legal read-fidelity is impossible while uploads use mammoth. |
| numbering reconstruction + silent-drop fixes + `paraId→number` (newly surfaced) | **Folded into R4.5 (WS-2/3/4)** | The core of the legal-fidelity thesis. |
| **G3** `setBlockAttr` applier (headings/lists/alignment as tracked **edits**) | **Kept in R5** | Edit-path capability, not read fidelity. But **related**: R4.5 read-numbering and G3 edit-numbering must share the numbering model — R4.5's WS-3 numbering engine is the dependency G3 will build on. Flag the coupling in the spec. |
| **G4** table op, **G5** hyperlink op | **Kept in R5** | Edit-path formatting; not read/reference fidelity. (Reading tables/hyperlinks already works via the projection.) |
| **G1/G2** authored-doc clean lifecycle + clean apply | **Kept in R5** | Save/edit lifecycle, orthogonal to read fidelity. |
| **G7** Save-Version/Save-New UX | **Kept in R5** | Versioning UX. |
| **G8** external-change refresh banner | **Kept in R5** | Concurrency UX. (Adjacent to document integrity but not read fidelity.) |
| **G9** comment scroll-sync | **Kept in R5** | Comments UX. |
| **G10** Document Profile re-run | **Kept in R5 / triage** | Dataverse profiling pipeline; but WS-4's reference layer is what makes profile citations precise — note the dependency. |

---

## 7. Placement Justification (BFF Hygiene — root §10)

- **BFF touched:** `Services/Compose/ComposeDocxProjectionBuilder.cs` (WS-2/3/4), `Api/ComposeEndpoints.cs` (WS-1: upload/project endpoints return a projection), `Services/Compose/ComposeDocxProjection.cs` (WS-4: reference fields). All stays inside `Services/Compose/` — no new top-level surface, no AI-internal injection (ADR-013), `byte[]`-in/projection-out, no `Microsoft.Graph` above `SpeFileStore` (ADR-007). **Extends** the existing projection builder — it is exactly the component that should own docx-read fidelity.
- **Publish-size:** WS-1..WS-4 add no runtime package (pure OOXML computation on the existing `DocumentFormat.OpenXml` dependency) — expect ~0 MB delta. **WS-5 is the exception:** a LibreOffice/rendering pipeline is a *separate process/service*, not a linked package — it must NOT be added to the BFF publish; if pursued it is a sidecar/container with its own size + ops budget (call out in the WS-5 spike).
- **New component justification:** no new *service abstraction* is introduced — WS-1..WS-4 extend `ComposeDocxProjectionBuilder` + `ComposeDocxProjection` (existing). The only genuinely new surface is the **reference map** (WS-4) and the **numbering engine** (WS-3) — both are new *capabilities inside the existing projection component*, justified by concrete failure modes: (a) numbered clauses render "1." repeated (WS-3), (b) the analysis tool cannot cite a section by number (WS-4). WS-5's rendering pipeline is a new component whose cost-of-doing-nothing is "page/line references impossible."

## 8. Hot-Path Declaration (root §10 / bff-extensions §G)

```xml
<hot-path-declaration>
  <bff>YES</bff>                <!-- Services/Compose projection + upload endpoint -->
  <spaarke-ai>YES</spaarke-ai>  <!-- Compose is hosted in sprk_spaarkeai; client mount path (mammoth removal) -->
  <ci-workflows>NO</ci-workflows>
  <skill-directives>NO</skill-directives>
  <root-claude-md>NO</root-claude-md>
</hot-path-declaration>
```

**Coordination:** `Services/Compose/` + `Spaarke.Compose.Components/` overlap prior compose projects and any active SpaarkeAi work — run `/conflict-check` before every PR. Recall the R4 deploy-contention: `sprk_spaarkeai` is a shared dev web resource; coordinate deploys. R4.5 should land on top of R4-on-master (R4 merged to master 2026-07-24 as `a58c0b5cc`).

## 9. ADR Tensions (root §6.5)

| # | ADR / rule | Tension | Proposed path |
|---|---|---|---|
| **T-1** | **NFR-03** (MIT/permissive only; no commercial/AGPL) | Page/line numbering (WS-5) needs a layout engine; the accurate commercial paginators are barred. | **Path A (project exception, documented) + spike:** use LibreOffice (MPL-2.0) as a **separate process** (not linked) or a Word-rendering service; record the licensing analysis in the WS-5 spike. No commercial lib linked into the BFF. |
| **T-2** | **ADR-040 / R4 I-2** (client never authors bytes; browse is client-only, no BFF round-trip) | WS-1 wants browse-local docs to render via the *server* projection, which implies sending the browsed bytes to the server. | **Path A:** a **read-only, stateless** `project` round-trip (bytes → projection, no persistence, no authoring) does not violate "client never authors bytes" (the client still authors nothing; the server only *reads*). Document explicitly; alternative (client-side projection) considered and rejected for breaking F-2 (single auditable reader). |
| **T-3** | R4 §6.5 two-author decision | None — R4.5 does not touch byte authoring. | Note for reviewers: R4.5 is read/reference only; the create/edit split stands. |

## 10. Success criteria

1. **One reader:** `mammoth` has zero Compose call sites; upload/browse/open-in-compose all render via `ComposeDocxProjectionBuilder`; grep-proven.
2. **Text-exact:** every corpus doc passes character-for-character text equality (source runs == projected text); `w:sym`/`w:cr` no longer silently dropped (represented or warned); indentation preserved.
3. **Numbering-exact:** every corpus doc's clause/section/heading/list numbers render **identical to Word** (golden-value harness), including interrupted, multi-level, and style-linked numbering — 100%, no "1." collapse, no dropped heading numbers.
4. **Referenceable:** the projection exposes `paraId → {computed number, level}`; the analysis/citation layer can cite "Section 4.2" and resolve to the exact paragraph; survives edits.
5. **Page/line honest:** WS-5 spike delivers a decision + (if chosen) a pagination pipeline with measured Word-divergence; the product never claims page/line 100% beyond what the engine guarantees.
6. **Hygiene:** BFF build + Compose suite + new fidelity harness green; publish ≤60 MB (WS-1..4 ~0 delta; WS-5 sidecar out-of-publish); Tier-1 NetArch green; no new HIGH CVE; `/conflict-check` clean.

## 11. Open questions for spec authoring

- WS-3 rendering: explicit number-atom (recommended) vs `<ol start>` + custom extension — pick in spec.
- WS-4: persistence store for the `paraId→number` map (with the session ledger? in the projection payload only? both?).
- WS-5: LibreOffice-headless vs Word-rendering-service — the spike decides; does R4.5 *ship* page/line or defer to a fast-follow?
- Does the analysis/citation layer need numbering **ranges** ("Sections 4–7") and **sub-references** ("4.2(b)(iii)") — i.e. how deep does the citation model go? (Informs WS-3 label granularity.)
- Edit-time renumber (insert a clause → downstream numbers shift): confirm this is R5 **G3** territory and that R4.5 guarantees only read-time correctness.

---

## 12. Next step

Run `/design-to-spec` → `/project-pipeline` on this design to produce `spec.md` (with the closed success-criteria set + the ADR-Tensions section formalized) and the task WBS. Suggested phase order: **WS-1 (one reader) → WS-2 (harden read) → WS-3 (numbering) → WS-4 (reference layer) → WS-5 (page/line spike)** — WS-1 is the unblock (kills the mammoth path + delivers alignment/tabs/comments on upload immediately), WS-3 is the flagship, WS-5 is a parallel research spike that can start early since its output is a decision.
