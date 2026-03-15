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
 * Logger interface returned by createLogger.
 */
export interface ISpaarkeLogger {
  /** Log informational message (development only). */
  logInfo(component: string, message: string, data?: unknown): void;
  /** Log warning message (development only). */
  logWarn(component: string, message: string, data?: unknown): void;
  /** Log error message (always logs, never guarded). */
  logError(component: string, message: string, data?: unknown): void;
  /** Log debug message (development only). */
  logDebug(component: string, message: string, data?: unknown): void;
}

/**
 * Check whether the current environment is development mode.
 * Uses process.env.NODE_ENV which is replaced at build time by webpack/bundlers.
 */
function isDevelopment(): boolean {
  try {
    return process.env.NODE_ENV === 'development';
  } catch {
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
export function createLogger(prefix: string): ISpaarkeLogger {
  const logPrefix = `[Spaarke.${prefix}]`;

  return {
    logInfo(component: string, message: string, data?: unknown): void {
      if (isDevelopment()) {
        if (data !== undefined) {
          console.log(`${logPrefix} [${component}] ${message}`, data);
        } else {
          console.log(`${logPrefix} [${component}] ${message}`);
        }
      }
    },

    logWarn(component: string, message: string, data?: unknown): void {
      if (isDevelopment()) {
        if (data !== undefined) {
          console.warn(`${logPrefix} [${component}] ${message}`, data);
        } else {
          console.warn(`${logPrefix} [${component}] ${message}`);
        }
      }
    },

    logError(component: string, message: string, data?: unknown): void {
      // logError always logs — never guarded by environment
      if (data !== undefined) {
        console.error(`${logPrefix} [${component}] ${message}`, data);
      } else {
        console.error(`${logPrefix} [${component}] ${message}`);
      }
    },

    logDebug(component: string, message: string, data?: unknown): void {
      if (isDevelopment()) {
        if (data !== undefined) {
          console.debug(`${logPrefix} [${component}] ${message}`, data);
        } else {
          console.debug(`${logPrefix} [${component}] ${message}`);
        }
      }
    },
  };
}
