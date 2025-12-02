# Task 1.3: Add FilePreview State & Methods

**Phase**: 1 - Core Functionality
**Priority**: High (Blocking)
**Estimated Time**: 45 minutes
**Depends On**: Task 1.1, Task 1.2
**Blocks**: Task 1.4 (Render Method)

---

## Objective

Update FilePreview React component to support editor mode by adding state properties and implementing mode toggle methods.

## Context & Knowledge Required

### What You Need to Know
1. **React Component State**: `this.state` and `this.setState()` patterns
2. **React Lifecycle**: `constructor()` for initial state
3. **Event Handlers**: Arrow functions for `this` binding
4. **Office File Extensions**: Common Microsoft Office file types
5. **Async/Await in React**: Handling promises in component methods

### Files to Review Before Starting
- **FilePreview.tsx (current)**: [C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\FilePreview.tsx](C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\FilePreview.tsx)
  - Current state initialization (~line 27)
  - Existing methods like `loadPreview()` and `handleRetry()` (~lines 58-124)
- **types.ts**: Updated `FilePreviewState` interface (Task 1.1)
- **BffClient.ts**: `getOfficeUrl()` method (Task 1.2)

---

## Implementation Prompt

### Step 1: Update State Initialization

**Location**: `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\FilePreview.tsx`
**Find**: `constructor(props: FilePreviewProps)` method (~line 23)

**Current Code**:
```typescript
this.state = {
    previewUrl: null,
    isLoading: true,
    error: null,
    documentInfo: null
};
```

**Replace With**:
```typescript
this.state = {
    previewUrl: null,
    officeUrl: null,
    isLoading: true,
    error: null,
    documentInfo: null,
    mode: 'preview',
    showReadOnlyDialog: false
};
```

---

### Step 2: Add Office File Type Detection Method

**Location**: After `handleRetry()` method (~line 125)

```typescript
/**
 * Check if file extension is a Microsoft Office type
 *
 * Office files can be opened in Office Online editor mode.
 * Other file types (PDF, images, etc.) only support preview.
 *
 * @param extension File extension (e.g., "docx", "xlsx", "pdf")
 * @returns true if Office file, false otherwise
 */
private isOfficeFile(extension?: string): boolean {
    if (!extension) {
        return false;
    }

    const officeExtensions = [
        // Word
        'docx', 'doc', 'docm', 'dot', 'dotx', 'dotm',
        // Excel
        'xlsx', 'xls', 'xlsm', 'xlsb', 'xlt', 'xltx', 'xltm',
        // PowerPoint
        'pptx', 'ppt', 'pptm', 'pot', 'potx', 'potm', 'pps', 'ppsx', 'ppsm'
    ];

    return officeExtensions.includes(extension.toLowerCase());
}
```

---

### Step 3: Add Open Editor Method

**Location**: After `isOfficeFile()` method

```typescript
/**
 * Open file in Office Online editor mode
 *
 * Workflow:
 * 1. Call BFF API to get Office URL
 * 2. Switch iframe to editor mode
 * 3. Show read-only dialog if user lacks edit permissions
 *
 * @throws Error if API call fails (handled by setState)
 */
private handleOpenEditor = async (): Promise<void> => {
    const { documentId, accessToken, correlationId } = this.props;

    console.log(`[FilePreview] Opening editor for document: ${documentId}`);

    // Set loading state
    this.setState({
        isLoading: true,
        error: null
    });

    try {
        // Call BFF API to get Office URL
        const response = await this.bffClient.getOfficeUrl(
            documentId,
            accessToken,
            correlationId
        );

        // Update state to editor mode
        this.setState({
            officeUrl: response.officeUrl,
            mode: 'editor',
            isLoading: false,
            // Show dialog if user has read-only access
            showReadOnlyDialog: !response.permissions.canEdit
        });

        console.log(
            `[FilePreview] Editor opened | CanEdit: ${response.permissions.canEdit} | Role: ${response.permissions.role}`
        );

        // Log permission details for debugging
        if (!response.permissions.canEdit) {
            console.warn(
                `[FilePreview] User has read-only access. Office Online will load in read-only mode.`
            );
        }

    } catch (error) {
        // Handle API errors
        const errorMessage = error instanceof Error ? error.message : String(error);
        console.error('[FilePreview] Failed to open editor:', errorMessage);

        this.setState({
            isLoading: false,
            error: errorMessage,
            mode: 'preview' // Stay in preview mode on error
        });
    }
};
```

---

### Step 4: Add Back to Preview Method

**Location**: After `handleOpenEditor()` method

```typescript
/**
 * Return to preview mode from editor mode
 *
 * Resets state to show preview iframe and hides read-only dialog.
 */
private handleBackToPreview = (): void => {
    console.log('[FilePreview] Returning to preview mode');

    this.setState({
        mode: 'preview',
        showReadOnlyDialog: false
    });
};
```

