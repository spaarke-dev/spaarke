# Task 6: Field Mapping & SharePoint Links

**Estimated Time**: 0.5 day
**Status**: Pending
**Prerequisites**: Tasks 1-5 complete ✅

---

## AI Coding Prompt

> Enhance the Universal Dataset Grid to render SharePoint URLs as clickable links and verify all metadata fields are properly auto-populated after file operations. Add custom column rendering for the sprk_fileurl field that displays as a clickable link opening SharePoint in a new tab, and validate that all upload/replace operations correctly populate SharePoint metadata.

---

## Objective

Ensure data integrity and user experience by:
1. Make SharePoint URLs clickable in grid
2. Verify auto-population of all metadata fields
3. Test field mapping completeness
4. Validate data synchronization

---

## Context & Knowledge

### What You're Building
A custom column renderer for the SharePoint URL field that:
- Detects `sprk_fileurl` column
- Renders as clickable `<a>` tag
- Opens in new tab with `target="_blank"`
- Prevents row selection when clicking link

### Why This Matters
- **User Experience**: One-click access to files in SharePoint
- **Data Integrity**: Verify upload/replace populate all fields correctly
- **Debugging**: Validate field mappings work as designed

### Existing Components
- **DatasetGrid**: `components/DatasetGrid.tsx` - Has column rendering logic
- **Upload/Replace Services**: From Tasks 2 & 5 - Should populate metadata

