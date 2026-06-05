# Test infra: 69 cascading BFF test compile-error fix (R4 task 070)

**Task**: 070 — `tests/unit/Sprk.Bff.Api.Tests/` 69 pre-existing compile errors across 17 files
**Predecessor**: 069 (fixed 7 namespace-drift errors that unmasked these 69)
**Date**: 2026-05-26
**Branch**: `work/spaarke-ai-platform-unification-r4`
**Rigor**: FULL (test-only fix, but 11 distinct patterns + DTO contract migration)

---

## Summary

All 69 pre-existing compile errors across 17 BFF test files resolved. **0 production code files modified**. Test suite now builds clean and runs end-to-end (5607 tests; 5217 pass, 283 fail on pre-existing test-infra issues, 107 skip — failures are out of scope per task).

R4 acceptance test suites now execute:
- ✅ Task 050 (chat attachments): all `ChatEndpointsAttachmentsTests` passing
- ✅ Task 053 (ModifiedOn wire shape): `GetLayouts_Response_IncludesModifiedOnAsCamelCaseIso8601` runs
- ✅ Task 054/055 (PUT+ETag): `UpdateLayout_*` tests run

---

## 69-error catalog

| Diagnostic | Count | Pattern |
|---|---|---|
| CS7036 | 31 | Constructor missing required parameter (mostly `ILogger<T>` + `TokenCredential`) |
| CS1503 | 24 | Argument type mismatch (positional / parameter drift) |
| CS1061 | 6 | Property no longer exists on type (DTO contract change) |
| CS0618 | 4 | Obsolete API usage (deprecated property) |
| CS1739 | 3 | Constructor parameter no longer exists (DTO contract change) |
| CS8625 | 1 | Null assignment to non-nullable reference type |

### Per-file breakdown

| File | Errors | Pattern |
|---|---|---|
| `Services/Workspace/TodoGenerationServiceTests.cs` | 21 | CS1503 — `QueryEventsAsync` added `ownerUserId` (Guid?) param at position 10 |
| `Services/Ai/ScopeResolverServiceTests.cs` | 15 | CS7036 — `AnalysisActionService`/`Skill`/`Knowledge`/`Tool` + `ScopeResolverService` added `TokenCredential` param before `ILogger<T>` |
| `Api/ExternalAccess/ExternalAccessEndpointTests.cs` | 12 | Mixed CS1739/CS1061/CS7036/CS1503/CS8625 — `InviteExternalUserRequest`/`Response` DTOs restructured (B2B flow) |
| `Api/EmailWebhookEndpointTests.cs` | 4 | CS0618 — `EmailProcessingOptions.WebhookSecret` deprecated by task 044 (use `WebhookSigningKey`) |
| `Services/Communication/CommunicationServiceTests.cs` | 2 | CS7036 — `CommunicationService` added `IDocumentDataverseService` at position 5 |
| `Services/Jobs/RecordSyncJobTests.cs` | 2 | CS7036 — `RecordSyncJob` added `IConfiguration` + `TokenCredential` params |
| `Services/Email/EmailAttachmentExtractionTests.cs` | 2 | CS1503 — `EmailToEmlConverter` param 4/5 order swapped (logger ↔ credential) |
| `Services/Ai/Tools/SendCommunicationToolHandlerRegistrationTests.cs` | 2 | CS7036 — `CommunicationService` IDocumentDataverseService param missing |
| `Services/Communication/ArchivalFlowTests.cs` | 1 | CS7036 — `CommunicationService` IDocumentDataverseService param missing |
| `Services/Communication/AssociationMappingTests.cs` | 1 | CS7036 — same |
| `Services/Communication/AttachmentValidationTests.cs` | 1 | CS7036 — same |
| `Services/Communication/DataverseRecordCreationTests.cs` | 1 | CS7036 — same |
| `Integration/CommunicationIntegrationTests.cs` | 1 | CS7036 — same |
| `Services/Ai/Tools/SendCommunicationToolHandlerScenarioTests.cs` | 1 | CS7036 — same |
| `Services/Ai/Sessions/SessionRestoreServiceTests.cs` | 1 | CS7036 — `SessionRestoreService` added `TokenCredential` param |
| `Services/Ai/WorkingDocumentServiceTests.cs` | 1 | CS7036 — `WorkingDocumentService` added `IServiceProvider` param |
| `Services/Ai/Visualization/VisualizationServiceTests.cs` | 1 | CS7036 — `VisualizationService` added `ITextExtractor` + `IOpenAiClient` params |
| **Total** | **69** | |

