---
source: https://github.com/microsoft/fluentui/blob/master/apps/public-docsite-v9/src/Concepts/QuickStart.mdx
upstream_commit: 0aa62de59fe5845eeba40c9028d527fd93d88f27
fetched: 2026-05-26
summary: Install `@fluentui/react-components`, mount `FluentProvider` at root for React 17 / 18; Next.js strict-mode caveat.
loadWhen: building a new React surface from scratch (rare in Spaarke — covered by patterns/ui/fluent-v9-component-authoring.md).
notes: Storybook URL (JS-rendered) — https://react.fluentui.dev/?path=/docs/concepts-developer-quick-start--docs
---

# Fluent UI React v9 — Quick Start

## Install

Fluent UI should be installed as a `dependency` of your app.

```sh
yarn add @fluentui/react-components
```

## Setup

Fluent UI components are styled using CSS in JS. This technique requires a style renderer which inserts CSS into DOM when needed. React context is used to provide the style renderer.

Place a `<FluentProvider />` at the root of your app and pass theme as a prop.

### React 18

```jsx
import React from 'react';
import { createRoot } from 'react-dom/client';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import App from './App';

const root = createRoot(document.getElementById('root'));

root.render(
  <FluentProvider theme={webLightTheme}>
    <App />
  </FluentProvider>,
);
```

### React 17

```jsx
import React from 'react';
import ReactDOM from 'react-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import App from './App';

ReactDOM.render(
  <FluentProvider theme={webLightTheme}>
    <App />
  </FluentProvider>,
  document.getElementById('root'),
);
```

## Usage

```jsx
import React from 'react';
import { Button } from '@fluentui/react-components';

export default () => <Button appearance="primary">Get started</Button>;
```

### Strict mode

There are some known strict mode bugs when using Fluent UI v9 in React 18. These bugs only show up in strict mode, and they will not stop the rest of your app from running. Track open issues on GitHub with labels [`Area: Strict Mode` + `React 18`](https://github.com/microsoft/fluentui/issues?q=is%3Aopen+is%3Aissue+label%3A%22Area%3A+Strict+Mode%22+label%3A%22React+18%22).

#### SSR with Next.js

To avoid strict mode hydration issues, you can disable strict mode in your Next.js app by adding the following configuration to your `next.config.js` file:

```js
module.exports = {
  reactStrictMode: false,
};
```
