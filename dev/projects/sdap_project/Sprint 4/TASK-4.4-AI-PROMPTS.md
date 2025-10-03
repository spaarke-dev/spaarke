# Task 4.4 - AI-Ready Prompts for Each Phase

**Purpose:** Copy-paste prompts for AI coding assistants to implement each phase
**Context:** Each prompt references the full phase documentation for complete implementation details

---

## How to Use These Prompts

1. Start each phase by copying the prompt below
2. Paste into your AI coding assistant (Claude, GitHub Copilot, etc.)
3. The AI will read the referenced phase file and implement the changes
4. Verify the changes using the acceptance criteria in the phase file

---

## Phase 1.1 Prompt: Add OBO Methods to ContainerOperations

```
I need you to implement Phase 1.1 of Task 4.4 for the Spaarke project.

**Context:**
- We're removing ISpeService/IOboSpeService interface abstractions per ADR-007
- This phase adds OBO (On-Behalf-Of) user authentication methods to existing operation classes
- OBO methods allow operations using user credentials instead of app-only credentials

**Task:**
Read the file `dev/projects/sdap_project/Sprint 4/TASK-4.4-PHASE-1-ADD-OBO-METHODS.md` and implement Phase 1.1 only.

**Specific Requirements:**
1. Add `ListContainersAsUserAsync` method to ContainerOperations class
2. Method signature: `Task<IList<ContainerDto>> ListContainersAsUserAsync(string userToken, Guid containerTypeId, CancellationToken ct = default)`
3. Use `_graphClientFactory.CreateOnBehalfOfClientAsync(userToken)` to create Graph client
4. Include error handling, logging, and validation
5. Follow the exact code structure in the phase file

**Files to Modify:**
- `src/api/Spe.Bff.Api/Infrastructure/Graph/ContainerOperations.cs`

**Verification:**
After implementation, run:
```bash
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
```
Expected: 0 errors

Please implement Phase 1.1 now.
```

---

## Phase 1.2 Prompt: Add OBO Methods to DriveItemOperations

```
I need you to implement Phase 1.2 of Task 4.4 for the Spaarke project.

**Context:**
- Continuing Task 4.4 (removing ISpeService/IOboSpeService abstractions)
- Phase 1.1 is complete (ContainerOperations OBO method added)
- This phase adds 4 OBO methods to DriveItemOperations

**Task:**
Read the file `dev/projects/sdap_project/Sprint 4/TASK-4.4-PHASE-1-ADD-OBO-METHODS.md` and implement Phase 1.2 only.

**Specific Requirements:**
Add these 4 methods to DriveItemOperations:
1. `ListChildrenAsUserAsync` - List files in container with pagination
2. `DownloadFileWithRangeAsUserAsync` - Download file with range support (HTTP 206)
3. `UpdateItemAsUserAsync` - Rename/move files
4. `DeleteItemAsUserAsync` - Delete files

All methods:
- Use `string userToken` as first parameter
- Use `_graphClientFactory.CreateOnBehalfOfClientAsync(userToken)`
- Include comprehensive error handling
- Follow exact code structure in phase file

**Files to Modify:**
- `src/api/Spe.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs`

**Verification:**
```bash
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
```
Expected: 0 errors

Please implement Phase 1.2 now.
```

---

## Phase 1.3 Prompt: Add OBO Methods to UploadSessionManager

```
I need you to implement Phase 1.3 of Task 4.4 for the Spaarke project.

**Context:**
- Continuing Task 4.4 (removing ISpeService/IOboSpeService abstractions)
- Phases 1.1 and 1.2 are complete (ContainerOperations and DriveItemOperations OBO methods added)
- This phase adds 3 OBO methods to UploadSessionManager for file upload operations

**Task:**
Read the file `dev/projects/sdap_project/Sprint 4/TASK-4.4-PHASE-1-ADD-OBO-METHODS.md` and implement Phase 1.3 only.

**Specific Requirements:**
Add these 3 methods to UploadSessionManager:
1. `UploadSmallAsUserAsync` - Upload files < 4MB (simple PUT)
2. `CreateUploadSessionAsUserAsync` - Create chunked upload session for large files
3. `UploadChunkAsUserAsync` - Upload file chunk to session

All methods:
- Use `string userToken` as first parameter
- Use `_graphClientFactory.CreateOnBehalfOfClientAsync(userToken)`
- Include size validation for small uploads (< 4MB)
- Follow exact code structure in phase file

**Files to Modify:**
- `src/api/Spe.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs`

**Verification:**
```bash
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
```
Expected: 0 errors

Please implement Phase 1.3 now.
```

