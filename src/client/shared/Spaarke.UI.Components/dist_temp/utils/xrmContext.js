/**
 * Xrm Context Utility
 *
 * Provides unified access to Xrm object from PCF controls and Custom Pages.
 * PCF controls have Xrm on window, Custom Pages (iframe) need parent.Xrm.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 * @see ADR-022 PCF Platform Libraries
 */
/* eslint-enable @typescript-eslint/no-explicit-any */
/**
 * Get the Xrm object from the appropriate context.
 *
 * - PCF controls have Xrm on window.Xrm or via context.webAPI
 * - Custom Pages run in an iframe, so Xrm is on window.parent.Xrm
 * - Returns undefined if Xrm is not available (graceful degradation)
 *
 * @returns XrmContext or undefined if not available
 *
 * @example
 * ```typescript
 * const xrm = getXrm();
 * if (xrm) {
 *   const result = await xrm.WebApi.retrieveMultipleRecords("account", "?$top=10");
 * }
 * ```
 */
export function getXrm() {
    // Try window.Xrm first (PCF controls or direct script access)
    try {
        const windowXrm = window.Xrm;
        if (windowXrm?.WebApi) {
            return windowXrm;
        }
    }
    catch {
        // window.Xrm not available
    }
    // Try parent.Xrm for Custom Pages running in iframe
    try {
        if (typeof window !== 'undefined' && window.parent && window.parent !== window) {
            const parentXrm = window.parent.Xrm;
            if (parentXrm?.WebApi) {
                return parentXrm;
            }
        }
    }
    catch {
        // Cross-origin access denied - expected in some environments
    }
    return undefined;
}
/**
 * Check if we're running in a Custom Page (iframe) context
 *
 * @returns true if in Custom Page iframe
 */
export function isCustomPageContext() {
    try {
        return typeof window !== 'undefined' && window.parent !== undefined && window.parent !== window;
    }
    catch {
        return false;
    }
}
/**
 * Check if we're running in a PCF control context
 *
 * @returns true if in PCF context (has window.Xrm directly)
 */
export function isPcfContext() {
    try {
        return typeof window.Xrm !== 'undefined' && window.Xrm?.WebApi !== undefined;
    }
    catch {
        return false;
    }
}
/**
 * Detect the current theme from the host environment.
 * Uses Xrm.Utility.getGlobalContext().userSettings when available.
 *
 * OS `prefers-color-scheme` is intentionally NOT consulted — ADR-021 requires
 * the Spaarke theme system (not the OS) to control all UI surfaces.
 *
 * @returns Object with isDarkTheme boolean and source of detection
 *
 * @example
 * ```typescript
 * const theme = detectThemeFromHost();
 * if (theme.isDarkTheme) {
 *   // Apply dark theme styles
 * }
 * ```
 */
export function detectThemeFromHost() {
    // Try Xrm global context first
    try {
        const xrm = getXrm();
        if (xrm?.Utility) {
            const globalContext = xrm.Utility.getGlobalContext();
            if (globalContext?.userSettings?.isDarkTheme !== undefined) {
                return {
                    isDarkTheme: globalContext.userSettings.isDarkTheme,
                    source: 'xrm',
                };
            }
        }
    }
    catch {
        // Xrm context not available or error accessing
    }
    // Default: light theme (OS prefers-color-scheme is intentionally NOT consulted)
    return {
        isDarkTheme: false,
        source: 'default',
    };
}
/**
 * Get the organization's base URL from Xrm context
 *
 * @returns Base URL string or undefined
 */
export function getClientUrl() {
    try {
        const xrm = getXrm();
        if (xrm?.Utility) {
            return xrm.Utility.getGlobalContext().getClientUrl();
        }
    }
    catch {
        // Unable to get client URL
    }
    return undefined;
}
/**
 * Get the current user's ID from Xrm context
 *
 * @returns User ID string (GUID without braces) or undefined
 */
export function getCurrentUserId() {
    try {
        const xrm = getXrm();
        if (xrm?.Utility) {
            return xrm.Utility.getGlobalContext().userSettings.userId;
        }
    }
    catch {
        // Unable to get user ID
    }
    return undefined;
}
//# sourceMappingURL=xrmContext.js.map