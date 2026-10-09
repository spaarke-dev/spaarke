# Task 082 (space-bearing matter-number tokenizer repair) — deviations from the POML, recorded per step 7

> spec FR-43; design.md §5 In-item; ADR-045.

## 1. The POML's `<relevant-files role="modify">` names the wrong directory

The POML cites `src/server/api/Sprk.Bff.Api/Services/RecordMatching` for the file to modify. That directory
exists (`IRecordMatchService.cs`, `RecordMatchService.cs`, `DataverseIndexSyncService.cs`,
`SearchIndexDocument.cs`) but has nothing to do with the Association Engine or `WellFormedTokenPattern`. The
actual tokenizer lives in `src/server/api/Sprk.Bff.Api/Services/Communication/Engine/Rungs/
IdentifierReverseLookupRung.cs` (confirmed by grep for `WellFormedTokenPattern` and cross-referenced against
`notes/mvp-technical-spec.md` §17.5(a) and `design.md`'s own table entry for this repair, both of which name
`IdentifierReverseLookupRung`). Modified that file instead; did not touch `Services/RecordMatching/`.

## 2. A first draft of the regex was ambiguous and broke an existing passing test — caught, fixed, documented

The directional step 3 instruction ("allow optional whitespace around the separator, without admitting
arbitrary prose") was implemented in two iterations, not one:

- **Draft 1**: allowed the alpha prefix to be followed by *any* additional space-separated alpha word
  (unbounded in content, capped only at one extra word). Running the full
  `tests/unit/Sprk.Bff.Api.Tests` test file after this draft **failed a pre-existing passing test**
  (`IdentifierReverseLookupRungTests.Fr12_NewRecordFraming_ReferencedIdentifier_CappedSubThreshold_NotAutoFiledAlone`).
  The subject `"This is a new litigation matter related to MAT-123"` tokenized as `"to MAT-123"` instead of
  `"MAT-123"` — the connector word "to" was absorbed as a bogus first prefix word, pulling the real 3-letter
  prefix "MAT" in as its "second word," so the extracted token no longer equaled the stored field value and
  the exact-match reverse lookup silently failed.
- **Draft 2 (shipped)**: restricts the second prefix word to *exactly one letter* (matching the real
  "Form"/"D" shape in the repro). A single letter can never stand alone as a prefix word (the existing `{2,}`
  floor exists for exactly that reason), so it can never be "stolen" by an unrelated connector the way a 2+
  letter word can. Re-ran the full filtered test set: 66/66 pass, including the previously-broken Fr12 test
  and a new permanent regression-guard row pinning the exact failing subject in
  `WellFormedTokenPatternCostDeltaTests.Corpus()`.

Full write-up: `notes/tokenizer-cost-delta.md` §3.1. This is reported as a deviation because it means the
task's "two deliberate, narrow changes" became **three** (whitespace tolerance, a *constrained* single-letter
second word, and the digit-in-body requirement) — a materially narrower generalization than the first attempt,
discovered only by actually running the existing test suite rather than reasoning about the regex in the
abstract.

## 3. The measurement method is a curated representative corpus, not live tenant traffic

The POML's step 2/4 ask to "measure the current query count per inbound communication on a representative
sample" and "re-measure... and record the delta." No live Dataverse communications were read (task
constraints forbid deploying or touching Dataverse data/metadata, and this worktree doesn't have a live
tenant sample readily available as a safe, reproducible input). Instead, a 35-message curated corpus was
constructed spanning realistic subject-line shapes (well-formed controls, the repro, ordinary hyphenated
English prose, dates, bare-numeric, no-token conversational subjects) and both the frozen pre-task pattern and
the shipped pattern were run against it via `System.Text.RegularExpressions` with production-matching
options, then re-verified through the rung's actual public surface as a durable xUnit theory. This is a
measurement, not an estimate — the numbers come from running actual regex/code against actual text, not from
reasoning about what the regex "should" do — but it is explicitly caveated in `notes/tokenizer-cost-delta.md`
§5 as corpus-based rather than live-traffic-based, since generalizing the exact magnitude to live traffic
was out of scope.

## 4. Build/test friction from the shared worktree (operational note, not a task deviation)

Five other task agents (010, 011, 012, 020, 023, 083) were building/testing concurrently in this same
worktree. Standard `dotnet build`/`dotnet test` against the shared `bin/obj` repeatedly hit
`MSB3027`/`MSB3021` file-lock errors from another agent's long-running `testhost.exe` (confirmed actively
executing via CPU-time sampling, not a hung zombie — never killed it). Worked around by building/testing to
an isolated `-p:BaseOutputPath` under the session scratchpad for every verification run in this task; the
shared BFF project build (no override) succeeded cleanly on its own once contention cleared. No production
or test file was affected by this; noted per CLAUDE.md §6 "Operational traps."
