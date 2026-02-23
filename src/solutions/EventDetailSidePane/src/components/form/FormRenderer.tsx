/**
 * FormRenderer - Top-level dynamic form renderer
 *
 * Reads an IFormConfig (from sprk_fieldconfigjson) and renders all
 * sections and fields dynamically. This is the core component of
 * Approach A — the JSON IS the form definition.
 *
 * Entity-agnostic: works for Events, Matters, Projects, etc.
 * The entityName prop is used for metadata fetching (optionset labels).
 *
 * @see approach-a-dynamic-form-renderer.md
 * @see ADR-012 - Shared Component Library
 */

import * as React from "react";
import { Spinner, makeStyles, shorthands } from "@fluentui/react-components";
import { SectionRenderer } from "./SectionRenderer";
import { useFieldMetadata } from "../../hooks/useFieldMetadata";
import type { IFormConfig, FieldChangeCallback } from "../../types/FormConfig";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
  },
  loading: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.padding("24px"),
  },
  empty: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.padding("24px"),
    color: "var(--colorNeutralForeground3)",
    fontStyle: "italic",
  },
});

export interface FormRendererProps {
  /** The parsed form configuration from sprk_fieldconfigjson */
  config: IFormConfig | null;
  /** Dataverse entity logical name (for metadata queries) */
  entityName: string;
  /** Current field values from the record */
  values: Record<string, unknown>;
  /** Callback when any field value changes */
  onChange: FieldChangeCallback;
  /** Whether all fields are disabled (read-only mode or saving) */
  disabled: boolean;
  /** Whether the config is still loading */
  isLoading?: boolean;
  /** Render fields in a flat list without section headers (default: false) */
  flatMode?: boolean;
  /** Section expanded states (for persistence across tab switches) */
  sectionStates?: Record<string, boolean>;
  /** Callback when a section's expanded state changes */
  onSectionExpandedChange?: (sectionId: string, expanded: boolean) => void;
}

export const FormRenderer: React.FC<FormRendererProps> = ({
  config,
  entityName,
  values,
  onChange,
  disabled,
  isLoading = false,
  flatMode = false,
  sectionStates,
  onSectionExpandedChange,
}) => {
  const styles = useStyles();

  // Fetch optionset metadata for choice fields
  const metadata = useFieldMetadata(entityName, config);

  if (isLoading) {
    return (
      <div className={styles.loading}>
        <Spinner size="small" label="Loading form configuration..." />
      </div>
    );
  }

  if (!config || config.sections.length === 0) {
    return (
      <div className={styles.empty}>
        No form configuration available
      </div>
    );
  }

  return (
    <div className={styles.container}>
      {config.sections.map((section) => (
        <SectionRenderer
          key={section.id}
          config={section}
          values={values}
          onChange={onChange}
          disabled={disabled}
          metadata={metadata}
          flatMode={flatMode}
          expanded={sectionStates?.[section.id]}
          onExpandedChange={onSectionExpandedChange}
        />
      ))}
    </div>
  );
};
