/**
 * Shared logger utility for Spaarke PCF controls and React Code Pages.
 *
 * Creates a prefixed logger instance that provides structured, environment-aware
 * console output. All methods except logError are guarded behind a development
 * mode check to prevent noisy production logs.
 *
 * @example
 * ```ts
 * import { createLogger } from '@spaarke/ui-components';
 *
 * const logger = createLogger('AssociationResolver');
 * logger.logInfo('RecordSelection', 'User selected 3 records', { count: 3 });
 * // Output: [Spaarke.AssociationResolver] [RecordSelection] User selected 3 records { count: 3 }
 * ```
 *
 * Standards: ADR-012 (shared utilities in @spaarke/ui-components)
 */
/**
 * Check whether the current environment is development mode.
 * Uses process.env.NODE_ENV which is replaced at build time by webpack/bundlers.
 */
function isDevelopment() {
    try {
        return process.env.NODE_ENV === 'development';
    }
    catch {
        // process may not be defined in some runtime environments
        return false;
    }
}
/**
 * Create a prefixed logger instance for a specific module or control.
 *
 * @param prefix - The module/control name (e.g., 'AssociationResolver', 'SpeFileViewer').
 *                 Output format: [Spaarke.{prefix}] [{component}] {message}
 * @returns An ISpaarkeLogger with logInfo, logWarn, logError, and logDebug methods.
 */
export function createLogger(prefix) {
    const logPrefix = `[Spaarke.${prefix}]`;
    return {
        logInfo(component, message, data) {
            if (isDevelopment()) {
                if (data !== undefined) {
                    console.log(`${logPrefix} [${component}] ${message}`, data);
                }
                else {
                    console.log(`${logPrefix} [${component}] ${message}`);
                }
            }
        },
        logWarn(component, message, data) {
            if (isDevelopment()) {
                if (data !== undefined) {
                    console.warn(`${logPrefix} [${component}] ${message}`, data);
                }
                else {
                    console.warn(`${logPrefix} [${component}] ${message}`);
                }
            }
        },
        logError(component, message, data) {
            // logError always logs — never guarded by environment
            if (data !== undefined) {
                console.error(`${logPrefix} [${component}] ${message}`, data);
            }
            else {
                console.error(`${logPrefix} [${component}] ${message}`);
            }
        },
        logDebug(component, message, data) {
            if (isDevelopment()) {
                if (data !== undefined) {
                    console.debug(`${logPrefix} [${component}] ${message}`, data);
                }
                else {
                    console.debug(`${logPrefix} [${component}] ${message}`);
                }
            }
        },
    };
}
//# sourceMappingURL=logger.js.map