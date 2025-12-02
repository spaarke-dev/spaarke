# Task 4.4 - Phase Implementation Index

**Sprint:** 4
**Task:** Remove ISpeService/IOboSpeService Abstractions (Full Refactor)
**Total Estimated Effort:** 12.5 hours (1.5 days)
**AI Prompts:** [TASK-4.4-AI-PROMPTS.md](TASK-4.4-AI-PROMPTS.md) ⭐ **Copy-paste prompts for each phase**

---

## Phase Files (In Order)

### ✅ Planning & Documentation Complete

| Phase | File | Effort | Status |
|-------|------|--------|--------|
| **Phase 1** | [Add OBO Methods to Operation Classes](TASK-4.4-PHASE-1-ADD-OBO-METHODS.md) | 6 hours | 📝 Ready |
| **Phase 2** | [Update SpeFileStore Facade](TASK-4.4-PHASE-2-UPDATE-FACADE.md) | 1 hour | 📝 Ready |
| **Phase 3** | [Create TokenHelper Utility](TASK-4.4-PHASE-3-TOKEN-HELPER.md) | 30 min | 📝 Ready |
| **Phase 4** | [Update Endpoints](TASK-4.4-PHASE-4-UPDATE-ENDPOINTS.md) | 2 hours | 📝 Ready |
| **Phase 5** | [Delete Interface Files](TASK-4.4-PHASE-5-DELETE-FILES.md) | 30 min | 📝 Ready |
| **Phase 6** | [Update DI Registration](TASK-4.4-PHASE-6-UPDATE-DI.md) | 1 hour | 📝 Ready |
| **Phase 7** | [Build & Test](TASK-4.4-PHASE-7-BUILD-TEST.md) | 1.5 hours | 📝 Ready |

---

## Implementation Sequence

Follow these phases in order:

### Phase 1: Add OBO Methods to Operation Classes (6 hours)
**Sub-phases:**
- 1.1 ContainerOperations - Add `ListContainersAsUserAsync` (1 hour)
- 1.2 DriveItemOperations - Add 4 methods (2 hours)
- 1.3 UploadSessionManager - Add 3 methods (2 hours)
- 1.4 Create UserOperations class (1 hour)

**Key Files Modified:**
- `src/api/Spe.Bff.Api/Infrastructure/Graph/ContainerOperations.cs`
- `src/api/Spe.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs`
- `src/api/Spe.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs`
- `src/api/Spe.Bff.Api/Infrastructure/Graph/UserOperations.cs` (NEW)

---

### Phase 2: Update SpeFileStore Facade (1 hour)

**Key Changes:**
- Add `UserOperations` to constructor
- Add 11 delegation methods

**Key Files Modified:**
- `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`

---

### Phase 3: Create TokenHelper Utility (30 minutes)

**Key Changes:**
- Create static utility class
- Centralize bearer token extraction

**Key Files Created:**
- `src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs` (NEW)

---

### Phase 4: Update Endpoints (2 hours)

**Key Changes:**
- Replace `IOboSpeService` with `SpeFileStore` (9 endpoints)
- Use `TokenHelper.ExtractBearerToken()` for all token extraction

**Key Files Modified:**
- `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs` (7 endpoints)
- `src/api/Spe.Bff.Api/Api/UserEndpoints.cs` (2 endpoints)

---

### Phase 5: Delete Interface Files (30 minutes)

**Key Changes:**
- Delete 4 interface/implementation files
- Verify no references remain

**Files Deleted:**
- `src/api/Spe.Bff.Api/Infrastructure/Graph/ISpeService.cs`
- `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeService.cs`
- `src/api/Spe.Bff.Api/Services/IOboSpeService.cs`
- `src/api/Spe.Bff.Api/Services/OboSpeService.cs`

---

### Phase 6: Update DI Registration (1 hour)

**Key Changes:**
- Remove interface-based registrations
- Add `UserOperations` to DI container

**Key Files Modified:**
- `src/api/Spe.Bff.Api/Infrastructure/DI/DocumentsModule.cs`

---

### Phase 7: Build & Test (1.5 hours)

**Key Activities:**
- Clean build verification (0 errors)
- Unit tests (all pass)
- Integration tests (all pass)
- Runtime verification (OBO endpoints work)

**Verification Commands:**
```bash
dotnet clean
dotnet build
dotnet test tests/unit/Spe.Bff.Api.Tests/
dotnet test tests/integration/Spe.Integration.Tests/
```

---

## Supporting Documentation

- **[Master Implementation Guide](TASK-4.4-FULL-REFACTOR-IMPLEMENTATION.md)** - Consolidated overview with all code
- **[OBO Explanation](TASK-4.4-OBO-EXPLANATION.md)** - Why OBO is required
- **[SPE Authentication Flow](SPE-USER-AUTHENTICATION-FLOW.md)** - Complete auth flow
- **[Decision Analysis](TASK-4.4-DECISION-ANALYSIS.md)** - Full refactor vs minimal change

---

## Quick Start

**To begin implementation:**

1. Open [Phase 1](TASK-4.4-PHASE-1-ADD-OBO-METHODS.md)
2. Follow sub-phases 1.1 → 1.2 → 1.3 → 1.4
3. Build and verify after each sub-phase
4. Proceed to Phase 2 when Phase 1 is complete

**To verify completion:**

After completing all phases, check:
- [ ] Build succeeds with 0 errors
- [ ] All unit tests pass
- [ ] No grep matches for `IOboSpeService` or `ISpeService` in `src/`
- [ ] OBO endpoints return 401 without token, 200 with valid token
- [ ] ADR-007 compliance confirmed (no storage interfaces)

---

## Success Criteria

1. **ADR-007 Compliance:** ✅ No interface abstractions for storage seam
2. **Single Facade:** ✅ All Graph operations via `SpeFileStore`
3. **Modular Design:** ✅ OBO methods in operation classes
4. **Code Reuse:** ✅ App-only and OBO methods in same classes
5. **Maintainability:** ✅ Clear separation of concerns

---

**Status:** All phases documented and ready to implement
**Next Step:** Begin [Phase 1.1 - ContainerOperations](TASK-4.4-PHASE-1-ADD-OBO-METHODS.md#phase-11-containeroperations)
