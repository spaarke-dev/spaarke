/**
 * SectionRenderer - Renders a single collapsible section with its fields
 *
 * Uses CollapsibleSection for the expand/collapse behavior and FieldRenderer
 * for each field within the section.
 *
 * @see approach-a-dynamic-form-renderer.md
 */

import * as React from "react";
import { makeStyles, shorthands } from "@fluentui/react-components";
import { CollapsibleSection } from "../CollapsibleSection";
import { FieldRenderer } from "./FieldRenderer";
import type {
  ISectionConfig,
  IFieldMetadata,
  FieldChangeCallback,
} from "../../types/FormConfig";

const useStyles = makeStyles({
  fieldsContainer: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("12px"),
  },
});

export interface SectionRendererProps {
  config: ISectionConfig;
  values: Record<string, unknown>;
  onChange: FieldChangeCallback;
  disabled: boolean;
  metadata: Map<string, IFieldMetadata>;
  /** Render fields without section header/collapsibility */
  flatMode?: boolean;
  /** Controlled expanded state (for persistence) */
  expanded?: boolean;
  /** Callback when expanded state changes */
  onExpandedChange?: (sectionId: string, expanded: boolean) => void;
}

export const SectionRenderer: React.FC<SectionRendererProps> = ({
  config,
  values,
  onChange,
  disabled,
  metadata,
  flatMode = false,
  expanded,
  onExpandedChange,
}) => {
  const styles = useStyles();

  const handleExpandedChange = React.useCallback(
    (isExpanded: boolean) => {
      onExpandedChange?.(config.id, isExpanded);
    },
    [config.id, onExpandedChange]
  );

  /**
   * Resolve the value for a field.
   * For lookup fields, the value key is _fieldname_value in OData results.
   */
  const getFieldValue = React.useCallback(
    (fieldName: string, fieldType: string): unknown => {
      if (fieldType === "lookup") {
        // Check for ILookupValue object first (from user selection)
        const directValue = values[fieldName];
        if (directValue && typeof directValue === "object" && "id" in (directValue as Record<string, unknown>)) {
          return directValue;
        }
        // Fall back to OData format _fieldname_value
        const odataKey = `_${fieldName}_value`;
        const guid = values[odataKey] as string | undefined;
        if (guid) {
          const nameKey = `${odataKey}@OData.Community.Display.V1.FormattedValue`;
          const entityKey = `${odataKey}@Microsoft.Dynamics.CRM.lookuplogicalname`;
          return {
            id: guid,
            name: values[nameKey] ?? "Unknown",
            entityType: values[entityKey] ?? "",
          };
        }
        return null;
      }
      return values[fieldName];
    },
    [values]
  );

  const fieldElements = config.fields.map((field) => (
    <FieldRenderer
      key={field.name}
      config={field}
      value={getFieldValue(field.name, field.type)}
      onChange={onChange}
      disabled={disabled}
      metadata={metadata.get(field.name)}
    />
  ));

  // Flat mode or non-collapsible: render fields directly without section header
  if (flatMode || config.collapsible === false) {
    return (
      <div className={styles.fieldsContainer} style={{ padding: "4px 20px" }}>
        {fieldElements}
      </div>
    );
  }

  return (
    <CollapsibleSection
      title={config.title}
      defaultExpanded={config.defaultExpanded ?? true}
      expanded={expanded}
      onExpandedChange={handleExpandedChange}
    >
      <div className={styles.fieldsContainer}>
        {fieldElements}
      </div>
    </CollapsibleSection>
  );
};
