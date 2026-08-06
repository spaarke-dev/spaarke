# Task 030 — WS-3 Numbering-Model Reader (FR-11/FR-12)

> Written by the task 030 sub-agent execution. Sub-agent write boundary: this file (under
> `projects/spaarkeai-compose-fidelity-r4.5/notes/`) is in-bounds; `TASK-INDEX.md` / `current-task.md` are NOT
> touched here — owned by the main session.

## Summary

First WS-3 task. Builds the **read-side numbering MODEL** inside `ComposeDocxProjectionBuilder.cs` — parses
`numbering.xml` (`w:num` → `w:abstractNumId` → `w:abstractNum`, per-level `w:numFmt`/`w:lvlText`/`w:start`/
`w:lvlRestart`/`w:isLgl`/`w:lvlOverride`/`w:startOverride`) and resolves STYLE-LINKED numbering from
`styles.xml` (FR-12: a paragraph style — e.g. `Heading2` — carrying the `w:numPr`). **No number is computed.**
This is the closed input task 031's computation engine will replay Word's per-`(abstractNumId, level)` counter
algorithm over. HTML output is byte-for-byte unchanged from before this task — only new queryable structure
was added, wired into the projection's EXISTING single document-order walk (no second pass).

## The model data structure exposed for 031

All new types are `internal` nested members of `ComposeDocxProjectionBuilder` (InternalsVisibleTo
`Sprk.Bff.Api.Tests` already covers them — same pattern as the existing `internal` test-seam constructor):

- **`NumberingLevelDef(AbstractNumId, Level, NumFmt, LvlText, Start, LvlRestart, IsLgl, ParagraphStyleIdInLevel, HasPictureBullet)`**
  — one level's authored definition. Field set mirrors exactly what `ComposeDocumentRenderer.cs` (write-side
  mirror) authors via `StartNumberingValue`/`LevelText`/`NumberingFormat`/`ParagraphStyleIdInLevel` (`:570-640`
  at time of writing), so task 033's read↔write round-trip test compares like-for-like.
- **`NumberingLevelOverrideDef(StartOverride, FullLevelOverride)`** — a `numId`-scoped `w:lvlOverride`: either
  a `w:startOverride` (restart THIS numId's counter without redefining the level) or a full `w:lvl` override.
- **`ParagraphNumberingRef(NumId, Ilvl, StyleLinked, SourceStyleId)`** — a paragraph's resolved numbering
  source. `StyleLinked=false` mirrors `ListInfo`'s direct `w:numPr` extraction exactly (same numId/ilvl
  reading code, so the two can never disagree on the direct case). `StyleLinked=true` is the FR-12 case;
  `SourceStyleId` is the style that ACTUALLY carries the `w:numPr` (may be an ancestor of the paragraph's own
  `pStyle`, reached via `w:basedOn` — see the inheritance test).
- **`NumberingModel`** — the closed model: `AbstractNumIdByNumId`, `Levels` (keyed `(abstractNumId, level)`),
  `Overrides` (keyed `(numId, level)`), `StyleLinkedNumbering` (keyed by styleId),
  `UnresolvedNumStyleLinkAbstractNumIds` (escalation surface — see below). `ResolveLevel(numId, ilvl)` honors
  a full `w:lvlOverride` before falling back to the abstractNum's own level (same precedence
  `ComposeDocumentRenderer` authors). `ResolveStartOverride(numId, ilvl)` reads just the `w:startOverride`.

**Entry points 031 calls:**
- `internal static NumberingModel BuildNumberingModel(MainDocumentPart mainPart)` — pure, parses `numbering.xml`
  + `styles.xml` ONCE. Fail-open (never throws) mirroring `ResolveOrdered`'s existing posture — a malformed
  numbering or styles part degrades that part of the model to empty rather than aborting `Build()` (F-04).
- `internal static ParagraphNumberingRef? ResolveParagraphNumbering(Paragraph p, NumberingModel model)` —
  resolves one paragraph's effective `(numId, ilvl)`: direct `w:numPr` first, else `pStyle` → `StyleLinkedNumbering`.

## Single-walk wiring

