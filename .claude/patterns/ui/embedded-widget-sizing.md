# Embedded Workspace-Widget Sizing — Boundary, Chain, and Box-Sizing

> **Last Reviewed**: 2026-06-22 (extended with HEIGHT chain after smart-todo-r4 rounds 4-12)
> **Status**: Current
> **Severity**: High — width problems cause ~120-150px column overshoot + horizontal scrollbars; height problems cause widgets to render at content height with large empty section areas below

## When

ANY workspace widget embedded inside SpaarkeAi, LegalWorkspace, or any
future workspace shell — covering both:
- **WIDTH**: content whose intrinsic width can exceed its container
  (DataGrid, wide tables, side-by-side cards, image galleries, horizontal scrollers)
- **HEIGHT**: content that should grow to fill the SectionPanel area
  (kanban boards, lists, vertical scrollers, embedded grids)

## Read These Files (in order)

For WIDTH:
1. [`../../../docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md`](../../../docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md) §2 + §9.1
2. [`../../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) §7.1
3. [`../../../src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DataverseEntityViewWidget.tsx`](../../../src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DataverseEntityViewWidget.tsx) — canonical reference impl
4. [`../../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx`](../../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx) — see `columnSizingOptions` useMemo

For HEIGHT:
1. [`../../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) §7.2 — full chain contract + diagnostic script
2. [`../../../src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx`](../../../src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx) — canonical fill-the-section pattern (`height: 100%` root + `flex: 1` scroll content)
3. [`../../../src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx`](../../../src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx) — Pattern D reference (`height: 100%` root + `flex: 1 1 auto, minHeight: 0` grid container)

## WIDTH — Four-Step Contract (every widget host MUST satisfy)

1. **Host Code Page's `index.html` MUST have `*, *::before, *::after { box-sizing: border-box }`.** Without it, every grid cell renders `+24px` wider than its declared width (12+12 padding adds to content-box). Audit before adding a widget:
   ```bash
   grep -l "box-sizing" src/solutions/<YourHost>/index.html
   ```

2. **Widget's root container MUST set `min-width: 0` and `width: 100%`.** Default `min-width: auto` resolves to `max-content` and lets descendant content inflate every ancestor.

3. **Widget MUST measure its own outer width via `ResizeObserver` and apply it as an explicit pixel `width` + `maxWidth` on a wrapper.** This creates the explicit containing block that the standalone Code Page gets for free from body-level `overflow: hidden`.

4. **If your widget mounts `<DataGrid>`, the rest is framework-internal** — already handled by the DataGrid component: Fluent `min-width: fit-content` override, 2-pass column math, per-cell padding reserve, full `min-width: 0` chain.

## HEIGHT — Three-Rule Contract (every widget author MUST satisfy)

The full height chain runs from viewport → 3-pane shell → WorkspacePane → tab content → **WorkspaceLayoutWidget.root** → LegalWorkspaceApp → WorkspaceShell.shell → **WorkspaceShell.row** → SectionPanel.card → SectionPanel.content → **YOUR WIDGET ROOT → inner wrappers → scrollable body**. Three of those links are author-controlled and break silently if any one of them is wrong.

1. **`WorkspaceLayoutWidget.root` MUST set `height: 100%`** (not just `flex: 1`). Its parent (a tab content wrapper) is `display: block`, which IGNORES flex on children. Already fixed in source — don't remove it.

2. **`WorkspaceShell.row` MUST set `flex: 1 1 0 + minHeight: 0 + alignItems: stretch`.** This makes the grid row claim the shell's vertical space AND stretch SectionPanel grid cells to fill row height. Already fixed in source — don't remove it.

3. **YOUR widget's intermediate wrappers MUST all be `display: flex` (or `grid`).** A `div` defaults to `display: block`. A child with `flex: 1 1 auto` inside a `display: block` parent has its flex IGNORED → child falls back to content height. The most common failure: a "body" or "content" wrapper with `flex: 1 1 auto, overflowY: auto` but MISSING `display: flex` — children can't claim the body's height.

