/**
 * CalendarSidePane - React Entry Point
 *
 * Mounts the React application for the web resource.
 * This file is loaded by index.html and bootstraps the App component.
 *
 * Note: This is a standalone web resource (not a PCF control), so it uses
 * React 18 which includes native useId() support required by Fluent UI v9.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/096-calendar-sidepane-webresource.poml
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";

// Mount React application to #root element
const rootElement = document.getElementById("root");

if (rootElement) {
  // React 18 createRoot API
  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
} else {
  console.error("[CalendarSidePane] Root element not found");
}
