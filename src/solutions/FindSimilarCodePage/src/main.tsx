/**
 * Find Similar Documents Dialog - Code Page Entry Point
 *
 * Single-step dialog with two mutually exclusive paths:
 *   Path A: Select existing Document via Xrm lookup → open DocumentRelationshipViewer
 *   Path B: Upload file → BFF extracts/embeds → temp AI Search entry → open viewer
 *
 * Opened via Xrm.Navigation.navigateTo with pageType: "webresource".
 * Web resource name: sprk_findsimilar
 *
 * Auth: @spaarke/auth with resolveRuntimeConfig() → initAuth() pattern.
 * Tenant ID: JWT tid claim (bridge token compatible).
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider, Spinner } from "@fluentui/react-components";
import { resolveCodePageTheme, setupCodePageThemeListener } from "@spaarke/ui-components";
import { resolveRuntimeConfig, initAuth, getAuthProvider, authenticatedFetch } from "@spaarke/auth";
import { FindSimilarApp } from "./App";

const container = document.getElementById("root");
if (!container) throw new Error("[FindSimilar] Root container #root not found in DOM.");

const root = createRoot(container);

// Render loading state while bootstrapping
let currentTheme = resolveCodePageTheme();
root.render(
  <FluentProvider theme={currentTheme} style={{ height: "100%" }}>
    <div style={{ display: "flex", justifyContent: "center", alignItems: "center", height: "100%" }}>
      <Spinner label="Initializing..." />
    </div>
  </FluentProvider>
);

/**
 * Bootstrap sequence:
 *   1. Resolve runtime config from Dataverse Environment Variables
 *   2. Initialize MSAL auth with resolved config
 *   3. Resolve tenantId from auth
 *   4. Render App with apiBaseUrl and tenantId
 */
async function bootstrap(): Promise<void> {
  // 1. Resolve runtime config (BFF URL, MSAL client ID, OAuth scope)
  const runtimeConfig = await resolveRuntimeConfig();

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).__SPAARKE_MSAL_CLIENT_ID__ = runtimeConfig.msalClientId;

  // 2. Initialize auth
  await initAuth({
    clientId: runtimeConfig.msalClientId,
    bffApiScope: runtimeConfig.bffOAuthScope,
    bffBaseUrl: runtimeConfig.bffBaseUrl,
  });

  // 3. Resolve tenantId
  let tenantId = "";
  try {
    tenantId = await getAuthProvider().getTenantId();
  } catch (err) {
    console.warn("[FindSimilar] Could not resolve tenantId:", err);
  }

  // 4. Render
  root.render(
    <FluentProvider theme={currentTheme} style={{ height: "100%" }}>
      <FindSimilarApp
        apiBaseUrl={runtimeConfig.bffBaseUrl}
        tenantId={tenantId}
        authenticatedFetch={authenticatedFetch}
      />
    </FluentProvider>
  );
}

// Listen for theme changes
setupCodePageThemeListener(() => {
  currentTheme = resolveCodePageTheme();
});

bootstrap().catch((err) => {
  console.error("[FindSimilar] Bootstrap failed:", err);
  root.render(
    <FluentProvider theme={currentTheme} style={{ height: "100%" }}>
      <div style={{ padding: 24, color: "red" }}>
        Failed to initialize. Please close and try again.
      </div>
    </FluentProvider>
  );
});
