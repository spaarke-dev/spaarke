# Wave 8 — CodeInterpreter Chat-Tool Migration

**Status**: COMPLETE
**Date**: 2026-06-08
**Predecessor**: `wave-07b-adapter-infra.md` (citations/widget envelope), `wave-07b-capability-filter.md` (capability gate)
**Cohort**: Wave 8 (4 parallel agents) — DocumentSearch / WebSearch / **CodeInterpreter** / LegalResearch

---

## TL;DR

Wave 8 migrates `CodeInterpreterTools` (2 LLM functions: `AnalyzeData`, `GenerateChart`) from hardcoded `SprkChatAgentFactory.ResolveTools` registration to a data-driven `IToolHandler` (`CodeInterpreterHandler`) backed by two `sprk_analysistool` rows (`CODE-ANALYZE` and `CODE-CHART`). The Wave 7b adapter post-processing infrastructure carries citations + (for chart) an `output_pane` `ChartViewer` widget envelope.

---

## Files created

| File | Purpose |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/CodeInterpreterHandler.cs` | Typed `IToolHandler` implementation; method dispatch via `sprk_configuration.method` (`"AnalyzeData"` / `"GenerateChart"`). |
| `infra/dataverse/sprk_analysistool-code-analyze-row.json` | Seed row: `sprk_toolcode = "CODE-ANALYZE"`, `sprk_requiredcapability = "code_interpreter"`. |
| `infra/dataverse/sprk_analysistool-code-chart-row.json` | Seed row: `sprk_toolcode = "CODE-CHART"`, `sprk_requiredcapability = "code_interpreter"`. |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/CodeInterpreterHandlerTests.cs` | 26 tests covering 4-point contract, both method dispatch paths, kill-switch, rate-limit, Wave 7b metadata, ADR-015 telemetry. |
| `projects/spaarke-ai-platform-unification-r6/notes/wave-08-code-interpreter-migration.md` | This note. |

**Out of scope (NOT modified)**: `SprkChatAgentFactory.cs`, `scripts/Seed-TypedHandlers.ps1`, `CodeInterpreterBridge`, `CodeInterpreterOptions`, `current-task.md`, `TASK-INDEX.md`.

---

## Two `sprk_analysistool` toolcodes

| `sprk_toolcode` | `sprk_name` | Method discriminator (`sprk_configuration.method`) | Widget |
|---|---|---|---|
| `CODE-ANALYZE` | SYS-Code Analyze Data | `"AnalyzeData"` | None (analysis is a text answer) |
| `CODE-CHART` | SYS-Code Generate Chart | `"GenerateChart"` | `output_pane` `ChartViewer` (carries base-64 PNG) |

Both rows: `sprk_handlerclass = "CodeInterpreterHandler"`, `sprk_availableincontexts = 100000001` (Chat only — matches the legacy hardcoded behavior), `sprk_requiredcapability = "code_interpreter"`.

---

## Resolved capability literal

`PlaybookCapabilities.CodeInterpreter = "code_interpreter"` (defined in `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/PlaybookCapabilities.cs` line 62). Dataverse option-set integer code: `100000008`. Both seed rows use the canonical lowercase snake_case string literal; the Wave 7b capability filter does case-insensitive comparison so admin typos like `"Code_Interpreter"` are tolerated, but the seed JSON uses the canonical form.

---

## Kill switch + rate limit verification

