/**
 * Host Context Service - URL parameter parsing for AnalysisWorkspace Code Page
 *
 * Parses and validates URL parameters from the Dataverse data envelope when the
 * Code Page is opened via Xrm.Navigation.navigateTo. Provides a typed HostContext
 * object for use across components and hooks.
 *
 * URL format (Dataverse data envelope):
 *   ?data=analysisId%3D{guid}%26documentId%3D{guid}%26tenantId%3D{guid}
 *
 * Or direct query parameters (for dev server):
 *   ?analysisId={guid}&documentId={guid}&tenantId={guid}
 *
 * @see ADR-006 - Code Pages for standalone dialogs
 */

import type { HostContext } from "../types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const LOG_PREFIX = "[AnalysisWorkspace:HostContext]";

// ---------------------------------------------------------------------------
// Parse URL Parameters
// ---------------------------------------------------------------------------

/**
 * Parse the Dataverse data envelope from the current URL.
 *
 * Dataverse wraps Code Page parameters in a single `data` query parameter.
 * If the `data` parameter is not found, falls back to direct query parameters
 * (useful for local development).
 *
 * @returns Parsed HostContext with all available parameters
 */
export function parseHostContext(): HostContext {
    const rawUrlParams = new URLSearchParams(window.location.search);
    const dataEnvelope = rawUrlParams.get("data");

    const params = dataEnvelope
        ? new URLSearchParams(decodeURIComponent(dataEnvelope))
        : rawUrlParams;

    const context: HostContext = {
        analysisId: params.get("analysisId") ?? "",
        documentId: params.get("documentId") ?? "",
        tenantId: params.get("tenantId") ?? "",
        theme: params.get("theme") ?? undefined,
    };

    if (context.analysisId) {
        console.info(`${LOG_PREFIX} Parsed context: analysisId=${context.analysisId}, documentId=${context.documentId}`);
    } else {
        console.warn(`${LOG_PREFIX} No analysisId found in URL parameters`);
    }

    return context;
}

/**
 * Validate that the required parameters are present in the host context.
 *
 * @param context - The parsed host context
 * @returns An array of validation error messages (empty if valid)
 */
export function validateHostContext(context: HostContext): string[] {
    const errors: string[] = [];

    if (!context.analysisId) {
        errors.push("Missing required parameter: analysisId");
    }

    if (!context.documentId) {
        errors.push("Missing required parameter: documentId");
    }

    return errors;
}

/**
 * Singleton instance of the host context, parsed once on first access.
 * Safe to call multiple times -- returns the same object each time.
 */
let cachedContext: HostContext | null = null;

/**
 * Get the host context (parsed from URL on first call, cached thereafter).
 *
 * @returns The parsed HostContext
 */
export function getHostContext(): HostContext {
    if (!cachedContext) {
        cachedContext = parseHostContext();
    }
    return cachedContext;
}

/**
 * Clear the cached host context. Useful for testing or when URL changes.
 */
export function clearHostContextCache(): void {
    cachedContext = null;
}
