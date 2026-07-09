# Task 013 — Compute TL;DR factual scaffolding deterministically; LLM composes prose only

> Task: `013-deterministic-tldr-scaffolding.poml` (Phase A: Accuracy core)
> Files: `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs`,
> `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs`,
> `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingNarrator.cs`,
> `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Narrators/DailyBriefingCollectorTests.cs`,
> `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Narrators/DailyBriefingNarratorEntityLinkTests.cs`,
> `projects/spaarke-daily-update-service/notes/playbooks/actions/brief-narrate-tldr.action.json`

## Placement decision (root CLAUDE.md §10 bullet 1)

Change stays entirely inside the existing coded-composite pipeline (`DailyBriefingCollector` +
`DailyBriefingNarrator`, ADR-039) — no new service, no new DI registration, no new endpoint, no
new orchestration node. `DailyBriefingCompositeService.cs` required **zero changes**: its
dispatch (`collect → workflow.ExecuteAsync → ledger → render/email`) is already generic and
carries whatever `DailyBriefingNarrateRequest` it's given straight through to the workflow.

## What changed

- **New DTOs** (`Api/Ai/DailyBriefingEndpoints.cs`): `TldrFactsDto` (totalNotificationCount,
  categoryCounts[], priorityItemCount, keyDates[], recordNames[]) + `TldrKeyDateDto`
  (recordName + date). `DailyBriefingNarrateRequest` gets a new optional `TldrFacts` field.
- **New computation** (`DailyBriefingCollector.BuildTldrFacts`, `internal static`): a pure
  function over an already-built `DailyBriefingNarrateRequest` view model. Computes:
  - `totalNotificationCount` / `categoryCounts` / `priorityItemCount` — pass-through of the
    request's own already-deterministic aggregates (reused, not recomputed).
  - `keyDates[]` — sourced from `PriorityItems` due dates only (already the curated top-N most
    urgent items); capped at `TldrFactsMaxKeyDates` (6).
  - `recordNames[]` — deduplicated, capped set (`TldrFactsMaxRecordNames`, 20) of priority-item
    titles + per-channel regarding/record names, so a large channel (up to 50 rows) cannot dump
    every record name into the TL;DR call (ADR-015 data-minimization / aggregation constraint).
  - `DailyBriefingCollector.BuildNarrateRequest` now stamps `TldrFacts` onto every request it
    builds (`request with { TldrFacts = BuildTldrFacts(request) }`).