```typescript
// ✅ CORRECT — every wrapper in the chain is flex
const useStyles = makeStyles({
  root: {
    display: 'flex', flexDirection: 'column',
    height: '100%',       // anchor to parent's supplied height (block-parent-safe)
    overflow: 'hidden',
  },
  body: {
    display: 'flex',      // ← CRITICAL — without this, child flex props don't work
    flexDirection: 'column',
    flex: '1 1 auto', minHeight: 0, overflowY: 'auto',
  },
  scrollableContent: {
    flex: '1 1 auto',     // claims body's height
    minHeight: 0,
  },
});

// ❌ THE TRAP — body div is implicitly display:block, children's flex is IGNORED
const broken = {
  body: { flex: '1 1 auto', minHeight: 0, overflowY: 'auto' /* MISSING display: flex */ },
};
```

In `workspaceConfig.tsx`, EVERY section MUST supply its own card height (SectionPanel.card has no `height: 100%` by default):
```typescript
{ id: "...", style: { height: "calc(100vh - 200px)", minHeight: "560px" }, renderContent: ... }
```

## Constraints

- **No `width: 100%` without `min-width: 0`.** Flex items still refuse to shrink below intrinsic content width.
- **No `flex: 1 1 0` on a child of `display: block`.** Flex is IGNORED in block parents — use `height: 100%` instead.
- **No CSS class without `!important`** to override Fluent v9 inline styles. Inline beats class in specificity unless `!important` is on the class.
- **No assumption about pane width OR height.** Operator can resize panes; widget must constrain itself regardless.
- **No `minHeight: 400px` (or any pixel floor) on the scrollable body to "guarantee" rendering.** Pixel floors mask broken chains — fix the chain instead; use `minHeight: 0` so the flex chain can supply natural height.

## Failure Modes Catalog

### Width failures

| Symptom | Cause | Fix |
|---|---|---|
| Every column rendered = declared + 24px | Host index.html missing box-sizing reset | Add the §2 reset block |
| Section card width ≠ workspace row track width | min-width:0 chain broken in some ancestor | Walk the chain via the §9.1.6 diagnostic; find the inflated parent |
| Table scrollWidth > container clientWidth, rendered ≈ declared widths | DataGrid column math regression | File a bug; do not work around at widget level |
| Widget renders fine in standalone Code Page but overflows when embedded | Missing the §7.1.1.3 ResizeObserver pixel-cap wrapper | Copy DataverseEntityViewWidget's pattern verbatim |

### Height failures

| Symptom | Cause | Fix |
|---|---|---|
| Widget renders at ~600px regardless of SectionPanel size | `WorkspaceLayoutWidget.root` missing `height: 100%` (block parent ignores its flex) | Already fixed in source. If reintroduced, restore. |
| Widget root fills correctly but section grid row stays at content height | `WorkspaceShell.row` missing `flex: 1 1 0 + alignItems: stretch` | Already fixed in source. If reintroduced, restore. |
| Card fills SectionPanel but YOUR widget's inner area caps at ~400px (your min-height floor) | A wrapper div between widget root and scrollable body is missing `display: flex` | Add `display: flex, flexDirection: column` to that wrapper |
| Other widgets (DailyBriefing, Calendar) fill correctly but yours doesn't | Your widget's intermediate wrappers don't follow Rule 3 above | Run the §7.2.4 diagnostic script to find the first `display: block` parent breaking the chain |

## Diagnostic Scripts

For WIDTH: switch DevTools Console to the Code Page iframe, paste from [`DATAGRID-CODE-PAGE-HOST-CONTRACT.md` §9.1.6](../../../docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md#916-diagnostic-script).

For HEIGHT: paste from [`BUILD-A-NEW-WORKSPACE-WIDGET.md` §7.2.4](../../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — walks UP from your widget to viewport and shows every element's display/flex/height so you can spot the chain break.

## See Also

- [`fluent-v9-host-visual-fit.md`](./fluent-v9-host-visual-fit.md) — theme + visual fit per surface (orthogonal concern)
- [`fluent-v9-component-authoring.md`](./fluent-v9-component-authoring.md) — Fluent v9 component-level rules
- iter-2 rounds 6-11 commit chain in `feature/ai-spaarke-ai-workspace-UI-r1` — 11-round saga that surfaced the WIDTH knowledge
- smart-todo-r4 UAT rounds 4-12 commit chain (June 2026) on `work/smart-todo-r4-uat4-fixes` — 9-round saga that surfaced the HEIGHT knowledge; PR #406
