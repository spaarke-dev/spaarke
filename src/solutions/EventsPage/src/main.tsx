/**
 * Events Custom Page - React Entry Point
 *
 * Mounts the React application for the Events Custom Page.
 * This file is loaded by index.html and bootstraps the App component.
 *
 * This page replaces the OOB Events entity main view and combines
 * the EventCalendarFilter and EventDataGrid components.
 *
 * Note: This is a standalone web resource (not a PCF control), so it uses
 * React 18 which includes native useId() support required by Fluent UI v9.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/060-events-custompage-scaffolding.poml
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
  console.error("[EventsPage] Root element not found");
}
