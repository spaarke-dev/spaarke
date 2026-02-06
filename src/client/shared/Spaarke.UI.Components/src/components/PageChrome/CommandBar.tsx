/**
 * CommandBar Component
 *
 * OOB-style command bar for Custom Pages.
 * Matches Power Apps entity homepage ribbon styling.
 *
 * Features:
 * - Standard commands: New, Delete, Refresh
 * - Custom commands via props
 * - Selection-aware delete button
 * - Optional search box
 * - Dark mode support
 * - Keyboard shortcuts
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
  Input,
  Badge,
  makeStyles,
  tokens,
  mergeClasses,
} from "@fluentui/react-components";
import {
  Add20Regular,
  Delete20Regular,
  ArrowClockwise20Regular,
  Search20Regular,
  MoreHorizontal20Regular,
} from "@fluentui/react-icons";
import { useKeyboardShortcuts } from "../../hooks/useKeyboardShortcuts";

/**
 * Command item for custom commands
 */
export interface ICommandBarItem {
  /** Unique key for the command */
  key: string;
  /** Display label */
  label: string;
  /** Fluent icon element */
  icon?: React.ReactElement;
  /** Whether command is disabled */
  disabled?: boolean;
  /** Tooltip description */
  description?: string;
  /** Click handler */
  onClick?: () => void;
  /** Show divider after this command */
  dividerAfter?: boolean;
}

/**
 * Props for CommandBar component
 */
export interface ICommandBarProps {
  /** Entity logical name for context */
  entityLogicalName: string;
  /** Currently selected record IDs */
  selectedIds?: string[];
  /** Custom commands to render */
  commands?: ICommandBarItem[];
  /** Handler for New button */
  onNew?: () => void;
  /** Handler for Delete button */
  onDelete?: (selectedIds: string[]) => void;
  /** Handler for Refresh button */
  onRefresh?: () => void;
  /** Handler for search */
  onSearch?: (searchText: string) => void;
  /** Show the New button (default: true) */
  showNew?: boolean;
  /** Show the Delete button (default: true) */
  showDelete?: boolean;
  /** Show the Refresh button (default: true) */
  showRefresh?: boolean;
  /** Show the Search box (default: false) */
  showSearch?: boolean;
  /** Search placeholder text */
  searchPlaceholder?: string;
  /** Whether New action is allowed (security) */
  canCreate?: boolean;
  /** Whether Delete action is allowed (security) */
  canDelete?: boolean;
  /** Compact mode (smaller height) */
  compact?: boolean;
  /** Additional CSS class name */
  className?: string;
}

const useStyles = makeStyles({
  toolbar: {
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    minHeight: "44px",
    display: "flex",
    alignItems: "center",
    flexWrap: "nowrap",
  },
  toolbarCompact: {
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    minHeight: "36px",
  },
  leftGroup: {
    display: "flex",
    alignItems: "center",
    flexGrow: 1,
  },
  rightGroup: {
    display: "flex",
    alignItems: "center",
    marginLeft: "auto",
  },
  searchBox: {
    width: "200px",
    marginLeft: tokens.spacingHorizontalM,
  },
  deleteBadge: {
    marginLeft: tokens.spacingHorizontalXS,
  },
  buttonLabel: {
    marginLeft: tokens.spacingHorizontalXS,
  },
});

/**
 * CommandBar - OOB-style command bar for Power Apps Custom Pages
 */
