# Record Header Composition — Pattern Pointer

> **Last Reviewed**: 2026-07-03
> **Status**: Current (project: `record-header-and-notepad-r1`)
> **Load when**: authoring a new per-entity Record Header PCF (`ProjectHeaderPcf`, `InvoiceHeaderPcf`, `EventHeaderPcf`, etc.); modifying `MatterHeaderPcf`; consuming `@spaarke/ui-components` header primitives from any surface.

## Canonical code

- **Shared primitives**: [`src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/`](../../../src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/) · [`.../RecordHeader/`](../../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/) · [`.../hooks/`](../../../src/client/shared/Spaarke.UI.Components/src/hooks/) (`useRecordFieldValues`, `useRelatedCount`, `useRecordHeaderToolbarActions`, `toolbarLaunchDefaults`)
- **Reference PCF**: [`src/client/pcf/MatterHeader/`](../../../src/client/pcf/MatterHeader/) — v1 thin PCF (~80 LOC) composing the primitives + version footer + Popover shell owned by consumer
- **Notepad code page**: [`src/solutions/Notepad/`](../../../src/solutions/Notepad/) — standalone Vite React 18 SPA, entity-agnostic URL launch contract

## Authoring guide

Follow [`docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md`](../../../docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md) for the complete pattern: manifest anatomy, `ComponentFramework.ReactControl` class shell, view composition, ProjectHeaderPcf worked example, Notepad launch contract, and the **mandatory bundle-optimization triad** (`featureconfig.json` + `webpack.config.js` + deep-path imports from `@spaarke/ui-components/dist/components/RecordHeader` — post-Wave-8 discovery, keeps bundles at ~40 KB vs 1.6 MB without).

## See also

- Spec: [`projects/record-header-and-notepad-r1/spec.md`](../../../projects/record-header-and-notepad-r1/spec.md)
- ADRs: 006, 012, 021, 022, 024, 038 (all cited in the guide)
- Pattern: [`fluent-v9-component-authoring.md`](fluent-v9-component-authoring.md) · [`fluent-v9-react-version-boundaries.md`](fluent-v9-react-version-boundaries.md) · [`record-modal-selection.md`](record-modal-selection.md)
