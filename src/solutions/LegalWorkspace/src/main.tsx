/**
 * LegalWorkspace entry point — bootstraps runtime config from Dataverse
 * Environment Variables before rendering the React app.
 *
 * Bootstrap sequence:
 *   1. Call resolveRuntimeConfig() from @spaarke/auth (queries Dataverse
 *      environment variables for BFF URL, OAuth scope, MSAL client ID)
 *   2. Store resolved values in runtimeConfig singleton + window globals
 *   3. Render <App /> via React 18 createRoot
 *
 * @see ADR-006 - Code Pages for standalone dialogs (not PCF)
 * @see ADR-022 - Code pages bundle their own React 18 (not platform-provided)
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import { resolveRuntimeConfig } from "@spaarke/auth";
import { setRuntimeConfig } from "./config/runtimeConfig";
import { App } from "./App";

async function bootstrap(): Promise<void> {
  // 1. Resolve runtime config from Dataverse Environment Variables
  const config = await resolveRuntimeConfig();
  setRuntimeConfig(config);

  // 2. Render the app
  const rootElement = document.getElementById("root");
  if (!rootElement) {
    console.error("[LegalWorkspace] Root element not found");
    return;
  }

  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
}

bootstrap().catch((err) => {
  console.error("[LegalWorkspace] Bootstrap failed:", err);

  // Show a user-friendly error in the DOM
  const rootElement = document.getElementById("root");
  if (rootElement) {
    rootElement.innerHTML = `
      <div style="padding: 40px; text-align: center; font-family: 'Segoe UI', sans-serif;">
        <h2>Configuration Error</h2>
        <p>Unable to load workspace configuration from Dataverse environment variables.</p>
        <p style="color: #999; font-size: 12px;">${err instanceof Error ? err.message : String(err)}</p>
      </div>
    `;
  }
});
