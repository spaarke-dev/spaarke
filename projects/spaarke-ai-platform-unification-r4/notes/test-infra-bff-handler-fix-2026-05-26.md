# Test infra: BFF handler test compile-error fix

**Task**: 069 — `tests/unit/Sprk.Bff.Api.Tests/` 7 pre-existing compile errors
**Date**: 2026-05-26
**Branch**: `work/spaarke-ai-platform-unification-r4`
**Rigor**: STANDARD (test fixes only; no production code modified)

---

## Summary

Original task scope (7 errors in 3 files): **FIXED**. All seven errors were namespace drift from a production refactor that moved `EmailAnalysisJobHandler`, `AppOnlyDocumentAnalysisJobHandler`, `EmbeddingMigrationService`, and `EmbeddingMigrationOptions` into `Sprk.Bff.Api.Services.Ai.Jobs` (from the older `Sprk.Bff.Api.Services.Jobs` / `Sprk.Bff.Api.Services.Jobs.Handlers` namespaces). Tests still imported the old namespaces. Fix is one-line `using` directive addition per file.

However, once the original 7 errors were resolved, **additional 69 previously-masked compile errors surfaced** across ~17 other test files. These are **out of scope** for task 069 (which was scoped to the 3 named files per R4 sub-agent reports) and require a separate follow-up task. See "Escalation" section below.

---

## 7-error catalog

