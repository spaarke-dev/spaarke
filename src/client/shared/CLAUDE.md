<!--
Maintainer notes (stripped before Claude reads this file):
- Loads whenever Claude reads or edits a file under src/client/shared/ (all shared TS packages, not only ui-components).
- Size target: about 6 KB — a target, not a cap; exceed it when the content is load-bearing and say why in the PR. No illustrative components that do not exist (the previous version showed StatusBadge,
  usePagination and formatters.ts, none of which are in the package).
- The "Scrollable Lists" section is cited by ADR-051 — keep its heading.
- Previous full version: .claude/archive/2026-10-07/modules/client-shared.CLAUDE.md
-->
# src/client/shared — shared TypeScript packages

Shared React/TypeScript libraries consumed by PCF controls, Code Pages (`src/solutions/*`), the external SPA and the Office add-ins. One folder per package; several have their own `README.md` (Compose, DocumentOperations, Events, Notifications, SdapClient, SmartTodo, Visuals) — read it before changing that package.

**`@spaarke/ui-components`** (`Spaarke.UI.Components/`) has two entry points:
- `src/index.ts` — the main barrel, for Code Pages (React 19);
- `src/pcf-safe.ts` — only exports verified compatible with React 16/17; **PCF controls import from here**. Type drift between the two React versions: [`.claude/patterns/ui/fluent-v9-react-version-boundaries.md`](../../../.claude/patterns/ui/fluent-v9-react-version-boundaries.md).

Consumers reference packages by relative path (`"@spaarke/ui-components": "file:../../shared/Spaarke.UI.Components"`), not a workspace protocol.

## Binding rules

- **Context-agnostic components (ADR-012).** Components take data and callbacks through props; they never take `ComponentFramework.Context`, `Xrm` or other host-specific objects. Data fetching stays with the host.
- **Which package, and when to add one** — [`.claude/adr/ADR-012-shared-components.md`](../../../.claude/adr/ADR-012-shared-components.md): `@spaarke/visuals` is the only home for data-viz primitives; a **new** shared package needs all three of ADR-012's criteria **and** an ADR amendment — otherwise extend an existing package. Promote a component only when 2+ surfaces use it (or credibly will), it is a core Spaarke UX pattern, and its API is clean; experimental or host-coupled components stay in their module. Root §11 applies.
- **Fluent UI v9 only** (`@fluentui/react-components`; never v8 `@fluentui/react`); design tokens, no hard-coded colours (ADR-021).
- Export prop types alongside each component; document public props with JSDoc; bump the package version on breaking changes.
- Auth for every package goes through `@spaarke/auth` (`Spaarke.Auth/`, ADR-028) — no other MSAL instance anywhere.

## Scrollable Lists — Infinite Lazy-Scroll (ADR-051, binding)

Every scrollable list / record collection in this library uses **infinite lazy-scroll + the canonical thin scrollbar** — **never a pager**.

- **Dataverse-backed list → use `<DataGrid configId=… />`.** It ships infinite lazy-scroll (internal `useLazyLoad` + a bottom-sentinel `IntersectionObserver`) AND the thin scrollbar out of the box. Don't build a parallel list component.
- **Any scroll container → spread `thinScrollbarStyle`** (single scroller) or `thinScrollbarDescendantStyle` (surface root) from this package's `theme/scrollbar`. Never hand-roll `::-webkit-scrollbar` or a hex thumb (breaks dark mode; see [`.claude/patterns/ui/thin-scrollbar.md`](../../../.claude/patterns/ui/thin-scrollbar.md)).
- **`hasMore` = `moreRecords === true || page-was-full`.** The MDA `Xrm.WebApi` client strips the FetchXML `morerecords`/paging-cookie annotations, so `useLazyLoad` falls back to page fullness — do not regress this to a `moreRecords`-only check (it silently caps MDA lists at page 1 / "shows only 25").

| ✅ DO | ❌ DON'T |
|-------|----------|
| Reuse `<DataGrid>` for record lists; page incrementally (`pageSize` ~25–50) | Add a "Load more" button, prev/next, numbered pages, or a down-arrow/chevron next-page control |
| Spread `thinScrollbarStyle` on the real `overflow:auto` element | Hand-roll `::-webkit-scrollbar` rules or a hex scrollbar thumb |
| Keep the page-fullness `hasMore` fallback | Rely on `moreRecords` alone (stripped under MDA `Xrm.WebApi`) |
| Verify scroll advances past page 1 in each host | Load the whole set in one giant page (`pageSize=500`) to "show all" |

Full rule: [`.claude/patterns/ui/infinite-scroll-list.md`](../../../.claude/patterns/ui/infinite-scroll-list.md) · [ADR-051](../../../.claude/adr/ADR-051-infinite-scroll-lists.md).

## Tests

Component tests use React Testing Library inside a `FluentProvider`, alongside the package source (e.g. `Spaarke.UI.Components/src/__tests__/`). Every shared component ships with tests.
