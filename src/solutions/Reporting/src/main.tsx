/**
 * Reporting Code Page - React Entry Point
 *
 * Bootstraps runtime config from Dataverse Environment Variables before
 * rendering the React application for the Power BI Embedded Reporting module.
 *
 * Bootstrap sequence:
 *   1. resolveRuntimeConfig() — queries Dataverse environment variables
 *      for BFF URL, OAuth scope, MSAL client ID
 *   2. setRuntimeConfig() — stores resolved values in singleton + window globals
 *   3. ensureAuthInitialized() — warms MSAL token cache, resolves tenant ID
 *   4. createRoot().render() — mount React app after config + auth are ready
 *
 * Note: This is a standalone web resource (not a PCF control), so it uses
 * React 19 which includes native useId() support required by Fluent UI v9.
 *
 * @see ADR-006 - Code Pages for full-page standalone surfaces (not PCF)
 * @see ADR-022 - Code Pages bundle their own React 19 (not platform-provided)
 * @see ADR-026 - Vite + vite-plugin-singlefile build standard
 * @see .claude/patterns/auth/spaarke-auth-initialization.md
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import { resolveRuntimeConfig } from "@spaarke/auth";
import { setRuntimeConfig } from "./config/runtimeConfig";
import { ensureAuthInitialized } from "./services/authInit";
import { initBodyTheme, SURFACE_BG, SURFACE_FG, SURFACE_FG3 } from "./styles/globalStyles";
import { App } from "./App";

// Apply the correct body background immediately — before any async work —
// so the initial paint matches the active Fluent v9 theme (light or dark).
// This prevents the white-body FOUC in dark mode when the page first loads.
// The returned cleanup function removes the theme-change listener on page unload.
const cleanupBodyTheme = initBodyTheme();
window.addEventListener("unload", cleanupBodyTheme, { once: true });

async function bootstrap(): Promise<void> {
  // 1. Resolve runtime config from Dataverse Environment Variables
  const config = await resolveRuntimeConfig();

  // 2. Store in singleton — getBffBaseUrl() / getMsalClientId() now work
  setRuntimeConfig(config);

  // 3. Initialize MSAL — warm token cache, resolve tenant ID
  try {
    await ensureAuthInitialized();
  } catch (err) {
    // Auth init failure is non-fatal for rendering — authenticated API calls
    // will fail individually and surface errors in the relevant components.
    console.warn("[Reporting] Auth init failed, will retry on first use:", err);
  }

  // 4. Render AFTER config + auth are ready
  const rootElement = document.getElementById("root");
  if (!rootElement) {
    console.error("[Reporting] Root element not found");
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
  console.error("[Reporting] Bootstrap failed:", err);

  // Show a user-friendly error in the DOM.
  // Body background is already set by initBodyTheme() above, so the error
  // page matches the active theme. Use SURFACE_* token values directly since
  // FluentProvider has not mounted and CSS custom properties are not available.
  const rootElement = document.getElementById("root");
  if (rootElement) {
    // Detect whether dark mode is active to pick the right token values
    const isDark =
      document.body.style.backgroundColor === SURFACE_BG.dark;
    const bgColor = isDark ? SURFACE_BG.dark : SURFACE_BG.light;
    const fgColor = isDark ? SURFACE_FG.dark : SURFACE_FG.light;
    const subtleFg = isDark ? SURFACE_FG3.dark : SURFACE_FG3.light;

    rootElement.innerHTML = `
      <div style="padding: 40px; text-align: center; background-color: ${bgColor}; color: ${fgColor}; height: 100%; box-sizing: border-box; font-family: 'Segoe UI', Segoe UI, system-ui, sans-serif;">
        <h2 style="margin-bottom: 12px;">Configuration Error</h2>
        <p style="margin-bottom: 8px;">Unable to load Reporting configuration from Dataverse environment variables.</p>
        <p style="color: ${subtleFg}; font-size: 12px;">${err instanceof Error ? err.message : String(err)}</p>
      </div>
    `;
  }
});
