# Dialog Patterns

> **Domain**: PCF / Dialog Close & Navigation / React Code Pages
> **Last Validated**: 2026-02-23
> **Source ADRs**: ADR-006, ADR-021, ADR-022

---

## Surface Decision First

Before building a dialog, choose the right surface (see ADR-006):

| Dialog Type | Technology | Why |
|-------------|------------|-----|
| Opens from a PCF button, no form binding needed | **React Code Page** | Simpler param passing, React 18, no custom page wrapper |
| Embedded panel inside a form (needs `updateView()`) | **PCF** | Requires Dataverse form lifecycle |
| Multi-step wizard (Create Matter, Upload Document) | **React Code Page** | Complex UI benefits from React 18 + WizardDialog template |
| Graph/visualization dialog | **React Code Page** | Concurrent rendering, React 18 |

---

## Pattern 1: Opening a React Code Page Dialog (from PCF)

The standard way to open a standalone dialog. No custom page needed.

```typescript
// NavigationService.ts — inside a PCF control
async openCodePageDialog(
    webresourceName: string,
    params: Record<string, string>,
    options?: { width?: number; height?: number }
): Promise<void> {
    const xrm = (window as any).Xrm;
    if (!xrm?.Navigation?.navigateTo) return;

    // Encode params as URL query string
    const data = new URLSearchParams(params).toString();

    await xrm.Navigation.navigateTo(
        {
            pageType: "webresource",
            webresourceName,
            data,
        },
        {
            target: 2,  // dialog
            width: { value: options?.width ?? 85, unit: "%" },
            height: { value: options?.height ?? 85, unit: "%" },
        }
    );
}

// Usage:
await this.openCodePageDialog(
    "sprk_documentrelationshipviewer",
    { documentId: result.documentId }
);
```

---

## Pattern 2: React Code Page Entry Point (React 18)

```typescript
// src/client/code-pages/DocumentRelationshipViewer/index.tsx
import { createRoot } from "react-dom/client";
import { FluentProvider, webLightTheme, webDarkTheme } from "@fluentui/react-components";
import { App } from "./App";

// Read parameters passed via navigateTo `data` field
const params = new URLSearchParams(window.location.search);
const documentId = params.get("documentId") ?? "";

// Dark mode detection
const isDark = window.matchMedia("(prefers-color-scheme: dark)").matches;

createRoot(document.getElementById("root")!).render(
    <FluentProvider theme={isDark ? webDarkTheme : webLightTheme}>
        <App documentId={documentId} />
    </FluentProvider>
);
```

---

## Pattern 3: WizardDialog (Multi-Step Form)

Use the `WizardDialog` from `@spaarke/ui-components` for multi-step workflows.

```tsx
// src/client/code-pages/CreateMatterWizard/App.tsx
import { WizardDialog } from "@spaarke/ui-components";
import { DocumentAdd20Regular, FormNew20Regular, CheckmarkCircle20Regular } from "@fluentui/react-icons";

export const CreateMatterWizardApp: React.FC = () => {
    const [activeStep, setActiveStep] = useState("files");

    return (
        <WizardDialog
            title="Create New Matter"
            steps={[
                { id: "files",  label: "Add file(s)",    icon: <DocumentAdd20Regular /> },
                { id: "record", label: "Create record",  icon: <FormNew20Regular /> },
                { id: "next",   label: "Next Steps",     icon: <CheckmarkCircle20Regular /> },
            ]}
            activeStep={activeStep}
            onCancel={() => window.history.back()}
            onNext={() => setActiveStep(getNextStep(activeStep))}
            onComplete={handleComplete}
        >
            {activeStep === "files"  && <AddFilesStep />}
            {activeStep === "record" && <CreateRecordStep />}
            {activeStep === "next"   && <NextStepsStep />}
        </WizardDialog>
    );
};
```

---

## Pattern 4: SidePanel (Filter / Detail Pane)

```tsx
// Inside a Code Page or Custom Page
import { SidePanel } from "@spaarke/ui-components";

<SidePanel
    title="Date Filter: Event"
    position="end"
    width={320}
    open={isPanelOpen}
    onDismiss={() => setPanelOpen(false)}
>
    <DateRangeFilter
        fields={["eventdate", "createdon"]}
        onApply={handleFilterApply}
        onClear={handleFilterClear}
    />
</SidePanel>
```

---

## Pattern 5: PCF Dialog Close (Legacy — when PCF in custom page is unavoidable)

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

        window.close();
    } catch (err) {
        console.error("closeDialog failed", err);
    }
}
```

---

## Pattern 6: Code Page Close

A React Code Page opened as a dialog (`target: 2`) closes when the user navigates away or the page calls:

```typescript
// Close the dialog from within the Code Page
window.history.back();
// or
window.close();
```

The `navigateTo` promise in the calling PCF resolves when the dialog closes.

---

## Canonical Implementations

| File | Surface | Purpose |
|------|---------|---------|
| `src/client/pcf/AnalysisBuilder/control/index.ts` | PCF | Dialog close pattern |
| `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/NavigationService.ts` | PCF | openCodePageDialog, navigateTo webresource |
| `src/client/code-pages/DocumentRelationshipViewer/` | Code Page | React 18 dialog entry point |

---

## Key Principles

1. **Prefer React Code Pages for dialogs** — simpler, React 18, no custom page wrapper
2. **Pass parameters via URL** — `data: "key=value"` in navigateTo, read with URLSearchParams
3. **For PCF close** — try multiple methods (navigateBack, ui.close, history, postMessage)
4. **Use shared layout components** — WizardDialog, SidePanel from `@spaarke/ui-components`

---

## Related Patterns

- [Control Initialization](control-initialization.md) - PCF notifyOutputChanged usage
- [PCF Constraints](../../constraints/pcf.md) - Surface selection rules

---

**Lines**: ~145
