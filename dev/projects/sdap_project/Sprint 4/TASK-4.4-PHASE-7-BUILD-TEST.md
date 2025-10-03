# Task 4.4 - Phase 7: Build & Test

**Sprint:** 4
**Phase:** 7 of 7 (Final)
**Estimated Effort:** 1.5 hours
**Dependencies:** Phases 1-6 complete
**Status:** Not Started

---

## Objective

Build the complete solution, run all tests, and verify the refactor is successful with no regressions.

---

## Verification Steps

### Step 1: Clean Build (5 minutes)

Clean the solution to remove old artifacts:
```bash
dotnet clean
```

Build the entire solution:
```bash
dotnet build
```

**Expected Result:**
- Build succeeds with 0 errors
- 0 warnings related to missing types or unused code

**Common Issues:**
- Missing using statements for `UserOperations`
- Unused using statements for deleted interfaces
- Namespace conflicts

---

### Step 2: Static Code Verification (10 minutes)

#### 2.1 Verify No Interface References Remain
```bash
# Should return 0 matches in src/ (tests may have mocks)
grep -r "ISpeService" src/ --exclude-dir=bin --exclude-dir=obj
grep -r "IOboSpeService" src/ --exclude-dir=bin --exclude-dir=obj
grep -r "SpeService" src/ --exclude-dir=bin --exclude-dir=obj | grep -v "OboSpeService" | grep -v "IDataverseService" | grep -v "SpeFileStore"
grep -r "OboSpeService" src/ --exclude-dir=bin --exclude-dir=obj
```

**Expected Result:** No matches in `src/` directory

#### 2.2 Verify UserOperations Usage
```bash
# Should find references in SpeFileStore, DocumentsModule, and UserEndpoints
grep -r "UserOperations" src/ --exclude-dir=bin --exclude-dir=obj
```

**Expected Matches:**
- `src/api/Spe.Bff.Api/Infrastructure/Graph/UserOperations.cs` (definition)
- `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs` (usage)
- `src/api/Spe.Bff.Api/Infrastructure/DI/DocumentsModule.cs` (DI registration)

#### 2.3 Verify TokenHelper Usage
```bash
# Should find references in OBOEndpoints and UserEndpoints
grep -r "TokenHelper" src/ --exclude-dir=bin --exclude-dir=obj
```

**Expected Matches:**
- `src/api/Spe.Bff.Api/Infrastructure/Graph/TokenHelper.cs` (definition)
- `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs` (9 usages)
- `src/api/Spe.Bff.Api/Api/UserEndpoints.cs` (2 usages)

---

### Step 3: Unit Tests (30 minutes)

#### 3.1 Run All Unit Tests
```bash
dotnet test tests/unit/Spe.Bff.Api.Tests/Spe.Bff.Api.Tests.csproj --logger "console;verbosity=detailed"
```

**Expected Result:** All tests pass (green)

#### 3.2 Tests Requiring Updates

**File:** `tests/unit/Spe.Bff.Api.Tests/Mocks/MockOboSpeService.cs`

**Action Required:**
- If tests use `MockOboSpeService`, update them to mock `SpeFileStore` instead
- Or delete `MockOboSpeService.cs` if unused

**Example Test Update:**
```csharp
// BEFORE (using IOboSpeService)
var mockOboSvc = new Mock<IOboSpeService>();
mockOboSvc.Setup(x => x.ListChildrenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<DriveItem>());

// AFTER (using SpeFileStore)
var mockFileStore = new Mock<SpeFileStore>(/* constructor args */);
mockFileStore.Setup(x => x.ListChildrenAsUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ListingParameters>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new ListingResponse { Items = new List<DriveItem>() });
```

#### 3.3 Common Test Failures

**Failure 1: DI Resolution Errors**
- **Cause:** Missing `UserOperations` registration
- **Fix:** Verify `DocumentsModule.AddDocumentsModule()` includes `UserOperations`

