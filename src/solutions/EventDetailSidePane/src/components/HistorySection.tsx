/**
 * HistorySection - Collapsible history/audit section for Event Detail Side Pane
 *
 * Displays audit trail and activity history in a collapsible section:
 * - Created by / Created on
 * - Modified by / Modified on
 * - Status changes (timeline)
 *
 * Implements lazy loading - data is only fetched when section is expanded.
 * Read-only display only.
 *
 * Uses Fluent UI v9 components with proper theming support.
 *
 * @see projects/events-workspace-apps-UX-r1/design.md - History section spec
 * @see ADR-021 - Fluent UI v9, dark mode support
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Spinner,
  Persona,
  Badge,
  Divider,
} from "@fluentui/react-components";
import {
  HistoryRegular,
  PersonRegular,
  CalendarClockRegular,
  CircleSmallFilled,
  ArrowCircleRightRegular,
} from "@fluentui/react-icons";
import { CollapsibleSection } from "./CollapsibleSection";

// -----------------------------------------------------------------------------
// Types
// -----------------------------------------------------------------------------

/**
 * User information for created by / modified by
 */
export interface UserInfo {
  /** User ID (guid) */
  id: string;
  /** User display name */
  name: string;
  /** User email (optional) */
  email?: string;
  /** User avatar URL (optional) */
  avatarUrl?: string;
}

/**
 * Status change history entry
 */
export interface StatusChangeEntry {
  /** Unique identifier for this entry */
  id: string;
  /** Previous status reason value */
  fromStatus?: number | null;
  /** Previous status label */
  fromStatusLabel?: string;
  /** New status reason value */
  toStatus: number;
  /** New status label */
  toStatusLabel: string;
  /** When the change occurred */
  changedOn: string;
  /** Who made the change */
  changedBy: UserInfo;
}

/**
 * History data structure
 */
export interface HistoryData {
  /** Who created the record */
  createdBy: UserInfo | null;
  /** When the record was created */
  createdOn: string | null;
  /** Who last modified the record */
  modifiedBy: UserInfo | null;
  /** When the record was last modified */
  modifiedOn: string | null;
  /** Status change history (optional, may require additional queries) */
  statusChanges?: StatusChangeEntry[];
}

/**
 * Props for the HistorySection component
 */
export interface HistorySectionProps {
  /** History data (null if not yet loaded) */
  historyData: HistoryData | null;
  /** Callback to fetch history data (called on expand) */
  onLoadHistory?: () => void | Promise<void>;
  /** Whether history is currently loading */
  isLoading?: boolean;
  /** Error message if loading failed */
  error?: string | null;
  /** Whether the section starts expanded (default: false) */
  defaultExpanded?: boolean;
  /** Controlled expanded state */
  expanded?: boolean;
  /** Callback when expanded state changes */
  onExpandedChange?: (expanded: boolean) => void;
}

// -----------------------------------------------------------------------------
// Styles
// -----------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("16px"),
  },
  section: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("8px"),
  },
  sectionTitle: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("6px"),
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    textTransform: "uppercase",
    letterSpacing: "0.5px",
  },
  sectionIcon: {
    fontSize: "12px",
    color: tokens.colorNeutralForeground3,
  },
  userEntry: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("12px"),
    ...shorthands.padding("8px", "12px"),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
  },
  userInfo: {
    display: "flex",
    flexDirection: "column",
    flexGrow: 1,
    minWidth: 0,
  },
  userName: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  dateText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  timeline: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("0"),
    position: "relative",
    marginLeft: "8px",
    paddingLeft: "16px",
    borderLeft: `2px solid ${tokens.colorNeutralStroke2}`,
  },
  timelineEntry: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("4px"),
    ...shorthands.padding("12px", "0"),
    position: "relative",
  },
  timelineEntryFirst: {
    paddingTop: "0",
  },
  timelineEntryLast: {
    paddingBottom: "0",
  },
  timelineDot: {
    position: "absolute",
    left: "-23px",
    top: "14px",
    width: "12px",
    height: "12px",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorBrandForeground1,
  },
  timelineDotFirst: {
    top: "2px",
  },
  statusChange: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("6px"),
    flexWrap: "wrap",
  },
  statusBadge: {
    fontSize: tokens.fontSizeBase200,
  },
  arrowIcon: {
    color: tokens.colorNeutralForeground3,
    fontSize: "14px",
  },
  changeInfo: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("6px"),
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  changeUser: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
  loadingContainer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.padding("24px"),
  },
  errorContainer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.padding("16px"),
    backgroundColor: tokens.colorPaletteRedBackground1,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
  },
  emptyState: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.padding("24px"),
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontStyle: "italic",
  },
  divider: {
    marginTop: "8px",
    marginBottom: "8px",
  },
});

