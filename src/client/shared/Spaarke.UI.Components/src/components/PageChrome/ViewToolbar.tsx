/**
 * ViewToolbar Component
 *
 * Horizontal toolbar row below the CommandBar containing the ViewSelector
 * and optional Edit filters / Edit columns buttons.
 * Styled to match OOB Power Apps view toolbar.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 * @see ADR-021 Fluent UI v9 Design System
 */

import * as React from "react";
import {
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  ToolbarGroup,
  Tooltip,
  Text,
  makeStyles,
  tokens,
  mergeClasses,
} from "@fluentui/react-components";
import {
  Filter20Regular,
  TableSettings20Regular,
  ChevronDown20Regular,
} from "@fluentui/react-icons";

/**
 * Props for ViewToolbar component
 */
export interface IViewToolbarProps {
  /** Children - typically ViewSelector component */
  children?: React.ReactNode;
  /** View name to display (when not using children) */
  viewName?: string;
  /** Record count to display */
  recordCount?: number;
  /** Show "Edit filters" button */
  showEditFilters?: boolean;
  /** Show "Edit columns" button */
  showEditColumns?: boolean;
  /** Handler for "Edit filters" click */
  onEditFilters?: () => void;
  /** Handler for "Edit columns" click */
  onEditColumns?: () => void;
  /** Handler for view dropdown click (when using viewName instead of children) */
  onViewClick?: () => void;
  /** Compact mode */
  compact?: boolean;
  /** Additional CSS class */
  className?: string;
}

const useStyles = makeStyles({
  toolbar: {
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    minHeight: "36px",
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
  },
  toolbarCompact: {
    minHeight: "32px",
    paddingTop: "2px",
    paddingBottom: "2px",
  },
  leftSection: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalM,
    flexGrow: 1,
  },
  rightSection: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  viewNameButton: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    cursor: "pointer",
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: "transparent",
    borderWidth: "0",
    fontFamily: "inherit",
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    "&:hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    "&:active": {
      backgroundColor: tokens.colorNeutralBackground1Pressed,
    },
  },
  viewName: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  recordCount: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    marginLeft: tokens.spacingHorizontalS,
  },
  chevron: {
    color: tokens.colorNeutralForeground3,
  },
  actionButton: {
    color: tokens.colorNeutralForeground2,
  },
  divider: {
    height: "20px",
  },
});

/**
 * ViewToolbar - Horizontal bar below CommandBar with view selector and actions
 */
export const ViewToolbar: React.FC<IViewToolbarProps> = ({
  children,
  viewName,
  recordCount,
  showEditFilters = false,
  showEditColumns = false,
  onEditFilters,
  onEditColumns,
  onViewClick,
  compact = false,
  className,
}) => {
  const styles = useStyles();

  const hasRightSection = showEditFilters || showEditColumns;

  return (
    <div
      className={mergeClasses(
        styles.toolbar,
        compact && styles.toolbarCompact,
        className
      )}
      role="toolbar"
      aria-label="View toolbar"
    >
      {/* Left section - View selector */}
      <div className={styles.leftSection}>
        {children ? (
          // Render children (ViewSelector component)
          <>
            {children}
            {recordCount !== undefined && (
              <Text className={styles.recordCount}>
                ({recordCount.toLocaleString()} {recordCount === 1 ? "record" : "records"})
              </Text>
            )}
          </>
        ) : viewName ? (
          // Render simple view name button
          <button
            className={styles.viewNameButton}
            onClick={onViewClick}
            aria-label="Change view"
            aria-haspopup="listbox"
          >
            <span className={styles.viewName}>{viewName}</span>
            <ChevronDown20Regular className={styles.chevron} />
            {recordCount !== undefined && (
              <Text className={styles.recordCount}>
                ({recordCount.toLocaleString()})
              </Text>
            )}
          </button>
        ) : null}
      </div>

      {/* Right section - Action buttons */}
      {hasRightSection && (
        <div className={styles.rightSection}>
          {showEditFilters && (
            <Tooltip content="Edit filters" relationship="label">
              <ToolbarButton
                icon={<Filter20Regular />}
                onClick={onEditFilters}
                className={styles.actionButton}
                aria-label="Edit filters"
                size={compact ? "small" : "medium"}
              >
                {!compact && "Edit filters"}
              </ToolbarButton>
            </Tooltip>
          )}

          {showEditFilters && showEditColumns && (
            <ToolbarDivider className={styles.divider} />
          )}

          {showEditColumns && (
            <Tooltip content="Edit columns" relationship="label">
              <ToolbarButton
                icon={<TableSettings20Regular />}
                onClick={onEditColumns}
                className={styles.actionButton}
                aria-label="Edit columns"
                size={compact ? "small" : "medium"}
              >
                {!compact && "Edit columns"}
              </ToolbarButton>
            </Tooltip>
          )}
        </div>
      )}
    </div>
  );
};

// Default export for convenience
export default ViewToolbar;
