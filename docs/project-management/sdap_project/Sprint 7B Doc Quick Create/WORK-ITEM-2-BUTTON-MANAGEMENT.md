# Work Item 2: Implement Button Management

**Sprint:** 7B - Document Quick Create
**Estimated Time:** 3 hours
**Prerequisites:** None (can be done in parallel with Work Item 1)
**Status:** Ready to Start
**Code Reference:** CODE-REFERENCE-BUTTON-MANAGEMENT.md

---

## Objective

Implement button management in the PCF control to hide standard "Save and Close" button and inject custom "Save and Create Documents" button in form footer.

---

## Context

Quick Create forms have a standard "Save and Close" button that would create only ONE record. We need to:
1. Hide this button (CSS injection)
2. Add our own button in the same location
3. Update button dynamically based on state
4. Handle form close and subgrid refresh

**Result:** User sees custom button where they expect it (form footer), with no confusion.

---

## Implementation Steps

### Step 1: Add Button Properties to PCF Class

In `UniversalQuickCreatePCF.ts`:

```typescript
export class UniversalQuickCreatePCF {
    private customSaveButton: HTMLButtonElement | null = null;
    private footerObserver: MutationObserver | null = null;

    // ... existing properties
}
```

---

### Step 2: Call Button Methods in init()

```typescript
public init(context: ComponentFramework.Context<IInputs>): void {
    // ... existing init code ...

    // Button management
    this.hideStandardButtons();
    this.injectCustomButtonInFooter();
    this.setupFooterWatcher();
}
```

---

### Step 3: Implement Core Methods

Implement these methods (see CODE-REFERENCE-BUTTON-MANAGEMENT.md for patterns):

1. **hideStandardButtons()** - CSS injection
   - Create `<style>` element with unique ID
   - Hide buttons using multiple selectors
   - Append to `<head>`

2. **findFormFooter()** - Find footer with fallbacks
   - Try 5+ selectors
   - Fall back to Cancel button's parent
   - Log which selector worked

3. **injectCustomButtonInFooter()** - Create and inject button
   - Create `<button>` element
   - Style to match Power Apps (blue primary)
   - Add click handler
   - Insert before Cancel button

4. **updateButtonState()** - Dynamic button text/status
   - Three states: disabled, ready, uploading
   - Update text based on file count
   - Update color/cursor

5. **updateButtonProgress()** - Show upload progress
   - Called during file upload
   - Updates button text: "Uploading 2 of 3..."

6. **setupFooterWatcher()** - MutationObserver
   - Watch for button removal
   - Re-inject if removed

7. **closeQuickCreateForm()** - Close dialog
   - Try Xrm.Navigation.closeDialog()
   - Try parent window
   - Fall back to Cancel click

8. **refreshParentSubgrid()** - Refresh grid
   - Get parent window
   - Find grid control by name
   - Call refresh()

---

## Button States

### Disabled (No Files)
```
Text: "Select Files to Continue"
Color: Gray (#999)
Cursor: not-allowed
Disabled: true
```

### Ready (Files Selected)
```
Text: "Save and Create 3 Documents"
Color: Blue (#0078d4)
Cursor: pointer
Disabled: false
```

### Uploading
```
Text: "Uploading 2 of 3..."
Color: Gray (#999)
Cursor: not-allowed
Disabled: true
```

---

## Integration with File Upload

### When Files Selected
```typescript
// In file picker change handler
handleFilesChange(files: File[]) {
    this.selectedFiles = files;
    this.updateButtonState(true, files.length, false);
}
```

### When Upload Starts
```typescript
async handleSaveAndCreateDocuments() {
    this.updateButtonState(false, 0, true);

    const result = await this.multiFileService.uploadFiles(
        request,
        (progress) => {
            this.updateButtonProgress(progress.current, progress.total);
        }
    );

    if (result.success) {
        this.closeQuickCreateForm();
        this.refreshParentSubgrid();
    }
}
```

---

## Footer Selectors (Priority Order)

Try these in order:
1. `[data-id="quickCreateFormFooter"]` - Specific
2. `[data-id="dialogFooter"]` - Dialog forms
3. `.ms-Dialog-actions` - Fluent UI class
4. `div[class*="footer"]` - Generic
5. `div[role="contentinfo"]` - Semantic HTML
6. Cancel button's parentElement - Fallback

**Why multiple?** Power Apps UI changes between versions.

---

## Testing Checklist

- [ ] Standard "Save and Close" button hidden on init
- [ ] Custom button appears in footer (next to Cancel)
- [ ] Button initially disabled with "Select Files to Continue"
- [ ] Button enables when files selected
- [ ] Button text updates: "Save and Create 3 Documents"
- [ ] Button shows progress: "Uploading 2 of 3..."
- [ ] Click handler fires handleSaveAndCreateDocuments()
- [ ] Form closes after successful upload
- [ ] Parent subgrid refreshes (new records visible)
- [ ] MutationObserver re-injects if footer changes
- [ ] Cleanup removes button on destroy

---

## Common Issues

### Issue: Footer not found
**Debug:** Log all tried selectors
**Fix:** Inspect DOM, add new selector pattern

### Issue: Button appears but doesn't work
**Debug:** Check click handler bound
**Fix:** Verify `this` context in handler

### Issue: Subgrid doesn't refresh
**Debug:** Check parent window access
**Fix:** Try alternative refresh methods

---

## Verification

```bash
# Button management code exists
grep "hideStandardButtons" UniversalQuickCreatePCF.ts
grep "injectCustomButtonInFooter" UniversalQuickCreatePCF.ts
grep "updateButtonState" UniversalQuickCreatePCF.ts

# Test in browser
# 1. Open Quick Create
# 2. Check: Standard button hidden? ✓
# 3. Check: Custom button in footer? ✓
# 4. Check: Button text correct? ✓
```

---

## Cleanup on destroy()

```typescript
public destroy(): void {
    // Remove button
    if (this.customSaveButton?.parentElement) {
        this.customSaveButton.parentElement.removeChild(this.customSaveButton);
    }

    // Remove CSS
    document.getElementById('spaarke-hide-quickcreate-buttons')?.remove();

    // Disconnect observer
    this.footerObserver?.disconnect();
}
```

---

**Status:** Ready for implementation
**Time:** 3 hours
**Code Reference:** CODE-REFERENCE-BUTTON-MANAGEMENT.md (complete patterns)
**Next:** Work Item 3 - Update Manifest
