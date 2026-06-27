# Task 028c — BFF Publish-Size Delta + Pattern A Migration Evidence

> **Status**: COMPLETE — within NFR-01
> **Baseline reference**: 46.28 MB compressed (W1 measurement 2026-06-24; reaffirmed by task 028a)

## Publish-size measurement

| Metric | Value |
|---|---|
| **Compressed publish size (post-028c)** | **46.29 MB** |
| Baseline (task 028a measurement) | 46.28 MB |
| **Delta** | **+0.01 MB** (~10 KB — within measurement precision) |
| NFR-01 ceiling | 60.00 MB |
| Headroom | 13.71 MB |

### Why ~zero delta?
028c modifies 4 existing files (3 services + 1 endpoints class). No new types, no new NuGet packages, no new transitive dependencies. The only additive content is ~15 lines of routing code per consumer (ResolveAsync call + Guid?-to-string conversion + fallback check). New tests (~250 lines across 4 files) are test-assembly only and do not ship.

### ADR-029 / NFR-01 status

- Well under +5 MB single-task threshold
- Cumulative 46.29 MB well under 55 MB architecture-review trigger
- No `<PublishTrimmed>` / `<PublishAot>` introduced
- No new package references

## Method
```bash
rm -rf src/server/api/Sprk.Bff.Api/publish deploy/api-publish deploy/api-publish-028c.zip
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
pwsh -Command "Compress-Archive -Path deploy/api-publish/* -DestinationPath deploy/api-publish-028c.zip -Force; (Get-Item 'deploy/api-publish-028c.zip').Length / 1MB"
```

