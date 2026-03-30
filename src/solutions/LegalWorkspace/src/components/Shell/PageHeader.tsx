import * as React from "react";
import {
  makeStyles,
  tokens,
  Button,
  CounterBadge,
  Dropdown,
  Option,
  OptionGroup,
  Divider,
  Tooltip,
} from "@fluentui/react-components";
import type { OptionOnSelectData } from "@fluentui/react-components";
import {
  AlertRegular,
  SettingsRegular,
  AddRegular,
  LockClosedRegular,
} from "@fluentui/react-icons";
import { ThemeToggle } from "./ThemeToggle";
import { NotificationPanel } from "../NotificationPanel/NotificationPanel";
import { useNotifications } from "../../hooks/useNotifications";
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
  notificationWrapper: {
    position: "relative",
    display: "inline-flex",
    alignItems: "center",
  },
  badge: {
    position: "absolute",
    top: "0px",
    right: "0px",
    transform: "translate(30%, -30%)",
    pointerEvents: "none",
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
}

export const PageHeader: React.FC<IPageHeaderProps> = ({
  activeLayout,
  layouts,
  onLayoutChange,
  onEditClick,
  onCreateClick,
}) => {
  const styles = useStyles();
  const [isNotificationPanelOpen, setIsNotificationPanelOpen] =
    React.useState<boolean>(false);

  const {
    notifications,
    isLoading,
    unreadCount,
    markAsRead,
    markAllAsRead,
    refresh,
  } = useNotifications();

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

  const handleNotificationClick = React.useCallback(() => {
    setIsNotificationPanelOpen((prev) => !prev);
  }, []);

  const handleClosePanel = React.useCallback(() => {
    setIsNotificationPanelOpen(false);
  }, []);

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
                  {layout.name}
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
          <div className={styles.notificationWrapper}>
            <Button
              appearance="subtle"
              icon={<AlertRegular />}
              onClick={handleNotificationClick}
              aria-label={
                unreadCount > 0
                  ? `Notifications (${unreadCount} unread)`
                  : "Notifications"
              }
              aria-expanded={isNotificationPanelOpen}
              aria-controls="notification-panel"
            />
            {unreadCount > 0 && (
              <CounterBadge
                className={styles.badge}
                count={unreadCount}
                size="small"
                color="danger"
                appearance="filled"
                aria-hidden="true"
              />
            )}
            {/* Screen reader live region for notification count changes */}
            <span
              role="status"
              aria-live="polite"
              aria-atomic="true"
              style={{ position: "absolute", width: "1px", height: "1px", overflow: "hidden", clip: "rect(0,0,0,0)", whiteSpace: "nowrap" }}
            >
              {unreadCount > 0 ? `${unreadCount} unread notification${unreadCount === 1 ? "" : "s"}` : ""}
            </span>
          </div>

          <ThemeToggle />
        </div>
      </header>

      {/* Notification panel drawer — rendered adjacent to header but portals to document.body */}
      <NotificationPanel
        isOpen={isNotificationPanelOpen}
        onClose={handleClosePanel}
        notifications={notifications}
        isLoading={isLoading}
        onMarkAsRead={markAsRead}
        onMarkAllAsRead={markAllAsRead}
        onRefresh={refresh}
      />
    </>
  );
};
