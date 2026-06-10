# Embedded Workspace-Widget Sizing — Boundary, Chain, and Box-Sizing

> **Last Reviewed**: 2026-06-09 (created after iter-2 round 11 fix)
> **Status**: Current
> **Severity**: High — getting this wrong causes ~120-150px column overshoot + permanent horizontal scrollbars

## When

ANY workspace widget that renders content whose intrinsic width can exceed
its container — DataGrid, wide tables, side-by-side cards, image galleries,
horizontal scrollers — embedded inside SpaarkeAi, LegalWorkspace, or any
future workspace shell.

## Read These Files (in order)

1. [`../../../docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md`](../../../docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md) §2 + §9.1
2. [`../../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) §7.1
3. [`../../../src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DataverseEntityViewWidget.tsx`](../../../src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DataverseEntityViewWidget.tsx) — canonical reference impl
4. [`../../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx`](../../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx) — see `columnSizingOptions` useMemo

## Four-Step Contract (every widget host MUST satisfy)

1. **Host Code Page's `index.html` MUST have `*, *::before, *::after { box-sizing: border-box }`.** Without it, every grid cell renders `+24px` wider than its declared width (12+12 padding adds to content-box). Audit before adding a widget:
   ```bash
   grep -l "box-sizing" src/solutions/<YourHost>/index.html
   ```

2. **Widget's root container MUST set `min-width: 0` and `width: 100%`.** Default `min-width: auto` resolves to `max-content` and lets descendant content inflate every ancestor.

3. **Widget MUST measure its own outer width via `ResizeObserver` and apply it as an explicit pixel `width` + `maxWidth` on a wrapper.** This creates the explicit containing block that the standalone Code Page gets for free from body-level `overflow: hidden`.

4. **If your widget mounts `<DataGrid>`, the rest is framework-internal** — already handled by the DataGrid component: Fluent `min-width: fit-content` override, 2-pass column math, per-cell padding reserve, full `min-width: 0` chain.

## Constraints

- **No `width: 100%` without `min-width: 0`.** Flex items still refuse to shrink below intrinsic content width.
- **No CSS class without `!important`** to override Fluent v9 inline styles. Inline beats class in specificity unless `!important` is on the class.
- **No assumption about pane width.** Operator can resize panes; widget must constrain regardless.

## Failure Modes Catalog

| Symptom | Cause | Fix |
|---|---|---|
| Every column rendered = declared + 24px | Host index.html missing box-sizing reset | Add the §2 reset block |
| Section card width ≠ workspace row track width | min-width:0 chain broken in some ancestor | Walk the chain via the §9.1.6 diagnostic; find the inflated parent |
| Table scrollWidth > container clientWidth, rendered ≈ declared widths | DataGrid column math regression | File a bug; do not work around at widget level |
| Widget renders fine in standalone Code Page but overflows when embedded | Missing the §7.1.1.3 ResizeObserver pixel-cap wrapper | Copy DataverseEntityViewWidget's pattern verbatim |

## Diagnostic Script

Switch DevTools Console to the Code Page iframe, then paste from
[`DATAGRID-CODE-PAGE-HOST-CONTRACT.md` §9.1.6](../../../docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md#916-diagnostic-script).

## See Also

- [`fluent-v9-host-visual-fit.md`](./fluent-v9-host-visual-fit.md) — theme + visual fit per surface (orthogonal concern)
- [`fluent-v9-component-authoring.md`](./fluent-v9-component-authoring.md) — Fluent v9 component-level rules
- iter-2 rounds 6-11 commit chain in `feature/ai-spaarke-ai-workspace-UI-r1` — the 11-round saga that surfaced this knowledge
