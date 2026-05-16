/**
 * SpaarkeAi Code Page — entry point.
 *
 * Bootstrap sequence (follows LegalWorkspace canonical pattern):
 *   1. Call resolveRuntimeConfig() from @spaarke/auth — reads Dataverse environment
 *      variables for BFF URL, OAuth scope, MSAL client ID, tenant ID
 *   2. Store resolved values via setRuntimeConfig() (sets window globals for auth lib)
 *   3. Eagerly initialize MSAL auth to ensure tenant ID is available synchronously
 *      before first user interaction (avoids "missing tenant ID" errors in AI calls)
 *   4. Render <App /> via React 19 createRoot with FluentProvider + theme detection
 *
 * Web resource: sprk_spaarkeai
 *
 * @see ADR-006 - Code Pages for standalone dialogs (not PCF)
 * @see ADR-021 - Fluent v9, dark mode required
 * @see ADR-022 - Code Pages bundle their own React 19 (not platform-provided)
 * @see ADR-026 - Vite + vite-plugin-singlefile for Code Pages
 * @see src/solutions/LegalWorkspace/src/main.tsx — canonical bootstrap pattern
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import { resolveRuntimeConfig, getAuthProvider } from "@spaarke/auth";
import { setRuntimeConfig } from "./config/runtimeConfig";
import { ensureAuthInitialized } from "./services/authInit";
import { App } from "./App";

async function bootstrap(): Promise<void> {
  const t0 = performance.now();

  // 1. Resolve runtime config from Dataverse Environment Variables
  const config = await resolveRuntimeConfig();
  setRuntimeConfig(config);

  // 2. Eagerly initialize MSAL auth before rendering.
  //
  //    Why: resolveRuntimeConfig() reads organizationSettings.tenantId from Xrm,
  //    but this property is empty on first page load (Dataverse initializes it
  //    asynchronously after the web resource starts). Without eagerly initializing
  //    auth, resolveTenantIdSync() has no reliable source — MSAL authority is
  //    'organizations' (not tenant-specific) and Xrm walk also returns empty.
  //    This causes "missing tenant ID" errors in AI calls that need tenant ID sync.
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
        console.info(
          "[SpaarkeAi] Tenant ID resolved from MSAL account:",
          tenantId.substring(0, 8) + "..."
        );
      }
    }
  } catch (err) {
    // Auth init failure is non-fatal for rendering — authenticated API calls will
    // fail individually and surface errors in the relevant components.
    console.warn("[SpaarkeAi] Eager auth init failed, will retry on first use:", err);
  }

  // 3. Render the app
  const rootElement = document.getElementById("root");
  if (!rootElement) {
    console.error("[SpaarkeAi] Root element not found");
    return;
  }

  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );

  const bootstrapMs = Math.round(performance.now() - t0);
  console.info(`[SpaarkeAi] Bootstrap complete in ${bootstrapMs}ms`);
}

bootstrap().catch((err) => {
  console.error("[SpaarkeAi] Bootstrap failed:", err);

  // Show a user-friendly error in the DOM
  const rootElement = document.getElementById("root");
  if (rootElement) {
    rootElement.innerHTML = `
      <div style="padding: 40px; text-align: center; font-family: 'Segoe UI', sans-serif;">
        <h2>Configuration Error</h2>
        <p>Unable to load SpaarkeAi configuration from Dataverse environment variables.</p>
        <p style="color: var(--colorNeutralForeground3); font-size: 12px;">${err instanceof Error ? err.message : String(err)}</p>
      </div>
    `;
  }
});
