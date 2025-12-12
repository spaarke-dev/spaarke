/**
 * Environment Variable Access for Analysis Workspace PCF
 *
 * Provides access to Dataverse environment variables for multi-tenant configuration.
 * Copy of shared utility - can be consolidated when PCF bundling supports shared modules.
 *
 * @version 1.0.1
 */

const LOG_PREFIX = "[Spaarke.AnalysisWorkspace.EnvVar]";

// Cache duration: 5 minutes
const CACHE_DURATION_MS = 5 * 60 * 1000;

// Default fallback for development
const DEFAULT_API_URL = "https://spe-api-dev-67e2xz.azurewebsites.net/api";

/**
 * Check if WebAPI is available (not in design-time/editor mode)
 * Custom Page editor doesn't implement WebAPI methods
 */
function isWebApiAvailable(webApi: ComponentFramework.WebApi | undefined): boolean {
    if (!webApi) return false;

    // Check if retrieveMultipleRecords is a real implementation
    // In editor mode, it may be undefined or throw "not implemented"
    try {
        if (typeof webApi.retrieveMultipleRecords !== "function") {
            return false;
        }
        return true;
    } catch {
        return false;
    }
}

interface CachedValue {
    value: string;
    timestamp: number;
}

const envVarCache: Map<string, CachedValue> = new Map();

function isCacheValid(cached: CachedValue | undefined): boolean {
    if (!cached) return false;
    return Date.now() - cached.timestamp < CACHE_DURATION_MS;
}

/**
 * Get an environment variable from Dataverse
 */
export async function getEnvironmentVariable(
    webApi: ComponentFramework.WebApi,
    schemaName: string
): Promise<string | undefined> {
    // Check cache first
    const cached = envVarCache.get(schemaName);
    if (isCacheValid(cached)) {
        console.log(`${LOG_PREFIX} Cache hit for ${schemaName}`);
        return cached!.value;
    }

    // Check if WebAPI is available (not in design-time/editor mode)
    if (!isWebApiAvailable(webApi)) {
        console.log(`${LOG_PREFIX} WebAPI not available (design-time mode), using default`);
        return undefined;
    }

    try {
        console.log(`${LOG_PREFIX} Fetching ${schemaName}`);

        // Get environment variable definition
        const defResult = await webApi.retrieveMultipleRecords(
            "environmentvariabledefinition",
            `?$filter=schemaname eq '${schemaName}'&$select=environmentvariabledefinitionid,defaultvalue`
        );

        if (!defResult.entities || defResult.entities.length === 0) {
            console.warn(`${LOG_PREFIX} ${schemaName} not found`);
            return undefined;
        }

        const definition = defResult.entities[0];
        const definitionId = definition.environmentvariabledefinitionid;
        const defaultValue = definition.defaultvalue as string | undefined;

        // Check for override value
        const valueResult = await webApi.retrieveMultipleRecords(
            "environmentvariablevalue",
            `?$filter=_environmentvariabledefinitionid_value eq '${definitionId}'&$select=value`
        );

        let finalValue: string | undefined;
        if (valueResult.entities && valueResult.entities.length > 0) {
            finalValue = valueResult.entities[0].value as string;
        } else {
            finalValue = defaultValue;
        }

        // Cache result
        if (finalValue !== undefined) {
            envVarCache.set(schemaName, { value: finalValue, timestamp: Date.now() });
            console.log(`${LOG_PREFIX} Cached ${schemaName}`);
        }

        return finalValue;
    } catch (error) {
        // Handle "not implemented" errors from design-time environment
        const errorMessage = error instanceof Error ? error.message : String(error);
        if (errorMessage.toLowerCase().includes("not implemented")) {
            console.log(`${LOG_PREFIX} WebAPI not implemented (design-time mode), using default`);
            return undefined;
        }
        console.error(`${LOG_PREFIX} Error fetching ${schemaName}:`, error);
        return undefined;
    }
}

/**
 * Get the BFF API base URL from environment variables
 */
export async function getApiBaseUrl(
    webApi: ComponentFramework.WebApi
): Promise<string> {
    const value = await getEnvironmentVariable(webApi, "sprk_BffApiBaseUrl");
    return value ?? DEFAULT_API_URL;
}

/**
 * Clear the environment variable cache
 */
export function clearCache(): void {
    envVarCache.clear();
    console.log(`${LOG_PREFIX} Cache cleared`);
}
