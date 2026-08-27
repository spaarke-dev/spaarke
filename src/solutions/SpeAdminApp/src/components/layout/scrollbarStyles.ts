/**
 * scrollbarStyles — modern thin scrollbars for every scrollable region in the SPE Admin App.
 *
 * Added 2026-08-26 (UAT round 7: "container scroll bars use the modern thin style type").
 *
 * ## What this does
 *
 * Fluent UI v9 does NOT style scrollbars — it leaves them to the platform, so a Dataverse-hosted
 * code page inherits the host OS's default chrome. On Windows that is the wide classic bar with
 * stepper arrows, which looks a decade older than everything around it.
 *
 * Two mechanisms are needed for full coverage, and they are not interchangeable:
 *
 *   - `scrollbar-width` / `scrollbar-color` — the CSS standard. Firefox, and Chromium 121+.
 *   - `::-webkit-scrollbar-*` — the legacy pseudo-elements. Safari, and Chromium before 121.
 *     Chromium ignores the standard properties when these are present, so the two must agree
 *     or the bar changes appearance depending on the browser version.
 *
 * Colours come from Fluent tokens, which are CSS custom properties set by `FluentProvider` on its
 * root element. They therefore resolve correctly for any scrollable element inside the provider —
 * including in dark mode, with no second rule.
 *
 * ## Scope, and why this is not in the shared library
 *
 * `makeStaticStyles` emits GLOBAL CSS, so this is applied once, in `App.tsx`, and reaches every
 * scroll container in the app.
 *
 * It is deliberately local to `SpeAdminApp` rather than in `@spaarke/ui-components`. That package
 * is consumed by roughly sixteen solutions and every PCF control; a global scrollbar rule shipped
 * there would silently restyle all of them without any of them being UAT'd. Promoting it is a
 * two-line move — export `useThinScrollbars` from the library barrel and call it in each host's
 * root — and should be a deliberate decision with its own test pass, not a side effect of this
 * one.
 *
 * Documented for that reason in `docs/standards/UI-SCROLLBARS.md`.
 *
 * ## Usage
 *
 *   export const App = () => {
 *     useThinScrollbars();   // once, at the root, inside FluentProvider's subtree
 *     …
 *   };
 *
 * ADR-021: Fluent design tokens only — no hard-coded colours.
 */

import { makeStaticStyles, tokens } from "@fluentui/react-components";

/**
 * Applies thin scrollbar styling globally. Call once from the app root.
 *
 * The `transparent` track is intentional: it lets the bar sit over whatever surface it belongs to
 * rather than cutting a grey channel through panes and grids.
 */
export const useThinScrollbars = makeStaticStyles({
  // ── CSS standard (Firefox, Chromium 121+) ──
  "*": {
    scrollbarWidth: "thin",
    scrollbarColor: `${tokens.colorNeutralStroke1} transparent`,
  },

  // ── WebKit pseudo-elements (Safari, older Chromium) ──
  "::-webkit-scrollbar": {
    width: "8px",
    height: "8px",
  },
  "::-webkit-scrollbar-track": {
    background: "transparent",
  },
  "::-webkit-scrollbar-thumb": {
    backgroundColor: tokens.colorNeutralStroke1,
    borderRadius: "4px",
  },
  "::-webkit-scrollbar-thumb:hover": {
    backgroundColor: tokens.colorNeutralStroke1Hover,
  },
  // Suppresses the stepper arrows and the square where the two bars meet — both are classic
  // Windows chrome with no modern equivalent.
  "::-webkit-scrollbar-button": {
    display: "none",
  },
  "::-webkit-scrollbar-corner": {
    background: "transparent",
  },
});
