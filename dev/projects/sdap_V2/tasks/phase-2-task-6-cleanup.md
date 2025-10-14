# Phase 2 - Task 6: Cleanup - Delete Obsolete Files

**Phase**: 2 (Simplify Service Layer)
**Duration**: 30 minutes
**Risk**: Low (all validation must pass first)
**Patterns**: Clean architecture through file deletion
**Anti-Patterns**: N/A

---

## Current State (Before Starting)

**Current Cleanup Need**:
- 8+ obsolete files still in repository (interfaces + implementations)
- These files replaced by SpeFileStore and authorization simplification
- Dead code confuses developers: "Which service do I use?"
- Git history preserved: Can recover if needed

**Files Impact**:
- Clutter: Services folder has old and new files mixed
- Confusion: New developers may use old interfaces
- Maintenance: Old files may be "fixed" by mistake
- Search pollution: Searching finds both old and new

**Quick Verification**:
```bash
# Check if old files still exist
ls src/api/Spe.Bff.Api/Services/IResourceStore.cs 2>/dev/null && echo "❌ Old files exist - cleanup needed" || echo "✅ Already cleaned"

# Count obsolete files
find src/api/Spe.Bff.Api/Services/ -name "IResourceStore.cs" -o -name "SpeResourceStore.cs" -o -name "ISpeService.cs" -o -name "OboSpeService.cs" | wc -l
```

**CRITICAL**: This task is **DESTRUCTIVE**. Only proceed after ALL validation passes!

---

## Background: Why Cleanup is Critical

**Historical Context**:
- Refactoring creates new files, leaving old ones "just in case"
- Fear: "What if we need to rollback?"
- Result: Repository accumulates dead code
- Over time: Confusion about which code is active

**Why Leaving Old Files is Harmful**:
1. **Confusion**: Which service is correct? IResourceStore or SpeFileStore?
2. **Wrong fixes**: Developer "fixes bug" in obsolete SpeResourceStore
3. **Merge conflicts**: Old files cause conflicts in branches
4. **Search pollution**: IDE search finds both old and new matches
5. **False coverage**: Tests for old code give false confidence

**Why Safe to Delete**:
- **Git history**: Every line preserved in commits (can recover)
- **All references removed**: Build succeeds, tests pass
- **Replacement exists**: SpeFileStore provides same functionality
- **Validation passed**: Application works without old files

