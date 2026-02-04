/**
 * EventDetailSidePane - React Entry Point
 *
 * Mounts the React application for the Custom Page.
 * This file is loaded by index.html and bootstraps the App component.
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/030-scaffold-eventdetailsidepane.poml
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
  console.error("[EventDetailSidePane] Root element not found");
}
