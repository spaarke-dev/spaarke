# Task 015 Implementation Notes

## Date: December 4, 2025

## Implementation Summary

Integrated the FileViewer "Edit" button with the BFF `/api/documents/{id}/open-links` endpoint. When clicked, the button calls the API to get the desktop protocol URL and launches the native Office application.

## Files Modified

1. **`src/client/pcf/SpeFileViewer/control/types.ts`** (Previous session)
   - Added `OpenLinksResponse` interface
   - Added `isEditLoading` to `FilePreviewState`

2. **`src/client/pcf/SpeFileViewer/control/BffClient.ts`** (Previous session)
   - Added `getOpenLinks()` method

3. **`src/client/pcf/SpeFileViewer/control/FilePreview.tsx`**
   - Updated `handleEditInDesktop()` to async with full implementation
   - Added loading state management
   - Added error handling for null desktopUrl
   - Updated Edit button to show loading spinner when active

4. **`src/client/pcf/SpeFileViewer/control/css/SpeFileViewer.css`**
   - Added `.spe-file-viewer__action-button--loading` styles
   - Added `.spe-file-viewer__button-spinner` for inline loading spinner
   - Added reduced motion support for button spinner

## Implementation Details

### Handler Flow

```typescript
private handleEditInDesktop = async (): Promise<void> => {
    // 1. Set loading state
    this.setState({ isEditLoading: true });

    try {
        // 2. Call BFF API
        const response = await this.bffClient.getOpenLinks(
            documentId, accessToken, correlationId
        );

        // 3. Check for desktop URL availability
        if (!response.desktopUrl) {
            this.setState({
                isEditLoading: false,
                error: 'This file type cannot be opened in a desktop application.'
            });
            return;
        }

        // 4. Launch desktop application via protocol URL
        window.location.href = response.desktopUrl;

        // 5. Reset loading state after delay
        setTimeout(() => {
            this.setState({ isEditLoading: false });
        }, 1000);

    } catch (error) {
        // 6. Handle errors
        this.setState({
            isEditLoading: false,
            error: `Failed to open in desktop: ${errorMessage}`
        });
    }
};
```

### Button Loading State

```tsx
<button
    className={`...${isEditLoading ? ' spe-file-viewer__action-button--loading' : ''}`}
    onClick={this.handleEditInDesktop}
    disabled={isEditLoading}
    aria-label={isEditLoading ? 'Opening in desktop application...' : 'Open in desktop application'}
    title={isEditLoading ? 'Opening...' : 'Open in Word, Excel, or PowerPoint'}
>
    {isEditLoading ? (
        <span className="spe-file-viewer__button-spinner" aria-hidden="true"></span>
    ) : (
        this.renderEditIcon()
    )}
    <span>{isEditLoading ? 'Opening...' : 'Edit'}</span>
</button>
```

### CSS Additions

```css
/* Loading state for action button */
.spe-file-viewer__action-button--loading {
    position: relative;
    cursor: wait;
}

/* Button spinner (small inline spinner) */
.spe-file-viewer__button-spinner {
    display: inline-block;
    width: 14px;
    height: 14px;
    border: 2px solid rgba(255, 255, 255, 0.3);
    border-top: 2px solid #ffffff;
    border-radius: 50%;
    animation: spe-file-viewer-spin 0.8s linear infinite;
    flex-shrink: 0;
}
```

## Desktop Protocol URLs

The BFF returns protocol URLs in this format:
- **Word**: `ms-word:ofe|u|{encoded-webUrl}`
- **Excel**: `ms-excel:ofe|u|{encoded-webUrl}`
- **PowerPoint**: `ms-powerpoint:ofe|u|{encoded-webUrl}`

These URLs trigger the operating system's protocol handler, which launches the appropriate Office desktop application.

## Error Handling

| Scenario | User Message |
|----------|--------------|
| API call fails | "Failed to open in desktop: {error message}" |
| No desktop URL (unsupported file) | "This file type cannot be opened in a desktop application." |
| Network error | "Failed to open in desktop: Failed to get open links: {details}" |

## Acceptance Criteria Verification

| Criterion | Status |
|-----------|--------|
| Clicking Edit calls /open-links API | ✅ |
| Loading state shown during API call | ✅ |
| Desktop URL opens native Office app | ✅ |
| Null desktopUrl handled gracefully | ✅ |
| API errors displayed to user | ✅ |
| npm run build succeeds | ✅ |

## Design Decisions

1. **`window.location.href` vs `window.open()`**: Using `window.location.href` for protocol URLs is the standard approach and works better with Office protocol handlers than `window.open()`.

2. **Loading timeout (1000ms)**: The loading state is reset after 1 second if the page doesn't navigate away. This handles the case where the Office app isn't installed.

3. **Error state**: Errors from the API call are shown in the component's existing error display area, maintaining UX consistency.

4. **Button text change**: Button shows "Opening..." during loading for immediate feedback.

## Integration with BFF

The BFF endpoint `/api/documents/{id}/open-links` returns:
```json
{
    "desktopUrl": "ms-word:ofe|u|https%3A%2F%2F...",
    "webUrl": "https://tenant.sharepoint.com/...",
    "mimeType": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    "fileName": "Report.docx"
}
```

## Next Steps

- Task 016 (Add CSS for loading states) is partially complete from Tasks 011 and 015
- Phase 3 (Performance Enhancements) can begin