export const CommandBar: React.FC<ICommandBarProps> = ({
  entityLogicalName,
  selectedIds = [],
  commands = [],
  onNew,
  onDelete,
  onRefresh,
  onSearch,
  showNew = true,
  showDelete = true,
  showRefresh = true,
  showSearch = false,
  searchPlaceholder = "Search...",
  canCreate = true,
  canDelete = true,
  compact = false,
  className,
}) => {
  const styles = useStyles();
  const [searchText, setSearchText] = React.useState("");

  const hasSelection = selectedIds.length > 0;
  const selectionCount = selectedIds.length;

  // Keyboard shortcuts
  const shortcuts = React.useMemo(
    () => [
      {
        key: "ctrl+n",
        handler: () => {
          if (showNew && canCreate && onNew) {
            onNew();
          }
        },
        description: "Create new record",
      },
      {
        key: "delete",
        handler: () => {
          if (showDelete && canDelete && hasSelection && onDelete) {
            onDelete(selectedIds);
          }
        },
        description: "Delete selected records",
      },
      {
        key: "f5",
        handler: (e: KeyboardEvent) => {
          e.preventDefault();
          if (showRefresh && onRefresh) {
            onRefresh();
          }
        },
        description: "Refresh data",
      },
    ],
    [showNew, showDelete, showRefresh, canCreate, canDelete, hasSelection, selectedIds, onNew, onDelete, onRefresh]
  );

  useKeyboardShortcuts(shortcuts);

  // Handle search
  const handleSearchChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const value = e.target.value;
      setSearchText(value);
    },
    []
  );

  const handleSearchKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === "Enter" && onSearch) {
        onSearch(searchText);
      }
    },
    [searchText, onSearch]
  );

  // Handle delete click
  const handleDeleteClick = React.useCallback(() => {
    if (onDelete && hasSelection) {
      onDelete(selectedIds);
    }
  }, [onDelete, hasSelection, selectedIds]);

  return (
    <Toolbar
      aria-label={`${entityLogicalName} command bar`}
      className={mergeClasses(
        styles.toolbar,
        compact && styles.toolbarCompact,
        className
      )}
    >
      {/* Left group - Primary commands */}
      <ToolbarGroup className={styles.leftGroup}>
        {/* New button */}
        {showNew && (
          <Tooltip
            content={
              <>
                New {entityLogicalName}
                <span style={{ marginLeft: "8px", opacity: 0.7 }}>Ctrl+N</span>
              </>
            }
            relationship="description"
          >
            <ToolbarButton
              icon={<Add20Regular />}
              disabled={!canCreate}
              onClick={onNew}
              aria-label={`Create new ${entityLogicalName}`}
              aria-keyshortcuts="Control+N"
            >
              <span className={styles.buttonLabel}>New</span>
            </ToolbarButton>
          </Tooltip>
        )}

        {/* Delete button */}
        {showDelete && (
          <>
            {showNew && <ToolbarDivider />}
            <Tooltip
              content={
                hasSelection
                  ? `Delete ${selectionCount} selected ${selectionCount === 1 ? "record" : "records"}`
                  : "Select records to delete"
              }
              relationship="description"
            >
              <ToolbarButton
                icon={<Delete20Regular />}
                disabled={!canDelete || !hasSelection}
                onClick={handleDeleteClick}
                aria-label={`Delete selected ${entityLogicalName} records`}
                aria-keyshortcuts="Delete"
              >
                <span className={styles.buttonLabel}>Delete</span>
                {hasSelection && (
                  <Badge
                    appearance="filled"
                    color="danger"
                    size="small"
                    className={styles.deleteBadge}
                  >
                    {selectionCount}
                  </Badge>
                )}
              </ToolbarButton>
            </Tooltip>
          </>
        )}

        {/* Refresh button */}
        {showRefresh && (
          <>
            {(showNew || showDelete) && <ToolbarDivider />}
            <Tooltip
              content={
                <>
                  Refresh data
                  <span style={{ marginLeft: "8px", opacity: 0.7 }}>F5</span>
                </>
              }
              relationship="description"
            >
              <ToolbarButton
                icon={<ArrowClockwise20Regular />}
                onClick={onRefresh}
                aria-label="Refresh data"
                aria-keyshortcuts="F5"
              >
                {!compact && <span className={styles.buttonLabel}>Refresh</span>}
              </ToolbarButton>
            </Tooltip>
          </>
        )}

        {/* Divider before custom commands */}
        {commands.length > 0 && (showNew || showDelete || showRefresh) && (
          <ToolbarDivider />
        )}

        {/* Custom commands */}
        {commands.map((command) => (
          <React.Fragment key={command.key}>
            <Tooltip
              content={command.description || command.label}
              relationship="description"
            >
              <ToolbarButton
                icon={command.icon}
                disabled={command.disabled}
                onClick={command.onClick}
                aria-label={command.label}
              >
                {!compact && <span className={styles.buttonLabel}>{command.label}</span>}
              </ToolbarButton>
            </Tooltip>
            {command.dividerAfter && <ToolbarDivider />}
          </React.Fragment>
        ))}
      </ToolbarGroup>

      {/* Right group - Search and overflow */}
      {showSearch && (
        <ToolbarGroup className={styles.rightGroup}>
          <Input
            className={styles.searchBox}
            contentBefore={<Search20Regular />}
            placeholder={searchPlaceholder}
            value={searchText}
            onChange={handleSearchChange}
            onKeyDown={handleSearchKeyDown}
            size={compact ? "small" : "medium"}
            aria-label="Search records"
          />
        </ToolbarGroup>
      )}
    </Toolbar>
  );
};

// Default export for convenience
export default CommandBar;
