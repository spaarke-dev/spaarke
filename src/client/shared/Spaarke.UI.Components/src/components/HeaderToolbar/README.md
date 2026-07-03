# HeaderToolbar

Canonical **shared** toolbar for record-header surfaces. Ships FR-01 of `record-header-and-notepad-r1`.

## Contract

```tsx
interface IHeaderToolbarSlot {
  key: string;                     // stable React key
  icon: React.ReactElement;        // Fluent v9 icon (from @fluentui/react-icons)
  onClick: () => void | Promise<void>;
  tooltip: string;                 // REQUIRED a11y label
  badge?: number;                  // rendered only when > 0
  disabled?: boolean;
}

interface IHeaderToolbarProps {
  title?: string;                  // ellipsis on overflow; omit for icons-only
  iconSlots: IHeaderToolbarSlot[]; // 0..N right-aligned icons
}
```

## Usage

```tsx
import { HeaderToolbar } from '@spaarke/ui-components';
import { Sparkle20Regular, Checkmark20Regular, Note20Regular } from '@fluentui/react-icons';

<HeaderToolbar
  title="Matter Summary"
  iconSlots={[
    { key: 'ai',    icon: <Sparkle20Regular />,   onClick: openSummary,  tooltip: 'View AI summary' },
    { key: 'todos', icon: <Checkmark20Regular />, onClick: openTodos,    tooltip: 'Related to-dos', badge: 3 },
    { key: 'note',  icon: <Note20Regular />,      onClick: openNotepad,  tooltip: 'Notes',          badge: 12 },
  ]}
/>
```

## Invariants (do NOT break)

- **One shared toolbar** — record-header surfaces consume this component; they do NOT hand-roll their own toolbar chrome. VisualHost's `CardChrome.tsx` remains internal to VisualHost per FR-VH-05; it is a behavior reference, not a re-use target.
- **Every slot MUST supply `tooltip`** — it doubles as the icon-only button's `aria-label`.
- **Badge is suppressed** when `badge` is `undefined`, `0`, negative, or non-finite. Only positive finite integers render.
- **Fluent v9 semantic tokens only** — zero hex/rgb/hsl literals (ADR-021 / NFR-03).
- **React 16/17 safe** — consumed by PCFs on the Dataverse platform-library runtime (ADR-022 / NFR-06). No `use()`, no `useSyncExternalStore` without a polyfill, no React 18-exclusive concurrent APIs.

## Related

- `RecordHeaderShell` — hosts `HeaderToolbar` at the top of the card (FR-02).
- `useRecordHeaderToolbarActions` — hook that produces a fully-wired `IHeaderToolbarProps` for the three canonical record actions (sparkle / checkmark / annotation).
- `src/client/pcf/VisualHost/control/components/CardChrome.tsx` — behavior reference only; DO NOT reuse.
