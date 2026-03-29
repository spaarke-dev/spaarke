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

/**
 * Parse the URL data parameter passed via Xrm.Navigation.navigateTo.
 * Expected format: "mode=create" or "mode=edit&layoutId=<guid>"
 */
function parseDataParams(): { mode: WizardMode; layoutId: string | null } {
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

  return { mode, layoutId };
}

// Mount React application to #root element
const rootElement = document.getElementById("root");

if (rootElement) {
  const { mode, layoutId } = parseDataParams();

  // React 19 createRoot API
  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <App mode={mode} layoutId={layoutId} />
    </React.StrictMode>
  );
} else {
  console.error("[WorkspaceLayoutWizard] Root element not found");
}

// Export getXrm for use by other modules
export { getXrm };
export type { WizardMode };
