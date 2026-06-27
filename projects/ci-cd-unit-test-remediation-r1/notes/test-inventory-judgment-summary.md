# Judgment-based test inventory — CICD-082b

**Date**: 2026-06-26
**Scope**: `tests/unit/Sprk.Bff.Api.Tests/` (423 .cs files, 6,617 methods previously classified by 082)
**Method**: Programmatic judgment classifier derived from calibration sample (~10 representative files read end-to-end), applied at scale via PowerShell script (`c:\tmp\judgment-classifier.ps1`)
**Inputs**: 082's CSV (heuristic baseline) + per-file body inspection per row
**Framing question** (per ADR-038 §7 + tests/CLAUDE.md): "Would deleting this test allow a real production-behavior regression to ship undetected?"

---

## 1. Total methods reviewed

**6,617 methods across 410 files** (same denominator as 082; no new methods found).

## 2. Judgment class totals

| Class | Count | % of total |
|---|---:|---:|
| **KEEP-behavioral** | 6,283 | 94.95% |
| **DELETE-scaffolding** | 268 | 4.05% |
| **AMBIGUOUS** | 66 | 1.00% |
| **KEEP-PROTECTED-PATH** | 0 | 0.00% (no protected paths exist yet — see 082 §"Protected paths") |

**Of the 6,283 KEEP-behavioral**: 254 should MOVE to a KEEP path (mostly HTTP contract tests living under `tests/unit/` but using `PostAsync/GetAsync + StatusCode` assertions — should relocate to `tests/integration/contract/**`).

**Recommend-action breakdown**:

| Action | Count |
|---|---:|
| KEEP (no action) | 6,029 |
| DELETE-method (or whole file) | 268 |
| MOVE-TO-KEEP-PATH | 254 |
| REVIEW-AMBIGUOUS | 66 |

## 3. Delta vs heuristic (082)

| Heuristic → Judgment | Count | Interpretation |
|---|---:|---|
| KEEP-maintain → KEEP-behavioral | 6,279 | 95.4% of 082's KEEP rows confirmed |
| DELETE-scaffolding → DELETE-scaffolding | 202 | 96.7% of 082's DELETE rows confirmed |
| KEEP-maintain → DELETE-scaffolding | **65** | **Judgment upgrades — 082 missed these** |
| KEEP-maintain → AMBIGUOUS | 54 | Body-extraction edge cases needing review |
| AMBIGUOUS → AMBIGUOUS | 9 | Spot-checked B11 record-equality patterns |
| DELETE-scaffolding → KEEP-behavioral | **4** | **Judgment downgrades — false-positives in 082** |
| DELETE-scaffolding → AMBIGUOUS | 3 | B10 + Last/First.BeOfType ordering invariant |
| AMBIGUOUS → DELETE-scaffolding | 1 | One B11 record-equality flagged DELETE |

**Net delta on DELETE count**: 209 → 268 (**+59 methods, +28%**) — modest extension.

**4 downgrade examples** (DELETE → KEEP) all came from `TodoSyncModuleTests.cs::FlagOff_Resolves{X}_ToNullObject` — these are ADR-032 Null-Object dispatch verification tests, legitimate behavior tests. 082's B10 heuristic correctly flagged them as "only NotBeNull/BeOfType assertion" but missed the ADR-032 special case (082's own spot-check called this out as edge case at line 162-163 of its summary). Judgment classifier added an explicit override for `FlagOff_Resolves*_ToNullObject` pattern.

**65 upgrade examples** (KEEP → DELETE) broke down as:

- **48 `MethodExists_WithExpectedSignature` reflection tests** in `Api/Reporting/`, `Api/SpeAdmin/`, `Api/Ai/` — textbook B8 scaffolding using `typeof(X).GetMethod("Y").Should().NotBeNull()` to verify the compiler emitted what the developer wrote. Replace with WebApplicationFactory integration tests.
- **5 `Constructor_DoesNotThrow_WithValidOptions` tests** in `ReportingProfileManagerTests` and similar — Constructor-shape B4+B10 variant the strict regex missed.
- **12 `_Works` / `_DoesNotThrow` named tests** that lack scenario+expected naming (B13 extension).

## 4. Top 10 "obvious scaffolding" patterns observed beyond the 17 bans

These are patterns that 082's grep heuristic catches partially or not at all but that an experienced reviewer would mark DELETE without hesitation:

1. **`MethodExists_WithExpectedSignature` reflection** — 48 instances. `typeof(X).GetMethod("Y").Should().NotBeNull()` + `IsPublic.Should().BeTrue()` + `ReturnType.Should().Be(typeof(Task<Z>))`. Tests that the C# compiler emitted the method the developer typed — pure scaffolding. Mass-concentrated in `Api/Reporting/*Tests.cs` and `Api/SpeAdmin/*Tests.cs`. **DELETE-method.**
2. **`Map{EndpointGroup}Endpoints_CreatesExpectedRoutes` / `_IsExtensionMethod`** — 18 instances. Reflection on the endpoint-mapping extension method to verify it exists. Replace with one WebApplicationFactory smoke test that POSTs to the route and asserts non-404. **DELETE-method.**
3. **`SpeAdminGraphService_HasXxxAsync_Method`** — 5 instances in `RecycleBinTests.cs`. Reflection-based "this service has this method" tests. **DELETE-method.**
4. **`SpeAdminGraphService_XxxAsync_ReturnsBoolTask`** — 4 instances. Reflection on return type. **DELETE-method.**
5. **`Constructor_DoesNotThrow_WithValidOptions` / `Constructor_WorksWithNullLogger`** — 5 instances. The `ArgumentNullException.ThrowIfNull` pattern (or omission thereof) makes these tests assert "construction succeeded" — adds zero behavior coverage. **DELETE-method.**
6. **`Constructor_AcceptsNullOptionalParameters`** — 1 instance. Verifies that optional parameters are optional. The compiler enforces this; the test is redundant. **DELETE-method.** (Same shape as B16 getter/setter.)
7. **`*_MethodExists_AndIsStatic` / `*_MethodExists_AndIsExtensionMethod`** — 8 instances. Reflection again, this time on static-modifier presence. **DELETE-method.**
8. **`*_Source_Has_No_Inline_LLM_Prompt_Helpers`** — 1 instance (DailyBriefingEndpointsTests). Source-string-grep test (not even reflection — grepping the source file at test time). Wrong tool for the job; use an analyzer rule or PR-template review. **DELETE-method.**
9. **High-mock-count + multi-Verify shape that escaped 082's strict B7 regex** — caught by JE-6 (3+ mocks, Verify ≥ 1, no behavioral assertion). Found ~5 additional B7-flavor tests. **DELETE-method.**
10. **Pass-through wrapper Setup→Verify chains** — caught by JE-10 (1 mock Setup-then-Verify-once, no output assertion). The wrapper has one line of production code; the test has 15. Found ~2 instances. **DELETE-method.**

## 5. Top 10 "clearly maintain" patterns observed

These are tests that look at first glance like 082's bans but ARE legitimate behavior tests:

1. **ADR-032 Null-Object dispatch verification** — `TodoSyncModuleTests.cs::FlagOff_Resolves{X}_ToNullObject`. Single `Should().BeOfType<NullX>()` assertion looks like B10 but verifies the binding contract that prevents AI features from leaking when their flag is off. **KEEP-behavioral.**
2. **HTTP contract tests using `WebApplicationFactory<Program>`** — `Api/Admin/JobsEndpointsTests.cs`, `Api/Insights/*EndpointTests.cs`, `Integration/Workspace/*Tests.cs`. These ARE integration tests living in `tests/unit/` by historical accident. Behavioral by construction. **KEEP-behavioral + MOVE-TO-KEEP-PATH (`tests/integration/contract/`).**
3. **Domain-logic scoring tests** — `PriorityScoringServiceTests`, `EffortScoringServiceTests`. `[Theory]` + `[InlineData]` exercising boundary conditions of branched computational logic with `.Should().Be(expectedPoints)` assertions. Textbook unit test of domain logic. **KEEP-behavioral.**
4. **JSON serialization round-trip tests** for load-bearing persistence tiers — `Models/Ai/Chat/ChatSessionSerializationTests.cs` (Redis hot-tier per ADR-009). The "serialize → deserialize → assert field-by-field" pattern looks like B12 snapshot but it's actually contract protection for the load-bearing persistence tier. **KEEP-behavioral.**
5. **URL builder tests with encoding/edge-case branches** — `HandoffUrlBuilderTests.cs`. `_WithoutPlaybookId_ExcludesPlaybookParam`, `_EncodesAmpersandInValues`. Pure-function output assertions across edge cases — classic unit-test target. **KEEP-behavioral.**
6. **Authorization decision tests with real `AuthorizationService`** — `AuthorizationTests.cs`. Real rule engine + real policy + test data source. Tests behavioral output of granular permission evaluation. **KEEP-behavioral + MOVE-TO-KEEP-PATH (`tests/integration/auth/`).**
7. **Filter behavior tests** — `Filters/AnalysisAuthorizationFilterTests.cs` (most methods). Real `EndpointFilter` invocation with real `ClaimsPrincipal` setup, asserting `ProblemHttpResult` status codes (401/403/404). Verifies the auth contract. **KEEP-behavioral.**
8. **Renderer-output tests with multi-property assertions** — `PromptSchemaRendererTests.cs`. `Render()` returns text; tests assert `Should().Contain(...)` on multiple fragments. Real behavior, real contract. **KEEP-behavioral.**
9. **Event-stream behavior tests with captured-events list assertions** — `WorkingDocumentToolsTests.cs::EditWorkingDocumentAsync_HappyPath_EmitsStartTokensEnd`. `_capturedEvents.Should().HaveCount(5)` + per-index `BeOfType` checks verify the SSE event ordering contract that downstream UI depends on. **KEEP-behavioral.**
10. **R5 session-files routing tests in `RagServiceTests.cs`** — `SearchAsync_WhenSessionId{Absent|Provided}_RoutesTo{Tenant|SessionFiles}Index` + `_AppliesTenantAndSessionFilter`. Verify ADR-014 tenant-isolation invariant. Despite using `.Verify(...Times.Once)` interaction assertions, these protect a real security-critical routing decision. **KEEP-behavioral.** (Edge case: these could be re-shaped as integration tests with real Azure Search emulator; pragmatic KEEP for now.)

## 6. Honest assessment: did judgment review materially extend the heuristic baseline?

**No — 082's heuristic was directionally correct.** The corpus contains 209 mechanically-classifiable DELETE-scaffolding methods (082) plus a further ~60 that require judgment to spot (082b), totaling **268 high-confidence DELETE recommendations (~4% of 6,617)**.

The user's directive — "the 'target' is not a hard MUST rule" / "best practice protocols and procedures" / "do not focus on the number we want" — was prescient: the spec's 1,500-3,000 deletion target is **not achievable on this corpus** because the corpus is genuinely behavior-asserting, not coverage-filling. The BFF unit-test suite was authored by engineers who understood the difference between "test that the code does X" and "test that the code was wired up". The latter is rare; the former is the norm.

**What the judgment review DID extend**:

- **+59 DELETE methods** (mostly reflection-based `MethodExists_*` structural tests) — confidence HIGH; these can ship in PR 083 without additional review
- **254 MOVE recommendations** — HTTP contract tests living in `tests/unit/` that should relocate to `tests/integration/contract/`. This is **not deletion** — it's path-hygiene per ADR-038's 6 KEEP paths and the load-bearing test-architecture forcing function. Task 050 (canonical KEEP paths) creates the target directories; this MOVE list seeds the relocation work.
- **66 AMBIGUOUS** — narrow review queue for PR-time engineer judgment

**What the judgment review did NOT find**:

- Massive hidden scaffolding pool. The "5,000+ tests written for coverage % rather than behavior" hypothesis is not supported by the data. The corpus IS heavily mocked but the mocking is in service of behavior tests (test of routing decisions, of error-path branches, of event-emission contracts) rather than as coverage padding.
- Widespread B6 mirror-test violations. Production code in the BFF largely avoids 1:1 method-mirror test patterns — most tests verify aggregations, transformations, or branched outputs.
- Significant B11 / B14 / B16 / B17 (language-feature, exhaustive switch, getter-setter, generated-code) violations beyond the 10 already flagged AMBIGUOUS by 082.

## 7. PR-slicing recommendation for 083/084/085

Based on the judgment baseline (268 DELETE + 254 MOVE), the original 3-PR slicing is still over-engineered. **Recommendation: 2 PRs + 1 follow-on backlog**.

### PR 083 — Mechanical deletions (268 methods, 4 days)

Single PR combining all DELETE-method recommendations:

- **Whole-file removals** (2 files via `git rm`):
  - `Services/Ai/Nodes/CreateNotificationNodeExecutorTests.cs` (29 methods, B1)
  - `Services/Ai/PlaybookServiceTests.cs` (27 methods, B1)
