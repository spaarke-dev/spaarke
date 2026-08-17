# Compose Read & Reference Fidelity

> **Purpose**: End-to-end reference for how Compose *reads* a `.docx` into the editor with legal-grade fidelity and makes every clause *referenceable*. Covers the single server projection (one reader), verbatim text + warn-don't-drop, the deterministic numbering engine, the `paraId → legal-number` reference layer + `CitationResolver`, and the honest page/line position. This is the **read/reference** companion to the **write/save** side ([ADR-049](../../.claude/adr/ADR-049-compose-shadow-document.md)); the editor-UX layer above both (save/save-as, draft-safe autosave, hotkeys, PDF import, save-identity) is [COMPOSE-EDITOR-UX.md](COMPOSE-EDITOR-UX.md).
>
> **Last reviewed**: 2026-07-28 (project `spaarkeai-compose-fidelity-r4.5`, merged to master; deployed to dev `spaarke-bff-dev` + `sprk_spaarkeai`). Establishes read-side invariants F-1…F-5 alongside R4's write invariants I-1…I-7.
>
> **Scope boundary**: R4.5 is about *reading* a legal document with perfect fidelity and making it *referenceable*. *Editing* it with full formatting fidelity (live renumber-on-edit, table/hyperlink ops, clean apply) is R5. Byte-authoring on save is R4 ([ADR-049](../../.claude/adr/ADR-049-compose-shadow-document.md)); the two-author (create/edit) split is untouched here.

---

## 1. Big picture

For a **legal** document platform, four things about *reading* a contract are non-negotiable:

1. **Text is exact** — no introduced, dropped, or altered characters.
2. **Numbering is 100% correct** — "Section 4.2(b)" renders exactly as Word shows it, on every entry path.
3. **Every paragraph is stably referenceable** — the analysis/citation layer can say "per Section 4.2" and resolve it to the exact place, surviving edits.
4. **Page/line references are honest** — delivered where technically possible, explicitly scoped where they are not derivable from the file.

R4 built the machinery (the server-side projection, a `paraId`-anchored high-fidelity reader) but wired it into only **one** doorway (stored-doc Load), left the lossy client `mammoth` reader in place for uploads/browse, and never reconstructed or stored the displayed numbering or a `paraId → legal-number` reference. **R4.5 closed those gaps** — the five read-side invariants:

| # | Invariant | One line |
|---|---|---|
| **F-1** | Text exactness | Run text emitted verbatim, character-for-character; any unrepresentable construct is **warned, never silently dropped**. |
| **F-2** | One reader | Exactly one docx→editor reader (the server projection); every entry path renders through it; the client `mammoth` fallback is deleted. |
| **F-3** | Deterministic numbering | Displayed clause/section/heading/list numbers computed server-side from the OOXML numbering model, identical to Word. |
| **F-4** | Stable reference | Every paragraph carries `paraId` **and** its computed legal number + level, persisted so citations survive edits. |
| **F-5** | Honest layout numbering | Page/line numbers are rendering artifacts — delivered only via an explicit pagination engine where in scope, never fabricated from OOXML. |

Everything extends the R4 projection component (`ComposeDocxProjectionBuilder` / `ComposeDocxProjection`) — **pure OOXML computation on the existing `DocumentFormat.OpenXml` dependency, zero new runtime package**, ADR-007/013 purity intact.

---

## 2. One reader — entry paths + the projection (F-2)

The **server projection** is the sole docx→editor mapper. Every entry path resolves to a `ComposeServerProjection` (paraId-tagged HTML + fail-closed status + warnings) and mounts the *projection* branch of `ComposeEditor.tsx`:

| Entry path | How the bytes reach the reader | Endpoint |
|---|---|---|
| **Stored-doc Load** | SPE bytes → projection (R4) | `GET /api/compose/documents/{id}` (`Load`) |
| **Upload / open-in-Compose** (transient) | retained bytes from `ITenantCache` → projection | `POST /api/compose/upload` (`ProjectDocument`) |
| **Browse-local `.docx`** | client sends the browsed bytes → projection, **no persist** | `POST /api/compose/project` (`Project`) |

The client **`mammoth` fallback + `docxToTipTapHtml` are deleted** from Compose (`docxBridge.ts` keeps only the `paraId` utilities). `mammoth` remains a repo dependency for SprkChat/Notepad — only the Compose usage was removed.