---

## Phase 1.4 Prompt: Create UserOperations Class

```
I need you to implement Phase 1.4 of Task 4.4 for the Spaarke project.

**Context:**
- Continuing Task 4.4 (removing ISpeService/IOboSpeService abstractions)
- Phases 1.1-1.3 are complete (OBO methods added to existing operation classes)
- This phase creates a NEW class for user-specific operations

**Task:**
Read the file `dev/projects/sdap_project/Sprint 4/TASK-4.4-PHASE-1-ADD-OBO-METHODS.md` and implement Phase 1.4 only.

**Specific Requirements:**
Create new file `UserOperations.cs` with 2 methods:
1. `GetUserInfoAsync` - Get current user info via /me endpoint
2. `GetUserCapabilitiesAsync` - Get user permissions for a container

Class should:
- Follow same pattern as other operation classes
- Accept `IGraphClientFactory` and `ILogger<UserOperations>` in constructor
- Use OBO authentication for all methods
- Include comprehensive error handling and logging

**Files to Create:**
- `src/api/Spe.Bff.Api/Infrastructure/Graph/UserOperations.cs` (NEW FILE)

**Verification:**
```bash
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
```
Expected: 0 errors

Please implement Phase 1.4 now.
```

---

## Phase 2 Prompt: Update SpeFileStore Facade

```
I need you to implement Phase 2 of Task 4.4 for the Spaarke project.

**Context:**
- Phase 1 is complete (all OBO methods added to operation classes)
- This phase updates SpeFileStore facade to expose OBO methods via delegation
- SpeFileStore becomes the single entry point for ALL Graph operations (app-only + OBO)

**Task:**
Read the file `dev/projects/sdap_project/Sprint 4/TASK-4.4-PHASE-2-UPDATE-FACADE.md` and implement all changes.

**Specific Requirements:**
1. Add `UserOperations _userOps` field to SpeFileStore
2. Add `UserOperations userOps` parameter to constructor
3. Add 11 delegation methods that call through to operation classes:
   - 1 container method (ListContainersAsUserAsync)
   - 4 drive item methods (List, Download, Update, Delete)
   - 3 upload methods (Small upload, Create session, Upload chunk)
   - 2 user methods (GetUserInfo, GetCapabilities)

All delegation methods should be one-liners calling through to operation classes.

**Files to Modify:**
- `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`

**Verification:**
```bash
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
```
Expected: 0 errors

Please implement Phase 2 now.
```

---

## Phase 3 Prompt: Create TokenHelper Utility

```
I need you to implement Phase 3 of Task 4.4 for the Spaarke project.

**Context:**
- Phases 1-2 are complete (OBO methods added and exposed via SpeFileStore)
- This phase creates a utility to extract bearer tokens from HttpContext
- Consolidates token extraction logic used across all OBO endpoints

**Task:**
Read the file `dev/projects/sdap_project/Sprint 4/TASK-4.4-PHASE-3-TOKEN-HELPER.md` and implement all changes.

**Specific Requirements:**
Create new static class `TokenHelper` with one method:
- `ExtractBearerToken(HttpContext httpContext)` returns `string`
- Throws `UnauthorizedAccessException` if token missing or malformed
- Validates "Bearer " prefix
- Returns clean token string

**Files to Create:**
- `src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs` (NEW FILE)

**Verification:**
```bash
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
```
Expected: 0 errors

Please implement Phase 3 now.
```

---

