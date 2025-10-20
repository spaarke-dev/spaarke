# Task 2: Update PCF Control for Custom Page

**Sprint:** Custom Page Migration v3.0.0
**Estimate:** 12 hours
**Status:** Not Started
**Depends On:** Task 1 (Optional - can proceed in parallel)

---

## Pre-Task Review Prompt

Before starting Task 2, run this review:

```
TASK REVIEW: Review the current PCF control implementation before making changes.

1. Read the following files in full:
   - src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts
   - src/controls/UniversalQuickCreate/UniversalQuickCreate/components/DocumentUploadForm.tsx
   - src/controls/UniversalQuickCreate/UniversalQuickCreate/types/index.ts

2. Verify Phase 7 implementation is intact:
   - NavMapClient exists and is imported
   - DocumentRecordService uses NavMapClient
   - No recent breaking changes to service layer

3. Check current build status:
   cd src/controls/UniversalQuickCreate
   npm run build

   Expected: Build succeeds with 0 errors

4. Review recent git commits:
   git log --oneline -10 src/controls/UniversalQuickCreate/

   Look for any uncommitted changes or conflicts

5. Verify BFF API is healthy:
   curl -i https://spe-api-dev-67e2xz.azurewebsites.net/healthz

   Expected: HTTP 200 OK

6. Output your findings:
   - "Ready to proceed - no blockers" OR
   - "Issues found: [list issues]"

If issues found, resolve before proceeding.
```

---

## Task Context

**What:** Update PCF control to support Custom Page dialog mode while maintaining backward compatibility with Quick Create form

**Why:** PCF control needs to detect context and handle dialog lifecycle independently

**Critical Constraints:**
- ⚠️ MUST maintain backward compatibility (Quick Create form still works)
- ⚠️ NO changes to service layer (NavMapClient, DocumentRecordService, etc.)
- ⚠️ NO changes to BFF API
- ⚠️ Phase 7 functionality must remain intact
- ⚠️ All existing entities must continue to work

---

## Knowledge Required

**Files to Understand:**

Current PCF Control Structure:
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/
├── index.ts                          ← MODIFY (main entry point)
├── components/
│   ├── DocumentUploadForm.tsx        ← REVIEW (may need minor updates)
│   ├── FileSelectionField.tsx        ← NO CHANGE
│   └── UploadProgressBar.tsx         ← NO CHANGE
├── services/
│   ├── NavMapClient.ts               ← NO CHANGE (Phase 7)
│   ├── DocumentRecordService.ts      ← NO CHANGE (Phase 7)
│   ├── FileUploadService.ts          ← NO CHANGE
│   ├── MultiFileUploadService.ts     ← NO CHANGE
│   ├── SdapApiClient.ts              ← NO CHANGE
│   └── MsalAuthProvider.ts           ← NO CHANGE
├── config/
│   └── EntityDocumentConfig.ts       ← NO CHANGE (Phase 7)
└── types/
    └── index.ts                      ← MAY ADD (dialog-related types)
```

**Architecture Documents:**
- [SDAP-UI-CUSTOM-PAGE-ARCHITECTURE.md](SDAP-UI-CUSTOM-PAGE-ARCHITECTURE.md) (lines 364-466)
- docs/PHASE-7-DEPLOYMENT-STATUS.md (Phase 7 implementation details)

**PCF Framework Documentation:**
- [PCF Control Lifecycle](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/control-lifecycle)
- [Navigation API](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation)

---

## Implementation Steps

### Step 2.1: Add Context Detection Logic

**Prompt:**
```
Add logic to detect whether PCF control is running in Custom Page or Quick Create form context.

Context:
- PCF control needs to behave differently based on where it's hosted
- Custom Page: Autonomous workflow, can close dialog programmatically
- Quick Create: Form-dependent workflow, relies on form save

File to Modify: src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts

Knowledge:
- Custom Page context: context.page exists and context.page.type === 'custom'
- Quick Create context: context.page doesn't exist or context.page.type === 'quickCreateForm'
```

**Add after existing private fields (around line 40):**

```typescript
/**
 * Tracks whether control is running in Custom Page or Quick Create form
 * @since 3.0.0
 */
