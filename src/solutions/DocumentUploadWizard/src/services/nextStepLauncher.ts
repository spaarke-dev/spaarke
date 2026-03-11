/**
 * nextStepLauncher.ts
 * Utility for launching post-wizard "next step" actions from the success screen.
 *
 * Launcher functions:
 *   1. openAnalysisBuilder — opens the Analysis Builder Code Page in a new browser tab
 *   2. openFindSimilar — opens the Document Relationship Viewer in a new browser tab
 *
 * Uses window.open() instead of Xrm.Navigation.navigateTo to avoid the
 * "Leave this page?" dialog that appears when navigating from inside a
 * Dataverse dialog iframe. This keeps the upload wizard dialog intact.
 *
 * @see ADR-006  - Code Pages for standalone dialogs (navigateTo webresource)
 * @see ADR-008  - Independent auth per Code Page (no tokens in URL params)
 */

import { getAuthProvider } from "@spaarke/auth";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Web resource name for the Analysis Builder Code Page. */
const ANALYSIS_BUILDER_WEB_RESOURCE = "sprk_analysisbuilder";

/** Web resource name for the Document Relationship Viewer (Find Similar). */
const FIND_SIMILAR_WEB_RESOURCE = "sprk_documentrelationshipviewer";

/** Log prefix for console output. */
const LOG_PREFIX = "[nextStepLauncher]";

// ---------------------------------------------------------------------------
// Xrm resolution helpers
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Resolve the Dataverse client URL from Xrm.Utility.getGlobalContext().
 * Walks the frame hierarchy (self → parent → top) for robustness.
 *
 * @returns Client URL (e.g. "https://spaarkedev1.crm.dynamics.com") or null
 */
export function getClientUrl(): string | null {
    const frames: Window[] = [window];
    try { if (window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
    try { if (window.top && window.top !== window) frames.push(window.top); } catch { /* cross-origin */ }

    for (const frame of frames) {
        try {
            const xrm = (frame as any).Xrm;
            const url: string | undefined =
                xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.();
            if (url) {
                return url.endsWith("/") ? url.slice(0, -1) : url;
            }
        } catch {
            // Cross-origin frame — skip
        }
    }
    return null;
}

/**
 * Resolve the Azure AD tenant ID from the MSAL auth provider.
 * Falls back to extracting from Xrm authority URL.
 */
export function getTenantId(): string {
    // Try @spaarke/auth provider (MSAL)
    try {
        const provider = getAuthProvider();
        const authority: string = provider?.getConfig?.()?.authority ?? "";
        if (authority) {
            const parts = authority.split("/");
            const tenantId = parts[parts.length - 1] ?? "";
            if (tenantId && tenantId !== "common" && tenantId !== "organizations") {
                return tenantId;
            }
        }
    } catch {
        // Auth provider not available — try Xrm fallback
    }

    // Fallback: extract from Xrm organizationSettings
    const frames: Window[] = [window];
    try { if (window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }

    for (const frame of frames) {
        try {
            const xrm = (frame as any).Xrm;
            const ctx = xrm?.Utility?.getGlobalContext?.();
            // organizationSettings.tenantId is available in UCI
            const tenantId = ctx?.organizationSettings?.tenantId;
            if (tenantId) return tenantId;
        } catch {
            // Cross-origin frame — skip
        }
    }

    return "";
}

/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// openWebResourceInNewTab — shared helper
// ---------------------------------------------------------------------------

/**
 * Open a Dataverse web resource in a new browser tab using window.open().
 *
 * This avoids the "Leave this page?" confirmation that Xrm.Navigation.navigateTo
 * triggers when used with target:1 from inside a dialog iframe.
 *
 * URL format: {clientUrl}/WebResources/{webResourceName}?data={encodedParams}
 */
function openWebResourceInNewTab(
    webResourceName: string,
    dataParams: URLSearchParams,
    label: string,
): void {
    const clientUrl = getClientUrl();
    if (!clientUrl) {
        console.warn(LOG_PREFIX, `Cannot resolve Dataverse client URL. Cannot open ${label}.`);
        return;
    }

    const data = dataParams.toString();
    const url = `${clientUrl}/WebResources/${webResourceName}?data=${encodeURIComponent(data)}`;

    console.log(LOG_PREFIX, `Opening ${label} in new tab:`, url);
    window.open(url, "_blank", "noopener,noreferrer");
}

// ---------------------------------------------------------------------------
// openAnalysisBuilder
// ---------------------------------------------------------------------------

/**
 * Open the Analysis Builder Code Page in a new browser tab.
 *
 * Uses window.open() to construct the web resource URL directly, keeping
 * the upload wizard dialog intact (no "Leave this page?" prompt).
 *
 * @param documentId  - Dataverse sprk_document record GUID
 * @param containerId - SPE container ID for file operations
 */
export function openAnalysisBuilder(
    documentId: string,
    containerId: string,
): void {
    const params = new URLSearchParams();
    if (documentId) params.set("documentId", documentId);
    if (containerId) params.set("containerId", containerId);

    openWebResourceInNewTab(ANALYSIS_BUILDER_WEB_RESOURCE, params, "Analysis Builder");
}

// ---------------------------------------------------------------------------
// openFindSimilar
// ---------------------------------------------------------------------------

/**
 * Open the Document Relationship Viewer (Find Similar) in a new browser tab.
 *
 * Resolves tenantId from the MSAL auth provider or Xrm organization settings.
 *
 * @param documentId  - Dataverse sprk_document record GUID
 * @param containerId - SPE container ID for file operations
 */
export function openFindSimilar(
    documentId: string,
    containerId: string,
): void {
    const tenantId = getTenantId();

    const params = new URLSearchParams();
    if (documentId) params.set("documentId", documentId);
    if (tenantId) params.set("tenantId", tenantId);
    if (containerId) params.set("containerId", containerId);

    openWebResourceInNewTab(FIND_SIMILAR_WEB_RESOURCE, params, "Find Similar");
}