**Best Practice**:
- Refactor → Replace → Validate → **Delete** (complete the cycle!)
- Don't leave dead code "just in case"
- Trust git history (that's what version control is for)
- Clean repository = clear intent

**Real Example**:
```bash
# ❌ BAD: Leave old files (causes confusion)
Services/
├── IResourceStore.cs        # Old (but still there!)
├── SpeResourceStore.cs      # Old (but still there!)
├── ISpeService.cs           # Old (but still there!)
├── OboSpeService.cs         # Old (but still there!)
└── ../Storage/
    └── SpeFileStore.cs      # New (which one do I use??)

# ✅ GOOD: Delete old files (clear intent)
Services/
└── (clean - old files deleted)

Storage/
└── SpeFileStore.cs          # Clear! This is the one to use!
```

**Safety Net**:
- All changes committed (can revert if needed)
- Tests pass (proves old code not needed)
- Build succeeds (no broken references)
- Git history intact (can git show any deleted file)

---

## 🤖 AI PROMPT

```
CONTEXT: You are working on Phase 2 of the SDAP BFF API refactoring, specifically cleaning up obsolete service files after consolidation is complete.

TASK: Delete 8 obsolete service files (interfaces and implementations) that have been replaced by SpeFileStore.

CONSTRAINTS:
- Must ONLY delete files after ALL Phase 2 validation passes
- Must verify no references remain before deletion
- Must delete corresponding test files
- Must preserve git history (use git rm, not manual deletion)

VERIFICATION BEFORE STARTING:
1. Verify ALL Phase 2 tasks complete (1-5)
2. Verify all tests pass (dotnet test)
3. Verify application starts without errors
4. Verify no references to old interfaces remain in source code
5. If ANY verification fails, STOP and fix issues first

FOCUS: Stay focused on safe file deletion only. Do NOT refactor additional code. This is cleanup only.
```

---

## Goal

Safely delete **8 obsolete service files** and their corresponding tests after verifying Phase 2 consolidation is complete.

**Files to Delete**:
- IResourceStore.cs + SpeResourceStore.cs (2 files)
- ISpeService.cs + OboSpeService.cs (2 files)
- IDataverseSecurityService.cs + DataverseSecurityService.cs (2 files)
- IUacService.cs + UacService.cs (2 files)
- Corresponding test files

**Why**: These files are obsolete after consolidation to SpeFileStore and authorization simplification.

---

## Pre-Flight Verification

### Step 0: CRITICAL - Verify Phase 2 Complete

```bash
# ============================================================================
# CRITICAL: ALL CHECKS MUST PASS BEFORE DELETING FILES
# ============================================================================

# 1. Verify all Phase 2 tasks complete
- [ ] Task 2.1: SpeFileStore created ✅
- [ ] Task 2.2: Endpoints updated ✅
- [ ] Task 2.3: DI registrations updated ✅
- [ ] Task 2.4: Tests updated ✅
- [ ] Task 2.5: Authorization simplified ✅

# 2. Verify NO references to old interfaces in source code
- [ ] Check IResourceStore references
grep -r "IResourceStore" src/api/Spe.Bff.Api/ --exclude-dir=bin --exclude-dir=obj
# Expected: No results (or only in comments marked for deletion)

- [ ] Check ISpeService references
grep -r "ISpeService" src/api/Spe.Bff.Api/ --exclude-dir=bin --exclude-dir=obj
# Expected: No results

- [ ] Check IOboSpeService references
grep -r "IOboSpeService" src/api/Spe.Bff.Api/ --exclude-dir=bin --exclude-dir=obj
# Expected: No results

- [ ] Check IDataverseSecurityService references
grep -r "IDataverseSecurityService" src/api/Spe.Bff.Api/ --exclude-dir=bin --exclude-dir=obj
# Expected: No results

- [ ] Check IUacService references
grep -r "IUacService" src/api/Spe.Bff.Api/ --exclude-dir=bin --exclude-dir=obj
# Expected: No results

# 3. Verify application builds
- [ ] dotnet build
# Expected: Success, 0 warnings

# 4. Verify all tests pass
- [ ] dotnet test
# Expected: All tests pass

# 5. Verify application starts
- [ ] dotnet run --project src/api/Spe.Bff.Api
# Expected: Starts without DI errors

# 6. Verify health check passes
- [ ] curl https://localhost:5001/healthz
# Expected: 200 OK
```

⚠️ **STOP HERE IF ANY CHECK FAILS** - Fix issues before proceeding

---

## Files to Delete

### Service Files (8 files)

```bash
# Interface and implementation pairs
- [ ] src/api/Spe.Bff.Api/Services/IResourceStore.cs
- [ ] src/api/Spe.Bff.Api/Services/SpeResourceStore.cs
- [ ] src/api/Spe.Bff.Api/Services/ISpeService.cs
- [ ] src/api/Spe.Bff.Api/Services/OboSpeService.cs
- [ ] src/api/Spe.Bff.Api/Services/IDataverseSecurityService.cs
- [ ] src/api/Spe.Bff.Api/Services/DataverseSecurityService.cs
- [ ] src/api/Spe.Bff.Api/Services/IUacService.cs
- [ ] src/api/Spe.Bff.Api/Services/UacService.cs
```

### Test Files (variable count)

```bash
# Find and delete corresponding test files
- [ ] tests/Spe.Bff.Api.Tests/Services/SpeResourceStoreTests.cs (if not renamed)
- [ ] tests/Spe.Bff.Api.Tests/Services/OboSpeServiceTests.cs
- [ ] tests/Spe.Bff.Api.Tests/Services/DataverseSecurityServiceTests.cs
- [ ] tests/Spe.Bff.Api.Tests/Services/UacServiceTests.cs

# Note: SpeResourceStoreTests may have been renamed to SpeFileStoreTests in Task 2.4
```

---

## Implementation

### Step 1: Final Verification Before Deletion

```bash
# Create safety backup (optional but recommended)
git stash save "backup-before-phase2-cleanup"

# Verify clean git status
git status
# Expected: No uncommitted changes (all Phase 2 tasks committed)

# Verify on feature branch (not main/master)
git branch --show-current
# Expected: refactor/phase-2-simplify-service-layer (or similar)
```

### Step 2: Delete Service Files

```bash
# Delete interfaces and implementations (use git rm for history)
git rm src/api/Spe.Bff.Api/Services/IResourceStore.cs
git rm src/api/Spe.Bff.Api/Services/SpeResourceStore.cs
git rm src/api/Spe.Bff.Api/Services/ISpeService.cs
git rm src/api/Spe.Bff.Api/Services/OboSpeService.cs

# Delete authorization wrappers (if they exist)
git rm src/api/Spe.Bff.Api/Services/IDataverseSecurityService.cs 2>/dev/null || echo "File doesn't exist, skipping"
git rm src/api/Spe.Bff.Api/Services/DataverseSecurityService.cs 2>/dev/null || echo "File doesn't exist, skipping"
git rm src/api/Spe.Bff.Api/Services/IUacService.cs 2>/dev/null || echo "File doesn't exist, skipping"
git rm src/api/Spe.Bff.Api/Services/UacService.cs 2>/dev/null || echo "File doesn't exist, skipping"
```

### Step 3: Delete Test Files

```bash
# Find test files (may vary by project structure)
find tests/ -name "*ResourceStoreTests.cs" -o -name "*SpeServiceTests.cs" -o -name "*SecurityServiceTests.cs" -o -name "*UacServiceTests.cs"

# Delete test files (adjust paths as needed)
git rm tests/Spe.Bff.Api.Tests/Services/SpeResourceStoreTests.cs 2>/dev/null || echo "Already renamed to SpeFileStoreTests"
git rm tests/Spe.Bff.Api.Tests/Services/OboSpeServiceTests.cs 2>/dev/null || echo "File doesn't exist, skipping"
git rm tests/Spe.Bff.Api.Tests/Services/DataverseSecurityServiceTests.cs 2>/dev/null || echo "File doesn't exist, skipping"
git rm tests/Spe.Bff.Api.Tests/Services/UacServiceTests.cs 2>/dev/null || echo "File doesn't exist, skipping"
```

### Step 4: Verify Deletion

```bash
# Verify files are staged for deletion
git status
# Expected: Should show "deleted: src/api/Spe.Bff.Api/Services/..." files

# Verify build still succeeds after deletion
dotnet build
# Expected: Success, 0 warnings

# Verify tests still pass
dotnet test
# Expected: All tests pass (same count or fewer)

# Verify no broken references
dotnet build --no-incremental
# Expected: Success
```

---

## Validation

### Build Check
```bash
dotnet clean
dotnet build
# Expected: Success, 0 warnings
```

### Test Check
```bash
dotnet test --no-build
# Expected: All tests pass
```

### Reference Check
```bash
# Verify no lingering references (should find nothing)
grep -r "IResourceStore\|ISpeService\|IDataverseSecurityService\|IUacService" src/api/ --exclude-dir=bin --exclude-dir=obj
# Expected: No results
```

### File Count Verification
```bash
# Count service files before (baseline)
# Should have deleted 8 service files + N test files

# Verify Services folder is cleaner
ls -la src/api/Spe.Bff.Api/Services/
# Expected: Should NOT see deleted files
```

---

## Checklist

- [ ] **CRITICAL**: All Phase 2 tasks (1-5) complete and validated ✅
- [ ] **CRITICAL**: All tests pass before deletion ✅
- [ ] **CRITICAL**: No references to old interfaces in source code ✅
- [ ] **CRITICAL**: Application builds and starts successfully ✅
- [ ] Created backup (git stash or commit) before deletion
- [ ] Verified on feature branch (not main/master)
- [ ] Deleted IResourceStore.cs
- [ ] Deleted SpeResourceStore.cs
- [ ] Deleted ISpeService.cs
- [ ] Deleted OboSpeService.cs
- [ ] Deleted IDataverseSecurityService.cs (if exists)
- [ ] Deleted DataverseSecurityService.cs (if exists)
- [ ] Deleted IUacService.cs (if exists)
- [ ] Deleted UacService.cs (if exists)
- [ ] Deleted corresponding test files
- [ ] Build succeeds after deletion: `dotnet build`
- [ ] Tests pass after deletion: `dotnet test`
- [ ] No broken references remain

---

## Expected Results

**Before**:
- 8 obsolete service files (interfaces + implementations)
- Corresponding test files
- Cluttered Services folder

**After**:
- 8 files deleted (tracked in git history)
- Test files cleaned up
- Cleaner Services folder
- Build succeeds, tests pass

**Metrics**:
- Files deleted: 8+ (services + tests)
- Lines of code removed: ~1000-1500 (estimated)
- Interfaces removed: 4 (IResourceStore, ISpeService, IDataverseSecurityService, IUacService)

---

## Rollback Plan

If issues arise after deletion:

### Option 1: Revert Commit (if committed)
```bash
# Find commit hash
git log --oneline -5

# Revert deletion commit
git revert <commit-hash>
```

### Option 2: Restore from Git (before commit)
```bash
# Unstage deletions
git reset HEAD src/api/Spe.Bff.Api/Services/

# Restore files
git checkout -- src/api/Spe.Bff.Api/Services/
```

### Option 3: Restore from Stash (if created backup)
```bash
# List stashes
git stash list

# Apply backup
git stash apply stash@{0}
```

---

## Context Verification

Before marking complete, verify:
- [ ] ✅ All 8 service files deleted
- [ ] ✅ Corresponding test files deleted
- [ ] ✅ Build succeeds after deletion
- [ ] ✅ Tests pass after deletion
- [ ] ✅ No broken references
- [ ] ✅ Files staged for commit (git rm used)

**If any item unchecked**: Investigate and fix before committing

---

## Commit Message

```bash
# Stage all deletions (should already be staged from git rm)
git status

# Commit deletions
git commit -m "refactor(cleanup): remove obsolete service abstractions after Phase 2

Deleted obsolete files (Phase 2 consolidation complete):
- IResourceStore.cs + SpeResourceStore.cs (replaced by SpeFileStore)
- ISpeService.cs + OboSpeService.cs (consolidated into SpeFileStore)
- IDataverseSecurityService.cs + DataverseSecurityService.cs (use AuthorizationService)
- IUacService.cs + UacService.cs (use AuthorizationService)
- Corresponding test files

Metrics:
- Files deleted: 8 service files + tests
- Lines removed: ~1200 (estimated)
- Interfaces removed: 4

Verification:
- All tests pass (dotnet test) ✅
- Build succeeds (dotnet build) ✅
- Application starts successfully ✅
- No broken references ✅

ADR Compliance: ADR-010 (Remove unnecessary abstractions)
Task: Phase 2, Task 6 - Cleanup"
```

---

## Next Phase

🎉 **Phase 2 Complete!** All service layer consolidation done.

➡️ [Phase 3 - Task 1: Create Feature Modules](phase-3-task-1-feature-modules.md)

**What's next**: Organize DI registrations into feature modules (SpaarkeCore, DocumentsModule, WorkersModule)

---

## Phase 2 Completion Summary

**Metrics**:
| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| Service interfaces | 10 | 3 | 70% reduction |
| Call chain depth | 6 layers | 3 layers | 50% reduction |
| Service files | 16+ | 8 | 50% reduction |
| Lines of code | ~2500 | ~1300 | 48% reduction |

**Achievements**:
- ✅ Created SpeFileStore (consolidated storage)
- ✅ Updated all endpoints to use concrete classes
- ✅ Updated DI to register concretes
- ✅ Updated tests to mock at infrastructure boundaries
- ✅ Simplified authorization (removed wrappers)
- ✅ Deleted 8 obsolete service files

**ADR Compliance**:
- ✅ ADR-007: SPE Storage Seam Minimalism
- ✅ ADR-010: DI Minimalism (Register Concretes)

---

## Related Resources

- **Phase 2 Overview**: [REFACTORING-CHECKLIST.md](../REFACTORING-CHECKLIST.md#phase-2-simplify-service-layer)
- **Architecture**: [TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md#solution-1-simplified-service-layer)
- **ADR**: [ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md) - ADR-007, ADR-010
