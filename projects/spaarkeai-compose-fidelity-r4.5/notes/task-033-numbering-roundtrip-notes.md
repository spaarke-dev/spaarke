# Task 033 — WS-3 round-trip agreement test: write-side renderer vs read-side computation (FR-15)

> Written by the task 033 sub-agent execution. Sub-agent write boundary: this file (under
> `projects/spaarkeai-compose-fidelity-r4.5/notes/`) is in-bounds; `TASK-INDEX.md` / `current-task.md` are NOT
> touched here — owned by the main session.

## Summary

Final WS-3 task. Authored the round-trip AGREEMENT seam test that drives the REAL born-in-editor write-side
author (`ComposeDocumentRenderer.SynthesizeDocument`, task 026/027) end-to-end into the REAL WS-3 read-side
computation (`ComposeDocxProjectionBuilder.Build` → `NumberingComputationEngine`, tasks 030/031), asserting
the read-side's computed labels equal the write side's authored intent.

**Result: 3 of 4 round-trip scenarios AGREE. One scenario — two separate ordered lists in the same document,
separated by an intervening paragraph — DIVERGES.** Per the task's escalation trigger and root CLAUDE.md
§6.5, this is surfaced, not patched. The failing test is left in the suite (intentional — "fails loudly"),
documented as **DEF-03** in `projects/spaarkeai-compose-fidelity-r4.5/notes/defer-issues.md`.

## What the round-trip authors + reads

New file: `tests/integration/seam/Compose/ComposeNumberingRoundTripSeamTests.cs`. Each test: (1) builds a
`ComposeContentModel` using the same block-builder pattern as `ComposeDocumentRendererTests.cs`, (2) calls
the REAL `ComposeDocumentRenderer.SynthesizeDocument` to get `.docx` bytes, (3) feeds those bytes to the REAL
`ComposeDocxProjectionBuilder.Build`, (4) reads every `ParaIdMap` entry's `ComputedNumber` in document order,
(5) asserts the sequence equals the write side's INTENDED labels (derived directly from the renderer's own
authored `lvlText` cascade text / `startOverride` semantics — not an independently hand-computed parallel
model, so the comparison is genuinely like-for-like).

Four scenarios, exercising what the renderer ACTUALLY authors (FR-27 keystone + list schemes):