## Per-service migration summary

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs` | Inject `IConsumerRoutingService`. Replace `_workspaceOptions.MatterPreFillPlaybookId` primary read with `await _consumerRouting.ResolveAsync(ConsumerTypes.MatterPreFill, …)`. Env-var read retained as FR-1R-06 deprecation-window fallback when `ResolveAsync` returns null. NFR-07 invariants preserved (45s timeout, `useAiPrefill`, `$choices`, public signature). |
| `src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs` | Same pattern as Matter — `ConsumerTypes.ProjectPreFill`. Env-var fallback retained. NFR-07 invariants preserved. |
| `src/server/api/Sprk.Bff.Api/Services/Workspace/WorkspaceAiService.cs` | Inject `IConsumerRoutingService`. Replace `_workspaceOptions.Value.AiSummaryPlaybookId` primary read with `ResolveAsync(ConsumerTypes.AiSummary, …)`. Env-var fallback retained. Graceful-degrade contract preserved — when routing AND env-var are both null, `BuildFallbackResponse` is still invoked so the workspace AI tile renders the template placeholder (no exception, no 500). |
| `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceFileEndpoints.cs` | Inject `IConsumerRoutingService` into the static `HandleSummarize` delegate (DI-by-parameter per ADR-001 minimal-API). Call `ResolveAsync(ConsumerTypes.SummarizeFile, consumerCode: "default", context: new RoutingContext { MimeType = mimeType })` — the first uploaded file's `ContentType` feeds the routing context so future `sprk_matchconditions` JSON predicates can route per MIME. Env-var fallback retained. FR-04 fail-fast preserved — `InvalidOperationException` thrown when both sources return null. |

## Test summary

| Test class | Pre-028c | Post-028c | Added | Status |
|---|---|---|---|---|
| `MatterPreFillServiceTests` | 9 | 11 | +2 (FR-1R-05 ctor + source) | Passing |
| `ProjectPreFillServiceTests` | 0 (none existed) | 8 | +8 (NEW — full migration contract) | Passing |
| `WorkspaceAiServiceTests` | 8 | 12 | +4 (FR-1R-05 ctor, happy path, env-var fallback, graceful-degrade preservation) | Passing |
| `WorkspaceFileEndpointsTests` | 6 | 9 | +3 (FR-1R-05 ctor param, ConsumerTypes constant + RoutingContext.MimeType source check, FR-04 fail-fast preservation) | Passing |
| **Total** | **23** | **40** | **+17** | **40/40 passing** |

Full filter `dotnet test --filter "FullyQualifiedName~MatterPreFill|FullyQualifiedName~ProjectPreFill|FullyQualifiedName~WorkspaceAiService|FullyQualifiedName~WorkspaceFileEndpoints"` reports 46 (some overlap from substring filter); per-class breakdown above is authoritative.

### Test fix necessitated by parallel 028d work
`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/EmailAnalysisIntegrationTests.cs` was failing to compile because the parallel 028d Pattern B sub-agent added `IConsumerRoutingService` to `AppOnlyAnalysisService` constructor but its test file had not been updated yet. A minimal one-line fix was applied (add `Mock<IConsumerRoutingService>` to the ctor argument list) so the test assembly compiles and my 028c tests can run. This is documented here for awareness; 028d's evidence should note this fix was already in place.

## Hardening per code-review S-5 (2026-06-24)

All 4 consumers use the compile-time `ConsumerTypes` constants — never literal strings:

| Consumer | Constant used |
|---|---|
| `MatterPreFillService` | `ConsumerTypes.MatterPreFill` (= `"matter-pre-fill"`) |
| `ProjectPreFillService` | `ConsumerTypes.ProjectPreFill` (= `"project-pre-fill"`) |
| `WorkspaceAiService` | `ConsumerTypes.AiSummary` (= `"ai-summary"`) |
| `WorkspaceFileEndpoints` | `ConsumerTypes.SummarizeFile` (= `"summarize-file"`) |

This is the compile-time typo defense recommended by the 2026-06-24 code review.

## FR-1R-04 MIME-aware routing demonstration

`WorkspaceFileEndpoints.RunSummarizePlaybookAsSSEAsync` now constructs a `RoutingContext` with `MimeType = files.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.ContentType))?.ContentType`. This passes the uploaded file's content type into the routing call so future `sprk_matchconditions` JSON predicates can route per MIME (e.g., NDA-specific PDF summarize playbook). When no file has a content type, `MimeType` stays null and the default routing record matches — preserving today's behavior for clients that don't set the header.

## NFR-07 invariant verification checklist (the two pre-fill services)

| Invariant | MatterPreFillService | ProjectPreFillService |
|---|---|---|
| Public `AnalyzeFilesAsync(files, userId, httpContext, ct)` signature | UNCHANGED | UNCHANGED |
| 45-second timeout (`TimeSpan.FromSeconds(45)`) | PRESERVED | PRESERVED |
| `useAiPrefill` flag / `$choices` envelope (frontend contract) | NOT TOUCHED | NOT TOUCHED |
| Internal playbook-ID resolution flow | env-var → routing-table primary + env-var fallback | env-var → routing-table primary + env-var fallback |
| Empty/missing playbook → empty pre-fill response | PRESERVED | PRESERVED |
| `_playbookLookup.GetByIdAsync(...)` call (downstream playbook record load) | UNCHANGED | UNCHANGED |

Pinned in source-text tests:
- `MatterPreFillServiceTests.MatterPreFillService_PreservesFortyFiveSecondTimeout_NFR07`
- `MatterPreFillServiceTests.MatterPreFillService_AnalyzeFilesAsync_PublicSignatureUnchanged_NFR07`
- `ProjectPreFillServiceTests.ProjectPreFillService_PreservesFortyFiveSecondTimeout_NFR07`
- `ProjectPreFillServiceTests.ProjectPreFillService_AnalyzeFilesAsync_PublicSignatureUnchanged_NFR07`

## Grep evidence (deviation from POML acceptance criterion — RATIONALE)

The POML's Step 9 acceptance criterion ("Grep over `src/server/api/Sprk.Bff.Api/Services/` for `WorkspaceOptions.MatterPreFillPlaybookId|ProjectPreFillPlaybookId|AiSummaryPlaybookId|SummarizePlaybookId` returns zero hits") was authored before the FR-1R-06 deprecation-window design settled. The parent agent's executing instructions explicitly require the env-var fallback to remain in each consumer:

> On null return: fall back to the existing typed-options value (graceful-degrade during the FR-1R-06 deprecation window).

The resulting reads — all guarded by `if (string.IsNullOrWhiteSpace(routedPlaybookId.ToString()))` — are documented as deprecation-window fallbacks in the same comment block. **Task 028e (next in the wave) is responsible for adding deprecation telemetry that warns when this fallback fires, and a future task will remove the reads entirely.** This task (028c) is the migration; 028e is the deprecation.

Current `Services/` (+ `Api/Workspace/`) reads of `*PlaybookId` env-var:
- `Services/Workspace/MatterPreFillService.cs:338` — `configuredPlaybookId = _workspaceOptions.MatterPreFillPlaybookId;` (fallback)
- `Services/Workspace/ProjectPreFillService.cs:305` — `configuredPlaybookId = _workspaceOptions.ProjectPreFillPlaybookId;` (fallback)
- `Services/Workspace/WorkspaceAiService.cs:363` — `configuredPlaybookId = _workspaceOptions.Value.AiSummaryPlaybookId;` (fallback)
- `Api/Workspace/WorkspaceFileEndpoints.cs:307` — `configuredPlaybookId = workspaceOptions.Value.SummarizePlaybookId;` (fallback)

Each read is the ONLY read in its file (down from 1 read pre-028c — same count, but the SEMANTIC role changed from primary to fallback). The grep delta in `Services/` is zero net new reads.

## Build status

- BFF API build: 0 errors, 17 warnings (unchanged from pre-028c baseline)
- Tests build: 0 errors, no new warnings

## Forward-looking

Task 028d (Pattern B — `SessionSummarizeOrchestrator` + `AppOnlyAnalysisService`) is in flight in parallel — `IConsumerRoutingService` injection visible in those files; tests still being authored. After 028d completes, 028e (env-var deprecation telemetry + Phase 1R exit gate grep) wraps the wave.