`BuildNumberingModel` is called ONCE in `Build()` right after `mainPart`/`body` are established — it is a
side-part read (`numbering.xml`/`styles.xml` are not the document body), so it adds no extra walk. Per-paragraph
resolution is folded into the EXISTING Pass-1 identity-assignment loop (`for (var i = 0; i < paragraphs.Count;
i++)` — the same loop that mints `w14:paraId`s and builds the offset-addressing table): each paragraph's
`ParagraphNumberingRef?` is resolved there and stored in a new `Dictionary<Paragraph, ParagraphNumberingRef>`
keyed by paragraph IDENTITY (mirrors the existing `idByParagraph` pattern exactly — never by ordinal index).
`BuildContext` now carries `Numbering` (the model) and `TryGetParagraphNumbering(Paragraph, out ...)` so task
031's computation, when it lands, can pull both out from the SAME per-paragraph call site `RenderParagraph`
already visits in Pass 2 — no second full-document walk is introduced by this task or required by 031.

**HTML output is untouched.** `RenderParagraph`'s actual emit logic (the `listInfo`/`headingLevel` branches)
was NOT modified — only new side-channel structure was added to `BuildContext`. The pinned WS-3 characterization
Facts in `ComposeReadFidelityHarnessSeamTests.cs` (which assert the CURRENT `<ol>`-restart-per-interruption /
dropped-heading-number / flat-multilevel defects) still pass unmodified — proof that this task changed nothing
observable, only added a queryable model for 031 to consume.

## Style-linked resolution (FR-12)

`ResolveStyleNumbering(Style, stylesById, maxHops=20)` — checks the style's own `StyleParagraphProperties.
NumberingProperties` first; if absent, follows `w:basedOn` up the ancestor chain (Word's own paragraph-property
style inheritance applies to numbering too), with a visited-set cycle guard. Verified against:
- A synthetic `Heading2` style carrying `w:numPr` directly (mirrors `heading-style-numbering.docx`).
- A synthetic `Heading2Sub` style with NO `w:numPr` of its own, `w:basedOn="Heading2"` — resolves through the
  ancestor; `SourceStyleId` correctly reports `"Heading2"` (the ancestor that defines it), not the queried
  `"Heading2Sub"` — this required a small self-caught fix during authoring (the first draft returned the
  QUERIED style's id instead of the DEFINING style's; caught before commit by writing the inheritance test).
- The real `heading-style-numbering.docx` corpus doc (task 001): confirmed Heading1 paragraphs resolve at
  `ilvl=0` and Heading2 paragraphs resolve at `ilvl=1`, both `StyleLinked=true`, entirely via `pStyle` — the
  document's `document.xml` carries zero `w:numPr` (task 001's confirmed fact, corpus-manifest.md row 10).

## `w:numStyleLink`/`w:styleLink` chain handling — RESOLVED (not just detect+warn)

The POML asked for "at minimum detect + warn" on `numStyleLink` chains. Implemented the full resolution
instead, since the OOXML SDK exposes everything needed cheaply: `AbstractNum.NumberingStyleLink` (`w:numStyleLink`)
names a "numbering style" (`w:style w:type="numbering"`) whose OWN `w:numPr` names the numId carrying the REAL
level definitions — one indirection hop beyond the ordinary `numId → abstractNum` lookup.
`ResolveNumStyleLinkTarget` chases that hop (bounded to 8 hops + a visited-set cycle guard) and returns the
concrete `AbstractNum` whose levels are then parsed normally. **If the chain cannot be resolved** (target style
missing, no numId on it, or the hop budget is exhausted), the abstractNum id is recorded in
`UnresolvedNumStyleLinkAbstractNumIds` and a `"numstylelink-unresolved"` warning fires — but ONLY when a
paragraph in the document actually resolves to that construct (via the Pass-1 loop's `AbstractNumIdByNumId`
lookup), not merely because numbering.xml happens to define one somewhere unused. This avoids false-positive
escalation on ordinary Word-authored template cruft (real docx files commonly carry numbering definitions no
paragraph references). None of the three WS-3 corpus exemplars (`nda-interrupted-clauses.docx`,
`heading-style-numbering.docx`, `multilevel-1-1-1.docx`) use `numStyleLink` — verified by a negative test
(`Build_OverNumberingExemplarCorpusDocs_ParsesModelWithoutAnyEscalationWarning`) asserting the projection raises
neither `numstylelink-unresolved` nor `picture-bullet-unresolved` on any of them.

**Picture bullets** (`w:lvlPicBulletId` / `LevelPictureBulletId`) are similarly detected (`NumberingLevelDef.
HasPictureBullet`) and warned on the same "only if actually used" basis. Note: `symbol-section-mark.docx`'s
Wingdings bullet (corpus-manifest.md row 12) is a DIFFERENT mechanism — a PUA character in `w:lvlText` with a
Wingdings `w:rFonts` in `NumberingSymbolRunProperties`, not an embedded `w:pict`/`w:lvlPicBulletId` reference —
so it is captured verbatim by `LvlText` with no escalation needed; its glyph-mapping question belongs to
render time (WS-2/032), not this model.

## Escalation trigger — NOT fired

No corpus doc exercised a construct outside the model. If a future corpus addition (or a real owner document)
uses an unresolvable `numStyleLink` chain or a picture bullet, `Build()` now surfaces it as a warning
(`numstylelink-unresolved` / `picture-bullet-unresolved`) rather than silently dropping the numbering — the
escalation mechanism is live and tested, just not triggered by the current corpus.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` — 0 errors (pre-existing warnings only, unrelated).
- `dotnet test --filter "FullyQualifiedName~Compose"` — 644 passed, 1 skipped (the WS-3 target
  `NumberingExactness_...` Theory, still gated on 031/032), 0 failed. Baseline before this task was 637
  passed / 1 skipped / 0 failed — the +7 are this task's new model-reader tests, 0 regressions.
