# Work Item 5: Create File Upload UI

**Estimated Time:** 2 hours
**Prerequisites:** Work Item 4 complete (Multi-file logic implemented)
**Status:** Ready to Start

---

## Objective

Create the FileUploadField.tsx React component that renders the file picker UI using Fluent UI v9.

---

## Context

The FileUploadField component is the visual interface for file upload. It provides:
- File picker button (click to select files)
- Drag-and-drop area
- File list with upload progress
- Error messages
- Container ID display
- Multi-file support

**Design:** Clean, modern UI using Fluent UI v9 components.

---

## File to Create

**Path:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/components/FileUploadField.tsx`

---

## Component Interface

```typescript
export interface FileUploadFieldProps {
    /** Allow multiple file selection */
    allowMultiple: boolean;

    /** Container ID (from parent Matter) */
    containerId: string | null;

    /** Loading state */
    isLoading: boolean;

    /** Error message */
    error: string | null;

    /** Uploaded files metadata */
    uploadedFiles: SpeFileMetadata[];

    /** Callback when files selected */
    onFilesSelected: (files: File[]) => void;

    /** Optional: Upload progress */
    uploadProgress?: {
        current: number;
        total: number;
    };
}
```

---

## UI Design

```
┌────────────────────────────────────────────────────┐
│ File Upload to SharePoint Embedded                 │
├────────────────────────────────────────────────────┤
│                                                     │
│ Container ID: abc-123-def-456                       │
│                                                     │
│ ┌──────────────────────────────────────────────┐   │
│ │  📁 Click or drag files here                 │   │
│ │                                              │   │
│ │  Drop files to upload                        │   │
│ └──────────────────────────────────────────────┘   │
│                                                     │
│ [Choose Files] (Multiple files allowed)            │
│                                                     │
│ ─── Uploaded Files (2) ───                         │
│                                                     │
│ ✅ contract.pdf (2.0 MB)                           │
│ ✅ invoice.pdf (1.5 MB)                            │
│                                                     │
└────────────────────────────────────────────────────┘
```

**With Loading State:**

```
┌────────────────────────────────────────────────────┐
│ File Upload to SharePoint Embedded                 │
├────────────────────────────────────────────────────┤
│                                                     │
│ 🔄 Uploading 2 of 3 files...                       │
│                                                     │
│ ████████████░░░░░░░░ 66%                           │
│                                                     │
└────────────────────────────────────────────────────┘
```

**With Error:**

```
┌────────────────────────────────────────────────────┐
│ File Upload to SharePoint Embedded                 │
├────────────────────────────────────────────────────┤
│                                                     │
│ ⚠️ Error: Container ID not found                   │
│                                                     │
└────────────────────────────────────────────────────┘
```

---

## Complete Component Implementation

```typescript
/**
 * File Upload Field Component
 *
 * Renders file picker UI for uploading files to SharePoint Embedded.
 * Uses Fluent UI v9 components for consistent styling.
 *
 * @version 2.0.0
 */

import * as React from 'react';
import {
    Button,
    Text,
    Title3,
    MessageBar,
    MessageBarBody,
    Spinner,
    makeStyles,
    tokens
} from '@fluentui/react-components';
import {
    ArrowUpload24Regular,
    DocumentRegular,
    CheckmarkCircle24Filled
} from '@fluentui/react-icons';
import { SpeFileMetadata } from '../types';

export interface FileUploadFieldProps {
    allowMultiple: boolean;
    containerId: string | null;
    isLoading: boolean;
    error: string | null;
    uploadedFiles: SpeFileMetadata[];
    onFilesSelected: (files: File[]) => void;
    uploadProgress?: {
        current: number;
        total: number;
    };
}

const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        padding: tokens.spacingVerticalL,
        maxWidth: '600px'
    },
    dropZone: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: tokens.spacingVerticalXXL,
        border: `2px dashed ${tokens.colorNeutralStroke1}`,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground3,
        cursor: 'pointer',
        transition: 'all 0.2s ease',
        ':hover': {
            backgroundColor: tokens.colorNeutralBackground3Hover,
            borderColor: tokens.colorBrandStroke1
        }
    },
    dropZoneDragging: {
        backgroundColor: tokens.colorBrandBackground2,
        borderColor: tokens.colorBrandStroke1
    },
    dropZoneDisabled: {
        cursor: 'not-allowed',
        opacity: 0.5
    },
    fileList: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS
    },
    fileItem: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingVerticalS,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground1
    },
    containerIdLabel: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingVerticalS,
        backgroundColor: tokens.colorNeutralBackground3,
        borderRadius: tokens.borderRadiusSmall
    },
    progressBar: {
        width: '100%',
        height: '8px',
        backgroundColor: tokens.colorNeutralBackground3,
        borderRadius: tokens.borderRadiusSmall,
        overflow: 'hidden'
    },
    progressBarFill: {
        height: '100%',
        backgroundColor: tokens.colorBrandBackground,
        transition: 'width 0.3s ease'
    }
});

