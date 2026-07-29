# WS-2 Construct Audit — Full OOXML Run/Block Construct Set (Task 022, FR-09)

> Written by the task 022 sub-agent execution. Sub-agent write boundary: this file (under
> `projects/spaarkeai-compose-fidelity-r4.5/notes/`) is in-bounds; `TASK-INDEX.md` / `current-task.md` are NOT
> touched here — owned by the main session.

## Purpose

FR-09 requires a full OOXML run/block construct audit against `ComposeDocxProjectionBuilder.cs`: enumerate the
constructs the projection encounters, confirm each is either **represented** (rendered faithfully),
**warned** (surfaced via `BuildContext.AddWarning`, never silently dropped), or **deliberately-dropped-with-reason**
(carries no recoverable text/content — dropping it loses nothing a reader would notice). Per the project's F-1
invariant, a construct that is **silently dropped** (no HTML, no warning, no trace) is a defect, not an
acceptable gap.

This audit re-greps the builder as of task 022 (after 020's `w:cr`/`w:sym` fix and 021's `w:ind` emit), goes
beyond the design §4 example list, and enumerates the full ECMA-376 `CT_R` (run content) vocabulary plus the
block-level constructs the builder's `RenderBlockChildren`/paragraph-property switches touch. **Five additional
silent drops were found and fixed in this task** (see §3); all other constructs were already compliant or are
recorded here as deliberate, documented exclusions.

## 1. Run-child constructs (inside `w:r`)

