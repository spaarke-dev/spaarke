/**
 * useFormConfig - Hook for fetching form configuration from Event Type (Approach A)
 *
 * Reads sprk_fieldconfigjson from the Event Type record and parses it
 * into an IFormConfig. Falls back to a generic config when JSON is missing.
 *
 * This replaces the old useEventTypeConfig hook (Approach B hide-from-defaults)
 * with the Approach A model where JSON IS the form definition.
 *
 * @see approach-a-dynamic-form-renderer.md
 * @see ADR-012 - Shared Component Library
 */

import * as React from "react";
import { getXrm } from "../utils/xrmAccess";
import { parseFormConfig, type IFormConfig } from "../types/FormConfig";
import { FALLBACK_FORM_CONFIG } from "../config/fallbackConfig";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface UseFormConfigResult {
  /** Whether the config is loading */
  isLoading: boolean;
  /** Error message if loading failed */
  error: string | null;
  /** The parsed form config (never null — falls back to generic) */
  formConfig: IFormConfig;
  /** The Event Type name (for display) */
  typeName: string | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

const EVENT_TYPE_ENTITY = "sprk_eventtype_ref";
const EVENT_TYPE_SELECT = "sprk_eventtype_refid,sprk_name,sprk_fieldconfigjson";

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Fetch and parse form configuration from the Event Type record.
 *
 * @param eventTypeId - GUID of the Event Type (from event record)
 * @returns Loading state, error, and parsed form config
 */
export function useFormConfig(
  eventTypeId: string | undefined | null
): UseFormConfigResult {
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [formConfig, setFormConfig] = React.useState<IFormConfig>(FALLBACK_FORM_CONFIG);
  const [typeName, setTypeName] = React.useState<string | null>(null);

  React.useEffect(() => {
    // No event type → use fallback
    if (!eventTypeId) {
      setFormConfig(FALLBACK_FORM_CONFIG);
      setTypeName(null);
      setIsLoading(false);
      setError(null);
      return;
    }

    let cancelled = false;

    async function fetchConfig() {
      setIsLoading(true);
      setError(null);

      const xrm = getXrm();
      if (!xrm?.WebApi) {
        console.warn("[useFormConfig] Xrm.WebApi not available, using fallback");
        setFormConfig(FALLBACK_FORM_CONFIG);
        setIsLoading(false);
        return;
      }

      try {
        const normalizedId = eventTypeId!.replace(/[{}]/g, "").toLowerCase();

        const record = await xrm.WebApi.retrieveRecord(
          EVENT_TYPE_ENTITY,
          normalizedId,
          `?$select=${EVENT_TYPE_SELECT}`
        );

        if (cancelled) return;

        // Extract name
        const name = (record["sprk_name"] as string) ?? null;
        setTypeName(name);

        // Parse the form config JSON
        const configJson = (record["sprk_fieldconfigjson"] as string) ?? null;
        const parsed = parseFormConfig(configJson);

        if (parsed) {
          setFormConfig(parsed);
          console.log(
            "[useFormConfig] Loaded config for:",
            name,
            `(${parsed.sections.length} sections,`,
            `${parsed.sections.reduce((sum, s) => sum + s.fields.length, 0)} fields)`
          );
        } else {
          // No config or invalid JSON — use fallback
          setFormConfig(FALLBACK_FORM_CONFIG);
          console.log("[useFormConfig] No config for:", name, "— using fallback");
        }
      } catch (err) {
        if (cancelled) return;

        const msg = err instanceof Error
          ? err.message
          : (typeof err === "object" ? JSON.stringify(err) : String(err));

        if (msg.includes("404") || msg.toLowerCase().includes("not found")) {
          console.warn(`[useFormConfig] Event Type not found: ${eventTypeId}, using fallback`);
          setFormConfig(FALLBACK_FORM_CONFIG);
          setError(null);
        } else {
          console.error("[useFormConfig] Error loading config:", msg);
          setFormConfig(FALLBACK_FORM_CONFIG);
          setError(msg);
        }
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
  }, [eventTypeId]);

  return { isLoading, error, formConfig, typeName };
}
