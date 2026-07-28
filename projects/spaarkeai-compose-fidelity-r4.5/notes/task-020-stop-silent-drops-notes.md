# Task 020 — WS-2 Stop Silent Glyph Drops (`w:cr`, `w:sym`) + Intra-Run Warning Mechanism

> Written by the task 020 sub-agent execution. Sub-agent write boundary: this file (under
> `projects/spaarkeai-compose-fidelity-r4.5/notes/`) is in-bounds; `TASK-INDEX.md` / `current-task.md` are NOT
> touched here — owned by the main session.

## Summary

Closes the two silent-drop defects task 002 characterized (WS-0 baseline) on the projection read path:
`w:cr` (`CarriageReturn`) and `w:sym` (`SymbolChar`) were absent from `ComposeDocxProjectionBuilder.RenderRun`'s
run-child switch and silently contributed nothing. Both are now represent-or-warn (F-1):

- **`w:cr`** → emits `<br>`, mirroring the pre-existing `w:br` (`Break`) handling exactly (FR-05).
- **`w:sym`** → maps to its verified Unicode equivalent where known, else emits a visible U+FFFD
  placeholder AND raises an intra-run glyph-loss warning (`unmapped-symbol-char`) (FR-06/FR-10).

No escalation fired: the one legally-load-bearing code point the corpus exercises (Symbol-font `F0A7`, the
§ section mark) has a verified, unambiguous Unicode mapping (U+00A7) — see corpus-manifest.md row 12.

## Run-switch audit (step 3, before editing)

Re-grepped `ComposeDocxProjectionBuilder.cs` before editing (line numbers had drifted from the POML's cited
`:685-705`; actual location was `:672-714` for `RenderRun`, `:530-547` for `ExtractRunsDisplayText`, `:587-611`
for `RunEditorLength`). Confirmed current handling across THREE parallel walks (the file's own documented
"parallel-walk pattern", `RunEditorLength` vs `RenderRun`, plus the field/atom `ExtractRunsDisplayText` walk):