---

### Step 5: Add Dialog Dismiss Method

**Location**: After `handleBackToPreview()` method

```typescript
/**
 * Dismiss the read-only permission dialog
 *
 * User can dismiss the dialog and continue using editor
 * in read-only mode (Office Online enforces this).
 */
private dismissReadOnlyDialog = (): void => {
    console.log('[FilePreview] Dismissing read-only dialog');

    this.setState({
        showReadOnlyDialog: false
    });
};
```

---

## Validation & Review

### Pre-Commit Checklist

1. **TypeScript Compilation**:
   ```bash
   cd C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer
   npm run build
   ```
   - [ ] No TypeScript errors
   - [ ] State properties match `FilePreviewState` interface

2. **Code Review**:
   ```bash
   git diff C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer/FilePreview.tsx
   ```
   - [ ] State initialization includes all new properties
   - [ ] All methods use arrow function syntax (`=` instead of function keyword)
   - [ ] Async methods properly handle errors with try/catch
   - [ ] Console logging includes correlation ID

3. **Method Verification**:
   - [ ] `isOfficeFile()` returns boolean
   - [ ] `handleOpenEditor()` is async, calls BffClient, updates state
   - [ ] `handleBackToPreview()` resets mode to 'preview'
   - [ ] `dismissReadOnlyDialog()` hides dialog

4. **State Management**:
   - [ ] `setState()` calls are immutable (don't mutate state directly)
   - [ ] Loading states properly managed (isLoading: true before API call, false after)
   - [ ] Error states properly set on catch blocks

---

## Testing

### Unit Test Cases (Manual Verification)

1. **Office File Detection**:
   ```typescript
   // In browser console after deploying:
   const component = /* get component instance */;
   console.log(component.isOfficeFile('docx')); // Should be true
   console.log(component.isOfficeFile('pdf'));  // Should be false
   console.log(component.isOfficeFile(undefined)); // Should be false
   ```

2. **State Transitions**:
   - Initial state: `mode: 'preview'`
   - After clicking "Open in Editor": `mode: 'editor'`, `officeUrl` populated
   - After clicking "Back to Preview": `mode: 'preview'`

---

## Acceptance Criteria

- [x] State initialization includes: `officeUrl`, `mode`, `showReadOnlyDialog`
- [x] `isOfficeFile()` method correctly identifies Office file extensions
- [x] `handleOpenEditor()` calls `BffClient.getOfficeUrl()` and updates state
- [x] `handleOpenEditor()` sets `showReadOnlyDialog: true` when `canEdit: false`
- [x] `handleBackToPreview()` resets mode to 'preview'
- [x] `dismissReadOnlyDialog()` hides dialog
- [x] All methods have JSDoc comments
- [x] Error handling preserves UI consistency (stays in preview mode on error)
- [x] TypeScript compiles without errors

---

## Common Issues & Solutions

### Issue 1: "Property 'officeUrl' does not exist on type 'FilePreviewState'"
**Symptom**: TypeScript error during build

**Root Cause**: Task 1.1 not completed correctly

**Solution**:
```bash
# Verify FilePreviewState interface
grep -A 15 "FilePreviewState" C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer/types.ts
```

### Issue 2: "Property 'getOfficeUrl' does not exist on type 'BffClient'"
**Symptom**: TypeScript error on method call

**Root Cause**: Task 1.2 not completed

**Solution**: Complete Task 1.2 first

### Issue 3: Console Error - "Cannot read property 'canEdit' of undefined"
**Symptom**: Runtime error when opening editor

**Root Cause**: BFF API returning different response structure

**Solution**: Add defensive check:
```typescript
showReadOnlyDialog: !(response?.permissions?.canEdit ?? false)
```

---

## Dependencies

### Required Before This Task
- ✅ Task 1.1: TypeScript Interfaces (FilePreviewState)
- ✅ Task 1.2: BffClient Method (getOfficeUrl)

### Required After This Task
- Task 1.4: Render method will use these methods

---

## Files Modified

- `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\FilePreview.tsx`

---

## Estimated Effort Breakdown

| Activity | Time |
|----------|------|
| Update state initialization | 5 min |
| Add isOfficeFile() method | 5 min |
| Add handleOpenEditor() method | 15 min |
| Add handleBackToPreview() method | 5 min |
| Add dismissReadOnlyDialog() method | 3 min |
| Add JSDoc comments | 7 min |
| Verify TypeScript compilation | 5 min |
| **Total** | **45 min** |

---

## Next Task

**Task 1.4**: Update FilePreview Render Method
- Adds UI buttons and iframe src toggle logic
- Uses methods created in this task
