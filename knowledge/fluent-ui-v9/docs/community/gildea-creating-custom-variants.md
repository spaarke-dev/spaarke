---
source: https://dev.to/paulgildea/creating-custom-variants-with-fluent-ui-react-v9-26a1
fetched: 2026-05-26
author: Paul Gildea
summary: Three patterns for building component variants — pure style overrides, wrapper component, wrapper-with-consumer-overrides (mergeClasses). Recommends wrapper-with-overrides.
loadWhen: designing a Spaarke.UI.Components variant of an upstream Fluent component (e.g., a "LaunchButton" with fixed shape/icon/color but caller-overridable styling).
notes: WebFetch capture (concise — verify against live post for full code samples before quoting verbatim).
---

# Creating Custom Variants with Fluent UI React v9 — Paul Gildea

## What this teaches

Methods for building UI component variants in Fluent UI React that maintain consistent UX while allowing controlled customization.

## Example: a "Launch Button"

Fixed properties:

- Circular shape
- Rocket icon included
- Custom background colors for rest / hover / pressed states
- Large size

## Three implementation approaches

### Option 1 — CSS-in-JS style overrides + props

Use Griffel's `makeStyles` hook to define custom styles, then apply via `className`. Consumers must manually configure the icon, shape, and size — creating inconsistency risks.

### Option 2 — Wrapper component

Encapsulates all styling and configuration inside a React functional component, eliminating consumer customization entirely. Simple but inflexible.

### Option 3 — Wrapper with consumer overrides (recommended)

Combines the wrapper approach with Griffel's `mergeClasses` API — consumers can override styles while keeping core properties fixed. Balances consistency with flexibility: downstream developers customize colors while preserving the button's essential characteristics.

## Takeaway

The recommended approach "provides a similar developer experience as any other Fluent UI React component" — coherent defaults while respecting consumer style customization needs.