- `dotnet test --filter "FullyQualifiedName~TextExactness"` — 8/8 passed (NFR-01 harness untouched).
- Publish-size (BFF Hygiene §10, root CLAUDE.md §10 bullet 4): compressed baseline (pre-task, same
  `Compress-Archive` tool for apples-to-apples) **47.51 MB** → post-task **47.52 MB**, **delta +0.008 MB**
  (~0 MB as expected — pure C# addition over the already-referenced `DocumentFormat.OpenXml` package, no new
  runtime package). Well under the ≤60 MB ceiling.
- No new NuGet package added — `dotnet list package --vulnerable` scope unaffected by this task.

## Placement Justification (root CLAUDE.md §10/§11, `.claude/constraints/bff-extensions.md`)

- **Existing**: `ResolveOrdered` (`ComposeDocxProjectionBuilder.cs`, pre-task ~`:916-939`) reads only a single
  bullet-vs-ordered bit from `w:numPr`; `ComposeDocumentRenderer.cs` authors numbering on the WRITE side. Grep
  of the pre-task builder for `abstractNum`/`lvlText`/`numFmt` returned zero matches — no read-side numbering
  model existed.
- **Extension**: Yes — a new read-side capability added INSIDE the existing `ComposeDocxProjectionBuilder`, not
  a new abstraction/service. It parses exactly the model `ComposeDocumentRenderer` already authors on the write
  side (task 033 proves the two agree).
- **Cost-of-doing-nothing**: without the parsed model, there is nothing for the WS-3 computation engine (031)
  to replay Word's algorithm over — heading-style numbers stay dropped and multi-level stays discarded to a
  warning count; the core legal-fidelity defect (numbers collapse/disappear, NFR-02 release blocker) cannot be
  fixed.
- `Services/Compose/` stays pure `byte[]`-in/projection-out — no `Microsoft.Graph`, no AI-internal type (ADR-007/
  ADR-013). Confirmed by inspection: the new code touches only `DocumentFormat.OpenXml.Wordprocessing`/
  `.Packaging` types already imported at the top of the file.

## Files changed

- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs` — numbering-model reader
  (new region between `ResolveOrdered` and `AppendParagraphStyle`), `Build()` wiring (model build + Pass-1
  per-paragraph resolution + escalation diagnostics), `BuildContext` extended with `Numbering` +
  `TryGetParagraphNumbering`.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeDocxProjectionBuilderTests.cs` — 7 new tests: 2
  synthetic-fixture model-field sanity tests (multi-level + override; direct-numPr resolution), 2 style-linked
  tests (direct style numPr; `w:basedOn` inheritance), 2 real-corpus assertions (heading-style doc,
  multilevel doc), 1 negative/escalation-boundary test over all three WS-3 exemplars.
