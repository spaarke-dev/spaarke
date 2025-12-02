# SpeFileViewer: Office Editor Mode - Task Index

**Project**: SPE File Viewer Enhancement v1.0.4
**Date**: 2025-11-26
**Total Estimated Time**: 6-8 hours

---

## Overview

This document serves as an **index** to individual task files. Each task has been broken out into a separate file with:
- **Clear implementation prompts** - Step-by-step code changes
- **Validation checklists** - Pre-commit and post-implementation verification
- **Context & knowledge required** - What you need to know before starting
- **Acceptance criteria** - How to know you're done
- **Common issues & solutions** - Troubleshooting guide

---

## Phase 1: Core Functionality (6 Tasks)

### ✅ Task 1.1: Add TypeScript Interfaces
**File**: [tasks/task-1.1-typescript-interfaces.md](./tasks/task-1.1-typescript-interfaces.md)
**Time**: 15 minutes | **Priority**: High (Blocking)

**Summary**: Add `OfficeUrlResponse` interface and update `FilePreviewState` to support editor mode.

**What You'll Do**:
- Add `OfficeUrlResponse` interface with office URL and permissions
- Update `FilePreviewState` with `officeUrl`, `mode`, `showReadOnlyDialog` properties

**Files Modified**: `types.ts`

**Blocks**: Task 1.2, Task 1.3

---

### ✅ Task 1.2: Add BffClient.getOfficeUrl() Method
**File**: [tasks/task-1.2-bffclient-method.md](./tasks/task-1.2-bffclient-method.md)
**Time**: 30 minutes | **Priority**: High (Blocking)

**Summary**: Implement HTTP method to call `/api/documents/{id}/office` endpoint and return Office URL with permissions.

**What You'll Do**:
- Add `getOfficeUrl()` async method to BffClient class
- Implement fetch call with bearer token and correlation ID
- Reuse existing `handleErrorResponse()` for error mapping
- Add console logging for debugging

**Files Modified**: `BffClient.ts`

**Depends On**: Task 1.1
**Blocks**: Task 1.3

---

### ✅ Task 1.3: Add FilePreview State & Methods
**File**: [tasks/task-1.3-filepreview-state.md](./tasks/task-1.3-filepreview-state.md)
**Time**: 45 minutes | **Priority**: High (Blocking)

**Summary**: Update FilePreview React component with editor mode state and toggle methods.

**What You'll Do**:
- Update state initialization with new properties
- Add `isOfficeFile()` utility method (file type detection)
- Add `handleOpenEditor()` method (calls BFF, switches mode)
- Add `handleBackToPreview()` method (returns to preview)
- Add `dismissReadOnlyDialog()` method (closes permission dialog)

**Files Modified**: `FilePreview.tsx`

**Depends On**: Task 1.1, Task 1.2
**Blocks**: Task 1.4

---

### ✅ Task 1.4: Update FilePreview Render Method
**File**: [tasks/task-1.4-filepreview-render.md](./tasks/task-1.4-filepreview-render.md)
**Time**: 45 minutes | **Priority**: High (Blocking)

**Summary**: Add UI buttons, dynamic iframe src, and read-only permission dialog to render method.

**What You'll Do**:
- Add Fluent UI imports (Dialog, DialogFooter, PrimaryButton)
- Add "Open in Editor" button (preview mode, Office files only)
- Add "Back to Preview" button (editor mode)
- Update iframe src to toggle between previewUrl and officeUrl
- Add read-only permission Dialog component

**Files Modified**: `FilePreview.tsx`

**Depends On**: Task 1.3
**Blocks**: Task 1.5

---

### ✅ Task 1.5: Add Button Styles (CSS)
**File**: [tasks/task-1.5-button-styles.md](./tasks/task-1.5-button-styles.md)
**Time**: 20 minutes | **Priority**: High

**Summary**: Add CSS styles for action buttons with Fluent UI design patterns and floating positioning.

**What You'll Do**:
- Update `.spe-file-viewer__preview` with `position: relative`
- Add `.spe-file-viewer__open-editor-button` styles (blue primary, top-right)
- Add `.spe-file-viewer__back-to-preview-button` styles (gray secondary, top-left)
- Add hover, active, focus, disabled states
- Add responsive adjustments for small screens

**Files Modified**: `SpeFileViewer.css`

**Depends On**: Task 1.4

---

### ⚠️ Task 1.6: Update Backend API Response (OPTIONAL)
**File**: [tasks/task-1.6-backend-api-optional.md](./tasks/task-1.6-backend-api-optional.md)
**Time**: 30 minutes | **Priority**: Low (Optional)

**Summary**: Modify `/office` endpoint to return JSON with permissions (instead of redirect).

**Recommendation**: **SKIP FOR PHASE 1** - Office Online automatically enforces permissions.

**What You'll Do** (if implementing):
- Modify GetOffice endpoint to request permissions facet from Graph API
- Extract canEdit/canView from driveItem.Permissions
- Return structured JSON response
- Handle null permissions gracefully

**Files Modified**: `FileAccessEndpoints.cs`

**Note**: Can be added in Phase 2 if user feedback requires it.

---

## Phase 2: Testing & Deployment (3 Tasks)

### Task 2.1: Build and Package PCF
**Time**: 20 minutes | **Priority**: High

**Steps**:
1. Update `Solution.xml` version to `1.0.4`
2. Run `npm run build` in SpeFileViewer folder
3. Run `dotnet msbuild` to build solution package
4. Verify bundle.js and solution zip created

**Acceptance Criteria**:
- No build errors
- `out/bundle.js` updated with new code
- Solution package in `bin/Release/`

---

### Task 2.2: Deploy to Dataverse
**Time**: 15 minutes | **Priority**: High

