/**
 * App — Root shell component for SmartTodo Code Page.
 *
 * Wraps the application in FluentProvider with theme detection via the
 * 3-level cascade (localStorage > URL flags > navbar DOM), and listens
 * for theme changes to stay in sync with the Spaarke theme system.
 *
 * The inner content is a placeholder that will be replaced in task 030
 * with the full Kanban board layout.
 */

import * as React from "react";
import { FluentProvider } from "@fluentui/react-components";
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
} from "@spaarke/ui-components";

export function App() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);

  React.useEffect(() => {
    const cleanup = setupCodePageThemeListener(setTheme);
    return cleanup;
  }, []);

  return (
    <FluentProvider theme={theme}>
      <div style={{ padding: 16 }}>SmartTodo App</div>
    </FluentProvider>
  );
}