- **Browse round-trip (Tension T-2, Path A)**: `POST /api/compose/project` is **read-only, stateless** — it injects no `ITenantCache`/`ISpeFileOperations`/Dataverse type, so persistence is *structurally impossible*. This does not violate ADR-040 / R4 I-2 ("client authors no bytes"): the client still authors nothing; the server only *reads* and hands back a render. **R6 refresh (task 027, 2026-08-06)**: the door remains STATELESS, but since the render-on-save cutover it is no longer purely read-only in the narrow sense — it paraId-MINTS the caller's bytes in-memory (the ingest fill-gaps stamp, so the HTML projection and the canonical `contentModel` it now also returns agree on ids) and, ONLY when minting mutated the bytes, echoes the minted copy back (`content`) for the client to adopt as its retained mount baseline. Nothing is persisted server-side; the echo is response payload. It also returns `contentModelWarnings` (the canonical projection's flatten warnings, folded by the client into the first model-path save's degradation banner).
- **Null/unreachable projection**: after mammoth removal, a `projection: null` mount (e.g. BFF unreachable on browse) renders an explicit **"couldn't prepare … for editing"** error state — never a silent blank editor and never a second reader.
- **One-reader proof**: seam tests assert the upload and browse endpoints return **byte-identical projection HTML** to the Load path for the same bytes.

---

## 3. Text exactness (F-1)

The projection emits run text **verbatim** — the only permitted transform is lossless HTML-structural encoding (`&`/`<`/`>` via `AppendEscaped`). No trimming, collapsing, or smart-quote rewriting. Any construct that cannot be represented is surfaced as a **warning** (`ComposeProjectionWarning {Code, Count}`), never silently dropped.

R4.5 fixed the silent drops the R4 run-switch missed and added an intra-run warning mechanism:

| Construct | Handling |
|---|---|
| `w:cr` (carriage return) | → `<br>` |
| `w:sym` (symbol glyph) | mapped to Unicode where known (Symbol `0xF0A7` → `§`); **unmapped → visible placeholder + warning** (never fabricate — a wrong legal glyph is worse than a warned placeholder) |
| `w:ind` (indentation) | emitted as `margin-left` / `text-indent` (pt via twips/20; hanging wins per ECMA-376) |
| `w:pict` / `w:ptab` / footnote+endnote refs / `w:ruby` / page+column breaks | represented or warned (see the WS-2 construct audit) |
| preserved whitespace | `white-space: pre-wrap` on the editor surface |

Result: all corpus documents are **character-for-character text-exact**, verified by the read-fidelity harness (a release-blocker assertion).

---

## 4. Deterministic numbering (F-3) — the engine

The displayed number is a **computation over the file's numbering model**, not stored text — Word's algorithm reproduced exactly, server-side, in the projection's single document-order walk (`NumberingComputationEngine` in `ComposeDocxProjectionBuilder`):

1. **Model reader** parses `numbering.xml` (`w:num` → `abstractNumId` → `abstractNum`, per-level `w:numFmt` / `w:lvlText` / `w:start` / `w:lvlRestart` / `w:isLgl` / `w:lvlOverride`/`w:startOverride`) **plus style-linked numbering** (a paragraph *style* — e.g. `Heading2` — carrying the `w:numPr`, resolved via `pStyle` + `w:basedOn` ancestry).
2. **Counter** keyed **`(numId, level)`** — instance-scoped per ECMA-376. This is what distinguishes **restart** (a fresh `w:num` + `w:startOverride=1` — two lists separated by prose) from **continue** (the same `numId` resumed after a heading/table interruption). *Keying by `abstractNumId` alone was a real bug (DEF-03), caught by the write↔read round-trip test.*
3. **Formatters**: decimal / lowerLetter (a…z, aa) / upperLetter / lowerRoman / upperRoman / bullet; `w:lvlText` template substitution (`%1.%2.%3` → "4.2.1"); `w:isLgl` forces decimal.
4. **Emit**: the computed label is attached to the paragraph and rendered as an **explicit non-editable number-atom** — a ProseMirror **widget decoration** (`composeNumberAtomExtension.ts`), *not* a doc node. It is structurally impossible to select/type into, never appears in `getJSON()`, and cannot shift text offsets. The editor never relies on the browser `<ol>` auto-count for a legal number (the native marker is suppressed with `list-style: none`).

