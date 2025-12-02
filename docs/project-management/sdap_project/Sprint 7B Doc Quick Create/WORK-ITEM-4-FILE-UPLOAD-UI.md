# Work Item 4: Implement File Upload UI Component

**Sprint:** 7B - Document Quick Create
**Estimated Time:** 4 hours
**Prerequisites:** Work Item 3 (Manifest Update)
**Status:** Ready to Start
**Code Reference:** CODE-REFERENCE-UI-COMPONENTS.md

---

## Objective

Create React component for file selection and management in Quick Create form. Support multi-file selection, file list display, and optional description field.

---

## Context

The file upload UI is the PRIMARY user interaction in Quick Create:
1. User clicks "+ Add File" button
2. Native file picker opens (multi-select enabled)
3. Selected files appear in list (name, size, remove button)
4. User can add more files or remove files
5. User can optionally add description
6. Custom "Save and Create Documents" button enables when files selected

**Result:** Clean, familiar file upload experience matching Power Apps UI conventions.

---

## Implementation Steps

### Step 1: Create Component File

Create: `src/controls/UniversalQuickCreate/UniversalQuickCreate/components/FileUploadField.tsx`

Component structure:
```typescript
import React, { useRef, useState } from 'react';
import {
    Button,
    Text,
    makeStyles,
    tokens
} from '@fluentui/react-components';
import { ArrowUpload24Regular, Delete24Regular } from '@fluentui/react-icons';

export interface FileUploadFieldProps {
    allowMultiple: boolean;
    onFilesChange: (files: File[]) => void;
    disabled?: boolean;
}

export const FileUploadField: React.FC<FileUploadFieldProps> = (props) => {
    // Implementation
};
```

---

### Step 2: Implement File Selection

Use hidden `<input type="file">` with button trigger (see CODE-REFERENCE-UI-COMPONENTS.md):

```typescript
const fileInputRef = useRef<HTMLInputElement>(null);
const [selectedFiles, setSelectedFiles] = useState<File[]>([]);

const handleButtonClick = () => {
    fileInputRef.current?.click();
};

const handleFileSelect = (event: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(event.target.files || []);
    const updatedFiles = [...selectedFiles, ...files];
    setSelectedFiles(updatedFiles);
    props.onFilesChange(updatedFiles);

    // Reset input for same file re-selection
    event.target.value = '';
};
```

**Key Points:**
- Hidden input with `ref` for programmatic trigger
- `multiple` attribute controlled by `allowMultiple` prop
- Reset input value to allow re-selection
- Merge new files with existing (accumulate)

---

### Step 3: Implement File List Display

Show selected files with name, size, and remove button:

```typescript
const handleRemoveFile = (index: number) => {
    const updatedFiles = selectedFiles.filter((_, i) => i !== index);
    setSelectedFiles(updatedFiles);
    props.onFilesChange(updatedFiles);
};

const formatFileSize = (bytes: number): string => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};
```

File list UI (see CODE-REFERENCE for complete pattern):
- Each file in bordered container
- File icon + name + size
- Remove button (red X icon)
- Hover effects for remove button

---

### Step 4: Style with Fluent UI Tokens

Use `makeStyles` for consistent Power Apps styling:

```typescript
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        padding: tokens.spacingVerticalM
    },
    uploadButton: {
        width: 'fit-content'
    },
    fileList: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS
    },
    fileItem: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: tokens.spacingVerticalS,
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        borderRadius: tokens.borderRadiusMedium
    },
    // ... more styles
});
```

**Design Tokens Used:**
- `tokens.colorBrandBackground` - Primary blue
- `tokens.colorNeutralStroke1` - Borders
- `tokens.borderRadiusMedium` - Rounded corners
- `tokens.spacingVerticalM/S` - Consistent spacing

---

### Step 5: Add Optional Description Field

Optional text area for document description:

```typescript
const [description, setDescription] = useState('');

<TextField
    label="Description (Optional)"
    multiline
    rows={3}
    value={description}
    onChange={(_, data) => setDescription(data.value)}
    placeholder="Enter document description..."
/>
```

**Note:** Description passed to parent via separate callback or context.

---

### Step 6: Render Complete UI

Final JSX structure:

```tsx
return (
    <div className={styles.container}>
        {/* Hidden file input */}
        <input
            ref={fileInputRef}
            type="file"
            multiple={allowMultiple}
            onChange={handleFileSelect}
            style={{ display: 'none' }}
        />

        {/* Add File Button */}
        <Button
            appearance="primary"
            icon={<ArrowUpload24Regular />}
            onClick={handleButtonClick}
            disabled={disabled}
            className={styles.uploadButton}
        >
            {selectedFiles.length > 0 ? 'Add More Files' : 'Add File'}
        </Button>

        {/* Selected Files List */}
        {selectedFiles.length > 0 && (
            <div className={styles.fileList}>
                <Text weight="semibold">
                    Selected Files ({selectedFiles.length})
                </Text>
                {selectedFiles.map((file, index) => (
                    <div key={index} className={styles.fileItem}>
                        {/* File info */}
                        <div className={styles.fileInfo}>
                            <Text>{file.name}</Text>
                            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                                {formatFileSize(file.size)}
                            </Text>
                        </div>
                        {/* Remove button */}
                        <Button
                            appearance="subtle"
                            icon={<Delete24Regular />}
                            onClick={() => handleRemoveFile(index)}
                            aria-label={`Remove ${file.name}`}
                        />
                    </div>
                ))}
            </div>
        )}

        {/* Optional description */}
        <TextField
            label="Description (Optional)"
            multiline
            rows={3}
            value={description}
            onChange={(_, data) => setDescription(data.value)}
        />
    </div>
);
```

