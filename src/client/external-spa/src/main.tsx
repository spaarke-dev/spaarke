import * as React from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";

// Note: This is a Power Pages Code Page SPA — not a PCF control.
// It uses React 18 with createRoot (bundled, not platform-provided).
// See ADR-022 for the Code Page React 18 standard.
// See ADR-026 for the full-page Code Page build pattern (viteSingleFile).

const rootElement = document.getElementById("root");

if (rootElement) {
  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
} else {
  console.error("[SecureProjectWorkspace] Root element not found");
}
