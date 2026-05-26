---
source: https://github.com/microsoft/fluentui/blob/master/apps/public-docsite-v9/src/Concepts/Slots/Slots.mdx
upstream_commit: 0aa62de59fe5845eeba40c9028d527fd93d88f27
fetched: 2026-05-26
summary: Slot API for callers (when to use vs theme/styles/hooks) AND slot internals for component authors (Slot<T,Alt>, slot.always/optional, useX/useXStyles/renderX hooks pipeline).
loadWhen: authoring a NEW component in Spaarke.UI.Components (the hooks-API section is essential). Routine slot consumption covered by patterns/ui/fluent-v9-component-authoring.md.
---

# Customizing Components with Slots

Fluent UI React components have parts that are designed to be modified or replaced. These are called **slots**. Slots provide a more flexible approach over render callbacks used in previous versions of Fluent UI and are conceptually similar to slots in other component libraries and frameworks.

- Each slot is exposed as a top-level prop of the same name.
- Some slots have default content and others are empty by default.
- Slots may target different types of elements or components to restrict the type of content.
- You can fill a slot with a primitive value, JSX/TSX, props objects, or a render function.

## When to use slots

Use a component's slots when you want to:

- set the content of a component part
- customize the style of a component part (via `className` prop)
- customize the props passed to a component part
- subscribe to event handlers of a component part
- change the element type of a component part (via `as` prop)
- completely replace existing content of a slot

## When NOT to use slots

- **Changing visual appearance of every instance** → customize the theme. Fluent UI React components leverage theme design tokens to render consistently.
- **Slight adjustments to a specific component instance** → apply a custom style via `makeStyles` + `className` (see [styling-griffel.md](./styling-griffel.md)).
- **Significant behavior, layout, or non-slot replacement** → consider the **hooks API**. The hooks API gives you complete control to recompose a component but is more complex than using slots.

## Conditional rendering

Some components conditionally render slots. For example, `Avatar` has a `label` slot that only renders when there is no image provided. It also defines an `icon` slot that only renders when neither an image nor a name are provided.

## Children vs. slots

The primary content within a component is defined by adding children. Component children allow for building deep hierarchies using JSX/TSX. They also allow for heterogeneous types of content.

```tsx
<Accordion>
  <AccordionItem value="1">
    <AccordionHeader>Accordion Header 1</AccordionHeader>
    <AccordionPanel>
      <div>Accordion Panel 1</div>
    </AccordionPanel>
  </AccordionItem>
  {/* ... */}
</Accordion>
```

## Usage examples

### Passing a shorthand value

```tsx
<Input contentBefore="$" value="10" contentAfter=".00" />
```

```tsx
<Button icon={<img src='site-icon.png' alt='branded site icon' />} />
<Button icon={<CalendarRegular24 />} />
```

Any shorthand value provided to a slot is converted to that slot's children content. In the example above, when the `icon` slot is passed an `img` JSX element, the `img` is rendered as the `icon` slot's children:

```html
<button className="fui-Button">
  <span className="fui-Button__icon">
    <img src="site-icon.png" alt="branded site icon" />
  </span>
</button>
```

### Passing a slot properties object

```tsx
<Avatar name="Support" badge={{ status: 'available', 'aria-label': 'available' }} />
```

```tsx
const useStyles = makeStyles({
  badge: { color: tokens.colorBrandStroke1 },
});

const BusyBrandAvatar = () => {
  const styles = useStyles();
  return <Avatar name="IT probably" badge={{ status: 'busy', className: styles.badge }} />;
};
```

```tsx
// Render AccordionHeader as h1; AccordionHeader has a button slot rendered as an anchor.
<AccordionHeader as="h1" button={{ as: 'a' }}>
  Accordion Header as h1
</AccordionHeader>
```

```html
<h1 className="fui-AccordionHeader">
  <a className="fui-AccordionHeader__button">Accordion Header as h1</a>
</h1>
```

### Replacing the entire slot (escape hatch)

If you need to replace the slot's entire content, including the containing element, pass a render function as the children. This is an escape hatch — prefer the other techniques whenever possible. If you replace the entire slot, verify accessibility, layout, and styling still work properly.

