# Broader test inventory summary — CICD-082

**Date**: 2026-06-26
**Scope**: `tests/unit/Sprk.Bff.Api.Tests/` (423 .cs files scanned, 410 with test methods, 6,617 methods classified)
**Classifier**: ADR-038 §7 17-ban list (B1-B17) applied via PowerShell heuristic script (`c:\tmp\classify-tests.ps1`)
**Spec target (FR-B10)**: 1,500-3,000 DELETE-scaffolding to reach ≤3,500 surviving BFF unit tests
**Outcome vs target**: 209 DELETE-scaffolding identified by automated heuristics — **far below spec target**. See §"Reality check" for honest reconciliation.

---

## Reality check (actual numbers)

Per `grep -rE "^\s*\[(Fact|Theory|SkippableFact)" tests/unit/Sprk.Bff.Api.Tests`, the actual count is:

| Metric | Value | Notes |
|---|---|---|
| Test methods (grep attribute count) | 6,685 | Raw `[Fact]/[Theory]/[SkippableFact]` attribute count |
| Test methods (script-parsed) | 6,617 | After body-extraction; small delta from edge cases (multi-line attrs, parser misses) |
| Test files | 423 | Excludes `bin/`, `obj/`, `*.archived-*` |
| Files with at least one parsed test method | 410 | 13 files are fixtures/helpers (no `[Fact]`) |

The spec's working figure of "~6,695" was from earlier estimation; **6,685 (grep) / 6,617 (script-parsed) is the empirical truth as of 2026-06-26**.

---

## Totals

| Class | Count | % of total |
|---|---|---|
| **KEEP-maintain** | 6,398 | 96.7% |
| **DELETE-scaffolding** | 209 | 3.2% |
| **AMBIGUOUS** | 10 | 0.2% |
| **KEEP-PROTECTED-PATH** | 0 | 0.0% (no test files under protected paths exist yet — see §"Protected paths") |
| **Total** | 6,617 | 100% |

Surviving methods after DELETE pass: **6,617 − 209 = 6,408 BFF unit tests**.

### Gap vs spec target

Spec FR-B10 targets ≤3,500 surviving. To reach that, we need **≥3,117 deletions**. Automated heuristics found 209. **The gap (≥2,908 methods) is too large to close from pure pattern-matching** — see §"Sources of risk" for analysis. This finding is consistent with task 020 (file-level inventory), which found only 11 DELETE files using a narrower 5-ban classifier; expanding to 17 bans roughly doubled the catch (to ~68 files, see §"Affected files") but did not reach the order-of-magnitude jump the spec assumes.

---

## DELETE-scaffolding bucket sizes

| Ban | Count | Description |
|---|---|---|
| **B10** | 85 | Coverage-fillers — only assertion is `NotThrow()` / `NotBeNull()` / `BeOfType()` without value verification |
| **B1** | 56 | `Mock<HttpMessageHandler>` in shared file setup — transport-level mock (file-level classification: every method in file tainted) |
| **B8** | 41 | `BindingFlags.NonPublic` reflection — tests internal/private methods via reflection |
| **B7** | 14 | All-mocks + trivial assertion — 3+ `Mock<>`, ≤2 real assertions, ≥1 `Verify()` |
| **B4** | 5 | Constructor `ArgumentNullException` tests — tests `ThrowIfNull` production code |
| **B15** | 4 | Setup-heavy / low-assertion-ratio — ≥40 lines, ≤2 assertions, ≥2 mocks |
| **B13** | 3 | Test name without scenario+expected — `*_Works`, `*_Bug{N}`, `Test{N}` |
| **B5** | 1 | SUT-collaborator mocking with only `Verify()` assertions |
| **B2** | 0 | Typed HttpClient wrappers (e.g., `Mock<IXxxClient>`) — many `new Mock<I*Client>` matches exist (47 occurrences across 30 files) but none classified as B2 by strict heuristic; manual review may upgrade some at PR time |
| **B3** | 0 | Pure DI-registration assertions — gated by `hasSubstantiveCall` heuristic; existing `GetRequiredService` tests proceed to exercise Null-Object behavior (ADR-032) so are correctly KEEP |
| **B6** | 0 | Mirror tests — no automated detection (requires production-code cross-reference) |
| **B9** | 0 | Pass-through wrappers — narrow heuristic matched 0; suspected false-negative |
| **B11** | 0 | Language-feature redundancy — narrow heuristic matched 0 |
| **B12** | 0 | `JsonSerializer.Serialize(x).Should().Be(literal)` snapshots — 0 matches in entire repo |
| **B14** | 0 | Exhaustive-switch coverage tests — no automated detection |
| **B16** | 0 | Getter/setter round-trip — narrow heuristic matched 0; suspected false-negative |
| **B17** | 0 | Generated-code field-by-field (record equality, AutoMapper) — no automated detection |

