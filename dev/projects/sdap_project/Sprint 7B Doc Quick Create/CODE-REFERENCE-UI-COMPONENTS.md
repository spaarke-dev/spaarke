# Code Reference: UI Components

**Purpose:** Reference implementations for React UI components using Fluent UI v9.

**Used By:** Work Items 4 & 5

---

## Component 1: FileUploadField (File Picker)

### Purpose
File picker with multi-select, selected files list, and shared description field.

### Key Props Interface

```typescript
interface FileUploadFieldProps {
    selectedFiles: File[];
    onFilesChange: (files: File[]) => void;
    allowMultiple: boolean;
    maxFiles: number;
    disabled: boolean;
}
```

### File Picker Pattern

```typescript
const fileInputRef = React.useRef<HTMLInputElement>(null);

const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files || []);

    if (files.length > maxFiles) {
        alert(`Maximum ${maxFiles} files allowed`);
        return;
    }

    onFilesChange(files);
    e.target.value = ''; // Reset for re-selection
};

// Render
<input
    ref={fileInputRef}
    type="file"
    multiple={allowMultiple}
    onChange={handleFileSelect}
    style={{ display: 'none' }}
    disabled={disabled}
/>

<Button
    appearance="primary"
    onClick={() => fileInputRef.current?.click()}
    disabled={disabled}
>
    Choose Files
</Button>
```

**Key Points:**
- Hidden file input
- Button triggers click
- `multiple` attribute for multi-select
- Reset input after selection (allows re-select same files)

---

### Selected Files List Pattern

```typescript
const handleRemoveFile = (index: number) => {
    const newFiles = [...selectedFiles];
    newFiles.splice(index, 1);
    onFilesChange(newFiles);
};

const formatFileSize = (bytes: number): string => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};

// Render
{selectedFiles.map((file, index) => (
    <div key={index} className={styles.fileItem}>
        <DocumentRegular />
        <Text>{file.name}</Text>
        <Text size={200}>({formatFileSize(file.size)})</Text>
        <Button
            appearance="subtle"
            icon={<Dismiss24Regular />}
            onClick={() => handleRemoveFile(index)}
        />
    </div>
))}
```

**Key Points:**
- Map over array with index as key
- Format file size (B/KB/MB)
- Remove button per file
- Fluent UI icons

---

### Summary Section Pattern

```typescript
const totalSize = selectedFiles.reduce((sum, f) => sum + f.size, 0);

// Render
{selectedFiles.length > 0 && (
    <div className={styles.summary}>
        {selectedFiles.length} file{selectedFiles.length > 1 ? 's' : ''} • {formatFileSize(totalSize)} total
    </div>
)}
```

---

### Fluent UI Styling Pattern

```typescript
import { makeStyles, tokens } from '@fluentui/react-components';

const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        padding: tokens.spacingVerticalL
    },
    fileItem: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingVerticalS,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusMedium
    },
    summary: {
        padding: tokens.spacingVerticalS,
        backgroundColor: tokens.colorNeutralBackground3,
        borderRadius: tokens.borderRadiusSmall,
        fontSize: '12px',
        fontWeight: 600
    }
});
```

**Key Points:**
- Use `tokens` for spacing/colors (theme-aware)
- CSS-in-JS via `makeStyles`
- Flexbox layouts
- Consistent with Fluent UI design system

---

## Component 2: UploadProgress (Progress Indicator)

### Purpose
Show detailed upload progress with file-by-file status.

### Key Props Interface

```typescript
interface UploadProgressProps {
    files: File[];
    currentProgress: UploadProgress | null;  // From MultiFileUploadService
    completedFiles: string[];
    failedFiles: string[];
}
```

### Progress Bar Pattern

```typescript
import { ProgressBar } from '@fluentui/react-components';

const progressPercent = currentProgress
    ? (currentProgress.current / currentProgress.total) * 100
    : 0;

// Render
<ProgressBar
    value={progressPercent / 100}  // 0.0 to 1.0
    thickness="large"
/>

<Text>
    {currentProgress?.current} of {currentProgress?.total} files • {Math.round(progressPercent)}% complete
</Text>
```

**Key Points:**
- Fluent UI ProgressBar (0-1 scale)
- Calculate percentage
- Show both fraction and percentage

---

### File Status Icons Pattern

```typescript
import {
    Checkmark24Filled,
    ErrorCircle24Filled,
    Spinner
} from '@fluentui/react-components';

const getFileStatus = (fileName: string): 'complete' | 'failed' | 'uploading' | 'waiting' => {
    if (completedFiles.includes(fileName)) return 'complete';
    if (failedFiles.includes(fileName)) return 'failed';
    if (currentProgress?.fileName === fileName) return 'uploading';
    return 'waiting';
};

const getIcon = (status: string) => {
    switch (status) {
        case 'complete':
            return <Checkmark24Filled style={{ color: '#107c10' }} />;
        case 'failed':
            return <ErrorCircle24Filled style={{ color: '#a4262c' }} />;
        case 'uploading':
            return <Spinner size="tiny" />;
        default:
            return <ArrowSync24Regular style={{ color: '#8a8886' }} />;
    }
};
```

