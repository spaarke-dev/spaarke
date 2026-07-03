/**
 * App — Root shell component for Notepad Code Page.
 *
 * Wraps the application in FluentProvider with theme detection via the
 * 3-level cascade (localStorage > URL flags > navbar DOM), and listens
 * for theme changes to stay in sync with the Spaarke theme system.
 *
 * v1 (task 030): Renders a placeholder. Task 037 replaces this with
 * NotepadShell (useLaunchContext + useSprkMemoRepository + MemoList +
 * MemoEditor + CreatedByPopover).
 */

import * as React from "react";
import { FluentProvider } from "@fluentui/react-components";
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
} from "@spaarke/ui-components/utils";

export function App() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);

  React.useEffect(() => {
    const cleanup = setupCodePageThemeListener(setTheme);
    return cleanup;
  }, []);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <div>
        <h1>Notepad</h1>
        <p>Placeholder — task 037 will land NotepadShell.</p>
      </div>
    </FluentProvider>
  );
}