// -----------------------------------------------------------------------------
// Helper Functions
// -----------------------------------------------------------------------------

/**
 * Format a date/time string for display
 */
function formatDateTime(isoString: string | null | undefined): string {
  if (!isoString) return "Unknown";
  try {
    const date = new Date(isoString);
    if (isNaN(date.getTime())) return "Invalid date";
    return date.toLocaleString(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return "Invalid date";
  }
}

/**
 * Format relative time (e.g., "2 hours ago")
 */
function formatRelativeTime(isoString: string | null | undefined): string {
  if (!isoString) return "";
  try {
    const date = new Date(isoString);
    if (isNaN(date.getTime())) return "";

    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMins / 60);
    const diffDays = Math.floor(diffHours / 24);

    if (diffMins < 1) return "just now";
    if (diffMins < 60) return `${diffMins} minute${diffMins !== 1 ? "s" : ""} ago`;
    if (diffHours < 24) return `${diffHours} hour${diffHours !== 1 ? "s" : ""} ago`;
    if (diffDays < 7) return `${diffDays} day${diffDays !== 1 ? "s" : ""} ago`;

    return formatDateTime(isoString);
  } catch {
    return "";
  }
}

/**
 * Get badge color based on status code
 */
function getStatusBadgeColor(
  statusCode: number
): "informative" | "success" | "warning" | "danger" | "important" {
  // Map Dataverse status codes to badge colors
  // Draft(1), Planned(2), Open(3), On Hold(4), Completed(5), Cancelled(6)
  switch (statusCode) {
    case 1: // Draft
      return "informative";
    case 2: // Planned
      return "important";
    case 3: // Open
      return "warning";
    case 4: // On Hold
      return "informative";
    case 5: // Completed
      return "success";
    case 6: // Cancelled
      return "danger";
    default:
      return "informative";
  }
}

// -----------------------------------------------------------------------------
// Sub-Components
// -----------------------------------------------------------------------------

/**
 * User entry display (for created by / modified by)
 */
interface UserEntryProps {
  user: UserInfo | null;
  timestamp: string | null;
  label: string;
}

const UserEntry: React.FC<UserEntryProps> = ({ user, timestamp, label }) => {
  const styles = useStyles();

  if (!user) {
    return (
      <div className={styles.userEntry}>
        <Persona
          size="small"
          name="Unknown"
          secondaryText={label}
          avatar={{ color: "neutral" }}
        />
      </div>
    );
  }

  return (
    <div className={styles.userEntry}>
      <Persona
        size="small"
        name={user.name}
        secondaryText={formatRelativeTime(timestamp) || formatDateTime(timestamp)}
        avatar={{
          image: user.avatarUrl ? { src: user.avatarUrl } : undefined,
          color: "brand",
        }}
      />
    </div>
  );
};

/**
 * Status change timeline entry
 */
interface TimelineEntryProps {
  entry: StatusChangeEntry;
  isFirst: boolean;
  isLast: boolean;
}

const TimelineEntry: React.FC<TimelineEntryProps> = ({
  entry,
  isFirst,
  isLast,
}) => {
  const styles = useStyles();

  const entryClassName = [
    styles.timelineEntry,
    isFirst && styles.timelineEntryFirst,
    isLast && styles.timelineEntryLast,
  ]
    .filter(Boolean)
    .join(" ");

  const dotClassName = [styles.timelineDot, isFirst && styles.timelineDotFirst]
    .filter(Boolean)
    .join(" ");

  return (
    <div className={entryClassName}>
      <span className={dotClassName}>
        <CircleSmallFilled />
      </span>

      <div className={styles.statusChange}>
        {entry.fromStatusLabel && (
          <>
            <Badge
              appearance="outline"
              color={getStatusBadgeColor(entry.fromStatus ?? 0)}
              className={styles.statusBadge}
            >
              {entry.fromStatusLabel}
            </Badge>
            <ArrowCircleRightRegular className={styles.arrowIcon} />
          </>
        )}
        <Badge
          appearance="filled"
          color={getStatusBadgeColor(entry.toStatus)}
          className={styles.statusBadge}
        >
          {entry.toStatusLabel}
        </Badge>
      </div>

      <div className={styles.changeInfo}>
        <span className={styles.changeUser}>{entry.changedBy.name}</span>
        <span>-</span>
        <span>{formatRelativeTime(entry.changedOn) || formatDateTime(entry.changedOn)}</span>
      </div>
    </div>
  );
};

// -----------------------------------------------------------------------------
// Main Component
// -----------------------------------------------------------------------------

