# Task 075 (FR-13) — test-hygiene batch: execution notes

Executed in an isolated worktree branched from `master` (not `work/spaarkeai-compose-r7`) —
`git diff` against the live `spaarke-wt-spaarkeai-compose-r7` worktree showed the three target
areas (`tests/unit/Sprk.Bff.Api.Tests/Services/Compose/**`,
`src/client/shared/Spaarke.Compose.Components/src/widgets/**`, and the `nda-interrupted-clauses.docx`
fixture) are byte-identical between the two branches, so this worktree is an equivalent base for
this task's scope.

## 1. FakeTimeProvider flake — `ComposeServiceCreateOnSaveTests.cs`

**Root cause** (confirmed via `projects/spaarkeai-compose-r6/notes/020-canonical-hub-design.md`
§22/§23 close-out entries — "ComposeServiceCreateOnSaveTests fire-and-forget timing — 3rd/4th
occurrences under parallel load, always green isolated + on rerun"): the two fire-and-forget
background-profile tests (`SaveAsync_TransientDraft_DispatchesProfileFireAndForget_ReturnsWithoutAwaiting`
and `SaveAsync_WhenBackgroundProfileThrows_SaveResultUnaffected_ExceptionSwallowed`) gate on a
`TaskCompletionSource`-backed signal (`GatedProfileFake.Started` / `.Finished`) with a real-wall-clock
`WaitAsync(TimeSpan.FromSeconds(5))` safety net. The TCS signal itself is fully deterministic (it
completes the instant the background `Task.Run` reaches `ProfileDocumentAsUserAsync`) — the flake is
NOT in the signal, it's in the 5s real-clock deadline racing against ThreadPool scheduling latency when
the full Compose suite (or the full assembly, ~10.5k tests) runs under xUnit's default parallel-collection
execution and the ThreadPool is saturated by every other test class's concurrent work.

This is the SAME class of flake already fixed/documented elsewhere in this codebase —
`AuditLogServiceTests.cs` (`Services/Ai/Audit`) uses the identical TCS + `WaitAsync` pattern at a **10s**
deadline with an explicit comment: "immune to ThreadPool saturation under full-suite parallelism; the
timeout is only a hang guard." `DispatchSessionEndpointContractTests.cs` uses the same 10s convention.

**Fix**: bumped all four `WaitAsync(TimeSpan.FromSeconds(5))` call sites in
`ComposeServiceCreateOnSaveTests.cs` to `TimeSpan.FromSeconds(10)`, matching the established
codebase convention, with a comment explaining the rationale and citing this task. This is a hang-guard
widening, not a behavior/assertion change — the test still asserts the exact same synchronous+background
contract; it simply stops being marginal under full-suite parallel load. No `TimeProvider`/`FakeTimeProvider`
injection was applicable here (unlike the codebase's genuine TTL/expiry `FakeTimeProvider` tests) because
nothing under test reads wall-clock time or has time-based business logic — the wait is purely a
"did the background continuation get scheduled yet" gate, which real thread scheduling must actually
satisfy regardless of any injected clock.

**Could not deterministically reproduce the flake locally** (10 402/10 402 passed on a full-assembly run,
5 repeated Compose-folder runs all green) — consistent with the "always green isolated + on rerun" note
from R6. The fix is validated by: (a) confirmed root-cause match against the documented symptom, (b) the
identical pattern already accepted/shipped elsewhere in this codebase for the same symptom class, and
(c) repeated local runs post-fix all green (see final report).

## 2. Pre-existing jest suites — 4× `ComposeWorkspace` "Element type is invalid" + `stepOperationInterceptor`

### 2a. `ComposeWorkspace` — 4 suites (`saveOpLogPreservation`, `bornInEditorSave`, `search`, `imports`)

**Root cause**: `ComposeWorkspace.tsx` conditionally mounts `<RichFilePreviewDialog/>` (from
`@spaarke/ui-components`) once a document is promoted (`canPreviewDocument = previewDocumentId.length > 0
&& bffBaseUrl.length > 0`) — the "Open Document" preview modal wired in a later task, mirroring the
existing unconditional-mount-under-its-own-`open`-prop pattern already used for `SendEmailDialog` and
`SprkModal` (both of which DO have `jest.mock('@spaarke/ui-components', …)` stubs in every suite file, each
with a comment explaining exactly this failure mode for `SprkModal`). `RichFilePreviewDialog` was never
added to the `@spaarke/ui-components` mock in these 4 suite files, so under the mock it resolves to
`undefined`; the 4 failing suites are exactly the ones whose test scenarios reach a promoted-document
state (a stored/loaded/saved document with a non-empty id) — every OTHER `ComposeWorkspace.*.test.tsx`
file either already has the stub (`unmountFlush`, `renderOnSave`) or never reaches a promoted-document
render path.

**Fix**: added `RichFilePreviewDialog: () => null,` to the `@spaarke/ui-components` jest.mock block in
all 4 files, with a comment cross-referencing the existing `SprkModal` comment (same failure class, same
fix shape). No production code changed — this is a genuine test-fixture drift (a new prop/export the
mock never kept up with), not a product bug.

### 2b. `stepOperationInterceptor.test.ts`

**Root cause**: `compose-operations.ts` (task 055, "ROBUST ANCHOR") added an ADDITIVE, backward-compatible
`paraOffset?: number` field to `RunLocalPosition` — the paragraph-relative character offset, alongside the
legacy `(runIndex, offset)` run-local pair. The type's own doc comment states every op the interceptor
emits now carries BOTH. 11 assertions across 10 tests in this file used `.toEqual({runIndex, offset})` /
`.toEqual({paraId, runIndex, offset})` without the new field, so exact deep-equality failed the moment
`paraOffset` started being populated — a test fixture that fell behind an intentional, documented, additive
production change, not a product bug.

**Fix**: added the correct `paraOffset` value to each expected object (11 call sites across 10 `it()`
blocks — one test had 2 assertions gated behind each other, only surfacing the second once the first was
fixed). Values were computed from each test's own paragraph structure (single-run paragraphs:
`paraOffset === offset`; the two-run `twoRunDoc` case at `resolveRunAnchor`'s "second run" test:
`paraOffset` = the paragraph-absolute offset (8), distinct from the run-local `offset` (3), matching the
doc comment's example exactly). No production code changed.

## 3. nda-interrupted-clauses.docx — paraId regeneration

**Root cause**: 8 of the fixture's 15 `w14:paraId` values were `>= 0x80000000` (`ST_LongHexNumber` requires
`0 < x < 0x80000000` — the SAME range `ParaIdPreParser.cs` / `ComposeBaselineParaIdStamper.cs` /
`ComposeDocumentRenderer.cs` enforce server-side). Confirmed count matches R6's close-out note ("8 distinct
pre-existing paraId errors confirmed").

**Fix**: wrote a one-off regeneration script (not committed — see below) that opens the `.docx` as a zip,
regex-extracts every `w14:paraId="XXXXXXXX"` attribute value in `word/document.xml`, mints a fresh random
8-hex uppercase id for each spec-invalid one (`0 < x < 0x80000000`, collision-checked against every id
already in the document — mirroring `ParaIdPreParser.MintUnique`), and rewrites only that one XML part back
into the same zip container, leaving every other byte/part untouched. Verified post-regeneration: all 15
ids in-range, all unique, all 9 OPC parts present and well-formed XML (same verification method the corpus
manifest documents for every fixture in this corpus). The regenerated bytes were committed through the
repo's Git-LFS filter (`git lfs status` showed the new OID staged correctly).

Regenerated 8 ids:
```
CBAF6DD2 -> 4102A23F
AD560931 -> 723D244C
DEF84E96 -> 60F098E3
DE3025B5 -> 7B8B7936
E51239E9 -> 1D1F54C4
E05FBF3A -> 4BE850AD
A39ADEA2 -> 21CF77E8
87DAFA30 -> 5B0933CD
```

The one-off Python regeneration script was NOT committed (it is a throwaway migration tool, not a
maintained test asset) — its logic is fully documented above for reproducibility if the fixture ever
needs regenerating again (e.g. after a future hand-edit re-drifts it out of range).

### 3a. Direct consequence: `ComposeTemplateChromeProvenanceSeamTests` follow-up (in-scope, not scope creep)

A full-assembly xUnit run (10,499 tests) after the fixture regeneration surfaced exactly ONE new failure:
`ComposeTemplateChromeProvenanceSeamTests.ApplyTemplate_ImportedNdaCarrier_NumberingRemapsTableCarriesAndTemplateStyleWins`
(`tests/integration/seam/Compose/ComposeTemplateChromeProvenanceSeamTests.cs`). This test's own
`AssertMergeIntroducesNoNewValidationErrors` helper (doc comment at line ~280) explicitly documented the
exact scenario this task creates: "nda-interrupted-clauses.docx … carries 8 spec-invalid w14:paraId
values … this helper exists ONLY for a source with a known validation baseline — if the fixture has been
cleaned up, switch this slice back to the strict `AssertPackageValidates`." Once the fixture became
spec-valid, `OpenXmlValidator` found zero errors on the raw source, so the helper's own self-check
(`sourceBaseline.Should().NotBeEmpty(...)`) failed — exactly the documented trigger.

**Fix** (per the test's own documented guidance, not a judgment call): swapped the test's final assertion
from `AssertMergeIntroducesNoNewValidationErrors(carrierBytes, doc)` to the strict
`AssertPackageValidates(doc)`. Verified empirically — the test now passes, confirming the regenerated
fixture merges into a fully validator-clean output (no repair-prompt-triggering errors at all, not merely
"no NEW errors"). This is treated as in-scope (a direct, foreseeable, single-file consequence of the
task's own explicit fixture-regeneration requirement) rather than scope creep — leaving it broken would
violate the task's own acceptance criterion ("Compose jest + xUnit suites run green … locally + CI").

Post-fix: full `Sprk.Bff.Api.Tests` assembly run is 10,402 passed / 0 failed / 97 skipped (10,499 total) —
zero failures, not just the target suites.

## 4. PR #690 coordination outcome

PR #690 (`work/ci-lfs-fix-r1`, "ci: pull Git-LFS corpus fixtures in Build & Test") was OPEN (not merged)
at execution time and targets the 5 Compose **seam** tests (`tests/integration/seam/Compose/**`) via an
LFS-pull CI fix — a different failure class (CI runner never materializing LFS content) from all three
repairs in this task (a real-clock test-timing flake, stale jest mocks, and fixture paraId range). No file
overlap: this task touched `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceCreateOnSaveTests.cs`,
4 `ComposeWorkspace.*.test.tsx` files + `stepOperationInterceptor.test.ts`, and the `nda-interrupted-clauses.docx`
fixture; PR #690's description says it modifies CI workflow/LFS-pull config, not these files. No double-fix.

## 5. Escalations

None. All three repairs traced to confirmed test-only causes (real-clock margin, stale mock, stale
assertion shape) with no genuine product-code defect uncovered.
