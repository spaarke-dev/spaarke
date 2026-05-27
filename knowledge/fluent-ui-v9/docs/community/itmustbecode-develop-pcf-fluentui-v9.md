---
source: https://itmustbecode.com/develop-pcf-controls-with-fluentui-react-v9/
fetched: 2026-05-26
author: David Rivard (It Must Be Code!)
published: 2022-08-26
summary: "v9 is NOT an upgrade from v8" — architectural overview. Griffel basics, Slots, theming, performance. Reference PCFs (Badge, Slider) under 90 KB.
loadWhen: orientation context OR justifying the v8 → v9 migration boundary.
notes: WebFetch capture; verify against live post before quoting verbatim.
---

# Develop PCF Controls with FluentUI React v9 — David Rivard

## Why FluentUI v9 suits PCF development

React and FluentUI v8 power PowerApps Model-Driven Forms. Using the same technology stack for PCF controls ensures better integration and styling consistency.

## Key changes from v8

**FluentUI React v9 is NOT an upgrade from v8** — it is a completely redesigned library converging `@fluentui/react` and `@fluentui/react-northstar`. Notable differences:

- 🎨 Theming
- ✨ Styling
- 🖥️ Rendering
- 🏃🏽 Performance

## Getting started

```sh
npm install @fluentui/react-components
```

Wrap components with `<FluentProvider />` and provide a theme:

```tsx
import { Button, FluentProvider, webLightTheme } from '@fluentui/react-components';

const SimpleApp = (): JSX.Element => (
  <FluentProvider theme={webLightTheme}>
    <Button appearance="primary">Hello FluentUI React v9</Button>
  </FluentProvider>
);

export default SimpleApp;
```

## Theming

A major distinction from v8 is the ability to inject themes — sets of common tokens assigned as CSS properties. Themes are provided through `<FluentProvider />`, and FluentUI v9 components natively adapt.

**Five built-in themes**:

- Web Light
- Web Dark
- Teams Light
- Teams Dark
- Teams High Contrast

Custom themes can be created or extended.

## Styling

FluentUI v9 is built on **Griffel**, Microsoft's open-source CSS-in-JS engine.

```tsx
import {
  makeStyles, tokens, Button, FluentProvider, webLightTheme,
} from "@fluentui/react-components";

const useStyles = makeStyles({
  myclassname: { backgroundColor: tokens.colorPaletteRedBackground3 },
});

const SimpleStyleApp = (): JSX.Element => {
  const classes = useStyles();
  return (
    <FluentProvider theme={webLightTheme}>
      <Button className={classes.myclassname}>Hello FluentUI React v9</Button>
    </FluentProvider>
  );
};
```

> **Stack alternative**: FluentUI v8's `Stack` has no direct v9 equivalent — use CSS-in-JS (Flexbox) per documentation.

## Rendering with Slots

A significant architectural change. Slots are customizable parts where developers inject other React elements — simpler than v8's callback functions.

```tsx
import { Tooltip, Badge, Button, Avatar, FluentProvider, webLightTheme }
  from '@fluentui/react-components';

const SimpleSlotApp = (): JSX.Element => (
  <FluentProvider theme={webLightTheme}>
    <Tooltip content='simple text injected' relationship='label'>
      <Button>Hover me (simple text)</Button>
    </Tooltip>

    <Tooltip content={<Badge>Badge injected</Badge>} relationship='label'>
      <Button>Hover me (badge)</Button>
    </Tooltip>

    <Tooltip content={<Avatar
      name="David Rivard"
      image={{ src: 'https://avatars.githubusercontent.com/u/38399134?s=400&v=4' }}
    />} relationship='label'>
      <Button>Hover me (Avatar)</Button>
    </Tooltip>
  </FluentProvider>
);
```

The Tooltip's `content` attribute accepts simple text or complex JSX elements (Badges, Avatars, …).

## Performance

In PCF projects, **proper `tsconfig.json`** configuration is critical for optimizing bundle size. The author's published Badge and Slider PCF controls remain below **90 KB** for deployable managed solutions.

## Released PCF controls (referenced)

| Control | Demonstrates |
|---|---|
| **FluentUI Badge PCF** | Configurable wrapper around v9 Badge, installs on most Dataverse field types. PCF Gallery + GitHub + Storybook. |
| **FluentUI Slider PCF** | More complex; uses Badge + Tooltip together (Tooltip follows the slider handle). PCF Gallery + GitHub + Storybook. |

## Author's take

- The **Slots system** reduces complexity from v8 control customization.
- **Theming capabilities** provide visual consistency and official Teams color support.
- v9 may become the future PowerApps runtime library.
- While v8 remains necessary for text fields and dropdowns in Model-Driven Apps, learning v9 represents "a good time investment" for PCF developers.
