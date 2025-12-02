# Task 1.4: Update FilePreview Render Method

**Phase**: 1 - Core Functionality
**Priority**: High (Blocking)
**Estimated Time**: 45 minutes
**Depends On**: Task 1.3 (FilePreview State & Methods)
**Blocks**: Task 1.5 (Button Styles)

---

## Objective

Update FilePreview component's `render()` method to add "Open in Editor" and "Back to Preview" buttons, toggle iframe src based on mode, and display read-only dialog.

## Context & Knowledge Required

### What You Need to Know
1. **React JSX**: Conditional rendering with `&&` and ternary operators
2. **Fluent UI Dialog**: Dialog component props and configuration
3. **Iframe Security**: sandbox and allow attributes
4. **State-driven UI**: Rendering based on `this.state.mode`

### Files to Review Before Starting
- **FilePreview.tsx (current render)**: [C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\FilePreview.tsx](C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\FilePreview.tsx#L129-L187)
- **Fluent UI Dialog Docs**: [Dialog Component](https://developer.microsoft.com/en-us/fluentui#/controls/web/dialog)

---

## Implementation Prompt

### Step 1: Add Fluent UI Imports

**Location**: Top of file (~line 8)

**Current Imports**:
```typescript
import { Spinner, SpinnerSize, MessageBar, MessageBarType } from '@fluentui/react';
```

**Updated Imports**:
```typescript
import {
    Spinner,
    SpinnerSize,
    MessageBar,
    MessageBarType,
    Dialog,
    DialogType,
    DialogFooter,
    PrimaryButton
} from '@fluentui/react';
```

---

### Step 2: Update render() Method

**Location**: `render()` method (~line 129)

**Replace the entire render method** with:

```typescript
public render(): React.ReactNode {
    const {
        isLoading,
        error,
        previewUrl,
        officeUrl,
        documentInfo,
        mode,
        showReadOnlyDialog
    } = this.state;

    return (
        <div className="spe-file-viewer">
            {/* Loading State */}
            {isLoading && (
                <div className="spe-file-viewer__loading">
                    <Spinner
                        size={SpinnerSize.large}
                        label={mode === 'editor' ? 'Loading editor...' : 'Loading preview...'}
                        ariaLive="assertive"
                    />
                </div>
            )}

            {/* Error State */}
            {!isLoading && error && (
                <div className="spe-file-viewer__error">
                    <MessageBar
                        messageBarType={MessageBarType.error}
                        isMultiline={true}
                    >
                        <strong>
                            Unable to load file {mode === 'editor' ? 'editor' : 'preview'}
                        </strong>
                        <p>{error}</p>
                    </MessageBar>
                    <button
                        className="spe-file-viewer__retry-button"
                        onClick={this.handleRetry}
                    >
                        Retry
                    </button>
                </div>
            )}

            {/* Preview/Editor State */}
            {!isLoading && !error && (previewUrl || officeUrl) && (
                <div className="spe-file-viewer__preview">
                    {/* Open in Editor Button (Preview Mode + Office Files Only) */}
                    {mode === 'preview' && this.isOfficeFile(documentInfo?.fileExtension) && (
                        <button
                            className="spe-file-viewer__open-editor-button"
                            onClick={this.handleOpenEditor}
                            aria-label="Open in Office Online Editor"
                            title="Edit this document in Office Online"
                        >
                            Open in Editor
                        </button>
                    )}

                    {/* Back to Preview Button (Editor Mode Only) */}
                    {mode === 'editor' && (
                        <button
                            className="spe-file-viewer__back-to-preview-button"
                            onClick={this.handleBackToPreview}
                            aria-label="Return to preview mode"
                            title="Return to read-only preview"
                        >
                            ← Back to Preview
                        </button>
                    )}

                    {/* Iframe - Dynamic src based on mode */}
                    <iframe
                        className="spe-file-viewer__iframe"
                        src={mode === 'editor' ? officeUrl! : previewUrl!}
                        title={mode === 'editor' ? 'Office Editor' : 'File Preview'}
                        sandbox="allow-same-origin allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox"
                        allow="autoplay"
                    />
                </div>
            )}

            {/* Empty State */}
            {!isLoading && !error && !previewUrl && !officeUrl && (
                <div className="spe-file-viewer__empty">
                    <MessageBar messageBarType={MessageBarType.info}>
                        No document selected
                    </MessageBar>
                </div>
            )}

            {/* Read-Only Permission Dialog */}
            {showReadOnlyDialog && (
                <Dialog
                    hidden={!showReadOnlyDialog}
                    onDismiss={this.dismissReadOnlyDialog}
                    dialogContentProps={{
                        type: DialogType.normal,
                        title: 'File Opened in Read-Only Mode',
                        subText: 'You have view-only access to this file. To make changes, contact the file owner to request edit permissions.'
                    }}
                    modalProps={{
                        isBlocking: false,
                        styles: { main: { maxWidth: 450 } }
                    }}
                >
                    <DialogFooter>
                        <PrimaryButton
                            onClick={this.dismissReadOnlyDialog}
                            text="OK"
                        />
                    </DialogFooter>
                </Dialog>
            )}
        </div>
    );
}
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
   - [ ] Fluent UI imports resolve correctly

2. **Code Review**:
   - [ ] Buttons only show in appropriate modes (preview vs editor)
   - [ ] Iframe src uses ternary: `mode === 'editor' ? officeUrl : previewUrl`
   - [ ] Dialog only renders when `showReadOnlyDialog === true`
   - [ ] All button onClick handlers reference correct methods

3. **Accessibility**:
   - [ ] Buttons have `aria-label` attributes
   - [ ] Buttons have `title` tooltips
   - [ ] Spinner has `ariaLive="assertive"`
   - [ ] Dialog is dismissible (not blocking)

4. **Conditional Rendering Logic**:
   - [ ] Office button: `mode === 'preview' && isOfficeFile()`
   - [ ] Back button: `mode === 'editor'`
   - [ ] Iframe: `previewUrl || officeUrl`

---

## Acceptance Criteria

- [x] Fluent UI Dialog, DialogFooter, PrimaryButton imports added
- [x] "Open in Editor" button renders in preview mode for Office files only
- [x] "Back to Preview" button renders in editor mode
- [x] Iframe src switches between previewUrl and officeUrl based on mode
- [x] Read-only dialog shows when showReadOnlyDialog === true
- [x] All states (loading, error, empty) still work correctly
- [x] TypeScript compiles without errors
- [x] No console warnings about missing keys or props

---

## Common Issues & Solutions

### Issue 1: "Cannot find module 'Dialog' from '@fluentui/react'"
**Symptom**: Build fails with import error

**Solution**: Verify @fluentui/react package version includes Dialog:
```bash
grep "@fluentui/react" C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer/package.json
```

If missing, install:
```bash
npm install @fluentui/react --save
```

### Issue 2: Buttons don't appear
**Symptom**: UI doesn't show buttons after deploying

**Solution**: Verify state conditions:
- Check `mode` state value in React DevTools
- Check `documentInfo?.fileExtension` value
- Verify `isOfficeFile()` returns true for test file

### Issue 3: Dialog doesn't close
**Symptom**: Dialog stays open after clicking OK

**Solution**: Verify `dismissReadOnlyDialog()` method is correctly bound and setState is called.

---

## Files Modified

- `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\FilePreview.tsx`

---

## Next Task

**Task 1.5**: Add Button Styles
- CSS for "Open in Editor" button (floating top-right)
- CSS for "Back to Preview" button (floating top-left)
