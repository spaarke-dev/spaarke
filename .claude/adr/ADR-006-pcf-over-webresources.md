# ADR-006: PCF Over WebResources (Concise)

> **Status**: Accepted
> **Domain**: PCF/Frontend Architecture
> **Last Updated**: 2025-12-18

---

## Decision

Use **PCF controls** for all new custom UI in model-driven apps. Do not create new legacy JavaScript webresources.

**Rationale**: PCF provides better testability, packaging, lifecycle management, and modern UI patterns compared to legacy webresources.

---

## Constraints

### ✅ MUST

- **MUST** build new interactive UI as PCF controls
- **MUST** place PCF controls in `src/client/pcf/`
- **MUST** use React for SPA surfaces (Power Pages, add-ins)
- **MUST** keep ribbon/command bar scripts minimal (invocation only)

### ❌ MUST NOT

- **MUST NOT** create new legacy JavaScript webresources
- **MUST NOT** add business logic to ribbon scripts
- **MUST NOT** make remote calls from ribbon scripts (call BFF instead)

---

## Implementation Patterns

### UI Surface Technology

| Surface | Technology | Location |
|---------|------------|----------|
| Model-driven forms | PCF (TypeScript) | `src/client/pcf/` |
| Subgrid replacement | Dataset PCF | `src/client/pcf/UniversalDatasetGrid/` |
| Quick create dialogs | PCF + FluentUI | `src/client/pcf/UniversalQuickCreate/` |
| Shared UI components | React | `src/client/shared/Spaarke.UI.Components/` |
| Office add-ins | React | `src/client/office-addins/` |

### Allowed Webresources (Exceptions)

```javascript
// ✅ ALLOWED: Thin ribbon invoker (no logic)
function openDocumentDialog(primaryControl) {
    // Invocation only - no business logic
    Xrm.Navigation.openWebResource("sprk_dialog", { data: id });
}

// ❌ NOT ALLOWED: Business logic in webresource
function processDocument(primaryControl) {
    // DON'T: Make API calls, validate, transform data here
    fetch("/api/process", { ... }); // WRONG
}
```

**See**: [PCF Control Pattern](../patterns/pcf/control-initialization.md)

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-011](ADR-011-dataset-pcf.md) | Dataset PCF over native subgrids |
| [ADR-012](ADR-012-shared-components.md) | Shared component library for PCF |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-006-prefer-pcf-over-webresources.md](../../docs/adr/ADR-006-prefer-pcf-over-webresources.md)

For detailed context including:
- Allowed webresource exceptions list
- Exception approval process

---

**Lines**: ~75
