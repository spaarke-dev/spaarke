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
| Multi-step wizard (Create Matter, Upload Document) | **React Code Page** | Complex UI benefits from React 18 + WizardShell / CreateRecordWizard |
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

## Pattern 3: WizardShell & CreateRecordWizard (Multi-Step Form)

Use `WizardShell` from `@spaarke/ui-components` for multi-step wizard layouts. For record-creation wizards that follow the standard "Add Files -> Create Record -> Next Steps" flow, use `CreateRecordWizard` which provides boilerplate around `WizardShell`.

### 3a. WizardShell (Low-Level)

`WizardShell` is the raw multi-step container with stepper, navigation, and layout. Use it directly when your wizard has a non-standard step sequence (e.g., CreateWorkAssignmentWizard).

```tsx
import { WizardShell } from "@spaarke/ui-components";
import { Briefcase20Regular, People20Regular, Calendar20Regular } from "@fluentui/react-icons";

export const WorkAssignmentWizardDialog: React.FC<Props> = ({ dataService, navigationService }) => {
    const [activeStep, setActiveStep] = useState("info");

    return (
        <WizardShell
            title="Assign Work"
            steps={[
                { id: "info",   label: "Enter info",       icon: <Briefcase20Regular /> },
                { id: "assign", label: "Assign work",      icon: <People20Regular /> },
                { id: "event",  label: "Follow-on event",  icon: <Calendar20Regular /> },
            ]}
            activeStep={activeStep}
            onCancel={() => window.close()}
        >
            {activeStep === "info"   && <EnterInfoStep dataService={dataService} />}
            {activeStep === "assign" && <AssignWorkStep dataService={dataService} />}
            {activeStep === "event"  && <CreateFollowOnEventStep dataService={dataService} />}
        </WizardShell>
    );
};
```

### 3b. CreateRecordWizard (High-Level)

`CreateRecordWizard` wraps `WizardShell` with standard record-creation boilerplate: file upload step, entity creation step, and next-steps/success screen. Use it for Matter, Project, Event, and Todo wizards.

```tsx
import { CreateRecordWizard } from "@spaarke/ui-components";

// The wizard content components (CreateMatterStep, etc.) are passed as children.
// CreateRecordWizard handles step navigation, file uploads, and success screen.
export const CreateMatterWizard: React.FC<Props> = ({ dataService, uploadService, navigationService }) => (
    <CreateRecordWizard
        title="Create New Matter"
        entityName="sprk_matter"
        dataService={dataService}
        uploadService={uploadService}
        navigationService={navigationService}
    >
        {(stepProps) => <CreateMatterStep {...stepProps} />}
    </CreateRecordWizard>
);
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

## Pattern 7: Code Page Wizard Wrapper

A Code Page wizard wrapper is a thin `main.tsx` entry point that bootstraps a shared-library wizard component. The wrapper handles platform-specific setup (React 18 `createRoot`, Fluent theme, adapter wiring) while all wizard logic lives in `@spaarke/ui-components`.

```tsx
// src/client/code-pages/CreateMatterWizard/main.tsx (thin wrapper — ~30 lines)
import { createRoot } from "react-dom/client";
import { FluentProvider, webLightTheme, webDarkTheme } from "@fluentui/react-components";
import {
    CreateMatterWizard,
    createXrmDataService,
    createXrmUploadService,
    createXrmNavigationService,
} from "@spaarke/ui-components";

const params = new URLSearchParams(window.location.search);
const isDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
const bffBaseUrl = params.get("bffBaseUrl") ?? "";

// Wire platform adapters — shared wizard never touches Xrm directly
const dataService = createXrmDataService();
const uploadService = createXrmUploadService(bffBaseUrl);
const navigationService = createXrmNavigationService();

createRoot(document.getElementById("root")!).render(
    <FluentProvider theme={isDark ? webDarkTheme : webLightTheme}>
        <CreateMatterWizard
            matterId={params.get("matterId") ?? ""}
            dataService={dataService}
            uploadService={uploadService}
            navigationService={navigationService}
        />
    </FluentProvider>
);
```

**Key rule**: The Code Page wrapper contains **zero business logic** — it reads URL params, creates adapters, and renders the shared wizard. All wizard steps, validation, and orchestration live in the shared library.

See also: [Full-Page Custom Page Template](../webresource/full-page-custom-page.md) for project structure and build tooling.

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
4. **Use shared layout components** — WizardShell, CreateRecordWizard, SidePanel from `@spaarke/ui-components`
5. **Code Page wrappers are thin** — only platform bootstrap; all wizard logic in shared library
6. **Wire adapters in the wrapper** — `createXrmDataService()`, `createXrmUploadService()`, `createXrmNavigationService()`

---

## Related Patterns

- [Control Initialization](control-initialization.md) - PCF notifyOutputChanged usage
- [PCF Constraints](../../constraints/pcf.md) - Surface selection rules

---

**Lines**: ~250
