# ADR-006: UI Surface Architecture — Code Pages, PCF, and Web Resources (Concise)

> **Status**: Accepted (Revised 2026-03-19; amended 2026-10-08: form-event scripts)
> **Domain**: Frontend Architecture
> **Last Updated**: 2026-10-08

---

## Decision

All Spaarke frontend UI is built using **three surface types**, each chosen based on the hosting context — not as a blanket preference. Code Pages are the **default** for new UI; PCF is used only when Dataverse form binding is required.

| UI Surface | Technology | React | Location | When to Use |
|-----------|------------|-------|----------|-------------|
| **Code Page** (dialog, wizard, full page, side pane) | Standalone HTML web resource (Vite + React 19) | 19 (bundled) | `src/solutions/{Name}/` | **Default for all new UI** — standalone dialogs, wizards, pages, panels |
| **PCF control** (form-embedded) | PCF (TypeScript/React) | 16/17 (platform) | `src/client/pcf/` | Only when Dataverse form binding is needed (bound properties, `updateView()` lifecycle) |
| **Ribbon/command script** | Thin JS (invocation only) | N/A | Webresource JS | Invoke `navigateTo` to open Code Pages; no business logic |
| **Form-event script** | Thin JS (form events only) | N/A | Webresource JS | OnLoad / OnSave / OnChange work that only a form script can do: form or field notifications, read-only/required/visible state, calling the BFF through `Spaarke.BffAuth`. No UI of its own, no access decision (2026-10-08 amendment) |
| **Shared components** | React library | peer `>=16.14.0` | `src/client/shared/Spaarke.UI.Components/` | Consumed by all surfaces |

### Legacy Anti-Pattern Rule (Still Active)

**No new legacy JavaScript web resources.** This means no jQuery, no framework-free JS with business logic, no ad hoc scripts. This rule is the origin of ADR-006 and remains in effect.

**Amendment 2026-10-08 — thin form-event scripts.** A framework-free script on form events (OnLoad / OnSave / OnChange / data OnLoad) is allowed ONLY for what no PCF or Code Page can do: form/field notifications and per-record field state (read-only, required, visible). It calls only platform form APIs and the BFF through `Spaarke.BffAuth`, renders no UI of its own, makes no access decision (the server decides; the script displays or locks), fails safe, is namespaced, idempotent and jest-tested, and is registered on forms by a checked-in operator script. Full limits: `docs/adr/ADR-006-prefer-pcf-over-webresources.md` → Exceptions.

---

## Code Page as Default Surface

A **Code Page** is a standalone React 19 app deployed as a Dataverse HTML web resource. It bundles its own React, Fluent v9, and shared library components.

**Use a Code Page when:**
- Building any new dialog, wizard, panel, or full-page UI
- The UI needs to be reusable from multiple contexts (workspace, entity forms, command bars, Power Pages SPA)
- You want React 19 features (concurrent rendering, Suspense, `useId`)
- The component should be independently deployable

**Code Page entry pattern:**
```typescript
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { detectTheme, parseDataParams } from "@spaarke/ui-components/utils";

const params = parseDataParams();
const theme = detectTheme();
createRoot(document.getElementById("root")!).render(
    <FluentProvider theme={theme}>
        <App {...params} />
    </FluentProvider>
);
```

**Opening a Code Page as a dialog:**
```typescript
Xrm.Navigation.navigateTo(
    { pageType: "webresource", webresourceName: "sprk_creatematterwizard", data: encodedParams },
    { target: 2, width: { value: 70, unit: "%" }, height: { value: 70, unit: "%" } }
);
```

---

## PCF: Only When Form Binding Is Required

Use a **PCF control** only when the UI must:
- Read/write Dataverse bound properties on a form
- Participate in the `updateView()` lifecycle
- Be embedded directly on an entity form as a field or subgrid replacement

**PCF does NOT get React 19** — the platform injects React 16/17. If you need React 19, use a Code Page instead.

```typescript
// PCF entry — React 16 API only
public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    return React.createElement(FluentProvider, { theme },
        React.createElement(MyComponent, { context })
    );
}
```

---

## Constraints

### MUST

- **MUST** use Code Page for all new standalone dialogs, wizards, and full-page UI
- **MUST** use PCF only for form-embedded controls requiring bound properties
- **MUST** keep ribbon/command bar scripts minimal (invocation only — call `navigateTo`, nothing else)
- **MUST** keep form-event scripts within the amendment's limits (platform form APIs + BFF via `Spaarke.BffAuth`; no own UI; no access decision; tested; registered by an operator script)
- **MUST** use `@spaarke/ui-components` shared library for reusable components

### MUST NOT

- **MUST NOT** create legacy JavaScript web resources (no-framework JS, jQuery, ad hoc scripts) — except thin form-event scripts within the 2026-10-08 amendment
- **MUST NOT** add business logic to ribbon scripts
- **MUST NOT** use a PCF + custom page wrapper when a Code Page achieves the same result
- **MUST NOT** embed complex dialog UI inline in a PCF when it should be a standalone Code Page (extract to Code Page for reusability)

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-012](ADR-012-shared-components.md) | Shared component library consumed by both PCF and Code Pages |
| [ADR-021](ADR-021-fluent-design-system.md) | Fluent v9 for all surfaces; React version differs by surface |
| [ADR-022](ADR-022-pcf-platform-libraries.md) | PCF platform libraries (field-bound controls only) |
| [ADR-026](ADR-026-full-page-custom-page-standard.md) | Build tooling standard for Code Pages (Vite + singlefile) |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-006-prefer-pcf-over-webresources.md](../../docs/adr/ADR-006-prefer-pcf-over-webresources.md)

---

**Lines**: ~110
