# PCF Dialog Patterns

> **Domain**: PCF / Dialog Close & Navigation
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-006

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/client/pcf/AnalysisBuilder/control/index.ts` | Dialog close pattern |
| `src/client/pcf/UniversalQuickCreate/control/index.ts` | Quick Create dialog |
| `src/client/pcf/AnalysisWorkspace/control/index.ts` | Full-page dialog |

---

## Dialog Close Pattern

PCF controls can be opened in multiple contexts. Close logic must handle all:

```typescript
private closeDialog(): void {
    try {
        const xrm = (window as any).Xrm;

        // 1. Custom Page opened as dialog
        if (xrm?.Navigation?.navigateBack) {
            xrm.Navigation.navigateBack();
            return;
        }

        // 2. Form dialog
        if (xrm?.Page?.ui?.close) {
            xrm.Page.ui.close();
            return;
        }

        // 3. Browser history
        if (window.history.length > 1) {
            window.history.back();
            return;
        }

        // 4. Iframe communication
        if (window.parent !== window) {
            window.parent.postMessage({ type: "CONTROL_CLOSE" }, "*");
            return;
        }

        // 5. Last resort
        window.close();
    } catch (err) {
        console.error("closeDialog failed", err);
    }
}
```

---

## Custom Page Output Property Pattern

For Custom Pages with Timer control monitoring:

```typescript
// In PCF control
private _shouldClose: boolean = false;

public getOutputs(): IOutputs {
    return {
        shouldClose: this._shouldClose
    };
}

private closeDialog(): void {
    this._shouldClose = true;
    this._notifyOutputChanged();
    // Custom Page Timer control watches this and closes dialog
}
```

### Custom Page Setup
1. Add output property `shouldClose` to manifest
2. Add Timer control to Custom Page
3. Timer checks `shouldClose` property value
4. When true, Timer triggers `Navigate(Back)` or `Back()`

---

## Quick Create Dialog Pattern

For dialogs that create records and return:

```typescript
interface DialogResult {
    success: boolean;
    recordId?: string;
    message?: string;
}

private async handleSave(): Promise<void> {
    try {
        const newRecord = await this.createRecord();
        this._result = { success: true, recordId: newRecord.id };
        this._shouldClose = true;
        this._notifyOutputChanged();
    } catch (error) {
        this._result = { success: false, message: error.message };
        // Don't close - show error to user
    }
}

public getOutputs(): IOutputs {
    return {
        shouldClose: this._shouldClose,
        result: JSON.stringify(this._result)
    };
}
```

---

## Confirmation Before Close

```typescript
private async attemptClose(): Promise<void> {
    if (this._hasUnsavedChanges) {
        const confirmed = await this.showConfirmDialog(
            "Unsaved Changes",
            "You have unsaved changes. Are you sure you want to close?"
        );
        if (!confirmed) return;
    }

    this.closeDialog();
}

private showConfirmDialog(title: string, message: string): Promise<boolean> {
    return new Promise((resolve) => {
        const xrm = (window as any).Xrm;
        if (xrm?.Navigation?.openConfirmDialog) {
            xrm.Navigation.openConfirmDialog(
                { title, text: message },
                { height: 200, width: 400 }
            ).then((result: { confirmed: boolean }) => resolve(result.confirmed));
        } else {
            resolve(window.confirm(message));
        }
    });
}
```

---

## Opening Dialogs from PCF

```typescript
// Open Custom Page as dialog
async function openCustomPageDialog(
    pageName: string,
    parameters: Record<string, string>
): Promise<void> {
    const xrm = (window as any).Xrm;
    if (!xrm?.Navigation?.navigateTo) {
        throw new Error("Xrm.Navigation not available");
    }

    await xrm.Navigation.navigateTo(
        {
            pageType: "custom",
            name: pageName,
            entityName: parameters.entityName,
            recordId: parameters.recordId
        },
        {
            target: 2, // Dialog
            position: 1, // Center
            width: { value: 80, unit: "%" },
            height: { value: 80, unit: "%" }
        }
    );
}
```

---

## Key Principles

1. **Always try multiple close methods** - Different contexts need different APIs
2. **Wrap in try/catch** - Xrm APIs may not be available
3. **Confirm unsaved changes** - Prevent data loss
4. **Use output properties** - Custom Pages monitor these

---

## Related Patterns

- [Control Initialization](control-initialization.md) - notifyOutputChanged usage
- [PCF Constraints](../../constraints/pcf.md) - Dialog requirements

---

**Lines**: ~100
