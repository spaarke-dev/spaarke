# Custom Dialogs in Dataverse Web Resources

> **Category**: Web Resources / UI Patterns
> **Last Updated**: January 2026
> **ADR Reference**: ADR-023 (Choice Dialogs)

---

## The Problem: Iframe Context

Dataverse model-driven apps run web resources inside **iframes**. This creates a critical issue for custom DOM-based dialogs:

```javascript
// ❌ WRONG - This renders inside the iframe, not visible to user
document.body.appendChild(overlay);
// overlay.offsetWidth === 0 (invisible!)
```

The dialog is appended to the iframe's `document.body`, which may have limited dimensions or be hidden from view. The user sees nothing.

---

## Solution: Use `window.top.document`

To render dialogs over the full page, use the **top window's document**:

```javascript
// ✅ CORRECT - Escapes iframe context
var targetDoc = window.top ? window.top.document : document;

// Create all elements using targetDoc
var overlay = targetDoc.createElement('div');
var dialog = targetDoc.createElement('div');
// ... etc

// Append to top window's body
targetDoc.body.appendChild(overlay);

// Event listeners on top document
targetDoc.addEventListener('keydown', escHandler);

// Cleanup uses targetDoc
function cleanup() {
    targetDoc.body.removeChild(overlay);
    targetDoc.removeEventListener('keydown', escHandler);
}
```

---

## Complete Pattern: Custom Choice Dialog

```javascript
function showChoiceDialog(config) {
    return new Promise(function(resolve) {
        // CRITICAL: Escape iframe context
        var targetDoc = window.top ? window.top.document : document;

        // Create overlay with inline styles (no CSS dependencies)
        var overlay = targetDoc.createElement('div');
        overlay.style.cssText = 'position:fixed;top:0;left:0;right:0;bottom:0;' +
            'background:rgba(0,0,0,0.4);display:flex;align-items:center;' +
            'justify-content:center;z-index:10000;font-family:"Segoe UI",sans-serif;';

        // Create dialog surface
        var dialog = targetDoc.createElement('div');
        dialog.style.cssText = 'background:#fff;border-radius:8px;' +
            'box-shadow:0 25px 65px rgba(0,0,0,0.35);max-width:480px;width:90%;';

        // ... build dialog content using targetDoc.createElement()

        // Cleanup function
        function cleanup(result) {
            targetDoc.body.removeChild(overlay);
            targetDoc.removeEventListener('keydown', escHandler);
            resolve(result);
        }

        // ESC key handler
        function escHandler(e) {
            if (e.key === 'Escape') cleanup(null);
        }
        targetDoc.addEventListener('keydown', escHandler);

        // Append to DOM
        targetDoc.body.appendChild(overlay);
    });
}
```

---

## When to Use Custom Dialogs vs Xrm.Navigation

### Use Custom Dialogs When:
- Need rich UI (icons, styled buttons, custom layouts)
- ADR-023 choice dialogs with multiple options + descriptions
- Need full control over appearance (Fluent-style design)
- Complex interactions (email links, multiple actions)

### Use Xrm.Navigation When:
- Simple confirmations (`openConfirmDialog`)
- Error/alert messages (`openAlertDialog`, `openErrorDialog`)
- Standard Yes/No decisions
- Don't need custom styling

### Comparison

| Feature | Custom Dialog | Xrm.Navigation |
|---------|--------------|----------------|
| **Styling** | Full control | Limited to Dataverse theme |
| **Complexity** | Higher | Simple API |
| **Iframe handling** | Must use `window.top` | Automatic |
| **Multiple options** | Yes (n choices) | 2 buttons max |
| **Icons/descriptions** | Yes | No |
| **Accessibility** | Manual (focus, ESC) | Built-in |

### Xrm.Navigation Examples

```javascript
// Simple confirmation - handles iframe automatically
var result = await Xrm.Navigation.openConfirmDialog({
    title: "Confirm Action",
    text: "Are you sure?",
    confirmButtonLabel: "Yes",
    cancelButtonLabel: "No"
});
if (result.confirmed) { /* proceed */ }

// Error dialog
await Xrm.Navigation.openErrorDialog({
    message: "Something went wrong"
});

// Alert dialog
await Xrm.Navigation.openAlertDialog({
    title: "Notice",
    text: "Operation completed successfully"
});
```

---

## Key Implementation Notes

### 1. Always Use Inline Styles
CSS files may not load in the top window context. Use inline `style.cssText`:

```javascript
// ✅ Works across contexts
button.style.cssText = 'padding:8px 20px;background:#0078d4;color:#fff;';

// ❌ May not work - CSS class might not exist in top window
button.className = 'my-button';
```

### 2. Z-Index Considerations
Dataverse UI uses high z-index values. Use `z-index: 10000` or higher for dialogs.

### 3. Cross-Origin Restrictions
If `window.top` is blocked by cross-origin policy, catch the error and fall back:

```javascript
var targetDoc;
try {
    targetDoc = window.top ? window.top.document : document;
} catch (e) {
    // Cross-origin restriction - fall back to Xrm dialogs
    console.warn("Cannot access top window, using Xrm.Navigation");
    return Xrm.Navigation.openConfirmDialog({...});
}
```

### 4. Cleanup on Navigation
If user navigates away while dialog is open, the cleanup function won't run. For critical cleanup, consider using `beforeunload`:

```javascript
function handleUnload() {
    if (overlay.parentNode) {
        targetDoc.body.removeChild(overlay);
    }
}
window.top.addEventListener('beforeunload', handleUnload);
```

---

## Reference Implementation

See `sprk_DocumentOperations.js` function `showChoiceDialog()` for a complete working implementation that:
- Uses `window.top.document` for all DOM operations
- Implements ADR-023 choice dialog pattern
- Handles ESC key for cancel
- Focuses first option for accessibility
- Cleans up event listeners properly

---

## Related

- **ADR-023**: Choice Dialog Pattern (design specifications)
- **ADR-006**: PCF over WebResources (when to use each approach)
- [sprk_DocumentOperations.js](../../../src/client/webresources/js/sprk_DocumentOperations.js) - Reference implementation
