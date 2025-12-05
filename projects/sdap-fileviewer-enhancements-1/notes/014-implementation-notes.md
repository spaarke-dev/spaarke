# Task 014 Implementation Notes

## Date: December 4, 2025

## Implementation Summary

Added an "Edit" button with pencil icon to the FileViewer toolbar. The button is displayed for Office files (Word, Excel, PowerPoint) and will launch the document in the native desktop application when Task 015 is implemented.

## Files Modified

1. **`src/client/pcf/SpeFileViewer/control/FilePreview.tsx`**
   - Added `handleEditInDesktop()` click handler (placeholder for Task 015)
   - Added `renderEditIcon()` method that returns pencil SVG icon
   - Updated render method to show Edit button for Office files
   - Button includes accessibility attributes

2. **`src/client/pcf/SpeFileViewer/control/css/SpeFileViewer.css`**
   - Updated `.spe-file-viewer__action-button` with `display: inline-flex` and `gap: 6px`
   - Added `.spe-file-viewer__edit-btn` for edit button styling
   - Added `.spe-file-viewer__edit-icon` for icon flex behavior

## Button Implementation

```tsx
<button
    className="spe-file-viewer__action-button spe-file-viewer__action-button--primary spe-file-viewer__edit-btn"
    onClick={this.handleEditInDesktop}
    aria-label="Open in desktop application"
    title="Open in Word, Excel, or PowerPoint"
    data-testid="edit-in-desktop-btn"
>
    {this.renderEditIcon()}
    <span>Edit</span>
</button>
```

## Visibility Logic

The Edit button is shown when:
- `!isIframeLoading` - Iframe has finished loading
- `documentInfo` exists - Document metadata is available
- `isOfficeFile(documentInfo.fileExtension)` - File is a supported Office format

Supported extensions:
- Word: docx, doc, docm, dot, dotx, dotm
- Excel: xlsx, xls, xlsm, xlsb, xlt, xltx, xltm
- PowerPoint: pptx, ppt, pptm, pot, potx, potm, pps, ppsx, ppsm

## Accessibility Features

| Attribute | Value | Purpose |
|-----------|-------|---------|
| `aria-label` | "Open in desktop application" | Screen reader label |
| `title` | "Open in Word, Excel, or PowerPoint" | Tooltip on hover |
| `data-testid` | "edit-in-desktop-btn" | Automated testing |
| Focus styles | `outline: 2px solid #605e5c` | Keyboard navigation |

## Click Handler (Placeholder)

```typescript
private handleEditInDesktop = (): void => {
    // TODO: Task 015 - Call BFF /open-links endpoint and open desktop URL
    console.warn('[FilePreview] Edit in Desktop not yet implemented (Task 015)');
};
```

## Acceptance Criteria Verification

| Criterion | Status |
|-----------|--------|
| "Edit" button exists in toolbar | ✅ |
| Button has pencil/edit icon | ✅ |
| Button has aria-label | ✅ |
| Button is keyboard accessible | ✅ |
| Button has data-testid | ✅ |
| Styling matches Fluent/Office design | ✅ |
| npm run build succeeds | ✅ |

## Design Decisions

1. **Button text "Edit"**: Per task spec, using "Edit" as the button text. This is concise and familiar to users.

2. **Pencil icon**: Custom SVG icon following Fluent UI design patterns. Uses `currentColor` for color inheritance.

3. **aria-hidden on icon**: The SVG has `aria-hidden="true"` since the button has an `aria-label`.

4. **Conditional rendering**: Button only appears for Office files. Other file types (PDF, images) don't have desktop edit support.

5. **Click handler placeholder**: Handler logs to console and warns that implementation is pending. Task 015 will add the actual functionality.

## Next Step

Task 015 will implement the click handler to:
1. Call BFF `/api/documents/{id}/open-links` endpoint
2. Receive desktop URL (e.g., `ms-word:ofe|u|{encoded-url}`)
3. Open the URL using `window.location.href` to launch desktop app