```tsx
const renderBigLetterIcon = (Component, props) => <b>B</b>;

<Button icon={{ children: renderBigLetterIcon }}>Bold</Button>;
```

## For component developers

### `Slot` type

In `@fluentui/react-utilities`, `compose/types.ts` defines the types for the slots API.

```ts
type Slot<Type, AlternateAs>
```

- `Type` — the default element or component for the slot (an HTML element string like `'div'`, or a React component type).
- `AlternateAs` — other intrinsic element types the slot supports through the `as` prop. Only intrinsic element types are supported here.

| Slot | Renders |
|---|---|
| `Slot<'div'>` | A `div` is always rendered |
| `Slot<typeof Button>` | A `Button` component accepting `Button` props is rendered |
| `Slot<'span', 'div' \| 'pre'>` | A `span` is rendered by default. A caller can opt to render `div` or `pre` by using the `as` prop |

### Defining slots in component props

In the Fluent UI React composition architecture, components define their props using `ComponentProps<Slot>` and state for rendering using `ComponentState<Slot>`. For example, `Spinner` defines optional spinner and label slots:

```tsx
type SpinnerSlots = {
  root: NonNullable<Slot<'div'>>;
  spinner?: Slot<'span'>;
  label?: Slot<typeof Label>;
};
```

#### Optional vs. non-nullable slots

- `?` (trailing) — slot is optional; the component developer decides whether default content renders when undefined.
- `NonNullable<T>` — required: caller cannot set to null to suppress rendering. Example: `RadioButton.indicator` must always render.
- These are independent dimensions; a slot can be optional/required AND nullable/non-nullable in any combination.

#### The special `root` slot

Every component has a `root` slot — the top-level element the component renders. The `className` and `style` props are always passed to the root slot. For components that wrap an intrinsic element (e.g. `Input`), additional props like `value` and `placeholder` are routed to the **primary slot** (the inner `<input>`).

```ts
export type InputSlots = {
  root: NonNullable<Slot<'span'>>;
  input: NonNullable<Slot<'input'>>;
  contentBefore?: Slot<'span'>;
  contentAfter?: Slot<'span'>;
};
```

### Hooks architecture for components with slots

Fluent UI breaks rendering primarily into 3 parts:

- `use<Component>()` — takes props, produces state
- `use<Component>Styles()` — uses state to define and apply class styles
- `render<Component>()` — renders the elements

> The `_unstable` suffix means the API may have a breaking change in the future. It does **not** mean the code is unstable or unfit for production.

```ts
const useButton_unstable = (
  props: ButtonProps,
  ref: React.Ref<HTMLButtonElement | HTMLAnchorElement>,
): ButtonState
```

```ts
const useButtonStyles_unstable = (state: ButtonState)
```

### Declaring component slots in state

```ts
const useButton_unstable = (props, ref): ButtonState => ({
  root: slot.always({ ...props, ref }, { elementType: 'button' }),
  icon: slot.optional(props.icon, { elementType: 'span' }),
});
```

- `slot.always` — slot always renders; caller cannot pass `null` to opt out (`NonNullable`).
- `slot.optional` — slot can be opted out; renders only when `props.icon !== undefined`.

### Rendering with slots

```tsx
/** @jsxRuntime automatic */
/** @jsxImportSource @fluentui/react-jsx-runtime */

import { assertSlots } from '@fluentui/react-utilities';

const renderButton_unstable = (state: ButtonState) => {
  const { iconOnly, iconPosition } = state;
  assertSlots<ButtonSlots>(state);

  return (
    <state.root>
      {iconPosition !== 'after' && state.icon && <state.icon />}
      {!iconOnly && state.root.children}
      {iconPosition === 'after' && state.icon && <state.icon />}
    </state.root>
  );
};
```

`assertSlots` ensures the state has the expected slots and provides strong typings. The custom JSX pragma at the top is required for `slot.always`, `slot.optional`, and `assertSlots` to work properly.

## Summary

- Slots are a great way to replace specific parts of a component. Theming, custom styles, or the hooks API may be better choices in other cases.
- The slots API provides a powerful extensibility mechanism with a minimal surface area. While the types are complex, they guide the caller with strong type safety.
- The slots API and the hooks API work together to let components create addressable locations for props and class styles.
