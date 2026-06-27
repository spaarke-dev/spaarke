# Task 085 — `/help` UI Affordance — Evidence

> **Task**: D-D-06 (Pillar 8 / FR-53)
> **Wave**: D-G2 (parallel with 084, 086)
> **Rigor**: STANDARD
> **Closed**: 2026-06-18
> **Branch**: `work/spaarke-ai-platform-unification-r6`

---

## Outputs

| File | Type | LOC |
|------|------|-----|
| `src/solutions/SpaarkeAi/src/components/conversation/HelpAffordance.tsx` | NEW component | 168 |
| `src/solutions/SpaarkeAi/src/components/conversation/__tests__/HelpAffordance.test.tsx` | NEW tests | 184 (10 tests) |
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | MODIFIED | +14 LOC (import + render + style anchor) |

---

## Component design

- **Fluent v9 `Button` (`appearance="subtle"`) + `Tooltip`** with `QuestionCircleRegular` icon
- **`aria-label`**: `"Show available commands (/help)"`
- **Tooltip content**: `"Show available commands (/help)"` (matches `aria-label` so the tooltip serves as the accessible name via `relationship="label"`)
- **Screen-reader-only `<span>`** duplicates the action label so assistive tech announces it independent of tooltip activation
- **Controlled `onClick` prop** — host owns the state mutation. Component does NOT directly touch `setHelpPanelOpen` so it stays trivially testable
- **`data-testid="help-affordance"`** for UI tests + future Playwright wiring

### Optional props
- `disabled?: boolean` — supports future "help loading" UX without component rewrite
- `className?: string` — host can override positioning if needed (e.g. toolbar embedding)

---

## Placement choice — Option A (absolute-positioned overlay)

**Decision**: Render the button as an absolutely-positioned overlay anchored to the top-right of the chat region (`sprkChatFlex` wrapper). Updated the wrapper style to `position: relative` so the absolute anchor works.

**Rationale**:
- `<SprkChat>` owns its input bar internally — wrapping it in a custom container (Option B) would force a refactor (NFR-11 violation: existing input bar behavior must be unchanged; additive UX only)
- Overlay positioning is one line of CSS and zero risk to SprkChat's layout
- Top-right placement is conventional for help affordances (mirrors VS Code's Command Palette button, GitHub's `?` shortcut, etc.)
- Z-index 1 keeps it above SprkChat's transcript but below any modals (CommandHelpPanel uses Fluent v9 Dialog portals so the Dialog renders above this regardless)

**Rejected alternatives**:
- **Option B (wrap SprkChat in custom container)**: invasive — violates NFR-11
- **Add button to PaneHeader**: works but reduces discoverability when the user is mid-conversation (header is far from where the user is typing)
- **Inject into SprkChat's `predefinedPrompts`**: not a button — text chips don't read as a help affordance

---

## Accessibility

| Aspect | Implementation | Verified by test |
|--------|----------------|------------------|
| `aria-label` | `"Show available commands (/help)"` on the `Button` | `renders a button with aria-label "Show available commands (/help)"` |
| Tooltip | Fluent v9 `Tooltip` with `relationship="label"`; appears on hover | `renders inside a Tooltip with the spec content` |
| Keyboard focus | Native `Button` element — Tab-navigable | `calls onClick when activated via Enter key (keyboard accessibility)` + `Space key` variant |
| Screen-reader text | Visually-hidden `<span>` duplicates the action label | `exposes a screen-reader-only text node duplicating the action label` |
| Disabled state | `disabled` prop blocks `onClick` invocation | `does NOT call onClick when disabled and clicked` |

---

## ADR / NFR compliance

| Constraint | Status | Evidence |
|------------|--------|----------|
| **FR-53** — `/help` UI discoverable from chat input bar | ✅ | Button anchored to chat region top-right |
| **Phase D exit criterion 1** — `/help` works + is discoverable | ✅ | Click → `setHelpPanelOpen(true)` → same CommandHelpPanel as `/help` slash (task 081 reuse) |
| **ADR-012** — Fluent v9 only | ✅ | Imports `@fluentui/react-components` + `@fluentui/react-icons` only |
| **ADR-021** — semantic tokens only; dark-mode safe | ✅ | All colors via `tokens.*`; tests render in `webLightTheme` + `webDarkTheme` |
| **ADR-022** — functional component + hooks | ✅ | `function HelpAffordance(props)`; `React.useCallback` for handler |
| **ADR-029** — BFF publish-size delta = 0 MB | ✅ | Frontend-only; no `Sprk.Bff.Api/` touched |
| **NFR-11** — additive UX; existing input bar unchanged | ✅ | SprkChat props untouched; absolute-positioned overlay does not alter SprkChat's layout |

---

## Test results

```
$ npx jest src/components/conversation/__tests__/HelpAffordance.test.tsx
Test Suites: 1 passed, 1 total
Tests:       10 passed, 10 total
Snapshots:   0 total
Time:        33.053 s
```

**10 tests covering**:
1. Renders button with `aria-label`
2. Screen-reader-only text duplicates label
3. Tooltip content matches spec
4. Click triggers `onClick` once
5. Enter key triggers `onClick`
6. Space key triggers `onClick`
7. Renders in `webDarkTheme` (ADR-021)
8. Renders in `webLightTheme` (ADR-021)
9. Disabled state respects `disabled` prop
10. Disabled state does NOT call `onClick`

---

## TypeScript verification

```
$ npx tsc --noEmit 2>&1 | grep -E "HelpAffordance\.tsx|ConversationPane\.tsx"
(no output — no new tsc errors introduced)
```

Pre-existing TS6133 (unused variable) errors in unrelated files (`Spaarke.AI.Context`, `Spaarke.AI.Outputs`, `Spaarke.AI.Widgets`, etc.) are unchanged — not caused by this task.

---

## Wire-up in ConversationPane.tsx

Minimal additive change (no refactor of surrounding code):

1. **Import** (line 133): `import { HelpAffordance } from "./HelpAffordance";`
2. **Style update** (line 580–588): `sprkChatFlex` gets `position: "relative"` so the absolute-positioned overlay anchors to the chat region
3. **Render** (line 2009–2016): `<HelpAffordance onClick={() => setHelpPanelOpen(true)} />` rendered inside the `sprkChatFlex` wrapper, immediately above the already-existing `<CommandHelpPanel>` (wired by Wave D-G1 / task 081)

`helpPanelOpen` / `setHelpPanelOpen` already existed (line 880 — added by Wave D-G1). No new state, no new effects, no SprkChat prop changes.

---

## Downstream impact

- **Task 087** (vertical-slice integration test, all 9 pillars) — gates on 085 ✅ along with 084 + 086. Pillar 8 affordance now discoverable from the chat input bar; 087's "user discovers /help without knowing slash syntax" subscenario is now executable.
- **No other tasks unblocked solely by 085** — it's a leaf UX affordance.

---

## Tool count

Approximately 14 tool calls (Read POML, Read CommandHelpPanel, Read PinToMatterButton, Read CommandHelpPanel test, Read PinToMatterButton test, Read ConversationPane (4 reads), Write HelpAffordance, Write test, 3 Edits, 2 Bash runs, Read TASK-INDEX, Write current-task, Write evidence).