**Failure 2: Null Reference in Endpoints**
- **Cause:** Endpoint trying to use deleted `IOboSpeService`
- **Fix:** Verify Phase 4 endpoint updates were applied correctly

**Failure 3: Mock Setup Errors**
- **Cause:** Tests still mocking deleted interfaces
- **Fix:** Update test mocks to use `SpeFileStore`

---

### Step 4: Integration Tests (20 minutes)

#### 4.1 Run Integration Tests
```bash
dotnet test tests/integration/Spe.Integration.Tests/Spe.Integration.Tests.csproj --logger "console;verbosity=detailed"
```

**Expected Result:** All tests pass

#### 4.2 Manual API Testing (Optional)

**Start the API:**
```bash
dotnet run --project src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
```

**Test OBO Endpoints:**
```bash
# List containers (requires valid bearer token)
curl -X GET "https://localhost:7001/api/obo/containers" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE"

# Get user info
curl -X GET "https://localhost:7001/api/user/info" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE"
```

**Expected Result:**
- 200 OK responses with valid token
- 401 Unauthorized with missing/invalid token

---

### Step 5: Code Quality Checks (15 minutes)

#### 5.1 Check for Unused Using Statements
```bash
# Run .NET analyzer to find unused usings
dotnet build /p:TreatWarningsAsErrors=false /p:WarningLevel=4
```

**Action:** Remove any `using Spe.Bff.Api.Services;` statements that reference deleted `IOboSpeService`

#### 5.2 Verify ADR-007 Compliance

**Checklist:**
- [ ] No `ISpeService` interface exists
- [ ] No `IOboSpeService` interface exists
- [ ] `SpeFileStore` is the only public facade
- [ ] All operation classes are internal or scoped to Graph namespace
- [ ] DI registrations use concrete classes only (no interfaces for storage seam)

#### 5.3 Documentation Review

**Files to Review:**
- `dev/projects/sdap_project/Sprint 4/README.md` - Task 4.4 status updated to "Complete"
- `dev/projects/sdap_project/Sprint 4/TASK-4.4-IMPLEMENTATION-COMPLETE.md` - Create completion summary

---

### Step 6: Performance & Resilience Verification (10 minutes)

#### 6.1 Verify Rate Limiting Still Works
```bash
# Start API
dotnet run --project src/api/Spe.Bff.Api/Spe.Bff.Api.csproj

# In another terminal, test rate limiting
for i in {1..150}; do
  curl -X GET "https://localhost:7001/api/obo/containers" \
    -H "Authorization: Bearer YOUR_TOKEN" \
    -w "\nStatus: %{http_code}\n"
done
```

**Expected Result:**
- First 100 requests: 200 OK
- Next 10 requests: Queued (200 OK with delay)
- Remaining requests: 429 Too Many Requests with `Retry-After` header

#### 6.2 Verify OBO Token Flow
Check logs for OBO token exchange messages:
```bash
# Look for token extraction and Graph API calls
grep "OBO" logs/app.log
grep "TokenHelper" logs/app.log
```

**Expected Result:** Logs show successful token extraction and Graph API calls with user context

---

## Acceptance Criteria

### Build & Compilation
- [ ] Clean build succeeds with 0 errors
- [ ] No warnings about missing types
- [ ] No unused using statements

### Static Verification
- [ ] No references to `ISpeService` in src/
- [ ] No references to `IOboSpeService` in src/
- [ ] `UserOperations` registered in DI
- [ ] `TokenHelper` used in 11 endpoint methods

### Tests
- [ ] All unit tests pass (green)
- [ ] All integration tests pass (green)
- [ ] Mock services updated or removed

### Runtime Verification
- [ ] Application starts without DI errors
- [ ] OBO endpoints return 401 without token
- [ ] OBO endpoints return 200 with valid token
- [ ] Rate limiting still works (429 after limit)

### Code Quality
- [ ] ADR-007 compliance verified (no storage interfaces)
- [ ] No compiler warnings
- [ ] Documentation updated