private isCustomPageMode: boolean = false;
```

**Add new method (around line 100, after constructor):**

```typescript
/**
 * Detect hosting context (Custom Page vs Quick Create Form)
 *
 * Custom Page indicators:
 * - context.page exists
 * - context.page.type === 'custom'
 * - context.navigation.close exists
 *
 * @param context - PCF context
 * @returns true if running in Custom Page, false if Quick Create form
 * @since 3.0.0
 */
private detectHostingContext(context: ComponentFramework.Context<IInputs>): boolean {
    // Check for Custom Page API
    if (context.page && context.page.type === 'custom') {
        logInfo('UniversalQuickCreate', 'Detected Custom Page context', {
            pageType: context.page.type,
            hasNavigationClose: !!(context.navigation && context.navigation.close)
        });
        return true;
    }

    // Default to Quick Create form mode
    logInfo('UniversalQuickCreate', 'Detected Quick Create form context', {
        hasPage: !!context.page,
        pageType: context.page?.type
    });
    return false;
}
```

**Update init() method to call detectHostingContext():**

Find the init() method (around line 150) and add at the beginning:

```typescript
public init(context: ComponentFramework.Context<IInputs>, notifyOutputChanged: () => void, state: ComponentFramework.Dictionary, container: HTMLDivElement): void {
    this.context = context;
    this.notifyOutputChanged = notifyOutputChanged;
    this.container = container;

    // NEW: Detect hosting context (Custom Page vs Quick Create)
    this.isCustomPageMode = this.detectHostingContext(context);

    logInfo('UniversalQuickCreate', 'Initializing UniversalDocumentUpload PCF', {
        version: '3.0.0',
        mode: this.isCustomPageMode ? 'Custom Page' : 'Quick Create Form'
    });

    // ... rest of existing init() code
}
```

**Guardrails:**
- ⚠️ Do NOT remove any existing code
- ⚠️ Only ADD new code
- ⚠️ Use logInfo() for all logging (already imported)
- ⚠️ Test compilation after changes: `npm run build`
- ⚠️ If build fails, review TypeScript errors carefully

---

### Step 2.2: Implement Dialog Close Method

**Prompt:**
```
Add method to close Custom Page dialog programmatically after successful upload.

Context:
- In Custom Page mode, control should close dialog automatically after all documents created
- In Quick Create mode, form handles close behavior (no change needed)

File to Modify: src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts
```

**Add new method (around line 300, after detectHostingContext):**

```typescript
/**
 * Close the Custom Page dialog programmatically
 *
 * Only works in Custom Page mode. In Quick Create form mode, this is a no-op.
 *
 * @since 3.0.0
 */
private closeDialog(): void {
    if (!this.isCustomPageMode) {
        logInfo('UniversalQuickCreate', 'closeDialog() called in Quick Create mode - no-op');
        return;
    }

    if (this.context.navigation && this.context.navigation.close) {
        logInfo('UniversalQuickCreate', 'Closing Custom Page dialog');
        this.context.navigation.close();
    } else {
        logError('UniversalQuickCreate', 'Cannot close dialog - navigation.close not available', {
            hasNavigation: !!this.context.navigation,
            hasClose: !!(this.context.navigation && this.context.navigation.close)
        });
    }
}
```

**Guardrails:**
- ⚠️ This method should ONLY be called in Custom Page mode
- ⚠️ Always check this.isCustomPageMode first
- ⚠️ Log all actions for debugging
- ⚠️ Do NOT call context.navigation.close() directly elsewhere

---

### Step 2.3: Update Upload Workflow to Support Custom Page Mode

**Prompt:**
```
Update the upload workflow to work independently of form save in Custom Page mode.

Context:
- Current implementation (v2.3.0): Relies on Quick Create form save event
- New requirement (v3.0.0): Custom Page mode needs autonomous workflow
- Must maintain backward compatibility with Quick Create form