export const FileUploadField: React.FC<FileUploadFieldProps> = ({
    allowMultiple,
    containerId,
    isLoading,
    error,
    uploadedFiles,
    onFilesSelected,
    uploadProgress
}) => {
    const styles = useStyles();
    const fileInputRef = React.useRef<HTMLInputElement>(null);
    const [isDragging, setIsDragging] = React.useState(false);

    // Handle file input change
    const handleFileChange = (event: React.ChangeEvent<HTMLInputElement>) => {
        const files = event.target.files;
        if (files && files.length > 0) {
            onFilesSelected(Array.from(files));
        }
        // Reset input to allow selecting same files again
        event.target.value = '';
    };

    // Handle drop zone click
    const handleDropZoneClick = () => {
        if (!isLoading && containerId) {
            fileInputRef.current?.click();
        }
    };

    // Handle drag events
    const handleDragOver = (event: React.DragEvent) => {
        event.preventDefault();
        if (!isLoading && containerId) {
            setIsDragging(true);
        }
    };

    const handleDragLeave = () => {
        setIsDragging(false);
    };

    const handleDrop = (event: React.DragEvent) => {
        event.preventDefault();
        setIsDragging(false);

        if (!isLoading && containerId) {
            const files = Array.from(event.dataTransfer.files);
            if (files.length > 0) {
                onFilesSelected(files);
            }
        }
    };

    // Format file size
    const formatFileSize = (bytes: number): string => {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    };

    // Calculate progress percentage
    const progressPercent = uploadProgress
        ? Math.round((uploadProgress.current / uploadProgress.total) * 100)
        : 0;

    return (
        <div className={styles.container}>
            <Title3>File Upload to SharePoint Embedded</Title3>

            {/* Container ID Display */}
            {containerId ? (
                <div className={styles.containerIdLabel}>
                    <DocumentRegular />
                    <Text size={300}>Container ID: {containerId}</Text>
                </div>
            ) : (
                <MessageBar intent="warning">
                    <MessageBarBody>
                        No Container ID found. Please ensure the parent Matter has a valid SharePoint container.
                    </MessageBarBody>
                </MessageBar>
            )}

            {/* Error Message */}
            {error && (
                <MessageBar intent="error">
                    <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
            )}

            {/* Loading State with Progress */}
            {isLoading && uploadProgress && (
                <div>
                    <Text>
                        Uploading {uploadProgress.current} of {uploadProgress.total} files... ({progressPercent}%)
                    </Text>
                    <div className={styles.progressBar}>
                        <div
                            className={styles.progressBarFill}
                            style={{ width: `${progressPercent}%` }}
                        />
                    </div>
                </div>
            )}

            {/* Loading State without Progress */}
            {isLoading && !uploadProgress && (
                <div style={{ display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS }}>
                    <Spinner size="small" />
                    <Text>Uploading files...</Text>
                </div>
            )}

            {/* Drop Zone */}
            {!isLoading && (
                <>
                    <div
                        className={`${styles.dropZone} ${
                            isDragging ? styles.dropZoneDragging : ''
                        } ${!containerId ? styles.dropZoneDisabled : ''}`}
                        onClick={handleDropZoneClick}
                        onDragOver={handleDragOver}
                        onDragLeave={handleDragLeave}
                        onDrop={handleDrop}
                    >
                        <ArrowUpload24Regular />
                        <Text size={400} weight="semibold">
                            {isDragging ? 'Drop files here' : 'Click or drag files here'}
                        </Text>
                        <Text size={200}>
                            {allowMultiple ? 'Multiple files allowed' : 'Single file only'}
                        </Text>
                    </div>

                    {/* Hidden File Input */}
                    <input
                        ref={fileInputRef}
                        type="file"
                        multiple={allowMultiple}
                        onChange={handleFileChange}
                        style={{ display: 'none' }}
                        disabled={!containerId}
                    />

                    {/* Choose Files Button */}
                    <Button
                        appearance="primary"
                        icon={<ArrowUpload24Regular />}
                        onClick={handleDropZoneClick}
                        disabled={!containerId}
                    >
                        Choose Files
                    </Button>
                </>
            )}

            {/* Uploaded Files List */}
            {uploadedFiles.length > 0 && (
                <div>
                    <Text size={400} weight="semibold">
                        Uploaded Files ({uploadedFiles.length})
                    </Text>
                    <div className={styles.fileList}>
                        {uploadedFiles.map((file, index) => (
                            <div key={index} className={styles.fileItem}>
                                <CheckmarkCircle24Filled style={{ color: tokens.colorPaletteGreenForeground1 }} />
                                <DocumentRegular />
                                <Text>
                                    {file.fileName} ({formatFileSize(file.fileSize)})
                                </Text>
                            </div>
                        ))}
                    </div>
                </div>
            )}
        </div>
    );
};
```

---

## Key Features

### 1. Drag-and-Drop Support

```typescript
<div
    onDragOver={handleDragOver}
    onDragLeave={handleDragLeave}
    onDrop={handleDrop}