| # | Scenario | Constructs exercised | Result |
|---|---|---|---|
| 1 | Multi-level style-linked heading cascade (7 headings, 3 levels, one level-0 increment that resets a deeper level) | `%1` / `%1.%2` / `%1.%2.%3` cascade, single `HeadingNumInstanceId=1` instance throughout, style-link via `w:pStyle` (FR-27) | ✅ AGREE |
| 2 | Single continuous ordered list (3 items, one `numId`, no interruption) | `%N.` per-level decimal scheme, direct `w:numPr` | ✅ AGREE |
| 3 | Two separate ordered lists broken by an intervening paragraph (renderer allocates a FRESH `w:num` instance + `w:startOverride=1` for list 2, per `RenderBlocks`' `currentOrderedNumId` reset) | Same `%N.` scheme, but TWO `numId` instances sharing ONE `abstractNumId` | ❌ **DIVERGE** — see below |
| 4 | Determinism: same model built twice (fresh paraId RNG each run) | Heading + ordered + bullet mix | ✅ AGREE (labels identical run-to-run; paraIds differ as expected) |

## The divergence (DEF-03) — root cause + which side is wrong

**Root cause**: `NumberingComputationEngine.Compute` (`ComposeDocxProjectionBuilder.cs` ~`:1347`) keys its
running counter by `(abstractNumId, level)` — **never by `numId`**. `InitialValue` only consults a
`numId`-scoped `w:startOverride` on the FIRST use of that `(abstractNumId, level)` key
(`_appliedStartOverrides.Add((numId, ilvl))`); once that key already exists in `_counters` (seeded by an
EARLIER, different `numId` sharing the same `abstractNumId`), the engine unconditionally increments — it
never re-checks whether the CURRENT paragraph's `numId` carries its own unconsumed `startOverride`.

`ComposeDocumentRenderer.RenderBlocks` (`:268-311`) allocates a brand-new `w:num` instance
(`plan.NewOrderedInstance()`) — with a level-0 `w:startOverride=1`
(`AddNumberingDefinitions`, `:542-548`) — every time `currentOrderedNumId` is reset to `null`, which happens
on ANY non-ordered-list block (heading, plain paragraph, table, bullet item) or an explicit
`block.StartsNewList`. This is not a contrived edge case: **any Compose-authored document containing two or
more separate numbered lists** (a completely ordinary document shape — e.g. two distinct numbered clause
sub-lists separated by explanatory prose) hits this exact pattern.

**Empirical proof** (`dotnet test --filter FullyQualifiedName~ComposeNumberingRoundTripSeamTests`):
five-block model `[Ordered("First A"), Ordered("First B"), Paragraph(...), Ordered("Second A", startsNewList),
Ordered("Second B")]` — write side intends `["1.", "2.", null, "1.", "2."]`; read side computes
`["1.", "2.", null, "3.", "4."]`. List 2 continues counting from list 1 instead of restarting.

**Which side is wrong**: the **write side is correct** and the **read side's counter model needs
reconciling**. Per ECMA-376, numbering counters are scoped to the numbering-definition INSTANCE (`numId`),
not the abstract definition (`abstractNumId`) — two independent `w:num` elements referencing the same
`w:abstractNum` have independent counters by construction; a `w:startOverride` on a fresh instance is the
standard OOXML/Word "Restart at 1" authoring idiom, exactly what `ComposeDocumentRenderer` emits. The
read-side engine's `(abstractNumId, level)`-only keying is a simplification that happens to be correct for
every corpus exemplar (031's own notes: "Every corpus exemplar uses a single numId per abstractNum, so this
is unambiguous for the corpus") but is NOT correct in general — this round-trip test is the first thing in
the project to exercise the multi-`numId`-per-`abstractNum` case, because the corpus never contained it and
031 was scoped/verified against the corpus, not against the write side's full authoring surface.

## Why NOT patched in this task

Task 033 is scoped as a test-authoring task (POML `<outputs>`: `tests/integration/seam/` only). Fixing
`NumberingComputationEngine`'s counter/reset model is a production-code change to the flagship WS-3 engine
that:
- needs its own design pass (does the fix key by `numId` outright — which could reintroduce the "restart via
  same-numId-continuation" corpus case's dependency on abstractNumId sharing across paragraphs correctly — or
  does it add a narrower "unconsumed startOverride forces a reset" rule that preserves the existing 24-case
  golden Theory unchanged?),
- must be re-verified against ALL 24 existing golden-Theory cases (NFR-02 acceptance) to avoid a regression,
- is exactly the kind of "surface, don't silently patch" case root CLAUDE.md §6.5 and this task's own
  `<escalation>` block require routing through explicit reconciliation rather than an in-task fix.

Per the task's binding instruction: *"Do NOT paper over a divergence... surface WHICH side is wrong (author
intent vs read computation) before any patch."* Done above. The failing test is LEFT in the suite (not
`[Skip]`-gated) so `dotnet test` visibly reports it red until a follow-up task reconciles the engine —
mirroring this project's own precedent of surfacing defects as visible failing/skip states (e.g. 030's
`NumberingExactness` Theory stayed `[Skip]`-gated until 031 fixed the underlying gap) rather than hiding them.

## Read-side capability the write side doesn't exercise