File to Modify: src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts
```

Find the main upload handler method (likely named `handleUploadAndCreate` or similar).

**Current behavior (Quick Create form):**
```typescript
// Triggered by form save event
// Creates records during save
// Form handles close/refresh
```

**New behavior (Custom Page):**
```typescript
// Triggered by button click (no form save)
// Creates records independently
// Control closes dialog on success
```

Locate the method that handles upload completion (search for where documents are created).

**Add at the end, after successful creation:**

```typescript
private async handleUploadComplete(files: SpeFileMetadata[], formData: FormData): Promise<void> {
    // ... existing code that creates Document records ...

    // Add at the end, after successful creation:
    const successCount = createResults.filter(r => r.success).length;
    const failureCount = createResults.filter(r => !r.success).length;

    logInfo('DocumentUploadForm', 'Upload workflow complete', { successCount, failureCount });

    // NEW: In Custom Page mode, close dialog if all successful
    if (this.isCustomPageMode && failureCount === 0) {
        logInfo('DocumentUploadForm', 'All documents created successfully, closing dialog');

        // Small delay to allow user to see success message
        setTimeout(() => {
            this.closeDialog();
        }, 1500);
    } else if (this.isCustomPageMode && failureCount > 0) {
        // Some failures - show summary, don't auto-close
        logInfo('DocumentUploadForm', 'Some documents failed, keeping dialog open for user review');
    } else {
        // Quick Create mode - form handles close behavior
        logInfo('DocumentUploadForm', 'Quick Create mode - form will handle close behavior');
    }
}
```

**Guardrails:**
- ⚠️ Do NOT auto-close if there are failures (let user review errors)
- ⚠️ Add 1.5 second delay before closing (user needs to see success message)
- ⚠️ Preserve Quick Create form behavior (don't close form programmatically)
- ⚠️ Log all decision points
- ⚠️ Test both modes after changes

---

### Step 2.4: Update React Components (If Needed)

**Prompt:**
```
Review and update React components to support Custom Page dialog mode.

Context:
- Most React components should work as-is
- May need to update button labels or styling for dialog mode
- Progress indicators should work the same

File to Review: src/controls/UniversalQuickCreate/UniversalQuickCreate/components/DocumentUploadForm.tsx
```

**Review Tasks:**

1. Check button labels:
   - "Upload & Create" button - OK as-is
   - "Cancel" button - verify it works in dialog

2. Check progress indicators:
   - Should show during upload
   - Should show during record creation
   - Should show success/error messages

3. Check error handling:
   - Validation errors (file size, count, type)
   - Upload errors (SPE failures)
   - Creation errors (Dataverse failures)

**Likely No Changes Needed:**
- If form is purely presentational (state managed by index.ts)
- If buttons call handlers passed from parent
- If progress is driven by props

**Possible Changes Needed:**
- If form has form-specific logic (e.g., "Save" vs "Upload")
- If error messages reference "form" explicitly
- If styling needs dialog-specific adjustments

**Action:**
1. Read DocumentUploadForm.tsx in full
2. Look for any references to:
   - "form.save"
   - "Quick Create"
   - Form-specific styling
3. If found, update to be context-agnostic
4. If not found, no changes needed

**Example Update (if needed):**

Before:
```typescript
<Button onClick={handleSave}>Save and Upload</Button>
```

After:
```typescript
<Button onClick={handleUploadAndCreate}>
    {isCustomPageMode ? 'Upload & Create' : 'Save'}
