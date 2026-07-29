# Task 040 — WS-4: extend the projection with per-paragraph reference fields (FR-16)

> Written by the task 040 sub-agent execution. Sub-agent write boundary: this file (under
> `projects/spaarkeai-compose-fidelity-r4.5/notes/`) is in-bounds; `TASK-INDEX.md` / `current-task.md` are
> owned by the main session and NOT touched here.

## Summary

Completes the per-paragraph reference field set FR-16 requires on `ParaIdMapEntry` / `ComposeDocxProjection`:
`computedNumber` (already present from task 031 — kept, not duplicated), plus three NEW additive fields —
`numberingLevel`, `listPath`, `headingLevel` — all populated from WS-3's already-computed numbering (tasks
031/032) in the SAME single document-order Pass-1 walk. No numbering is recomputed; this task only lifts
031's internal state onto the projection payload.

## Fields added (and how each is populated)

| Field | Type | Populated from | Null convention |
|---|---|---|---|
| `NumberingLevel` | `int?` | `ParagraphNumberingRef.Ilvl` (the paragraph's own `w:ilvl`) — the SAME value already emitted as `data-numbering-level` on the HTML (task 032) | `null` unless the paragraph resolved to a computed label (same gate as `ComputedNumber`) |
| `ListPath` | `IReadOnlyList<int>?` | NEW `NumberingComputationEngine.BuildOrdinalChain(numId, ilvl)` — the raw per-level counter chain (0..ilvl) `ComposeLabel`'s own `%n` substitution reads from, returned alongside the label from the SAME `Compute()` call so it can never diverge from `ComputedNumber` | `null` unless numbered (same gate) |
| `HeadingLevel` | `int?` | Existing `ComposeDocxProjectionBuilder.HeadingLevel(Paragraph)` helper (Heading1..Heading6 → 1..6) — moved into Pass 1 (previously only called from Pass 2/render); UNUSED `BuildContext ctx` parameter dropped since it was dead code and `BuildContext` doesn't exist yet at the Pass-1 point in the walk | `null` for a non-heading paragraph — INDEPENDENT of numbering (verified: the interrupting plain Heading1 in `nda-interrupted-clauses.docx` carries `HeadingLevel=1` with `ComputedNumber=null`) |

`ComputedNumber` (task 031) and `Index`/`ParaId`/`IsMinted` (pre-existing) are untouched — reconciled with
031/032 per the task brief, no duplication.

## The engine change

`NumberingComputationEngine.Compute(ParagraphNumberingRef)` changed return type from `string?` to a new
`NumberingComputationResult?` (`internal readonly record struct { string Label; IReadOnlyList<int> ListPath; }`).
The chain is computed in the SAME call, immediately after `ComposeLabel`, from the SAME counter state the
label was just composed from — a caller can never observe a chain that belongs to a later paragraph's
counters. `Compute()` was previously called with no direct test coverage (all 14 engine tests + 24 golden
Theories drive it end-to-end via `Build()` → `ParaIdMap.ComputedNumber`), so widening its return type
required no test-signature changes.

New private helper `NumberingComputationEngine.BuildOrdinalChain(numId, ilvl)` mirrors
`SubstituteLvlText`'s own `%n` counter-or-`w:start`-fallback logic exactly, for levels `0..ilvl`.

## Verified ground truth (via a throwaway diagnostic seam test, run once then replaced)

```
heading-style-numbering.docx
  idx=6 num=4    lvl=0 path=[4]     heading=1   (Heading1 "Confidentiality")
  idx=7 num=4.1  lvl=1 path=[4,1]   heading=2   (Heading2 "Purpose")
  idx=9 num=4.2  lvl=1 path=[4,2]   heading=2   (Heading2 "Confidentiality" — the FR-12 example)
multilevel-1-1-1.docx
  idx=3 num=1.1.1. lvl=2 path=[1,1,1] heading=null
  idx=6 num=2.      lvl=0 path=[2]     heading=null (level-1/2 reset)
nda-interrupted-clauses.docx
  idx=5  num=null lvl=null path=null heading=1   (interrupting Heading1 — proves HeadingLevel independence)
  idx=12 num=4.   lvl=0    path=[4]  heading=null (post-interruption, continuous — not restarted)
```

This confirms the design's worked examples (`"4.2"` → `numberingLevel=1`, `listPath=[4,2]`, `headingLevel=2`;
`"1.1.1"` → `listPath=[1,1,1]`) exactly, plus the independence invariant (a plain, unnumbered heading still
carries `HeadingLevel`).