---

## Component Props Interface

```typescript
export interface FileUploadFieldProps {
    allowMultiple: boolean;          // Enable multi-select
    onFilesChange: (files: File[]) => void;  // Callback when files change
    disabled?: boolean;              // Disable during upload
    maxFileSize?: number;            // Optional size limit (bytes)
    acceptedFileTypes?: string[];    // Optional file type filter
}
```

---

## Integration with PCF Control

In `UniversalQuickCreatePCF.ts`:

```typescript
// State management
private selectedFiles: File[] = [];

// Render React component
public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    const allowMultiple = context.parameters.allowMultipleFiles.raw !== false;

    return React.createElement(FileUploadField, {
        allowMultiple,
        onFilesChange: this.handleFilesChange.bind(this),
        disabled: this.isUploading
    });
}

// Handle file selection
private handleFilesChange(files: File[]): void {
    this.selectedFiles = files;

    // Update custom button state
    this.updateButtonState(
        files.length > 0,  // hasFiles
        files.length,       // fileCount
        false               // isUploading
    );
}
```

---

## File Validation (Optional Enhancement)

Add validation for file size and type:

```typescript
const validateFile = (file: File): string | null => {
    // Check size (e.g., 100MB limit)
    if (props.maxFileSize && file.size > props.maxFileSize) {
        return `File "${file.name}" exceeds ${formatFileSize(props.maxFileSize)} limit`;
    }

    // Check type
    if (props.acceptedFileTypes && props.acceptedFileTypes.length > 0) {
        const extension = file.name.split('.').pop()?.toLowerCase();
        if (!extension || !props.acceptedFileTypes.includes(extension)) {
            return `File type "${extension}" not allowed`;
        }
    }

    return null; // Valid
};

const handleFileSelect = (event: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(event.target.files || []);

    // Validate each file
    const errors: string[] = [];
    const validFiles: File[] = [];

    files.forEach(file => {
        const error = validateFile(file);
        if (error) {
            errors.push(error);
        } else {
            validFiles.push(file);
        }
    });

    // Show errors (optional)
    if (errors.length > 0) {
        console.warn('File validation errors:', errors);
        // Could show error notification
    }

    // Add valid files
    const updatedFiles = [...selectedFiles, ...validFiles];
    setSelectedFiles(updatedFiles);
    props.onFilesChange(updatedFiles);
};
```

---

## Testing Checklist

- [ ] Component renders in Quick Create form
- [ ] "+ Add File" button opens file picker
- [ ] Single file selection works
- [ ] Multiple file selection works (when enabled)
- [ ] Selected files appear in list with name and size
- [ ] File sizes formatted correctly (B, KB, MB)
- [ ] Remove button deletes file from list
- [ ] "Add More Files" appears after first selection
- [ ] Description field works (optional)
- [ ] Component disabled during upload
- [ ] File input resets after selection (can re-select same file)
- [ ] onFilesChange callback fires correctly

---

## Common Issues

### Issue: File picker doesn't open
**Cause:** `ref` not attached or button click not triggering
**Fix:** Verify `fileInputRef.current?.click()` in handler

### Issue: Same file can't be selected twice
**Cause:** Input value not reset
**Fix:** Add `event.target.value = ''` after file selection

### Issue: Styling doesn't match Power Apps
**Cause:** Not using Fluent UI tokens
**Fix:** Replace hardcoded colors with `tokens.*` values

---

## Accessibility Notes

- Use `aria-label` on remove buttons
- Ensure keyboard navigation works (Tab, Enter, Space)
- Add `role="button"` to interactive elements if needed
- Provide screen reader feedback for file addition/removal

---

## Verification

```bash
# Component file exists
ls src/controls/UniversalQuickCreate/UniversalQuickCreate/components/FileUploadField.tsx

# Build succeeds
npm run build

# Test in browser
# 1. Open Quick Create
# 2. Click "+ Add File"
# 3. Select multiple files
# 4. Verify list display
# 5. Remove a file
# 6. Add more files
```

---

**Status:** Ready for implementation
**Time:** 4 hours
**Code Reference:** CODE-REFERENCE-UI-COMPONENTS.md (complete component pattern)
**Next:** Work Item 5 - Upload Progress Component
