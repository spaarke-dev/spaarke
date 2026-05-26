---
source: https://github.com/microsoft/fluentui/blob/master/apps/public-docsite-v9/src/Concepts/ReactVersionSupport.mdx
upstream_commit: 0aa62de59fe5845eeba40c9028d527fd93d88f27
fetched: 2026-05-26
summary: v9 support matrix — React 17 from v9.0, React 18 from v9.66.2, React 19 from v9.72.2. React 19 cross-version JSX type rules (use JSXElement/JSXIntrinsicElement).
loadWhen: drill-down on React 19 cross-version TS types OR bumping React in a Spaarke surface. Quick boundary reference covered by patterns/ui/fluent-v9-react-version-boundaries.md.
notes: |
  Critical for Spaarke PCFs. PCF Canvas apps ship React 16.14; this page documents v9 support boundaries.
  Storybook URL (JS-rendered) — https://react.fluentui.dev/?path=/docs/concepts-developer-react-version-support--docs
---

# React Version Support

> ℹ️ **Note**: The migration docs here focus solely on Fluent UI-related changes. If you're migrating between React major versions, refer to the official React documentation for comprehensive migration guides. For migrating your codebase TypeScript types, you can leverage [Types React Codemod](https://github.com/eps1lon/types-react-codemod).

## React 17

Full support starting `@fluentui/react-components` v9.0.0.

## React 18

Full support starting `@fluentui/react-components` v9.66.0.

### Migration

> 💡 See [PR #34456](https://github.com/microsoft/fluentui/pull/34456) for details.

#### Runtime/API changes

NONE

#### TypeScript types changes

##### Slot Children as a Function

Because of `@types/react@18` breaking changes, the `Slot` children property was loosened to `any`. This affects users that use `Slot` children as a function in conjunction with TypeScript strict mode — `noImplicitAny` will fail. Add a `satisfies SlotRenderFunction<T>` assertion:

```tsx
// Before
<Button
  // children was inferred as union of ReactNode and SlotRenderFunction
  icon={{ children: (Component, props) => <Component {...props /> }}
>
  Label
</Button>

// After
import { type SlotRenderFunction } from '@fluentui/react-utilities';

<Button
  icon={{
    // children is now `any` and needs to be asserted as `SlotRenderFunction`
    children: ((Component, props) => <Component {...props} />) satisfies SlotRenderFunction<
      React.ComponentProps<'span'>
    >,
  }}
>
  Label
</Button>
```

## React 19

Full support starting `@fluentui/react-components` v9.72.2.

### Migration

> 💡 Follow official React 19 migration guidelines.

#### Runtime/API changes

NONE

#### TypeScript type recommendations

> 📚 **For Library Authors & FluentUI Extension Developers** — these recommendations ensure your library's TypeScript types remain backwards compatible across React 17, 18, and 19. See [PR #34733](https://github.com/microsoft/fluentui/pull/34733) for technical details.

##### 1. Enforce explicit return types for render functions & hooks

React 19 removed the global `JSX` type, which can cause type compatibility issues across versions. Always explicitly type your APIs that return JSX markup.

```js
// ESLint
{
  '@typescript-eslint/explicit-module-boundary-types': [
    'error',
    {
      allowArgumentsExplicitlyTypedAsAny: true,
      allowOverloadFunctions: true,
    },
  ],
}
```

**Why?** Prevents implicit return type inference that may differ between React versions, ensuring component and hook signatures remain consistent. See [PR #35080](https://github.com/microsoft/fluentui/pull/35080).

##### 2. Use Fluent UI cross-version compatible JSX types

React's JSX namespace types changed between versions. Fluent UI provides stable, cross-compatible type utilities that work across React 17, 18, and 19.

Configure `@typescript-eslint/no-restricted-types` to enforce Fluent UI types. [Complete setup in PR #34923](https://github.com/microsoft/fluentui/pull/34923/files#diff-76039af2f09c079049956d7b4b45efee6d01993677e1680519f5863fae6f0919).

❌ **Before (React-specific types)**:

```tsx
const renderFoo = (): JSX.Element => <div>Hello</div>;

interface SomeElementProps {
  as?: keyof JSX.IntrinsicElements;
  divProps?: JSX.IntrinsicElements['div'];
}
```

✅ **After (FluentUI cross-version types)**:

```tsx
import type { JSXElement, JSXIntrinsicElementKeys, JSXIntrinsicElement } from '@fluentui/react-components';

const renderFoo = (): JSXElement => <div>Hello</div>;

interface SomeElementProps {
  as?: JSXIntrinsicElementKeys;
  divProps?: JSXIntrinsicElement<'div'>;
}
```

**Available types**:

- `JSXElement` — replaces `JSX.Element` / `React.JSX.Element`
- `JSXIntrinsicElementKeys` — replaces `keyof JSX.IntrinsicElements`
- `JSXIntrinsicElement<K>` — replaces `JSX.IntrinsicElements[K]`

**Benefits**:

- ✅ Works across React 17, 18, and 19
- ✅ No breaking changes when users upgrade React versions
- ✅ Consistent type checking regardless of `@types/react` version