- **`DailyBriefingNarrator.NarrateAsync`**: the TL;DR LLM call's `inputPayload` is now
  `req.TldrFacts ?? DailyBriefingCollector.BuildTldrFacts(req)` — i.e. the deterministically-
  computed scaffolding, not the previous raw `{ categories, priorityItems, channels,
  totalNotificationCount }` dump (which included every item's id/body/priority/regarding/
  sourceEntityType/createdOn across every channel — up to 300 rows' worth of fields). The
  fallback computation is still pure C#, never delegated to the LLM; it exists so callers that
  build a `DailyBriefingNarrateRequest` directly (the legacy `/narrate` leg with a caller-
  supplied payload, or a test driving `NarrateAsync` in isolation) still get ground-truth facts.
  Both call sites (collector, narrator fallback) reach the exact same `BuildTldrFacts`
  implementation — one source of truth for what counts as a "fact".
- **BRIEF-NARRATE-TLDR Action JPS mirror** (`brief-narrate-tldr.action.json`, `$version` 1→2):
  instruction rewritten to state the input is deterministically-computed ground truth and the
  model's job is prose composition + prioritization ONLY — "you are a writer, not a counter".
  Added an explicit R5 grounding-rule constraint ("use ONLY the counts in
  totalNotificationCount/categoryCounts/priorityItemCount, the dates in keyDates[], and the
  names in recordNames[]... never compute, estimate, or infer"). Input/examples updated to the
  new `TldrFactsDto` shape. Output schema (`summary`/`keyTakeaways`/`topAction`/`categoryCount`/
  `priorityItemCount`) is UNCHANGED — only the input contract changed, so
  `Sync-BriefNarrateOutputSchemas.ps1` (which syncs `sprk_outputschemajson`) needs no changes.
- Existing entity-name allow-list scrub (`BuildAllowList` / `_scrubber.Scrub`) is **unchanged** —
  it still computes its allow-list from the full `req` (broader than the smaller `RecordNames`
  set shown to the LLM), so it remains a superset safety net independent of what the prompt
  hints the model toward.

## Follow-up NOT done in this task (flagged, not silently skipped)

The BRIEF-NARRATE-TLDR Dataverse row's live `sprk_systemprompt` (deployed via R4 task 006 MCP
`create_record`, id `ce299eb4-fc70-f111-ab0e-7ced8ddc4cc6` per
`Sync-BriefNarrateOutputSchemas.ps1`) was **not** PATCHed to match the updated mirror JSON in
this task — the task's VERIFY section scopes to `dotnet build`/`test`/`publish` only, and step 3
asks only to "record the prompt change in notes" (done above). Until a follow-up deploy step
PATCHes `sprk_systemprompt` on the live row, the deployed instruction text still describes the
pre-task input shape while the runtime code now sends the new `TldrFactsDto` shape — this does
not break anything functionally (the runtime payload is independently serialized and appended
under a literal `## Input` header regardless of what the stored instruction text says), but the
live model won't benefit from the strengthened R5 grounding-rule wording until synced. Recommend
a small follow-up script (mirroring `Sync-BriefNarrateOutputSchemas.ps1`'s PATCH pattern, but for
`sprk_systemprompt` instead of `sprk_outputschemajson`) before/alongside task 016 (operator UAT).

## Test changes (root CLAUDE.md §10 bullet 6 / bff-extensions.md §F)

- **`DailyBriefingCollectorTests.cs`**:
  - `CollectAsync_AttachesTldrFacts_MatchingTheRequestsOwnDeterministicViewModel` — extends the
    existing live-mocked `CollectAsync` fixture to assert `request.TldrFacts` is non-null and its
    counts equal `request.Categories`/`TotalNotificationCount`/`PriorityItems.Length` exactly —
    proves the collector wiring end-to-end.
  - New `DailyBriefingTldrFactsTests` class — pure unit tests of `BuildTldrFacts` (no Dataverse
    I/O): a single-category fixture, a multi-category fixture (3 categories, 2 dated priority
    items, asserts `KeyDates` match verbatim), and a large-channel fixture (30 items) asserting
    `RecordNames` stays capped at `TldrFactsMaxRecordNames` instead of dumping every record —
    the direct proof for "aggregate, don't dump" (ADR-015).
- **`DailyBriefingNarratorEntityLinkTests.cs`**:
  - `BuildBoundaryMocks` extended with an optional `onTldrPrompt` callback so tests can capture
    the exact prompt string sent to the (Strict-mocked) `IOpenAiClient`.
  - New test `NarrateAsync_TldrLlmCallReceivesOnlyDeterministicFacts_MatchingTheRequestsViewModel`
    — captures the TL;DR call's actual `## Input` JSON section, deserializes it back into
    `TldrFactsDto`, and asserts its counts/dates/names equal the request's own deterministic view
    model verbatim. Also asserts the prompt does NOT contain `"sourceEntityType"` (a raw
    per-channel item field) — the negative half of "facts are supplied, not requested": grep of
    the captured prompt proves the raw item dump never reaches the LLM.

## Build + test results

```
dotnet build src/server/api/Sprk.Bff.Api/
  → Build succeeded. 0 Error(s). (18 pre-existing warnings, none introduced by this task)

dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --no-build \
  --filter "FullyQualifiedName~Narrators|FullyQualifiedName~Workflows"
  → Passed! Failed: 0, Passed: 39, Skipped: 0, Total: 39
  (34 pre-existing + 5 new: 1 CollectAsync wiring test, 3 BuildTldrFacts pure-function tests,
   1 narrator prompt-capture test)

dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --no-build \
  --filter "FullyQualifiedName~DailyBriefing"
  → Passed! Failed: 0, Passed: 51, Skipped: 0, Total: 51
```

## Publish-size verification (root CLAUDE.md §10 bullet 4)

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
```

- Compressed (`zipfile`, level 9), **incl. PDBs**: **44.87 MB**
- Compressed, **excl. PDBs**: **44.17 MB**
- Baseline (root CLAUDE.md §10, 2026-07-08): ~49.63 MB incl. PDBs / 45.87 MB excl. PDBs
- **Delta incl. PDBs: −4.76 MB** · **Delta excl. PDBs: −1.70 MB**
- Both deltas are DECREASES and well under the +5 MB single-task escalation threshold. As task
  010's note observed, PDB-generation variance between builds means the incl-PDB delta shouldn't
  be read as "this task shrank the binary" for a change that only adds ~2 small DTOs + one
  method + JSON prompt-text edits (no `.csproj` changes — confirmed via `git status`); the
  excl-PDB delta is the more apples-to-apples number and is still a decrease, consistent with
  other tasks having landed on this branch since the 49.63 MB baseline was recorded (this
  worktree shows concurrent modifications to `Spaarke.DailyBriefing.Components/**` from another
  in-flight session — see Coordination note below).

## CVE check (root CLAUDE.md §10 bullet 5)

```
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

Result: 1 HIGH-severity advisory — `Microsoft.Kiota.Abstractions 1.21.2`
(https://github.com/advisories/GHSA-7j59-v9qr-6fq9). **Pre-existing, not introduced by this
task** — zero `.csproj` changes (confirmed via `git status`). Same finding already recorded by
tasks 010/030.

## Coordination note — concurrent worktree activity (not part of this task's deliverable)

`git status` at task completion shows `src/client/shared/Spaarke.DailyBriefing.Components/src/
components/{ActivityNotesSection.tsx, NarrativeBullet.tsx, NarrativeCitedText.tsx}` as modified.
**None of these files were touched by this task** — this task's scope explicitly excludes the
client component library (owned by the parallel task 011 per the dispatch instructions). This is
the same "another session working concurrently in this worktree" pattern task 010 flagged for
task 033; here it is task 011 (or a related client-side task) modifying `Spaarke.DailyBriefing.
Components` while this task ran. Flagging for the main session to confirm intentional/expected,
consistent with current-task.md's own note that parallel-wave dispatch was in flight.

## Deviations from the POML's suggested step sequence

- Step 3 ("Update the BRIEF-NARRATE-TLDR system prompt... catalog Action, BA editor/MCP,
  mirror-first") — only the **mirror-first source file** was updated; the live Dataverse row was
  intentionally NOT PATCHed via MCP in this task run (see "Follow-up NOT done" section above).
  This is a scope-conservative choice: the task's VERIFY section is scoped to dotnet build/test/
  publish, and step 3 itself only asks to "record the prompt change in notes" — a live catalog
  write felt like it belonged to a deliberate, separately-reviewed deploy step rather than a
  side effect of a BFF code task. Flagging explicitly rather than silently deploying or silently
  skipping.
- No other deviations. `DailyBriefingCompositeService.cs` required no changes (confirmed by
  inspection, consistent with task 010's finding that its dispatch is fully generic).
