# Spaarke Compose R3 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-16
> **Source**: [`design.md`](design.md) (code-grounded first pass + July-2026 best-practices research + 6 passed pre-spec spikes)
> **Theme**: **Fidelity.** Make the Word round-trip faithful; give Compose a credible editing surface — without recreating Word, on the MIT TipTap base (no TipTap product features, paid or unpaid).
> **Owner**: Ralph Schroeder
> **Supersedes seed**: [`notes/seed-README.md`](notes/seed-README.md) Scope Areas A–J (superseded where this spec differs)
> **Evidence base**: [`ooxml-fidelity-findings.md`](ooxml-fidelity-findings.md) · [`notes/tiptap-docx-fidelity-research-2026-07-16.md`](notes/tiptap-docx-fidelity-research-2026-07-16.md) · [`notes/spikes/`](notes/spikes/) (S1/S1b/S2/S3/S4/S5)

---

## Executive Summary

R1 shipped the Compose editor round-trip (with documented loss); R2 shipped AI actions + native-OOXML track-changes/comments **authoring**. R3 makes the round-trip **faithful**: any edit today rebuilds the whole `.docx` from the editor's simplified view, silently dropping headers/footers, firm styles, multi-level clause numbering, hyperlinks, and embedded objects — even for content the user never touched. R3 inverts the save so the baseline is the **retained original** and edits apply as a **delta** onto it (via the MIT `Docxodus` `WmlComparer`), anchored by stable `w14:paraId` identity. It adds a **grounding-tied confidence** signal to AI suggestions, a **credible editing toolset**, and **import** of pre-existing Word revisions/comments. All six load-bearing assumptions were validated by executable spikes before this spec.

## Scope

### In Scope (D2 — full scope)
- **E1 — Retained-original delta save**: dirty saves apply edits as a delta onto the load-time original; untouched content preserved (byte-identical under Option C). Synthesize the redline in place (`ComposeParagraphRedlineSynthesizer` — Option C; **Docxodus removed** per the NFR-09 §6.5 amendment); drop `docx.js` from export.
- **E2 — `w14:paraId` identity**: server pre-parse stamps/carries paraIds; editor carries them as hidden node attrs; anchors use paraId (fuzzy match retained as cross-Word-session fallback).
- **E3 — Grounding-tied confidence + formatted insertions**: server-derived coarse confidence band (rationale-first); enrich AI-inserted `new_text` to carry run formatting (D4).
- **Editing toolset (Expanded)**: find/replace, basic tables, sticky toolbar, one-line bubble menu, dismissible simplification warning, **styles pane (apply existing styles)**, **richer comment-thread UI**.
- **Import round-trip**: read existing `w:ins`/`w:del` + `w:comment` threads on Load and render them in-editor, preserved across save.

