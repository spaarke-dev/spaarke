# Fluent v9 Portal-Component Theming Gotcha

> **Last Reviewed**: 2026-05-26
> **Status**: Current

## When

Using ANY of these Fluent v9 components: `Popover`, `Tooltip`, `Toast`, `Dialog`, `Menu`, `Combobox` dropdown, `Drawer`. **All render through React `Portal` and escape the FluentProvider's DOM subtree.**

## Symptom

The component appears but is **unthemed** — wrong colors, wrong font, hard-coded defaults instead of the customer-tenant theme. Especially visible in MDA dark mode (popover background stays white).

## Read These Files

1. `knowledge/fluent-ui-v9/samples/fluentui_react-v9/Provider/FluentProviderApplyStylesToPortals.stories.tsx` — Microsoft's canonical example
2. Drill-down: `knowledge/fluent-ui-v9/docs/community/birkelbach-style-fluent-ui-9-pcfs.md` §"React portal theme provider"

## Constraints

- **ADR-021**: Dark mode required. An unthemed portal IS a dark-mode regression.

## Fix — pick ONE

### Option A — Re-wrap the portal surface (preferred, explicit)

```tsx
<FluentProvider theme={theme}>           {/* root */}
  <Popover>
    <PopoverTrigger disableButtonEnhancement>
      <Button>Open</Button>
    </PopoverTrigger>
    <PopoverSurface>
      <FluentProvider theme={theme}>     {/* ← MUST re-wrap inside the surface */}
        <MyPopoverContent />
      </FluentProvider>
    </PopoverSurface>
  </Popover>
</FluentProvider>
```

### Option B — `applyStylesToPortals` on the outer provider (less explicit)

```tsx
<FluentProvider theme={theme} applyStylesToPortals={true}>   {/* default IS true, but be explicit */}
  {/* all portal-rendered children inherit theme automatically */}
</FluentProvider>
```

Note: Option B's default is `true`, but it does NOT propagate the theme to portals mounted into a different `document` (e.g. iframe, MCP App widget host). For iframe/host-bridge scenarios, Option A is required.

## Key Rules

- ✅ Code review checklist item: any new `Popover`/`Tooltip`/`Toast`/`Dialog`/`Menu` MUST have a paired `FluentProvider` re-wrap OR explicit `applyStylesToPortals` on the root.
- ❌ Don't rely on the default — be explicit. The MDA dark-mode regression that costs an hour to debug is almost always this.
- ❌ Don't pass a DIFFERENT theme to the inner provider unless you mean to — pass the same `theme` variable.

## See Also

- [`fluent-v9-theming.md`](./fluent-v9-theming.md) — where `theme` comes from per surface
