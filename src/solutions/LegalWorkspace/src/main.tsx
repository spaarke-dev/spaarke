import * as React from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";

// Note: This is a standalone web resource (not a PCF control), so it uses
// React 18 which includes native useId() support required by Fluent UI v9.
// See ADR-026 for the full-page Custom Page standard.

const rootElement = document.getElementById("root");

if (rootElement) {
  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
} else {
  console.error("[LegalWorkspace] Root element not found");
}
