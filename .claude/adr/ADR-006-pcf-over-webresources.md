# ADR-006: Anti-Legacy-JS — PCF for Form Controls, React Code Pages for Dialogs (Concise)

> **Status**: Accepted (Revised 2026-02-23)
> **Domain**: PCF/Frontend Architecture
> **Last Updated**: 2026-02-23

---

## Decision

ADR-006 is fundamentally an **anti-legacy-JavaScript rule**, not an anti-React rule. It prevents untestable "random JS" sprinkled across Dataverse forms, poor lifecycle management, and UI logic becoming a shadow runtime inside Dataverse.

**Two-tier architecture:**

| UI Surface | Technology | Location |
|-----------|------------|----------|
| Field-bound form controls | **PCF** (React 16/17 platform library) | `src/client/pcf/` |
| Standalone dialog / custom page | **React Code Page** (React 18, bundled) | `src/client/code-pages/` |
| Dataset list views (form-embedded) | Dataset PCF | `src/client/pcf/UniversalDatasetGrid/` |
| Shared UI components | React library (React 18-compatible) | `src/client/shared/Spaarke.UI.Components/` |
| Office add-ins | React 18 | `src/client/office-addins/` |
| Ribbon/command bar | Thin JS (invocation only) | Webresource JS |

---

## What Is a React Code Page?

A **React Code Page** is an HTML web resource that bundles its own React 18 + Fluent v9. It is opened as a dialog or page via Xrm.Navigation, not embedded in a form.

**When to use React Code Page (not PCF):**
- No Dataverse form binding needed (document ID passed via URL parameter)
- Complex standalone UI: multi-step wizards, graph visualizations, rich panels
- Benefits from React 18 concurrent features (smooth animations, Suspense)
- Opening via `navigateTo` as a dialog — no custom page wrapper needed

**Code Page entry point pattern:**
```typescript
// src/client/code-pages/DocumentRelationshipViewer/index.tsx
import { createRoot } from "react-dom/client";  // React 18
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { App } from "./App";

// Parameters passed via Xrm.Navigation data string
const params = new URLSearchParams(window.location.search);
const documentId = params.get("documentId") ?? "";

const root = createRoot(document.getElementById("root")!);
root.render(
    <FluentProvider theme={webLightTheme}>
        <App documentId={documentId} />
    </FluentProvider>
);
```

**Opening a Code Page as a dialog:**
```typescript
Xrm.Navigation.navigateTo(
    {
        pageType: "webresource",
        webresourceName: "sprk_documentrelationshipviewer",
        data: `documentId=${documentId}`,
    },
    { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" } }
);
```

---

## Constraints

### ✅ MUST

- **MUST** use PCF for field-bound form controls (bound properties, `updateView()` lifecycle)
- **MUST** use React Code Page for standalone dialogs and custom pages
- **MUST** place PCF controls in `src/client/pcf/`
- **MUST** place Code Pages in `src/client/code-pages/`
- **MUST** keep ribbon/command bar scripts minimal (invocation only)

### ❌ MUST NOT

- **MUST NOT** create legacy JavaScript webresources (no-framework JS, jQuery, ad hoc scripts)
- **MUST NOT** add business logic to ribbon scripts
- **MUST NOT** make remote calls from ribbon scripts (call BFF instead)
- **MUST NOT** use a PCF + custom page wrapper when a Code Page achieves the same result more simply

---

## Why Not PCF for Dialogs?

PCF controls cannot be opened directly as dialogs — they require a container (entity form or custom page). Using a custom page as a PCF wrapper adds:
- Power Apps Studio dependency for parameter wiring (`Param()` formula)
- React 16/17 ceiling (no concurrent features, no React 18)
- No actual benefit: the dialog doesn't use form binding or `updateView()` lifecycle

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-011](ADR-011-dataset-pcf.md) | Dataset PCF (form-embedded) vs Code Page list views |
| [ADR-012](ADR-012-shared-components.md) | Shared component library consumed by both PCF and Code Pages |
| [ADR-021](ADR-021-fluent-design-system.md) | Fluent v9 for all surfaces; React version differs by surface |
| [ADR-022](ADR-022-pcf-platform-libraries.md) | PCF platform libraries (field-bound controls only) |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-006-prefer-pcf-over-webresources.md](../../docs/adr/ADR-006-prefer-pcf-over-webresources.md)

---

**Lines**: ~100
