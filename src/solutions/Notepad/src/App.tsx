/**
 * App — Root shell component for Notepad Code Page.
 *
 * Wraps the application in FluentProvider with theme detection via the
 * 3-level cascade (localStorage > URL flags > navbar DOM), and listens
 * for theme changes to stay in sync with the Spaarke theme system.
 *
 * The theme-cascade wrappers (task 030) are preserved above `NotepadShell`
 * so dark-mode compliance per ADR-021 works out of the box.
 *
 * Task 037: Replaced the placeholder with `<NotepadShell />`, which owns
 * the launch-context + repository + memo-list + editor + popover wiring.
 */

import * as React from "react";
import { FluentProvider } from "@fluentui/react-components";
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
} from "@spaarke/ui-components/utils/themeStorage";
import { NotepadShell } from "./components/NotepadShell";

export function App() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);

  React.useEffect(() => {
    const cleanup = setupCodePageThemeListener(setTheme);
    return cleanup;
  }, []);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <NotepadShell />
    </FluentProvider>
  );
}
