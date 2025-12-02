# Work Item 5: Implement Upload Progress Component

**Sprint:** 7B - Document Quick Create
**Estimated Time:** 3 hours
**Prerequisites:** Work Item 4 (File Upload UI)
**Status:** Ready to Start
**Code Reference:** CODE-REFERENCE-UI-COMPONENTS.md

---

## Objective

Create React component to display real-time upload progress during multi-file upload to SharePoint Embedded. Show overall progress, per-file status, and error handling.

---

## Context

When user clicks "Save and Create Documents" button:
1. Upload process starts (multi-file)
2. Progress component replaces file selection UI
3. Shows: overall progress bar, per-file status icons, current file being uploaded
4. Updates in real-time via progress callbacks
5. Shows success/error summary when complete

**Result:** User has clear visibility into upload progress and status.

---

## Implementation Steps

### Step 1: Create Component File

Create: `src/controls/UniversalQuickCreate/UniversalQuickCreate/components/UploadProgress.tsx`

```typescript
import React from 'react';
import {
    ProgressBar,
    Text,
    makeStyles,
    tokens
} from '@fluentui/react-components';
import {
    Checkmark24Filled,
    ErrorCircle24Filled,
    Spinner
} from '@fluentui/react-icons';

export interface FileUploadStatus {
    fileName: string;
    status: 'pending' | 'uploading' | 'complete' | 'failed';
    error?: string;
}

export interface UploadProgressProps {
    files: FileUploadStatus[];
    currentFileIndex: number;
    totalFiles: number;
    overallProgress: number;  // 0-100
}

export const UploadProgress: React.FC<UploadProgressProps> = (props) => {
    // Implementation
};
```

---

### Step 2: Implement Progress Bar

Overall progress bar (see CODE-REFERENCE-UI-COMPONENTS.md):

```typescript
const progressPercent = props.overallProgress;
const progressLabel = `Uploading ${props.currentFileIndex + 1} of ${props.totalFiles}...`;

<div className={styles.progressSection}>
    <Text weight="semibold">{progressLabel}</Text>
    <ProgressBar
        value={progressPercent / 100}
        max={1}
        thickness="large"
        color="brand"
    />
    <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
        {Math.round(progressPercent)}% complete
    </Text>
</div>
```

**Key Points:**
- `value` is 0-1 (divide percent by 100)
- `thickness="large"` for visibility
- `color="brand"` for blue primary color
- Label shows "Uploading 2 of 5..."

---

### Step 3: Implement File Status Icons

Status icons for each file (see CODE-REFERENCE for complete pattern):

```typescript
const getStatusIcon = (status: FileUploadStatus['status']) => {
    switch (status) {
        case 'complete':
            return <Checkmark24Filled style={{ color: tokens.colorPaletteGreenForeground1 }} />;
        case 'failed':
            return <ErrorCircle24Filled style={{ color: tokens.colorPaletteRedForeground1 }} />;
        case 'uploading':
            return <Spinner size="tiny" />;
        case 'pending':
            return <div className={styles.pendingIcon} />;
        default:
            return null;
    }
};
```

**Icon Colors:**
- Complete: Green (`tokens.colorPaletteGreenForeground1`)
- Failed: Red (`tokens.colorPaletteRedForeground1`)
- Uploading: Spinner (blue animation)
- Pending: Gray circle outline

---

### Step 4: Implement File Status List

List showing all files with status:

```typescript
<div className={styles.fileList}>
    <Text weight="semibold">Files</Text>
    {props.files.map((file, index) => (
        <div key={index} className={styles.fileItem}>
            <div className={styles.fileInfo}>
                {getStatusIcon(file.status)}
                <Text>{file.fileName}</Text>
            </div>
            {file.status === 'failed' && file.error && (
                <Text size={200} style={{ color: tokens.colorPaletteRedForeground1 }}>
                    {file.error}
                </Text>
            )}
        </div>
    ))}
</div>
```

**Display Rules:**
- Show all files (pending, uploading, complete, failed)
- Highlight current file being uploaded (bold or background color)
- Show error message for failed files
- Scroll if list exceeds height

---

### Step 5: Style with Fluent UI

```typescript
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL,
        padding: tokens.spacingVerticalM
    },
    progressSection: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS
    },
    fileList: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
        maxHeight: '300px',
        overflowY: 'auto'
    },
    fileItem: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
        padding: tokens.spacingVerticalS,
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        borderRadius: tokens.borderRadiusMedium
    },
    fileInfo: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS
    },
    pendingIcon: {
        width: '24px',
        height: '24px',
        borderRadius: '50%',
        border: `2px solid ${tokens.colorNeutralStroke1}`
    },
    currentFile: {
        backgroundColor: tokens.colorNeutralBackground1Selected
    }
});
```

---

### Step 6: Highlight Current File

Add visual indicator for current file:

```typescript
<div
    key={index}
    className={`${styles.fileItem} ${
        index === props.currentFileIndex && file.status === 'uploading'
            ? styles.currentFile
            : ''
    }`}
>
    {/* File info */}
</div>
```

---

### Step 7: Add Summary Section (Optional)

Show summary after completion:

```typescript
const completedCount = props.files.filter(f => f.status === 'complete').length;
const failedCount = props.files.filter(f => f.status === 'failed').length;
const isComplete = props.currentFileIndex === props.totalFiles;

{isComplete && (
    <div className={styles.summary}>
        <Text weight="semibold">Upload Complete</Text>
        <Text>
            {completedCount} of {props.totalFiles} files uploaded successfully
        </Text>
        {failedCount > 0 && (
            <Text style={{ color: tokens.colorPaletteRedForeground1 }}>
                {failedCount} file(s) failed
            </Text>
        )}
    </div>
)}
```

---

## Progress State Management

In `UniversalQuickCreatePCF.ts`:

```typescript
// State
private uploadProgress: FileUploadStatus[] = [];
private currentFileIndex: number = 0;
private overallProgress: number = 0;

// Initialize progress tracking
private initializeProgress(files: File[]): void {
    this.uploadProgress = files.map(file => ({
        fileName: file.name,
        status: 'pending' as const
    }));
    this.currentFileIndex = 0;
    this.overallProgress = 0;
}

// Progress callback from MultiFileUploadService
private handleUploadProgress(progress: {
    current: number;
    total: number;
    currentFileName: string;
    status: 'uploading' | 'complete' | 'failed';
    error?: string;
}): void {
    // Update current file status
    this.uploadProgress[progress.current - 1] = {
        fileName: progress.currentFileName,
        status: progress.status,
        error: progress.error
    };

    // Update overall progress
    this.currentFileIndex = progress.current - 1;
    this.overallProgress = (progress.current / progress.total) * 100;

    // Re-render to update UI
    this.notifyOutputChanged();
}
```

---

## Integration with Upload Service

When upload starts:

```typescript
async handleSaveAndCreateDocuments(): void {
    // Initialize progress
    this.initializeProgress(this.selectedFiles);
    this.isUploading = true;
    this.notifyOutputChanged();  // Show progress UI

    // Start upload with progress callback
    const result = await this.multiFileService.uploadFiles(
        {
            files: this.selectedFiles,
            formData: this.getFormData(),
            parentEntityName: this.parentEntityName,
            parentRecordId: this.parentRecordId
        },
        (progress) => {
            this.handleUploadProgress(progress);
        }
    );

    // Upload complete
    this.isUploading = false;

    if (result.success) {
        // Close form and refresh subgrid
        this.closeQuickCreateForm();
        this.refreshParentSubgrid();
    } else {
        // Show error summary
        this.notifyOutputChanged();
    }
}
```

---

## Component Render Logic

In `updateView()`:

```typescript
public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    if (this.isUploading) {
        // Show progress UI
        return React.createElement(UploadProgress, {
            files: this.uploadProgress,
            currentFileIndex: this.currentFileIndex,
            totalFiles: this.selectedFiles.length,
            overallProgress: this.overallProgress
        });
    } else {
        // Show file upload UI
        return React.createElement(FileUploadField, {
            // ... props
        });
    }
}
```

---

## Testing Checklist

- [ ] Progress component renders when upload starts
- [ ] Overall progress bar updates (0-100%)
- [ ] Progress label shows "Uploading X of Y..."
- [ ] Percent text updates in real-time
- [ ] File list shows all files
- [ ] Status icons correct: pending (gray circle), uploading (spinner), complete (green check), failed (red X)
- [ ] Current file highlighted (background color)
- [ ] Error messages appear for failed files
- [ ] File list scrolls if > 10 files
- [ ] Summary shows after completion
- [ ] Component handles partial failures (some succeed, some fail)

---

## Common Issues

### Issue: Progress doesn't update
**Cause:** `notifyOutputChanged()` not called
**Fix:** Call after updating state in progress callback

### Issue: Icons don't show
**Cause:** Fluent UI icons not imported
**Fix:** Import from `@fluentui/react-icons`

### Issue: Progress jumps to 100% immediately
**Cause:** Progress calculation incorrect
**Fix:** Ensure `(current / total) * 100` is correct

---

## Error Handling

Display errors clearly:

```typescript
{file.status === 'failed' && (
    <div className={styles.errorContainer}>
        <Text size={200} style={{ color: tokens.colorPaletteRedForeground1 }}>
            Error: {file.error || 'Upload failed'}
        </Text>
    </div>
)}
```

---

## Accessibility

- Progress bar has `aria-valuenow`, `aria-valuemin`, `aria-valuemax`
- Status icons have `aria-label` (e.g., "Upload complete")
- Error messages associated with file names
- Keyboard navigation not needed (read-only display)

---

## Verification

```bash
# Component file exists
ls src/controls/UniversalQuickCreate/UniversalQuickCreate/components/UploadProgress.tsx

# Build succeeds
npm run build

# Test in browser
# 1. Open Quick Create
# 2. Select multiple files
# 3. Click "Save and Create Documents"
# 4. Verify progress UI appears
# 5. Watch progress bar update
# 6. Verify file status icons change
# 7. Check summary after completion
```

---

**Status:** Ready for implementation
**Time:** 3 hours
**Code Reference:** CODE-REFERENCE-UI-COMPONENTS.md (progress component pattern)
**Next:** Work Item 6 - Integration Layer
