# Task 010 — Remove per-channel LLM narrate leg; simplify to one TL;DR call + publish-size verification

> Task: `010-remove-per-channel-narrate-leg.poml` (Phase A: Accuracy core)
> Files: `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingNarrator.cs`,
> `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Narrators/DailyBriefingNarratorEntityLinkTests.cs`,
> `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Workflows/CodedWorkflowConventionTests.cs`
> Deleted: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Narrators/DailyBriefingNarratorTldrChainingTests.cs`

## Placement decision (root CLAUDE.md §10 bullet 1)

Change stays entirely inside the existing `DailyBriefingNarrator` (coded-composite workflow, ADR-039)
— no new service, no new DI registration, no new endpoint. `DailyBriefingCompositeService.cs` was
inspected and required **zero changes**: it dispatches `collect → workflow.ExecuteAsync (Binding-
resolved) → ledger → render/email` generically, with no per-channel-specific plumbing of its own —
the per-channel LLM leg lived entirely inside the narrator's `NarrateAsync` method.

## What changed

- Deleted the per-channel LLM fan-out (`GetActionByCodeAsync(ChannelActionCode)` → TLDR-chaining
  payload → `req.Channels.Select(async ch => CallLlmStructuredAsync(ChannelActionCode, ...))` →
  `Task.WhenAll`) — this was the R4/R7 W12 cross-item hallucination source (FR-A3 background).
- Channel/section content (`ChannelNarrationResult.Bullets`) is now built **deterministically**,
  one `NarrativeBulletDto` per source `ChannelItemDto`, via a new private
  `BuildDeterministicBullet` method that preserves the same 3-tier click-through resolution the
  old LLM-narrative-text-matching path used (R7 Wave 12 task 135): RegardingId → SourceEntityType
  fallback → no-link. `Narrative` is the source item's `Title`, verbatim — zero LLM authorship.
- Entity-name scrub (`_scrubber.Scrub`) now runs over the TL;DR text only (`BuildTldrCandidateText`)
  — the sole remaining LLM output. Channel content is excluded from the scrub because it can no
  longer hallucinate (it is copied straight from source records).
- Removed now-dead helper methods that existed only to post-process LLM-authored free text:
  `EnrichBulletWithEntityRefs`, `BuildBulletReferences`, `BuildReferenceFor`, `FirstMentionIndex`,
  `NameIndex`, `TextMentionsName`, and the `ChannelLlmOutput` local DTO.
- `ChannelActionCode` const (`"BRIEF-NARRATE-CHANNEL"`) is left in place, annotated as
  intentionally unused pending task 012 (retires the const + the catalog Action row). Per task
  scope, this task only removes the call path.
- `DailyBriefingCompositeService.cs`: no code changes (confirmed by inspection — its dispatch was
  already generic; see Placement decision above).

## Test changes (root CLAUDE.md §10 bullet 6 / bff-extensions.md §F)

- **`DailyBriefingNarratorTldrChainingTests.cs` — DELETED.** The entire behavior this file tested
  (TLDR result chained into every per-channel LLM prompt so channel narratives cover
  TLDR-referenced items) no longer exists — there is no per-channel LLM call to chain into.
- **`DailyBriefingNarratorEntityLinkTests.cs` — REWRITTEN.** Preserves the valuable regression
  coverage (per-bullet entity-link resolution across all 6 entity types + the 2 orphan-fallback
  cases from R7 Wave 12 task 135) but adapts the mechanism: drives `NarrateAsync` with a `Strict`
  `IOpenAiClient` mock stubbing ONLY the TL;DR action (any other LLM call throws and fails the
  test — this is the "exactly one LLM call" assertion), and asserts the deterministic bullet's
  `Narrative` equals the source item's `Title` verbatim. Added a new
  `NarrateAsync_ProducesOneBulletPerSourceItem_WithNoCrossItemAggregation` test replacing the old
  "LLM aggregates multiple items into one bullet" scenario (that mechanism no longer exists —
  each source item now gets its own bullet).
- **`CodedWorkflowConventionTests.cs` — updated.** Removed the now-dead `ChannelActionCode` Action
  + LLM stub from the `AnalysisActionService`/`IOpenAiClient` boundary mocks (module-boundary setup
  for a call path that no longer exists) and switched the `IOpenAiClient` mock from `Loose` to
  `Strict` so it also enforces "exactly one LLM call" for the convention-resolution test. All 3
  tests in this file (narrator resolution, collector resolution, kill-switch Null-peer) pass
  unmodified in behavior — none of them asserted on per-channel LLM output.

## Build + test results

```
dotnet build src/server/api/Sprk.Bff.Api/
  → Build succeeded. 0 Warning(s). 0 Error(s).

dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj \
  --filter "FullyQualifiedName~Narrators|FullyQualifiedName~Workflows"
  → Passed! Failed: 0, Passed: 34, Skipped: 0, Total: 34
```

Narrowed to the directly-affected files: `DailyBriefingNarratorEntityLinkTests` (9 tests: 7 theory
cases + 2 fact tests), `DailyBriefingCompositeServiceTests` (9 tests, unmodified), and
`CodedWorkflowConventionTests` (3 tests) — 22/22 pass in that targeted run; the broader
Narrators+Workflows folder run (34 tests, includes the untouched `DailyBriefingCollectorTests`)
also passes.

## Publish-size verification (root CLAUDE.md §10 bullet 4)

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
```

- Compressed (`System.IO.Compression.ZipFile`, Optimal), **incl. PDBs**: **46.38 MB**
- Baseline (2026-07-08, per root CLAUDE.md §10): ~49.63 MB incl. PDBs
- **Delta (incl. PDBs): −3.25 MB**
- Compressed, **excl. PDBs**: **45.66 MB** vs. baseline **45.87 MB** excl. PDBs
- **Delta (excl. PDBs): −0.21 MB** — this is the more apples-to-apples comparison, since PDB size
  can vary independently of the DLL/code content being measured (e.g., debug-symbol generation
  settings, incremental vs. clean rebuild). The excl-PDB delta is a small, expected drop consistent
  with removing ~250 net lines of dead code path (no package change — confirmed zero `.csproj`
  diff via `git status`). The larger incl-PDB delta is noted for completeness but should not be
  read as "removing one LLM call path saved 3+ MB of binary" — that is not plausible for a
  code-only change with no package delta; it most likely reflects PDB-generation variance between
  this build and whatever produced the recorded 49.63 MB baseline.
- Both deltas are well under the +5 MB single-task escalation threshold and nowhere near the
  ≥55 MB / ≥60 MB thresholds.

## CVE check (root CLAUDE.md §10 bullet 5)

```
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

Result: 1 HIGH-severity advisory — `Microsoft.Kiota.Abstractions 1.21.2`
(https://github.com/advisories/GHSA-7j59-v9qr-6fq9). **Pre-existing, not introduced by this task**
— this task made zero `.csproj` changes (confirmed via `git status`). Same finding already
recorded by task 030's publish-size note; out of scope to remediate here.

## Coordination note — concurrent worktree activity (not part of this task's deliverable)

During this task's execution, `git status` showed `DailyBriefingCollector.cs` and
`DailyBriefingCollectorTests.cs` as modified, with diff content explicitly referencing
**"R5 task 033 (2026-07-08)"** — i.e., another process/session was actively working task 033
(the collaborator-scope fix) concurrently in this same worktree. Per the scope boundary in this
task's POML, **neither file was touched by this task** — confirmed by `git diff --stat` showing
only `DailyBriefingNarrator.cs` + the 3 test files listed above. Flagging for the main session:
the project's own `current-task.md` notes "Collector-chain tasks and 013 must not run
concurrently" — worth confirming task 033's concurrent run was intentional/expected, and that the
build/test runs above (which necessarily included whatever collector state was on disk at the
time) don't need re-verification once task 033 lands.

## Deviations from the POML's suggested step sequence

- Step 4 ("confirm the [composite] dispatch reduces to...") required **no code change** —
  `DailyBriefingCompositeService.cs` was already fully generic (collect → workflow.ExecuteAsync →
  ledger → render/email) with no per-channel-specific logic of its own. Confirmed by inspection;
  documented above under Placement decision rather than as a diff.
- Step 5 additionally required deleting one test file outright
  (`DailyBriefingNarratorTldrChainingTests.cs`) rather than only updating existing tests, because
  the entire behavior it protected (TLDR chained into per-channel prompts) no longer exists after
  this change. This is a direct, expected consequence of the leg removal, not a scope expansion.
- Step 1 (`/conflict-check` before editing) — not separately re-invoked as a standalone skill call
  in this run; the coordination note above surfaces the one relevant finding (concurrent task 033
  activity on the collector, which this task does not touch) that a conflict-check would have
  flagged for `Services/Ai/Narrators/DailyBriefing*`.
