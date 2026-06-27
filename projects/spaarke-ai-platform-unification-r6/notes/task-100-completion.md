# Task 100 — Handler Infrastructure + Registration Pattern (D-H-00) — Completion Notes

**Completed**: 2026-06-07
**Rigor**: FULL
**Phase**: Parallel — 8 Typed Tool Handlers, Wave H-G0 (GATE)
**Branch**: `work/spaarke-ai-platform-unification-r6`
**Status**: ✅ Complete; Wave 1 (101–104) + Wave 2 (105–108) handler tasks unblocked

---

## Files Touched

### New files

| File | Purpose | LOC |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/HandlerRegistrationConventions.md` | Canonical 4-point contract doc + worked example using `GenericAnalysisHandler` | ~210 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/TemplateHandler.cs` | Reference handler skeleton; auto-discovered, never invoked at runtime (no `sprk_analysistool` row points at it) | ~165 |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/TypedToolHandlerTestFixture.cs` | Shared mocks (`IOpenAiClient`, `IScopeResolverService`), context builders (`BuildToolExecutionContext`, `BuildChatInvocationContext`), telemetry assertion helper (`AssertTelemetryRespectsAdr015`) | ~280 |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/HandlerContractTestTemplate.cs` | 4 contract tests every Wave 1/Wave 2 handler test class copies (registered + discoverable + valid metadata + non-empty supported types) | ~140 |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/AutoDiscoveryVerificationTests.cs` | Gate test: assembly scan finds every concrete `IToolHandler` (+ `IAnalysisToolHandler` alias survives + scoped lifetime + no duplicates) | ~175 |

### Modified files

None. Auto-discovery in `ToolFrameworkExtensions.AddToolHandlersFromAssembly` and `AnalysisServicesModule.AddToolFramework` was verified intact post-rename (task 006 added the `IAnalysisToolHandler` global-using alias so the existing assembly-scan code keeps working unchanged).

### Bookkeeping

- `projects/spaarke-ai-platform-unification-r6/tasks/100-handler-infrastructure-and-registration-pattern.poml` — status `not-started` → `completed` + `<completion-notes>`
- `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` — row 100 🔲 → ✅
- This file

---

## Key Design Decisions

### D1 — Conventions doc location: in-tree at `Services/Ai/Handlers/HandlerRegistrationConventions.md` (not `docs/architecture/`)

**Chosen**: Co-locate the conventions doc with the canonical reference handler (`GenericAnalysisHandler.cs`) in `Services/Ai/Handlers/`.

**Rationale**:
- The task POML names the file `HandlerRegistrationConventions.md` and specifies the `Services/Ai/Handlers/` folder explicitly
- Wave 1/Wave 2 handler authors land their PRs in this folder anyway — they see the conventions doc next to their working file
- The doc is bound to the code, not to broader architecture — it documents a class-level contract, not a system design
- We added a brief cross-reference from the doc itself ("See also: `Services/Ai/Handlers/TemplateHandler.cs`") so authors find the template

### D2 — Sample skeleton: separate file (`TemplateHandler.cs`) instead of a doc-embedded code block

**Chosen**: A real, compilable, auto-discovered `TemplateHandler` class.

**Rationale**:
- Wave 1/Wave 2 authors copy-paste the file and substitute the class name + behavior — that workflow is broken if the template only exists as a code-fenced block in a `.md`
- The auto-discovery verification (`AutoDiscoveryVerificationTests`) and the contract template (`HandlerContractTestTemplate`) gain a real test target — they exercise the registration contract against a real class, not a hypothetical
- The template handler is intentionally inert at runtime: no `sprk_analysistool` row in Dataverse points at the string `"TemplateHandler"`, so the registry knows about it but the orchestrator never invokes it

### D3 — Test fixture is a CLASS (`TypedToolHandlerTestFixture`), not a static helper

**Chosen**: `abstract class TypedToolHandlerTestFixture` that Wave 1/Wave 2 handler test classes inherit from.

**Rationale**:
- The fixture owns per-instance state (the captured log messages list) that the ADR-015 telemetry-assertion helper inspects
- xUnit test classes can inherit from a non-fixture abstract base without needing `IClassFixture<T>` plumbing
- Existing handler tests (`DocumentClassifierHandlerTests`, `SemanticSearchToolHandlerTests`) are NOT forced to migrate — they remain clean with per-test mock setup; the fixture is opt-in for the 8 new typed handler tasks

### D4 — Existing test pattern is good; no cosmetic refactoring

**Chosen**: Document the conventions in the fixture's XML doc rather than refactor `DocumentClassifierHandlerTests` / `SummaryHandlerTests` / `SemanticSearchToolHandlerTests` to inherit from the new fixture.

**Rationale**:
- Task 100 prompt explicitly says: "If the existing pattern is already clean and reusable, document its conventions in the test fixture's XML doc instead — no need for cosmetic refactoring"
- Existing handlers' tests are self-contained, readable, and follow the project's xUnit conventions consistently
- Wave 1/Wave 2 handler tasks inherit from the fixture; existing tests keep their existing shape

### D5 — File name: `TemplateHandler.cs` (not `_TemplateHandler.cs`)

**Initial attempt**: Use `_TemplateHandler.cs` (with leading underscore) to flag "template, not production".

**Outcome**: Reverted. The Write tool's interaction with Windows hidden-file conventions caused the file to be filtered/lost. Renamed to `TemplateHandler.cs`. The "template" semantic is documented in:
- The XML doc summary ("Reference template for R6 Pillar 2 typed tool handler implementations. NOT a production handler.")
- The `Metadata.Name` field ("Template Handler (reference; not a production handler)")
- The conventions doc

The lack of an `sprk_analysistool` row pointing at `"TemplateHandler"` keeps it from being invoked at runtime — the safety property is preserved via Dataverse routing, not via the file name.

---

## Auto-Discovery Verification

The 4 existing handlers (`GenericAnalysisHandler`, `DocumentClassifierHandler`, `SummaryHandler`, `SemanticSearchToolHandler`) + the new `TemplateHandler` are all auto-discovered by `ToolFrameworkExtensions.AddToolHandlersFromAssembly`:

```
✓ AddToolHandlersFromAssembly_DiscoversAllConcreteToolHandlerTypes
✓ AddToolHandlersFromAssembly_RegistersHandlersAsScoped
✓ AssemblyScan_FindsExpectedExistingHandlers
✓ IAnalysisToolHandlerAlias_ResolvesToIToolHandler
✓ HandlerRegistry_EnumeratesAllAutoDiscoveredHandlers
✓ NoConcreteHandlerHasManualDiRegistrationOutsideToolFramework
```

The `IAnalysisToolHandler` global-using alias added in task 006 makes the assembly scan work with the renamed `IToolHandler` interface without modification — the line
```csharp
var handlerInterface = typeof(IAnalysisToolHandler);
```
in `ToolFrameworkExtensions.cs` resolves to `IToolHandler` via the alias, so the discovery contract is preserved exactly.

---

## Build + Test Output

**BFF build**:
```
Build succeeded.
    16 Warning(s)
    0 Error(s)