---

## Fix patterns applied

### Pattern A — `CommunicationService` insertion of `IDocumentDataverseService` (9 sites, 10 errors)

Production added `IDocumentDataverseService` at constructor position 5 between `IGenericEntityService` (4) and `EmlGenerationService` (6). Tests passed 10 args; production now wants 11.

**Fix**: insert `Mock.Of<IDataverseService>()` (or existing `_dataverseServiceMock.Object`) at position 5. `IDataverseService` inherits `IDocumentDataverseService`, so the same mock satisfies all three positional interfaces (positions 3, 4, 5).

```csharp
// Before
return new CommunicationService(
    _graphClientFactoryMock.Object,
    senderValidator,
    _dataverseServiceMock.Object,
    _dataverseServiceMock.Object,
    emlGenerationService,
    speFileStore,
    null!, null!,
    Options.Create(opts),
    _loggerMock.Object);

// After
return new CommunicationService(
    _graphClientFactoryMock.Object,
    senderValidator,
    _dataverseServiceMock.Object,
    _dataverseServiceMock.Object,
    _dataverseServiceMock.Object, // IDocumentDataverseService (IDataverseService satisfies it)
    emlGenerationService,
    speFileStore,
    null!, null!,
    Options.Create(opts),
    _loggerMock.Object);
```

### Pattern B — `ScopeResolverServiceTests` insertion of `TokenCredential` (15 errors)

`AnalysisActionService`, `AnalysisSkillService`, `AnalysisKnowledgeService`, `AnalysisToolService`, `ScopeResolverService` all gained a `TokenCredential` parameter before the trailing `ILogger<T>`. Test file has 3 ctor-setup blocks (lines 51, 593, 1192).

**Fix**: `Mock.Of<TokenCredential>()` injected before each `Mock.Of<ILogger<T>>()`.

### Pattern C — `SessionRestoreServiceTests` add `TokenCredential` (1 error)

Production signature: `(ISessionPersistenceService, IHttpClientFactory, IConfiguration, TokenCredential, ILogger<>)`.

**Fix**: `Mock.Of<Azure.Core.TokenCredential>()` inserted before `loggerMock.Object`.

### Pattern D — `WorkingDocumentServiceTests` add `IServiceProvider` (1 error)

Production signature: `(IGenericEntityService, IServiceProvider, IOptions<AnalysisOptions>, ILogger<>)`.

**Fix**: `Mock.Of<IServiceProvider>()` inserted at position 2.

### Pattern E — `VisualizationServiceTests` add `ITextExtractor` + `IOpenAiClient` (1 error)

Production signature: `(IKnowledgeDeploymentService, IDocumentDataverseService, ITextExtractor, IOpenAiClient, IOptions<DataverseOptions>, ILogger<>)`. Test only passed 4 of 6 deps; compiler reported the first missing required param (`dataverseOptions`).

**Fix**: `Mock.Of<ITextExtractor>()` + `Mock.Of<IOpenAiClient>()` inserted at positions 3-4. `Mock<IDataverseService>` continues to satisfy `IDocumentDataverseService` via inheritance.

### Pattern F — `RecordSyncJobTests` add `IConfiguration` + `TokenCredential` (2 errors)

Production signature: `(IDistributedCache, IHttpClientFactory, IConfiguration, IOptions<RecordSyncOptions>, TokenCredential, ILogger<>)`. Tests had 4 of 6.

**Fix**: `new ConfigurationBuilder().Build()` + `Mock.Of<TokenCredential>()` inserted (with `using Microsoft.Extensions.Configuration` and `using Azure.Core` added). Applied to both the helper at line 53 AND the `TestableRecordSyncJob` base-class call at line 540.

### Pattern G — `EmailAttachmentExtractionTests` parameter order swap (2 errors)

Production: `(HttpClient, IOptions<EmailProcessingOptions>, IConfiguration, TokenCredential, ILogger<>)`. Test passed `(httpClient, options, config, logger, credential)` — args 4 & 5 swapped.

