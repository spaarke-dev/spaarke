/**
 * useFieldMetadata - Fetch optionset labels from Dataverse entity metadata
 *
 * Queries Xrm.Utility.getEntityMetadata for all choice fields in the form config.
 * Returns a Map of field name → metadata (including optionset options with labels).
 *
 * Results are cached for the session since metadata doesn't change at runtime.
 *
 * @see approach-a-dynamic-form-renderer.md
 */

import * as React from "react";
import { getXrm } from "../utils/xrmAccess";
import type { IFormConfig, IFieldMetadata, IChoiceOption } from "../types/FormConfig";
import { extractChoiceFieldNames } from "../types/FormConfig";

// Session-level cache: entity:fieldName → IFieldMetadata
const metadataCache = new Map<string, IFieldMetadata>();

/**
 * Fetch field metadata for all choice fields in the form config.
 *
 * @param entityName - Dataverse entity logical name (e.g., "sprk_event")
 * @param config - The form config (to extract choice field names)
 * @returns Map of field name → metadata
 */
export function useFieldMetadata(
  entityName: string,
  config: IFormConfig | null
): Map<string, IFieldMetadata> {
  const [metadata, setMetadata] = React.useState<Map<string, IFieldMetadata>>(
    new Map()
  );

  React.useEffect(() => {
    if (!config) return;

    const choiceFields = extractChoiceFieldNames(config);
    if (choiceFields.length === 0) {
      setMetadata(new Map());
      return;
    }

    // Check cache first
    const allCached = choiceFields.every((f) =>
      metadataCache.has(`${entityName}:${f}`)
    );
    if (allCached) {
      const cached = new Map<string, IFieldMetadata>();
      for (const f of choiceFields) {
        cached.set(f, metadataCache.get(`${entityName}:${f}`)!);
      }
      setMetadata(cached);
      return;
    }

    let cancelled = false;

    async function fetchMetadata() {
      const xrm = getXrm();
      if (!xrm?.Utility?.getEntityMetadata) {
        console.warn("[useFieldMetadata] Xrm.Utility.getEntityMetadata not available");
        return;
      }

      try {
        const entityMeta = await xrm.Utility.getEntityMetadata(
          entityName,
          choiceFields
        );

        if (cancelled) return;

        const result = new Map<string, IFieldMetadata>();

        for (const fieldName of choiceFields) {
          const attrMeta = entityMeta.Attributes.get(fieldName);

          if (attrMeta?.OptionSet?.Options) {
            const options: IChoiceOption[] = attrMeta.OptionSet.Options.map(
              (opt) => ({
                value: opt.Value,
                label: opt.Label?.UserLocalizedLabel?.Label ?? `Value ${opt.Value}`,
              })
            );

            const fieldMeta: IFieldMetadata = {
              fieldName,
              options,
            };

            result.set(fieldName, fieldMeta);
            metadataCache.set(`${entityName}:${fieldName}`, fieldMeta);
          } else {
            // No optionset metadata found — create empty
            const fieldMeta: IFieldMetadata = {
              fieldName,
              options: [],
            };
            result.set(fieldName, fieldMeta);
          }
        }

        setMetadata(result);
      } catch (error) {
        if (cancelled) return;
        console.error("[useFieldMetadata] Failed to fetch metadata:", error);
      }
    }

    fetchMetadata();

    return () => {
      cancelled = true;
    };
  }, [entityName, config]);

  return metadata;
}
