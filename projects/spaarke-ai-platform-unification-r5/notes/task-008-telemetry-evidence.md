# Task 008 — D1-08 Telemetry events + cost observability — evidence

> **Status**: Implementation complete (sub-agent wave); main session will run build, tests, publish-size verification, and quality gates.
> **Completed**: 2026-06-04
> **Estimated effort**: 3h · **Actual effort**: ~2h (sub-agent wave)

---

## 1. Locked event schema (downstream contract for Phase 3 task 042 / D3-03)

> **Treat as a public-API surface from this commit forward.** Renames after this task ships require coordinated dashboard updates per task POML §`<constraints>` "project — schema stability".

Meter name: **`Sprk.Bff.Api.R5Summarize`** · ActivitySource: same name.

| Instrument                          | Type              | Unit            | Dimensions (BOUNDED)                                                                 |
|-------------------------------------|-------------------|-----------------|--------------------------------------------------------------------------------------|
| `r5.summarize.invocation`           | `Counter<long>`   | `{invocation}`  | `path`, `completion_status`, `tenant.id` (optional)                                  |
| `r5.summarize.file_count`           | `Histogram<long>` | `{file}`        | `path`, `completion_status`, `tenant.id` (optional)                                  |
| `r5.summarize.total_tokens`         | `Histogram<long>` | `{token}`       | `path`, `completion_status`, `tenant.id` (optional)                                  |
| `r5.summarize.latency_ms`           | `Histogram<double>` | `ms`          | `path`, `completion_status`, `tenant.id` (optional)                                  |
| `r5.session_files.index_size`       | `Histogram<long>` | `{document}`    | `phase`, `tenant.id` (optional)                                                      |

### Bounded enum dimensions

- **`path`** ∈ { `agent_tool` | `direct_endpoint` } — invocation entry point. Both literals route to the SAME counter (load-bearing single-event-stream invariant).
- **`completion_status`** ∈ { `success` | `failed` | `declined` | `cancelled` } — terminal status of the invocation.
- **`phase`** ∈ { `post_write` | `post_evict` | `post_cleanup` } — lifecycle phase at which the session-files index was observed.

### Cardinality discipline (enforced)

Invalid enum values throw `ArgumentException` at the call site (loud-fail during development; cardinality safety in production). HashSet whitelists in `R5SummarizeTelemetry` are the enforcement mechanism.

**EXCLUDED from metric dimensions** (per ADR-014 + ADR-015):
- `sessionId` (high cardinality — belongs on the Activity / span only)
- Correlation IDs (belong on the Activity / span only)
- User IDs, file names, prompt text, document content — never PII / customer data in metric dimensions.

`tenant.id` is the ONLY identifier dimension permitted, matching the `RagTelemetry.RecordRagSearchSuccess` precedent (ADR-014).

---

## 2. Sample App Insights / Kusto queries (for D3-03 dashboard authors)

```kusto
// Invocation path mix
customMetrics
| where name == "r5.summarize.invocation"
| summarize count() by tostring(customDimensions["path"])

// Completion status breakdown
customMetrics
| where name == "r5.summarize.invocation"
| summarize count() by tostring(customDimensions["completion_status"])

// Per-tenant per-invocation token-budget burn (cost dashboards, spec NFR-06)
customMetrics
| where name == "r5.summarize.total_tokens"
| summarize p50=percentile(value, 50), p95=percentile(value, 95) by tostring(customDimensions["tenant.id"])

// Cleanup-cadence tuning input (spec NFR-02)
customMetrics
| where name == "r5.session_files.index_size"
| summarize avg(value) by tostring(customDimensions["phase"]), bin(timestamp, 1h)
```

---

## 3. Downstream consumer obligations

| Task | Consumes / emits                                                                                                                   |
|------|------------------------------------------------------------------------------------------------------------------------------------|
| 007  | MAY call `RecordSessionFilesIndexSize(phase: "post_evict" \| "post_cleanup", documentCount, tenantId)` from the cleanup `IHostedService` after each eviction / sweep. Note: task 007 emits its own `r5.session_files_cleanup.run` event via `AiTelemetry` for run-level metrics; index-size observations via `R5SummarizeTelemetry` are independent. |
| 012  | INJECT `R5SummarizeTelemetry` into `SessionSummarizeOrchestrator`. Call `RecordSummarizeInvocation(...)` at the end of every invocation regardless of completion status. |
| 014  | The `POST /api/ai/chat/sessions/{id}/summarize` endpoint must pass `path: "direct_endpoint"` through to the orchestrator's telemetry call. |
| 015  | `InvokeSummarizePlaybookTool` must pass `path: "agent_tool"` through to the orchestrator's telemetry call. Both paths converge on the SAME counter — verified by unit test `BothInvocationPaths_RecordViaSameCounter`. |
| 042  | Phase 3 D3-03 dashboard authors write Kusto queries against the locked schema above. |

Task 003 RAG indexing pipeline MAY call `RecordSessionFilesIndexSize(phase: "post_write", ...)` after successful session-file indexing — this gives a complete post-write/post-evict/post-cleanup trio for tuning storage growth thresholds.

---

## 4. Files modified / created

