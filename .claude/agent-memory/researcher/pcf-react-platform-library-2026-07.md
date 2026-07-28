---
name: pcf-react-platform-library-2026-07
description: Current (2026) PCF platform-library React/Fluent versions — max declarable React 16.14.0, runtime loads 17.0.2 in Model-driven / 16.14.0 Canvas; Fluent v9 declared <=9.46.2 loads 9.68.0. No React 18/19 platform library, no roadmap.
metadata:
  type: reference
---

# PCF platform-library React + Fluent versions (verified 2026-07-27)

Source: Microsoft Learn "React controls & platform libraries" (ms.date 2025-10-10, updated 2025-10-11).
URL: https://learn.microsoft.com/en-us/power-apps/developer/component-framework/react-controls-platform-libraries

**Supported platform libraries list (authoritative table):**

| Name | npm package | Allowed version range (declarable in manifest) | Version LOADED at runtime |
|---|---|---|---|
| React | react | 16.14.0 (max) | **17.0.2 (Model-driven), 16.14.0 (Canvas)** |
| Fluent | @fluentui/react (v8) | 8.29.0 | 8.29.0 |
| Fluent | @fluentui/react (v8) | 8.121.1 | 8.121.1 |
| Fluent | @fluentui/react-components (v9) | >=9.4.0 <=9.46.2 | **9.68.0** |

Key facts:
- **Max declarable React in a virtual PCF manifest = 16.14.0.** You cannot declare 17/18/19.
- **Runtime injects React 17.0.2 in Model-driven apps** (16.14.0 in Canvas). "App might load a higher compatible version at runtime." This is the update vs the GA-era 16.14.0-everywhere story (Birkelbach 2024-12).
- Fluent v8 and v9 cannot both be in the same manifest.
- **No React 18 or 19 platform library exists. No published roadmap/timeline** (confirmed via powerplatform-build-tools discussion #679, maintainer Jan-2026 punts — out of scope).
- Virtual controls (`control-type="virtual"`) attach to the PLATFORM React tree via `ReactControl.updateView` returning a ReactElement (no `createRoot`, no own DOM div). They run against the platform React instance — cannot bundle a different React major.
- Standard controls (`control-type="standard"`) own an HtmlDivElement and CAN bundle any React (18/19) + createRoot — the escape hatch — at the cost of bundle size + multiple React instances/roots per form. DOM-id collisions → David Rivard's `IdPrefixProvider` fix.
- Virtual PCFs still NOT supported in Power Pages.

Fluent v9 React support matrix (fluentui ReactVersionSupport.mdx):
- React 17: from @fluentui/react-components v9.0.0
- React 18: from v9.66.0
- React 19: from v9.72.2
- A single v9 version does NOT cover all Rs — need >=9.72.2 for React 19. v9 provides cross-version JSX types (JSXElement/JSXIntrinsicElement) so ONE source can compile against @types/react 17/18/19.

Consequence for "standardize shared cores on React 19": fine as a BUILD/TYPE target (Code Pages, standard controls, cross-version Fluent types), but a virtual PCF EXECUTES the shared core under platform React 17 in model-driven — so the shared core must avoid React 18/19 runtime-only APIs. Matches ADR-022 (per-import type cast bridge) and user memory feedback_shared-lib-react-version-tension.
