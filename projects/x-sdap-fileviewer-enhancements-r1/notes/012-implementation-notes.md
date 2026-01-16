# Task 012 Implementation Notes

## Date: December 4, 2025

## Implementation Summary

Updated the FilePreview React component to track iframe loading state, keeping the spinner visible until the iframe's `onload` event fires. Added timeout fallback (10 seconds) to prevent infinite loading states.

## Files Modified

1. **`src/client/pcf/SpeFileViewer/control/types.ts`**
   - Added `isIframeLoading: boolean` property to `FilePreviewState`

2. **`src/client/pcf/SpeFileViewer/control/FilePreview.tsx`**
   - Added `IFRAME_LOAD_TIMEOUT_MS` constant (10 seconds)
   - Added `iframeLoadTimeoutId` private field for timeout tracking
   - Added `componentWillUnmount()` lifecycle method for cleanup
   - Added `clearIframeLoadTimeout()` helper method
   - Added `startIframeLoadTimeout()` method to start timeout
   - Added `handleIframeLoad()` handler - clears timeout, sets `isIframeLoading: false`
   - Added `handleIframeError()` handler - clears timeout, sets error state
   - Updated `loadPreview()` to set `isIframeLoading: true` after API returns
   - Updated `handleOpenEditor()` similarly for editor mode
   - Updated `render()` to show spinner while either `isLoading` OR `isIframeLoading` is true
   - Added `onLoad` and `onError` handlers to iframe element
   - Iframe is hidden (`visibility: hidden`) while loading

## State Flow

```
loadPreview() called
    │
    ├── isLoading: true, isIframeLoading: false
    │   └── Spinner shows: "Loading preview..."
    │
    ▼
API returns successfully
    │
    ├── isLoading: false, isIframeLoading: true
    │   └── Spinner shows: "Rendering document..."
    │   └── Iframe exists but is hidden (visibility: hidden)
    │   └── Timeout started (10s)
    │
    ▼
iframe.onload fires
    │
    ├── isLoading: false, isIframeLoading: false
    │   └── Spinner hidden
    │   └── Iframe visible
    │   └── Action buttons visible
    │
    ▼
READY
```

## Timeout Handling

```
If iframe doesn't load within 10 seconds:
    - Timeout callback fires
    - Sets error: "Document load timeout. Please try again."
    - User can click Retry to try again
```

## Design Decisions

1. **Two-Phase Loading**: Rather than a single `isLoading` flag, we now distinguish between API loading (`isLoading`) and iframe loading (`isIframeLoading`). This allows more specific loading messages.

2. **Hidden Iframe During Load**: The iframe is rendered but hidden (`visibility: hidden`) during `isIframeLoading`. This allows the `onload` event to fire. Alternative approaches like conditional rendering would prevent the event from firing.

3. **Action Buttons Hidden During Iframe Load**: The "Open in Editor" button is hidden while the iframe loads to prevent user interaction before content is ready.

4. **10-Second Timeout**: Based on task spec. SharePoint preview URLs should load within 3 seconds on a warm BFF, but we use 10 seconds to account for cold starts and slow networks.

5. **Cross-Origin Limitation**: The task notes mention that cross-origin iframes may not fire `onerror` reliably. We rely on the timeout as a fallback for error detection.

## Acceptance Criteria Verification

| Criterion | Status |
|-----------|--------|
| iframe has onload handler that transitions state to Ready | ✅ |
| iframe has onerror handler that transitions state to Error | ✅ |
| Timeout fallback exists (10 seconds) | ✅ |
| Loading overlay remains visible until onload fires | ✅ |
| State transitions correctly: Loading -> Ready on success | ✅ |
| State transitions correctly: Loading -> Error on failure/timeout | ✅ |
| npm run build succeeds | ✅ |

## Note on Task Architecture

The task spec assumed the iframe was managed in `index.ts` (PCF entry point). The actual implementation manages the iframe in `FilePreview.tsx` (React component). The state machine integration happens at the React component level rather than the PCF level.

This is appropriate because:
- PCF `index.ts` handles PCF lifecycle (MSAL auth, token acquisition)
- React `FilePreview.tsx` handles document lifecycle (API fetch, iframe rendering)

The separation of concerns is maintained while achieving the task goals.