### Created
- `src/server/api/Sprk.Bff.Api/Telemetry/R5SummarizeTelemetry.cs` — singleton telemetry class. Mirrors the `AiTelemetry` / `RagTelemetry` structural template (private `Meter`, public static `ActivitySource`, `MeterName` const, instrument fields, IDisposable).
- `tests/unit/Sprk.Bff.Api.Tests/Telemetry/R5SummarizeTelemetryTests.cs` — 8 test cases:
  - `RecordSummarizeInvocation_WithValidEnums_EmitsExpectedCounter`
  - `RecordSummarizeInvocation_WithInvalidPath_ThrowsArgumentException`
  - `RecordSummarizeInvocation_WithInvalidCompletionStatus_ThrowsArgumentException`
  - `RecordSummarizeInvocation_WithNullTenantId_DoesNotEmitTenantDimension`
  - `RecordSessionFilesIndexSize_WithValidPhase_RecordsHistogram`
  - `RecordSessionFilesIndexSize_WithInvalidPhase_ThrowsArgumentException`
  - **`BothInvocationPaths_RecordViaSameCounter`** (load-bearing invariant)
  - `RecordSummarizeInvocation_AlsoEmits_FileCount_TotalTokens_Latency_Histograms`

### Modified
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/TelemetryModule.cs` — adds `metrics.AddMeter(R5SummarizeTelemetry.MeterName)` and `tracing.AddSource(R5SummarizeTelemetry.MeterName)`.
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — adds `services.AddSingleton<R5SummarizeTelemetry>()` UNCONDITIONALLY at the top of `AddAnalysisServicesModule` (per R5 CLAUDE.md §3.2 — no new feature flags; sidesteps asymmetric-registration anti-pattern per CLAUDE.md §10 F.1).

### Untouched (intentional)
- `Program.cs` — ZERO new top-level lines (per ADR-010 + R5 CLAUDE.md §3.3).
- `Sprk.Bff.Api.csproj` — no new package references (reuses existing OpenTelemetry; expected publish-size delta ≈ 0 MB).

---

## 5. Verification deferred to main session

Per sub-agent wave coordination rules, the following verification steps are explicitly deferred to the main session (sub-agent does CODE AUTHORING only):

- [ ] `dotnet build src/server/api/Sprk.Bff.Api/` — zero new warnings / zero new errors
- [ ] `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~R5SummarizeTelemetryTests"` — all 8 cases pass
- [ ] `dotnet test tests/unit/Sprk.Bff.Api.Tests/` — no previously-passing test regresses
- [ ] `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` — measure compressed output; expected ≈ 45.65 MB (baseline) ± rounding; expected delta ≈ 0 MB
- [ ] `dotnet list package --vulnerable --include-transitive` — verify no NEW HIGH-severity CVE (expected no-op, no new packages)
- [ ] `code-review` + `adr-check` quality gates at Step 9.5

---

## 6. Coordination with task 007 (parallel sub-agent)

Task 007 (P1-G5 parallel-safe sibling) emits its own `r5.session_files_cleanup.run` event via the existing `AiTelemetry` (per its POML §93). No naming overlap with `R5SummarizeTelemetry` events. Task 007 MAY optionally consume `R5SummarizeTelemetry.RecordSessionFilesIndexSize(phase: "post_evict" | "post_cleanup", ...)` as an additional observation, but it is NOT required by task 007's POML — that is a forward-compat surface this task ships for downstream consumers.

No event-naming collisions. No code-path collisions. Both tasks register independent telemetry singletons.

---

## 7. ADR / constraint conformance

| ADR / constraint | How this task complies |
|---|---|
| ADR-010 DI minimalism | All registration in `AnalysisServicesModule.cs` + `TelemetryModule.cs`; zero new `Program.cs` lines. |
| ADR-014 Tenant isolation | `tenant.id` as low-cardinality dimension (matches `RagTelemetry` precedent). `sessionId` excluded. |
| ADR-015 No PII in telemetry | No document content, file contents, prompt text, user IDs, or file names as dimensions or log fields. |
| ADR-018 Flag Scope Discipline | No new feature flags. `R5SummarizeTelemetry` is unconditionally registered. |
| ADR-029 BFF publish hygiene | No new packages. Expected delta ≈ 0 MB; main session to confirm. |
| ADR-030 PaneEventBus | No PaneEventBus changes (BFF-side OpenTelemetry only). |
| R5 CLAUDE.md §3.1 reuse | Mirrors `AiTelemetry` / `RagTelemetry` structural pattern; no parallel telemetry framework introduced. |
| R5 CLAUDE.md §3.2 no new flags | Unconditional singleton registration. |
| R5 CLAUDE.md §3.3 DI minimalism | In `AnalysisServicesModule` + `TelemetryModule`; zero `Program.cs` lines. |
| R5 CLAUDE.md §3.7 test obligation | 8 unit tests authored in `tests/unit/Sprk.Bff.Api.Tests/Telemetry/`. |
| CLAUDE.md §10 BFF Placement Justification | Telemetry instrumentation is intrinsic to BFF request lifecycle; cannot be cleanly extracted into a sidecar. Zero meaningful publish-size budget consumed. |
| CLAUDE.md §10 F.1 asymmetric-registration | Sidestepped by registering UNCONDITIONALLY (no `if (flag) { ... }` block introduced; no Null-Object mirror needed because the telemetry surface is harmless when unused). |
