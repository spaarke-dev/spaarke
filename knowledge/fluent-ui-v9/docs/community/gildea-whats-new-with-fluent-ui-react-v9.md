---
source: https://dev.to/paulgildea/whats-new-with-fluent-ui-react-v9-5h2d
fetched: 2026-05-26
author: Paul Gildea
published: 2022-08-02
summary: v9 launch overview — Griffel performance, 50% fewer DOM nodes vs v8 Persona/PersonaCoin, accessibility (axe-core, native elements), slots, theming, migration.
loadWhen: orientation only — historical launch context.
notes: WebFetch capture; verify against live post before quoting verbatim.
---

# What's new with Fluent UI React v9? — Paul Gildea

After nearly two years in development, Fluent UI React v9 reached stable version 9.0 — a collaboration between the Teams and Office team providing a stable architecture and baseline component set.

## Performance

### CSS-in-JS engine — Griffel

Fluent UI React v9 leverages **Griffel**, an open-sourced CSS-in-JS implementation offering "near zero runtime, SSR support, and optional build-time transforms to improve performance." Features:

- Zero-config startup with runtime and build-time implementations
- Optional build-time transforms
- Type-safe styles via `csstype`
- Atomic CSS for style reuse and specificity management
- Experimental CSS extraction with Webpack plugin
- Griffel DevTools extension for debugging

### Lighter-weight DOM

Component design prioritized DOM node efficiency. The v9 `Avatar` features **50% fewer DOM elements** compared to v8's `Persona` + `PersonaCoin` while delivering identical visual output.

## Accessibility

Design specifications incorporated keyboard, narrator, and high contrast considerations from inception. Each component undergoes extensive manual accessibility testing.

Key features:

- Native element usage to minimize ARIA overuse
- Built-in high contrast support
- Fixed `SplitButton` automated test errors and screen-reader bugs
- Cross-platform slider support for desktop and mobile readers
- Native `select` component wrapper
- Reduced-animation user settings support

## Customization — Slots

Internal component parts are called **slots**. Each slot is exposed as a top-level prop, enabling full control through:

- Passing content
- Passing props
- Changing slot type
- Complete slot replacement

## Design to code — Theming with design tokens

Design tokens provide semantic key-value pairs representing the design language — colors, typography, radius, spacing. Enables consistent theming across products while supporting **light / dark / high-contrast** modes.

## Migration

Migration from v0 or v8 to v9 requires effort due to substantial architectural improvements. Resources:

- Upgrading section on the documentation site
- Component mapping and renames
- Color mapping reference
- Component-specific migration guides co-located in the GitHub repository

## Resources

- GitHub: <https://github.com/microsoft/fluentui>
- Documentation: <https://react.fluentui.dev>
- Twitter: <https://twitter.com/fluentui>
