---
source: https://learn.microsoft.com/en-us/power-apps/maker/canvas-apps/controls/modern-controls/overview-modern-controls
fetched: 2026-05-28
upstream_commit: 997adebe81eda0f80a30b493d9ae52a687479cbe
last_updated_upstream: 2026-02-23
summary: Canvas Apps modern controls (Fluent 2 design system) — opt-in toggle, control catalog, theming pane.
loadWhen: orienting on what "modern controls" are in Canvas Apps OR confirming Spaarke PCFs must coexist with first-party modern controls in Canvas surfaces.
---

# Overview of modern controls and theming in canvas apps

Canvas apps support **modern controls and theming based on the [Microsoft Fluent 2 design system](https://fluent2.microsoft.design)**. Modern controls offer improved accessibility, performance, and usability compared to classic controls. The accompanying theming system lets you customize your app's appearance from a central location.

## Enable modern controls and themes

With your canvas app open for editing:

1. On the command bar, select **Settings** → **Updates**
2. Select the **New** tab → turn on **Modern controls and themes**

- The app refreshes with the new app authoring menu.
- Modern controls are part of existing categories.
- Two additional categories exist for legacy: **Classic** and **Classic icons**.

## Themes

To see modern themes: on the app authoring menu, select **Themes**. See [`host-canvas-modern-theming.md`](./host-canvas-modern-theming.md).

## What's next

The Power Apps team publishes updates monthly to the [Power Apps blog](https://powerapps.microsoft.com/blog/).

## Feedback

Community forum: https://go.microsoft.com/fwlink/?linkid=2229838

Per-control feedback via thumbs up/down in Power Apps Studio control properties. Same mechanism for themes in the Themes pane.

## Spaarke implications

| Concern | Action |
|---|---|
| Spaarke PCFs coexist with first-party modern controls in Canvas | Match visual language by reading `context.fluentDesignLanguage.tokenTheme` + applying `appearance="filled-darker"` on Input/Combobox. |
| Maker may not have enabled modern controls | PCFs MUST fall back to `webLightTheme` when `context.fluentDesignLanguage` is undefined. See [`pcf-modern-theming.md`](./pcf-modern-theming.md). |
| Canvas + MDA disabled-state behavior diverges | See [`../../.claude/patterns/pcf/fluent-v9-canvas-vs-mda-disabled.md`](../../../.claude/patterns/pcf/fluent-v9-canvas-vs-mda-disabled.md). |