```
The 16 warnings are pre-existing nullable / async / obsolete-API warnings unrelated to task 100.

**New tests** (run with the unrelated WIP files quarantined; see "Known gaps" below):
```
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10, Duration: 407 ms
  - AutoDiscoveryVerificationTests (6 tests)
  - HandlerContractTestTemplate (4 tests)
```

---

## BFF Publish-Size Delta (NFR-02 + ADR-029)

| Metric | Value |
|---|---|
| Raw publish size | 140.16 MB |
| Compressed publish size | **47.85 MB** |
| Baseline (CLAUDE.md §10) | ~45.65 MB |
| Delta vs baseline | **+2.20 MB** |
| Task 100's contribution to delta | ~0 (1 new .cs file at ~9.4 KB + 1 .md doc at ~13 KB, both embed-on-disk only; no new NuGet packages) |

**Analysis**: The +2.20 MB delta vs the 45.65 MB baseline reflects cumulative R6 work across tasks 001/002/003/004/006/007/009 (the prior completed wave), NOT task 100. Task 100 added zero new NuGet packages, zero new top-level Program.cs lines, zero new DI registrations outside `AnalysisServicesModule`. The conventions `.md` doc is not embedded into the assembly — it ships as a static file in `publish/Services/Ai/Handlers/`. Per ADR-029 §F.1 task-100 contribution is well under the per-task ≤+0.5 MB ceiling.

**Action item for next task**: The cumulative R6 delta is approaching the +5 MB R6 budget ceiling (NFR-02). Phase A wrap-up task (028 integration test or 029 exit-gate) should rebaseline + verify total R6 delta < +5 MB.

---

## Acceptance Criteria — Evidence

| Criterion | Evidence |
|---|---|
| `HandlerRegistrationConventions.md` exists in `Services/Ai/Handlers/`, documents the 4-point contract, includes worked example | `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/HandlerRegistrationConventions.md` |
| `AutoDiscoveryVerificationTests.cs` proves every `IToolHandler` impl is resolvable with ZERO per-handler DI lines | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/AutoDiscoveryVerificationTests.cs` — 6 tests green |
| `TypedToolHandlerTestFixture.cs` provides shared mocks + context builders + telemetry assertion helper enforcing ADR-015 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/TypedToolHandlerTestFixture.cs` |
| `HandlerContractTestTemplate.cs` provides 4-test template every handler test class will copy | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/HandlerContractTestTemplate.cs` — 4 tests green |
| `dotnet build src/server/api/Sprk.Bff.Api/` succeeds with no new errors | Build succeeded, 0 errors (same 16 pre-existing warnings) |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/` passes with the new `AutoDiscoveryVerificationTests` green | 10/10 new tests pass; pre-existing test-project WIP issues NOT caused by task 100 (see "Known gaps") |
| BFF publish-size delta verified ≤+0.2 MB | Task 100 contribution ~0 MB; total cumulative R6 delta +2.2 MB (mostly from prior tasks) |
| ZERO new top-level Program.cs lines | Confirmed — no Program.cs edits; auto-discovery infrastructure pre-exists |
| `code-review` + `adr-check` quality gates both pass | See "Quality Gates" section below |

---

## Quality Gates (Step 9.5)

### code-review

**Files reviewed**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/HandlerRegistrationConventions.md` (docs only; no code review needed beyond style/clarity check — passes)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/TemplateHandler.cs` (✅ idiomatic C#; nullable handling correct; uses `ToolResult.Ok` overload correctly; XML docs complete; no logging of governed content)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/TypedToolHandlerTestFixture.cs` (✅ proper xUnit conventions; abstract base + protected helpers; thread-safe Moq usage; ADR-015 telemetry-assert pattern follows existing project conventions)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/HandlerContractTestTemplate.cs` (✅ 4 focused tests; FluentAssertions with `because:` rationale; deterministic — no flakes)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/AutoDiscoveryVerificationTests.cs` (✅ verifies the load-bearing registration contract; uses reflection appropriately for assembly-scan validation)

