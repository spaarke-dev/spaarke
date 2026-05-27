# PCF + Fluent v9 Modern Theming

> **Last Reviewed**: 2026-05-26
> **Status**: Current

## When

Creating a new PCF; touching the `ControlManifest.Input.xml` `<resources>` block; deciding between bundled-Fluent vs platform-library-Fluent; debugging "control doesn't match host theme" issues.

## Read These Files

1. `knowledge/fluent-ui-v9/samples/PowerApps-Samples_FluentThemingAPIControl/FluentThemingAPIControl/components/` — Microsoft's canonical reference (4 examples, one per approach)
2. `knowledge/fluent-ui-v9/samples/PowerApps-Samples_FluentThemingAPIControl/FluentThemingAPIControl/ControlManifest.Input.xml` — the `<platform-library>` declarations
3. `src/client/pcf/UniversalDatasetGrid/control/index.ts` — Spaarke's working reference using approach #1 (platform Fluent v9)
4. Drill-down only if needed: `knowledge/fluent-ui-v9/docs/pcf-modern-theming.md`

## Constraints

- **ADR-021**: Fluent v9 only.
- **ADR-022**: Use virtual / platform-library PCFs for new work (React 16.14 + Fluent 9.46.2 baseline from platform).
- Bundle size budget: production solution < 100 KB for PCF (per `bff-extensions.md` analog for client surfaces — verify per surface).

## Decision Table — pick ONE approach per control

| Your component uses | Approach | What to do |
|---|---|---|
| Fluent UI v9 controls | **1. Auto-theme via platform libraries (PREFERRED)** | Declare `<platform-library>` for React + Fluent in manifest. Don't mount your own `FluentProvider` — modern theme applies automatically. |
| Fluent UI v8 controls (legacy) | 2. `createV8Theme` bridge | `import { createV8Theme } from '@fluentui/react-migration-v8-v9'`. Mount `ThemeProvider theme={createV8Theme(brand, theme)}` using `context.fluentDesignLanguage`. |
| Non-Fluent / raw HTML | 3. Direct token consumption | Read tokens from `context.fluentDesignLanguage.theme.fontSizeBase300` etc., apply via inline styles. |
| Fluent v9 BUT need a different theme (e.g., Spaarke brand) | 4. Custom `FluentProvider` | Mount your own `<FluentProvider theme={customTheme}>`. Modern theming is NOT auto-applied — you opt out. |

## Manifest Pattern (approach 1)

```xml
<resources>
  <code path="index.ts" order="1"/>
  <!-- Dependency on React controls & platform libraries -->
  <platform-library name="React"  version="16.14.0" />
  <platform-library name="Fluent" version="9.46.2" />
</resources>
```

Control type MUST be `virtual`: `<control namespace="..." constructor="..." control-type="virtual" ...>`.

## Key Rules

- ✅ Default to approach 1 for ALL new Spaarke PCFs. Document the exception in PR description if not.
- ✅ `pac pcf init --framework react` (PAC CLI ≥ 1.37.4) generates approach 1 automatically.
- ❌ Don't mix approaches in one control. Pick one per control.
- ❌ Don't use approach 4 just because you want custom styling — first try Griffel `makeStyles` overrides on individual components (see [`../ui/fluent-v9-component-authoring.md`](../ui/fluent-v9-component-authoring.md)).
- ⚠️ Power Pages still doesn't support virtual PCFs (per Birkelbach 2024-12). For Power Pages, fall back to non-virtual + bundled Fluent.
- Disable modern theming for one subtree: wrap in `<IdPrefixProvider value="custom-prefix">`.

## See Also

- [`fluent-v9-canvas-vs-mda-disabled.md`](./fluent-v9-canvas-vs-mda-disabled.md) — disabled-state handling diverges Canvas vs MDA
- [`../ui/fluent-v9-portal-gotcha.md`](../ui/fluent-v9-portal-gotcha.md) — portal components still need re-wrap even under approach 1
- [`theme-management.md`](./theme-management.md) — existing Spaarke pattern, dark-mode wiring
