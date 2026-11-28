# Task 048: phone layout at 480px or less (FR-32, #2466)

Status: done, 2026-11-28. Rigor FULL, Sonnet-tier task run on the session model, steps directional.

## What changed

All paths are under `src/client/external-spa/`.

| File | Change |
|---|---|
| `src/components/phoneLayout.ts` (new) | The one breakpoint: `PHONE_MAX_WIDTH_PX` = 480, `PHONE_MEDIA_QUERY`, `PHONE_MEDIA` (the makeStyles key), `TOUCH_TARGET` = 44px. `useIsPhoneWidth()` (matchMedia, follows `change`; false where matchMedia is absent). `usePhoneTouchTargetStyles` (makeStaticStyles), an app-wide rule inside the phone query: every Fluent Button and ToggleButton is at least 44 × 44px; menu items, tabs, table rows, inputs and selects are at least 44px high. It selects Fluent's exported class names with a doubled class, so it beats the components' atomic classes; `min-width` beats Fluent's `max-width` on icon-only buttons. |
| `src/App.tsx` | `AppShell` mounts the touch-target rule, so portalled dialogs and menus get it too. |
| `components/shell/PortalWorkspaceShell.tsx` | At phone width the body is one column. The collapsed assistant rail becomes a full-width bar. An open assistant takes the body (main is hidden) until it is collapsed. "Move assistant" is hidden (left/right means nothing in one column). Paddings drop to M. |
| `components/shell/TabStrip.tsx` | Tabs are 44px high, labels shorten with an ellipsis, and the close control is an inline 44px target instead of the 18px corner circle. |
| `components/AppHeader.tsx` | The product title wraps; paddings drop to M. |
| `components/shell/QuickStartPane.tsx`, `WidgetLibraryModal.tsx` | Card grids are one column. |
| `components/record/recordTemplates.ts` | `PHONE_OVERFLOW_ACTIONS` = Copy link, Change status, Delete, History. |
| `components/record/RecordTemplateModal.tsx` | At phone width those actions get `overflow: true` on the shared RecordCommandBar (its existing option; API unchanged). Refresh and the type's primary action (Download, Submit invoice, Edit) stay inline. Content padding drops to M/S. |
| `components/record/RecordTemplateHeader.tsx` | Header facts stack one per line without the left divider. |
| `tests/MobileLayout.test.tsx` (new) | 13 tests; see below. |

## Step 1 audit at 375px (before the change)

Measured in a throwaway harness (local Vite, the real shell, tab strip and record template with stand-in data; deleted before commit):
- the shell's body row held main and the assistant dock side by side; the dock's fixed width pushed the page past 375px (horizontal scroll);
- the tab close control was an 18px circle; tabs were 38px high;
- record toolbar buttons were 20 × 20px (Fluent `size="small"` icon-only), five in a row on an event record;
- header facts sat side by side with left dividers and wrapped unevenly;
- Quick Start and the widget library used two-column card grids;
- the SprkModal header/footer buttons and section header buttons were under 44px.

## Measurements after the change

**375 × 812** (harness, light and dark):
- `document.documentElement.scrollWidth` = 375; no element's right edge past the viewport.
- Controls under 44 × 44px: 0 (9 on the shell; 31–32 with a record open, SprkModal header/footer, section headers and the More actions menu included).
- Document record: Refresh and More actions inline (44 × 44); Copy link is in the menu at 44px high, aria-label "Copy link" = its tooltip.
- Event record (five actions): only Refresh and More actions inline.
- Assistant open: main is `display: none`, the dock is 375px wide, Move assistant is hidden; collapsing it brings the workspace back.

**1000px**: toolbar buttons 20 × 20 with Copy link inline, rail 44px wide, tabs 38px high, as before. **1280 / 1440px**: every new rule sits inside the phone media query, so nothing changes there.

Suite after the review fixes: 78 files, 2,183 tests pass; eslint and tsc clean.

## Decisions