>
```

**User Experience:**
- User drags file from desktop
- Drop zone highlights (blue background)
- User drops file
- File uploads automatically

---

### 2. File Picker Button

```typescript
<input
    type="file"
    multiple={allowMultiple}
    onChange={handleFileChange}
    style={{ display: 'none' }}
/>

<Button onClick={() => fileInputRef.current?.click()}>
    Choose Files
</Button>
```

**User Experience:**
- User clicks "Choose Files" button
- Native file picker dialog opens
- User selects file(s)
- File uploads automatically

---

### 3. Upload Progress

```typescript
{uploadProgress && (
    <div>
        Uploading {uploadProgress.current} of {uploadProgress.total} files... ({progressPercent}%)
        <div className={styles.progressBar}>
            <div style={{ width: `${progressPercent}%` }} />
        </div>
    </div>
)}
```

**User Experience:**
- Shows "Uploading 2 of 5 files... (40%)"
- Progress bar fills from left to right
- Updates in real-time

---

### 4. Uploaded Files List

```typescript
{uploadedFiles.map((file, index) => (
    <div key={index}>
        ✅ contract.pdf (2.0 MB)
    </div>
))}
```

**User Experience:**
- Shows list of successfully uploaded files
- Green checkmark indicates success
- File name and size displayed

---

### 5. Error Handling

```typescript
{error && (
    <MessageBar intent="error">
        <MessageBarBody>{error}</MessageBarBody>
    </MessageBar>
)}
```

**User Experience:**
- Red error banner at top
- Clear error message
- Doesn't block UI (user can try again)

---

## Styling with Fluent UI v9

**Benefits:**
- Consistent with Microsoft 365 design
- Accessible (WCAG 2.1 AA compliant)
- Responsive (works on all screen sizes)
- Theme-aware (dark mode support)

**Key Components Used:**
- `Button` - File picker button
- `MessageBar` - Error/warning messages
- `Spinner` - Loading indicator
- `Text` / `Title3` - Typography
- `makeStyles` - CSS-in-JS styling

---

## Testing the Component

### Test 1: Single File Upload

**Steps:**
1. Open Quick Create form
2. Click "Choose Files"
3. Select 1 file
4. Verify file uploads
5. Verify file appears in "Uploaded Files" list

**Expected:**
- File picker opens
- File uploads successfully
- Green checkmark shows
- File name and size displayed

---

### Test 2: Multi-File Upload

**Steps:**
1. Open Quick Create form
2. Click "Choose Files"
3. Select 3 files (hold Ctrl/Cmd)
4. Verify all files upload
5. Verify all 3 appear in list

**Expected:**
- All 3 files upload sequentially
- Progress shows "Uploading 1 of 3", "2 of 3", "3 of 3"
- All 3 files appear in list

---

### Test 3: Drag-and-Drop

**Steps:**
1. Open Quick Create form
2. Drag file from desktop
3. Hover over drop zone (should highlight)
4. Drop file
5. Verify file uploads

**Expected:**
- Drop zone highlights blue when dragging over
- File uploads automatically when dropped
- File appears in list

---

### Test 4: No Container ID

**Steps:**
1. Open Quick Create form from entity without Container ID
2. Verify warning message shows
3. Verify file picker is disabled

**Expected:**
- Yellow warning: "No Container ID found..."
- Drop zone grayed out
- "Choose Files" button disabled

---

### Test 5: Upload Error

**Steps:**
1. Disconnect network
2. Select file
3. Wait for error
4. Verify error message shows

**Expected:**
- Red error banner: "File upload failed: Network error"
- File picker remains active (can retry)
- No file in uploaded list

---

## Verification Checklist

After creating FileUploadField.tsx:

- ✅ Component created at correct path
- ✅ Imports Fluent UI v9 components
- ✅ Accepts FileUploadFieldProps interface
- ✅ Renders file picker button
- ✅ Supports drag-and-drop
- ✅ Shows Container ID
- ✅ Shows error messages
- ✅ Shows loading spinner
- ✅ Shows upload progress
- ✅ Shows uploaded files list
- ✅ Formats file sizes (KB, MB)
- ✅ Disables input when no Container ID
- ✅ Calls onFilesSelected() callback

---

## Dependencies

**Required NPM Packages:**

```json
{
    "@fluentui/react-components": "^9.54.0",
    "@fluentui/react-icons": "^2.0.239",
    "react": "^18.2.0",
    "react-dom": "^18.2.0"
}
```

**Already installed** in package.json (verify):

```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
cat package.json | grep "@fluentui/react-components"
```

---

## Next Steps

After completing this work item:

1. ✅ Create FileUploadField.tsx component
2. ✅ Verify Fluent UI packages installed
3. ✅ Test component in Quick Create form
4. ⏳ Move to Work Item 6: Configure Quick Create Form

---

**Status:** Ready for implementation
**Estimated Time:** 2 hours
**Next:** Work Item 6 - Configure Quick Create Form
