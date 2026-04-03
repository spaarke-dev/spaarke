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
  DeleteRegular,
  CheckmarkSquareRegular,
} from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Summary of a workspace layout — enough for the header switcher. */
export interface WorkspaceLayoutSummary {
  id: string;
  name: string;
  isSystem: boolean;
}

export interface IWorkspaceHeaderProps {
  activeLayout: WorkspaceLayoutSummary;
  layouts: WorkspaceLayoutSummary[];
  onLayoutChange: (layoutId: string) => void;
  onEditClick: () => void;
  onCreateClick: () => void;
  onDeleteClick?: (layoutId: string) => void;
  onSetDefaultClick?: (layoutId: string) => void;
}

// ---------------------------------------------------------------------------
// Sentinel values for action options
// ---------------------------------------------------------------------------

const NEW_WORKSPACE_VALUE = "__new_workspace__";
const SET_DEFAULT_VALUE = "__set_default__";

// ---------------------------------------------------------------------------
// Styles
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
  userOptionContent: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    width: "100%",
  },
  deleteButton: {
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
    ":hover": {
      color: tokens.colorPaletteRedForeground1,
    },
  },
  actionOption: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground2,
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

export const WorkspaceHeader: React.FC<IWorkspaceHeaderProps> = ({
  activeLayout,
  layouts,
  onLayoutChange,
  onEditClick,
  onCreateClick,
  onDeleteClick,
  onSetDefaultClick,
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

      if (value === SET_DEFAULT_VALUE) {
        // Set the currently active layout as default
        if (!activeLayout.isSystem && onSetDefaultClick) {
          onSetDefaultClick(activeLayout.id);
        }
        return;
      }

      if (value !== activeLayout.id) {
        onLayoutChange(value);
      }
    },
    [activeLayout.id, activeLayout.isSystem, onLayoutChange, onCreateClick, onSetDefaultClick],
  );

  const settingsTooltip = activeLayout.isSystem
    ? "Save As new workspace"
    : "Edit workspace";

  // Can set default if active layout is a user layout
  const canSetDefault = !activeLayout.isSystem && !!onSetDefaultClick;

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
                <span className={styles.userOptionContent}>
                  {layout.name}
                  {onDeleteClick && (
                    <Button
                      appearance="subtle"
                      size="small"
                      icon={<DeleteRegular fontSize={16} />}
                      className={styles.deleteButton}
                      onClick={(e) => {
                        e.stopPropagation();
                        e.preventDefault();
                        // setTimeout escapes the Dropdown's click handler
                        // so the confirm dialog isn't swallowed
                        setTimeout(() => onDeleteClick(layout.id), 0);
                      }}
                      aria-label={`Delete ${layout.name}`}
                    />
                  )}
                </span>
              </Option>
            ))}
          </OptionGroup>
        )}

        {/* Actions divider */}
        <Divider />

        {/* Set as default (only for user layouts) */}
        {canSetDefault && (
          <Option value={SET_DEFAULT_VALUE} text="Set as default view">
            <span className={styles.actionOption}>
              <CheckmarkSquareRegular fontSize={16} />
              Set as default view
            </span>
          </Option>
        )}

        {/* New Workspace action */}
        <Option value={NEW_WORKSPACE_VALUE} text="New Workspace">
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