**Fix**: swap to `(httpClient, options, config, credential, logger)`.

### Pattern H — `TodoGenerationServiceTests` add `ownerUserId` (21 errors)

`IEventDataverseService.QueryEventsAsync` gained a `Guid? ownerUserId = null` parameter at position 10, before the `CancellationToken`. Tests passed 10 args, all instances of the pattern `(p1..p7, 0, 100, It.IsAny<CancellationToken>())`.

**Fix**: bulk replace — `0, 100, It.IsAny<CancellationToken>()))` → `0, 100, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))`. 21 occurrences updated atomically.

### Pattern I — `EmailWebhookEndpointTests` migrate to `WebhookSigningKey` (4 errors)

Per task 044, `EmailProcessingOptions.WebhookSecret` is marked `[Obsolete]`. Tests directly referenced it.

**Fix**: switch to `WebhookSigningKey` (the canonical replacement). Two tests that literally verified `WebhookSecret` existed were renamed and updated to verify `WebhookSigningKey` instead. Behaviorally equivalent at the test level.

### Pattern J — `ExternalAccessEndpointTests` DTO contract migration (12 errors)

Most invasive fix. `InviteExternalUserRequest` and `InviteExternalUserResponse` records changed substantially for the Azure AD B2B invitation flow:
- Request: `ContactId` → `Email` (+ added `AccessLevel`, `FirstName`, `LastName`)
- Response: `(InvitationId, InvitationCode, ExpiryDate)` → `(ContactId, InviteRedeemUrl, Status)`

**Fix**: rewrote the 6 affected test methods to use the current contract:
- `InviteExternalUser_EmptyContactId_ShouldFailValidation` → `InviteExternalUser_EmptyEmail_ShouldFailValidation`
- Other validation tests updated to use named `Email:`, `ProjectId:`, `AccessLevel:`, etc.
- Response DTO tests updated to verify new fields (`ContactId`, `InviteRedeemUrl`, `Status`)
- `InviteExternalUserResponse_NullExpiryDate_IsAccepted` → `InviteExternalUserResponse_EmptyRedeemUrl_IsAccepted` (since the response no longer has `ExpiryDate`)

Tests were updated, not deleted — the underlying validation behavior they exercise (`Guid.Empty` checks, string emptiness checks, DTO shape) is still relevant on the new contract.

---

## Deletions made

**None.** All 69 errors were FIXABLE-IN-TEST. No tests deleted, no handlers removed.

---

## Verification

### Build status

```
$ dotnet build tests/unit/Sprk.Bff.Api.Tests/
Build succeeded.
    17 Warning(s)  ← pre-existing (NuGet vulnerability + obsolete API in OTHER files)
    0 Error(s)     ← all 69 errors resolved
```

### Test run baseline

```
$ dotnet test tests/unit/Sprk.Bff.Api.Tests/ --no-build
...
Failed!  - Failed: 283, Passed: 5217, Skipped: 107, Total: 5607
Duration: 1 m 14 s
```

The 283 failures are **pre-existing** test-logic issues (NSubstitute setup compatibility, `IChatClient.GetStreamingResponseAsync` mocking, `WebApplicationFactory` DI failures around `AiPersistenceModule`, etc.) unrelated to the compile errors fixed in this task. They are **out of scope** per the task spec ("any individual test failures are out of scope but the suite must execute").

### R4-added tests confirmed running

| Test cluster | Status | Evidence |
|---|---|---|
| **050 attachments** (`ChatEndpointsAttachmentsTests`) | ✅ Runs, all passing | `ValidateAttachments_*`, `ComposeMessage_*`, `ChatEndpoints_Constants_MatchFr07Nfr04Spec` all pass |
| **053 ModifiedOn** (`GetLayouts_Response_IncludesModifiedOnAsCamelCaseIso8601`) | ✅ Runs (fails on DI setup — pre-existing) | Test appears in run output |
| **054/055 PUT+ETag** (`UpdateLayout_*`) | ✅ Runs (fails on DI setup — pre-existing) | 7 `UpdateLayout_*` test names appear in run output |

### Production code untouched

```
$ git diff --stat src/server/api/ src/server/shared/
(empty)
```

### Files modified

```
$ git diff --stat tests/unit/
 17 files changed, 118 insertions(+), 65 deletions(-)
```

