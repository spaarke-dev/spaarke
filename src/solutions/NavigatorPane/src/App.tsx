/**
 * NavigatorPane - Root Application Component
 *
 * Mirrors the proven CalendarSidePane/EventDetailSidePane pattern
 * (src/solutions/CalendarSidePane/src/App.tsx): a SIMPLE root `FluentProvider`
 * wrapping the single pane body, with theme + `--sprk-ui-scale` resolution.
 *
 * WHY NOT SprkSidePaneHost here (task 086 UAT fix, 2026-08-13): the deployed
 * Navigator pane is a SINGLE contributor. Mounting the full multi-contributor
 * host (`SprkSidePaneHost`) inside the pane pulled in the pane-lifecycle
 * orchestrator, async lazy contributor resolution, and a one-icon rail with no
 * benefit — and produced a blank pane in the live UCI (while rendering fine in
 * headless/test). CalendarSidePane — the canonical working side-pane code page
 * — mounts its section body directly under a root `FluentProvider`; this
 * component does the same with `NavigatorBody`, which was authored from the
 * start to be mounted directly (see its docblock). The framework host +
 * registry (`SprkSidePaneHost` / `sidePaneRegistry`, task 011) remain in
 * `@spaarke/ui-components`, unit-proven by tasks 011 + 085 (FR-13); they are
 * simply not needed to host a single contributor in its own pane webresource.
 *
 * ADR-021: Fluent v9 tokens only; light/dark via `resolveCodePageTheme` +
 * `setupCodePageThemeListener`; `--sprk-ui-scale` via `useUiScale`/`scaleTheme`
 * (NFR-07); `applyStylesToPortals` so `NavigatorBody`'s portalled surfaces
 * (Tooltip, QuickSwitcher results) inherit the theme.
 *
 * @see src/solutions/CalendarSidePane/src/App.tsx (the pattern this mirrors)
 * @see NavigatorBody.tsx (the pane body, designed for direct mount)
 */

import * as React from "react";
import { FluentProvider } from "@fluentui/react-components";
import {
  resolveCodePageTheme,
  scaleTheme,
  setupCodePageThemeListener,
  useUiScale,
} from "@spaarke/ui-components";
import { NavigatorBody } from "./NavigatorBody";

// Matches the Path B bootstrap's createPane paneId (sprk_SidePaneManager.js).
const PANE_ID = "sprk-navigator";

export const App: React.FC = () => {
  const [baseTheme, setBaseTheme] = React.useState(resolveCodePageTheme);
  const { uiScale } = useUiScale();
  const theme = React.useMemo(() => scaleTheme(baseTheme, uiScale), [baseTheme, uiScale]);

  React.useEffect(() => setupCodePageThemeListener(setBaseTheme), []);

  React.useEffect(() => {
    document.documentElement.style.setProperty("--sprk-ui-scale", String(uiScale));
  }, [uiScale]);

  return (
    <FluentProvider theme={theme} applyStylesToPortals={true} style={{ height: "100%" }}>
      <NavigatorBody paneId={PANE_ID} />
    </FluentProvider>
  );
};

export default App;
