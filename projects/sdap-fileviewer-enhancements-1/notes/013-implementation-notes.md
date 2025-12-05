# Task 013 Implementation Notes

## Date: December 4, 2025

## Implementation Summary

Removed embedded Office Online editor mode from the FileViewer PCF control. The component now operates in preview-only mode, with the action buttons area preserved for Task 014's "Open in Desktop" button.

## Files Modified

1. **`src/client/pcf/SpeFileViewer/control/FilePreview.tsx`**
   - Removed imports: `Dialog`, `DialogType`, `DialogFooter`, `PrimaryButton`
   - Removed state: `officeUrl`, `mode`, `showReadOnlyDialog`
   - Removed methods: `handleOpenEditor()`, `handleBackToPreview()`, `dismissReadOnlyDialog()`
   - Removed UI: "Open in Editor" button, "← Back to Preview" button, Read-Only Permission Dialog
   - Simplified render logic to preview-only mode
   - Kept `isOfficeFile()` method for Task 014

2. **`src/client/pcf/SpeFileViewer/control/types.ts`**
   - Removed from `FilePreviewState`: `officeUrl`, `mode`, `showReadOnlyDialog`
   - Updated JSDoc to document preview-only mode
   - Kept `OfficeUrlResponse` interface (may be used by future tasks)

## Code Removed

```typescript
// State properties removed
officeUrl: string | null;
mode: 'preview' | 'editor';
showReadOnlyDialog: boolean;

// Methods removed
handleOpenEditor()
handleBackToPreview()
dismissReadOnlyDialog()

// UI elements removed
- "Open in Editor" button
- "← Back to Preview" button
- Read-Only Permission Dialog
```

## Code Kept

```typescript
// Kept for Task 014 (Open in Desktop button)
isOfficeFile(extension?: string): boolean

// Kept as placeholder for Task 014
<div className="spe-file-viewer__actions">
    {/* Future: Open in Desktop button will be added here */}
</div>
```

## Build Size Improvement

Bundle size reduced from 3.18 MiB to 3.04 MiB (~140 KB reduction) due to removal of Fluent UI Dialog components.

## Acceptance Criteria Verification

| Criterion | Status |
|-----------|--------|
| No embedded edit mode code remains | ✅ |
| Preview mode continues to work | ✅ |
| No commented-out code left | ✅ |
| Edit button removed | ✅ |
| npm run build succeeds | ✅ |

## Design Decisions

1. **Kept `isOfficeFile()` method**: This helper is needed by Task 014 to determine if a file can be opened in a desktop Office application.

2. **Kept `OfficeUrlResponse` interface**: The types.ts interface was retained since future tasks may use similar response structures for desktop links.

3. **Kept action buttons container**: The `.spe-file-viewer__actions` div is preserved (empty) as a placeholder for Task 014's "Open in Desktop" button.

4. **Complete removal**: No code was commented out - git history preserves the removed functionality if ever needed.
