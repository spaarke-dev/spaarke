# Task 032 — EmailReadingPaneShell (composition root)

**Status**: Complete. Build green (`tsc`), jest green (11/11 new tests; 39/39 package-wide).

## What was built

- `src/client/shared/Spaarke.Communication.Components/src/components/EmailReadingPaneShell/EmailReadingPaneShell.types.ts` — the slot + dispatch contract: `EmailPaneSlotRenderer`, `EmailToolbarActionHandlers`, `EmailReadingPaneShellProps`.
- `.../EmailReadingPaneShell/EmailReadingPaneShell.tsx` — the two-pane composition root. Reuses `PanelSplitter` + `useTwoPanelLayout` (both `@spaarke/ui-components`, ADR-012) for the resizable list/reading-pane split; owns `selectedId` state (`EmailCardList.onSelect` drives it); renders the "Select an email" placeholder (FR-19) when unset; renders the full-width `<EmailToolbar/>` + the 5 slot calls when a card is selected.
- `.../EmailReadingPaneShell/EmailToolbar.tsx` — the full-width action toolbar (FR-08): Reply / Reply All / Forward / New / Archive / Create. Pure dispatch — calls the host-supplied `EmailToolbarActionHandlers` entry with the selected id (New excepted — it is not record-scoped); an unwired handler falls back to a console-warned no-op instead of throwing or silently doing nothing.
- `.../EmailReadingPaneShell/index.ts` — barrel for the component folder (component + types + toolbar).
- `.../EmailReadingPaneShell/__tests__/EmailReadingPaneShell.test.tsx` — 11 tests: two-pane layout render, FR-19 placeholder, selection drives all 5 slots with the selected id, `onSelectedIdChange` observability, all 6 toolbar buttons render + dispatch with the correct id, New always enabled, unwired-handler no-op (console.warn spy), splitter width persists across remount (localStorage key), default width when no persisted value, dark-mode theming with a `console.error` spy.
- `src/client/shared/Spaarke.Communication.Components/src/components/index.ts` — appended `export * from './EmailReadingPaneShell';` (one-line barrel addition only — did not reorder or touch the sibling `EmailViewSelector` line added concurrently by task 031/033-adjacent work).

## The slot + dispatch contract (for tasks 033/034/035/036)

`EmailReadingPaneShellProps` (`EmailReadingPaneShell.types.ts`) is the stable surface downstream tasks fill in — **the shell file itself does not need to change** for any of the following:

| Prop | Type | Filled by | Notes |
|---|---|---|---|
| `renderBody` | `(selectedId: string) => React.ReactNode` | Task 033 | `.eml` render (sandboxed iframe per NFR-03) |
| `renderHeader` | `(selectedId: string) => React.ReactNode` | Task 034 | header + from/to/subject chrome |
| `renderAttachments` | `(selectedId: string) => React.ReactNode` | Task 034 | reuses `AttachmentList` (task 021) |
| `renderConnections` | `(selectedId: string) => React.ReactNode` | Task 035 | production `ConnectionsEditor`, additive write path only (ADR-045) |
| `renderTracking` | `(selectedId: string) => React.ReactNode` | Task 035 | tracking fields (task 023 core) |
| `actions` | `EmailToolbarActionHandlers` | Task 036 | Reply/ReplyAll/Forward/New/Archive/Create real dispatch + "Open full form" |

All five `render*` slots are called (top→bottom: header → body → attachments → connections → tracking) whenever `selectedId` is set — a slot renders nothing if its prop is omitted (`?.()`), so partial adoption during the P3b wave is safe (a sub-view task landing before its siblings doesn't break the others).

`EmailToolbarActionHandlers` (task 022 dispatch seam — NOT a forked action-bar):

```ts
interface EmailToolbarActionHandlers {
  onReply?: (selectedId: string) => void;
  onReplyAll?: (selectedId: string) => void;
  onForward?: (selectedId: string) => void;
  onNew?: () => void;          // not record-scoped — blank compose
  onArchive?: (selectedId: string) => void;
  onCreate?: (selectedId: string) => void;
}
```

`EmailToolbar` never implements compose/prefill/archive logic — every button calls the matching handler (or a `console.warn`'d no-op if the host hasn't wired it yet). Task 036 is expected to build these handlers on top of the already-landed task-022 `logic/actions` (`deriveComposerFields`, `launchCreate`, `fetchSourceAttachments`, etc. — `@spaarke/communication-components/logic/actions`) plus the existing `/communications/{id}/archive` BFF endpoint, mirroring `CommunicationActionsApp.tsx`'s `openComposer`/`handleArchive`/`handleCreate` — without editing this shell.

Selection: the shell owns `selectedId` internally (`useState`, seeded by the optional `initialSelectedId` prop) and exposes it to slots by CALLING them with the id, not by exporting the state — `onSelectedIdChange` is provided purely for host-side observability (e.g. updating a URL/breadcrumb), it does **not** control selection.

Splitter persistence: reused `useTwoPanelLayout` (`@spaarke/ui-components`) owns the `localStorage` persistence + drag/keyboard resize; the shell only supplies `storageKey` (default `sprk-email-reading-pane-splitter`) — a host embedding multiple shell instances should pass a distinct `storageKey` per instance.

## Deviations / notes for reviewers

1. **"Create" is a single toolbar slot**, not the 3-icon Event/To-Do/Invoice cluster in the current `CommunicationActionsApp` PCF. The task POML lists six flat slots (Reply/Reply All/Forward/New/Archive/Create); disambiguating "Create" into its 3 sub-kinds (event/todo/invoice) — or a menu — is left to task 036, which owns the real dispatch behavior. `onCreate` is typed to take just `selectedId`; if 036 needs the `CreateKind` distinction, the natural extension is widening `onCreate` to accept an optional kind or exposing three handlers — either is additive to `EmailToolbarActionHandlers`, no shell-file edit required.
2. **No `useTwoPanelLayout`/`PanelSplitter` fork** — considered building a bespoke two-pane layout for the email-specific min-width defaults, but `useTwoPanelLayout`'s existing `minPrimaryWidth`/`minDetailWidth`/`defaultDetailWidth`/`storageKey` options cover the need; used as-is (ADR-012).
3. No ADR violations found (ADR-012, ADR-021, ADR-022/NFR-05, ADR-045 all compliant per Step 9.5 `adr-check`).

## Quality gates (Step 9.5)

- `code-review`: Clean — 0 Critical, 0 Warning, 0 Suggestion (fresh files, all under size/complexity thresholds; no AI code smells found).
- `adr-check`: Clean — 0 Violations, 0 Warnings across ADR-012 / ADR-021 / ADR-022 / ADR-045 (grep-verified: no Fluent v8 imports, no hardcoded colors, no `as React.ComponentType` cast in code, no direct `Xrm`/`WebApi`/association calls).
