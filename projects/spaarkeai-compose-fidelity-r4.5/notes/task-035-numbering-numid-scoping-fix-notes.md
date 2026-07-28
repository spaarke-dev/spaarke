# Task 035 — WS-3 numbering counter numId-scoping fix (DEF-03 / NFR-02 release blocker)

> Written by the task 035 sub-agent. Write boundary: this file (under
> `projects/spaarkeai-compose-fidelity-r4.5/notes/`) is in-bounds; `TASK-INDEX.md` / `current-task.md`
> are owned by the main session and NOT touched here.

## Summary

Fixed the NFR-02 numbering-exactness bug that task 033's round-trip test caught (DEF-03). The read-side
`NumberingComputationEngine` (`ComposeDocxProjectionBuilder.cs`, task 031) keyed its running counter by
`(abstractNumId, level)`. Per ECMA-376 a numbering counter is scoped to the numbering-definition
**instance** (`w:num` / `numId`), NOT the shared abstract definition (`w:abstractNum` / `abstractNumId`):
two independent `w:num` referencing one `w:abstractNum` have independent counters. The write side
(`ComposeDocumentRenderer`) authors exactly this — every ordered list broken by a non-list block gets a
fresh `w:num` instance + a level-0 `w:startOverride=1` (the standard Word "Restart at 1" idiom). Because
the read side ignored `numId`, the second list continued the first's count ("3., 4." instead of "1., 2.").

**The fix**: re-key `_counters` from `(abstractNumId, level)` to `(numId, level)` throughout the engine.

## Exact change (counter-key / reset)

`_counters` field type changed `Dictionary<(int AbstractNumId, int Level), int>` → `Dictionary<(int NumId, int Level), int>`. Consistently updated all counter access sites:

| Site | Before | After |
|---|---|---|
| `Compute` increment/init key | `(abstractNumId, ilvl)` | `(numId, ilvl)` |
| `ResetDeeperLevels` filter | `k.AbstractNumId == abstractNumId` | `k.NumId == numId` (signature gained `numId`; still passes `abstractNumId` to `ShouldRestart` because `w:lvlRestart` is defined on the shared abstractNum level) |
| `ComposeLabel` no-template fallback read | `(abstractNumId, ilvl)` | `(numId, ilvl)` |
| `SubstituteLvlText` `%n` cross-level read | `(abstractNumId, refLevel)` | `(numId, refLevel)` |

`InitialValue` + `_appliedStartOverrides` (numId-scoped already) are unchanged: a fresh `numId` has no
counter key yet, so its first paragraph goes through `InitialValue`, which applies that numId's
`w:startOverride` (once) or the level's `w:start`.

## Restart vs continue — the crux, exactly right

- **Restart** ("two numbered lists separated by prose"): the write side allocates a **fresh `numId`** +
  `w:startOverride=1`. Fresh numId ⇒ no `(numId, level)` key ⇒ `InitialValue` seeds it at 1 ⇒ list restarts.
- **Continue** ("one list interrupted by a heading/table then resumed"): the **same `numId`** resumes ⇒
  its live `(numId, level)` counter persists across the interruption ⇒ list continues N+1.

The distinguishing signal is the `numId` (+ its startOverride), exactly as the write side uses it.

## Why the 24-case golden Theory is provably unchanged

Every corpus doc uses a **single `numId` per `abstractNum`** (031's own notes state this). Within any
such document `numId ↔ abstractNumId` is a bijection, so keying by `numId` is behaviorally identical to
keying by `abstractNumId` there — the corpus results are bit-identical. The only case where the two
keyings differ is two `w:num` over one `w:abstractNum`, which the corpus never contained but the write
side routinely authors. Verified empirically: 24-case numbering-exactness Theory GREEN, text-exactness
8/8 GREEN, all 031 engine unit tests GREEN.

## New tests (permanent coverage for the corpus blind spot)

`tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeDocxProjectionBuilderTests.cs`:
1. `Compute_TwoNumIdsSharingOneAbstractNumWithStartOverride_SecondListRestartsAt1` — the exact DEF-03
   shape at the engine unit level; asserts `["1.","2.",null,"1.","2."]`.
2. `Compute_TwoIndependentNumIdsInterleaved_EachMaintainsItsOwnCounterIndependently` — numId 1, numId 1,
   numId 2, numId 2, numId 1 → `["1.","2.","1.","2.","3."]`; a single test guarding BOTH restart
   (numId 2 independent) AND continue-within-a-numId (numId 1 resumes at 3).

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` — **0 errors** (23 pre-existing warnings, unchanged set).
- `dotnet test --filter "FullyQualifiedName~Compose"` — **694 passed / 0 skipped / 0 failed** (was
  691/0/1 at task 033 — the +1 red round-trip flips green, +2 new engine tests).
- `RoundTrip_TwoOrderedListsSeparatedByAParagraph_...` — RED→GREEN by fixing the ENGINE (assertion
  untouched).
- Golden gates: `NumberingExactness` (24) + `TextExactness` (8) + `ComposeNumberingRoundTripSeamTests`
  (4) = **36/36 passed**.
- Publish size (BFF Hygiene §10 bullet 4): **46.16 MB compressed** (`Compress-Archive`), well under the
  ≤60 MB ceiling; **~0 delta** — pure C# logic, no new package, no new type (within measurement-tool
  variance of the ~47.5 MB / ~49.63 MB baseline).
- `dotnet list package --vulnerable` scope unaffected (no package change).

## Placement Justification (root CLAUDE.md §10/§11, `.claude/constraints/bff-extensions.md`)

- **Existing**: the numbering computation already lives in `NumberingComputationEngine` inside
  `ComposeDocxProjectionBuilder` (task 031). This task MODIFIES that existing engine — no new service /
  abstraction / interface / endpoint / DI registration / package (root §11 does not require a new-component
  justification for modification-only work).
- **Extension**: a targeted counter-key correction inside the existing engine, in the existing single
  Pass-1 walk. Not a new surface.
- **Cost-of-doing-nothing**: any Compose-authored (born-in-editor) legal document containing 2+ separate
  ordered lists mis-numbers the 2nd+ list on every subsequent read/open (e.g. two numbered clause lists
  separated by prose read "3., 4." instead of "1., 2.") — a concrete NFR-02 release-blocker fidelity defect.
- `Services/Compose/` stays **pure** `byte[]`-in/projection-out (ADR-007/013): the engine touches only
  `DocumentFormat.OpenXml.Wordprocessing`/`.Packaging` types + primitives — no `Microsoft.Graph`, no
  AI-internal type, no I/O. Tests are KEEP-path pure-domain over real in-memory `.docx` (ADR-038 — no
  `Mock<HttpMessageHandler>`/DI/ctor tests).
- `/conflict-check` must be run by the MAIN SESSION before the PR (subagent does not commit/PR):
  `Services/Compose/` overlaps `spaarkeai-compose-r1/r2/r3/r4` + `spaarke-ai-architecture-redesign-r2`.

## Files changed

- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs` — `_counters` re-keyed
  `(abstractNumId, level)` → `(numId, level)`; `ResetDeeperLevels` gained a `numId` param and filters by
  numId; `ComposeLabel` + `SubstituteLvlText` counter reads re-keyed by numId; doc comments corrected.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeDocxProjectionBuilderTests.cs` — 2 new engine
  unit tests for the multi-numId-per-abstractNum case.
- `projects/spaarkeai-compose-fidelity-r4.5/notes/defer-issues.md` — DEF-03 disposition → ✅ RESOLVED.