## Tests added

`tests/integration/seam/Compose/ComposeReadFidelityHarnessSeamTests.cs` (KEEP path: `tests/integration/seam/**`,
ADR-038):

- `ReferenceFieldExemplars()` `[MemberData]` (9 rows) + `ReferenceFields_OnNumberedExemplars_ExposeLevelListPathAndHeadingLevel`
  `[Theory]` — paraId-keyed (not ordinal-only), asserting `NumberingLevel`/`ListPath`/`HeadingLevel` against
  hand-verified corpus values across `heading-style-numbering.docx` (incl. the FR-12 "4.2" example) and
  `multilevel-1-1-1.docx` (incl. the level-reset case).
- `ReferenceFields_OnUnnumberedParagraph_CarryConsistentNullTriple` `[Fact]` — the un-numbered
  `multilevel-1-1-1.docx` title paragraph carries `ComputedNumber`/`NumberingLevel`/`ListPath` all `null`
  together (never a fabricated partial value).
- `ReferenceFields_OnPlainUnnumberedHeading_PopulatesHeadingLevelWithoutFabricatingANumber` `[Fact]` — the
  FR-16 independence proof: the interrupting Heading1 in `nda-interrupted-clauses.docx` carries
  `HeadingLevel=1` while `ComputedNumber`/`NumberingLevel`/`ListPath` stay `null`.

No `Mock<HttpMessageHandler>`/DI-registration/ctor-null test added (ADR-038 bans). All new tests drive the
real `ComposeDocxProjectionBuilder` over real corpus `.docx` fixtures, same pattern as the existing
`NumberingExactness_*` Theory.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` (Debug + Release) — **0 errors** (23 pre-existing warnings,
  unchanged set). No `.csproj` diff (`git diff --stat -- '*.csproj'` empty).
- `dotnet test --filter "FullyQualifiedName~Compose"` — **705 passed / 0 skipped / 0 failed** (032's baseline
  was 688; +11 = 9 Theory cases + 2 Facts, this task's new coverage; 17 other passing tests in this filter
  belong to sibling suites already compiled into the same assembly and were unaffected).
- `dotnet test --filter "FullyQualifiedName~TextExactness|FullyQualifiedName~NumberingExactness"` —
  **32 passed** (8 text-exactness + 24 numbering-exactness), unchanged — this task did not touch the label
  computation, only surfaced additional data alongside it.
- `Spaarke.ArchTests` `ADR013_ComposeFacadeTests` (Tier-1 purity guard for `Services/Compose/`) — **passed**.
  `ADR007_GraphIsolationTests` has a **pre-existing, unrelated failure** (5 `Sprk.Bff.Api.Services.Communication.*`
  / `Infrastructure.Errors.*` / `Api.Office.Errors.*` types) — none in `Services.Compose`, confirmed present
  before this task's changes (`ADR007_GraphIsolationTests.cs` last touched by an unrelated repo-cleanup commit).
  Not in this task's scope to fix; flagged here for visibility only.
- Publish size (BFF Hygiene §10): compressed **47.52 MB** (`Compress-Archive`, same method as 030/031/032)
  vs 032's post-task **47.52 MB** → **delta +0.00 MB**. No new package; pure additive record fields + one
  engine return-type widen over the already-referenced `DocumentFormat.OpenXml`. Well under the ≤60 MB
  ceiling and the ~49.63 MB baseline.

## Escalation check (per POML)

**Did not fire.** All three new fields are ADDITIVE, nullable, with default values on the existing
`ParaIdMapEntry` positional record — every existing 3-arg (`IsMinted:` named) and 4-arg (`computedNumber`
positional) construction site at `ParaIdPreParser.cs:136,142` continues to compile and behave identically
(new fields default to `null`). `ComposeDocxProjection.SchemaVersion` remains `"compose-html-v1"` — no
consumer that deserializes the payload can break on new optional JSON properties by construction (additive
JSON contract). No serialization/version incompatibility surfaced.

## Placement Justification (root CLAUDE.md §10/§11, `.claude/constraints/bff-extensions.md`)

- **Existing**: `ParaIdMapEntry`/`ComposeDocxProjection` already carry `Index`/`ParaId`/`IsMinted` (task 010)
  and `ComputedNumber` (task 031) — grep of the pre-040 types confirms no `NumberingLevel`/`ListPath`/
  `HeadingLevel` field existed anywhere on the read path.
- **Extension**: Yes — fields added to the EXISTING `ParaIdMapEntry` record, populated inside the EXISTING
  single Pass-1 walk in `ComposeDocxProjectionBuilder.Build()`. `HeadingLevel`'s source helper
  (`ComposeDocxProjectionBuilder.HeadingLevel`) already existed (used by Pass 2 for the `<h#>` tag choice) —
  reused verbatim, not reimplemented, only its unused `ctx` parameter was dropped so it is callable earlier
  in the walk. `ListPath`'s source (`NumberingComputationEngine`'s counters) already existed inside task 031's
  engine — this task exposes it via a widened `Compute()` return, not a new computation.