### Out of Scope
- Any **TipTap product feature** (paid or unpaid): TrackChanges, Comments, AI, Collaboration, Import/Export "Conversion", Pages. All built on the MIT base / our code / MIT OSS.
- **New AI dispatch endpoint** (ADR-039) and **new AI catalog rows** — engine frozen; E3 is server-derived, not a new Action output.
- **Recreating Word**: pagination/page layout, footnote/endnote numbering, cross-reference/TOC field computation, print fidelity, full style *management* → deferred to **Open-in-Word** (already shipped). *(Reconciled 2026-07-18: multi-level numbering **authoring** was previously out-of-scope when every doc had a retained original; now that born-in-editor docs are authored server-side (FR-01a), **style-linked multi-level numbering authoring is IN scope** for the renderer — FR-27. Numbering **preservation** on the edit path remains by-construction. Still out: cross-reference/TOC field computation, pagination.)*
- **Multi-user co-editing / CRDT** (R5+).
- ~~**True byte-identity of untouched content**~~ — **now IN scope + delivered** by Option C (§6.5 amendment): the synthesizer rewrites only edited paragraphs, so untouched content is byte-identical. (The retired Approach A / WmlComparer accepted cosmetic re-serialization; Option C is strictly better.)
- R2 deferrals owned elsewhere or explicitly deferred (checkout model, DEF-14 Save-lock, DEF-19 session-scoping) — not R3 unless they block the fidelity flow.

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs` — `SaveAsync` (delta-onto-original), `LoadAsync` (paraId pre-parse + existing-mark projection).
- `src/server/api/Sprk.Bff.Api/Services/Compose/` — NEW: `ParaIdPreParser` (paraId pre-parse) + `ComposeParagraphRedlineSynthesizer` (Option C in-place redline); REUSE: `ComposeParaIdSpliceMap`, `DocxAnnotationWriter.cs`, `DocxAnnotationReader.cs`, `AnnotationReanchorService.cs`.
- `src/server/api/Sprk.Bff.Api/Services/…/SpeFileStore` (facade) — NEW: fetch a driveItem **version's** content by `versionId`.
- `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` — bump `DocumentFormat.OpenXml` 3.4.1 → 3.5.1. (**Docxodus NOT added** — removed per the NFR-09 §6.5 amendment; the synthesizer uses only `DocumentFormat.OpenXml`.)
- `src/client/shared/Spaarke.Compose.Components/src/` — `widgets/ComposeWorkspace.tsx` (`triggerSave` baseline), `widgets/ComposeEditor.tsx` + `types/compose-contracts.ts` (`ComposeDraftPayload` additive fields), `utils/docxBridge.ts` (drop docx.js export; paraId carry), `widgets/hooks/usePendingRedline.ts` (formatted `new_text`), `marks/` (reused), NEW: `@tiptap/extension-unique-id` config, find/replace, styles pane, comment-thread UI.
- `infra/dataverse/` — **none** (engine frozen; no catalog change).

## Requirements

### Track A — E1: Retained-Original Delta Save

> **🔁 AMENDMENT 2026-07-17 (CLAUDE.md §6.5, owner-approved) — redline engine = Option C, not Docxodus.** The NFR-09 gate (task 003) proved Docxodus `WmlComparer` strips `w14:paraId` + drops tables on real templates (6.4.0 AND 7.1.0). E1 now uses **`ComposeParagraphRedlineSynthesizer`** (Option C): word-diff each paraId-keyed edited paragraph → native `w:ins`/`w:del` emitted **in place** on the retained original. This **removes the Docxodus dependency** (tasks 001/020/021 retired) and preserves `w14:paraId` + tables **by construction**. FR-02/03/05/07 below are amended accordingly; see design.md §4 amendment banner + `notes/spikes/S1-nfr09-real-template-hardening-2026-07-17.md`.

> **🔁 AMENDMENT 2026-07-18 (CLAUDE.md §6.5, owner-approved) — the SERVER is the single authority for ALL `.docx` authoring; the client never authors bytes.** Investigation (2026-07-18) surfaced that FR-01…07 all presume a *loaded original* — but a document **born in the editor** (AI-drafted `initialHtml` seed / blank / browse-local, `docxBytes=null`) has **no original** and was being saved by the LOSSY client `docx.js` writer, degrading AI-drafted legal documents at first save. Resolution (validated against Harvey's architecture — deterministic server-side OOXML, LLM confined to text): the server owns all authoring — **`ComposeParagraphRedlineSynthesizer`** deltas onto a retained original (loaded-doc edit), and a **NEW `ComposeDocumentRenderer`** renders a high-fidelity `.docx` from the client content model (born-in-editor). The client is a pure editing surface that sends a **paraId-keyed content model, never `docx.js` bytes**. Adds **FR-01a** (born-in-editor creation) + **FR-27** (authored-document numbering fidelity); scopes FR-01 to docs-with-an-original; splits task 022 → 022 (server, done) + new 026 (renderer) + 027 (client cutover). See design.md §4.4/§8/§11 + `current-task.md` re-architecture note.

1. **FR-01 (baseline inversion)** *(scoped 2026-07-18)*: On a **dirty** save of a document that HAS a load-time original, the persisted document MUST be derived from the **load-time original OOXML**, NOT a TipTap reconstruction. `docx.js` is removed from the export path for edits. (A document with no original is FR-01a, not this rule.) — Acceptance: a save after editing a formatted doc no longer routes through `tipTapJsonToDocxBytes`; a through-the-wire test asserts the saved doc's untouched paragraphs match the original (structurally/semantically).
1a. **FR-01a (born-in-editor / AI-drafted document creation)** *(added 2026-07-18)*: A document with **no retained original** (AI-drafted seed, blank, or browse-local — `docxBytes=null`) MUST be authored **server-side** from the client's paraId-keyed content model via **`ComposeDocumentRenderer`** — NOT by a client `docx.js` reconstruction. The client sends the content model to `create-on-save`; the server renders real Word styles, native tables, inline formatting, style-linked multi-level numbering (FR-27), and mints a `w14:paraId` on every paragraph so the rendered doc is a first-class E2 substrate for the next edit. — Acceptance: a born-in-editor first Save persists server-rendered bytes (no `tipTapToDocxBytes` in the path); the output re-opens in Word, round-trips through the Load import + `ParaIdPreParser`, and a subsequent tracked-change edit does not corrupt it.
2. **FR-02 (edited-paragraph redline, in place)** *(amended)*: The server rewrites **only the edited paragraphs** (keyed by `w14:paraId`) as tracked-change redlines in place on the retained original; untouched paragraphs are not touched. — Acceptance: given an N-paragraph doc with K edited, exactly K paragraphs carry revision markup; the other N−K are byte-untouched.
3. **FR-03 (redline synthesis)** *(amended — was "via Docxodus")*: The server synthesizes minimal `w:ins`/`w:del` per edited paragraph via a word-level diff (`ComposeParagraphRedlineSynthesizer`) of old→new text, with author attribution. **No external diff engine** (Docxodus removed — NFR-09 gate). — Acceptance: 3 edited paragraphs → minimal authored ins/del; every `w14:paraId` and every table (incl. nested) preserved; no exception on real firm templates (per the NFR-09 hardening harness on the real CSA + NDA).
4. **FR-04 (AI redlines reuse)**: AI redlines + comments continue to apply via the existing `DocxAnnotationWriter` (native `w:ins`/`w:del`/`w:comment`) onto the retained-original baseline — unchanged from R2. — Acceptance: an accepted AI redline persists as native OOXML on the faithful baseline.
5. **FR-05 (run-level format fidelity — D4)** *(amended)*: Inline run-formatting edits (bold/italic/font) inside a changed paragraph are represented as a format change (`rPr`/`pPrChange`), not delete+re-insert — handled explicitly by the synthesizer (compare run properties), since the retired WmlComparer's Format-Change Detection is gone. — Acceptance: bolding a word yields an `rPr`/`pPrChange`, not a full-run del+ins.
6. **FR-06 (baseline retrieval — S4)** *(Load-VersionId capture added 2026-07-18)*: The baseline is the **load-time SPE version fetched by `versionId`** (captured at Load). `SpeFileStore` is extended to fetch a specific driveItem version's content (task 002, done). **`LoadAsync` MUST capture + return the load-time `VersionId`** (`LoadComposeDocumentResult.VersionId` + Load-endpoint projection) so the client can send it back — today Load returns only ETag, so this branch has no source (task 022 completion item). Client-retained `state.docxBytes` is a same-session fast-path; a size-capped Redis cache (ADR-009, Tier-3) is the fallback (deferred — the versionId fetch discharges FR-06). — Acceptance: a save after a page refresh (client bytes gone) still applies the delta onto the correct load-time version.
7. **FR-07 (fidelity guarantee — structural/semantic)** *(amended — now byte-identical for untouched content)*: Untouched content — paragraph text, `w14:paraId`, styles, numbering, headers/footers, footnotes, tables (incl. nested) — is preserved across a dirty save. Under Option C untouched paragraphs are **byte-identical** (only edited paragraphs are rewritten), a stronger guarantee than the retired Approach A's cosmetic re-serialization. — Acceptance: the flagship round-trip (Success Criteria) shows all listed structures intact after edit+save+reopen.
27. **FR-27 (authored-document numbering fidelity)** *(added 2026-07-18 — the born-in-editor render path)*: When `ComposeDocumentRenderer` authors a born-in-editor legal document (FR-01a), multi-level numbering MUST be authored as a real **style-linked** OOXML numbering definition — ONE `w:abstractNum` (ilvl 0-8) with `%N` `w:lvlText` cascades (1 / 1.1 / 1.1.1), referenced via the heading/list **styles** (not a direct paragraph `numId` alongside a style-supplied one — that produces double/ghost numbering), `w:isLgl` for Arabic-forced hybrids, and a fresh `w:num` instance + `w:lvlRestart` per restart-scoped list. Numbering MUST be instance-clean at birth (a malformed `numId` or double-numbering stays invisible until a later tracked-change edit corrupts the redline). Numbering *preservation* on the edit path is by-construction (untouched paragraphs are byte-identical, FR-07); numbering *authoring* is in scope only for born-in-editor rendering. — Acceptance: a rendered 1/1.1/1.1.1 clause tree carries a style-linked multi-level `abstractNum` (numbering golden-file), NOT literal "1. " text runs; a subsequent tracked edit does not corrupt the numbering.

### Track B — E2: `w14:paraId` Identity

8. **FR-08 (server pre-parse + minting)**: On Load, a server-side Open XML pre-parse collects each paragraph's `w14:paraId`; paragraphs lacking one get a minted OOXML-valid id (random 32-bit `< 0x80000000`, collision-checked in the part). — Acceptance: every body paragraph (incl. table cells) has a unique paraId after Load.
9. **FR-09 (explicit load-time carry)**: Load sets each paraId as a **hidden ProseMirror node attribute** via an explicit transaction (not left to auto-assign — per S2). — Acceptance: editor doc nodes carry `paraId` immediately after mount; ids not rendered to the DOM.
10. **FR-10 (split/merge minting via UniqueID)**: Configure `@tiptap/extension-unique-id` (3.28.0, MIT) for `paragraph` with `attributeName: 'paraId'` and an OOXML-shaped `generateID`; on split, one half keeps the id, the other is re-minted. **No custom minting plugin.** — Acceptance: splitting a paragraph yields two distinct paraIds, one equal to the original (per S2).
11. **FR-11 (paraId-primary anchoring + fuzzy fallback)**: Annotations/redlines anchor by `paraId` first; `AnnotationReanchorService` (textPattern + Levenshtein + paragraphHint) is retained as the **cross-Word-session** fallback (Word regenerates paraIds on external edits). — Acceptance: an anchor resolves by paraId within our round-trip and by fuzzy match after an external Word edit that changed paraIds.
12. **FR-12 (paraId as splice key)**: On save, paraId is the key that maps edited editor paragraphs to original OOXML paragraphs for the FR-02 splice. — Acceptance: an edit to paragraph P updates exactly the original paragraph with matching paraId.

### Track C — E3: Grounding-Tied Confidence + Formatted Insertions

13. **FR-13 (server-derived confidence band)**: Each AI redline carries a coarse `confidence_band` (`high`/`medium`/`low`) **derived server-side from grounding/verifiability evidence** (is the change supported by a cited source / the retained original?). **No AI catalog Action change** (engine frozen). Additive field on `ComposeDraftPayload` (client mirror + server contract), snake_case, Tier-3-safe. — Acceptance: a grounded suggestion renders `high`; an ungrounded one renders `low`; no catalog row modified.
14. **FR-14 (rationale-first, anti-rubber-stamp)**: The accept/reject surface leads with the cited rationale; the confidence band is secondary. Low-confidence edits are **never** pre-selected or auto-accepted and expose an explicit-review affordance. — Acceptance: "accept all" does not include low-band edits without explicit confirmation.
15. **FR-15 (formatted AI insertions — S5/D4)**: `ComposeDraftPayload.new_text` is enriched to a **formatting-bearing** fragment (or structured runs) and `buildInsertionHtml` emits the marks, so AI-inserted text can carry bold/italic. The mark/range apply layer is unchanged (already slice-safe). — Acceptance: an AI suggestion that includes bold renders bold in the inserted redline.
16. **FR-16 (paraId + offsets on the anchor)**: The redline anchor gains explicit `paraId` + character offsets (rides E2). — Acceptance: a redline round-trips to the exact paragraph + offset.

### Track D — Editing Toolset (Expanded)

17. **FR-17 (find/replace)**: In-editor find and replace (case-sensitivity + replace-all). — Acceptance: find highlights matches; replace-all updates all; interoperates with tracked-changes marks without corrupting them.
18. **FR-18 (basic tables)**: Insert/edit basic tables (MIT `@tiptap/extension-table`); table-cell paragraphs carry paraIds (FR-08/10). — Acceptance: a table edit round-trips fidelity (S1b nested-table result) and cell paraIds are preserved.
19. **FR-19 (sticky toolbar — DEF-16)**: The formatting toolbar stays pinned during scroll. — Acceptance: toolbar visible at any scroll position.
20. **FR-20 (one-line bubble menu — DEF-17)**: The selection bubble menu renders on a single line. — Acceptance: bubble menu is one row at default widths.
21. **FR-21 (dismissible simplification warning — DEF-15)**: The "opened with N simplification(s)" banner is dismissible. — Acceptance: an × closes it; state persists for the session.
22. **FR-22 (styles pane — apply existing styles only)**: A pane lists the document's existing paragraph/character styles and applies them to the selection. **Scope guard**: apply existing styles only — NOT create/rename/manage styles (that is Word-parity, out of scope). — Acceptance: applying a style changes the paragraph's `pStyle`; no style-authoring UI is present.
23. **FR-23 (richer comment-thread UI)**: Comments render as threads with author, timestamp, and replies (feeds import round-trip FR-25). **Scope guard**: view/create/reply/resolve — NOT full Word comment-feature parity. — Acceptance: a comment thread shows author+timestamp and supports a reply; persists as `w:comment` on save.

### Track E — Import Round-Trip

24. **FR-24 (import existing revisions)**: On Load, run the existing `DocxAnnotationReader` to extract pre-existing `w:ins`/`w:del` (regardless of authorship) and render them as first-class tracked changes in the editor. — Acceptance: a doc redlined in Word opens with those revisions visible + accept/reject-able, not flattened.
25. **FR-25 (import existing comments)**: On Load, extract pre-existing `w:comment` threads (author/timestamp/replies) and render them via FR-23. — Acceptance: a Word-commented doc opens with its comment threads intact.
26. **FR-26 (imported anchors survive save)**: Imported revisions/comments anchor by `paraId` (E2) and are preserved across the retained-original save (E1). — Acceptance: open→save→reopen preserves imported revisions/comments.

### Non-Functional Requirements

- **NFR-01 (publish size)**: BFF publish ≤ 60 MB compressed. *(Amended 2026-07-18: Docxodus reversed — Option C (`ComposeParagraphRedlineSynthesizer`) and the new `ComposeDocumentRenderer` are both pure `DocumentFormat.OpenXml` 3.5.1, already referenced → **no new NuGet, ~0 MB package delta**; no SkiaSharp concern.)* Measure the delta on every BFF-touching task vs the current baseline (task 022 Increment A: **46.00 MB** compressed incl PDBs); ≥ +5 MB single-task → justify.
- **NFR-02 (no new HIGH CVE)**: `dotnet list package --vulnerable --include-transitive` clean for the Docxodus + OpenXml 3.5.1 additions.
- **NFR-03 (licensing)**: **No TipTap product features (paid or unpaid); permissive licenses only — no AGPL** (no SuperDoc/ONLYOFFICE/LibreOffice code; patterns only). MIT ex-Pro TipTap extensions allowed after scope verification (`@tiptap/extension-*`, not `@tiptap-pro/*`).
- **NFR-04 (engine frozen / no new dispatch)**: No new AI dispatch endpoint (ADR-039); no new/changed AI catalog rows (E3 is server-derived).
- **NFR-05 (AI facade)**: `Services/Compose/` injects no AI internals (`IOpenAiClient`/executor/routing) — ADR-013 Tier-1 NetArchTest stays green.
- **NFR-06 (E2E Definition-of-Done)**: Every save/load/dispatch change carries a through-the-wire `WebApplicationFactory` slice test (edit-formatted-doc → assert untouched OOXML preserved + edits applied). Unit-green ≠ done. Mock-`HttpMessageHandler`/DI-registration/ctor-null tests do NOT satisfy (ADR-038).
- **NFR-07 (fidelity invariant)**: After a dirty save, untouched paragraphs preserve text + `w14:paraId` + styles + numbering + headers/footers + footnotes + (nested) tables. Re-serialization is cosmetic-only.
- **NFR-08 (performance)**: Save latency for a typical contract (≤ ~50 pp) stays within an interactive budget; measure WmlComparer time on representative fixtures (target < ~2 s server-side; tune if exceeded).
- **NFR-09 (real-template hardening — gate)**: Before build-freeze, re-run the S1/S1b harness on 2–3 **real** firm templates (nested tables, deep numbering, cross-references); a failure gates the delta-save cutover.
- **NFR-10 (Fluent v9 + auth)**: New UI uses Fluent v9 + dark mode (ADR-021) and `@spaarke/auth` for fetches (ADR-028).

## Technical Constraints

### Applicable ADRs
- **ADR-039** (grounded execution / closed catalogs) — no new dispatch endpoint; no new catalog rows.
- **ADR-040** (session ledger) — AI edit payloads remain ledger-first.
- **ADR-013** (AI facade) — Compose injects no AI internals.
- **ADR-007** (Graph isolation) — no `Microsoft.Graph` types above `SpeFileStore` (incl. the new version-content fetch).
- **ADR-005 / ADR-009 / ADR-015** — SPE storage; Redis-first for any baseline cache; Tier-3 isolation for document bytes.
- **ADR-021 / ADR-028** — Fluent v9; `@spaarke/auth`.
- **ADR-038** (testing) — integration-heavy; through-the-wire slice tests; banned mock classes.
- **ADR-029** (publish hygiene) — publish-size discipline.
- **ADR-032** (Null-Object kill-switch) — only if any R3 surface is feature-gated.

### MUST Rules
- ✅ MUST derive dirty-save output from the retained load-time original (FR-01) for docs that HAVE an original; MUST NOT reconstruct the whole doc from TipTap on the client. **For a born-in-editor doc (no original), MUST author server-side via `ComposeDocumentRenderer` (FR-01a) from the client content model — still NOT a client `docx.js` reconstruction.**
- ✅ MUST author all `.docx` bytes **server-side** (delta synthesizer for edits, renderer for creation); the client MUST send a paraId-keyed content model and MUST NOT author bytes (except the clean-save byte-identical passthrough of the retained original).
- ~~MUST exclude SkiaSharp assets when adding Docxodus~~ **OBSOLETE (Docxodus removed — Option C, no external diff engine, no SkiaSharp).**
- ✅ MUST anchor primarily by `paraId` and MUST retain the fuzzy re-anchor as cross-Word-session fallback.
- ✅ MUST derive E3 confidence server-side from grounding; MUST NOT emit a false-precision numeric self-report or auto-accept low-confidence edits.
- ❌ MUST NOT add any TipTap product feature (paid or unpaid) or any AGPL-licensed code.
- ❌ MUST NOT add a new AI dispatch endpoint or new AI catalog rows.

### Existing Patterns to Follow
- Retained-original delta reference: `PushAnnotationsAsync` download→annotate→replace (`ComposeService.cs:1107`).
- Native-OOXML writer/reader: `DocxAnnotationWriter.cs` / `DocxAnnotationReader.cs` (reused).
- Slice-safe redline apply: `usePendingRedline.ts:461-465` (mark-over-range) + `marks/InsertionMark.ts` / `DeletionMark.ts` (S5).
- Atomic If-Match save: `ReplaceFileContentAsUserAsync`.

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR / rule | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| R2 project decision "Save regenerates `.docx` from editor" (2026-07-08) | "Save regenerates from editor (original if unedited)" | That decision **is** the fidelity defect | **B — supersede** | R3 amends it to "delta onto retained original." Documented in design §11; cite in PR. |
| §10 BFF hygiene / ADR-029 (publish-size, CVE) | ≤ 60 MB ceiling; no new HIGH CVE | Docxodus is a new NuGet | **C — comply, measured** | Managed 2.44 MB with SkiaSharp excluded (S3); measure per task; Codeuctivity fork = fallback. |
| ADR-007 / ADR-013 | Graph isolation / AI-facade discipline | New paraId pre-parse + version-content fetch server-side | **C — comply** | Both live behind `Services/Compose` / `SpeFileStore`; no Graph types leak; no AI internals injected. |
| Owner rule 2026-07-14 | No TipTap product features | Track-changes/comments/DOCX-conversion are TipTap's paid modules | **C — comply** | All on the MIT base / our code / MIT OSS (Docxodus, `@tiptap/extension-unique-id`). |
| ADR-039 | One dispatch protocol / closed catalogs | E3 adds a confidence signal | **C — comply** | Server-derived; additive field on `ComposeDraftPayload`; no new endpoint or catalog row. |
| Core charter §3.4 (inherited from R2) | No local MemoryItem variants | `AnchoredAnnotation` gains paraId/offsets | **A — inherited deviation** | Stays positional document-adjacent UI state; never written via `memory.*`. Same argument as R2. |

## Success Criteria

**Flagship graduation gate (G-R3 — browser-verified on spaarkedev1, one run):**
1. [ ] Open a **formatted contract** (letterhead header/footer, multi-level clause numbering, custom styles, a table) in Compose. — Verify: renders with structure intact.
2. [ ] Edit **3 clauses** (incl. one bold/italic run change) and **accept one AI redline**. — Verify: edits show as tracked changes; the AI redline shows rationale + a grounding-tied confidence band.
3. [ ] Exercise the **toolset**: a **find/replace** and a **table edit** in the same session. — Verify: both work; tracked-changes marks intact.
4. [ ] **Save**, then **reopen**. — Verify (the core): header/footer/numbering/styles/table **intact**; the 3 edits + accepted redline present as tracked changes; each redline anchored to the correct paragraph by `paraId`.

**Supporting criteria:**
5. [ ] A doc that **already has Word revisions/comments** opens with them rendered in-editor and preserved across save. — Verify: import round-trip (FR-24/25/26).
6. [ ] BFF publish ≤ 60 MB with Docxodus + SkiaSharp-excluded; no new HIGH CVE. — Verify: publish measurement + `dotnet list package --vulnerable`.
7. [ ] ADR-013 NetArchTest green; through-the-wire slice test proves untouched OOXML preserved on a dirty save. — Verify: CI + the FR-01 slice test.
8. [ ] Real-template hardening (NFR-09) passed before the delta-save cutover. — Verify: S1/S1b re-run report on real firm docs.

## Dependencies

### Prerequisites
- R1 + R2 merged to master (Compose service/layout/endpoints, native-OOXML writer/reader, slice-safe redline marks) — ✅ present.
- `.NET 10` SDK (Docxodus target) — ✅ present in the build environment (verified S1).

### External Dependencies
- **`Docxodus` 7.1.0** (NuGet, MIT) — the redline engine (SkiaSharp excluded).
- **`@tiptap/extension-unique-id` 3.28.0** (npm, MIT) — paraId carry/minting.
- **SPE driveItem version-content fetch** (Graph capability) — a small `SpeFileStore` addition (FR-06).

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| E1 approach | How does a dirty save apply edits onto the original? | **Hybrid** (D1) — retained-original + Docxodus redline + existing writer | Tracks A/B; FR-01..07 |
| R3 scope | What's in R3 vs fast-follow? | **Everything** (D2) — fidelity core + E3 + toolset + import | All tracks in scope |
| Confidence | How is the E3 band produced? | **Server-derived from grounding** | FR-13; no catalog change (NFR-04) |
| Edit fidelity | How deep inside a changed paragraph? | **Text + run-level formatting** (D4) | FR-05, FR-15 |
| Toolset boundary | Exact toolset for R3? | **Expanded** — §7.5 set + styles pane + richer comment threads | FR-17..23 (with scope guards on FR-22/23) |
| Graduation gate | Single flagship end-to-end? | **Fidelity round-trip + toolset demo** (find/replace + table edit) | Success Criteria G-R3 |

## Assumptions

*Proceeding with these unless the owner corrects them:*
- **Styles pane (FR-22)**: "Expanded" = **apply existing document styles** to a selection; NOT create/rename/manage styles (that is Word-parity, explicitly out of scope). If full style management is wanted, it is a separate scope decision.
- **Comment-thread UI (FR-23)**: view/create/reply/resolve; NOT full Word comment-feature parity.
- **E3 grounding evidence**: the confidence band is derived from existing grounding signals (cited sources / retained-original support / re-anchor score) — no new AI call. If no grounding signal exists for an action, the band is `low`/omitted rather than guessed.
- **Byte-identity**: MVP is Approach A (WmlComparer re-serialized output). Approach B (byte-identity splice-back) is deferred; assumed no Compose source docs are OOXML-digitally-signed during drafting.
- **Import comment depth**: threads render author/timestamp/replies as read+reply; nested reply chains beyond one level render flat if the source lacks the modern-comments 4-part structure.

## Unresolved Questions

*None blocking. Carried into `plan.md`/tasks as build-time confirmations (not design decisions):*
- [ ] Confirm `SpeFileStore` can fetch a specific driveItem **version's** content (FR-06) — small addition; validate the Graph route early. — Blocks: FR-06 baseline retrieval if the capability is missing (fallback = Redis cache of load-time original).
- [ ] Tune the E3 grounding→band thresholds on real suggestions (FR-13) — starting default, refined during implementation. — Blocks: nothing (defaults ship).
- [ ] Re-run S1b on 2–3 real firm templates (NFR-09) before the delta-save cutover. — Blocks: the E1 cutover only.

---
*AI-optimized specification. Original design: [`design.md`](design.md). All work on the MIT TipTap base — no TipTap product features; permissive licenses only (no AGPL).*