| Test file | Insertions | Deletions |
|---|---|---|
| `Api/EmailWebhookEndpointTests.cs` | 8 | 8 |
| `Api/ExternalAccess/ExternalAccessEndpointTests.cs` | 45 | 18 |
| `Integration/CommunicationIntegrationTests.cs` | 1 | 0 |
| `Services/Ai/ScopeResolverServiceTests.cs` | 15 | 12 |
| `Services/Ai/Sessions/SessionRestoreServiceTests.cs` | 1 | 0 |
| `Services/Ai/Tools/SendCommunicationToolHandlerRegistrationTests.cs` | 2 | 0 |
| `Services/Ai/Tools/SendCommunicationToolHandlerScenarioTests.cs` | 1 | 0 |
| `Services/Ai/Visualization/VisualizationServiceTests.cs` | 3 | 1 |
| `Services/Ai/WorkingDocumentServiceTests.cs` | 5 | 1 |
| `Services/Communication/ArchivalFlowTests.cs` | 1 | 0 |
| `Services/Communication/AssociationMappingTests.cs` | 1 | 0 |
| `Services/Communication/AttachmentValidationTests.cs` | 1 | 0 |
| `Services/Communication/CommunicationServiceTests.cs` | 2 | 0 |
| `Services/Communication/DataverseRecordCreationTests.cs` | 1 | 0 |
| `Services/Email/EmailAttachmentExtractionTests.cs` | 1 | 1 |
| `Services/Jobs/RecordSyncJobTests.cs` | 11 | 3 |
| `Services/Workspace/TodoGenerationServiceTests.cs` | 21 | 21 |

---

## Acceptance criteria status

| Criterion | Status |
|---|---|
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/` → 0 errors | ✅ Met |
| `dotnet test` runs to completion | ✅ Met (5607 tests executed) |
| R4-added tests (050, 053, 054/055) appear in test output | ✅ Met (all three clusters confirmed) |
| No production code modified | ✅ Met (`git diff --stat src/server/` empty) |
| Memo at `notes/test-infra-bff-cascading-fix-2026-05-26.md` | ✅ Met (this file) |

---

## Escalations / carry-overs

**None requiring production-code change.** All 69 errors were genuinely test-side drift from production refactors that didn't update test fixtures.

The 283 pre-existing test failures are documented but out of scope. They cluster into:
- `SseStreamingIntegrationTests` — NSubstitute / `IChatClient` mocking compatibility (~80 failures)
- `WorkspaceLayoutEndpointTests` — `WebApplicationFactory` startup fails on `AiPersistenceModule.AddAiPersistenceModule` DI config (~13 failures)
- Other clusters — not investigated; out of scope per task spec

If those are to be addressed, a separate task should be filed.

---

## Lessons learned (additive to 069 memo)

1. **Compiler "missing param" errors can mis-report**: when an arg list is short by N parameters and intermediate types satisfy adjacent interfaces (e.g., `IDataverseService` satisfies multiple positions), the compiler reports a CS7036 for what appears to be the wrong parameter. The fix is to inspect the full production signature and count, not just inspect the named parameter in the error.
2. **Interface segregation makes test fixture updates trivial**: `IDataverseService : IDocumentDataverseService, ...` meant 9 `CommunicationService` test sites could be fixed by adding a single `Mock.Of<IDataverseService>()` at the new position, satisfying the new `IDocumentDataverseService` slot via interface inheritance. Production's ISP refactor (composite + segregated interfaces) paid off here.
3. **DTO contract changes need explicit "test migration" subtasks in the original work**: the `InviteExternalUserRequest`/`Response` contract change (B2B flow migration) updated production but left 12 dependent test errors. Adding a "audit dependent tests" step to the original PR template would have caught this before merge.
4. **The CS-error count is not a complexity proxy**: 21 of 69 errors were a single Find+Replace in TodoGenerationServiceTests; 12 of 69 were a single coherent DTO migration in ExternalAccessEndpointTests; the actual code-judgment work was concentrated in ~30 minutes of inspection across the 17 files.

---

## Files modified

See "Files modified" table above. 17 test files, 0 production files, 1 new memo (this file).

No `.claude/` files modified.
No `TASK-INDEX.md` modified (per parent agent instructions; reported back instead).
