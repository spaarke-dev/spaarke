# ADR-011: List & Grid UI — Dataset PCF vs React Code Page (Concise)

> **Status**: Accepted (Revised 2026-02-23)
> **Domain**: PCF/Frontend Architecture
> **Last Updated**: 2026-02-23

---

## Decision

Use **Dataset PCF** for list/grid views that are **embedded on Dataverse entity forms** (require dataset binding). Use **React Code Pages** for standalone list/browse pages opened as dialogs or full pages (no form binding needed). Do not use native Power Platform subgrids for complex document management scenarios.

**Rationale**: Native subgrids have limited customization, poor bulk UX, and no support for custom actions. Dataset PCF and Code Pages both provide virtual scrolling, bulk operations, and consistent Spaarke UX. The choice between them depends on whether Dataverse dataset binding is required.

---

## Constraints

### ✅ MUST

- **MUST** use Dataset PCF when the list is embedded on a Dataverse entity form with a dataset parameter
- **MUST** use React Code Page when the list/view is opened as a standalone dialog or page
- **MUST** implement/extend `src/client/pcf/UniversalDatasetGrid/` for Dataset PCF controls
- **MUST** reuse shared components from `@spaarke/ui-components`
- **MUST** use Fluent UI v9 exclusively
- **MUST** achieve 80%+ test coverage on PCF controls

### ❌ MUST NOT

- **MUST NOT** add new native subgrids without tech lead approval
- **MUST NOT** create legacy bespoke JS webresources for list UX
- **MUST NOT** duplicate UI primitives (use shared library)
- **MUST NOT** wrap a standalone grid/list in a custom page + PCF when a Code Page is simpler

---

## Decision Table

| Scenario | Technology | Reason |
|----------|------------|--------|
| Related documents on entity form | ✅ Dataset PCF | Needs dataset binding |
| Bulk operations on form subgrid | ✅ Dataset PCF | Needs dataset binding |
| Custom grid actions on form | ✅ Dataset PCF | Needs dataset binding |
| Custom column visualizations on form | ✅ Dataset PCF | Needs dataset binding |
| Standalone document browse dialog | ✅ React Code Page | No form binding needed |
| Standalone list/search page | ✅ React Code Page | No form binding needed |
| Simple read-only reference (<20 records) | Native subgrid OK | No customization needed |
| Admin/configuration lists | Native subgrid OK | No customization needed |

---

## Dataset PCF Structure (Form-Embedded)

```typescript
// index.ts — Dataset PCF entry point (React 16/17 platform library)
import { DataGrid } from "@spaarke/ui-components";
import { FluentProvider } from "@fluentui/react-components";

export class UniversalDataset implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    public updateView(context: ComponentFramework.Context<IInputs>): void {
        ReactDOM.render(
            <FluentProvider theme={spaarkeLight}>
                <DataGrid items={items} columns={columns} />
            </FluentProvider>,
            this.container
        );
    }
}
```

## React Code Page List (Standalone)

```typescript
// src/client/code-pages/DocumentBrowser/index.tsx (React 18)
import { createRoot } from "react-dom/client";
const params = new URLSearchParams(window.location.search);
const scopeId = params.get("scopeId") ?? "";

createRoot(document.getElementById("root")!).render(
    <FluentProvider theme={webLightTheme}>
        <DocumentBrowserApp scopeId={scopeId} />
    </FluentProvider>
);
```

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-006](ADR-006-pcf-over-webresources.md) | Two-tier: PCF for form-bound, Code Page for standalone |
| [ADR-012](ADR-012-shared-components.md) | Shared component library used by both |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-011-dataset-pcf-over-subgrids.md](../../docs/adr/ADR-011-dataset-pcf-over-subgrids.md)

---

**Lines**: ~90
