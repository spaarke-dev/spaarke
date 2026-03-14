/**
 * ApplicationPermissionsStep — Step 3 of the RegisterWizard.
 *
 * Displays a list of available application permissions for the consuming
 * application. At least one application permission must be selected to proceed.
 *
 * Application permissions are granted without a signed-in user (daemon/service
 * scenarios), in contrast to delegated permissions which act on behalf of users.
 *
 * ADR-006: Code Page — React 18 patterns, no PCF/ComponentFramework.
 * ADR-021: All styles use makeStyles + Fluent design tokens (no hard-coded colors).
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Checkbox,
  shorthands,
  Divider,
} from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Permission Definitions
// ─────────────────────────────────────────────────────────────────────────────

export interface AppPermissionDef {
  value: string;
  label: string;
  description: string;
}

/** All available SPE application (non-delegated) permissions with descriptions. */
export const APPLICATION_PERMISSIONS: AppPermissionDef[] = [
  {
    value: "ReadContent",
    label: "Read Content",
    description:
      "Allows the application to read file content and metadata inside containers without a signed-in user.",
  },
  {
    value: "WriteContent",
    label: "Write Content",
    description:
      "Allows the application to create, update, and delete files within containers without a signed-in user.",
  },
  {
    value: "Create",
    label: "Create",
    description:
      "Allows the application to create new containers of this type without a signed-in user.",
  },
  {
    value: "Delete",
    label: "Delete",
    description:
      "Allows the application to delete containers of this type without a signed-in user.",
  },
  {
    value: "ManagePermissions",
    label: "Manage Permissions",
    description:
      "Allows the application to view and modify access permissions on containers without a signed-in user.",
  },
  {
    value: "AddAllPermissions",
    label: "Add All Permissions",
    description:
      "Allows the application to add any permission level to containers without a signed-in user.",
  },
  {
    value: "ReadAllPermissions",
    label: "Read All Permissions",
    description:
      "Allows the application to read all permission entries on containers without a signed-in user.",
  },
  {
    value: "WriteAllPermissions",
    label: "Write All Permissions",
    description:
      "Allows the application to write all permission entries on containers without a signed-in user.",
  },
  {
    value: "DeleteAllPermissions",
    label: "Delete All Permissions",
    description:
      "Allows the application to remove any permission entry from containers without a signed-in user.",
  },
  {
    value: "EnumeratePermissions",
    label: "Enumerate Permissions",
    description:
      "Allows the application to list all permission entries across containers without a signed-in user.",
  },
];

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalL),
  },
  description: {
    color: tokens.colorNeutralForeground2,
  },
  permissionList: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalS),
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    overflow: "hidden",
  },
  permissionItem: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: "pointer",
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  permissionItemSelected: {
    backgroundColor: tokens.colorBrandBackground2,
    ":hover": {
      backgroundColor: tokens.colorBrandBackground2Hover,
    },
  },
  permissionText: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalXS),
    marginLeft: tokens.spacingHorizontalS,
  },
  permissionLabel: {
    color: tokens.colorNeutralForeground1,
  },
  permissionDescription: {
    color: tokens.colorNeutralForeground3,
  },
  validationMessage: {
    color: tokens.colorPaletteRedForeground1,
  },
  selectAllRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  infoBox: {
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface ApplicationPermissionsStepProps {
  /** Currently selected application permissions (controlled by RegisterWizard). */
  selectedPermissions: string[];
  /** Called when the selection changes. */
  onPermissionsChanged: (permissions: string[]) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// ApplicationPermissionsStep
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Step 3 of the registration wizard — choose application permissions.
 *
 * Renders a checkbox list with descriptions. At least one permission
 * must be selected to allow advancing to the next step.
 */
export const ApplicationPermissionsStep: React.FC<ApplicationPermissionsStepProps> = ({
  selectedPermissions,
  onPermissionsChanged,
}) => {
  const styles = useStyles();

  const allSelected = selectedPermissions.length === APPLICATION_PERMISSIONS.length;
  const someSelected = selectedPermissions.length > 0 && !allSelected;

  const handleToggle = React.useCallback(
    (value: string) => {
      if (selectedPermissions.includes(value)) {
        onPermissionsChanged(selectedPermissions.filter((p) => p !== value));
      } else {
        onPermissionsChanged([...selectedPermissions, value]);
      }
    },
    [selectedPermissions, onPermissionsChanged]
  );

  const handleSelectAll = React.useCallback(() => {
    if (allSelected) {
      onPermissionsChanged([]);
    } else {
      onPermissionsChanged(APPLICATION_PERMISSIONS.map((p) => p.value));
    }
  }, [allSelected, onPermissionsChanged]);

  return (
    <div className={styles.root}>
      <Text size={400} weight="semibold">
        Choose Application Permissions
      </Text>
      <Text size={300} className={styles.description}>
        Select the application permissions granted to the consuming app for
        service-to-service operations (no signed-in user). At least one
        permission is required.
      </Text>

      {/* Info box explaining difference from delegated */}
      <div className={styles.infoBox}>
        <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
          Application permissions apply to daemon or background services that
          access SharePoint Embedded containers without user interaction. These
          are distinct from the delegated permissions granted in the previous step.
        </Text>
      </div>

      {/* Select All toggle */}
      <div className={styles.selectAllRow}>
        <Checkbox
          checked={allSelected ? true : someSelected ? "mixed" : false}
          onChange={handleSelectAll}
          label={
            <Text size={200} weight="semibold">
              {allSelected ? "Deselect All" : "Select All"}
            </Text>
          }
        />
        <Text size={200} className={styles.description}>
          ({selectedPermissions.length} of {APPLICATION_PERMISSIONS.length} selected)
        </Text>
      </div>

      <Divider />

      {/* Permission list */}
      <div className={styles.permissionList}>
        {APPLICATION_PERMISSIONS.map((perm, idx) => {
          const isSelected = selectedPermissions.includes(perm.value);
          return (
            <React.Fragment key={perm.value}>
              {idx > 0 && (
                <Divider style={{ margin: "0" }} />
              )}
              <div
                className={`${styles.permissionItem} ${isSelected ? styles.permissionItemSelected : ""}`}
                onClick={() => handleToggle(perm.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter" || e.key === " ") {
                    e.preventDefault();
                    handleToggle(perm.value);
                  }
                }}
                role="checkbox"
                aria-checked={isSelected}
                tabIndex={0}
              >
                <Checkbox
                  checked={isSelected}
                  onChange={() => handleToggle(perm.value)}
                  tabIndex={-1}
                  aria-label={perm.label}
                />
                <div className={styles.permissionText}>
                  <Text size={300} weight="semibold" className={styles.permissionLabel}>
                    {perm.label}
                  </Text>
                  <Text size={200} className={styles.permissionDescription}>
                    {perm.description}
                  </Text>
                </div>
              </div>
            </React.Fragment>
          );
        })}
      </div>

      {/* Validation hint */}
      {selectedPermissions.length === 0 && (
        <Text size={200} className={styles.validationMessage}>
          At least one application permission must be selected to continue.
        </Text>
      )}
    </div>
  );
};
