# MDA Dialog Chrome Dark Mode Limitation

> **Created**: December 7, 2025
> **Status**: Platform Limitation (Waiting on Microsoft)
> **Related**: MDA Dark Mode Theme Toggle Project

---

## Summary

When a Custom Page is opened as a dialog via `Xrm.Navigation.navigateTo`, the MDA platform wraps it in a dialog chrome (header with title and close button, footer). This chrome does **NOT** respect the dark mode URL flag (`themeOption=darkmode`) and remains white (#FFFFFF) regardless of the app's theme setting.

## Technical Details

### What We Observed

1. **Custom Page content supports dark mode correctly** - The PCF control (UniversalDocumentUpload v3.0.9) and Custom Page screen Fill property correctly detect and apply dark mode styling.

2. **Dialog chrome remains white** - The header element (`defaultDialogChromeHeader`) has a hardcoded white background (#FFFFFF) that doesn't change with dark mode.

3. **Other MDA dialogs work correctly** - Native MDA dialogs (Quick Create forms, Lookups) DO respect dark mode. Only Custom Page dialogs have this issue.

### DevTools Inspection

```html
<div id="defaultDialogChromeHeader-7"
     role="presentation"
     class="pa-g pa-be pa-rh pa-ri pa-rj pa-w pa-rk pa-ir flexbox">
  <h1 id="defaultDialogChromeTitle-7"
      class="pa-on pa-fn pa-rm pa-rn pa-k pa-ej pa-ei"
      aria-label="File Upload">File Upload</h1>
  <button aria-label="Close" data-id="dialogCloseIconButton">...</button>
</div>
```

The `background: #FFFFFF` is applied via CSS class, not inline style.

### Current Dialog Configuration

```javascript
// From sprk_subgrid_commands.js
const navigationOptions = {
    target: 2,      // Dialog
    position: 2,    // Right side pane (Quick Create style)
    width: { value: 640, unit: 'px' }
};

Xrm.Navigation.navigateTo(pageInput, navigationOptions);
```

### Why It Happens

Custom Pages are essentially **embedded Canvas Apps** rendered inside an MDA dialog frame. The dialog chrome is rendered by the MDA platform **outside** the Custom Page iframe. Microsoft has not yet extended dark mode support to Custom Page dialogs.

From Microsoft documentation:
> "Not yet, but we are actively working on bringing the new design system support for these areas."

## What Doesn't Work

### 1. Setting Empty Title
Setting `title: ""` in `navigationOptions` only removes the title text; the white header bar remains.

### 2. Custom Page Theme Variables
The Custom Page `OnStart` formula can set `varBackgroundColor` for the screen Fill, but this only affects content **inside** the Custom Page iframe, not the parent dialog chrome.

```yaml
# Custom Page App.fx.yaml - This only themes the content, not the chrome
OnStart: |
  Set(varIsDarkMode, Or("themeOption%3Ddarkmode" in Param("flags"), ...));
  Set(varBackgroundColor, If(varIsDarkMode, RGBA(32, 31, 31, 1), RGBA(255, 255, 255, 1)));
```

### 3. navigateTo Options
The `Xrm.Navigation.navigateTo` API has no theme-related parameters. Available options are only:
- `target` (1=inline, 2=dialog)
- `position` (1=center, 2=side)
- `width` / `height`
- `title`

## Potential Workarounds (Unsupported)

### Option A: DOM Manipulation from JavaScript

Add code to `sprk_subgrid_commands.js` after opening the dialog:

```javascript
// After navigateTo, attempt to style the parent dialog chrome
Xrm.Navigation.navigateTo(pageInput, navigationOptions);

// Detect dark mode and style chrome
setTimeout(() => {
    const isDarkMode = window.location.href.includes('themeOption');
    if (isDarkMode) {
        const headers = document.querySelectorAll('[id*="defaultDialogChromeHeader"]');
        headers.forEach(h => {
            h.style.backgroundColor = '#201F1F';  // Dark mode bg
            h.style.borderColor = '#201F1F';
            h.style.color = '#FFFFFF';
        });
    }
}, 200);
```

**Caveats:**
- Unsupported by Microsoft
- May break with platform updates
- Timing-dependent (dialog may not be rendered yet)
- Element IDs/classes may change

### Option B: CSS Injection from PCF

The PCF control could inject CSS targeting parent elements:

```typescript
// In PCF init() or updateView()
try {
    const parentDoc = window.parent?.document;
    if (parentDoc) {
        const style = parentDoc.createElement('style');
        style.textContent = `
            [id*="defaultDialogChromeHeader"] {
                background-color: #201F1F !important;
                border-color: #201F1F !important;
            }
            [id*="defaultDialogChromeTitle"] {
                color: #FFFFFF !important;
            }
        `;
        parentDoc.head.appendChild(style);
    }
} catch (e) {
    // Cross-origin restrictions may block this
}
```

**Caveats:**
- Unsupported by Microsoft
- Cross-origin security may block access
- May affect other dialogs unintentionally

## Recommendation

**Accept the limitation for now.** The Custom Page content is properly themed; only the dialog chrome is white. Microsoft is actively working on dark mode support for Custom Pages.

### Revisit When

- Microsoft announces Custom Page dark mode support
- A supported API parameter is added to `navigateTo`
- Platform updates show dialog chrome respecting `themeOption` flag

## Related Resources

- [Microsoft Learn - navigateTo Reference](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto)
- [Microsoft Learn - Custom Page Known Issues](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/model-app-page-issues)
- [Power Platform Community - Remove Custom Page Title and Header](https://community.powerplatform.com/forums/thread/details/?threadid=c3add205-405f-4a94-aa9e-9768e6a1b880)
- [PnP Blog - Preparing for Dark Mode Icons](https://pnp.github.io/blog/post/preparing-for-dark-mode-model-driven-app-icons/)

---

## Changelog

| Date | Change |
|------|--------|
| 2025-12-07 | Initial documentation of limitation |