| Run child | `RenderRun` (HTML emit) | `RunEditorLength` (offset table) | `ExtractRunsDisplayText` (atom/field text) |
|---|---|---|---|
| `Text` / `DeletedText` | `AppendEscaped` (verbatim) | text length | verbatim append |
| `TabChar` | `<span class="compose-tab"> </span>` | +1 | `' '` |
| `Break` | `<br>` | +1 | `' '` |
| `NoBreakHyphen` | U+2011 (`‑`) | +1 | — (not in the group before this task; unchanged, pre-existing gap, out of this task's scope) |
| `CarriageReturn` | **ABSENT → dropped** | **ABSENT → 0** | **ABSENT → dropped** |
| `SymbolChar` | **ABSENT → dropped** | **ABSENT → 0** | **ABSENT → dropped** |
| (default / other) | dropped (e.g. `SoftHyphen` — deliberate, correct, out of scope) | 0 | dropped |

This task added `CarriageReturn` + `SymbolChar` to all three walks so the parallel-walk invariant (offset
table length == HTML render length == atom display-text length) holds for the new constructs exactly like it
already did for `Break`/`TabChar`/`NoBreakHyphen`.

## `w:sym` mapping table implemented (FR-06 coverage — deliberately small, hand-verified)

```csharp
private static readonly Dictionary<(string Font, string Char), string> KnownSymbolGlyphMap = new()
{
    [("Symbol", "F0A7")] = "§",   // section mark, U+00A7 — corpus-manifest.md row 12, task 001's verified fixture
};
```

**Deliberately NOT algorithmic.** Symbol-font PUA code points (`F020`–`F0FF`) often follow a
"subtract `0xF000`, treat the remainder as the legacy Symbol/Latin-1-ish charset code point" heuristic — it
happens to hold for `F0A7 → §` (`0xA7` is coincidentally also the Latin-1 Supplement section-mark position).
But this heuristic is **unverified across the rest of the Symbol font**, and per this task's escalation
trigger and F-1, a wrongly-mapped legal glyph is a worse failure than an honest, warned placeholder — a wrong
§ (or any other guessed glyph) is a wrong legal document, not a cosmetic gap. The mapping table is therefore
kept small and hand-curated; extend it only with entries verified against a real corpus/owner document. It
mirrors (and must stay in sync with) the identically-scoped `KnownSymbolGlyphMap` in
`ComposeReadFidelityHarnessSeamTests.cs` (task 002's golden-extraction helper).

The corpus's OTHER `symbol-section-mark.docx` negative case — 2 Wingdings-bullet paragraphs with no
canonical Unicode target — is **not** a body-run `w:sym` at all; it is defined on the numbering LEVEL's
`lvlText` (a list-marker glyph the browser's default `<ul>` styling renders, never emitted as paragraph run
text), so it is untestable via this projection's run-text path and was, and remains, unaffected by this task
(same "known gap, not asserted" disposition task 002's WS-0 baseline already recorded). The genuinely-unmapped
`w:sym` run-level case (FR-06's negative branch) is exercised instead by a synthetic unit fixture — see Tests
below.

## Intra-run glyph-loss warning mechanism (FR-10)

Extended (not duplicated) the existing `BuildContext.AddWarning(string code, int count)` / `Warnings`
mechanism (the same one the F-03 `"unrendered-paragraphs"` guard, `"content-control"`, `"multi-level-numbering"`,
and `"numbering-unresolved"` warnings already use) with a new code: **`"unmapped-symbol-char"`**, raised once
per unmapped `w:sym` run child, from inside `RenderRun` (i.e. INSIDE the per-run switch, not a paragraph-level
guard) — this is what makes it an intra-run observation per FR-10, unlike the pre-existing F-03 guard which
only counts paragraphs. A represented `w:cr` (mapped to `<br>`) does NOT raise a warning — only genuinely
unrepresentable content (the unmapped-symbol branch) does, consistent with "never drop without a warning,
never warn about something correctly represented."

## Tests

### Unit (`ComposeDocxProjectionBuilderTests.cs`) — flipped + added

- `Build_ParagraphWithSymbolCharRun_MappedSymbolFont_RendersUnicodeGlyphWithNoWarning` — flips task 002's
  `..._CurrentlyDropsGlyphSilently_CharacterizationForWS2Fr06`. Symbol `F0A7` now renders `§` verbatim; no
  `unmapped-symbol-char` warning.
- `Build_ParagraphWithSymbolCharRun_UnmappedSymbolFont_RendersPlaceholderAndRaisesWarning` — NEW. A
  synthetic `Wingdings`/`F0A8` (not in the mapping table) renders U+FFFD in place AND raises exactly one
  `unmapped-symbol-char` warning (`Count == 1`); asserts `Status == Partial`. This is the acceptance-criteria
  "placeholder always co-occurs with a warning" negative case, and covers FR-06's unmapped branch that the
  corpus itself does not exercise at the run level (see mapping-table note above).
- `Build_ParagraphWithCarriageReturnRun_RendersBreakLikeExistingWBr` — flips task 002's
  `..._CurrentlyDropsGlyphSilently_CharacterizationForWS2Fr05`. `"before" + w:cr + "after"` now renders
  `"before<br>after"`.

### Seam harness (`ComposeReadFidelityHarnessSeamTests.cs`) — characterization flipped

`TextExactness_OnEveryCorpusDoc_MatchesSourceRunTextOrCharacterizesKnownDrop` renamed to
`TextExactness_OnEveryCorpusDoc_MatchesSourceRunTextExactly` and simplified: removed the
`hasUnrepresentedConstruct` (`SymbolChar`/`CarriageReturn` descendant) special-case branch entirely — every
paragraph in every corpus doc now asserts a straight exact match against golden text, unconditionally. This
is correct because: (a) every corpus `w:sym` occurrence (`symbol-section-mark.docx`'s 2 body-run instances)
resolves through the VERIFIED mapping, and (b) the corpus carries zero `w:cr` occurrences (WS-0 baseline).
Verified live: `symbol-section-mark.docx: 6 of 6 paragraphs exact` (previously 4 of 6, with the 2 `w:sym`
paragraphs characterized as mismatching). All 8 corpus docs are now 100% text-exact.

The `KnownSymbolGlyphMap` / `ResolveGoldenSymbolGlyph` golden-extraction helper in this file (task 002's own
production-mirroring logic) was left unchanged — it already computed the CORRECT golden text (including the
§ glyph), so no change was needed there; only the assertion branch that previously characterized the
(now-fixed) drop was removed.

## Build / test / publish-size results

- `dotnet build src/server/api/Sprk.Bff.Api/ -c Release` → **0 errors** (23 pre-existing warnings, unrelated
  to this task).
- `dotnet test --filter "FullyQualifiedName~Compose"` → **Passed: 623, Skipped: 1, Failed: 0, Total: 624**
  (skip is the pre-existing WS-3-gated `NumberingExactness_...` Theory, unrelated to this task). No
  regressions; net +1 new test (2 characterization tests flipped in place, 1 new unmapped-placeholder test
  added).
- Publish-size delta (measured directly, same-method before/after via `git stash` isolation of only the
  production file, `dotnet publish -c Release`): `Sprk.Bff.Api.dll` **11,265,024 → 11,266,048 bytes
  (+1,024 bytes / +1 KB)**. Confirms the expected ~0 MB delta for a pure-`Services/Compose/` code addition
  with no new NuGet dependency — well inside the root CLAUDE.md §10 ≥5 MB escalation threshold and the
  ≤60 MB hard ceiling.

## Placement Justification (root CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

- **Existing**: `ComposeDocxProjectionBuilder.RenderRun`'s run-child switch already handles
  `Text`/`DeletedText`/`TabChar`/`Break`/`NoBreakHyphen` in-place; `BuildContext.AddWarning` already exists
  as the general warning-surface mechanism (used by 3 other warning codes).
- **Extension**: Yes — this task adds two `case` arms to the existing switch (mirroring `Break`'s pattern for
  `CarriageReturn`, and a small resolve-or-placeholder helper for `SymbolChar`) and one new warning `code`
  string routed through the EXISTING `AddWarning`/`Warnings` surface. No new service, no new abstraction, no
  new DI registration, no new package.
- **Cost of doing nothing**: A `w:sym` § (the section mark in "§ 4.2" legal citations) and a `w:cr` vanish
  from the rendered legal document with zero warning — F-1 text exactness (a release blocker) fails
  silently, and a reader trusts a document that is missing characters. This is the exact defect class task
  002's WS-0 characterization baseline recorded and this task closes.
- `Services/Compose/` stays pure — no `Microsoft.Graph`/AI-internal reference added (ADR-007/013); no new
  `byte[]`-in/projection-out contract change. Publish-size delta ~1 KB (~0 MB), well under the escalation
  threshold.

## Escalation

**Did not fire.** The escalation trigger (a legally-load-bearing glyph — specifically the § mark — with no
safe mapping/placeholder) does not apply: § has a verified, unambiguous Unicode mapping (U+00A7) confirmed
against the corpus's hand-authored OOXML fixture (corpus-manifest.md row 12). The general "no known mapping"
case (any OTHER symbol-font code point) is handled per the non-escalation default: visible placeholder +
warning, never a silent drop — exercised by the synthetic unmapped-symbol unit test.

## Deviations from the task POML

None. All steps executed as specified; the mapping table is deliberately smaller than "a small table of
common Symbol/Wingdings code points" might suggest — see the "Deliberately NOT algorithmic" rationale above
for why only the one corpus-verified entry was added rather than guessed additional entries.
