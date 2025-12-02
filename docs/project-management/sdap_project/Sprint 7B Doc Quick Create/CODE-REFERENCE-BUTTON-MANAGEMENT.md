# Code Reference: Button Management

**Purpose:** Reference implementation for custom button injection and management in Quick Create form footer.

**Used By:** Work Item 2

---

## Overview

This code handles:
1. Hiding standard "Save and Close" button via CSS
2. Injecting custom button into form footer
3. Finding footer with multiple fallback selectors
4. Updating button state dynamically
5. MutationObserver to re-inject if footer re-renders
6. Closing form and refreshing subgrid

---

## CSS Injection Pattern

```typescript
private hideStandardButtons(): void {
    const styleId = 'spaarke-hide-quickcreate-buttons';

    if (document.getElementById(styleId)) return; // Already injected

    const style = document.createElement('style');
    style.id = styleId;
    style.textContent = `
        /* Hide standard Quick Create buttons */
        button[data-id="quickCreateSaveBtn"],
        button[data-id="quickCreateSaveAndCloseBtn"],
        button[aria-label*="Save and Close"],
        button[aria-label*="Save & Close"] {
            display: none !important;
        }
    `;

    document.head.appendChild(style);
    logger.info('ButtonManagement', 'Standard buttons hidden');
}
```

**Key Points:**
- Use unique ID to prevent duplicate injection
- Multiple selectors for resilience
- `!important` to override Power Apps styles
- Inject into `<head>` not local container

---

## Footer Finding Pattern

```typescript
private findFormFooter(): HTMLElement | null {
    // Try multiple selectors (fallbacks)
    const selectors = [
        '[data-id="quickCreateFormFooter"]',
        '[data-id="dialogFooter"]',
        '.ms-Dialog-actions',
        'div[class*="footer"]',
        'div[role="contentinfo"]'
    ];

    for (const selector of selectors) {
        const element = document.querySelector(selector);
        if (element) {
            logger.info('ButtonManagement', `Footer found: ${selector}`);
            return element as HTMLElement;
        }
    }

    // Fallback: Find Cancel button's parent
    const cancelBtn = document.querySelector('button[data-id="quickCreateCancelBtn"]');
    if (cancelBtn?.parentElement) {
        logger.info('ButtonManagement', 'Footer found via Cancel button');
        return cancelBtn.parentElement as HTMLElement;
    }

    logger.warn('ButtonManagement', 'Footer not found');
    return null;
}
```

**Key Points:**
- Try specific selectors first (most reliable)
- Fall back to generic selectors
- Final fallback: find by known element (Cancel button)
- Log which selector worked (debugging)

---

## Button Injection Pattern

```typescript
private injectCustomButtonInFooter(): void {
    const footer = this.findFormFooter();
    if (!footer) {
        logger.warn('ButtonManagement', 'Cannot inject button - footer not found');
        return;
    }

    // Create button
    this.customButton = document.createElement('button');
    this.customButton.type = 'button';
    this.customButton.className = 'ms-Button ms-Button--primary';
    this.customButton.setAttribute('data-id', 'spaarke-save-create-btn');
    this.customButton.disabled = true;

    // Style to match Power Apps
    this.customButton.style.cssText = `
        background-color: #0078d4;
        color: white;
        border: none;
        padding: 8px 16px;
        font-size: 14px;
        font-weight: 600;
        border-radius: 2px;
        cursor: pointer;
        margin-right: 8px;
        min-width: 80px;
    `;

    // Hover effect
    this.customButton.addEventListener('mouseenter', () => {
        if (!this.customButton.disabled) {
            this.customButton.style.backgroundColor = '#106ebe';
        }
    });

    this.customButton.addEventListener('mouseleave', () => {
        if (!this.customButton.disabled) {
            this.customButton.style.backgroundColor = '#0078d4';
        }
    });

    // Click handler
    this.customButton.addEventListener('click', () => {
        this.handleSaveAndCreateDocuments();
    });

    // Insert before Cancel button
    const cancelBtn = footer.querySelector('button[data-id="quickCreateCancelBtn"]');
    if (cancelBtn) {
        footer.insertBefore(this.customButton, cancelBtn);
    } else {
        footer.appendChild(this.customButton);
    }

    logger.info('ButtonManagement', 'Custom button injected');
}
```

**Key Points:**
- Use Power Apps CSS classes for consistency
- Set data-id for testing/debugging
- Insert before Cancel (standard button order)
- Bind click handler immediately

---

## Button State Management Pattern

```typescript
updateButtonState(filesSelected: boolean, fileCount: number, isUploading: boolean): void {
    if (!this.customButton) return;

    if (isUploading) {
        // Uploading state
        this.customButton.disabled = true;
        this.customButton.textContent = 'Uploading...';
        this.customButton.style.backgroundColor = '#999';
        this.customButton.style.cursor = 'not-allowed';
    } else if (filesSelected && fileCount > 0) {
        // Ready state
        this.customButton.disabled = false;
        this.customButton.textContent = fileCount > 1
            ? `Save and Create ${fileCount} Documents`
            : 'Save and Create Document';
        this.customButton.style.backgroundColor = '#0078d4';
        this.customButton.style.cursor = 'pointer';
    } else {
        // Disabled state
        this.customButton.disabled = true;
        this.customButton.textContent = 'Select Files to Continue';
        this.customButton.style.backgroundColor = '#999';
        this.customButton.style.cursor = 'not-allowed';
    }
}

updateButtonProgress(current: number, total: number): void {
    if (!this.customButton) return;

    this.customButton.textContent = `Uploading ${current} of ${total}...`;
}
```