| # | File | Line | Diagnostic | Type | Classification | Fix |
|---|---|---|---|---|---|---|
| 1 | `Services/Ai/EmailAnalysisIntegrationTests.cs` | 669,35 | CS0246 | `EmailAnalysisJobHandler` not found | FIXABLE-IN-TEST | Add `using Sprk.Bff.Api.Services.Ai.Jobs;` |
| 2 | `Services/Ai/EmailAnalysisIntegrationTests.cs` | 670,22 | CS0246 | `EmailAnalysisJobHandler` not found (same type, second usage) | FIXABLE-IN-TEST | (covered by fix #1) |
| 3 | `Services/Jobs/AppOnlyDocumentAnalysisJobHandlerTests.cs` | 22,35 | CS0246 | `AppOnlyDocumentAnalysisJobHandler` not found | FIXABLE-IN-TEST | Replace `using Sprk.Bff.Api.Services.Jobs.Handlers;` with `using Sprk.Bff.Api.Services.Ai.Jobs;` |
| 4 | `Services/Jobs/AppOnlyDocumentAnalysisJobHandlerTests.cs` | 23,22 | CS0246 | `AppOnlyDocumentAnalysisJobHandler` not found (same type) | FIXABLE-IN-TEST | (covered by fix #3) |
| 5 | `Services/Jobs/EmbeddingMigrationServiceTests.cs` | 30,9 | CS0246 | `EmbeddingMigrationOptions` not found | FIXABLE-IN-TEST | Add `using Sprk.Bff.Api.Services.Ai.Jobs;` |
| 6 | `Services/Jobs/EmbeddingMigrationServiceTests.cs` | 29,13 | CS0246 | `EmbeddingMigrationService` not found | FIXABLE-IN-TEST | (covered by fix #5) |
| 7 | `Services/Jobs/EmbeddingMigrationServiceTests.cs` | 20,35 | CS0246 | `EmbeddingMigrationService` not found | FIXABLE-IN-TEST | (covered by fix #5) |

## Root cause (per error)

All 7 errors share the **same** root cause: **namespace reorganization during a prior production refactor (likely R3 or earlier)** that moved AI-related job handlers from `Sprk.Bff.Api.Services.Jobs.Handlers` (legacy) and `Sprk.Bff.Api.Services.Jobs` (also legacy for these specific classes) into the canonical `Sprk.Bff.Api.Services.Ai.Jobs` namespace. The test files were not updated at the time.

Production-side verification:
```
src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/EmbeddingMigrationService.cs:10:namespace Sprk.Bff.Api.Services.Ai.Jobs;
src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/EmailAnalysisJobHandler.cs:8:namespace Sprk.Bff.Api.Services.Ai.Jobs;
src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/AppOnlyDocumentAnalysisJobHandler.cs:8:namespace Sprk.Bff.Api.Services.Ai.Jobs;
```

`EmbeddingMigrationOptions` is also declared in `EmbeddingMigrationService.cs:15` within the same `Sprk.Bff.Api.Services.Ai.Jobs` namespace.

## Fixes applied (diff summary)

Three test files updated; each gains one `using` directive (`Sprk.Bff.Api.Services.Ai.Jobs`). One file (`AppOnlyDocumentAnalysisJobHandlerTests.cs`) removed the orphan `using Sprk.Bff.Api.Services.Jobs.Handlers;` (the `Handlers` sub-namespace appears to be defunct):

```diff
# tests/unit/Sprk.Bff.Api.Tests/Services/Ai/EmailAnalysisIntegrationTests.cs
 using Sprk.Bff.Api.Services.Ai;
+using Sprk.Bff.Api.Services.Ai.Jobs;
 using Sprk.Bff.Api.Services.Jobs;
-using Sprk.Bff.Api.Services.Jobs.Handlers;
 using Sprk.Bff.Api.Telemetry;

# tests/unit/Sprk.Bff.Api.Tests/Services/Jobs/AppOnlyDocumentAnalysisJobHandlerTests.cs
 using Sprk.Bff.Api.Services.Ai;
+using Sprk.Bff.Api.Services.Ai.Jobs;
 using Sprk.Bff.Api.Services.Jobs;
-using Sprk.Bff.Api.Services.Jobs.Handlers;
 using Sprk.Bff.Api.Telemetry;

# tests/unit/Sprk.Bff.Api.Tests/Services/Jobs/EmbeddingMigrationServiceTests.cs
 using Sprk.Bff.Api.Services.Ai;
+using Sprk.Bff.Api.Services.Ai.Jobs;
 using Sprk.Bff.Api.Services.Jobs;
```

git diff confirms:
- Production code modified: **0 files** (`git diff --stat src/server/api/` → empty)
- Test files modified: 3 files (3 insertions, 2 deletions)

## Deletions made

None. All 7 errors were FIXABLE-IN-TEST; no obsolete tests required removal.

---

## Escalation: 69 newly-surfaced pre-existing errors

After resolving the original 7 errors, `dotnet build tests/unit/Sprk.Bff.Api.Tests/` now reports **69 additional errors** across ~17 other test files. These are pre-existing (verified via stash + rebuild on master) but were previously **masked** because the C# compiler halts at sufficient errors — fixing the first 7 surfaced the next batch.

### Pattern of failures

Almost all 69 errors are constructor-signature drift (CS7036: missing `logger` parameter) caused by production refactors that added an `ILogger<T>` constructor parameter to multiple services without updating the tests. Examples (by affected production class):

- `AnalysisActionService`, `AnalysisSkillService`, `AnalysisKnowledgeService`, `AnalysisToolService` (now require `ILogger<T>`)
- `ScopeResolverService` (now requires `ILogger<ScopeResolverService>`)
- `SessionRestoreService` (now requires `ILogger<SessionRestoreService>`)
- `CommunicationService`, `WorkingDocumentService`, `VisualizationService`, `RecordSyncJob`, etc.
- `SendCommunicationToolHandler` registration tests
- `EmailWebhookEndpointTests` (CS0618: obsolete API usage)
- `ExternalAccessEndpointTests` (CS1739, CS1061, CS1503, CS8625: type/property drift)
- `EmailAttachmentExtractionTests`, `TodoGenerationServiceTests` (CS1503: argument type drift)

### Affected files (full list, ~17)

```
ArchivalFlowTests.cs                                  (1 error, CS7036)
AssociationMappingTests.cs                            (1 error, CS7036)
AttachmentValidationTests.cs                          (1 error, CS7036)
CommunicationIntegrationTests.cs                      (1 error, CS7036)
CommunicationServiceTests.cs                          (2 errors, CS7036)
DataverseRecordCreationTests.cs                       (1 error, CS7036)
EmailAttachmentExtractionTests.cs                     (2 errors, CS1503)
EmailWebhookEndpointTests.cs                          (4 errors, CS0618)
ExternalAccessEndpointTests.cs                        (~11 errors, mixed)
RecordSyncJobTests.cs                                 (2 errors, CS7036)
ScopeResolverServiceTests.cs                          (15 errors, CS7036)
SendCommunicationToolHandlerRegistrationTests.cs      (2 errors, CS7036)
SendCommunicationToolHandlerScenarioTests.cs          (1 error, CS7036)
SessionRestoreServiceTests.cs                         (1 error, CS7036)
TodoGenerationServiceTests.cs                         (~17 errors, CS1503)
VisualizationServiceTests.cs                          (1 error, CS7036)
WorkingDocumentServiceTests.cs                        (1 error, CS7036)
```

### Why this is out of scope for task 069

Task 069 was explicitly scoped to "**7 pre-existing compile errors in 3 BFF handler test files**" (`AppOnlyDocumentAnalysisJobHandler`, `EmbeddingMigrationService`, `EmailAnalysisJobHandler`) per R4 sub-agent reports (050, 053, 054). The 69 newly-surfaced errors:
- Are in **17+ other test files** never mentioned in the task POML
- Touch **9+ unrelated production handlers/services** (Communication, External Access, Visualization, ScopeResolver, etc.)
- Represent **multi-day effort** vs. the task's 4-hour estimate
- Some (e.g., `ExternalAccessEndpointTests` with CS1739 + CS1061) may indicate genuine API contract changes requiring design review, not just `using` adjustments

Per CLAUDE.md §6 ("Scope expansion beyond task boundaries"), the correct action is **escalate** and propose a follow-up task.

### Proposed follow-up task

**Working title**: "Test infra: fix ~69 pre-existing compile errors across 17 BFF test files (CS7036 logger param drift + misc type drift)"
**Estimated hours**: 12–16 (vs. 069's 4)
**Approach**:
1. Auto-fix CS7036 (logger) via mock-injection — likely 40+ errors, mechanical
2. Investigate CS1503 / CS1061 / CS1739 case-by-case for genuine API drift vs. test staleness
3. Investigate CS0618 obsolete-API usage — replace with current API
4. If any error genuinely requires production-code change, escalate per CLAUDE.md §6
5. Final goal: `dotnet test` runs end-to-end

## Verification

### Original 7 errors

| Verification | Before | After |
|---|---|---|
| The 7 original errors | 7 errors (CS0246) | 0 errors |
| Production code modified | n/a | 0 files (`git diff --stat src/server/api/` empty) |
| Test files modified | n/a | 3 files (3 insertions, 2 deletions) |

### Build status (current)

```
$ dotnet build tests/unit/Sprk.Bff.Api.Tests/
...
    2 Warning(s)
    69 Error(s)  ← all pre-existing, newly-surfaced after 069 fix
    0 of the original 7 errors remain
```

### Test run status (R4 acceptance criteria items)

Cannot run `dotnet test` end-to-end because the 69 newly-surfaced errors prevent compilation. The R4-added tests (050 attachments, 053 ModifiedOn, 054 PUT+ETag) are still **gated** on resolving the additional 69 errors.

**However**, the original 7 errors that R4 sub-agents flagged ARE fully resolved, and the test file scope of task 069 is met.

## Acceptance-criteria status

| Criterion | Status |
|---|---|
| 7 named errors in 3 named test files resolved | ✅ Met |
| No production code modified in `src/server/api/Sprk.Bff.Api/` | ✅ Met (`git diff --stat src/server/api/` empty) |
| Memo at `notes/test-infra-bff-handler-fix-2026-05-26.md` catalogs all 7 errors + fixes | ✅ Met (this file) |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/` → 0 errors | ⚠️ Partial — original 7 are gone, but 69 pre-existing errors surfaced (out of scope) |
| `dotnet test` runs to completion | ⚠️ Blocked by the newly-surfaced 69 errors (escalated above) |
| R4-added BFF tests (050, 053, 054) appear in test run output | ⚠️ Blocked by the same 69 errors |

## Lessons learned

1. **Pre-existing baseline checks should look beyond first-N errors**: When sub-agents reported "7 errors" they had probably stopped reading after 7 useful errors. Future test-infra audits should run `dotnet build … /p:ErrorOnDuplicateBuildItems=false` or otherwise exhaust the error list per file.
2. **Namespace reorganizations should include test-file sweeps**: The R3-era AI namespace move (`Services.Jobs.Handlers` → `Services.Ai.Jobs`) updated production but missed tests. Phase 6 build-hygiene cluster should include a "namespace-drift sweep" rule.
3. **Constructor-signature changes should include test-fixture updates**: Adding `ILogger<T>` to a service constructor (now standard for new code) leaves any unupdated test in CS7036 limbo. Consider adding a test-helper builder that auto-supplies `Mock<ILogger<T>>` for common service shapes — would prevent ~40 of the 69 follow-up errors.

---

## Files modified

- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/EmailAnalysisIntegrationTests.cs` (using directives)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Jobs/AppOnlyDocumentAnalysisJobHandlerTests.cs` (using directives)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Jobs/EmbeddingMigrationServiceTests.cs` (using directives)
- `projects/spaarke-ai-platform-unification-r4/notes/test-infra-bff-handler-fix-2026-05-26.md` (this memo, new)

No production code modified.
No `.claude/` files modified.
No `TASK-INDEX.md` modified (per parent agent instructions; status reported back instead).