**Key Points:**
- Four states: complete, failed, uploading, waiting
- Color-coded icons
- Animated spinner for active upload

---

### File List with Status Pattern

```typescript
{files.map((file, index) => {
    const status = getFileStatus(file.name);
    const isActive = currentProgress?.fileName === file.name;

    return (
        <div
            key={index}
            className={`${styles.fileItem} ${isActive ? styles.active : ''}`}
        >
            {getIcon(status)}
            <Text className={styles.fileName}>{file.name}</Text>
            <Text size={200}>
                {status === 'complete' && '✓ Uploaded'}
                {status === 'failed' && '✗ Failed'}
                {status === 'uploading' && 'Uploading...'}
                {status === 'waiting' && 'Waiting...'}
            </Text>
        </div>
    );
})}
```

**Key Points:**
- Highlight active file
- Status text per file
- Icon + text combination

---

### Warning Message Pattern

```typescript
<MessageBar intent="warning" style={{ marginTop: '12px' }}>
    <MessageBarBody>
        ⚠️ Please keep this window open until upload completes
    </MessageBarBody>
</MessageBar>
```

---

## Shared Description Field Pattern

```typescript
import { Field, Textarea } from '@fluentui/react-components';

const [description, setDescription] = React.useState('');

<Field
    label="Description (applies to all documents)"
    hint="This description will be added to all created documents"
>
    <Textarea
        value={description}
        onChange={(e, data) => setDescription(data.value)}
        rows={3}
        disabled={isUploading}
        placeholder="Enter a description..."
    />
</Field>
```

**Key Points:**
- Fluent UI Field wrapper (label + hint)
- Textarea for multi-line
- Disabled during upload
- Placeholder text

---

## Component Communication Pattern

### Parent to Child (Props)

```typescript
<FileUploadField
    selectedFiles={files}
    onFilesChange={setFiles}
    allowMultiple={true}
    maxFiles={10}
    disabled={isUploading}
/>
```

### Child to Parent (Callbacks)

```typescript
// In parent component
const handleFilesChange = (newFiles: File[]) => {
    setFiles(newFiles);
    // Update button state
    updateButtonState(newFiles.length > 0, newFiles.length, false);
};
```

### Progress Updates

```typescript
// Parent component receives progress from service
multiFileService.uploadFiles(request, (progress) => {
    setCurrentProgress(progress);

    if (progress.status === 'complete') {
        setCompletedFiles(prev => [...prev, progress.fileName]);
    }

    if (progress.status === 'failed') {
        setFailedFiles(prev => [...prev, progress.fileName]);
    }
});
```

---

## Layout Patterns

### Vertical Stack

```typescript
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM
    }
});
```

### Horizontal Row

```typescript
const useStyles = makeStyles({
    row: {
        display: 'flex',
        flexDirection: 'row',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS
    }
});
```

### Responsive Padding

```typescript
const useStyles = makeStyles({
    container: {
        padding: tokens.spacingVerticalL,
        maxWidth: '600px',
        margin: '0 auto'
    }
});
```

---

## Error Display Pattern

```typescript
{error && (
    <MessageBar intent="error">
        <MessageBarBody>
            <strong>Upload failed:</strong> {error}
        </MessageBarBody>
    </MessageBar>
)}
```

---

## Conditional Rendering Patterns

### Show/Hide Based on State

```typescript
{!isUploading && (
    <Button onClick={handleChooseFiles}>Choose Files</Button>
)}

{isUploading && (
    <UploadProgress files={files} ... />
)}
```

### Show When Files Selected

```typescript
{selectedFiles.length > 0 && (
    <div className={styles.fileList}>
        {/* File list */}
    </div>
)}
```

### Show Only for Multiple Files

```typescript
{selectedFiles.length > 1 && (
    <div className={styles.summary}>
        {selectedFiles.length} files selected
    </div>
)}
```

---

## Testing Checklist

**FileUploadField:**
- [ ] File picker opens on button click
- [ ] Multi-select works (Ctrl+Click)
- [ ] Selected files display in list
- [ ] Remove button works
- [ ] File size formatted correctly
- [ ] Summary shows correct count/size
- [ ] Disabled state works

**UploadProgress:**
- [ ] Progress bar updates (0-100%)
- [ ] File-by-file status correct
- [ ] Icons display (✓ ↻ ⏳ ✗)
- [ ] Active file highlighted
- [ ] Percentage calculates correctly

---

**Reference:** Copy these patterns into your React components. Adapt as needed for your specific use case.
