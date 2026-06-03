# Task 074: @spaarke/ui-components tsc fixes — Memo

**Date**: 2026-05-27
**Task**: R4 Phase 6.5 / B.2 — Fix 24 pre-existing tsc errors
**Status**: ✅ Complete — `npx tsc --noEmit` 0 errors (down from 12 distinct errors / ~24 with multi-line continuations)

## Summary

The "24 errors" cataloged in `notes/b11-cast-inventory.md` CO-1 turned out to be
**12 distinct compiler errors** (some appeared as multi-line continuations in
tsc output). All 12 fit cleanly into Categories A/B/C; Category D was empty.

## Final tsc result

```
$ npx tsc --noEmit
(empty output, exit 0)
```

## Per-category breakdown

### Category B — JSX namespace migration (3 errors) — fixed first

TypeScript 5 + recent @types/react no longer expose a global `JSX` namespace
unless `jsxImportSource: "react"` shim emits one. Cleanest fix: prefix with
`React.` since each affected file already imports `* as React`.

| File | Change |
|------|--------|
| `src/components/AiProgressStepper/AiProgressStepper.tsx:272` | `JSX.Element` → `React.JSX.Element` |
| `src/components/PanelSplitter/PanelSplitter.tsx:98` | `JSX.Element` → `React.JSX.Element` |
| `src/components/ThreePaneLayout/ThreePaneLayout.tsx:315` | `JSX.Element` → `React.JSX.Element` |

### Category A — React 19 RefObject nullability drift (3 errors)

React 19 + `@types/react@19` types changed: `useRef<T>(null)` now returns
`RefObject<T | null>`, not `RefObject<T>`. Consumer prop types declared
`RefObject<HTMLElement>` (non-null), so assignment from `useRef<HTMLDivElement>(null)`
no longer matched.

Fix: widen the **declared prop types** to `RefObject<T | null>`. This is the
correct React 19 idiom — refs may always be null mid-render (or once the
component unmounts), and consumer code already null-checks at the use site
(e.g. `contentRef.current` is guarded).

| File | Change |
|------|--------|
| `src/components/SprkChat/types.ts:470` | `RefObject<HTMLElement>` → `RefObject<HTMLElement \| null>` (`ISprkChatProps.contentRef`) |
| `src/components/SprkChat/types.ts:692` | `RefObject<HTMLElement>` → `RefObject<HTMLElement \| null>` (`ISprkChatHighlightRefineProps.contentRef`) |
| `src/components/SlashCommandMenu/slashCommandMenu.types.ts:134` | `RefObject<HTMLElement>` → `RefObject<HTMLElement \| null>` (`ISlashCommandMenuProps.anchorRef`) |
| `src/hooks/useSlashCommands.ts:79` | `RefObject<HTMLTextAreaElement \| HTMLInputElement>` → `RefObject<HTMLTextAreaElement \| HTMLInputElement \| null>` |

Three error sites (SprkChat.tsx:2190, SprkChatInput.tsx:119, SprkChatInput.tsx:233)
all resolved by these type-declaration widenings — no call-site changes needed.

### Category C — Combobox/Dropdown onInput/onOptionSelect drift (6 errors)

Fluent v9's `Combobox.onInput` is typed as
`React.FormEventHandler<HTMLInputElement>` (not `ChangeEventHandler`). For
`onOptionSelect`, the handler signature is
`(event: SelectionEvents, data: OptionOnSelectData) => void`. Inference via
`DropdownProps['onOptionSelect']` failed under `noImplicitAny` because the
nullable callback type stripped parameter types.

#### TodoDetail.tsx (3 errors)

- Line 507 (`searchTimerRef`): `useRef<ReturnType<typeof setTimeout>>()` now
  requires an explicit initial value in TS 5+. Changed to
  `useRef<ReturnType<typeof setTimeout> | undefined>(undefined)`.
- Line 508 (`handleContactInput`): Changed event type from
  `React.ChangeEvent<HTMLInputElement>` to `React.FormEvent<HTMLInputElement>`,
  and reading from `ev.target.value` to `ev.currentTarget.value` (which is
  always typed as the element vs. the broader `EventTarget`).
