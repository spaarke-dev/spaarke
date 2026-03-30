/**
 * App — Root shell component for SmartTodo Code Page.
 *
 * Wraps the application in FluentProvider with theme detection via the
 * 3-level cascade (localStorage > URL flags > navbar DOM), and listens
 * for theme changes to stay in sync with the Spaarke theme system.
 *
 * Renders SmartTodoApp which provides the two-panel Kanban + Detail layout
 * wrapped in TodoProvider for shared state management.
 */

import * as React from "react";
import { FluentProvider } from "@fluentui/react-components";
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
} from "@spaarke/ui-components/utils";
import { SmartTodoApp } from "./SmartTodoApp";

export function App() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);

  React.useEffect(() => {
    const cleanup = setupCodePageThemeListener(setTheme);
    return cleanup;
  }, []);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <SmartTodoApp />
    </FluentProvider>
  );
}