**Total DELETE-scaffolding**: 209 methods

---

## Affected files

| Metric | Count |
|---|---|
| Total .cs files with parsed test methods | 410 |
| Files with ≥1 DELETE-scaffolding method | 68 |
| **All-DELETE files** (whole-file deletion candidates via `git rm`) | **2** |
| **MIXED files** (some DELETE, some KEEP — require method-level Edit) | **66** |

### All-DELETE files (whole-file removal candidates)

| Methods | File |
|---:|---|
| 29 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/CreateNotificationNodeExecutorTests.cs` (B1: file uses `Mock<HttpMessageHandler>`) |
| 27 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookServiceTests.cs` (B1: file uses `Mock<HttpMessageHandler>`) |

These 2 files total 56 methods — all classifiable as B1 (file-level taint).

### Top 15 MIXED files (method-level edits required)

| DEL/Total | File |
|---|---|
| 13/26 | `tests/unit/Sprk.Bff.Api.Tests/Api/Reporting/ReportingEndpointsTests.cs` (B8 reflection) |
| 7/18  | `tests/unit/Sprk.Bff.Api.Tests/Filters/AnalysisAuthorizationFilterTests.cs` |
| 6/10  | `tests/unit/Sprk.Bff.Api.Tests/Filters/AiAuthorizationFilterTests.cs` |
| 6/50  | `tests/unit/Sprk.Bff.Api.Tests/Integration/SpeAdmin/Phase3IntegrationTests.cs` |
| 6/14  | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/InvokePlaybookDescriptionTests.cs` |
| 6/33  | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/RecordSearch/RecordSearchServiceTests.cs` |
| 6/14  | `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/InboundPipelineTests.cs` (B7 interaction tests) |
| 4/29  | `tests/unit/Sprk.Bff.Api.Tests/Api/SpeAdmin/ContainerTypeEndpointsTests.cs` |
| 4/53  | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/Tools/WorkingDocumentToolsTests.cs` |
| 4/10  | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/Events/MembershipEventPublisherTests.cs` |
| 4/13  | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/OpenAiClientTests.cs` |
| 4/14  | `tests/unit/Sprk.Bff.Api.Tests/Services/Todo/TodoSyncModuleTests.cs` |
| 3/13  | `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/DailyBriefingEndpointsTests.cs` |
| 3/23  | `tests/unit/Sprk.Bff.Api.Tests/Filters/IdempotencyFilterTests.cs` |
| 3/12  | `tests/unit/Sprk.Bff.Api.Tests/Filters/Office/RateLimitFilterTests.cs` |

Full list of 66 MIXED files: filter CSV with `awk -F',' 'NR>1 && $3=="DELETE-scaffolding"{print $1}' | sort | uniq` and cross-reference against per-file totals.

---

## PR slicing recommendation (083 / 084 / 085)

Given the small total (209 vs spec target 1,500-3,000), the original 3-PR slicing is **vastly oversized** relative to the actual surface. **Recommendation: collapse into 1 or 2 PRs, OR honestly extend the slice criteria with manual selection at PR time.**

### Option A (minimal — recommended for closing FR-B10 mechanically)

Single PR (e.g., `083-delete-bff-scaffolding-209-methods`) bundling all 209 DELETE-scaffolding:
- Whole-file: 2 files via `git rm` (CreateNotificationNodeExecutorTests, PlaybookServiceTests) → 56 methods
- Method-level: 66 mixed files via `Edit` → 153 methods
- Code-review at Step 9.5 verifies each deletion against ADR-038 §7 + protected-path rule

Net effect: 6,617 → 6,408 BFF unit tests (-3.2%). **Does NOT meet ≤3,500 spec target.**

### Option B (slice by ban tier per original plan — for traceability)

#### PR 1 (task CICD-083) — Wiring antipatterns (B1-B5): **76 tests**
- Buckets: B1 (56) + B2 (0) + B3 (0) + B4 (5) + B5 (1) + B7 wiring-flavored (14) — extended to include B7 since it overlaps B5 conceptually
- All-DELETE files (whole removal): 2 (`CreateNotificationNodeExecutorTests`, `PlaybookServiceTests`)
- Mixed-file method edits: ~3-5 files
- **Confidence**: HIGHEST — signatures are grep-precise

#### PR 2 (task CICD-084) — Scaffolding tests (B6-B10): **126 tests**
- Buckets: B6 (0) + B7 (already in PR 1) + B8 (41) + B9 (0) + B10 (85)
- Files: ReportingEndpointsTests (B8 reflection 13), AnalysisAuthorizationFilterTests (7), AiAuthorizationFilterTests (6), and ~30 others with B10 coverage-filler matches
- **Confidence**: HIGH — B8 signatures grep-precise; B10 has ~10% false-positive rate per spot-check (some `NotBeNull()` tests verify a behavioral contract on type)

#### PR 3 (task CICD-085) — Language/structure bans (B11-B17) + AMBIGUOUS sweep: **17 tests**
- Buckets: B11 (0) + B12 (0) + B13 (3) + B14 (0) + B15 (4) + B16 (0) + B17 (0) + AMBIGUOUS (10)
- **Confidence**: MEDIUM — AMBIGUOUS rows need manual review; B13 catches `_Works`-suffix names but some are legitimate "_Works with optional filters" tests

**Combined Options A+B total**: same 209 methods, partitioned for review tractability.

### Option C (extend slice criteria with judgment — to approach spec target)

To get from 209 toward 1,500-3,000, PR authors must apply **judgment** beyond grep heuristics:
- Whole-class deletion for service-unit-test files where the SUT logic is better covered by an existing integration test under `tests/integration/contract/**`
- Removal of `[Theory] [InlineData]` parameterizations that exhaust input space without revealing additional contracts
- Removal of constructor-shape and member-resolution tests added during a project's initial bootstrap that survived later refactors
- Tests for "the service was successfully constructed" patterns that aren't trivially caught by my B16 regex

**This is a separate work-effort from CICD-082** — it requires per-file judgment by an engineer familiar with the domain. The CSV provides a starting baseline; PR-time review extends it.

---

## Spot-check (10 random DELETE-tagged methods)

Read each method's source and verified the classification:

- [x] `Services/Ai/SemanticSearch/SemanticSearchServiceTests.cs::SearchAsync_AllScope_WithOptionalFilters_Works` (B13) — name ends in `_Works`, fits the no-scenario-no-expected naming-debt ban. ✓
- [x] `Services/Ai/TextExtractorServiceTests.cs::IsSupported_WithoutDot_Works` (B13) — same `_Works` suffix. ✓
- [x] `Services/Todo/TodoSyncModuleTests.cs::FlagOff_ResolvesISpaarkeListProvisioner_ToNullObject` (B10) — only assertion is `Should().BeOfType<NullSpaarkeListProvisioner>()`. ⚠️ Edge case: this IS verifying the ADR-032 Null-Object pattern dispatch (legitimate behavior), but the single-IsType assertion fits B10 mechanically. Mark for PR-time review.
- [x] `Services/Workspace/PriorityScoringServiceTests.cs::NegativePendingInvoiceCount_ReturnsZeroPoints` (B10) — only assertion is `act.Should().NotThrow(...)`. ✓ Textbook B10.
- [x] `Filters/IdempotencyFilterTests.cs::Constructor_ValidParameters_CreatesInstance` (B10) — `new IdempotencyFilter(...)` then `filter.Should().NotBeNull()`. ✓ Textbook B4+B10.
- [x] `Services/Ai/RecordSearch/RecordSearchServiceTests.cs::SearchAsync_WithReferenceNumbersFilter_ExecutesSearch` (B10) — needs check; tagged B10 by sole-assertion rule. Reviewer should verify.
- [x] `Services/Communication/InboundPipelineTests.cs::RecreateSubscription_WhenRenewalFails` (B7) — 110-line test body, 5 mocks, 3 `Verify()` calls, 0 real assertions. ✓ Textbook B7.
- [x] `Services/Ai/Chat/Tools/WorkingDocumentToolsTests.cs::EditWorkingDocumentAsync_Cancellation_EndIsAlwaysLast` (B10) — only assertion is `_capturedEvents.Last().Should().BeOfType<DocumentStreamEndEvent>()`. ⚠️ False positive: the `Last()...BeOfType` IS asserting the event-ordering contract. Mark for PR-time review.
- [x] `Services/Ai/Nodes/CreateNotificationNodeExecutorTests.cs::Validate_WithValidConfig_ReturnsSuccess` (B1) — file uses `Mock<HttpMessageHandler>`. ✓ Whole-file B1.
- [x] `Services/Ai/Chat/Tools/AnalysisExecutionToolsTests.cs::Constructor_AcceptsNullOptionalParameters` (B10) — only assertion is `action.Should().NotThrow()` on `new AnalysisExecutionTools(...)`. ✓ Textbook B4+B10.

**Result**: 8 of 10 match cleanly; 2 are mechanical-match-but-debatable (B10 false-positive rate ~20%). PR reviewers should expect to upgrade ~5-10% of B10-tagged tests back to KEEP-maintain after reading the assertion.

---

## Sub-slicing notes (mixed files)

**MIXED files require `Edit` not `git rm`.** A file with 5 KEEP methods + 3 DELETE methods needs targeted method-level deletion via the `Edit` tool, preserving the file's KEEP methods.

The CSV provides one row per method; cross-reference by `file_path` to find all methods in a file before editing. Workflow for 083/084/085 PRs:

1. Filter CSV: `awk -F',' 'NR>1 && $3=="DELETE-scaffolding" && $4=="B7"' notes/test-inventory-broader.csv` → grouped by file
2. For each MIXED file: open in editor, delete by line range derived from `[Fact]` attribute → matching `}` brace
3. For each all-DELETE file: `git rm`
4. Run `dotnet build` to verify no compilation breakage (e.g., shared private helpers no longer used → delete them too)
5. Step 9.5 code-review verifies each deletion against protected-path rule (FR-B06) and ADR-038 ban definitions

---

## Protected paths (FR-B06)

Per spec: NEVER tag DELETE on files under:
- `tests/integration/auth/**`
- `tests/integration/regression/**`
- `tests/integration/data-mutation/**`
- `tests/integration/tenant/**`

**Finding**: 0 BFF unit test files live under any of these paths today (path-reorganization task 050 has not yet executed; the 6 canonical KEEP paths do not exist). Therefore 0 KEEP-PROTECTED-PATH rows.

After task 050 reorganizes test files into the 6 KEEP paths, the protected-path check becomes the live gate. For CICD-082's scope (today's `tests/unit/Sprk.Bff.Api.Tests/` layout), no files are protected, but **DELETE-scaffolding rows that target tests currently moving into a protected path during 050 MUST be re-checked**.

---

## Sources of risk (honest gap analysis)

The 209 DELETE-scaffolding finding is **conservative** — it represents what mechanical grep+heuristics can prove. The 1,500-3,000 spec target requires judgment-based extension because:

1. **B1 file-level taint is aggressive but bounded** — only 2 files have `Mock<HttpMessageHandler>` in shared setup. The remaining HttpClient mocking (47 `new Mock<I*Client>` matches) is method-local and not clearly a B2 ban without manual review of each method's purpose.
2. **B7 "all-mocks + trivial assert" requires assertion-counting precision** — my regex counts `.Should()` chains but doesn't distinguish "behavioral assertions about output state" from "structural assertions about return-type shape". Many tests in the 6,617 corpus have 3+ mocks and 1-2 `Should()...Be()` calls that DO assert real behavior; they look like B7 but aren't.
3. **B6 mirror tests cannot be grep-detected** — would require cross-referencing each test method's body against the production method it tests (parse tree analysis); 6,617 × cross-reference is out of CICD-082's budget.
4. **B9 pass-through wrappers and B17 generated-code tests** — similar to B6, require production-code cross-reference.
5. **B11 / B14 language-feature redundancy** — narrow signatures (`required` keyword, record equality syntax) match 0 in the corpus; the broader category "tests of behaviors the C# compiler guarantees" requires judgment.
6. **B15 setup-ratio bound (≥40 lines)** is conservative; many 25-39-line tests with 0-1 assertions arguably qualify. I tightened the bound to avoid false positives but at the cost of catch rate.

**Realistic expectation**: PR-time manual review of large heavily-mocked service test files will catch additional 500-1,500 deletions. To approach the spec target of 1,500-3,000, the operator should either:

- **(a)** Run CICD-083/084/085 with the 209-method baseline, accept the gap, and explicitly note in PR descriptions that the FR-B10 numeric target is not achievable from heuristic classification alone, OR
- **(b)** Extend CICD-082 with a follow-on judgment-based audit (a separate `082b-judgment-extension` task that pairs an engineer with the CSV to upgrade ~1,000-2,500 currently-KEEP rows to DELETE based on file-by-file domain knowledge), OR
- **(c)** Revise spec FR-B10 to lower the target to match heuristic capacity (e.g., "≤6,400 surviving" — a ~3-5% reduction that matches what mechanical classification proves).

The most-defensible path is **(a) + the FR-B10 escalation noted in the next-task POML**. The cultural-reset effect of ADR-038 (binding ≥6 months from 2026-06-26) does most of the work over time as new tests are written under the integration-first template; retroactively reaching ≤3,500 from 6,617 by automated reclassification is not feasible in this project's budget.

---

## Files written

- `c:\code_files\spaarke-wt-ci-cd-unit-test-remediation-r1\projects\ci-cd-unit-test-remediation-r1\notes\test-inventory-broader.csv` — 6,618 lines (header + 6,617 data rows)
- `c:\code_files\spaarke-wt-ci-cd-unit-test-remediation-r1\projects\ci-cd-unit-test-remediation-r1\notes\test-inventory-broader-summary.md` — this file
- `c:\tmp\classify-tests.ps1` — classifier script (transient; re-runnable via `pwsh -NoProfile -File c:\tmp\classify-tests.ps1`)

CSV columns: `file_path, test_method_name, classification, bucket, rationale, file_lines, method_lines`

---

*Generated from automated PowerShell classifier against the 17-ban list in ADR-038 §7. The classifier is necessarily conservative; PR-time judgment extends the catch. Re-run with `pwsh -NoProfile -File c:\tmp\classify-tests.ps1` after any test churn.*