1. **Secondary actions** are Copy link, Change status, Delete and History. Refresh stays inline because it is how a user recovers a stale record; the type's primary action stays inline because it is the reason to open that record on a phone.
2. **One app-wide touch-target rule** rather than per-component styles. Child dialogs, menus and Fluent controls inside sections are portalled or owned by other files; one static rule inside the phone query reaches all of them, and above 480px it does not apply.
3. **An open assistant takes the body on a phone.** Side by side does not fit in 375px, and stacking both would push the workspace below a full-height chat. The assistant's own collapse control brings the workspace back.
4. **Width-driven, not Teams-only** (NFR-03): a browser at 480px or less gets the same layout. No reason to gate on the Teams host was found.
5. The close control's icon keeps a 16px font size, matching the file's other icon sizes (ADR-021 governs colours and tokens; Fluent icon sizes are px by convention).

## Tests (scope)

`tests/MobileLayout.test.tsx` covers the breakpoint and the overflow, as the POML scopes:
- the 480px constant drives both the media key and the matchMedia query; the static rule sits inside it;
- `useIsPhoneWidth` follows the viewport;
- must-fire: at phone width Copy link is in More actions with aria-label = tooltip; must-not-fire: wider, every action is inline and there is no More actions;
- the record content and header facts carry the phone rules (Griffel atomic-class probe, which hashes the media query too);
- the shell stacks and hides main behind an open assistant; must-not-fire: a collapsed assistant leaves main visible;
- the tab strip's 44px tab and close control.

Focus logic was not changed; the existing child-dialog focus/Esc tests still pass. `App.teamsHost.test.tsx` passes unchanged.

## Known limits

- **K: not checked in the Teams mobile client, high-contrast, or the Teams dark theme.** The check used browser emulation at 375px in light and dark. The layout is width-driven and uses tokens only. Task 058 repeats the 375px check on dev, and should include those themes.
- **K: record types checked in the browser were Document and Event.** All six share `RecordTemplateModal`, the header and the section frame, and the tests run the real template.

## Found in passing

- Local mock mode (`VITE_DEV_MOCK=true`) still sends the browser to the real CIAM sign-in once a grid widget loads: `src/services/gridDataverseClient.ts` (`ActiveBackendDataverseClient` → `acquireActiveBffToken`) ignores `isDevMockEnabled()`. Filed as D-48 (#3331).

## Review (Step 9.5)

One adversarial pass (Sonnet), on commit `f58256fbbb`. It confirmed: the Fluent 9.73 class names in the static rule are the real roots (ToggleButton, MenuButton and BreadcrumbButton also carry `fui-Button`); the doubled class beats the atomic classes; nothing applies above 480px; `useIsPhoneWidth` reads the query on first render and cleans up; the assistant starts collapsed, and its collapse control is always shown, so the workspace is never unreachable; RecordCommandBar is unchanged.

Fixed:
- **F1:** the header's account button showed the display name, which can be an email address, in a non-shrinking group, so a long name could scroll the page at 375px in a browser (Teams hides this header). At phone width the button is now icon-only. Test: the name carries the phone `display: none` rule and never an unconditional one.
- **F2:** the only test of the app-wide touch-target rule compared the CSS constant with itself. Griffel makes `makeStaticStyles` a no-op under NODE_ENV=test, so the hook cannot be observed by rendering. The test now compiles the CSS through Griffel core (`@griffel/core`, a transitive dependency of Fluent) into a real stylesheet and checks the media query, the doubled button class and the 44px rule. Source pins check that the hook is built from that CSS and that `AppShell` calls it.

Known limits (K), added to the ones above:
- The real record sections (Documents, Messages, Invoices, Events, Information) and the widget tab bodies were not measured at 375px; the harness and tests used stand-in sections. Information keeps its label/value grid, so a long unbreakable value may scroll inside the modal body. **Task 058 must measure them on dev.**
- The static rule covers Button, ToggleButton, MenuItem, Tab, table rows, Input and Select. It does not cover Checkbox, Radio, Switch, Link, Dropdown or Combobox; the Quick Start intake modal uses Checkbox and Radio.
- At phone width, Delete, Change status and History open their dialogs from the More actions menu. No test covers focus on that path. The shared bar leaves a child dialog's focus alone, so the risk is low.
- While the assistant is open on a phone, a tab it switches is not visible until the assistant is collapsed.
- `minWidth: 0` on the tab strip and `minHeight: 0` on main apply at every width; the review traced both and found no desktop change.
