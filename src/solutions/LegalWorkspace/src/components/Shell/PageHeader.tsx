import * as React from "react";
import {
  makeStyles,
  tokens,
  Button,
  Dropdown,
  Option,
  OptionGroup,
  Divider,
  Tooltip,
} from "@fluentui/react-components";
import type { OptionOnSelectData } from "@fluentui/react-components";
import {
  SettingsRegular,
  AddRegular,
  LockClosedRegular,
  DeleteRegular,
} from "@fluentui/react-icons";
import { ThemeToggle } from "@spaarke/ui-components";
import type { WorkspaceLayoutSummary } from "../WorkspaceHeader";

const useStyles = makeStyles({
  header: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    boxSizing: "border-box",
  },
  title: {
    color: tokens.colorNeutralForeground1,
    flex: "1 1 auto",
    minWidth: 0,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  dropdown: {
    minWidth: "200px",
    maxWidth: "320px",
    // Override Fluent Dropdown's internal button to match Power Apps view selector
    "& button": {
      fontSize: "20px",
      fontFamily: tokens.fontFamilyBase,
      color: tokens.colorNeutralForeground1,
      fontWeight: tokens.fontWeightSemibold,
      borderBottomColor: "transparent",
    },
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
  newWorkspaceOption: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  actions: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flex: "0 0 auto",
  },
});

const NEW_WORKSPACE_VALUE = "__new_workspace__";

export interface IPageHeaderProps {
  /** The currently active workspace layout. */
  activeLayout?: WorkspaceLayoutSummary;
  /** All available layouts (system + user). */
  layouts?: WorkspaceLayoutSummary[];
  /** Called when the user selects a different layout from the dropdown. */
  onLayoutChange?: (layoutId: string) => void;
  /** Called when the user clicks the settings gear button. */
  onEditClick?: () => void;
  /** Called when the user selects "+ New Workspace" from the dropdown. */
  onCreateClick?: () => void;
  /** Called when the user clicks the delete icon on a user workspace. */
  onDeleteClick?: (layoutId: string) => void;
}

export const PageHeader: React.FC<IPageHeaderProps> = ({
  activeLayout,
  layouts,
  onLayoutChange,
  onEditClick,
  onCreateClick,
  onDeleteClick,
}) => {
  const styles = useStyles();

  const systemLayouts = React.useMemo(
    () => (layouts ?? []).filter((l) => l.isSystem),
    [layouts],
  );
  const userLayouts = React.useMemo(
    () => (layouts ?? []).filter((l) => !l.isSystem),
    [layouts],
  );

  const handleOptionSelect = React.useCallback(
    (_event: unknown, data: OptionOnSelectData) => {
      const value = data.optionValue;
      if (!value) return;
      if (value === NEW_WORKSPACE_VALUE) {
        onCreateClick?.();
        return;
      }
      if (value !== activeLayout?.id) {
        onLayoutChange?.(value);
      }
    },
    [activeLayout?.id, onLayoutChange, onCreateClick],
  );

  const settingsTooltip = activeLayout?.isSystem
    ? "Save As new workspace"
    : "Edit workspace";

  return (
    <>
      <header className={styles.header} role="banner">
        {/* Workspace name as dropdown selector — always rendered, no flash */}
        <Dropdown
          className={styles.dropdown}
          value={activeLayout?.name ?? "Corporate Workspace"}
          selectedOptions={activeLayout ? [activeLayout.id] : []}
          onOptionSelect={handleOptionSelect}
          aria-label="Select workspace layout"
          appearance="filled-lighter"
          disabled={!activeLayout}
        >
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
          {systemLayouts.length > 0 && userLayouts.length > 0 && <Divider />}
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
                          onDeleteClick(layout.id);
                        }}
                        aria-label={`Delete ${layout.name}`}
                      />
                    )}
                  </span>
                </Option>
              ))}
            </OptionGroup>
          )}
          <Divider />
          <Option value={NEW_WORKSPACE_VALUE} text="New Workspace">
            <span className={styles.newWorkspaceOption}>
              <AddRegular fontSize={16} />
              New Workspace
            </span>
          </Option>
        </Dropdown>

        <div className={styles.actions}>
          {/* Gear button — workspace layout settings */}
          {onEditClick && (
            <Tooltip content={settingsTooltip} relationship="label">
              <Button
                appearance="subtle"
                icon={<SettingsRegular />}
                onClick={onEditClick}
                aria-label={settingsTooltip}
              />
            </Tooltip>
          )}
          <ThemeToggle />
        </div>
      </header>
    </>
  );
};
