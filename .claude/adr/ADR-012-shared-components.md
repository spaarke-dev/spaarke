# ADR-012: Shared Component Library (Concise)

> **Status**: Accepted (Revised 2026-03-10)
> **Domain**: PCF/Frontend Architecture
> **Last Updated**: 2026-03-10

---

## Decision

Maintain a shared TypeScript/React component library at `src/client/shared/Spaarke.UI.Components/` for reuse across **both PCF controls and React Code Pages**. The library must be React 18-compatible (consumed by Code Pages), and tested against React 16/17 for PCF compatibility.

**Rationale**: Prevents code duplication, ensures consistent UX, and centralizes maintenance. The wizard, side panel, filter panel, and grid primitives are used by multiple surfaces — they belong in the shared library, not recreated per-dialog.

---

## Constraints

### ✅ MUST

- **MUST** use Fluent UI v9 components exclusively
- **MUST** import shared components via `@spaarke/ui-components`
- **MUST** use semantic tokens for theming (no hard-coded colors)
- **MUST** support dark mode and high-contrast
- **MUST** match model-driven app interaction patterns
- **MUST** export TypeScript types alongside components
- **MUST** achieve 90%+ test coverage on shared components
- **MUST** author components to be React 18-compatible (used in Code Pages)
- **MUST** verify React 16/17 compatibility for components consumed by PCF
- **MUST** use callback-based props (zero service dependencies in shared components)

### ❌ MUST NOT

- **MUST NOT** mix Fluent UI versions (v9 only)
- **MUST NOT** reference PCF-specific APIs (`ComponentFramework.*`) in shared components
- **MUST NOT** hard-code Dataverse entity names or schemas
- **MUST NOT** use custom CSS (Fluent tokens only)
- **MUST NOT** use React 18-only APIs (`useTransition`, `useDeferredValue`) in components intended for PCF
- **MUST NOT** export components without tests
- **MUST NOT** import services directly — accept behavior via callback props

---

## Component Inventory (v2.0.0)

### Components (16 groups)

| Component | Description | PCF Safe? |
|-----------|-------------|-----------|
| SprkButton | Button with tooltip | Yes |
| DatasetGrid (Grid/Card/List/Virtualized) | Multi-view dataset | Yes |
| ViewSelector | View mode switcher | Yes |
| CommandToolbar | Action bar | Yes |
| PageChrome | Page header (OOB parity) | Yes |
| ChoiceDialog | Simple choice dialog | Yes |
| EventDueDateCard | Event date display | Yes |
| SidePaneShell | Slide-in side panel | Yes |
| WizardShell / Stepper / SuccessScreen | Multi-step wizard | Yes |
| DiffCompareView | AI diff viewer | Yes |
| LookupField | Search-as-you-type lookup | Yes |
| SendEmailDialog | Email composition | Yes |
| AiSummaryPopover | AI summary with lazy fetch | Yes (deep import) |
| FindSimilarDialog | Iframe dialog shell | Yes (deep import) |
| RichTextEditor | Lexical WYSIWYG | **No** (needs jsx-runtime) |
| SprkChat | SSE streaming chat | Code Pages only |

### Hooks (10)

`useDatasetMode`, `useHeadlessMode`, `useVirtualization`, `useKeyboardShortcuts`, `useEntityTypeConfig`, `useDirtyFields`, `useOptimisticSave`, `useWriteMode`, `useSseStream`, `useAiSummary`

### Services (8)

`CommandRegistry`, `CommandExecutor`, `FieldMappingService`, `EventTypeService`, `FetchXmlService`, `ViewService`, `ConfigurationService`, `SprkChatBridge`

### Theme

`spaarkeBrand` (BrandVariants #2173d7), `spaarkeLight`, `spaarkeDark`

---

## PCF Import Pattern (Critical)

PCF controls **must use deep imports** to avoid pulling in Lexical/RichTextEditor which requires `react/jsx-runtime` (unavailable in React 16):

```typescript
// ✅ PCF — deep import
import { FindSimilarDialog } from "@spaarke/ui-components/dist/components/FindSimilarDialog";
import { AiSummaryPopover } from "@spaarke/ui-components/dist/components/AiSummaryPopover";

// ❌ PCF — barrel import pulls in ALL components
import { FindSimilarDialog } from "@spaarke/ui-components";

// ✅ Code Pages — barrel is safe (React 18 has jsx-runtime)
import { FindSimilarDialog, WizardShell } from "@spaarke/ui-components";
```

---

## When to Add to Shared Library

| Add to Shared Library | Keep in Module |
|----------------------|----------------|
| Used by 2+ modules/surfaces | Module-specific logic |
| Core Spaarke UX pattern (wizard, side panel) | Experimental/POC |
| Clear, callback-based API with props | Tight coupling to services/context |
| Layout primitive | One-off dialog with unique flow |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-006](ADR-006-pcf-over-webresources.md) | Shared library consumed by both PCF and Code Pages |
| [ADR-011](ADR-011-dataset-pcf.md) | DataGrid, CommandBar used by Dataset PCF |
| [ADR-021](ADR-021-fluent-design-system.md) | All components use Fluent v9 tokens |
| [ADR-022](ADR-022-pcf-platform-libraries.md) | PCF uses React 16 (platform); Code Pages use React 18 (bundled) |

---

## Source Documentation

- **Full ADR**: [docs/adr/ADR-012-shared-component-library.md](../../docs/adr/ADR-012-shared-component-library.md)
- **Developer Guide**: [docs/guides/SHARED-UI-COMPONENTS-GUIDE.md](../../docs/guides/SHARED-UI-COMPONENTS-GUIDE.md)

---

**Lines**: ~120