### Field Mappings (Reference)
See [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md#field-mappings-dataverse--sdap-api) for complete table.

**Key Auto-Populated Fields**:
- `sprk_fileurl` ← `webUrl` (from SPE response)
- `sprk_spitemid` ← `sharepointIds.listItemId` (from SPE response)
- `sprk_filesize` ← `file.size` (from File object)
- `sprk_mimetype` ← `file.type` (from File object)

---

## Implementation Steps

### Step 1: Update DatasetGrid Column Rendering

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/DatasetGrid.tsx`

**Requirements**:
- Find existing `columns` useMemo that creates column definitions
- Add special rendering logic for `sprk_fileurl` column
- Use Fluent UI brand color for link (`tokens.colorBrandForeground1`)
- Prevent row selection when clicking link (`e.stopPropagation()`)
- Keep default rendering for all other columns

**Implementation**:
```typescript
import { tokens } from '@fluentui/react-components';

// Inside DatasetGridComponent

const columns = React.useMemo<TableColumnDefinition<GridRow>[]>(() => {
    if (!dataset.columns || dataset.columns.length === 0) {
        return [];
    }

    return dataset.columns.map((column: ComponentFramework.PropertyHelper.DataSetApi.Column) => {
        // Special renderer for SharePoint URL column
        if (column.name === 'sprk_fileurl') {
            return createTableColumn<GridRow>({
                columnId: column.name as TableColumnId,
                compare: (a: GridRow, b: GridRow) => {
                    const aVal = a[column.name]?.toString() || '';
                    const bVal = b[column.name]?.toString() || '';
                    return aVal.localeCompare(bVal);
                },
                renderHeaderCell: () => column.displayName,
                renderCell: (item: GridRow) => {
                    const url = item[column.name]?.toString();

                    if (!url) {
                        return <TableCellLayout>-</TableCellLayout>;
                    }

                    return (
                        <TableCellLayout>
                            <a
                                href={url}
                                target="_blank"
                                rel="noopener noreferrer"
                                style={{
                                    color: tokens.colorBrandForeground1,
                                    textDecoration: 'none'
                                }}
                                onClick={(e) => e.stopPropagation()} // Prevent row selection
                            >
                                Open in SharePoint
                            </a>
                        </TableCellLayout>
                    );
                }
            });
        }

        // Default renderer for all other columns (existing logic)
        return createTableColumn<GridRow>({
            columnId: column.name as TableColumnId,
            compare: (a: GridRow, b: GridRow) => {
                const aVal = a[column.name]?.toString() || '';
                const bVal = b[column.name]?.toString() || '';
                return aVal.localeCompare(bVal);
            },
            renderHeaderCell: () => column.displayName,
            renderCell: (item: GridRow) => {
                return (
                    <TableCellLayout>
                        {item[column.name]?.toString() || ''}
                    </TableCellLayout>
                );
            }
        });
    });
}, [dataset.columns]);
```

**Alternative Link Text Options**:
```typescript
// Option 1: Show full URL
<a href={url} ...>{url}</a>

// Option 2: Show "Open in SharePoint" (recommended for cleaner UI)
<a href={url} ...>Open in SharePoint</a>

// Option 3: Show icon only
import { Link24Regular } from '@fluentui/react-icons';
<a href={url} ...><Link24Regular /></a>

// Option 4: Show shortened URL
<a href={url} ...>{url.length > 50 ? url.substring(0, 47) + '...' : url}</a>
```

**Import Statements** (if not already present):
```typescript
import { tokens } from '@fluentui/react-components';
```

---

### Step 2: Verify Auto-Population Logic

**Files to Review**:
- `services/FileUploadService.ts` (from Task 2)
- `services/FileUploadService.ts` - `replaceFile()` method (from Task 5)

**Verification Checklist**:

#### In `uploadFile()` Method:
```typescript
// Step 2: Create document - verify these fields set
const createDocRequest: CreateDocumentRequest = {
    displayName: file.name,           // ✅ From File object
    matterId,                          // ✅ From parameter
    filePath: file.name,               // ✅ From File object
    fileSize: file.size,               // ✅ From File object (auto-populated)
    mimeType: file.type || 'application/octet-stream' // ✅ From File object (auto-populated)
};

// Step 3: Update with SharePoint metadata - verify these fields set
await this.apiClient.updateDocument(documentResponse.id, {
    webUrl: uploadResponse.webUrl,                          // ✅ From SPE (auto-populated)
    sharepointItemId: uploadResponse.sharepointIds.listItemId // ✅ From SPE (auto-populated)
});
```

#### In `replaceFile()` Method:
```typescript
// Step 3: Update document - verify ALL metadata fields updated
await this.apiClient.updateDocument(documentId, {
    displayName: newFile.name,        // ✅ New filename
    filePath: newFile.name,            // ✅ New file path
    fileSize: newFile.size,            // ✅ New file size (auto-populated)
    mimeType: newFile.type || 'application/octet-stream', // ✅ New MIME type (auto-populated)
    webUrl: uploadResponse.webUrl,     // ✅ New SharePoint URL (auto-populated)
    sharepointItemId: uploadResponse.sharepointIds.listItemId // ✅ New item ID (auto-populated)
});
```

**If any fields missing**: Add them to the appropriate `updateDocument()` call.

---

### Step 3: Add Field Formatting (Optional Enhancement)

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/DatasetGrid.tsx`

**Requirements**: Add custom formatting for file size field

**File Size Formatter** (optional):
```typescript
// Add special renderer for file size column
if (column.name === 'sprk_filesize') {
    return createTableColumn<GridRow>({
        columnId: column.name as TableColumnId,
        compare: (a: GridRow, b: GridRow) => {
            const aVal = Number(a[column.name]) || 0;
            const bVal = Number(b[column.name]) || 0;
            return aVal - bVal;
        },
        renderHeaderCell: () => column.displayName,
        renderCell: (item: GridRow) => {
            const bytes = Number(item[column.name]);
            if (!bytes) return <TableCellLayout>-</TableCellLayout>;

            // Format as KB, MB, GB
            const formatted = formatFileSize(bytes);
            return <TableCellLayout>{formatted}</TableCellLayout>;
        }
    });
}

// Helper function
function formatFileSize(bytes: number): string {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round(bytes / Math.pow(k, i) * 100) / 100 + ' ' + sizes[i];
}
```

**Note**: This is optional. Defer if time-constrained.

---

## Validation Criteria

Before marking this task complete, verify:

- [ ] `sprk_fileurl` column renders as clickable link
- [ ] Links open SharePoint in new tab
- [ ] Clicking link does NOT select row
- [ ] Link text appropriate (URL or "Open in SharePoint")
- [ ] All uploaded files have `sprk_fileurl` populated
- [ ] All uploaded files have `sprk_spitemid` populated
- [ ] All uploaded files have `sprk_filesize` populated correctly
- [ ] All uploaded files have `sprk_mimetype` populated correctly
- [ ] Replaced files update ALL metadata fields
- [ ] No TypeScript compilation errors
- [ ] Grid still sortable by all columns

---

## Testing Instructions

### Build and Deploy
```bash
npm run build
npx tsc --noEmit
```

### Manual Testing - Clickable Links
1. Deploy control to environment
2. Open grid with document records that have files uploaded
3. Verify `sprk_fileurl` column shows clickable links
4. Click a SharePoint link
5. Verify:
   - Link opens in new tab
   - SharePoint page loads (file accessible)
   - Row NOT selected in grid
6. Close SharePoint tab
7. Try clicking different rows to verify row selection still works

### Manual Testing - Field Population (Upload)
1. Upload a new file (use Task 2 functionality)
2. After grid refreshes, inspect the new record
3. Verify these fields populated:
   - `sprk_name` = filename
   - `sprk_filepath` = filename
   - `sprk_filesize` = actual file size in bytes
   - `sprk_mimetype` = correct MIME type (e.g., "application/pdf")
   - `sprk_fileurl` = SharePoint URL (starts with "https://")
   - `sprk_spitemid` = SharePoint item ID (numeric or GUID)

### Manual Testing - Field Population (Replace)
1. Replace an existing file (use Task 5 functionality)
2. After grid refreshes, inspect the updated record
3. Verify ALL fields updated to new file's metadata
4. Verify old filename/size/URL replaced with new values

### Cross-Browser Testing
- [ ] Edge (primary)
- [ ] Chrome
- [ ] Firefox (if supported)

---

## Expected Outcomes

After completing this task:

✅ **SharePoint URLs clickable** in grid
✅ **Links open in new tab** with proper security (noopener noreferrer)
✅ **All metadata fields auto-populated** correctly
✅ **Data integrity verified** across upload/replace operations
✅ **Professional UX** with branded link colors

---

## Code Reference

### Full Implementation Example

See [SPRINT-7-OVERVIEW.md](SPRINT-7-OVERVIEW.md#61-update-datasetgrid-column-rendering) lines 1290-1357 for complete code.

**Key sections**:
- Column rendering logic: Lines 1296-1357
- SharePoint URL renderer: Lines 1303-1336
- Default column renderer: Lines 1340-1355

---

## Troubleshooting

### Issue: Link not clickable
**Solution**: Verify `<a>` tag has `href` attribute. Check CSS not overriding pointer events.

### Issue: Link selects row instead of opening
**Solution**: Ensure `onClick={(e) => e.stopPropagation()}` present on `<a>` tag.

### Issue: Links open in same tab
**Solution**: Verify `target="_blank"` attribute on `<a>` tag.

### Issue: Security warning about external links
**Solution**: Add `rel="noopener noreferrer"` to `<a>` tag.

### Issue: Field not populated after upload
**Solution**:
1. Check API response includes field (inspect network tab)
2. Verify `updateDocument()` call includes field
3. Check field name matches Dataverse schema exactly

---

## Advanced: Custom Link Rendering (Optional)

For more sophisticated link rendering:

```typescript
// Show icon + text
import { Link24Regular } from '@fluentui/react-icons';

<TableCellLayout>
    <a href={url} target="_blank" rel="noopener noreferrer"
       style={{
           color: tokens.colorBrandForeground1,
           textDecoration: 'none',
           display: 'flex',
           alignItems: 'center',
           gap: '4px'
       }}
       onClick={(e) => e.stopPropagation()}>
        <Link24Regular />
        <span>Open</span>
    </a>
</TableCellLayout>
```

---

## Data Validation Script (Optional)

To validate all records have correct metadata:

```typescript
// Add to CommandBar for debugging
const validateMetadata = () => {
    const records = Object.values(dataset.records);
    const invalid = records.filter(record => {
        const hasUrl = record.getValue('sprk_fileurl');
        const hasItemId = record.getValue('sprk_spitemid');
        const hasSize = record.getValue('sprk_filesize');
        return !hasUrl || !hasItemId || !hasSize;
    });

    if (invalid.length > 0) {
        logger.warn('Metadata Validation', `${invalid.length} records missing metadata`);
        console.table(invalid.map(r => ({
            id: r.getValue('sprk_documentid'),
            name: r.getFormattedValue('sprk_name'),
            hasUrl: !!r.getValue('sprk_fileurl'),
            hasItemId: !!r.getValue('sprk_spitemid'),
            hasSize: !!r.getValue('sprk_filesize')
        })));
    } else {
        logger.info('Metadata Validation', 'All records have complete metadata');
    }
};
```

---

## Next Steps

After Task 6 completion:
- **Task 7**: Testing & Deployment (final testing, bundle size validation, production deployment)

---

## Master Resource

For additional context, see:
- [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md#field-mappings-dataverse--sdap-api) - Field mappings
- [TASK-2-FILE-UPLOAD.md](TASK-2-FILE-UPLOAD.md) - Upload logic reference
- [TASK-5-FILE-REPLACE.md](TASK-5-FILE-REPLACE.md) - Replace logic reference

---

**Task Owner**: AI-Directed Coding Session
**Estimated Completion**: 0.5 day
**Status**: Ready to Begin (after Tasks 1-5)