## Phase 4 Prompt: Update OBO and User Endpoints

```
I need you to implement Phase 4 of Task 4.4 for the Spaarke project.

**Context:**
- Phases 1-3 are complete (OBO methods exist, TokenHelper created)
- This phase replaces IOboSpeService with SpeFileStore in 9 endpoints
- Updates all endpoints to use TokenHelper for token extraction

**Task:**
Read the file `dev/projects/sdap_project/Sprint 4/TASK-4.4-PHASE-4-UPDATE-ENDPOINTS.md` and implement all changes.

**Specific Requirements:**
1. Update OBOEndpoints.cs (7 endpoints):
   - Replace `[FromServices] IOboSpeService oboSvc` with `[FromServices] SpeFileStore speFileStore`
   - Replace token extraction with `TokenHelper.ExtractBearerToken(ctx)`
   - Add try/catch for `UnauthorizedAccessException` → return 401
   - Update method calls to use `speFileStore.*AsUserAsync()`

2. Update UserEndpoints.cs (2 endpoints):
   - Same pattern as OBOEndpoints
   - Replace IOboSpeService with SpeFileStore
   - Use TokenHelper for token extraction

**Files to Modify:**
- `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs`
- `src/api/Spe.Bff.Api/Api/UserEndpoints.cs`

**Verification:**
```bash
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
```
Expected: 0 errors (may have warnings about unused IOboSpeService - that's OK, we'll delete it in Phase 5)

Please implement Phase 4 now.
```

---

## Phase 5 Prompt: Delete Interface Files

```
I need you to implement Phase 5 of Task 4.4 for the Spaarke project.

**Context:**
- Phases 1-4 are complete (all code now uses SpeFileStore instead of interfaces)
- This phase deletes the interface/implementation files that violate ADR-007
- CRITICAL: Only proceed if Phase 4 is complete and build succeeds

**Task:**
Read the file `dev/projects/sdap_project/Sprint 4/TASK-4.4-PHASE-5-DELETE-FILES.md` and implement all changes.

**Specific Requirements:**
Delete these 4 files:
1. `src/api/Spe.Bff.Api/Infrastructure/Graph/ISpeService.cs`
2. `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeService.cs`
3. `src/api/Spe.Bff.Api/Services/IOboSpeService.cs`
4. `src/api/Spe.Bff.Api/Services/OboSpeService.cs`

**Verification:**
```bash
# Verify no references remain
grep -r "IOboSpeService" src/api/Spe.Bff.Api/ --exclude-dir=bin --exclude-dir=obj
grep -r "ISpeService" src/api/Spe.Bff.Api/ --exclude-dir=bin --exclude-dir=obj
# Expected: No matches

# Build should succeed
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
# Expected: 0 errors
```

Please implement Phase 5 now.
```

---

## Phase 6 Prompt: Update DI Registration

```
I need you to implement Phase 6 of Task 4.4 for the Spaarke project.

**Context:**
- Phases 1-5 are complete (interface files deleted)
- This phase updates dependency injection to remove interface registrations
- Adds UserOperations to DI container

**Task:**
Read the file `dev/projects/sdap_project/Sprint 4/TASK-4.4-PHASE-6-UPDATE-DI.md` and implement all changes.

**Specific Requirements:**
Update `DocumentsModule.cs`:
1. Remove these registrations (if they exist):
   - `builder.Services.AddScoped<ISpeService, SpeService>();`
   - `builder.Services.AddScoped<IOboSpeService, OboSpeService>();`

2. Add this registration:
   - `builder.Services.AddScoped<UserOperations>();`

Final state should have:
- ContainerOperations, DriveItemOperations, UploadSessionManager, UserOperations registered
- SpeFileStore registered
- NO interface-based registrations for storage seam

**Files to Modify:**
- `src/api/Spe.Bff.Api/Infrastructure/DI/DocumentsModule.cs`

**Verification:**
```bash
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
# Expected: 0 errors

