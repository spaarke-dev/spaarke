# Task 4.4 - Phase 5: Delete Interface Files

**Sprint:** 4
**Phase:** 5 of 7
**Estimated Effort:** 30 minutes
**Dependencies:** Phase 4 complete
**Status:** Blocked

---

## Objective

Delete interface and implementation files that violate ADR-007.

---

## Files to Delete

### 1. ISpeService.cs
**Path:** `src/api/Spe.Bff.Api/Infrastructure/Graph/ISpeService.cs`
**Reason:** ADR-007 forbids this interface
**Status:** Currently unused (no registrations found)

### 2. SpeService.cs
**Path:** `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeService.cs`
**Reason:** Implementation of forbidden interface
**Status:** Currently unused

### 3. IOboSpeService.cs
**Path:** `src/api/Spe.Bff.Api/Services/IOboSpeService.cs`
**Reason:** ADR-007 forbids this interface
**Status:** Used by OBOEndpoints and UserEndpoints (will be removed in Phase 4)

### 4. OboSpeService.cs
**Path:** `src/api/Spe.Bff.Api/Services/OboSpeService.cs`
**Reason:** Implementation of forbidden interface
**Status:** Used by IOboSpeService (will be removed in Phase 4)

---

## Steps

1. Verify Phase 4 is complete (no references to IOboSpeService exist)
2. Delete the 4 files listed above
3. Build project to verify no compilation errors
4. Check test project for MockOboSpeService usage

---

## Test Mock Handling

**File:** `tests/unit/Spe.Bff.Api.Tests/Mocks/MockOboSpeService.cs`

**Options:**
- **If used by tests:** Update tests to mock `SpeFileStore` instead
- **If unused:** Delete this file too

---

## Acceptance Criteria

- [ ] All 4 files deleted
- [ ] Build succeeds with 0 errors
- [ ] No grep matches for "IOboSpeService" or "ISpeService" in src/
- [ ] Test mocks updated or removed

---

## Verification Commands

```bash
# Verify no references remain
grep -r "IOboSpeService" src/
grep -r "ISpeService" src/

# Should return: no matches
```

---

## Next Phase

**Phase 6:** Update DI registration
