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

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;

/**
 * Walk the frame hierarchy to find the Xrm global object.
 * Web resources run inside iframes — Xrm may be on a parent or top window.
 */
function getXrm(): any | null {
  // 1. Current window (direct embedding)
  if (typeof Xrm !== "undefined" && Xrm?.WebApi) return Xrm;
  // 2. Parent window (iframe in Custom Page)
  try {
    const p = (window.parent as any)?.Xrm;
    if (p?.WebApi) return p;
  } catch {
    /* cross-origin */
  }
  // 3. Top window (nested iframes)
  try {
    const t = (window.top as any)?.Xrm;
    if (t?.WebApi) return t;
  } catch {
    /* cross-origin */
  }
  return null;
}

/** Wizard mode determines the wizard behavior */
type WizardMode = "create" | "edit" | "saveAs";

/** Parsed data parameters from the URL / Xrm.Page.data */
interface DataParams {
  mode: WizardMode;
  layoutId: string | null;
  /** Template ID from the source layout (saveAs mode) */
  layoutTemplateId: string | null;
  /** JSON-encoded sections from the source layout (saveAs mode) */
  sectionsJson: string | null;
  /** Display name of the source layout (saveAs mode) */
  sourceName: string | null;
}

/**
 * Parse the URL data parameter passed via Xrm.Navigation.navigateTo.
 * Expected format: "mode=create" or "mode=edit&layoutId=<guid>"
 * SaveAs format: "mode=saveAs&layoutId=<guid>&layoutTemplateId=<id>&sectionsJson=<json>&name=<name>"
 */
function parseDataParams(): DataParams {
  let dataString = "";

  // Try Xrm context first (Dataverse runtime)
  const xrm = getXrm();
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

  const parsed = new URLSearchParams(dataString);
  const modeParam = parsed.get("mode");
  const mode: WizardMode =
    modeParam === "edit" || modeParam === "saveAs" ? modeParam : "create";
  const layoutId = parsed.get("layoutId") || null;
  const layoutTemplateId = parsed.get("layoutTemplateId") || null;
  const sectionsJson = parsed.get("sectionsJson") || null;
  const sourceName = parsed.get("name") || null;

  return { mode, layoutId, layoutTemplateId, sectionsJson, sourceName };
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
      <Root />
    </React.StrictMode>
  );
} else {
  console.error("[WorkspaceLayoutWizard] Root element not found");
}

// Export getXrm for use by other modules
export { getXrm };
export type { WizardMode };
