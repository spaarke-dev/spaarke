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

import { resolveTenantIdSync } from "@spaarke/auth";
import { getXrm } from "@spaarke/ui-components/utils/xrmContext";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Web resource name for the Playbook Library Code Page (merged from AnalysisBuilder). */
const ANALYSIS_BUILDER_WEB_RESOURCE = "sprk_playbooklibrary";

/** Web resource name for the Document Relationship Viewer (Find Similar). */
const FIND_SIMILAR_WEB_RESOURCE = "sprk_documentrelationshipviewer";

/** Log prefix for console output. */
const LOG_PREFIX = "[nextStepLauncher]";

// ---------------------------------------------------------------------------
// Xrm resolution helpers
// ---------------------------------------------------------------------------

/**
 * Resolve the Dataverse client URL from Xrm.Utility.getGlobalContext().
 * Shared cross-frame walker (task 081 / C-8).
 *
 * @returns Client URL (e.g. "https://spaarkedev1.crm.dynamics.com") or null
 */
export function getClientUrl(): string | null {
    try {
        const url: string | undefined =
            getXrm('clientUrl')?.Utility?.getGlobalContext?.()?.getClientUrl?.();
        if (url) {
            return url.endsWith("/") ? url.slice(0, -1) : url;
        }
    } catch {
        // getGlobalContext() unavailable
    }
    return null;
}

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
 * @param documentId  - Dataverse sprk_document record GUID
 * @param containerId - SPE container ID for file operations
 */
export function openFindSimilar(
    documentId: string,
    containerId: string,
): void {
    const tenantId = resolveTenantIdSync();

    const params = new URLSearchParams();
    if (documentId) params.set("documentId", documentId);
    if (tenantId) params.set("tenantId", tenantId);
    if (containerId) params.set("containerId", containerId);

    openWebResourceInNewTab(FIND_SIMILAR_WEB_RESOURCE, params, "Find Similar");
}
