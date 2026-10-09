/**
 * Workspace Layout Wizard - React Entry Point
 *
 * Mounts the React application for the Workspace Layout Wizard dialog.
 * This file is loaded by index.html and bootstraps the App component.
 *
 * The wizard is opened via Xrm.Navigation.navigateTo as a webresource dialog.
 * URL data parameter carries the wizard mode (create/edit/saveAs) and optional layoutId.
 *
 * Note: This is a standalone web resource (not a PCF control), so it uses
 * React 19 which includes native useId() support required by Fluent UI v9.
 * See ADR-026 for the full-page Custom Page standard.
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { resolveRuntimeConfig, initAuth, authenticatedFetch } from "@spaarke/auth";
import { AppErrorBoundary, getXrm } from "@spaarke/ui-components";
import { parseLayoutWizardData } from "./launchParams";
import type { DataParams, WizardMode } from "./launchParams";

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Resolve the launch data string passed via Xrm.Navigation.navigateTo, then parse it with the
 * shared contract (`launchParams.ts`, also used by the in-app host).
 */
function parseDataParams(): DataParams {
  let dataString = "";

  // Try Xrm context first (Dataverse runtime).
  // Shared cross-frame walker (task 081 / C-8); `any` view because XrmContext.Page lacks `data`.
  const xrm: any = getXrm('page');
  if (xrm?.Page?.data) {
    try {
      dataString = xrm.Page.data || "";
    } catch {
      /* not available */
    }
  }

  // Fallback: parse from URL search params (dev server / direct navigation)
  if (!dataString) {
    const params = new URLSearchParams(window.location.search);
    dataString = params.get("data") || params.toString();
  }

  return parseLayoutWizardData(dataString);
}

/**
 * Root wrapper that initializes auth before rendering the wizard.
 * Follows the same pattern as CreateEventWizard/main.tsx.
 */
function Root() {
  const dataParams = React.useMemo(() => parseDataParams(), []);
  const [isAuthReady, setIsAuthReady] = React.useState(false);

  React.useEffect(() => {
    let cancelled = false;
    async function initialize(): Promise<void> {
      try {
        const config = await resolveRuntimeConfig();
        await initAuth({
          clientId: config.msalClientId,
          bffBaseUrl: config.bffBaseUrl,
          bffApiScope: config.bffOAuthScope,
          tenantId: config.tenantId || undefined,
          proactiveRefresh: true,
          // ai-spaarke-ai-workspace-UI-r1 #5 (2026-06-08):
          // The wizard is launched via `Xrm.Navigation.navigateTo({ target: 2 })`
          // (popup window) whose MSAL cache is isolated from the SpaarkeAi
          // host window. On first open the silent paths
          // (acquireTokenSilent, ssoSilent) frequently fail because the
          // popup has no cached accounts; the default fallback to
          // acquireTokenPopup surfaces an involuntary AAD sign-in. ADR-028
          // INV-5 forbids popups except on explicit user action. Setting
          // requireSilentOnly accepts a degraded steady state (Save may 401
          // once and rely on authenticatedFetch's retry) in exchange for no
          // surprise sign-in dialogs.
          requireSilentOnly: true,
        });
        if (!cancelled) {
          setIsAuthReady(true);
        }
      } catch (err) {
        console.error("[WorkspaceLayoutWizard] Failed to initialize auth:", err);
        // Still render the wizard — save will fail with an auth error if needed
        if (!cancelled) setIsAuthReady(true);
      }
    }
    void initialize();
    return () => { cancelled = true; };
  }, []);

  if (!isAuthReady) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%" }}>
        <span>Initializing...</span>
      </div>
    );
  }

  return (
    <App
      mode={dataParams.mode}
      layoutId={dataParams.layoutId}
      layoutTemplateId={dataParams.layoutTemplateId}
      sectionsJson={dataParams.sectionsJson}
      sourceName={dataParams.sourceName}
      authenticatedFetch={authenticatedFetch}
      templateFilter={dataParams.templateFilter}
      startAtStep={dataParams.startAtStep ?? undefined}
    />
  );
}

// Mount React application to #root element
const rootElement = document.getElementById("root");

if (rootElement) {
  // React 19 createRoot API
  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <AppErrorBoundary surfaceName="Workspace Layout Wizard">
        <Root />
      </AppErrorBoundary>
    </React.StrictMode>
  );
} else {
  console.error("[WorkspaceLayoutWizard] Root element not found");
}

export type { WizardMode };