**Read-time only**: the number is a display of the source. Live renumber-on-insert/delete (reflected in redline) is **R5 G3**; WS-3's engine is the shared model G3 will build on. The read side agrees with the write side — a round-trip test authors via `ComposeDocumentRenderer` → reads via the engine → identical labels.

---

## 5. Reference & citation layer (F-4)

Per-paragraph reference data lives on `ComposeDocxProjection.ParaIdMap[]` (`ParaIdMapEntry`):

```
ParaId · Index (doc-order) · ComputedNumber ("4.2") · NumberingLevel (ilvl) · ListPath ([4,2]) · HeadingLevel
```

- **Persisted both** in the projection payload **and** the document session ledger — the map is written into the existing `ChatSession` (Redis hot) + `StoredSession` (Cosmos warm) stack, following the `AnchoredAnnotations`/`DefinedTermsTracking`/`ActiveDocument` precedent (no new store). It survives edits: unchanged paragraphs keep their stable `paraId → number`; new/split paragraphs re-anchor per R4.
- **`CitationResolver`** (`Services/Compose/CitationResolver.cs`, pure static, never throws) maps a human citation ↔ the exact `paraId`(s), against `ComputedNumber`/`ListPath`:
  - single label — "Section 4.2" / "§ 4.2" → `[4,2]`
  - sub-item depth — "4.2(b)(iii)" → `[4,2,2,3]` (letter/roman token parse)
  - contiguous range — "Sections 4–7" → all paragraphs with top-level ordinal 4..7, in document order
  - reverse — `paraId` → its canonical number

  An unresolvable citation returns **empty matches** — an explicit not-found, never a fabricated `paraId`.

This is the most valuable output for the analysis product: a citation that resolves to the exact clause and renders exactly as the source. **Consumer wiring** (e.g. review-note citations) is where this pays off — the first consumer is the Compose review-note location label (`ndaClauseLocation.ts`, which now reads `computedNumber`); broader wiring continues in `ai-advanced-capabilities-agreements-r1`.

---

## 6. Page/line — honest, and deferred (F-5)

Page and line numbers **are not in the `.docx`** — they are computed at layout/render time (page size, margins, fonts, image/table flow). `w:lnNumType` only turns line-number *display* on; the content→line mapping is still rendered. Therefore:

- Paragraph/clause/section numbering → **100% guaranteed** (F-3, deterministic from the file).
- Page/line → requires a **Word-compatible layout engine**, and "100% identical to Word" is guaranteed only by Word's own layout. The reachable ceiling is **"Word-Online-identical"** (Graph `format=pdf`) or **"close-but-diverges"** (LibreOffice sidecar — measured ~21% page-break shift on the corpus). Self-run desktop/headless Word is barred server-side (KB257757), independent of licensing.
- **NFR-03**: permissive-only for anything linked into the BFF; LibreOffice (MPL-2.0) permitted only as a **separate process/sidecar**, out of the BFF publish.

**Decision (WS-5): deferred** — R4.5 ships the honest scoping + the engine analysis + the licensing path; **no pagination engine is built**, so the product makes no page/line "100%" claim. Implementation is a possible fast-follow. Two licensing items (AGPL-as-sidecar; Syncfusion Community "free ≠ permissive") require human sign-off at fast-follow time. See `projects/spaarkeai-compose-fidelity-r4.5/notes/ws5-pagination-decision.md`.

---

## 7. BFF surface (read-relevant endpoints)

All under the authenticated `/api/compose` group (`RequireAuthorization`, ADR-008):

| Endpoint | Purpose | Notes |
|---|---|---|
| `GET /api/compose/documents/{id}` | stored-doc Load → projection | R4 |
| `POST /api/compose/upload` | retained-bytes → projection (transient mount) | returns projection **alongside** `Content` bytes |
| `POST /api/compose/project` | **stateless** bytes → projection + canonical `contentModel` (+ minted-bytes echo when ids were minted) (browse) | R4.5 (T-2); R6 task 027 refresh — no persist; in-memory paraId mint + echo only |