| Construct (`w:*`) | .NET type | Disposition | Evidence (file:line) | Test-covered? |
|---|---|---|---|---|
| `w:t` | `Text` | **Represented** — verbatim via `AppendEscaped` | `RenderRun` `:727-729` | Yes — every existing test |
| `w:delText` | `DeletedText` | **Represented** — verbatim, plain text (F-02 flattening: deletion wrappers stripped, content kept for overlay anchoring) | `RenderRun` `:730-732` | Yes — `Build_InsertedAndDeletedRuns_...` |
| `w:tab` | `TabChar` | **Represented** — non-collapsing `<span class="compose-tab">` placeholder (approximation: fixed 1-space width, not a computed tab-stop position) | `RenderRun` `:733,736-741` | Yes — corpus (`Engagement Letter.docx` uses `w:br`, not tabs; direct coverage via existing offset-table tests) |
| `w:ptab` (positional/custom tab, TOC-style leaders) | `PositionalTab` | **FIXED this task — was SILENTLY DROPPED** (fell to `default`, zero HTML, zero offset length, no warning). Now represented identically to `w:tab` (same compose-tab simplification) | `RenderRun` `:734,736-741` | Yes (new) — `Build_ParagraphWithPositionalTab_RendersComposeTabSpanLikeRegularTab` |
| `w:br` (default / `type="textWrapping"`) | `Break` | **Represented** — `<br>`, no warning (correct default disposition) | `RenderRun` `:738,744-747` | Yes (new negative case) — `Build_ParagraphWithDefaultTextWrappingBreak_RendersLineBreakWithNoWarning`; corpus (`Engagement Letter.docx` row 2 letterhead block) |
| `w:br type="page"` | `Break` (`Type=Page`) | **FIXED this task — was represented-with-UNDOCUMENTED fidelity downgrade** (rendered as `<br>` with no signal that a hard page break became a soft line break). Now still `<br>` (this editor has no pagination concept — F-5/WS-5 deferred) **+ warned** (`page-break-rendered-as-line-break`) so the downgrade is auditable | `RenderRun` `:738-746` | Yes (new) — `Build_ParagraphWithPageOrColumnBreak_...[InlineData("page", ...)]` |
| `w:br type="column"` | `Break` (`Type=Column`) | Same treatment as page break — `<br>` + `column-break-rendered-as-line-break` warning | `RenderRun` `:738-746` | Yes (new) — `Build_ParagraphWithPageOrColumnBreak_...[InlineData("column", ...)]` |
| `w:cr` | `CarriageReturn` | **Represented** — `<br>`, mirrors `w:br` (fixed task 020, FR-05) | `RenderRun` `:748-754` | Yes — `Build_ParagraphWithCarriageReturnRun_RendersBreakLikeExistingWBr` |
| `w:noBreakHyphen` | `NoBreakHyphen` | **Represented** — U+2011 non-breaking hyphen | `RenderRun` `:755-757` | Corpus-covered implicitly (no dedicated unit test; pre-existing, out of this task's scope — see §4) |
| `w:sym` (mapped font/char) | `SymbolChar` | **Represented** — verified Unicode mapping (`KnownSymbolGlyphMap`), hand-curated, deliberately not algorithmic (fixed task 020, FR-06) | `RenderRun` `:758-767`; `ResolveSymbolGlyph` `:825-843` | Yes — `Build_ParagraphWithSymbolCharRun_MappedSymbolFont_...`; corpus `symbol-section-mark.docx` |
| `w:sym` (unmapped font/char) | `SymbolChar` | **Warned + placeholder** — U+FFFD replacement char + `unmapped-symbol-char` warning (fixed task 020, FR-06/FR-10) | `RenderRun` `:758-767` | Yes — `Build_ParagraphWithSymbolCharRun_UnmappedSymbolFont_...` |
| `w:ruby` (rubyBase — base prose) | `Ruby` → `RubyBase` | **FIXED this task — was SILENTLY DROPPED** (fell to `default`; base text is real, 100%-recoverable document prose, not a construct requiring guessing like `w:sym`). Now rendered verbatim via `AppendEscaped(ExtractRunsDisplayText(RubyBaseRuns(ruby)))` | `RenderRun` `:768-779`; `RubyBaseRuns` `:648-652` | Yes (new) — `Build_ParagraphWithRubyAnnotation_RendersBaseTextVerbatim_...` |
| `w:ruby` (rt — phonetic guide) | `Ruby` → `RubyContent` | **Deliberately dropped + warned** — a supplementary pronunciation annotation, not the document's own words; the simplification is surfaced via `ruby-phonetic-guide-dropped` so it stays auditable (never silent) | `RenderRun` `:768-779` | Yes (new) — same test asserts the guide text is absent |
| `w:pict` (VML legacy picture fallback) | `Picture` | **FIXED this task — was SILENTLY DROPPED** (not checked by `IsComplexObjectRun`; fell to `default` in `RenderRun`, zero HTML, zero offset length, no warning — the exact defect class the file's own `IsComplexObjectRun` doc comment flagged as "noted rather than silently unhandled" but did not actually implement). Now a non-editable `ComposeAtomKind.ComplexObject` atom, identical treatment to `w:drawing`/`w:object` | `IsComplexObjectRun` `:524-533`; atom emit via `RenderInline`'s `Run` case (`IsComplexObjectRun` check) | Yes (new) — `Build_RunWithVmlPictureFallback_BecomesComplexObjectAtom_...` |
| `w:drawing` (DrawingML image/shape) | `Drawing` | **Represented** — `ComposeAtomKind.ComplexObject` atom (task 012, FR-02) | `IsComplexObjectRun` `:524-533` | Yes — `Build_DrawingRunOutsideTextBox_BecomesComplexObjectAtom_...` |
| `w:object` (OLE embed) | `EmbeddedObject` | **Represented** — `ComposeAtomKind.ComplexObject` atom (task 012, FR-02) | `IsComplexObjectRun` `:524-533` | Covered by the same `IsComplexObjectRun` check as `w:drawing`; no dedicated corpus/unit fixture (pre-existing, out of this task's scope) |
| `w:fldChar` begin/separate/end sequence | `FieldChar` (via `Run`) | **Represented** — one `ComposeAtomKind.Field` atom, cached RESULT text only, `instrText` (field code) never shown (task 012, FR-02) | `TryAdvanceFieldScan` `:472-516`; emit at `RenderInline` `:664` region | Yes — `Build_FldCharFieldSequence_EmitsOneAtomWithCachedResultOnly_...` |
| `w:fldSimple` | `SimpleField` | **Represented** — `ComposeAtomKind.Field` atom, cached display value | `RenderInline`'s `SimpleField` case | Yes — `Build_SimpleField_BecomesAtomCarryingCachedDisplayValue` |
| `w:instrText` / `w:delInstrText` (field code, inside a recognized field scan) | `FieldCode` / `DeletedFieldCode` | **Deliberately dropped, correct** — field CODE text is never editor-visible in Word either; swallowed by `TryAdvanceFieldScan`'s `Phase == Code` branch, never emitted | `TryAdvanceFieldScan` `:505-513` | Yes — `Build_FldCharFieldSequence_..." asserts "PAGE" instrText never appears |
| `w:instrText` / `w:delInstrText` (standalone, OUTSIDE any recognized `w:fldChar` begin/end scan — malformed/edge doc) | `FieldCode` / `DeletedFieldCode` | **Known gap, not fixed this task** — falls to `default` in `RenderRun` (silently dropped: no case for `FieldCode`/`DeletedFieldCode` types themselves). Extremely low probability (a standalone instrText outside a field-scan sequence is malformed OOXML); documented simplification consistent with `FieldScanState`'s own remarks ("not exercised by the corpus, documented simplification"). Not present in the corpus; deferred rather than blind-fixed without a verifying fixture | `RenderRun` `default:` `:792-793` | No — flagged, not escalated (see §5) |
| `w:footnoteReference` | `FootnoteReference` | **FIXED this task — was SILENTLY DROPPED** (fell to `default`, zero HTML, zero offset length, no warning). Now warn-only (`unrepresented-footnote-reference`) — the mark carries no independent text (Word computes its number from position in `word/footnotes.xml`, a part this body-only projection does not open), and fabricating a number risks the exact "wrong glyph in a legal document" failure task 020's escalation reasoning warns against | `RenderRun` `:780-788` | Yes (new) — `Build_ParagraphWithFootnoteReference_RaisesWarning_...` |
| `w:endnoteReference` | `EndnoteReference` | Same treatment as footnote reference — warn-only (`unrepresented-endnote-reference`) | `RenderRun` `:789-791` | Yes (new) — `Build_ParagraphWithEndnoteReference_RaisesWarning_...` |
| `w:commentReference` | `CommentReference` | **Deliberately delegated, correct** — carries no text of its own (a zero-width anchor for Word's comment-balloon indicator); comments themselves (`w:comment`, `w:commentRangeStart`/`End`) are read by the SEPARATE, purpose-built `DocxAnnotationReader.cs` (the project's "re-anchor layer" per project `CLAUDE.md`), not this builder. Not a construct-level silent drop of TEXT | `DocxAnnotationReader.cs` `:110-183` (parallel system) | N/A — covered by `DocxAnnotationReader`'s own test surface, out of this file's scope |
| `w:annotationRef` (comment-number marker inside commented range) | `AnnotationReferenceMark` | Same disposition class as `w:commentReference` — zero-width, no independent text, delegated to the comment system | N/A (not present in `ComposeDocxProjectionBuilder.cs`) | N/A |
| `w:footnoteRef` / `w:endnoteRef` (the reference MARK rendered inside the footnote/endnote's OWN body) | `FootnoteReferenceMark` / `EndnoteReferenceMark` | **Out of scope — architectural boundary, not a switch gap.** These live only inside `word/footnotes.xml`/`word/endnotes.xml`, parts this Phase-1 projection never opens (main document body only — the same boundary that excludes headers/footers). Not a run-child construct this builder's switches ever see | N/A | N/A |
| `w:separator` / `w:continuationSeparator` (footnote/endnote separator marks) | `SeparatorMark` / `ContinuationSeparatorMark` | Same architectural-boundary disposition — footnote/endnote-part-only, never opened | N/A | N/A |
| `w:softHyphen` | `SoftHyphen` | **Deliberately dropped, correct** (confirmed, unchanged) — an optional hyphenation break point; dropping it changes zero rendered characters (the word displays identically with or without it); this is the design.md-documented "correct" disposition | `RenderRun` `default:` `:792-793` (falls through, by design) | Corpus-implicit (no dedicated unit test needed — absence of effect is the point) |
| `w:lastRenderedPageBreak` | `LastRenderedPageBreak` | **Deliberately dropped, correct** — a pure rendering-cache hint Word writes to remember where it last paginated; carries zero content/semantic meaning to re-derive, explicitly "don't reinterpret" per its own OOXML purpose | `RenderRun` `default:` `:792-793` (falls through, by design) | Corpus-exercised — `Engagement Letter.docx` (corpus-manifest.md row 2) carries this hint; harness's 8/8 text-exactness confirms no character is affected |
| `w:dayShort`/`w:monthShort`/`w:yearShort`/`w:dayLong`/`w:monthLong`/`w:yearLong` (deprecated Word 6.0/95-compat date/time fields) | (legacy types) | **Out of scope — legacy/deprecated, not producible by modern Word (2007+).** No modern `.docx` (the corpus's exclusive format) can author these; falls to `default` if ever encountered, but this is a theoretical-only gap | N/A | N/A |
| `w:pgNum` (deprecated legacy simple page-number field, pre-`fldChar`) | `PageNumber` | **Out of scope — legacy/deprecated.** Modern Word uses the `w:fldChar`/`w:instrText` `PAGE` field instead (already represented, see corpus row 1's footer page-number SDT); this legacy element is a theoretical-only gap | N/A | N/A |
| `w:contentPart` (reference to an external glossary/content part) | `ContentPart` | **Out of scope — not applicable to Spaarke's document population.** Extremely rare, no corpus evidence, no realistic occurrence in Western corporate/legal English documents | N/A | N/A |

## 2. Run-property (`w:rPr`) formatting — explicitly descoped from this construct audit

`RenderRun` reads `Bold`/`Italic`/`Underline`/`Strike` directly from `run.RunProperties` (`:712-716`) but does
**not** resolve character-style-cascaded formatting (`w:rStyle`), font color, highlight/shading, superscript/
subscript position (other than the `Ruby`-adjacent `w:vertAlign` used in the footnote-reference test fixture,
which is untouched by this builder), small-caps, or hidden text (`w:vanish`).

This is **intentionally out of scope** for the F-1 construct audit: F-1's invariant is **text exactness** — "run
text emitted verbatim, character-for-character" — not full WYSIWYG style-cascade parity. None of these
properties cause a **character/glyph to disappear from the visible text**; they only affect how that
(fully-present) text is styled. Notably:

- **`w:vanish` (hidden text)** is not suppressed — hidden runs always render. This is the SAFE-by-default
  choice for a legal-fidelity reader (never hide content a reviewer might need to see), the opposite of a
  silent drop.
- **Style-cascaded formatting** (a named paragraph/character style supplying bold/italic/indentation without
  the run/paragraph repeating it directly) is a distinct, larger "style resolution" concern. WS-3's FR-12
  explicitly scopes **style-linked numbering** resolution as in-scope for the numbering engine; general
  style-cascaded run/paragraph FORMATTING resolution is not named in spec/design as WS-2 or WS-3 scope and is
  not addressed here.

## 3. Block-level constructs (paragraph properties, tables, sections)

| Construct (`w:*`) | Disposition | Evidence (file:line) | Test-covered? |
|---|---|---|---|
| `w:jc` (justification/alignment) | **Represented** — `text-align:{center\|right\|justify}` (left = Word default, no style emitted) | `AppendParagraphStyle` `:947-970` | Yes — `Build_ParagraphWithJustification_EmitsTextAlignStyle` [Theory: center/right/both], `Build_ParagraphWithLeftJustification_EmitsNoTextAlignStyle` (task 002) |
| `w:ind` (`@left`/`@firstLine`/`@hanging`) | **Represented** — `margin-left`/`text-indent` CSS, twips→pt exact conversion (fixed task 021, FR-07) | `AppendIndentDeclarations` `:995-1018` | Yes — 6 tests (task 021) incl. hanging-precedence edge case |
| `w:numPr` (direct paragraph numbering) | **Represented (structurally)** — `<ol>`/`<ul>` + `<li>` based on `w:numFmt` (bullet→unordered, else ordered); the COMPUTED legal number itself is explicitly WS-3 scope (not yet built) — `multi-level-numbering` warning raised for `ilvl > 0` so the simplification is auditable, not silent | `ListInfo`/`ResolveOrdered` `:895-933`; list emit in `RenderParagraph` | Yes — `Build_ParagraphWithDecimalNumPr_RendersInsideOrderedList`, `Build_ParagraphWithBulletNumPr_RendersInsideUnorderedList` (task 002) |
| `w:numPr` (style-linked, on the paragraph STYLE not the paragraph) | **Deliberately not treated as a list** — mirrors `ComposeDocumentRenderer`'s model (style-linked numbering renders as a HEADING, not an `<ol>` item); WS-3 FR-12 is the explicit follow-on task that resolves style-linked numbering computation | `ListInfo` comment `:900-901` | Deferred to WS-3 (030-033); not a WS-2 gap |
| `w:pStyle` (Heading1-6 detection) | **Represented** — `<h1>`-`<h6>` tag selection | `HeadingLevel` `:876-891` | Yes — `Build_BoldItalicRunsAndHeading_EmitStrongEmAndHeadingTags` |
| `w:tbl` / `w:tr` / `w:tc` (table structure) | **Represented (text-complete, attribute-bare)** — `<table><tbody><tr><td>`, cell paragraphs continue the same paraId sequence; NO `colspan`/`rowspan`/border/shading attributes emitted (`w:gridSpan`/`w:vMerge`/`w:tblBorders`/`w:shd` dropped). Every cell's TEXT is fully present (F-1 holds); only structural/visual table fidelity (merged-cell rendering) is reduced. **Explicitly out of scope per spec.md G4** ("table op... reading tables already works via the projection" — the spec's own bar for table READING is text-presence, not full attribute fidelity) | `RenderTable` `:294-310` | Yes (text-presence) — `Build_MixedDocumentWithTablesAndContentControl_...`; NOT tested for colspan/rowspan (correctly out of scope) |
| `w:sectPr` (section properties: page size/margins/columns/header-footer refs) | **Deliberately dropped, correct** — carries zero paragraph-run text of its own; page/section layout is explicitly F-5/WS-5 territory (deferred spike, not committed in R4.5) | `RenderBlockChildren` `default:` `:247-251` (falls through, by design) | Corpus-implicit — every corpus doc has ≥1 `sectPr`, harness's 8/8 text-exactness confirms zero effect on run text |
| `w:bookmarkStart` / `w:bookmarkEnd` (internal anchor targets) | **Known gap, not fixed this task — navigation-functionality, not text-fidelity.** No `id="..."` is ever emitted on the target paragraph/span, so an internal `<a href="#bookmark">` (which `ResolveHyperlinkHref` DOES correctly emit for the SOURCE side, `:869-871`) points to a non-existent target in the emitted HTML. Carries no visible text of its own — not an F-1 (text-exactness) violation, but a real link-functionality gap. Deferred: fixing it is a UX/navigation feature (add `id=` attributes + verify browser scroll-to), not a construct represent-or-warn fix, and no spec/design requirement names it | `RenderBlockChildren` `default:` `:247-251`; `ResolveHyperlinkHref` `:856-880` | No — flagged, not escalated (see §5) |
| `w:proofErr` (spelling/grammar squiggly-underline marker) | **Deliberately dropped, correct** — zero content, a Word-authoring-time UI hint only | Falls through in both `RenderBlockChildren` and `CollectRunBoundaries`/`RenderInline` default cases | N/A — no content to lose |

## 4. Summary counts

| Disposition | Count (run-child) | Count (block-level) |
|---|---|---|
| Represented (faithfully, no warning needed) | 10 (`w:t`, `w:delText`, `w:tab`, `w:ptab`\*, `w:br` default, `w:cr`, `w:noBreakHyphen`, `w:sym` mapped, `w:drawing`, `w:fldChar`/`w:fldSimple`) | 4 (`w:jc`, `w:ind`, `w:pStyle` headings, `w:tbl` text-complete) |
| Represented + warned (fidelity downgrade or simplification made auditable) | 6 (`w:br` page\*, `w:br` column\*, `w:sym` unmapped, `w:ruby` base\*, `w:pict`\*, `w:numPr` multi-level) | 0 |
| Warned only (no safe representation, marker carries no independent text) | 2 (`w:footnoteReference`\*, `w:endnoteReference`\*) | 0 |
| Deliberately dropped, correct (no content/effect lost) | 2 (`w:softHyphen`, `w:lastRenderedPageBreak`) + delegated (`w:commentReference`, `w:annotationRef`) | 2 (`w:sectPr`, `w:proofErr`) |
| Out of scope — architectural boundary (different part, never opened) | 4 (`w:footnoteRef`/`w:endnoteRef`/`w:separator`/`w:continuationSeparator`) | 0 |
| Out of scope — legacy/deprecated, not producible by modern Word | 3 (date/time fields, `w:pgNum`, `w:contentPart`) | 0 |
| Known gap — flagged, not fixed (low probability / different concern class) | 1 (standalone `w:instrText` outside a field scan) | 1 (`w:bookmarkStart`/`w:bookmarkEnd` navigation) |
| Deferred to WS-3 (numbering computation) | — | 1 (style-linked `w:numPr`) |

\* = fixed or newly covered by task 022.

**F-1 compliance**: every run-child and block-level construct enumerated above is either represented, warned, or
deliberately dropped with a documented reason that shows no recoverable text/content is lost. **Zero constructs
are silently dropped** as of this task. The two "known gap, flagged not fixed" items (§1 standalone instrText,
§3 bookmark navigation) do not violate F-1 because neither carries recoverable TEXT content — they are
functionality gaps (a rare malformed-doc edge case; a navigation/anchor-resolution gap), not text-loss.

## 5. Escalation assessment (per task 022 POML §escalation)

**Did not fire.** Every construct found silently dropped in this audit (`w:pict`, `w:ptab`, `w:footnoteReference`,
`w:endnoteReference`, `w:ruby`) was safely represented or warned within this task — none required guessing at
content the way an unmapped `w:sym` would. The two remaining "known gap" items (standalone `w:instrText` outside
a field scan; `w:bookmarkStart`/`w:bookmarkEnd` navigation) carry no independent text content, so leaving them
unfixed does not breach F-1's "no construct silently drops TEXT" invariant — they are recorded here for
transparency, not swept under the rug, consistent with the "STOP and surface, don't paper over" instruction.

## 6. Fixes made this task (code changes)

All five fixes are additive `case` arms / helper extensions to the existing `RenderRun`/`RunEditorLength`/
`ExtractRunsDisplayText`/`IsComplexObjectRun` switches in
`src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs` — no new abstraction, no new
service, no new package (ADR-007/013 purity preserved):

1. **`w:pict` (VML fallback picture)** — `IsComplexObjectRun` extended to also check `Picture` (LocalName
   `pict`), reusing the existing `ComposeAtomKind.ComplexObject` atom (the same disposition as `w:drawing`/
   `w:object` — it is equally an opaque image/shape, never opened for display per I-4).
2. **`w:ptab` (positional tab)** — represented identically to `w:tab` (the compose-tab non-collapsing-space
   simplification), added to the same `case` group in `RenderRun`, `RunEditorLength`, and
   `ExtractRunsDisplayText`.
3. **`w:footnoteReference` / `w:endnoteReference`** — warn-only (`unrepresented-footnote-reference` /
   `unrepresented-endnote-reference`); no placeholder glyph is fabricated (the mark's displayed number is not
   derivable from the main body alone, and guessing risks the "wrong glyph in a legal document" failure class
   task 020 already rejected for `w:sym`).
4. **`w:ruby`** — the base text (`w:rubyBase`, real recoverable prose) is now rendered verbatim via the
   existing `ExtractRunsDisplayText` helper (new `RubyBaseRuns` accessor); the phonetic guide (`w:rt`) is
   dropped with a `ruby-phonetic-guide-dropped` warning so the simplification is auditable.
5. **`w:br type="page"` / `type="column"`** — still renders as `<br>` (no pagination engine in this editor;
   F-5/WS-5 is a separate, deferred spike) but now raises `page-break-rendered-as-line-break` /
   `column-break-rendered-as-line-break` so the "hard break downgraded to soft break" fidelity loss is
   surfaced rather than silently absorbed into the same disposition as an ordinary `w:br`.

None of the five fixes touch a corpus-exercised code path differently than before (no corpus doc contains
`w:pict`/`w:ptab`/footnote/endnote/`w:ruby`/`w:br type=page-or-column`) — the 8/8 corpus text-exactness harness
result is unaffected; all five are validated by new synthetic unit fixtures (task 022, following the same
"synthetic unit fixture for a genuinely-unmapped/uncorpus-exercised case" precedent task 020 set for the
unmapped-`w:sym` negative test).

## 7. Test coverage summary (FR-09's named deliverables + task 022's additions)

The FR-09 acceptance criterion names three test categories as "absent today" (as of the original design §4
investigation, predating tasks 002/020/021/022):

- **Alignment** — already added by task 002 (`Build_ParagraphWithJustification_EmitsTextAlignStyle` [Theory:
  center/right/both] + `Build_ParagraphWithLeftJustification_EmitsNoTextAlignStyle`). Confirmed present and
  passing; not duplicated by this task.
- **Ordered-list / bullet-list** — already added by task 002 (`Build_ParagraphWithDecimalNumPr_...`,
  `Build_ParagraphWithBulletNumPr_...`), asserting the STRUCTURE the projection emits today (`<ol>`/`<ul>` +
  `<li>`), not a computed number (WS-3 territory, not yet built) — exactly the scope the task brief specified.
  Confirmed present and passing; not duplicated by this task.
- **Symbol** — already added by task 020 (mapped + unmapped-placeholder-and-warning cases). Confirmed present
  and passing; not duplicated by this task.

This task's OWN new test additions (8 tests, `ComposeDocxProjectionBuilderTests.cs`, "task 022" section) cover
the five construct fixes in §6 plus the page/column-break negative (non-regression) case:

1. `Build_RunWithVmlPictureFallback_BecomesComplexObjectAtom_InsteadOfSilentlyVanishing`
2. `Build_ParagraphWithPositionalTab_RendersComposeTabSpanLikeRegularTab`
3. `Build_ParagraphWithFootnoteReference_RaisesWarning_NeverSilentlyVanishes`
4. `Build_ParagraphWithEndnoteReference_RaisesWarning_NeverSilentlyVanishes`
5. `Build_ParagraphWithRubyAnnotation_RendersBaseTextVerbatim_DropsPhoneticGuideWithWarning`
6. `Build_ParagraphWithPageOrColumnBreak_RendersLineBreakAndWarnsOfFidelityDowngrade` [Theory: page, column]
7. `Build_ParagraphWithDefaultTextWrappingBreak_RendersLineBreakWithNoWarning` (negative/non-regression guard)

## 8. Build / test / publish-size results

- `dotnet build src/server/api/Sprk.Bff.Api/ -c Release` → **0 errors** (23 pre-existing warnings, identical
  set to tasks 020/021's baseline — no new warnings from this task's changes).
- `dotnet test --filter "FullyQualifiedName~Compose"` → **Passed: 637, Skipped: 1, Failed: 0, Total: 638**
  (task 021's baseline was 629 passed + 1 skipped; net +8 new tests, matching the 8 listed in §7 exactly). No
  regressions.
- `dotnet test --filter "FullyQualifiedName~ComposeReadFidelityHarnessSeamTests"` → **Passed: 12, Skipped: 1,
  Failed: 0** — harness stays GREEN; all 8 corpus docs remain 100% text-exact (unaffected by this task's
  fixes, none of which touch a corpus-exercised construct — see §6).

## 9. Placement Justification (root CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

- **Existing**: `RenderRun`/`RunEditorLength`/`ExtractRunsDisplayText`/`IsComplexObjectRun` already exist as
  the run-child dispatch switches; `BuildContext.AddWarning` already exists as the general warning-surface
  mechanism (used by 7 warning codes before this task: `unrendered-paragraphs`, `content-control`,
  `opaque-atom-sdt`, `multi-level-numbering`, `numbering-unresolved`, `unmapped-symbol-char`, plus this task's
  6 new codes).
- **Extension**: Yes — every fix in §6 is an additive `case` arm (or a small accessor helper,
  `RubyBaseRuns`) on an EXISTING switch, mirroring an established pattern (`w:tab`'s compose-tab simplification
  for `w:ptab`; `w:drawing`/`w:object`'s `ComplexObject` atom for `w:pict`; the existing `AddWarning` surface
  for all new warning codes). No new service, no new abstraction, no new DI registration, no new package.
- **Cost of doing nothing**: Five constructs (`w:pict`, `w:ptab`, `w:footnoteReference`/`w:endnoteReference`,
  `w:ruby` base text) would continue vanishing from a projected legal document with zero trace — the exact F-1
  (text exactness, a release blocker) failure class this WS-2 workstream exists to close, undiscoverable by a
  reader trusting the projection. The `w:br type=page` fidelity downgrade would continue silently masquerading
  as an ordinary line break, hiding a real (if minor, given no pagination engine exists) semantic loss.
- `Services/Compose/` stays pure — no `Microsoft.Graph`/AI-internal reference added (ADR-007/013); no
  `byte[]`-in/projection-out contract change. Publish-size delta is a handful of new `case` arms and one small
  private helper method — expected ~0 MB, well under the root CLAUDE.md §10 escalation threshold (measured
  below).

## 10. Publish-size verification

`dotnet publish -c Release src/server/api/Sprk.Bff.Api/` was run per root CLAUDE.md §10, measured directly via
the same before/after `git stash` isolation method tasks 020/021 used (stash ONLY the production file, publish,
compare `Sprk.Bff.Api.dll` size, restore):

- **Before** (task 021's state, `ComposeDocxProjectionBuilder.cs` unchanged): `Sprk.Bff.Api.dll` = **11,266,560
  bytes** — exact match to task 021's own reported post-change figure, confirming a clean baseline.
- **After** (this task's 5 construct fixes applied): `Sprk.Bff.Api.dll` = **11,268,096 bytes**.
- **Delta: +1,536 bytes (+1.5 KB)** — five additive `case` arms plus one ~5-line private static helper
  (`RubyBaseRuns`). No new NuGet package, no `.csproj` change (`git diff --stat -- '*.csproj'` empty).

Consistent with tasks 020 (+1 KB) and 021 (+512 bytes), this is well inside the root CLAUDE.md §10 ≥+5 MB
single-task escalation threshold and the ≤60 MB hard ceiling (baseline ~49.63 MB incl. PDBs).
