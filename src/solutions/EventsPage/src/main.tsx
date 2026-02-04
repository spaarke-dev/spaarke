/**
 * Events Custom Page - React Entry Point
 *
 * Mounts the React application for the Events Custom Page.
 * This file is loaded by index.html and bootstraps the App component.
 *
 * This page replaces the OOB Events entity main view and combines
 * the EventCalendarFilter and UniversalDatasetGrid components.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/060-events-custompage-scaffolding.poml
 */

import * as React from "react";
import * as ReactDOM from "react-dom";
import { App } from "./App";

// Mount React application to #root element
const rootElement = document.getElementById("root");

if (rootElement) {
  // React 16 render API (per ADR-022: PCF Platform Libraries)
  ReactDOM.render(
    <React.StrictMode>
      <App />
    </React.StrictMode>,
    rootElement
  );
} else {
  console.error("[EventsPage] Root element not found");
}