</Button>
```

**Guardrails:**
- ⚠️ Only change if necessary
- ⚠️ Maintain backward compatibility
- ⚠️ Test both modes after changes
- ⚠️ Keep component as "dumb" as possible (state in index.ts)

---

### Step 2.5: Update Version and Build

**Files to Modify:**

1. **src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml**

   Find:
   ```xml
   <control namespace="sprk_Spaarke.Controls" ... version="2.3.0.0">
   ```

   Update to:
   ```xml
   <control namespace="sprk_Spaarke.Controls" ... version="3.0.0.0">
   ```

2. **src/controls/UniversalQuickCreate/package.json**

   Find:
   ```json
   "version": "2.3.0",
   ```

   Update to:
   ```json
   "version": "3.0.0",
   ```

3. **Rebuild PCF Control:**
   ```bash
   cd src/controls/UniversalQuickCreate
   npm run build
   ```

   Expected Output:
   - No TypeScript errors
   - No compilation warnings
   - Output: [pcf] Code component built successfully
   - Bundle created in: out/controls/UniversalQuickCreate/

4. **Test Build Locally:**
   ```bash
   npm start
   ```

   - Opens test harness in browser
   - Verify control renders
   - Check console for errors
   - Close test harness (Ctrl+C)

**Guardrails:**
- ⚠️ Version must be 3.0.0.0 (4 parts) in ControlManifest
- ⚠️ Version must be 3.0.0 (3 parts) in package.json
- ⚠️ Stop if build fails - fix errors before proceeding
- ⚠️ Do NOT deploy yet (deployment in Task 4)

---

### Step 2.6: Test PCF Control Locally (Test Harness)

**Prerequisites:**
- Build successful (Step 2.5)
- Test harness configured

**Steps:**

1. Start test harness:
   ```bash
   cd src/controls/UniversalQuickCreate
   npm start
   ```

2. Simulate Custom Page Parameters:

   In test harness browser, open DevTools console and set test data:

   ```javascript
   // Simulate Custom Page context
   window.testParameters = {
       parentEntityName: 'sprk_matter',
       parentRecordId: '12345678-1234-1234-1234-123456789012',
       containerId: 'b!test-container-id-here',
       parentDisplayName: 'Test Matter #12345',
       sdapApiBaseUrl: 'https://spe-api-dev-67e2xz.azurewebsites.net'
   };

   console.log('✅ Test parameters set:', window.testParameters);
   ```

3. Test Context Detection:

   - Check console logs for "Detected Custom Page context" or "Detected Quick Create form context"
   - Should default to Quick Create in test harness (no Custom Page API)

4. Test File Selection:

   - Click "Browse" or drag files
   - Try 1 file (should work)
   - Try 10 files (should work)
   - Try 11 files (should show error)
   - Try oversized file (should show error)

5. Mock Upload Test:

   - Since test harness has no real BFF API, upload will fail
   - That's expected - we're testing UI behavior
   - Check for proper error handling

6. Test Build Output:

   - No console errors (except expected API failures)
   - UI renders correctly
   - Buttons are clickable
   - Progress indicators work

**Success Criteria:**
- ✅ Test harness starts without errors
- ✅ Control renders with all UI elements
- ✅ Context detection logs appear
- ✅ File validation works
- ✅ Error messages display correctly
- ✅ No TypeScript runtime errors

**Document Results:**
- Screenshot of test harness UI
- Screenshot of console logs
- Note any issues found

---

## Acceptance Criteria

- [ ] Context detection logic added (isCustomPageMode)
- [ ] closeDialog() method implemented
- [ ] Upload workflow updated for autonomous mode
- [ ] React components reviewed (updated if needed)
- [ ] Version updated to 3.0.0
- [ ] Build successful (0 errors, 0 warnings)
- [ ] Test harness validation passed
- [ ] Backward compatibility maintained (Quick Create still works)
- [ ] All Phase 7 functionality intact
- [ ] Code documented with comments
- [ ] Changes logged in git (commit but don't push yet)

---

## Deliverables

1. ✅ Modified `src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts`
2. ✅ Updated `ControlManifest.Input.xml` (v3.0.0)
3. ✅ Updated `package.json` (v3.0.0)
4. ✅ Build output in `out/controls/UniversalQuickCreate/`
5. ✅ Test harness screenshots
6. ✅ Git commit with detailed message

---

## Rollback Plan

If PCF control updates break existing functionality:
1. Revert git commit
2. Rebuild from v2.3.0
3. Document issues
4. Review and fix before re-attempting

---

**Created:** 2025-10-20
**Sprint:** Custom Page Migration v3.0.0
**Version:** 1.0.0
