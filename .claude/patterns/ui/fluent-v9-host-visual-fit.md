# Fluent v9 Host Visual Fit — Same Look, Different Mechanism Per Surface

> **Last Reviewed**: 2026-05-28
> **Status**: Current

## When

Any UI work that needs to look "native" inside the host (MDA, Canvas, Code Page, Office Add-in, Power Pages). Use ALONGSIDE [`fluent-v9-component-authoring.md`](./fluent-v9-component-authoring.md) + [`fluent-v9-theming.md`](./fluent-v9-theming.md).

## Read These Files

1. [`../../../knowledge/fluent-ui-v9/docs/host-mda-modern-look.md`](../../../knowledge/fluent-ui-v9/docs/host-mda-modern-look.md) — the visual standard MDA UI must match
2. [`../../../knowledge/fluent-ui-v9/docs/host-canvas-modern-theming.md`](../../../knowledge/fluent-ui-v9/docs/host-canvas-modern-theming.md) — Canvas Apps maker-side theming
3. [`../../../knowledge/fluent-ui-v9/docs/host-code-pages-styling.md`](../../../knowledge/fluent-ui-v9/docs/host-code-pages-styling.md) — Spaarke Code Page convention (no auto-inheritance)

## Constraints

- **ADR-021**: Fluent v9 only. Token-only colors. Dark mode required where the host supports it.
- **ADR-022**: PCF = React 16.14 (platform). Code Pages = React 18 (bundled). Cross-surface components must be React-16.14-safe.

## The Core Distinction — SAME visual, DIFFERENT mechanism per surface

| Surface | React | Theme source | Auto-inherits modern host theme? | Visual standard to match |
|---|---|---|---|---|
| **PCF (virtual + platform-library)** in MDA | 16.14 | `context.fluentDesignLanguage.tokenTheme` | **✅ YES** (auto via platform-library) | `host-mda-modern-look.md` field controls + filled-darker |
| **PCF (virtual + platform-library)** in Canvas | 16.14 | `context.fluentDesignLanguage.tokenTheme` | **✅ YES** (when maker enabled modern themes) | Canvas modern theme + filled-darker; canvas-vs-mda-disabled handling |
| **PCF (non-virtual / bundled)** | 16.x | Read `context.fluentDesignLanguage?.tokenTheme`; fallback `webLightTheme` | ⚠️ Manual — must explicitly read + apply via `FluentProvider` | Same as virtual PCF |
| **Code Page (React 18 SPA)** in MDA | 18 | Developer picks at root (`webLightTheme` default) | **❌ NO** — Code Pages don't get PCF context | `host-mda-modern-look.md` look matched manually |
| **Code Page** outside MDA (direct web URL, Power Pages embed) | 18 | Brand theme + browser-prefers-color-scheme | n/a (no host theme) | Spaarke brand standards |
| **Office Add-in (Outlook / Word)** | 18 | Office.js theme bridge → map to v9 theme | ⚠️ Manual bridge required | Office host theme |
| **External SPA** | 18+ | Spaarke brand theme (custom `createLightTheme(brandRamp)`) | n/a | Spaarke brand standards |
| **MCP App widget** | 18 | `useThemeColors` host-bridge resolution | n/a | Copilot widget guidelines (`knowledge/mcp-apps/`) |
| **Power Pages (Spaarke React SPA)** | 18 | Brand theme — **Power Pages is Bootstrap**, NOT Fluent. SPA renders Fluent v9 inside its own iframe. | n/a | Spaarke brand standards; coexist with surrounding Bootstrap chrome |

## Visual standards — MDA "new look" target

These apply universally — PCF, Code Page, anything rendering inside MDA must look like this:

- **Input controls**: `appearance="filled-darker"` (matches modern MDA field controls)
- **Field labels**: icons on the **right** of the label
- **Section containers**: streamlined; rely on whitespace + light backgrounds + drop shadows for separation
- **Floating aesthetic**: brighter backgrounds, drop shadows to "float" content
- **Dialogs**: auto-resize height based on content (Fluent v9 `Dialog` does this natively)
- **Command bar**: rounded corners, elevation, consistent spacing (Microsoft 365 style)
- **Icons**: SVG only (PNG sitemap icons are ignored)
- **Density**: match field section vertical rhythm — avoid custom padding/margin overrides that drift from `tokens.spacingVertical*`

## Decision Tree — picking the theme source

```
Is this a PCF?
├── Yes → Is virtual + platform-library?
│         ├── Yes → context.fluentDesignLanguage.tokenTheme (auto-inherits)
│         └── No (non-virtual)  → Read context.fluentDesignLanguage?.tokenTheme;
│                                 fallback webLightTheme. Wrap in FluentProvider.
└── No  → Is this a Code Page (React 18 SPA, Dataverse web resource)?
          ├── Yes → Default webLightTheme; brand override at root if applicable.
          │         MDA doesn't pass theme to Code Pages.
          └── No  → Is this an Office Add-in?
                    ├── Yes → Office.js getTheme() bridge → map to webLight/webDark
                    └── No  → External SPA / Power Pages / MCP widget — use brand theme.
```

## Key Rules

- ✅ ALL surfaces use `tokens.*` for colors, spacing, radius. NEVER raw hex.
- ✅ `appearance="filled-darker"` is the canonical look for Input/Combobox/Dropdown when rendering inside Power Apps surfaces.
- ✅ PCF surfaces that target BOTH Canvas + MDA must apply the disabled-state divergence pattern — see [`../pcf/fluent-v9-canvas-vs-mda-disabled.md`](../pcf/fluent-v9-canvas-vs-mda-disabled.md).
- ✅ Code Pages must mount `FluentProvider` at the SPA root explicitly. They will not auto-receive any host theme.
- ✅ Power Pages-hosted Spaarke React SPAs use Fluent v9 inside the iframe + don't try to match the surrounding Bootstrap host chrome.
- ❌ NEVER assume `context.fluentDesignLanguage` is defined — older MDA versions and all non-PCF surfaces don't provide it. Always fallback.
- ❌ NEVER hard-code dark mode in MDA — MDA doesn't currently support dark mode (per [`host-mda-modern-look.md`](../../../knowledge/fluent-ui-v9/docs/host-mda-modern-look.md) "Known limitations"). Adding dark UI to MDA-only PCFs is premature.
- ❌ Power Apps "custom pages" (canvas-style Power Fx pages, NOT Spaarke Code Pages) don't get the modern theme — same constraint as Spaarke Code Pages but managed by makers, not us.

## See Also

- [`fluent-v9-component-authoring.md`](./fluent-v9-component-authoring.md) — component-level conventions
- [`fluent-v9-theming.md`](./fluent-v9-theming.md) — token + brand-theme construction
- [`fluent-v9-portal-gotcha.md`](./fluent-v9-portal-gotcha.md) — applies to ALL surfaces
- [`../pcf/fluent-v9-modern-theming.md`](../pcf/fluent-v9-modern-theming.md) — the four PCF integration approaches
- [`../pcf/fluent-v9-canvas-vs-mda-disabled.md`](../pcf/fluent-v9-canvas-vs-mda-disabled.md) — disabled-state divergence
