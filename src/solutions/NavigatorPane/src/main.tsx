/**
 * NavigatorPane - React Entry Point
 *
 * Mounts the React application for the webresource pane (ADR-006 Path C —
 * a Vite code page hosted as a `pageType:'webresource'` pane, mirroring
 * CalendarSidePane/EventDetailSidePane; NOT a classic HTML web resource,
 * NOT a per-form handler).
 *
 * The docked Navigator pane is created by the Path B app-startup bootstrap
 * (`sprk_SidePaneManager`, task 086), which `createPane`s pane id
 * `sprk-navigator` and `navigate`s it to THIS bundle's webresource. This entry
 * therefore mounts the pane CONTENT — a root `FluentProvider` + `NavigatorBody`
 * via `App` (mirroring CalendarSidePane) — NOT the multi-contributor host.
 * (Task 086 UAT: mounting the full `SprkSidePaneHost` here blanked the live
 * pane; see App.tsx for the rationale.)
 *
 * Note: this is a standalone web resource (not a PCF control), so it uses
 * React 19's `createRoot` (ADR-006 / ADR-021 Code Page rules).
 *
 * @see App.tsx — the root FluentProvider + NavigatorBody
 * @see NavigatorBody.tsx — the pane body
 * @see src/solutions/CalendarSidePane/src/main.tsx — the pattern this mirrors
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";

// Mount React application to #root element
const rootElement = document.getElementById("root");

if (rootElement) {
  // React 19 createRoot API
  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
} else {
  console.error("[NavigatorPane] Root element not found");
}
