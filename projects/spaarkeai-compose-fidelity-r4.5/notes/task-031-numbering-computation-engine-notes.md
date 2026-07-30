# Task 031 — WS-3 Numbering Computation Engine (FR-11..FR-14) — THE FLAGSHIP

> Written by the task 031 sub-agent execution. Sub-agent write boundary: this file (under
> `projects/spaarkeai-compose-fidelity-r4.5/notes/`) is in-bounds; `TASK-INDEX.md` / `current-task.md` are
> owned by the main session and NOT touched here.

## Summary

The deterministic numbering COMPUTATION engine — replays Word's numbering algorithm (design §4 WS-3) over
the task-030 `NumberingModel`, producing the EXACT displayed label for every numbered paragraph. It runs
inside the projection's EXISTING single document-order Pass-1 walk (no second pass), carries counters
forward across the whole document (so interrupted runs never restart at 1), and attaches the computed
label to the projection via a new `ParaIdMapEntry.ComputedNumber` field for 032 (render) + WS-4 (task 040,
citation map). **NFR-02 acceptance proof is GREEN**: the previously `[Skip]`-gated
`NumberingExactness_...` Theory in the read-fidelity harness is now LIVE and passing for every numbered
paragraph across the corpus (24 golden cases).

## The algorithm as implemented (design §4 WS-3)

New nested `internal sealed class NumberingComputationEngine` in `ComposeDocxProjectionBuilder.cs`
(between `ResolveParagraphNumbering` and the alignment/`AppendParagraphStyle` region). One instance is
created before the Pass-1 loop; `Compute(ParagraphNumberingRef)` is called — in document order — for each
numbered paragraph (`numberingRef is not null`, direct `w:numPr` OR style-linked). Per call:

1. **Resolve** `abstractNumId = model.AbstractNumIdByNumId[numId]` and `levelDef = model.ResolveLevel(numId,
   ilvl)` (honors a full `w:lvlOverride` before the abstractNum's own level).
2. **Increment** the counter keyed `(abstractNumId, ilvl)`. First use since a reset initializes to
   `w:startOverride` (numId-scoped, applied once via an `_appliedStartOverrides` set) else the level's
   `w:start` (default 1); otherwise `+1`.
3. **Reset deeper levels** of the same abstractNum: default Word behavior (a more-significant level
   increment restarts all deeper levels), modified by `w:lvlRestart` — `0` ⇒ never restart; value `N`
   (1-based) ⇒ restart only when a level whose 1-based number ≤ `N` increments. Reduces to default for the
   corpus (which sets no non-default `lvlRestart`).
4. **Compose** the label from the level's `w:lvlText`: substitute each `%n` (n=1..9) with the counter at
   level `n-1` formatted per that level's `w:numFmt`, unless the current level is `w:isLgl` (legal) — then
   EVERY inserted reference is forced to decimal. Literal characters (`.`, `(`, `)`, spaces, `Article `)
   are copied verbatim. Depth reaches the current level → gives WS-4 the `"4.2(b)(iii)"` granularity.

Counter key is `(abstractNumId, level)` exactly as spec FR-11 / design §4 / 030 state. Every corpus
exemplar uses a single `numId` per `abstractNum`, so this is unambiguous for the corpus.

## The formatters (design §4 WS-3 step 3)

`FormatCounter(value, numFmt)`:
- **decimal** → `value.ToString()`
- **lowerLetter / upperLetter** → bijective base-26 (`ToLetters`): 1→a, 26→z, **27→aa**, 28→ab (z→aa
  overflow handled); non-positive degrades to decimal.
- **lowerRoman / upperRoman** → standard subtractive (`ToRoman`): 1→i, 4→iv, 9→ix, 40→xl; out-of-range
  (≤0 or >3999) degrades to decimal.
- **bullet** → the `lvlText` glyph verbatim (the displayed marker; WS-4 treats a non-numeric marker as a
  non-citation).
- **Any other format** (decimalZero, ordinal, cardinalText, …) → decimal fallback. Not used by the legal
  corpus; the honest never-throw degrade. See "Fallback honesty" below.

## How the label attaches to the node

`ParaIdMapEntry` (in `ParaIdPreParser.cs`) gained an optional 4th positional field
`string? ComputedNumber = null`. Additive / non-breaking: the Load-side `ParaIdPreParser` construction
sites (which pass `IsMinted:` by name) keep working and leave it `null` (numbering is a projection
concern, not identity-stamping). The projection `Build()` Pass-1 loop now computes the label BEFORE
`map.Add(...)` and passes it in. HTML emit (`RenderParagraph`) is **untouched** — 031 attaches DATA only;
rendering the label as a non-editable number-atom is task 032's job, which is why the 4 live
characterization Facts in the harness (asserting the CURRENT `<ol>`-restart / dropped-heading / flat
multi-level HTML shape) still pass unchanged.

The harness's `GetCurrentComputedNumber(projection, paraId)` stub was flipped from `=> null` to read
`projection.ParaIdMap.FirstOrDefault(e => e.ParaId == paraId)?.ComputedNumber`, and the `[Skip]` was
removed from `NumberingExactness_OnLegalNumberingExemplars_ComputedLabelMatchesGoldenWordLabel`.

## Golden-Theory flip result — per doc (NFR-02 acceptance, ALL PASS)

| Corpus doc | Golden cases | Result |
|---|---|---|
| `nda-interrupted-clauses.docx` | 1.–6. continuous across heading/body/table interruption (no restart) | ✅ |
| `heading-style-numbering.docx` | 1 / 2 / 3 / 4 / 4.1 / **4.2** (style-linked, FR-12 example) | ✅ |
| `multilevel-1-1-1.docx` | 1. / 1.1. / 1.1.1. / 1.1.2. / 1.2. / 2. / 2.1. | ✅ |
| `line-numbered-pleading.docx` | 1. / 2. / 3. / 4. … / 12. continuous across 4 section headings | ✅ |

**No divergence — the escalation trigger did NOT fire.** Every corpus doc's computed label equals Word's
displayed golden label. (`line-numbered-pleading.docx`'s LINE numbers remain out of scope — a WS-5 layout
artifact per design §5.5, task 050 — only its paragraph numbers are computed here.)

## FR-14 — Read-time only + R5 G3 coupling (RECORDED, per constraint)

WS-3 computes the label **as read**. The engine does NOT auto-renumber on edit within R4.5. Live
renumber-on-insert/delete (delete a clause → renumber, reflected in redline) is **R5 G3** (spec FR-14,
design §6). **G3 MUST build on THIS engine's shared numbering model — R5 must not fork it.** The engine is
deliberately a self-contained, deterministic replay of Word's algorithm over the 030 model, so the
edit-side renumber can reuse `NumberingComputationEngine` (re-run over the post-edit paragraph sequence)
rather than reimplement the counter/format/compose logic. Recorded here so the coupling survives to R5.

## Determinism (NFR-06)

Pure computation, no RNG, no I/O. Deeper-level resets remove dictionary keys (order-independent); the
label depends only on document-order inputs, never on hash-iteration order. A dedicated test builds each
corpus exemplar's projection TWICE and asserts identical computed-label sequences.

## Fallback honesty (no silent wrong legal number)

The formatter degrades unknown `w:numFmt` values to decimal rather than throwing. This is safe for the
corpus (all covered formats) and for the acceptance gate. It is NOT a "close enough" for a KNOWN legal
scheme — the five named schemes + decimal + bullet are computed exactly. If a FUTURE corpus/owner doc uses
an unsupported format (decimalZero, ordinalText, etc.), the decimal fallback could differ from Word; that
is an escalation-relevant gap to surface at that time (the model already warns on genuinely
outside-the-model constructs like unresolved `numStyleLink` / picture bullets via 030). No such doc exists
in the corpus today.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` — **0 errors** (pre-existing unrelated warnings only).
- `dotnet test --filter "FullyQualifiedName~Compose"` — **682 passed / 0 skipped / 0 failed**. Baseline
  before this task (030 notes) was 644 passed / **1 skipped** / 0 failed. The +38 = 24 numbering-exactness
  Theory cases flipped from skipped-to-live + 14 new engine unit tests; the 1 skipped is now 0.
- `dotnet test --filter "FullyQualifiedName~TextExactness|FullyQualifiedName~NumberingExactness"` —
  **32 passed** (8 text-exact, unchanged; 24 numbering-exact, now LIVE + green).
- Publish size (BFF Hygiene §10): compressed **47.52 MB** (same `Compress-Archive` tool as 030 for
  apples-to-apples) vs 030's post-task **47.52 MB** → **delta +0.00 MB**. Pure C# over the already-
  referenced `DocumentFormat.OpenXml`; no new package. Well under the ≤60 MB ceiling and the ~49.63 MB
  baseline. `dotnet list package --vulnerable` scope unaffected (no package change).

## Placement Justification (root CLAUDE.md §10/§11, `.claude/constraints/bff-extensions.md`)

- **Existing**: no numbering COMPUTATION existed on the read path — grep of the pre-031 builder found only
  `ResolveOrdered`'s bullet-vs-ordered bit and 030's MODEL reader (no counter/format/compose).
  `ComputedNumber` did not exist on `ParaIdMapEntry`. `ComposeDocumentRenderer` computes on the WRITE side.
- **Extension**: Yes — a new read-side capability INSIDE the existing `ComposeDocxProjectionBuilder`,
  running in the EXISTING single walk over the 030 model. Not a new service/abstraction/endpoint/package/
  DI registration. It is the read-side twin of the write-side renderer (task 033 proves agreement).
- **Cost-of-doing-nothing**: numbered clauses render "1." repeated on every interruption and heading-style
  numbers are dropped — the core legal-fidelity defect (NFR-02 release blocker). A legal reader cannot
  trust "Section 4.2" if the tool renders it "1." or drops it.
- `Services/Compose/` stays **pure** `byte[]`-in/projection-out (ADR-007/013): the engine touches only
  `DocumentFormat.OpenXml.Wordprocessing`/`.Packaging` types + primitives — no `Microsoft.Graph`, no
  AI-internal type. Tests are KEEP-path pure-domain over real in-memory `.docx` (ADR-038 — no
  `Mock<HttpMessageHandler>`/DI/ctor-null tests).
- **`/conflict-check`** must be run by the MAIN SESSION before the PR (subagent does not commit/PR):
  `Services/Compose/` overlaps `spaarkeai-compose-r1/r2/r3/r4` + `spaarke-ai-architecture-redesign-r2`.
  The only cross-file surface this task touched is `ParaIdMapEntry` (additive optional field) and the two
  test files.

## Files changed

- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs` — the
  `NumberingComputationEngine` (counters, formatters `ToLetters`/`ToRoman`, `lvlText` composition, isLgl,
  startOverride, deeper-level reset) + Build() Pass-1 wiring (compute label, attach to `ParaIdMapEntry`).
- `src/server/api/Sprk.Bff.Api/Services/Compose/ParaIdPreParser.cs` — `ParaIdMapEntry.ComputedNumber`
  (additive optional field) + doc comment.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeDocxProjectionBuilderTests.cs` — 14 engine tests
  (interrupted, lowerLetter+z→aa, upperLetter, lowerRoman, upperRoman, multi-level reset, sub-item depth
  "4.2(b)(iii)", legal isLgl→decimal, startOverride, style-linked headings, 3 corpus golden-label tests,
  determinism).
- `tests/integration/seam/Compose/ComposeReadFidelityHarnessSeamTests.cs` — flipped
  `GetCurrentComputedNumber` to read `ComputedNumber`; removed `[Skip]` from the numbering-exactness Theory
  (the NFR-02 acceptance proof).
