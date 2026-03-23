/**
 * LegalWorkspace entry point — bootstraps runtime config from Dataverse
 * Environment Variables before rendering the React app.
 *
 * Bootstrap sequence:
 *   1. Call resolveRuntimeConfig() from @spaarke/auth (queries Dataverse
 *      environment variables for BFF URL, OAuth scope, MSAL client ID)
 *   2. Store resolved values in runtimeConfig singleton + window globals
 *   3. Eagerly initialize MSAL auth to ensure tenant ID is available
 *      synchronously before the first user interaction
 *   4. Render <App /> via React 18 createRoot
 *
 * @see ADR-006 - Code Pages for standalone dialogs (not PCF)
 * @see ADR-022 - Code pages bundle their own React 18 (not platform-provided)
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import { resolveRuntimeConfig, getAuthProvider } from "@spaarke/auth";
import { setRuntimeConfig } from "./config/runtimeConfig";
import { ensureAuthInitialized } from "./services/authInit";
import { App } from "./App";

async function bootstrap(): Promise<void> {
  // 1. Resolve runtime config from Dataverse Environment Variables
  const config = await resolveRuntimeConfig();
  setRuntimeConfig(config);

  // 2. Eagerly initialize MSAL auth before rendering.
  //
  //    Why: resolveRuntimeConfig() reads organizationSettings.tenantId from Xrm,
  //    but this property is empty on first page load (Dataverse initializes it
  //    asynchronously after the web resource starts). Without eagerly initializing
  //    auth, resolveTenantIdSync() has no reliable source — the MSAL authority is
  //    'organizations' (not tenant-specific) and the Xrm walk also returns empty.
  //    This causes "missing tenant ID" errors in Find Similar and other components
  //    that need a tenant ID synchronously from click handlers.
  //
  //    After ensureAuthInitialized(), MSAL has authenticated and getAllAccounts()[0]
  //    contains the real tenant ID extracted from the JWT — always correct.
  try {
    await ensureAuthInitialized();

    // Patch the stored config if tenant ID was empty at resolveRuntimeConfig() time.
    // MSAL account.tenantId (from the authenticated JWT) is the authoritative source.
    if (!config.tenantId) {
      const tenantId = await getAuthProvider().getTenantId();
      if (tenantId) {
        setRuntimeConfig({ ...config, tenantId });
        console.info('[LegalWorkspace] Tenant ID resolved from MSAL account:', tenantId.substring(0, 8) + '...');
      }
    }
  } catch (err) {
    // Auth init failure is non-fatal for rendering — authenticated API calls will
    // fail individually and surface errors in the relevant components.
    console.warn('[LegalWorkspace] Eager auth init failed, will retry on first use:', err);
  }

  // 3. Render the app
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