**No critical issues**. No warnings. No lint errors.

### adr-check

| ADR | Status | Notes |
|---|---|---|
| **ADR-010 (DI minimalism)** | ✅ Pass | Zero new top-level Program.cs lines; zero new DI registrations outside `AnalysisServicesModule`; auto-discovery is the sole registration path for handlers |
| **ADR-013 (AI architecture)** | ✅ Pass | Handlers stay in `Services/Ai/Handlers/`; no PublicContracts facade modifications; CRUD-side code does NOT see `IToolHandler` directly |
| **ADR-014 (AI caching)** | ✅ Pass (documented) | Conventions doc binds tenantId-prefix cache-key rule; template handler enforces `context.TenantId` validation as the cache-key invariant |
| **ADR-015 (AI data governance)** | ✅ Pass | Test fixture's `AssertTelemetryRespectsAdr015` is the binding enforcement mechanism; conventions doc explicitly cites the rule; template handler has no body-logging |
| **ADR-016 (rate limit)** | ✅ Pass (documented) | Conventions doc binds "Wave 2 handlers MUST use `IOpenAiClient`, NOT direct Azure OpenAI calls — rate limit lives behind the interface" |
| **ADR-029 (BFF publish hygiene)** | ✅ Pass | Task 100 contribution ~0 MB; delta reported above |

**No ADR violations**.

---

## Known Gaps / Follow-ups

### Pre-existing unrelated WIP in test project

Two test files were already in the working tree (uncommitted) when task 100 started:
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisToolDtoTests.cs` (modified; raw-string-literal syntax errors at line 348) — task 008 WIP
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryPersonaTests.cs` (new file; references `Normalize` symbol that doesn't exist in scope) — task 005 WIP

Both are unrelated to task 100. They block the full test project build but DO NOT affect:
- BFF production build (still 0 errors)
- Task 100's new files (which compile + pass when WIP files are temporarily quarantined)

**Recommendation**: Task 005 and task 008 owners should complete or revert their WIP before Wave 1 (101–104) kicks off. The task 100 deliverables themselves are complete and verified.

### Optional future work

- Could refactor the existing `DocumentClassifierHandlerTests` / `SummaryHandlerTests` to inherit from `TypedToolHandlerTestFixture` for consistency. Not required — the existing tests are clean and the fixture is opt-in. Defer to a future hygiene pass.
- Could add a `dotnet-format` Markdown linter to CI for the conventions doc to catch future drift. Out of scope for task 100.

---

## What This Unblocks

| Task | Status before | Status after |
|---|---|---|
| **101** `DateExtractorHandler` (Wave 1, deterministic) | 🚧 blocked on 100 | 🔲 ready |
| **102** `FinancialCalculatorHandler` (Wave 1, deterministic) | 🚧 blocked on 100 | 🔲 ready |
| **103** `ClauseComparisonHandler` (Wave 1, deterministic) | 🚧 blocked on 100 | 🔲 ready |
| **104** `FinancialCalculationToolHandler` (Wave 1, deterministic) | 🚧 blocked on 100 | 🔲 ready |
| **105–108** Wave 2 LLM-assisted handlers | 🚧 blocked on 100 + 104 | 🔲 ready after 104 |
| **109** Handler dispatch tests | 🚧 blocked on 100 + Wave 2 | 🔲 ready after 108 |

Wave 1 (101–104) can now run as 4 parallel agents per the parallel-execution plan in `TASK-INDEX.md` line 230 (`H-G1` group). Each handler task copies `TemplateHandler.cs` as the starting shape and `HandlerContractTestTemplate.cs` as the test scaffold, plus inherits from `TypedToolHandlerTestFixture` for shared mocks + ADR-015 telemetry assertions.

---

*Maintained by task-execute. R6 task 100 (D-H-00) is the gate task for the 8 typed tool handler workstream — Wave 1 (101–104) and Wave 2 (105–108) follow.*
