/**
 * Logger utility for Analysis Workspace PCF control
 */

const LOG_PREFIX = "[Spaarke.AnalysisWorkspace]";

export function logInfo(component: string, message: string, data?: unknown): void {
    console.log(`${LOG_PREFIX} [${component}] ${message}`, data !== undefined ? data : "");
}

export function logWarn(component: string, message: string, data?: unknown): void {
    console.warn(`${LOG_PREFIX} [${component}] ${message}`, data !== undefined ? data : "");
}

export function logError(component: string, message: string, error?: unknown): void {
    console.error(`${LOG_PREFIX} [${component}] ${message}`, error !== undefined ? error : "");
}

export function logDebug(component: string, message: string, data?: unknown): void {
    if (process.env.NODE_ENV === "development") {
        console.debug(`${LOG_PREFIX} [${component}] ${message}`, data !== undefined ? data : "");
    }
}
