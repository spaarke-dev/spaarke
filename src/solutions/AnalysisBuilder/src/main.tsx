/**
 * Analysis Builder - Code Page Entry Point
 *
 * Thin wrapper that mounts the App component inside a FluentProvider with
 * Code Page theme resolution. Uses React 18 createRoot API.
 *
 * Web resource name: sprk_analysisbuilder
 * Display name: Analysis Builder
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { resolveCodePageTheme, setupCodePageThemeListener } from "@spaarke/ui-components/utils/codePageTheme";
import { App } from "./App";

function Root() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <App />
    </FluentProvider>
  );
}

const rootElement = document.getElementById("root");

if (rootElement) {
  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <Root />
    </React.StrictMode>
  );
} else {
  console.error("[AnalysisBuilder] Root element not found");
}