- Line 528 (`handleContactSelect`): Added explicit
  `(_ev: SelectionEvents, data: OptionOnSelectData) =>` parameter types.
  Imported the types from `@fluentui/react-components`.

#### ViewSelector.tsx (1 error site, 2 messages)

- Line 160: Added explicit
  `(_event: SelectionEvents, data: OptionOnSelectData) =>` parameter types on
  the `onOptionSelect` callback. Imported the types from
  `@fluentui/react-components`.

### Category D — Miscellaneous

Empty. No errors fell outside A/B/C in the actual baseline output.

## Files modified (8)

```
src/components/AiProgressStepper/AiProgressStepper.tsx
src/components/PanelSplitter/PanelSplitter.tsx
src/components/ThreePaneLayout/ThreePaneLayout.tsx
src/components/SprkChat/types.ts
src/components/SlashCommandMenu/slashCommandMenu.types.ts
src/hooks/useSlashCommands.ts
src/components/DatasetGrid/ViewSelector.tsx
src/components/TodoDetail/TodoDetail.tsx
```

## Exclusion verification

Per task scope, `DatasetGrid/GridView.tsx` and `DatasetGrid/ListView.tsx` were
NOT touched (those belong to task 073, running in parallel). The diffs that
appear in `git status` against those files were already present in the
working tree at task start (their contents include `// Task 073 (B.1):`
comments).

## Hard guardrails verified

| Check | Result |
|-------|--------|
| No `as any` added | ✅ — `grep "^+.*as any"` returned 0 hits in my 8 files |
| No `@ts-ignore` added | ✅ — 0 hits |
| No `@ts-nocheck` added | ✅ — 0 hits |
| GridView.tsx not modified by me | ✅ — diff predates my session (task 073) |
| ListView.tsx not modified by me | ✅ — diff predates my session (task 073) |
| `npx tsc --noEmit` final | ✅ — 0 errors, exit 0 |

## Test verification

`npm test` final: 53/55 suites pass, 1050/1051 tests pass.

Two pre-existing failures (verified pre-existing via `git stash`-and-rerun):

1. `SprkChat.attachments.test.tsx` — 1 test fails (`mockClearAll` not called).
   Root cause: `useKeyboardShortcuts.ts:32` TypeError on undefined commands
   array. This is independent of my type fixes — same failure mode reproduces
   without my changes.
2. `CommandBar.test.tsx` — suite fails to load due to Jest worker child-process
   exceptions (4 retries exhausted). Same failure reproduces without my
   changes. Infrastructure-level issue, not a test-content failure.

Both failures are infrastructure / pre-existing test bugs and are NOT caused
by Task 074's type fixes. Recommend filing as carry-overs / separate task if
not already tracked.

## Cascading errors

None. Each category fix resolved cleanly without revealing new errors.
12 → 9 → 6 → 0 errors across the three category fix waves.

## Why these fixes are React-19-idiomatic

- **Category A**: Widening declared prop types to `T | null` is the
  React-19-recommended approach when consumers pass refs that can be null
  (initial render, post-unmount). The alternative — narrowing at call sites
  with `as RefObject<HTMLElement>` — would have introduced unchecked casts.
- **Category B**: `React.JSX.Element` is the canonical fully-qualified form
  per React 19's typings. It avoids the deprecated global `JSX` namespace
  without requiring a `tsconfig.json`-level `jsxImportSource` change that
  could affect other packages.
- **Category C**: Using Fluent v9's exported `SelectionEvents` /
  `OptionOnSelectData` types is the documented Fluent v9 pattern. For
  `onInput`, `React.FormEvent<HTMLInputElement>` matches the Fluent type
  exactly; reading from `currentTarget.value` is the type-safe path (versus
  `target` which can widen to `EventTarget`).

## ADR compliance

- **ADR-021** (Fluent v9): ✅ All Combobox/Dropdown handlers now use Fluent v9's
  actual event signature. No Fluent v8 patterns introduced.
- **ADR-022** (React 19): ✅ All ref types now match React 19's nullable
  `RefObject<T | null>` contract. No `useRef` shims or back-compat patches.
