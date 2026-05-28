---
source: https://learn.microsoft.com/en-us/power-apps/maker/canvas-apps/controls/modern-controls/modern-theming
fetched: 2026-05-28
upstream_commit: 8391e6edc1f4310e685c41192d036ac73b09ffcb
last_updated_upstream: 2026-01-21
summary: Canvas Apps modern themes — 16-slot brand ramp from seed color, HueTorsion + Vibrancy, ColorOverrides, YAML format, App.Theme Power Fx binding.
loadWhen: designing a Spaarke brand theme that must work cross-surface OR understanding how Canvas Apps generate themes from a single seed color (relevant pattern for PCFs that need to generate their own brand variant matching maker intent).
---

# Use modern themes in canvas apps

Modern themes are **preestablished style sets** based on Microsoft's Fluent design language that modify color, typography, borders, and shadows.

## Prerequisites

1. Open or create an app.
2. **Settings** → **Updates** → **New** → **Modern controls and themes** → **On**

> When modern controls + themes are enabled, classic themes are unavailable unless you toggle **Retired** → **Keep classic themes** → **On**.

## Create a theme

App authoring menu → **Themes** → **Add a theme** → **Create custom theme** OR **Paste theme** (YAML).

| Feature | Action |
|---|---|
| **Primary (seed) color** | Color picker, or hex / RGB. |
| **Lock primary color (preview)** | OFF (default): palette generated for accessibility. ON: seed color goes in middle slot, others incrementally lighter/darker — may not meet contrast requirements. |
| **Theme name** | Unique within app. |
| **Font** | Default font used by controls. |
| **Torsion** | Tint, shade, tone adjustment (N/A when Lock primary color is on). |
| **Vibrancy** | Muteness ↔ brightness (N/A when Lock primary color is on). |
| **Palette overrides** | Override individual slots in the palette. |

## YAML format

```yaml
Themes:
  Theme Name:
    Font: "'Segoe UI', 'Open Sans', sans-serif"
    BasePaletteColor: '#2e4bf0'
    HueTorsion: -27
    Vibrancy: -52
    ColorOverrides:
      Lighter30: '#bd3535'
      Darker10: '#1a2d8f'
```

| Property | Description | Required |
|---|---|---|
| `Theme Name` | Display name; unique. | Yes |
| `Font` | CSS font family. | Yes |
| `BasePaletteColor` | Seed color in hex; determines base for all 16 slots. | Yes |
| `HueTorsion` | -100 to 100; negative = cooler, positive = warmer. | Yes |
| `Vibrancy` | -100 to 100; negative = muted, positive = vibrant. | Yes |
| `ColorOverrides` | Per-slot overrides: `Lighter30`, `Lighter20`, `Lighter10`, `Base`, `Darker10`, `Darker20`, `Darker30`, `Darker40`. | No |

### Example — corporate brand theme

```yaml
Themes:
  Corporate Brand:
    Font: "'Segoe UI', 'Open Sans', sans-serif"
    BasePaletteColor: '#0078d4'
    HueTorsion: 0
    Vibrancy: 10
    ColorOverrides:
      Base: '#0078d4'
      Darker10: '#106ebe'
      Darker20: '#005a9e'
      Lighter10: '#2b88d8'
      Lighter20: '#c7e0f4'
```

## Apply a theme

App authoring menu → **Themes** → select a default theme. If classic controls exist, prompt to apply modern theme to them — visual alignment isn't exact (classic isn't Fluent v9). Sets `App.Theme`.

## Use themes with Power Fx

Reference active theme: `App.Theme`. Reference loaded theme by instance name: e.g. `RedTheme`. For theme adaptability, ALWAYS use `App.Theme`.

Each theme object exposes:

- **Name**
- **Colors** — 16-color brand ramp, addressable by name; **Primary foreground color** = default text color
- **Font**

Example — manual styling of a classic control:

```pwsh
Button.Fill = App.Theme.Colors.Primary
```

## Tips

- **Save themes externally** — copy YAML to a text file for backup / version control.
- **Share across teams** — paste YAML across apps for consistent branding.
- **Duplicate-and-modify** — start from an existing theme rather than from scratch.
- **Font availability** — ensure font families are licensed + available across deployments.
- **Minimal overrides** — let `BasePaletteColor` + `HueTorsion` + `Vibrancy` do the work; override only what must differ.
- **Test accessibility** — WCAG 2.1 AA minimum on both desktop + mobile.

## Spaarke implications

| Concern | Action |
|---|---|
| **Brand consistency cross-surface** | If Spaarke has a brand palette, the same `BasePaletteColor` should feed the Canvas YAML AND the Fluent v9 `createLightTheme(brandVariants)` factory in `Spaarke.UI.Components`. The 16-slot Canvas ramp ≠ Fluent v9 `BrandVariants` (16 slots vs. 16 brand variants) but the seed color is shared — produce both from one source of truth. |
| **`tinycolor2` brand-variant generation pattern** (per Birkelbach) | Canvas does this for makers automatically; Spaarke PCFs replicate the same generation when authoring custom themes. See [`community/birkelbach-standard-custom-theming.md`](./community/birkelbach-standard-custom-theming.md). |
| **Font choice** | Match the maker's Canvas theme `Font` value if Spaarke ships custom CSS that overrides fonts — otherwise the Spaarke control looks "off" inside a maker's themed Canvas app. Default: leave font to inherit. |