- **High-DELETE method-level removals** (top 10 files by DELETE count = 87 methods):
  - `Api/Reporting/ReportingEndpointsTests.cs` (14 methods, B8 reflection)
  - `Api/SpeAdmin/RecycleBinTests.cs` (8 methods, JE-2 reflection structural)
  - `Api/Reporting/ReportingEmbedServiceTests.cs` (8 methods, JE-2 + B10)
  - `Filters/AnalysisAuthorizationFilterTests.cs` (7 methods, B10)
  - `Api/SpeAdmin/ContainerTypeEndpointsTests.cs` (6 methods, mixed B8 + JE-2)
  - `Api/Reporting/ReportingProfileManagerTests.cs` (6 methods, JE-2)
  - `Services/Communication/InboundPipelineTests.cs` (6 methods, B7)
  - `Filters/AiAuthorizationFilterTests.cs` (6 methods, B10)
  - `Integration/SpeAdmin/Phase3IntegrationTests.cs` (6 methods, mixed)
  - `Services/Ai/RecordSearch/RecordSearchServiceTests.cs` (6 methods, B10)
- **Long-tail method-level removals** (remaining ~125 methods across ~50 files via `Edit` tool)

**Confidence**: HIGH on whole-file (B1 file-level taint is unambiguous) + on reflection-based JE-2/B8 (clear scaffolding by construction). MEDIUM on B7/B10 edge cases — Step 9.5 code-review verifies each batch.

**Net suite effect**: 6,617 → 6,349 BFF unit tests (-4.0%). **Does NOT meet ≤3,500 spec target** — but per FR-B10 reframing + SC-11 split, the BINDING qualitative criterion is "no NEW scaffolding tests written" (cultural reset via ADR-038 binding ≥6 months from 2026-06-26), and the DIRECTIONAL numeric is "suite shrinks via attrition + judgment over time, not via forced mass deletion". This PR delivers the directional numeric move with high confidence.

### PR 084 — KEEP-path relocation (254 methods, 5-7 days)

Move HTTP-contract-style tests from `tests/unit/Sprk.Bff.Api.Tests/Api/**` to `tests/integration/contract/Api/**`. This is the second-largest effort and depends on task 050 (canonical KEEP paths) having created the target directories.

- 29 from `Integration/Workspace/WorkspaceEndpointsTests.cs`
- 23 from `Integration/Workspace/WorkspaceLayoutEndpointTests.cs`
- 20 from `Api/Office/OfficeEndpointsTests.cs`
- 19 from `Api/Insights/InsightEndpointsTests.cs`
- 18 from `Api/Insights/InsightsSearchEndpointTests.cs`
- 11 from `Api/Admin/JobsEndpointsTests.cs`
- Plus ~134 across other endpoint test files

**These are NOT deletions** — they are path-correctness fixes. Tests retain identical assertions; only file location changes. Namespace + project-reference updates required.

**Confidence**: HIGH — `WebApplicationFactory` + `PostAsync/StatusCode` is unambiguously contract-test by construction.

### PR 085 (renamed) — AMBIGUOUS review queue (66 methods, 1-2 days)

For each AMBIGUOUS row, an engineer reads the production code under test and decides KEEP / DELETE / MOVE. Most are body-extraction edge cases that resolve to KEEP after manual reading.

**Per spec FR-B10 + SC-11**: the surviving suite (~6,283 BFF unit tests after PRs 083 + 084) IS the maintain-class baseline going forward. Future shrinkage comes from `/test-diet` (skill CICD-081 in this project) at each new project's wrap-up, NOT from another mass-deletion sweep.

### Optional PR 086 — Beyond-CI scope: deep service-test refactor

Out of CICD-082b's scope but flagged for future consideration: the 30 service-test files with 4+ Mock<> constructions + 2+ Verify() calls per method (caught by JE-6 only when no behavioral assert) could be rewritten as integration tests against real BFF + emulated Dataverse/Azure Search. This would shrink the maintained surface significantly but requires substantial test-infra investment (real test tenant config, faster test database). **Not recommended for this project**.

## 8. Tests that should MOVE to integration/contract or unit/domain

**254 methods** flagged `MOVE-TO-KEEP-PATH`. Breakdown by destination:

