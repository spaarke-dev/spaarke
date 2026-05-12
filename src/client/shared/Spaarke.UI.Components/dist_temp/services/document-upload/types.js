/**
 * Document Upload Service Types
 *
 * Shared type definitions for document upload services extracted from
 * UniversalQuickCreate PCF control. These types support both PCF (context.webAPI)
 * and Code Page (OData fetch with MSAL) contexts.
 *
 * @version 1.0.0
 */
/**
 * Default console logger implementation.
 */
export const consoleLogger = {
    info: (source, message, data) => console.log(`[${source}] ${message}`, data ?? ''),
    warn: (source, message, data) => console.warn(`[${source}] ${message}`, data ?? ''),
    error: (source, message, error) => console.error(`[${source}] ${message}`, error ?? ''),
    debug: (source, message, data) => console.debug(`[${source}] ${message}`, data ?? ''),
};
//# sourceMappingURL=types.js.map