**Key Points:**
- Three states: disabled, ready, uploading
- Dynamic text based on file count
- Visual feedback (color, cursor)
- Progress updates during upload

---

## MutationObserver Pattern

```typescript
private setupFooterWatcher(): void {
    const observer = new MutationObserver((mutations) => {
        // Check if button still in DOM
        if (this.customButton && !document.body.contains(this.customButton)) {
            logger.warn('ButtonManagement', 'Button removed, re-injecting');
            this.injectCustomButtonInFooter();
        }
    });

    // Watch entire body for changes
    observer.observe(document.body, {
        childList: true,
        subtree: true
    });

    this.footerObserver = observer;
}
```

**Key Points:**
- Watch for button removal (footer re-render)
- Re-inject automatically if removed
- Store observer reference for cleanup

---

## Close Form Pattern

```typescript
private closeQuickCreateForm(): void {
    try {
        // Method 1: Xrm.Navigation API
        if ((window as any).Xrm?.Navigation?.closeDialog) {
            (window as any).Xrm.Navigation.closeDialog();
            logger.info('ButtonManagement', 'Closed via Xrm.Navigation');
            return;
        }

        // Method 2: Parent window (if in iframe)
        if (window.parent !== window) {
            (window.parent as any).Xrm?.Navigation?.closeDialog?.();
            logger.info('ButtonManagement', 'Closed via parent window');
            return;
        }

        // Method 3: Simulate Cancel click (fallback)
        const cancelBtn = document.querySelector('button[data-id="quickCreateCancelBtn"]');
        if (cancelBtn) {
            (cancelBtn as HTMLButtonElement).click();
            logger.info('ButtonManagement', 'Closed via Cancel button');
            return;
        }

        logger.error('ButtonManagement', 'Could not close form');
    } catch (error) {
        logger.error('ButtonManagement', 'Close failed', error);
    }
}
```

**Key Points:**
- Try multiple methods (APIs change)
- Prefer official API
- Fallback to UI automation (Cancel click)
- Log which method worked

---

## Refresh Subgrid Pattern

```typescript
private refreshParentSubgrid(): void {
    try {
        // Method 1: Direct grid control
        if (window.parent && window.parent !== window) {
            const parentXrm = (window.parent as any).Xrm;

            if (parentXrm?.Page?.getControl) {
                const grid = parentXrm.Page.getControl('Documents'); // Subgrid name
                if (grid?.refresh) {
                    grid.refresh();
                    logger.info('ButtonManagement', 'Subgrid refreshed');
                    return;
                }
            }
        }

        // Method 2: Via formContext
        const formContext = (window.parent as any)?.Xrm?.Page;
        if (formContext?.data?.refresh) {
            formContext.data.refresh(false); // false = no prompt
            logger.info('ButtonManagement', 'Form data refreshed');
            return;
        }

        logger.warn('ButtonManagement', 'Could not refresh subgrid');
    } catch (error) {
        logger.error('ButtonManagement', 'Refresh failed', error);
    }
}
```

**Key Points:**
- Access parent window (Quick Create is in iframe)
- Get grid control by name ('Documents')
- Multiple fallback methods
- Graceful failure (records still created)

---

## Cleanup Pattern

```typescript
public destroy(): void {
    // Remove custom button
    if (this.customButton?.parentElement) {
        this.customButton.parentElement.removeChild(this.customButton);
        this.customButton = null;
    }

    // Remove CSS
    const style = document.getElementById('spaarke-hide-quickcreate-buttons');
    if (style) {
        style.remove();
    }

    // Stop observer
    if (this.footerObserver) {
        this.footerObserver.disconnect();
        this.footerObserver = null;
    }

    logger.info('ButtonManagement', 'Cleanup complete');
}
```

**Key Points:**
- Clean up DOM modifications
- Disconnect observers
- Null references
- Called by PCF destroy()

---

## Complete Flow

```
PCF init()
    ↓
hideStandardButtons() → Inject CSS
    ↓
injectCustomButtonInFooter() → Create & inject button
    ↓
setupFooterWatcher() → Start MutationObserver
    ↓
updateButtonState() → Set initial state (disabled)
    ↓
[User selects files]
    ↓
updateButtonState(true, 3, false) → Enable button
    ↓
[User clicks button]
    ↓
updateButtonState(false, 0, true) → Show uploading
    ↓
updateButtonProgress(1, 3) → Update progress
updateButtonProgress(2, 3)
updateButtonProgress(3, 3)
    ↓
closeQuickCreateForm() → Close dialog
    ↓
refreshParentSubgrid() → Refresh grid
    ↓
destroy() → Cleanup
```

---

## Testing Checklist

- [ ] Standard button hidden on init
- [ ] Custom button appears in footer (next to Cancel)
- [ ] Button disabled initially
- [ ] Button enabled when files selected
- [ ] Button text updates with file count
- [ ] Button shows progress during upload
- [ ] Form closes after completion
- [ ] Subgrid refreshes (new records visible)
- [ ] MutationObserver re-injects if footer changes
- [ ] Cleanup works (button removed on destroy)

---

**Reference:** This is a complete pattern library. Copy relevant sections into your implementation.