**Steps**:
1. Import solution to Dataverse environment
2. Publish all customizations
3. Refresh test entity form
4. Verify PCF control loads without errors

**Acceptance Criteria**:
- Solution imports successfully
- No import warnings
- PCF control visible on form

---

### Task 2.3: User Acceptance Testing
**Time**: 1 hour | **Priority**: High

**Test Cases**:
1. Office file preview shows "Open in Editor" button
2. Non-Office file (PDF) does NOT show button
3. Editor mode loads Office Online with edit access
4. Read-only user sees dialog explaining view-only access
5. "Back to Preview" returns to preview mode
6. Error handling works (invalid ID, network errors)

**Acceptance Criteria**:
- All test cases pass
- No console errors
- Performance acceptable (< 3s to load editor)

---

## Task Dependency Graph

```
┌─────────────┐
│  Task 1.1   │  Add TypeScript Interfaces
│  (15 min)   │
└──────┬──────┘
       │
       ├──────────────────────────────┐
       │                              │
       ↓                              ↓
┌─────────────┐                ┌─────────────┐
│  Task 1.2   │                │  Task 1.3   │  Add FilePreview State
│  (30 min)   │                │  (45 min)   │  (also depends on 1.2)
└──────┬──────┘                └──────┬──────┘
       │                              │
       └──────────────┬───────────────┘
                      │
                      ↓
               ┌─────────────┐
               │  Task 1.4   │  Update Render Method
               │  (45 min)   │
               └──────┬──────┘
                      │
                      ↓
               ┌─────────────┐
               │  Task 1.5   │  Add Button Styles
               │  (20 min)   │
               └─────────────┘

               ┌─────────────┐
               │  Task 1.6   │  Backend API (Optional, independent)
               │  (30 min)   │
               └─────────────┘

                      ↓
               ┌─────────────┐
               │  Task 2.1   │  Build & Package
               │  (20 min)   │
               └──────┬──────┘
                      │
                      ↓
               ┌─────────────┐
               │  Task 2.2   │  Deploy to Dataverse
               │  (15 min)   │
               └──────┬──────┘
                      │
                      ↓
               ┌─────────────┐
               │  Task 2.3   │  User Acceptance Testing
               │  (60 min)   │
               └─────────────┘
```

---

## Critical Path

**Must be completed in order**:
1. Task 1.1 → Task 1.2 → Task 1.3 → Task 1.4 → Task 1.5
2. Task 2.1 → Task 2.2 → Task 2.3

**Can be skipped**:
- Task 1.6 (Backend API - optional for Phase 1)

**Total Critical Path Time**: 4 hours 15 minutes (frontend only)

---

## Quick Start Guide

### For First-Time Implementation

1. **Read the Technical Overview** first:
   - [TECHNICAL-OVERVIEW.md](./TECHNICAL-OVERVIEW.md)
   - Understand architecture and security model

2. **Start with Task 1.1**:
   - Open [tasks/task-1.1-typescript-interfaces.md](./tasks/task-1.1-typescript-interfaces.md)
   - Follow step-by-step implementation prompt
   - Run validation checklist before committing
   - Mark task complete

3. **Continue sequentially**:
   - Task 1.2 → Task 1.3 → Task 1.4 → Task 1.5
   - Each task builds on the previous one
   - Don't skip validation steps

4. **Test before deploying**:
   - Run `npm run build` after each frontend task
   - Verify TypeScript compilation succeeds
   - Review git diff for unintended changes

5. **Deploy and test**:
   - Task 2.1 (Build package)
   - Task 2.2 (Deploy to Dataverse)
   - Task 2.3 (User acceptance testing)

---

## Files Summary

### Frontend (PCF Control)
```
C:\code_files\spaarke\src\controls\SpeFileViewer\
├── SpeFileViewer\
│   ├── types.ts                 → Task 1.1
│   ├── BffClient.ts             → Task 1.2
│   ├── FilePreview.tsx          → Task 1.3, 1.4
│   └── css\
│       └── SpeFileViewer.css    → Task 1.5
└── SpeFileViewerSolution\
    └── src\Other\
        └── Solution.xml         → Task 2.1 (version bump)
```

### Backend (BFF API) - Optional
```
c:\code_files\spaarke\src\api\Spe.Bff.Api\
└── Api\
    └── FileAccessEndpoints.cs   → Task 1.6 (optional)
```

---

## Success Metrics

**Phase 1 Complete When**:
- [x] All 5 critical frontend tasks completed (1.1-1.5)
- [x] TypeScript compiles without errors
- [x] Solution package builds successfully

**Phase 2 Complete When**:
- [x] Solution deployed to Dataverse
- [x] All UAT test cases pass
- [x] No console errors in production
- [x] Performance acceptable (< 3s load time)

---

## Rollback Plan

If issues occur after deployment:

1. **Immediate** (< 5 minutes):
   ```css
   /* Hide buttons with CSS hotfix */
   .spe-file-viewer__open-editor-button { display: none !important; }
   ```

2. **Short-term** (< 30 minutes):
   - Revert to solution v1.0.3
   - Import previous solution package

3. **Long-term**:
   - Fix issues in dev environment
   - Redeploy v1.0.4

---

## Support

**Questions?**
- **Implementation**: Read individual task file ([tasks/task-*.md](./tasks/))
- **Architecture**: See [TECHNICAL-OVERVIEW.md](./TECHNICAL-OVERVIEW.md)
- **Decision Rationale**: See [ADR-EDITOR-MODE.md](./ADR-EDITOR-MODE.md)

**Stuck on a task?**
- Check "Common Issues & Solutions" section in task file
- Verify all prerequisite tasks are completed
- Review dependency graph above
