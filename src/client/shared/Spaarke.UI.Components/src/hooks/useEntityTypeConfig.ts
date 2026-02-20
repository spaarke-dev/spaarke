/**
 * useEntityTypeConfig - Generic hook for reading configuration JSON from entity type records
 *
 * Provides a reusable pattern for reading a JSON configuration field from any
 * Dataverse "type" entity (sprk_eventtype, sprk_mattertype, sprk_projecttype, etc.)
 * and computing field/section visibility states.
 *
 * This is the generic foundation that entity-specific hooks (useEventTypeConfig,
 * useMatterTypeConfig, etc.) build upon.
 *
 * @see ADR-012 - Shared Component Library
 * @see Task 107 - Extract useEntityTypeConfig generic hook
 *
 * @example
 * ```tsx
 * // Event-specific usage
 * const config = useEntityTypeConfig<IEventTypeFieldConfig>({
 *   entityName: "sprk_eventtype",
 *   recordId: eventTypeId,
 *   configFieldName: "sprk_fieldconfigjson",
 *   selectFields: "sprk_eventtypeid,sprk_name,sprk_fieldconfigjson",
 * });
 *
 * // Matter-specific usage (future)
 * const config = useEntityTypeConfig<IMatterTypeConfig>({
 *   entityName: "sprk_mattertype",
 *   recordId: matterTypeId,
 *   configFieldName: "sprk_sidepaneconfigjson",
 *   selectFields: "sprk_mattertypeid,sprk_name,sprk_sidepaneconfigjson",
 * });
 * ```
 */

import * as React from "react";
import { getXrm } from "../utils/xrmContext";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Options for the useEntityTypeConfig hook
 */
export interface UseEntityTypeConfigOptions {
  /** Dataverse entity logical name (e.g., "sprk_eventtype") */
  entityName: string;
  /** Record ID (GUID) to retrieve config from. If undefined, returns defaults. */
  recordId: string | undefined;
  /** Field name containing the JSON configuration (e.g., "sprk_fieldconfigjson") */
  configFieldName: string;
  /** OData $select fields for the retrieveRecord call */
  selectFields: string;
}

/**
 * Result of the useEntityTypeConfig hook
 */
export interface UseEntityTypeConfigResult<TConfig> {
  /** Whether the config is currently loading */
  isLoading: boolean;
  /** Error message if loading failed */
  error: string | null;
  /** Raw parsed configuration object (null if not loaded or no config) */
  config: TConfig | null;
  /** The entity type record name (if available) */
  typeName: string | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook Implementation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Generic hook for reading a JSON configuration field from a Dataverse entity type record.
 *
 * @param options - Configuration for which entity/field to read
 * @returns Loading state, error, and parsed config
 */
export function useEntityTypeConfig<TConfig>(
  options: UseEntityTypeConfigOptions
): UseEntityTypeConfigResult<TConfig> {
  const { entityName, recordId, configFieldName, selectFields } = options;

  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [config, setConfig] = React.useState<TConfig | null>(null);
  const [typeName, setTypeName] = React.useState<string | null>(null);

  React.useEffect(() => {
    // No record ID → no config to load (use defaults)
    if (!recordId) {
      setConfig(null);
      setTypeName(null);
      setIsLoading(false);
      setError(null);
      return;
    }

    let cancelled = false;

    async function fetchConfig() {
      setIsLoading(true);
      setError(null);

      try {
        const xrm = getXrm();
        if (!xrm?.WebApi) {
          setError("Xrm.WebApi not available");
          setIsLoading(false);
          return;
        }

        // Normalize GUID (remove braces, lowercase)
        const normalizedId = recordId!.replace(/[{}]/g, "").toLowerCase();

        const record = await xrm.WebApi.retrieveRecord(
          entityName,
          normalizedId,
          `?$select=${selectFields}`
        );

        if (cancelled) return;

        // Extract the config JSON field
        const configJson = (record[configFieldName] as string) ?? null;

        // Extract name field (convention: look for common name patterns)
        const nameValue =
          (record["sprk_name"] as string) ??
          (record["name"] as string) ??
          null;
        setTypeName(nameValue);

        if (!configJson) {
          // No config JSON stored — use defaults
          setConfig(null);
          setIsLoading(false);
          return;
        }

        // Parse JSON
        try {
          const parsed = JSON.parse(configJson) as TConfig;
          setConfig(parsed);
        } catch (parseError) {
          console.error(
            `[useEntityTypeConfig] Invalid JSON in ${entityName}.${configFieldName}:`,
            parseError
          );
          setError(`Invalid configuration JSON for ${entityName}`);
          setConfig(null);
        }
      } catch (fetchError) {
        if (cancelled) return;
        const msg = fetchError instanceof Error ? fetchError.message : String(fetchError);
        console.error(`[useEntityTypeConfig] Failed to fetch ${entityName} config:`, msg);
        setError(msg);
        setConfig(null);
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    }

    fetchConfig();

    return () => {
      cancelled = true;
    };
  }, [entityName, recordId, configFieldName, selectFields]);

  return { isLoading, error, config, typeName };
}