---

## Completion Summary

After all acceptance criteria are met, create a completion summary:

**File:** `dev/projects/sdap_project/Sprint 4/TASK-4.4-IMPLEMENTATION-COMPLETE.md`

**Template:**
```markdown
# Task 4.4 - Implementation Complete

**Date Completed:** YYYY-MM-DD
**Total Time:** X hours (estimated 12.5 hours)
**Result:** ✅ Success

## What Was Done

### Phase 1: Add OBO Methods to Operation Classes (6 hours)
- Added `ListContainersAsUserAsync` to ContainerOperations
- Added 4 methods to DriveItemOperations
- Added 3 methods to UploadSessionManager
- Created UserOperations class with 2 methods

### Phase 2: Update SpeFileStore Facade (1 hour)
- Added UserOperations to constructor
- Added 11 delegation methods for OBO operations

### Phase 3: Create TokenHelper Utility (30 min)
- Created static TokenHelper class
- Centralized bearer token extraction logic

### Phase 4: Update Endpoints (2 hours)
- Updated 7 endpoints in OBOEndpoints.cs
- Updated 2 endpoints in UserEndpoints.cs
- Replaced IOboSpeService with SpeFileStore

### Phase 5: Delete Interface Files (30 min)
- Deleted ISpeService.cs
- Deleted SpeService.cs
- Deleted IOboSpeService.cs
- Deleted OboSpeService.cs

### Phase 6: Update DI Registration (1 hour)
- Removed interface-based registrations
- Added UserOperations to DI container

### Phase 7: Build & Test (1.5 hours)
- Clean build: ✅ 0 errors
- Unit tests: ✅ All pass
- Integration tests: ✅ All pass
- Runtime verification: ✅ OBO endpoints work

## ADR-007 Compliance

✅ No storage seam interfaces exist
✅ Single focused facade (SpeFileStore)
✅ Modular operation classes
✅ DI uses concrete classes only

## Next Steps

Task 4.4 is complete. Sprint 4 is now complete with all P0 blockers resolved:
- ✅ Task 4.1: Distributed Cache
- ✅ Task 4.2: Authentication
- ✅ Task 4.3: Rate Limiting
- ✅ Task 4.4: Interface Removal (Full Refactor)
- ✅ Task 4.5: CORS Configuration

Ready for Sprint 5 (Integration Testing & Deployment).
```

---

## Troubleshooting

### Issue: Build Errors After Refactor

**Symptoms:** Compilation errors about missing types

**Diagnosis:**
```bash
# Find all compilation errors
dotnet build 2>&1 | grep "error CS"
```

**Common Fixes:**
1. Add missing `using Spe.Bff.Api.Infrastructure.Graph;` statements
2. Remove unused `using Spe.Bff.Api.Services;` statements
3. Verify `UserOperations` is registered in DI

---

### Issue: Tests Failing

**Symptoms:** Unit tests fail with DI resolution errors

**Diagnosis:**
```bash
# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed" 2>&1 | grep -A 5 "Failed"
```

**Common Fixes:**
1. Update `CustomWebAppFactory.cs` to register `UserOperations`
2. Update test mocks to use `SpeFileStore` instead of `IOboSpeService`
3. Verify test setup includes all required DI registrations

---

### Issue: 401 Unauthorized in Production

**Symptoms:** OBO endpoints return 401 even with valid token

**Diagnosis:**
```bash
# Check token extraction logs
grep "TokenHelper" logs/app.log
grep "Unauthorized" logs/app.log
```

**Common Fixes:**
1. Verify `TokenHelper.ExtractBearerToken()` is called correctly
2. Check Authorization header format (must be `Bearer <token>`)
3. Verify token has correct audience claim

---

## Next Phase

**Sprint 4 Complete!** All P0 production blockers resolved.

**Next Sprint:** Sprint 5 - Integration Testing & Deployment Preparation