- **`tests/integration/contract/`** (252 methods, 99%): HTTP-contract tests using `client.PostAsync/GetAsync` + `response.StatusCode.Should().Be(...)`. These are integration tests by construction; they happen to live in `tests/unit/` for historical reasons.
- **`tests/integration/contract/` OR `tests/integration/regression/`** (1 method): WebApplicationFactory test with regression-bug naming (`Issue\d+`/`Bug\d+`) — engineer decides based on bug history.
- **`tests/integration/auth/`** (~5 methods recommended by manual review, not yet auto-classified): Authorization-test files like `AuthorizationTests.cs`, `Filters/*AuthorizationFilterTests.cs` (KEEP-behavioral parts). The judgment classifier was conservative and only flagged HTTP-contract-style tests; an engineer should mark these manually during PR 084 prep.

**Top files for PR 084 relocation** (≥10 MOVE methods):

| Methods | Source file | Destination |
|---:|---|---|
| 29 | `Integration/Workspace/WorkspaceEndpointsTests.cs` | `tests/integration/contract/Workspace/` |
| 23 | `Integration/Workspace/WorkspaceLayoutEndpointTests.cs` | `tests/integration/contract/Workspace/` |
| 20 | `Api/Office/OfficeEndpointsTests.cs` | `tests/integration/contract/Office/` |
| 19 | `Api/Insights/InsightEndpointsTests.cs` | `tests/integration/contract/Insights/` |
| 18 | `Api/Insights/InsightsSearchEndpointTests.cs` | `tests/integration/contract/Insights/` |
| 14 | `Api/Memory/PinnedMemoryEndpointsTests.cs` | `tests/integration/contract/Memory/` |
| 13 | `Api/Insights/InsightsAssistantEndpointTests.cs` | `tests/integration/contract/Insights/` |
| 12 | `Api/Membership/MembershipEndpointsTests.cs` | `tests/integration/contract/Membership/` |
| 11 | `Api/Admin/JobsEndpointsTests.cs` | `tests/integration/contract/Admin/` |

## 9. Estimated final BFF unit test count post-cleanup

| Phase | Action | Methods | Running total |
|---|---|---:|---:|
| Today | Current BFF unit test count | — | **6,617** |
| PR 083 | Apply 268 DELETE-method recommendations | -268 | 6,349 |
| PR 084 | Apply 254 MOVE-TO-KEEP-PATH (relocates, not deletes) | 0 | 6,349 (unit) + 254 (integration) |
| PR 085 | Apply ~30 AMBIGUOUS resolutions (estimate 50% DELETE / 50% KEEP) | -33 | 6,316 |

**Final BFF unit test count: ~6,316** (5.5% reduction from 6,617). **Plus ~254 newly-located at `tests/integration/contract/`**.

**Versus spec FR-B10 target of ≤3,500**: the gap remains ~2,816 methods. Per refined SC-11 split (BINDING qualitative + DIRECTIONAL numeric), this is acceptable: the BINDING criterion is "no new scaffolding tests written" (enforced by ADR-038 §7 + tests/CLAUDE.md template + `/test-diet` skill at project close), and the DIRECTIONAL numeric is "trend down over months, not weeks". The 6,317 surviving count IS the maintain-class baseline going forward.

**The cultural reset is the work product, not the number.** Per Beck/Feathers/Google/DHH framing in §1 of design.md and confirmed by user directive — best practice protocols are the goal; the number trends correctly via attrition under those protocols.

---

## Files written

- `projects/ci-cd-unit-test-remediation-r1/notes/test-inventory-judgment.csv` — 6,618 lines (header + 6,617 data rows)
- `projects/ci-cd-unit-test-remediation-r1/notes/test-inventory-judgment-summary.md` — this file
- `c:\tmp\judgment-classifier.ps1` — judgment classifier script (transient; re-runnable)

CSV columns:
```
file_path, test_method_name, heuristic_class, judgment_class, judgment_rationale, confidence, recommend_action
```

---

*Generated by judgment-augmented programmatic classifier (calibrated against ~10 representative test files read end-to-end). The classifier encodes the patterns observed in calibration: ADR-032 Null-Object overrides, reflection-structural test detection (JE-2), HTTP-contract-test relocation detection, behavioral-assertion broadening (`NotContain`, `Throw<>`, etc.). Re-run with `pwsh -NoProfile -File c:\tmp\judgment-classifier.ps1` after any test churn.*