`ComposeProjectionResponse` mapping is shared (`MapProjectionResponse`) across Load/upload/project — one wire shape (F-2). Publish delta for the whole read/reference feature: **~0 MB** (pure OOXML; no package).

---

## 8. Where to start reading code

| Concern | Start here |
|---|---|
| Projection + numbering engine + construct handling | `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs` (`NumberingComputationEngine`, `BuildNumberingModel`, `RenderRun`, `AppendNumberingAttrs`) |
| Projection payload + reference fields | `Services/Compose/ComposeDocxProjection.cs` (`ParaIdMapEntry`) |
| Citation resolver | `Services/Compose/CitationResolver.cs` |
| Endpoints (upload/project) | `Api/ComposeEndpoints.cs` (`Upload`, `Project`, `MapProjectionResponse`) |
| Session-ledger persistence | `Services/Compose/ComposeService.cs` (`BuildReferenceMap`) + `Models/Ai/Chat/ChatSession.cs` (`ReferenceMap`) |
| Number-atom render | `src/client/shared/Spaarke.Compose.Components/src/widgets/composeNumberAtomExtension.ts` |
| Indentation render | `.../composeIndentExtension.ts` |
| One-reader mount + error state | `.../ComposeEditor.tsx` (projection branch, `projectionUnavailable`), `.../ComposeWorkspace.tsx` (upload/browse effects) |
| Review-note citation label | `.../ndaClauseLocation.ts` (`deriveClauseLocationLabel` reads `computedNumber`) |
| Fidelity harness (acceptance) | `tests/integration/seam/Compose/ComposeReadFidelityHarnessSeamTests.cs` + corpus `tests/fixtures/compose-corpus/` |

---

## 9. Extension recipes

- **Consume citations in a new surface**: call `CitationResolver.Resolve(citationString, projection.ParaIdMap)` (or the session-ledger overload) — returns matched `paraId`(s). For a UI location label, read the clause paragraph node's `computedNumber` attribute directly (see `ndaClauseLocation.ts`). No endpoint/DI needed — the map is passed as data.
- **Add a symbol-font glyph mapping**: extend `KnownSymbolGlyphMap` in `ComposeDocxProjectionBuilder` with a *corpus-verified* code point → Unicode entry. Never add an algorithmic guess — unmapped stays warned-placeholder.
- **Handle a new OOXML construct**: add a `case` in the run/block switch that either represents it faithfully or raises a warning via the `BuildContext.AddWarning` mechanism, and add a projection test. Never leave a silent drop (F-1). Update the WS-2 construct audit.

---

## 10. ADR + constraint pointers

- [ADR-049 — Compose Shadow Document](../../.claude/adr/ADR-049-compose-shadow-document.md) — the canonical Compose ADR; write invariants I-1..I-7 + the R4.5 read/reference companion (F-1..F-5).
- ADR-040 / R4 I-2 — client authors no bytes (browse via read-only `project`, Tension T-2 Path A).
- ADR-013 / ADR-007 — `Services/Compose/` purity: no AI-internal types, no `Microsoft.Graph` above `SpeFileStore`, `byte[]`-in/projection-out.
- ADR-038 — read-fidelity harness = seam/KEEP-path vertical slice; no `Mock<HttpMessageHandler>`/DI/ctor tests.
- Root `CLAUDE.md` §10 (BFF Hygiene — ≤60 MB publish) / §11 (default-to-reuse).

---

## 11. Related docs

- [`.claude/adr/ADR-049-compose-shadow-document.md`](../../.claude/adr/ADR-049-compose-shadow-document.md) — write/save + read/reference invariants.
- `projects/spaarkeai-compose-r4/` — the write/save side (Shadow Document Architecture).
- `projects/spaarkeai-compose-fidelity-r4.5/` — this read/reference project: `design.md`, `spec.md`, and `notes/` (WS-1..WS-5, incl. the numbering-engine, citation-resolver, and WS-5 pagination-decision notes).
- `projects/spaarkeai-compose-r5/` — editing completeness (deferred): live renumber-on-edit (G3), table/hyperlink ops, clean apply.
- `projects/ai-advanced-capabilities-agreements-r1/notes/HANDOFF-from-compose-fidelity-r4.5.md` — consumer-side follow-ons (review-note citations; advisory-comment placement DEF-01; `ndaClauseLocation` generalization).