| Concern | Where preserved | How tested |
|---|---|---|
| **ADR-018 kill switch** (`CodeInterpreterOptions.Enabled`) | Inside `CodeInterpreterHandler.ExecuteChatAsync` BEFORE every sandbox invocation (mirrors legacy `CodeInterpreterTools.AnalyzeDataAsync` / `GenerateChartAsync`). Bridge is NEVER called when `Enabled = false`. | `ExecuteChatAsync_KillSwitchOff_AnalyzeData_ReturnsUnavailableMessage_NoBridgeCall` + same for `GenerateChart`: assert `bridgeCallCount == 0` AND `payload.Unavailable == true`. |
| **ADR-016 rate limit** (`SemaphoreSlim` bounded by `MaxConcurrency`) | Static `s_concurrencyGate` field on `CodeInterpreterHandler` (mirrors legacy `CodeInterpreterTools.s_concurrencyGate`). Initialised lazily on first construction; shared across all handler instances in the process. `await s_concurrencyGate.WaitAsync(timeout, ct)` before every bridge call; `Release()` in `finally`. | `ExecuteChatAsync_BoundsConcurrency_PerStaticGate`: queues 4 concurrent calls + a release latch; asserts observed in-flight count never exceeds 3 (below the queued count of 4, proving the gate exists). |
| **ADR-015 data governance** (only caller-supplied excerpts forwarded; no external auto-fetch) | `CodeInterpreterHandler.ExecuteAnalyzeDataAsync` / `ExecuteGenerateChartAsync` invoke `InvokeBridgeAsync(prompt, ct)` where `prompt` is built deterministically from the caller's `data`/`question` (or `dataSeries`/`chartType`) only. No `IHttpClient*`, `IUrlFetcher`, or similar dependencies. Telemetry logs lengths + bucket flags only. | `ExecuteChatAsync_AnalyzeData_ForwardsCallerDataOnly_ToBridge` + `ExecuteChatAsync_GenerateChart_ForwardsCallerDataSeriesOnly_ToBridge`: capture the prompt forwarded to the bridge; assert it contains only caller-supplied substrings. `Telemetry_RespectsAdr015_*` tests stash sentinel strings into data/question/output and assert via `AssertTelemetryRespectsAdr015` that none of them appear in captured log messages. |

---

## Wave 7b metadata envelope

| Method | `Metadata[Citations]` | `Metadata[Widget]` |
|---|---|---|
| `AnalyzeData` | 1 × `ToolResultCitation` (`SourceType = "code-interpreter"`, `SourceName = "Code Interpreter Data Analysis"`, excerpt = first 200 chars of sandbox output) | NOT set (analysis is a plain text answer; no widget) |
| `GenerateChart` | 1 × `ToolResultCitation` (`SourceType = "code-interpreter-chart"`, `SourceName = "AI-generated {chartType} chart"`) | 1 × `ToolResultWidget` (`PaneType = "output_pane"`, `WidgetType = "ChartViewer"`, `Data = { chartType, hasChart, chartBase64, outputLength }`) |

The chat-visible text (`data.content`) still embeds the chart inline as a markdown image data URI (`![Chart](data:image/png;base64,...)`) — preserving the pre-Wave-8 user-visible rendering AND adding the structured widget for richer pane rendering.

---

## Build + test outcomes

- `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors, 16 warnings (baseline)**
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~CodeInterpreterHandlerTests"` → **26 passed, 0 failed, 0 skipped**
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Sprk.Bff.Api.Tests.Services.Ai.Handlers"` → **482 passed, 0 failed** (no regressions in the broader handler test surface)

---

## Surprises / design notes

### CodeInterpreterBridge is `sealed`; Moq cannot mock it

Both `CodeInterpreterBridge` and its dependency `AgentServiceClient` are `sealed`. Moq cannot subclass either to provide a test double, and the prompt forbids modifying either. The handler is also `sealed` by convention.

**Resolution** (no ADR change; no production-code semantics change):

1. Changed `CodeInterpreterHandler` from `sealed` to non-sealed — only impact is that subclassing is now possible (no production callsite subclasses it).
2. Added a `protected internal virtual Task<CodeInterpreterResult> InvokeBridgeAsync(...)` indirection that defaults to calling `_bridge.InvokeCodeInterpreterAsync`. Production behaviour is unchanged.
3. Test project defines `TestableCodeInterpreterHandler : CodeInterpreterHandler` and overrides `InvokeBridgeAsync` with a `Func<string, CancellationToken, Task<CodeInterpreterResult>>` provided per-test.
4. Bridge instance is constructed via `FormatterServices.GetUninitializedObject(typeof(CodeInterpreterBridge))` (with `#pragma warning disable SYSLIB0050` + comment explaining why) so the handler's constructor null-check passes; the seam ensures the uninitialized bridge is never actually invoked.

This is a minimal-impact compromise that keeps the handler class shape clean while making it testable. The alternative — extracting an `ICodeInterpreterInvoker` interface — would require adding a new DI registration (NFR-03 / ADR-010 friction) and a thin wrapper.

### Static concurrency gate is shared across handler instances

The static `s_concurrencyGate : SemaphoreSlim` field is process-wide and initialised by the FIRST `CodeInterpreterHandler` constructed. This exactly mirrors the legacy `CodeInterpreterTools.s_concurrencyGate` design. Test consequence: the test `ExecuteChatAsync_BoundsConcurrency_PerStaticGate` cannot reliably assert `MaxConcurrency=1` (which would require the test to be the first constructor in the process). Instead it queues 4 concurrent calls and asserts the bridge never sees more than 3 in flight — proving the gate exists and bounds concurrency below the queued-call count without depending on test ordering.

