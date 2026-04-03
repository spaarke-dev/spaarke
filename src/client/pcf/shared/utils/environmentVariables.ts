/**
 * Environment Variable Access Pattern for PCF Controls
 *
 * This module provides a standardized way to access Dataverse environment variables
 * from PCF controls. This is critical for multi-tenant deployment where configuration
 * values (like BFF API URLs) vary between environments.
 *
 * Environment Variables Used:
 * - sprk_BffApiBaseUrl: Base URL for the BFF API
 * - sprk_AzureOpenAiEndpoint: Azure OpenAI endpoint URL
 * - sprk_ApplicationInsightsKey: App Insights instrumentation key
 *
 * Usage in PCF Controls:
 * ```typescript
 * import { getEnvironmentVariable, getApiBaseUrl } from "../shared/utils/environmentVariables";
 *
 * // Get specific variable
 * const apiUrl = await getEnvironmentVariable(webApi, "sprk_BffApiBaseUrl");
 *
 * // Or use convenience method
 * const apiUrl = await getApiBaseUrl(webApi);
 * ```
 *
 * ADR Compliance:
 * - ADR-010: Configuration Over Code
 * - Multi-tenant parameterization pattern
 *
 * @version 1.0.0
 */

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Known environment variable schema names
 */
export type KnownEnvironmentVariable =
  | 'sprk_BffApiBaseUrl'
  | 'sprk_BffApiAppId'
  | 'sprk_MsalClientId'
  | 'sprk_TenantId'
  | 'sprk_AzureOpenAiEndpoint'
  | 'sprk_ApplicationInsightsKey'
  | 'sprk_SharePointEmbeddedContainerId'
  | 'sprk_DefaultPlaybookId';

/**
 * Cached environment variable value
 */
