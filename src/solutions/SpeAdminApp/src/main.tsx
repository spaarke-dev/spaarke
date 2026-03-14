import * as React from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";

// Note: This is a standalone web resource (not a PCF control), so it uses
// React 18 which includes native useId() support required by Fluent UI v9.
// See ADR-022 for the Code Page React 18 standard.
// See ADR-006 for the Code Page architecture standard.

const rootElement = document.getElementById("root");

if (rootElement) {
  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
} else {
  console.error("[SpeAdminApp] Root element not found");
}