### `SupportedInvocationContexts = Chat` (not `Both`)

The legacy `CodeInterpreterTools` was registered exclusively for chat. The 11 production node executors (NFR-08) do not include a Code Interpreter executor, so playbook orchestration has no call path. To preserve this boundary the handler:

- Sets `SupportedInvocationContexts = InvocationContextKind.Chat`
- Implements `ExecuteAsync` (playbook path) as a defensive validation error rather than the canonical dispatcher — `ExecuteAsync_PlaybookContext_ReturnsValidationError_NoBridgeCall` asserts the bridge is not called from the playbook path
- Sets `sprk_availableincontexts = 100000001` (Chat only) on both seed rows

### Chart payload is potentially large but kept as widget data (not stop-and-surface)

The base-64 PNG chart payload can be tens of KB. The Wave 7b envelope pattern uses `ToolResultWidget.Data` as a plain `object`; the adapter serializes it through System.Text.Json. This is consistent with `KnowledgeRetrievalHandler` emitting widget data with chunk text content. The chart payload is **AI-generated sandbox output**, not user content — ADR-015 governs user content. The widget data + citation excerpt are the standard envelope shapes.

Alternative considered: store chart in blob storage, return URL via widget metadata. **Rejected** because (a) it requires new infrastructure for a feature already protected by ADR-018 kill switch + ADR-016 rate limit + per-tenant capability gate; (b) the existing `CodeInterpreterTools` already inlined the base-64 in the chat-visible text; (c) the citation excerpt's `MaxExcerptLength = 200` cap already bounds the citation envelope. Widget Data is unbounded by design (the frontend decides what payload size to accept).

No stop-and-surface was needed.

---

## Hardcoded factory block removal (next step — main session)

Main session removes `SprkChatAgentFactory.ResolveTools` lines 1066–1105 (`// --- CodeInterpreterTools ---` block) after all Wave 8 PRs land. The capability gate (Wave 7b infrastructure + `sprk_requiredcapability = "code_interpreter"` on the two seed rows) preserves the security boundary that block enforced — playbooks without the `code_interpreter` capability still do NOT see the tool in the LLM function schema.

---

## ADR / NFR check

- **ADR-010 (DI minimalism)**: Handler is auto-discovered via assembly scan. ZERO new top-level DI registrations. `CodeInterpreterBridge` + `IOptions<CodeInterpreterOptions>` were already registered for the legacy `CodeInterpreterTools`.
- **ADR-013 (PublicContracts boundary)**: Handler lives in `Services/Ai/Handlers/`, NOT in `Services/Ai/PublicContracts/`. CRUD-side code never injects this handler.
- **ADR-014 (per-tenant scoping)**: Handler validates `TenantId` on both chat and playbook paths. Sandbox itself is tenant-agnostic (ephemeral threads invalidated post-call by the bridge); tenant check exists for log correlation.
- **ADR-015 (data governance)**: Only caller-supplied `data`/`question`/`dataSeries` are forwarded to the sandbox. No external auto-fetch. Telemetry logs lengths + counts + flags only — sentinel-string tests verify no leakage.
- **ADR-016 (rate limit)**: Preserved verbatim from `CodeInterpreterTools` — static `SemaphoreSlim` bounded by `MaxConcurrency`.
- **ADR-018 (kill switch)**: Preserved verbatim — `CodeInterpreterOptions.Enabled` checked before EVERY invocation; disabled state returns user-readable string (no exception).
- **ADR-029 / NFR-02 (publish size)**: BCL-only implementation; per-handler delta ≤+0.1 MB target. No new NuGet packages.
- **NFR-03 (no new ADRs)**: PASS — additive change within existing ADR envelope.
- **NFR-04 (no Agent Framework)**: PASS — no `Microsoft.Agents.*` references introduced.
- **NFR-12 (LLM tool descriptions load-bearing)**: Seed-row `sprk_description` text preserved verbatim from the legacy hardcoded registration so the LLM's tool-selection behavior is unchanged.

---

## Next-wave handoff

Wave 8 sibling tools (DocumentSearch, WebSearch, LegalResearch) follow the same pattern. The static-gate-across-handler-instances pattern documented above is unique to CodeInterpreter and LegalResearch (the two tools whose legacy implementations used static gates); the other tools use per-instance state.