interface CachedValue {
  value: string;
  timestamp: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// URL Normalization
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Normalize a BFF base URL: strip trailing slashes and /api suffix.
 *
 * WHY THIS EXISTS: The Dataverse env var sprk_BffApiBaseUrl stores the URL as
 * "https://host/api", but all client-side endpoint paths include /api themselves
 * (e.g., `${baseUrl}/api/ai/search`). Without stripping, URLs double up to
 * /api/api/... (404). This matches @spaarke/auth's normalizeUrl() convention.
 *
 * This is the SINGLE normalization point for all PCF controls. Individual
 * service constructors should NOT need their own normalization.
 */
function normalizeBffUrl(raw: string): string {
  return raw.trim().replace(/\/+$/, '').replace(/\/api$/i, '');
}

// ─────────────────────────────────────────────────────────────────────────────
// Configuration
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Cache duration in milliseconds (5 minutes)
 * Environment variables rarely change, so caching improves performance
 */
const CACHE_DURATION_MS = 5 * 60 * 1000;

/**
 * Default fallback values for development
 * These should ONLY be used in development - production should always use environment variables
 */
const DEFAULT_VALUES: Record<KnownEnvironmentVariable, string> = {
  sprk_BffApiBaseUrl: 'https://spe-api-dev-67e2xz.azurewebsites.net',
  sprk_BffApiAppId: '',
  sprk_MsalClientId: '',
  sprk_TenantId: '',
  sprk_AzureOpenAiEndpoint: 'https://spaarke-openai-dev.openai.azure.com/',
  sprk_ApplicationInsightsKey: '',
  sprk_SharePointEmbeddedContainerId: '',
  sprk_DefaultPlaybookId: '',
};

// ─────────────────────────────────────────────────────────────────────────────
// Cache
// ─────────────────────────────────────────────────────────────────────────────

/**
 * In-memory cache for environment variables
 * This avoids repeated Dataverse queries for the same values
 */
const envVarCache = new Map<string, CachedValue>();

/**
 * Check if a cached value is still valid
 */
function isCacheValid(cached: CachedValue | undefined): boolean {
  if (!cached) return false;
  return Date.now() - cached.timestamp < CACHE_DURATION_MS;
}

/**
 * Clear the environment variable cache
 * Useful when environment variables are updated
 */
export function clearEnvironmentVariableCache(): void {
  envVarCache.clear();
  console.log('[Spaarke.EnvVar] Cache cleared');
}

// ─────────────────────────────────────────────────────────────────────────────
// Core Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Get an environment variable value from Dataverse
 *
 * @param webApi - The PCF WebAPI instance
 * @param schemaName - The schema name of the environment variable
 * @param useCache - Whether to use cached values (default: true)
 * @returns The environment variable value, or undefined if not found
 */
export async function getEnvironmentVariable(
  webApi: ComponentFramework.WebApi,
  schemaName: string,
  useCache = true
): Promise<string | undefined> {
  // Check cache first
  if (useCache) {
    const cached = envVarCache.get(schemaName);
    if (isCacheValid(cached)) {
      console.log(`[Spaarke.EnvVar] Cache hit for ${schemaName}`);
      return cached!.value;
    }
  }

  try {
    console.log(`[Spaarke.EnvVar] Fetching ${schemaName} from Dataverse`);

    // Query the environment variable definition
    const definitionResult = await webApi.retrieveMultipleRecords(
      'environmentvariabledefinition',
      `?$filter=schemaname eq '${schemaName}'&$select=environmentvariabledefinitionid,defaultvalue`
    );

    if (!definitionResult.entities || definitionResult.entities.length === 0) {
      console.warn(`[Spaarke.EnvVar] Environment variable ${schemaName} not found`);
      return undefined;
    }

    const definition = definitionResult.entities[0];
    const definitionId = definition.environmentvariabledefinitionid;
    const defaultValue = definition.defaultvalue as string | undefined;

    // Query for an override value
    const valueResult = await webApi.retrieveMultipleRecords(
      'environmentvariablevalue',
      `?$filter=_environmentvariabledefinitionid_value eq '${definitionId}'&$select=value`
    );

    // Use override value if present, otherwise use default
    let finalValue: string | undefined;
    if (valueResult.entities && valueResult.entities.length > 0) {
      finalValue = valueResult.entities[0].value as string;
    } else {
      finalValue = defaultValue;
    }

    // Cache the result
    if (finalValue !== undefined) {
      envVarCache.set(schemaName, {
        value: finalValue,
        timestamp: Date.now(),
      });
      console.log(`[Spaarke.EnvVar] Cached ${schemaName}: ${finalValue.substring(0, 30)}...`);
    }

    return finalValue;
  } catch (error) {
    console.error(`[Spaarke.EnvVar] Error fetching ${schemaName}:`, error);
    return undefined;
  }
}

/**
 * Get an environment variable with a fallback value
 *
 * @param webApi - The PCF WebAPI instance
 * @param schemaName - The schema name of the environment variable
 * @param fallback - The fallback value if not found
 * @returns The environment variable value or fallback
 */
export async function getEnvironmentVariableOrDefault(
  webApi: ComponentFramework.WebApi,
  schemaName: KnownEnvironmentVariable,
  fallback?: string
): Promise<string> {
  const value = await getEnvironmentVariable(webApi, schemaName);
  return value ?? fallback ?? DEFAULT_VALUES[schemaName] ?? '';
}

// ─────────────────────────────────────────────────────────────────────────────
// Convenience Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Get the BFF API base URL from environment variables.
 *
 * IMPORTANT: Returns HOST ONLY (e.g., "https://spe-api-dev-67e2xz.azurewebsites.net").
 * The Dataverse env var stores the value WITH /api suffix, but this function strips it
 * via normalizeBffUrl() to match @spaarke/auth's resolveRuntimeConfig() convention.
 * All callers MUST add /api prefix themselves: `${baseUrl}/api/ai/search`
 *
 * @param webApi - The PCF WebAPI instance
 * @returns The BFF API base URL (host only, no /api suffix)
 */
export async function getApiBaseUrl(webApi: ComponentFramework.WebApi): Promise<string> {
  const raw = await getEnvironmentVariableOrDefault(webApi, 'sprk_BffApiBaseUrl');
  return normalizeBffUrl(raw);
}

/**
 * Get the Azure OpenAI endpoint from environment variables
 *
 * @param webApi - The PCF WebAPI instance
 * @returns The Azure OpenAI endpoint URL
 */
export async function getAzureOpenAiEndpoint(webApi: ComponentFramework.WebApi): Promise<string> {
  return getEnvironmentVariableOrDefault(webApi, 'sprk_AzureOpenAiEndpoint');
}

/**
 * Get the Application Insights instrumentation key
 *
 * @param webApi - The PCF WebAPI instance
 * @returns The App Insights key
 */
export async function getAppInsightsKey(webApi: ComponentFramework.WebApi): Promise<string> {
  return getEnvironmentVariableOrDefault(webApi, 'sprk_ApplicationInsightsKey');
}

/**
 * Get the MSAL Client ID from environment variables.
 * Returns empty string if sprk_MsalClientId is not defined in Dataverse.
 *
 * @param webApi - The PCF WebAPI instance
 * @returns The MSAL Client (Application) ID
 */
export async function getMsalClientId(webApi: ComponentFramework.WebApi): Promise<string> {
  return getEnvironmentVariableOrDefault(webApi, 'sprk_MsalClientId');
}

/**
 * Get the BFF API Application ID from environment variables.
 * Used to construct the BFF API scope: api://<appId>/user_impersonation
 * Returns empty string if sprk_BffApiAppId is not defined in Dataverse.
 *
 * @param webApi - The PCF WebAPI instance
 * @returns The BFF API Application ID
 */
export async function getBffApiAppId(webApi: ComponentFramework.WebApi): Promise<string> {
  return getEnvironmentVariableOrDefault(webApi, 'sprk_BffApiAppId');
}

/**
 * Get the Azure AD Tenant ID from environment variables.
 * Returns empty string if sprk_TenantId is not defined in Dataverse.
 *
 * @param webApi - The PCF WebAPI instance
 * @returns The Azure AD Tenant (Directory) ID
 */
export async function getTenantId(webApi: ComponentFramework.WebApi): Promise<string> {
  return getEnvironmentVariableOrDefault(webApi, 'sprk_TenantId');
}

// ─────────────────────────────────────────────────────────────────────────────
// Bulk Loading
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Load multiple environment variables at once
 * More efficient than calling getEnvironmentVariable multiple times
 *
 * @param webApi - The PCF WebAPI instance
 * @param schemaNames - Array of schema names to load
 * @returns Object with schema names as keys and values
 */
export async function loadEnvironmentVariables(
  webApi: ComponentFramework.WebApi,
  schemaNames: string[]
): Promise<Record<string, string | undefined>> {
  const result: Record<string, string | undefined> = {};

  // Check cache for all values first
  const uncachedNames: string[] = [];
  for (const name of schemaNames) {
    const cached = envVarCache.get(name);
    if (isCacheValid(cached)) {
      result[name] = cached!.value;
    } else {
      uncachedNames.push(name);
    }
  }

  // Fetch uncached values
  if (uncachedNames.length > 0) {
    console.log(`[Spaarke.EnvVar] Bulk loading ${uncachedNames.length} environment variables`);

    // Build filter for multiple schema names
    const filter = uncachedNames.map(name => `schemaname eq '${name}'`).join(' or ');

    try {
      // Get all definitions
      const definitionResult = await webApi.retrieveMultipleRecords(
        'environmentvariabledefinition',
        `?$filter=${filter}&$select=environmentvariabledefinitionid,schemaname,defaultvalue`
      );

      // Map definitions by schema name
      const definitions = new Map<string, { id: string; defaultValue?: string }>();
      for (const entity of definitionResult.entities) {
        definitions.set(entity.schemaname as string, {
          id: entity.environmentvariabledefinitionid as string,
          defaultValue: entity.defaultvalue as string | undefined,
        });
      }

      // Get all values at once
      const definitionIds = Array.from(definitions.values()).map(d => d.id);
      if (definitionIds.length > 0) {
        const valueFilter = definitionIds.map(id => `_environmentvariabledefinitionid_value eq '${id}'`).join(' or ');

        const valueResult = await webApi.retrieveMultipleRecords(
          'environmentvariablevalue',
          `?$filter=${valueFilter}&$select=_environmentvariabledefinitionid_value,value`
        );

        // Map values by definition ID
        const values = new Map<string, string>();
        for (const entity of valueResult.entities) {
          values.set(entity._environmentvariabledefinitionid_value as string, entity.value as string);
        }

        // Build final results
        for (const [schemaName, def] of definitions) {
          const value = values.get(def.id) ?? def.defaultValue;
          result[schemaName] = value;

          // Cache the value
          if (value !== undefined) {
            envVarCache.set(schemaName, {
              value,
              timestamp: Date.now(),
            });
          }
        }
      }
    } catch (error) {
      console.error('[Spaarke.EnvVar] Error bulk loading environment variables:', error);
      // Return what we have from cache
    }
  }

  return result;
}

// ─────────────────────────────────────────────────────────────────────────────
// Configuration Loader
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Standard configuration object for Spaarke PCF controls
 */
export interface SpaarkeConfiguration {
  apiBaseUrl: string;
  azureOpenAiEndpoint: string;
  appInsightsKey: string;
  containerId: string;
}

/**
 * Load all standard Spaarke configuration at once
 * Use this in PCF init() to get all required configuration
 *
 * @param webApi - The PCF WebAPI instance
 * @returns Configuration object with all standard values
 */
export async function loadSpaarkeConfiguration(webApi: ComponentFramework.WebApi): Promise<SpaarkeConfiguration> {
  const values = await loadEnvironmentVariables(webApi, [
    'sprk_BffApiBaseUrl',
    'sprk_AzureOpenAiEndpoint',
    'sprk_ApplicationInsightsKey',
    'sprk_SharePointEmbeddedContainerId',
  ]);

  return {
    apiBaseUrl: normalizeBffUrl(values['sprk_BffApiBaseUrl'] ?? DEFAULT_VALUES.sprk_BffApiBaseUrl),
    azureOpenAiEndpoint: values['sprk_AzureOpenAiEndpoint'] ?? DEFAULT_VALUES.sprk_AzureOpenAiEndpoint,
    appInsightsKey: values['sprk_ApplicationInsightsKey'] ?? DEFAULT_VALUES.sprk_ApplicationInsightsKey,
    containerId: values['sprk_SharePointEmbeddedContainerId'] ?? DEFAULT_VALUES.sprk_SharePointEmbeddedContainerId,
  };
}

export default {
  getEnvironmentVariable,
  getEnvironmentVariableOrDefault,
  getApiBaseUrl,
  getAzureOpenAiEndpoint,
  getAppInsightsKey,
  getMsalClientId,
  getBffApiAppId,
  getTenantId,
  loadEnvironmentVariables,
  loadSpaarkeConfiguration,
  clearEnvironmentVariableCache,
};