# Application should start without DI errors
dotnet run --project src/api/Spe.Bff.Api/Spe.Bff.Api.csproj --no-build
# Expected: No InvalidOperationException about missing services
```

Please implement Phase 6 now.
```

---

## Phase 7 Prompt: Build & Test

```
I need you to implement Phase 7 of Task 4.4 for the Spaarke project.

**Context:**
- Phases 1-6 are complete (all code changes done)
- This final phase verifies the implementation through build, tests, and static checks

**Task:**
Read the file `dev/projects/sdap_project/Sprint 4/TASK-4.4-PHASE-7-BUILD-TEST.md` and execute all verification steps.

**Specific Requirements:**
1. Clean build verification (0 errors)
2. Static code verification (grep for removed interfaces)
3. Run unit tests (all pass)
4. Run integration tests (all pass)
5. Verify runtime behavior (OBO endpoints work)
6. Check ADR-007 compliance

**Verification Steps:**
```bash
# 1. Clean build
dotnet clean
dotnet build
# Expected: 0 errors

# 2. Verify no interface references
grep -r "ISpeService" src/ --exclude-dir=bin --exclude-dir=obj
grep -r "IOboSpeService" src/ --exclude-dir=bin --exclude-dir=obj
# Expected: No matches in src/

# 3. Run tests
dotnet test tests/unit/Spe.Bff.Api.Tests/
dotnet test tests/integration/Spe.Integration.Tests/
# Expected: All pass

# 4. Runtime verification (optional - requires valid token)
# Start API and test OBO endpoint returns 401 without token
```

**After Verification:**
Create completion summary document:
- `dev/projects/sdap_project/Sprint 4/TASK-4.4-IMPLEMENTATION-COMPLETE.md`

Please execute Phase 7 verification now.
```

---

## Complete Implementation Prompt (All Phases at Once)

If you prefer to implement all phases in a single session, use this prompt:

```
I need you to implement the complete Task 4.4 refactor for the Spaarke project.

**Context:**
- We're removing ISpeService/IOboSpeService interface abstractions per ADR-007
- This is a 7-phase implementation that moves OBO code to modular operation classes
- Full details in `dev/projects/sdap_project/Sprint 4/TASK-4.4-FULL-REFACTOR-IMPLEMENTATION.md`

**Task:**
Read the master implementation guide and all phase files:
1. Read `TASK-4.4-FULL-REFACTOR-IMPLEMENTATION.md` for overview
2. Read `TASK-4.4-PHASE-INDEX.md` for implementation sequence
3. Implement each phase in order (1.1 → 1.2 → 1.3 → 1.4 → 2 → 3 → 4 → 5 → 6 → 7)
4. Build and verify after each phase
5. Create completion summary when done

**Expected Changes:**
- 3 operation classes modified (ContainerOperations, DriveItemOperations, UploadSessionManager)
- 1 new operation class created (UserOperations)
- 1 facade updated (SpeFileStore)
- 1 utility created (TokenHelper)
- 2 endpoint files updated (OBOEndpoints, UserEndpoints)
- 4 interface files deleted (ISpeService, SpeService, IOboSpeService, OboSpeService)
- 1 DI module updated (DocumentsModule)

**Final Verification:**
- Build: 0 errors
- Tests: All pass
- ADR-007 compliance: No storage seam interfaces
- Runtime: OBO endpoints work with valid token

Please implement all phases now.
```

---

## Notes on Using These Prompts

### Best Practices:

1. **Incremental Implementation:** Use individual phase prompts for better control and verification
2. **Verify Between Phases:** Run build after each phase to catch errors early
3. **Reference Full Documentation:** Each prompt tells the AI to read the full phase file
4. **Track Progress:** Use todo list or checkboxes to mark completed phases

### When to Use Complete Prompt:

- You have high confidence in AI capabilities
- You want to minimize back-and-forth
- You can review all changes before committing

### When to Use Phase Prompts:

- You want to verify each step (recommended)
- You're learning the codebase
- You want to catch errors early
- You're working with token limits

---

**Ready to Start?** Copy Phase 1.1 prompt above to begin implementation!
