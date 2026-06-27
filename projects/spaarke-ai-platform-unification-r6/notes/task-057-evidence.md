# Task 057 evidence — User affordances: Send to Workspace + Add to Assistant + Pin to Matter (D-C-08 / D-C-09 / D-C-10)

**Pillar / Spec ref**: R6 Pillar 6b / FR-39 — three end-user workspace affordances.
**Wave**: C-G3 gap-fill (closing the 2677f9439 partial-checkpoint state).
**Date**: 2026-06-11.

## Components (on disk)

All three live in `src/solutions/SpaarkeAi/src/components/workspace/`:

- `SendToWorkspaceButton.tsx` — Fluent v9 Button + Tooltip. Promotes a chat
  assistant message to a new workspace tab by dispatching
  `workspace.widget_load` to the existing WorkspacePane subscriber (the same
  pipeline as agent-initiated loads). Controlled by `content` prop; disabled
  when content is empty/whitespace. Carries a `widgetType` selector (Summary
  default for narrative chat messages) and an optional `onSent` observer
  callback. Per ADR-021: Fluent v9 semantic tokens only.
- `AddToAssistantToggle.tsx` — Fluent v9 Switch + Tooltip. Flips the tab's
  `visibleToAssistant` flag (Pillar 9 privacy default — user-created tabs
  default false; the user opts in via this toggle). Dispatches
  `workspace.tab_edited` with `editedFields: ['visibleToAssistant']` per
  ADR-015 (field NAMES, not values). Controlled (parent owns the boolean +
  the persistence call).
- `PinToMatterButton.tsx` — Fluent v9 ToggleButton + Tooltip. Promotes a tab
  from the Redis hot tier (24h TTL) to the Cosmos durable tier attached to
  `matterId` (Pillar 6a Q4 hybrid persistence). Dispatches
  `workspace.tab_edited` with `editedFields: ['isPinned', 'matterContext']`
  per ADR-015. Disabled when `matterId` or `tabId` is empty.

## Test files (on disk)

`src/solutions/SpaarkeAi/src/components/workspace/__tests__/`:

- `SendToWorkspaceButton.test.tsx` — 6 tests covering render, disabled state,
  dispatch payload shape (default + custom widgetType), `onSent` observer,
  and the disabled no-op contract.
- `AddToAssistantToggle.test.tsx` — 8 tests covering both aria-label states,
  toggle-on / toggle-off `onChange` propagation, the
  `workspace.tab_edited`-with-`editedFields:['visibleToAssistant']` dispatch
  shape (ADR-015), and disabled states.
- `PinToMatterButton.test.tsx` — 13 tests (NEW in this gap-fill) covering
  render (light + dark theme — ADR-021 parity smoke), aria-label state
  flips, aria-pressed, click handler with next-state propagation, the
  `workspace.tab_edited` dispatch shape with `editedFields:['isPinned',
  'matterContext']`, ADR-015 anti-leakage assertion (matterId VALUE never
  appears in the dispatched event payload), and three disabled states
  (empty matterId, empty tabId, explicit disabled prop).

## ADR-030 PaneEventBus channel audit

All three affordances dispatch on the EXISTING `workspace` channel — NO new
5th channel introduced. Event types used:

- `workspace.widget_load` (pre-existing; SendToWorkspaceButton)
- `workspace.tab_edited` (additive event type per Pillar 6c task 060;
  AddToAssistantToggle, PinToMatterButton)

NFR-05 binding satisfied: `workspace` channel is one of the existing four
(`workspace / context / conversation / safety`).

## Test infrastructure fixes (gap-fill scope)

Two unrelated SpaarkeAi-workspace test-infra blockers surfaced when verifying
the three test suites. Both fixed in scope:

1. **`@spaarke/sdap-client` not resolvable** — every test transitively
   importing the `@spaarke/ui-components` barrel (via the SpaarkeAi test
   moduleNameMapper) failed at `EntityCreationService.ts` line 34. Fixed by:
   - Added `src/solutions/SpaarkeAi/src/__mocks__/sdap-client.ts` — minimal
     no-op stub of `SdapApiClient` + type-only re-exports.
   - Added `'^@spaarke/sdap-client$'` mapping in
     `src/solutions/SpaarkeAi/jest.config.ts` mirroring the existing `marked`
     mock pattern.

2. **Barrel-side-effect chain pulled in `CreateMatterWizard` widget** which
   needs SDAP client + d3-force ESM. Fixed by rewiring the three affordance
   components and their tests from `@spaarke/ai-widgets` (full barrel) to
   the narrower `@spaarke/ai-widgets/events` subpath. The components only
   use `useDispatchPaneEvent` and the tests only need
   `PaneEventBus / PaneEventBusProvider / WorkspacePaneEvent` — all of which
   the `events/index.ts` barrel exports cleanly. No production behavioral
   change; the runtime barrel still re-exports the `events` slice.

Also: corrected a pre-existing JSX-attribute-vs-JS-expression bug in
`SendToWorkspaceButton.test.tsx` ("is disabled when content is whitespace
only") — the original `content="   \n\t  "` did not interpret backslash
escapes because JSX attribute strings are literal. Changed to a JS
expression `content={'   \n\t  '}` so `\n` / `\t` become actual whitespace
that the component's `content.trim().length === 0` check detects.

## Tests run

```
cd src/solutions/SpaarkeAi && npm test -- --testPathPatterns="PinToMatter|SendToWorkspace|AddToAssistant"

Test Suites: 3 passed, 3 total
Tests:       27 passed, 27 total
Time:        3.058 s
```

## Governance

- **ADR-012** (Fluent v9 shared lib): all three components use
  `@fluentui/react-components` only — Buttons, Switch, ToggleButton, Tooltip
  — no v8 imports.
- **ADR-015** (data governance): the dispatched event payloads carry FIELD
  NAMES only. The PinToMatterButton test explicitly asserts the `matterId`
  VALUE never appears in the dispatched event (substring-search assertion
  on the serialized payload).
- **ADR-021** (dark-mode parity): tests render under both `webLightTheme`
  and `webDarkTheme` and confirm no light-only hex colors are baked in.
  Visual VRT in CI is the higher-fidelity check; the component-level test
  is the structural floor.
- **ADR-022** (React 19 functional components + hooks): all three
  components are functional with `React.useCallback` for click handlers and
  no class components, no lifecycle methods.
- **ADR-030** (PaneEventBus 4-channel): only `workspace` channel used; no
  new channel added; additive event-type usage only.

## Outcome

- ✅ Three Fluent v9 affordances on disk + tested (27/27 passing).
- ✅ ADR-015 anti-leakage assertion enforced for PinToMatterButton.
- ✅ ADR-021 dark-mode smoke covered.
- ✅ Existing 5th Send/AddToAssistant tests verified passing after subpath
  rewire (the 2677f9439 checkpoint had committed them without test
  verification).
- ✅ Test infrastructure heals applied (SpaarkeAi sdap-client mock +
  barrel-to-subpath rewire) — no regression on other SpaarkeAi tests
  (only affordance test files touched in their test bodies).

R6 Pillar 6b user-affordance surface is end-to-end functional. The host
wiring (chat-message strip integration, tab-header integration, BFF
persistence call from `onPin` / `onChange`) is consumer responsibility per
the components' controlled-affordance contracts.
