import * as React from "react";
import {
  makeStyles,
  tokens,
  Dropdown,
  Option,
  OptionGroup,
  Divider,
  Button,
  Tooltip,
} from "@fluentui/react-components";
import type { OptionOnSelectData } from "@fluentui/react-components";
import {
  LockClosedRegular,
  SettingsRegular,
  AddRegular,
} from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Summary of a workspace layout — enough for the header switcher. */
export interface WorkspaceLayoutSummary {
  /** Unique layout identifier. */
  id: string;
  /** Display name of the layout. */
  name: string;
  /** Whether this is a system-provided (non-editable) layout. */
  isSystem: boolean;
}

export interface IWorkspaceHeaderProps {
  /** The currently active workspace layout. */
  activeLayout: WorkspaceLayoutSummary;
  /** All available layouts (system + user). */
  layouts: WorkspaceLayoutSummary[];
  /** Called when the user selects a different layout from the dropdown. */
  onLayoutChange: (layoutId: string) => void;
  /** Called when the user clicks the settings gear button. */
  onEditClick: () => void;
  /** Called when the user selects "+ New Workspace" from the dropdown. */
  onCreateClick: () => void;
}

// ---------------------------------------------------------------------------
// Sentinel value for the "New Workspace" action option
// ---------------------------------------------------------------------------

const NEW_WORKSPACE_VALUE = "__new_workspace__";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 makeStyles / Griffel — semantic tokens for dark mode)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  dropdown: {
    minWidth: "200px",
    maxWidth: "320px",
  },
  optionContent: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
  lockIcon: {
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },
  newWorkspaceOption: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * WorkspaceHeader — dropdown switcher for workspace layouts with settings
 * and create actions.
 *
 * Pure presentational component: the parent supplies data and callbacks.
 * System layouts appear first (with a lock icon), followed by user layouts
 * after a divider. A "+ New Workspace" action sits at the bottom.
 */
export const WorkspaceHeader: React.FC<IWorkspaceHeaderProps> = ({
  activeLayout,
  layouts,
  onLayoutChange,
  onEditClick,
  onCreateClick,
}) => {
  const styles = useStyles();

  const systemLayouts = React.useMemo(
    () => layouts.filter((l) => l.isSystem),
    [layouts],
  );

  const userLayouts = React.useMemo(
    () => layouts.filter((l) => !l.isSystem),
    [layouts],
  );

  const handleOptionSelect = React.useCallback(
    (_event: unknown, data: OptionOnSelectData) => {
      const value = data.optionValue;
      if (!value) return;

      if (value === NEW_WORKSPACE_VALUE) {
        onCreateClick();
        return;
      }

      if (value !== activeLayout.id) {
        onLayoutChange(value);
      }
    },
    [activeLayout.id, onLayoutChange, onCreateClick],
  );

  const settingsTooltip = activeLayout.isSystem
    ? "Save As new workspace"
    : "Edit workspace";

  return (
    <div className={styles.root}>
      <Dropdown
        className={styles.dropdown}
        value={activeLayout.name}
        selectedOptions={[activeLayout.id]}
        onOptionSelect={handleOptionSelect}
        aria-label="Select workspace layout"
      >
        {/* System layouts */}
        {systemLayouts.length > 0 && (
          <OptionGroup label="System">
            {systemLayouts.map((layout) => (
              <Option key={layout.id} value={layout.id} text={layout.name}>
                <span className={styles.optionContent}>
                  <LockClosedRegular className={styles.lockIcon} fontSize={16} />
                  {layout.name}
                </span>
              </Option>
            ))}
          </OptionGroup>
        )}

        {/* Divider between system and user groups */}
        {systemLayouts.length > 0 && userLayouts.length > 0 && <Divider />}

        {/* User layouts */}
        {userLayouts.length > 0 && (
          <OptionGroup label="My Workspaces">
            {userLayouts.map((layout) => (
              <Option key={layout.id} value={layout.id} text={layout.name}>
                {layout.name}
              </Option>
            ))}
          </OptionGroup>
        )}

        {/* Divider before create action */}
        <Divider />

        {/* New Workspace action */}
        <Option
          value={NEW_WORKSPACE_VALUE}
          text="New Workspace"
        >
          <span className={styles.newWorkspaceOption}>
            <AddRegular fontSize={16} />
            New Workspace
          </span>
        </Option>
      </Dropdown>

      <Tooltip content={settingsTooltip} relationship="label">
        <Button
          appearance="subtle"
          icon={<SettingsRegular />}
          onClick={onEditClick}
          aria-label={settingsTooltip}
        />
      </Tooltip>
    </div>
  );
};
