# Task 020 BFF Publish Delta + FR-03 Pattern B Migration (AppOnlyAnalysisService + ChatContextMappingService)

> **Generated**: 2026-06-22
> **Task**: 020 — Migrate Pattern B backend name-resolve consumers to stable ID (Wave 1-F)
> **Wave**: 1-F (depends on Waves 1-D + 1-E; FR-03 Pattern B BFF consumers)
> **Phase 0 baseline**: 44.75 MB compressed
> **Post-Wave 1-E baseline (per `018-bff-publish-delta.md`)**: 44.75 MB compressed

---

## Identified stable IDs (Pattern B execution-path consts)

| Playbook name | `sprk_playbookid` (stable alt-key) | Source |
|---|---|---|
| **Document Profile** | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` | POML list (task 014 backfill evidence) — same GUID as `WorkspaceOptions.AiSummaryPlaybookId` (task 018) |
| **Email Analysis** | `bc71facf-6af1-f011-8406-7ced8d1dc988` | DEV Dataverse MCP query 2026-06-22 (`SELECT sprk_name, sprk_playbookid FROM sprk_analysisplaybook WHERE sprk_name = 'Email Analysis'`) — name was NOT in the POML's listed 5 production playbooks |

Both backfilled in DEV `sprk_playbookid` per task 014 (mirror of `sprk_analysisplaybookid` PK at seed time). Same GUIDs valid across DEV/QA/PROD (admin re-seeds on solution import).

---

## Migration shape chosen

**Class-level `private const string` + `IPlaybookLookupService.GetByIdAsync`** — per POML constraint "for execution-path values, use class-level `const string`" (NOT typed-options, per project rule "DO NOT add typed options to `WorkspaceOptions.cs` in this task").

- Inject `IPlaybookLookupService` (already registered Scoped in `FinanceModule.cs:115` — global).
- Add two `private const string` class-level fields (`DocumentProfilePlaybookId`, `EmailAnalysisPlaybookId`) — both immutable opaque GUIDs.
- Introduce a single `ResolvePlaybookAsync(string playbookName, CancellationToken)` private method as the convergence point — both `AnalyzeDocumentAsync` (former line 166) and `ExecutePlaybookAnalysisAsync` (former line 364) delegate here.
- Well-known names (`DefaultPlaybookName` == `"Document Profile"`, `EmailAnalysisPlaybookName` == `"Email Analysis"`) resolve via `_playbookLookup.GetByIdAsync(stableId, ct)`.
- Custom-override names (test fixtures, future custom playbooks) still resolve via `_playbookService.GetByNameAsync` (legacy by-name path retained for the FR-03 deprecation window; task 024 owns the `/by-name/` endpoint deprecation telemetry; this code does NOT call the `/by-name/` HTTP endpoint, it calls the in-process `IPlaybookService.GetByNameAsync`).

**Public interface contract preserved**: `IAppOnlyAnalysisService.AnalyzeDocumentAsync(Guid, string? playbookName, CancellationToken)`, `IAppOnlyAnalysisService.DefaultPlaybookName` const, and `AppOnlyAnalysisService.EmailAnalysisPlaybookName` const are all unchanged — consumers (`ProfileSummaryWorker`, `ProfileSummaryJobHandler`, `AppOnlyDocumentAnalysisJobHandler`, `EmailAnalysisJobHandler`) require no changes.

---

## ChatContextMappingService outcome — NO CHANGES NEEDED

`ChatContextMappingService.cs` was scanned per POML Step 3. **It has NO literal-name `GetByNameAsync` call sites.** Its design is fundamentally different — it resolves playbooks dynamically from the `sprk_aichatcontextmapping` Dataverse entity (entityType + pageType → playbook lookup column → linked entity name for display). It is data-driven (not name-string-driven) and already uses the canonical `sprk_playbookid` lookup column shape. **No refactor required for Pattern B compliance** — the literal-name lookup anti-pattern the POML targets does not exist in this service. Documented here so future audits don't re-open the question.

---

## Files modified

| Path | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/AppOnlyAnalysisService.cs` | Added `IPlaybookLookupService` injection; added 2 stable-ID consts; added `ResolvePlaybookAsync` convergence method; refactored 2 call sites (former lines 166, 364) to use it |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/EmailAnalysisIntegrationTests.cs` | Added `IPlaybookLookupService` mock + stable-ID const; stubbed `_playbookLookupMock.GetByIdAsync(EmailAnalysisPlaybookId, …)` in 2 test setups; replaced 1 verification assertion |

No changes to:
- `Services/Ai/Chat/ChatContextMappingService.cs` (rationale above)
- `Services/Ai/IAppOnlyAnalysisService.cs` (interface contract preserved)
- `Infrastructure/DI/AnalysisServicesModule.cs` (auto-resolution; DI graph picks up new ctor param)
- `WorkspaceOptions.cs` (POML constraint forbids typed-options addition for this task)
- `Workers/Office/ProfileSummaryWorker.cs`, `Services/Ai/Jobs/*JobHandler.cs` (interface contract preserved; no changes needed)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/ChatContextMappingServiceTests.cs` (service unchanged)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Jobs/AppOnlyDocumentAnalysisJobHandlerTests.cs` (tests `IAppOnlyAnalysisService` mock, not the concrete; unaffected by ctor change)

---

## Build + test verification

| Command | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | ✅ Build succeeded — 0 errors, 16 warnings (all pre-existing/unrelated) |
| `dotnet test … --filter "EmailAnalysis\|ChatContextMapping\|AppOnlyDocumentAnalysis"` | ✅ 74 passed, 5 skipped (pre-existing skips for playbook-orchestration-dependent tests; not regressions), 0 failed |
| `grep "by-name\|GetByNameAsync" AppOnlyAnalysisService.cs` | ✅ Remaining hits are inside the legitimate fallback method (custom-playbook path retained per FR-03 deprecation window) or comments |
| `grep "by-name\|GetByNameAsync" ChatContextMappingService.cs` | ✅ 0 matches |

---

## Publish-size measurement (NFR-01 enforcement)

```powershell
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish-020/
Compress-Archive -Path 'deploy/api-publish-020/*' -DestinationPath 'deploy/api-publish-020.zip' -Force
(Get-Item 'deploy/api-publish-020.zip').Length / 1MB
# → 44.7554 MB
```

| Measurement | Value |
|---|---|
| **Compressed publish size (task 020)** | **44.755 MB** |
| **Prior baseline (post-Wave 1-E task 018)** | 44.75 MB |
| **Delta** | **~+0.005 MB (≈5 KB)** — negligible (one additional ctor param wiring + ~120 LOC of comments/method body; no new package references) |
| **NFR-01 ceiling** | 60 MB |
| **Headroom** | **~15.24 MB (25.4%)** |
| **Single-task escalation threshold (+5 MB)** | ✅ Cleared by orders of magnitude |
| **Cumulative architecture-review threshold (55 MB)** | ✅ Cleared |
| **HARD STOP threshold (60 MB)** | ✅ Cleared |

**Verdict**: NFR-01 compliant. No new HIGH-severity CVEs introduced (no new package references added).

---

## ADR compliance summary

| ADR | Status | Notes |
|---|---|---|
| **ADR-010** (DI minimalism) | ✅ | Direct constructor injection of `IPlaybookLookupService`; no `IServiceProvider.GetService<T>()` |
| **ADR-013** (AI facade boundary) | ✅ | `AppOnlyAnalysisService` is inside `Services/Ai/`; uses existing `IPlaybookLookupService` from the AI facade |
| **ADR-014** (AI caching) | ✅ | `IPlaybookLookupService.GetByIdAsync` has built-in 1-hour `IMemoryCache` per `PlaybookLookupService.cs:32` |
| **ADR-018** (typed options) | ✅ | Class-level `const string` chosen per POML Pattern B (execution-path values, not configuration) — explicit constraint forbidding typed-options for THIS task |
| **ADR-029** (publish hygiene) | ✅ | +0.005 MB delta; well under the +5 MB single-task threshold |
| **FR-03 (spec)** | ✅ | Well-known names (Document Profile, Email Analysis) now resolve via stable ID; custom-name fallback retained for deprecation window per FR-03 contract |
| **NFR-02 (spec)** | ✅ | Playbook GUIDs unchanged; only consumer's resolution mechanism changes |
| **Project rule** (no `WorkspaceOptions` add) | ✅ | No typed-options addition; class-level const used per Pattern B |
| **Project rule** (don't modify `/by-name/` backend endpoint) | ✅ | No endpoint modifications; only the in-process service call path changed |

---

## Wave 1-F parallel-safety verification

Task 021 (frontend Pattern B) ran in parallel. Task 020 (this) stayed strictly within:
- `src/server/api/Sprk.Bff.Api/Services/Ai/AppOnlyAnalysisService.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/EmailAnalysisIntegrationTests.cs`

Zero overlap with frontend file paths.

---

## Recommended TASK-INDEX status

`Task 020` → ✅ (complete). Build OK, tests pass, publish-size compliant, ADRs satisfied, Pattern B migration shape matches the canonical pattern (`SessionSummarizeOrchestrator` task 015), with the well-justified deviation that the public name-based interface contract was preserved (per the consumer count — 4+ callers — and the FR-03 deprecation window).

## Notes for downstream tasks

- **Task 024** (by-name deprecation telemetry): The legacy `IPlaybookService.GetByNameAsync` path inside `ResolvePlaybookAsync` is the surface task 024 will instrument. The custom-playbook path logs an INFO message in this service ("resolving custom playbook '{Name}' via legacy by-name path") which is suitable for task 024's telemetry hook point.
- **Future cleanup** (post-deprecation window): Once task 024 confirms zero custom-name calls in production, the entire `_playbookService` field + ctor param + `ResolvePlaybookAsync` legacy branch can be removed from this service. Both `DefaultPlaybookName` and `EmailAnalysisPlaybookName` public consts can stay as documentation; internally everything becomes pure stable-ID resolution.
- **Test resurrection**: The 5 skipped tests in `EmailAnalysisIntegrationTests.cs` predate this task (orchestration pipeline mock complexity) and are not affected by the Pattern B refactor. Consider re-enabling them in a future task that completes the playbook orchestration mock setup.
