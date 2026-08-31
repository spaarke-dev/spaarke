# UI Patterns Index

> **Last Reviewed**: 2026-05-26
> **Status**: Current

> Pointer-based pattern files for Spaarke UI work using Fluent UI v9 across PCF, Code Pages, External SPA, Office Add-ins, and MCP App widgets.

| Pattern | When to Load | Last Reviewed | Status |
|---|---|---|---|
| [choice-dialog-pattern.md](choice-dialog-pattern.md) | Implementing choice/selection dialog UI | 2026-04-05 | Verified |
| [record-modal-selection.md](record-modal-selection.md) | Opening a record / document / form / picker AS a modal — picks between OOB `navigateTo`, proprietary Fluent v9 Dialog, and the browse-shell (`RecordNavigationModalShell`). Load whenever a task involves a modal launch. | 2026-07-01 | Current |
| [modal-shell.md](modal-shell.md) | BUILDING or modifying a modal (confirm/choice/form/preview/browse/wizard) — the COMPONENT layer (`SprkModal` shell + six presets, `MODAL-DESIGN-SYSTEM.md`) beneath record-modal-selection.md's decision layer. Load when authoring on the modal shell. | 2026-08-01 | Current |
| [inline-lookup-field.md](inline-lookup-field.md) | Adding or modifying ANY lookup / entity-reference / search-as-you-type field on any surface (PCF, Code Page, wizard step, widget). Compose the shared `LookupField` — ~14 consumers. Covers the TWO same-named components, the `onSearch`/`span`/`appearance`/`onAdvanced` wiring contract, and why the OOB inline control cannot be hosted. | 2026-08-27 | Current |
| [fluent-v9-component-authoring.md](fluent-v9-component-authoring.md) | Authoring/modifying any Fluent v9 React component | 2026-05-26 | Current |
| [fluent-v9-theming.md](fluent-v9-theming.md) | Theme decisions, FluentProvider wiring, dark mode, Spaarke brand theme | 2026-05-26 | Current |
| [fluent-v9-portal-gotcha.md](fluent-v9-portal-gotcha.md) | Using Popover / Tooltip / Toast / Dialog / Menu / Combobox dropdown | 2026-05-26 | Current |
| [fluent-v9-react-version-boundaries.md](fluent-v9-react-version-boundaries.md) | Authoring in Spaarke.UI.Components OR bumping React versions | 2026-05-26 | Current |
| [fluent-v9-host-visual-fit.md](fluent-v9-host-visual-fit.md) | Surface-by-surface theme-source matrix; "make it look native" inside MDA / Canvas / Code Pages / Office Add-ins | 2026-05-28 | Current |
| [embedded-widget-sizing.md](embedded-widget-sizing.md) | Building/maintaining ANY workspace widget — WIDTH chain (box-sizing + min-width:0 + ResizeObserver pixel cap) AND HEIGHT chain (WorkspaceLayoutWidget.root height:100% + WorkspaceShell.row flex + widget body display:flex + row-height override addendum) | 2026-07-03 | Current |
| [record-header-composition.md](record-header-composition.md) | Modifying the Record Header PCF OR consuming `@spaarke/ui-components` header primitives. Includes the mandatory bundle-optimization triad (`featureconfig.json` + `webpack.config.js` + deep-path imports). **Do NOT author a new per-entity header PCF** — withdrawn 2026-08-21 in favor of ONE configurable control (`projects/record-header-and-notepad-r2/design.md`). | 2026-08-21 | Current |
| [navigateto-popup-result-bridge.md](navigateto-popup-result-bridge.md) | Any wizard opened via `Xrm.Navigation.navigateTo({ target: 2 })` that must signal a result (savedId, confirmed flag) back to its opener when it closes | 2026-07-03 | Current |
| [oob-form-dialog-chrome.md](oob-form-dialog-chrome.md) | Modifying the chrome of an OOB Dataverse main form (hide tab navigator / entity-name subtitle, restyle dialog chrome), full-page OR `navigateTo` modal. Supported-API-first ladder + the two frame/re-render gotchas + console-first workflow. | 2026-08-18 | Current |
| [thin-scrollbar.md](thin-scrollbar.md) | Applying the Spaarke "modern thin grey scrollbar" (`thinScrollbarStyle` / `thinScrollbarDescendantStyle` from `@spaarke/ui-components`). Token-based (dark-mode-safe), the no-cascade gotcha, the "thumb length is native/proportional" answer, and the raw-CSS form for non-React surfaces. | 2026-08-18 | Current |
| [infinite-scroll-list.md](infinite-scroll-list.md) | Building ANY scrollable list / record collection. The standard = **infinite lazy-scroll + thin scrollbar, NEVER a pager** (no numbered pages / prev-next / "Load more" / down-arrow). Use `<DataGrid>` (built-in `useLazyLoad` + sentinel `IntersectionObserver`); covers the page-fullness `hasMore` fallback (why MDA grids silently cap at 25) + the custom-scroller recipe. **Governed by [ADR-051](../../adr/ADR-051-infinite-scroll-lists.md).** | 2026-08-31 | Current |

## Critical Constraint (ADR-021 + ADR-022)

All UI: Fluent UI v9 only. NO `@fluentui/react` (v8). NO hard-coded colors — use `tokens.*`. Spaarke.UI.Components must be React-16.14-safe (consumed by PCF).

## Related

- [`../pcf/fluent-v9-modern-theming.md`](../pcf/fluent-v9-modern-theming.md) + [`../pcf/fluent-v9-canvas-vs-mda-disabled.md`](../pcf/fluent-v9-canvas-vs-mda-disabled.md) — PCF-specific Fluent v9 patterns
- [`../../skills/fluent-v9-component/SKILL.md`](../../skills/fluent-v9-component/SKILL.md) — skill that loads these patterns on UI tasks
- [`../../../knowledge/fluent-ui-v9/docs/INDEX.md`](../../../knowledge/fluent-ui-v9/docs/INDEX.md) — verbose Microsoft + MVP reference archive