/**
 * HistorySection component for displaying audit trail and activity history.
 *
 * Rendered as a collapsible section that lazy loads data when expanded.
 * Read-only display showing:
 * - Created by and created on date
 * - Modified by and modified on date
 * - Status change timeline (if available)
 *
 * Uses Fluent UI v9 components with proper dark mode support via tokens.
 *
 * @example Basic usage
 * ```tsx
 * <HistorySection
 *   historyData={historyData}
 *   onLoadHistory={loadHistoryData}
 *   isLoading={isLoadingHistory}
 *   defaultExpanded={false}
 * />
 * ```
 *
 * @example With controlled expanded state
 * ```tsx
 * const [expanded, setExpanded] = useState(false);
 *
 * <HistorySection
 *   historyData={historyData}
 *   onLoadHistory={loadHistoryData}
 *   isLoading={isLoadingHistory}
 *   expanded={expanded}
 *   onExpandedChange={setExpanded}
 * />
 * ```
 */
export const HistorySection: React.FC<HistorySectionProps> = ({
  historyData,
  onLoadHistory,
  isLoading = false,
  error = null,
  defaultExpanded = false,
  expanded: controlledExpanded,
  onExpandedChange,
}) => {
  const styles = useStyles();

  // Track if data has been loaded (for lazy loading)
  const [hasLoaded, setHasLoaded] = React.useState(false);

  // Track internal expanded state for uncontrolled mode
  const [internalExpanded, setInternalExpanded] = React.useState(defaultExpanded);

  // Determine effective expanded state
  const isControlled = controlledExpanded !== undefined;
  const expanded = isControlled ? controlledExpanded : internalExpanded;

  /**
   * Handle expand/collapse state change with lazy loading
   */
  const handleExpandedChange = React.useCallback(
    (newExpanded: boolean) => {
      // Update internal state for uncontrolled mode
      if (!isControlled) {
        setInternalExpanded(newExpanded);
      }

      // Notify parent
      onExpandedChange?.(newExpanded);

      // Lazy load: trigger data fetch on first expand
      if (newExpanded && !hasLoaded && !isLoading && onLoadHistory) {
        setHasLoaded(true);
        onLoadHistory();
      }
    },
    [isControlled, hasLoaded, isLoading, onLoadHistory, onExpandedChange]
  );

  // ---------------------------------------------------------------------------
  // Render Content
  // ---------------------------------------------------------------------------

  const renderContent = () => {
    // Loading state
    if (isLoading) {
      return (
        <div className={styles.loadingContainer}>
          <Spinner size="small" label="Loading history..." />
        </div>
      );
    }

    // Error state
    if (error) {
      return <div className={styles.errorContainer}>{error}</div>;
    }

    // No data yet (not expanded or not loaded)
    if (!historyData) {
      return (
        <div className={styles.emptyState}>
          Expand section to load history
        </div>
      );
    }

    return (
      <div className={styles.container}>
        {/* Created By Section */}
        <div className={styles.section}>
          <div className={styles.sectionTitle}>
            <PersonRegular className={styles.sectionIcon} />
            <span>Created</span>
          </div>
          <UserEntry
            user={historyData.createdBy}
            timestamp={historyData.createdOn}
            label="Created by"
          />
        </div>

        {/* Modified By Section */}
        <div className={styles.section}>
          <div className={styles.sectionTitle}>
            <CalendarClockRegular className={styles.sectionIcon} />
            <span>Last Modified</span>
          </div>
          <UserEntry
            user={historyData.modifiedBy}
            timestamp={historyData.modifiedOn}
            label="Modified by"
          />
        </div>

        {/* Status Change Timeline (if available) */}
        {historyData.statusChanges && historyData.statusChanges.length > 0 && (
          <>
            <Divider className={styles.divider} />
            <div className={styles.section}>
              <div className={styles.sectionTitle}>
                <HistoryRegular className={styles.sectionIcon} />
                <span>Status History</span>
              </div>
              <div className={styles.timeline}>
                {historyData.statusChanges.map((entry, index) => (
                  <TimelineEntry
                    key={entry.id}
                    entry={entry}
                    isFirst={index === 0}
                    isLast={index === historyData.statusChanges!.length - 1}
                  />
                ))}
              </div>
            </div>
          </>
        )}
      </div>
    );
  };

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <CollapsibleSection
      title="History"
      icon={<HistoryRegular />}
      defaultExpanded={defaultExpanded}
      expanded={controlledExpanded}
      onExpandedChange={handleExpandedChange}
      ariaLabel="History section"
    >
      {renderContent()}
    </CollapsibleSection>
  );
};

export default HistorySection;