- **Cost-of-doing-nothing**: without `NumberingLevel`/`ListPath`/`HeadingLevel` on the payload, tasks 041
  (persisted `paraId → number` map) and 042 (citation resolver, sub-item depth "4.2(b)(iii)" + contiguous
  ranges "Sections 4–7" per this project's locked decisions) would have no server-side source for the level
  hierarchy or heading outline — they would either re-parse the formatted label string (fragile, format-
  specific) or re-run numbering computation a second time (duplicating 031's engine, contradicting this
  task's explicit "does not recompute" scope).
- `Services/Compose/` stays pure `byte[]`-in/projection-out (ADR-007/013): the new fields are `int?`/
  `IReadOnlyList<int>?`/primitives; the new engine method (`BuildOrdinalChain`) touches only
  `DocumentFormat.OpenXml.Wordprocessing` model types and integers already in scope inside
  `NumberingComputationEngine` — no `Microsoft.Graph`, no AI-internal type. `ADR013_ComposeFacadeTests`
  (Tier-1 NetArchTest) verified green. WS-4 EXPOSES this data via the projection contract for a future
  consumer (041/042) to READ — nothing is injected INTO `Services/Compose/`.
- **`/conflict-check`** must be run by the MAIN SESSION before the PR (subagent does not commit/PR):
  `Services/Compose/` overlaps `spaarkeai-compose-r1/r2/r3/r4` + `spaarke-ai-architecture-redesign-r2`. This
  task's cross-cutting-visible surface is the `ParaIdMapEntry` record (additive fields) and
  `NumberingComputationEngine.Compute`'s return type (internal, no external caller besides `Build()`).

## Files changed

- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs` — Pass-1 loop populates
  `numberingLevel`/`listPath`/`headingLevel` alongside the existing `computedNumber`; `HeadingLevel(Paragraph)`
  signature simplified (dropped unused `ctx` param, now callable from Pass 1); `NumberingComputationEngine.Compute`
  widened to return the new `NumberingComputationResult` (`Label` + `ListPath`); new `BuildOrdinalChain` helper.
- `src/server/api/Sprk.Bff.Api/Services/Compose/ParaIdPreParser.cs` — `ParaIdMapEntry` gained
  `NumberingLevel`/`ListPath`/`HeadingLevel` (additive, defaulted, non-breaking) + extended doc comments.
- `tests/integration/seam/Compose/ComposeReadFidelityHarnessSeamTests.cs` — new `[Theory]` +2 `[Fact]`s for
  the FR-16 reference-field population (KEEP path, no banned test shapes).

## Note for 041 (persisted map) / 042 (citation resolver)

The full per-paragraph reference set now lives on `ComposeDocxProjection.ParaIdMap[i]` — each
`ParaIdMapEntry` carries `Index`, `ParaId`, `IsMinted`, `ComputedNumber`, `NumberingLevel`, `ListPath`,
`HeadingLevel`. This is the ONE map both 041 (persist `paraId → number` to the projection payload — already
done, here — AND the session ledger, per this project's locked WS-4 decision) and 042 (the citation resolver,
sub-item depth + contiguous ranges) should read from. Do NOT re-parse `ComputedNumber` strings to recover
level/chain data — `NumberingLevel`/`ListPath` are already the parsed, structured form. A bullet-format
paragraph carries a non-numeric `ComputedNumber` (the glyph) but STILL carries a numeric `ListPath` (the raw
counter chain, per `NumberingComputationEngine`'s bullet handling) — 042 should treat a bullet's `ComputedNumber`
as non-citable per this project's existing "non-numeric marker = non-citation" convention (see task 031/032
notes), independent of `ListPath` being present.
