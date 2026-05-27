---
source: https://learn.microsoft.com/en-us/power-apps/developer/component-framework/sample-controls/modern-theming-api-control
fetched: 2026-05-26
upstream_commit: 715858f2085a5cfce507bd378181d13d35241a65
last_updated_upstream: 2024-12-07
sample_path: samples/PowerApps-Samples_FluentThemingAPIControl/
summary: Sample-index doc for the canonical PCF + Fluent v9 + modern theming reference (PowerApps-Samples/FluentThemingAPIControl). Maps each of 4 example components to the corresponding theming approach.
loadWhen: orienting around the FluentThemingAPIControl sample folder (already mirrored under samples/).
---

# Modern Theming API control — sample reference

This sample component shows use cases for the modern theming API capabilities to style your component based on the current theme used in your app. The imported components adhere to the default Power Apps modern theme initially, until you [enable modern controls and themes for your app](https://learn.microsoft.com/power-apps/maker/canvas-apps/controls/modern-controls/overview-modern-controls#enable-modern-controls-and-themes-for-your-app) and [apply a modern theme](https://learn.microsoft.com/power-apps/maker/canvas-apps/controls/modern-controls/modern-theming#apply-modern-theme).

## Available for

Model-driven and canvas apps.

## What the sample demonstrates

The sample component contains four examples of consuming the Power Apps modern theming API. Each maps to one of the four approaches documented in [pcf-modern-theming.md](./pcf-modern-theming.md):

| Example | Approach |
|---|---|
| Fluent v9 sample with automatic application of the current modern theme | [Fluent UI v9 controls](./pcf-modern-theming.md#fluent-ui-v9-controls) |
| Fluent v8 sample styling itself by creating its own v8 `ThemeProvider` based on v9 theme tokens passed via PCF context parameters | [Fluent UI v8 controls](./pcf-modern-theming.md#fluent-ui-v8-controls) |
| Non-Fluent sample that applies styling to its HTML elements by directly referencing v9 theme tokens passed via PCF context parameters | [Non-Fluent UI controls](./pcf-modern-theming.md#non-fluent-ui-controls) |
| Fluent v9 sample creating its own custom v9 `FluentProvider` modifying the theme passed via PCF context parameters | [Custom theme providers](./pcf-modern-theming.md#custom-theme-providers) |

## Where to find the code

- **Upstream**: [PowerApps-Samples/component-framework/FluentThemingAPIControl](https://github.com/microsoft/PowerApps-Samples/tree/master/component-framework/FluentThemingAPIControl) at commit `a6d30c10d17938fbeb85245e57a4a2cb435c71c8`.
- **Mirrored here**: [`samples/PowerApps-Samples_FluentThemingAPIControl/`](../samples/PowerApps-Samples_FluentThemingAPIControl/). The mirror includes all four `components/Fluent*` TSX files plus the `ControlManifest.Input.xml` showing the `<platform-library>` declarations.

## Related Microsoft articles

- [Style components with modern theming (preview)](https://learn.microsoft.com/power-apps/developer/component-framework/fluent-modern-theming) — mirrored as [pcf-modern-theming.md](./pcf-modern-theming.md)
- [Theming reference](https://learn.microsoft.com/power-apps/developer/component-framework/reference/theming)
- [Use modern themes in canvas apps (preview)](https://learn.microsoft.com/power-apps/maker/canvas-apps/controls/modern-controls/modern-theming)
- [Overview of modern controls and themes in canvas apps](https://learn.microsoft.com/power-apps/maker/canvas-apps/controls/modern-controls/overview-modern-controls)
- [Download sample components](https://github.com/microsoft/PowerApps-Samples/blob/master/component-framework/README.md)
- [How to use the sample components](https://learn.microsoft.com/power-apps/developer/component-framework/use-sample-components)
