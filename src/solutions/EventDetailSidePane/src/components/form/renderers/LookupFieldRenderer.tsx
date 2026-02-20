/**
 * LookupFieldRenderer - Renders a Dataverse lookup field
 *
 * Uses Xrm.Utility.lookupObjects to open the standard Dataverse lookup dialog.
 * Displays the selected record as a Persona (for contacts/users) or text.
 *
 * Value format in record values:
 *   _sprk_assignedto_value = "guid"
 *   _sprk_assignedto_value@OData.Community.Display.V1.FormattedValue = "Name"
 *   _sprk_assignedto_value@Microsoft.Dynamics.CRM.lookuplogicalname = "contact"
 *
 * On change, stores an ILookupValue object:
 *   { id: "guid", name: "Name", entityType: "contact" }
 *
 * @see approach-a-dynamic-form-renderer.md
 */

import * as React from "react";
import {
  Button,
  Text,
  Persona,
  makeStyles,
  shorthands,
  tokens,
} from "@fluentui/react-components";
import {
  SearchRegular,
  DismissCircleRegular,
} from "@fluentui/react-icons";
import type {
  IFieldConfig,
  ILookupValue,
  FieldChangeCallback,
} from "../../../types/FormConfig";
import { getXrm } from "../../../utils/xrmAccess";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
    width: "100%",
  },
  selectedRecord: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    ...shorthands.padding("6px", "10px"),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.border("1px", "solid", tokens.colorNeutralStroke1),
    width: "100%",
    minHeight: "32px",
  },
  recordInfo: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
    minWidth: 0,
    flexGrow: 1,
  },
  recordName: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  actions: {
    display: "flex",
    ...shorthands.gap("2px"),
    flexShrink: 0,
  },
  emptyState: {
    display: "flex",
    alignItems: "center",
    width: "100%",
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Lookup Dialog
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Open the standard Dataverse lookup dialog via Xrm.Utility.lookupObjects
 */
async function openLookupDialog(
  targets: string[]
): Promise<ILookupValue | null> {
  const xrm = getXrm();
  if (!xrm?.Utility?.lookupObjects) {
    console.error("[LookupField] Xrm.Utility.lookupObjects not available");
    return null;
  }

  const lookupOptions = {
    defaultEntityType: targets[0],
    entityTypes: targets,
    allowMultiSelect: false,
  };

  try {
    const result = await xrm.Utility.lookupObjects(lookupOptions);
    if (result && result.length > 0) {
      return {
        id: result[0].id.replace(/[{}]/g, "").toLowerCase(),
        name: result[0].name ?? "Unknown",
        entityType: result[0].entityType ?? targets[0],
      };
    }
    return null; // User cancelled
  } catch (error) {
    console.error("[LookupField] Lookup dialog error:", error);
    return null;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export interface LookupFieldRendererProps {
  config: IFieldConfig;
  value: unknown;
  onChange: FieldChangeCallback;
  disabled: boolean;
}

/**
 * Resolve the display value from the record values.
 * Lookup fields store value as ILookupValue or can be extracted from
 * the formatted OData annotations.
 */
function resolveLookupValue(
  value: unknown,
  fieldName: string,
  allValues?: Record<string, unknown>
): ILookupValue | null {
  // If value is already an ILookupValue (from user selection)
  if (value && typeof value === "object" && "id" in (value as Record<string, unknown>)) {
    const lv = value as ILookupValue;
    if (lv.id && lv.name) return lv;
  }

  // If value is a string GUID (from OData), try to get formatted name from allValues
  if (typeof value === "string" && value) {
    const lookupKey = `_${fieldName}_value`;
    const formattedKey = `${lookupKey}@OData.Community.Display.V1.FormattedValue`;
    const entityKey = `${lookupKey}@Microsoft.Dynamics.CRM.lookuplogicalname`;

    const name = allValues?.[formattedKey] as string | undefined;
    const entityType = allValues?.[entityKey] as string | undefined;

    return {
      id: value.replace(/[{}]/g, "").toLowerCase(),
      name: name ?? "Unknown",
      entityType: entityType ?? "",
    };
  }

  return null;
}

export const LookupFieldRenderer: React.FC<LookupFieldRendererProps> = ({
  config,
  value,
  onChange,
  disabled,
}) => {
  const styles = useStyles();
  const isDisabled = disabled || config.readOnly;
  const targets = config.targets ?? [];

  // Resolve the lookup value for display
  const lookupValue = React.useMemo(
    () => resolveLookupValue(value, config.name),
    [value, config.name]
  );

  const handleLookup = React.useCallback(async () => {
    if (isDisabled || targets.length === 0) return;

    const selected = await openLookupDialog(targets);
    if (selected) {
      onChange(config.name, selected);
    }
  }, [isDisabled, targets, config.name, onChange]);

  const handleClear = React.useCallback(() => {
    onChange(config.name, null);
  }, [config.name, onChange]);

  // Determine if this is a person-type lookup (for Persona display)
  const isPersonLookup = targets.some((t) =>
    ["contact", "systemuser", "team"].includes(t)
  );

  // Has a selected value
  if (lookupValue) {
    return (
      <div className={styles.selectedRecord}>
        <div className={styles.recordInfo}>
          {isPersonLookup ? (
            <Persona
              name={lookupValue.name}
              size="small"
              avatar={{ color: "colorful" }}
            />
          ) : (
            <Text size={300} className={styles.recordName} title={lookupValue.name}>
              {lookupValue.name}
            </Text>
          )}
        </div>
        {!isDisabled && (
          <div className={styles.actions}>
            <Button
              appearance="subtle"
              icon={<SearchRegular />}
              onClick={handleLookup}
              size="small"
              aria-label={`Change ${config.label}`}
              title="Change"
            />
            <Button
              appearance="subtle"
              icon={<DismissCircleRegular />}
              onClick={handleClear}
              size="small"
              aria-label={`Clear ${config.label}`}
              title="Clear"
            />
          </div>
        )}
      </div>
    );
  }

  // Empty state
  return (
    <div className={styles.emptyState}>
      <Button
        appearance="secondary"
        icon={<SearchRegular />}
        onClick={handleLookup}
        disabled={isDisabled}
        aria-label={`Select ${config.label}`}
        style={{ width: "100%" }}
      >
        Select {config.label}
      </Button>
    </div>
  );
};