The write side (`ComposeDocumentRenderer`) only ever authors: decimal cascades (`Decimal` numFmt), never
`lowerLetter`/`upperLetter`/`lowerRoman`/`upperRoman`/`isLgl`; a fixed 9-level heading scheme and a fixed
9-level ordered/bullet scheme (no `w:lvlRestart` override, no full `w:lvlOverride` level replacement, no
`w:numStyleLink` chain). The read-side engine (031) supports ALL of these (letters/roman/legal/lvlRestart/
lvlOverride/numStyleLink) — proven against the REAL corpus fixtures in 031's own tests, not against the
renderer. This round-trip test therefore cannot (and does not attempt to) prove agreement on those
richer read-side constructs — they are validated elsewhere (031's corpus-golden tests), just never authored
by this specific write path. This is consistent with the corpus being LOADED real-world Word documents (which
use the richer schemes) while `ComposeDocumentRenderer` is the narrower born-in-editor authoring surface.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` — **0 errors** (23 pre-existing warnings, unchanged set; no
  production code touched by this task).
- `dotnet test --filter "FullyQualifiedName~ComposeNumberingRoundTripSeamTests"` — **3 passed, 1 failed**
  (the intentional DEF-03 divergence proof), 0 skipped.
- `dotnet test --filter "FullyQualifiedName~Compose"` — **691 passed / 0 skipped / 1 failed / 692 total**.
  Baseline before this task (032 notes) was 688 passed / 0 skipped / 0 failed. This task adds 4 new tests
  (+3 passed, +1 failed — the DEF-03 proof); **zero regressions** to any pre-existing test.
- `dotnet test --filter "FullyQualifiedName~TextExactness|FullyQualifiedName~NumberingExactness"` — **32/32
  passed**, unchanged from 031/032's baseline — the existing 24-case NFR-02 golden Theory is UNAFFECTED (this
  task's divergence is a gap the corpus never exercised, not a regression of a corpus-covered case).
- Publish-size (BFF Hygiene §10, root CLAUDE.md §10 bullet 4): `git diff --stat -- 'src/**' '*.csproj'` is
  **empty** — this task adds only a test file under `tests/**`, so publish size is provably **unaffected
  (delta +0.00 MB)** vs the ~49.63 MB baseline; no `dotnet publish` compressed re-measurement needed beyond
  confirming zero `src/`/`.csproj` diff (verified: `dotnet publish -c Release` still succeeds, 0 errors).
- No new NuGet package added — `dotnet list package --vulnerable` scope unaffected.
- `/conflict-check`: not run by this subagent (main session runs it before PR per project convention);
  `Services/Compose/` was NOT modified by this task (test-only change), reducing conflict surface to zero for
  this specific task's diff.

## Placement Justification (root CLAUDE.md §10/§11, `.claude/constraints/bff-extensions.md`)

- **Existing**: no test exercised the write-side renderer AND the read-side WS-3 computation TOGETHER in one
  round-trip — `ComposeDocumentRendererTests.cs` tests the renderer in isolation (OOXML shape assertions);
  `ComposeDocxProjectionBuilderTests.cs` (030/031) tests the read-side engine against hand-built/corpus
  `.docx` fixtures, never against the renderer's own output.
- **Extension**: Yes — a new seam test file at the established `tests/integration/seam/Compose/` KEEP path
  (ADR-038), driving the two EXISTING production components together. Not a new service/abstraction.
- **Cost-of-doing-nothing**: without this round-trip, a divergence between the write-side author's intent and
  the read-side computation (exactly DEF-03) would ship silently — a legal document authored in Compose with
  two numbered clause lists would display the WRONG numbers on next open, with no test anywhere catching it
  (the 24-case golden Theory only covers LOADED real-world documents, never renderer-authored ones).
- `Services/Compose/` untouched by this task — pure test addition, no `Microsoft.Graph`, no AI-internal type,
  no I/O beyond in-memory OOXML (ADR-007/013 N/A — no production code changed).

## Files changed

- `tests/integration/seam/Compose/ComposeNumberingRoundTripSeamTests.cs` (NEW) — 4 round-trip agreement
  tests: multi-level style-linked heading cascade (agree), single continuous ordered list (agree), two
  ordered lists separated by a paragraph (**DIVERGE — DEF-03**, left failing/red intentionally), determinism
  (agree).
- `projects/spaarkeai-compose-fidelity-r4.5/notes/defer-issues.md` — added **DEF-03** (this divergence, full
  root-cause + which-side-is-wrong analysis + recommended follow-up).

## Escalation (per task POML `<escalation>` + root CLAUDE.md §6.5)

🔔 **Numbering Model Divergence — Reconciliation Required (DEF-03)**

- **Construct**: two separate ordered (decimal) lists in one Compose-authored document, separated by an
  intervening non-list block.
- **Write side** (`ComposeDocumentRenderer`, correct per ECMA-376 instance-scoped counters): intends list 2
  to restart — `"1.", "2."`.
- **Read side** (`NumberingComputationEngine`, `ComposeDocxProjectionBuilder.cs`): computes list 2 as
  continuing list 1's count — `"3.", "4."`.
- **Proposed path**: reconcile the read-side counter model (a follow-up task, not this one) to recognize a
  `numId` carrying an unconsumed `w:startOverride` as a reset trigger for its `(abstractNumId, level)`
  counter, then re-verify against the 24-case golden Theory + this round-trip test before flipping the
  failing test green.
- **Not patched here**: 033 is scoped to test authoring; the fix belongs to the WS-3 engine's own change
  surface with its own regression proof. Filed as DEF-03; recommend promoting to a GitHub Issue + follow-up
  task at project wrap-up (or sooner, given the "any 2-numbered-list document" blast radius).
