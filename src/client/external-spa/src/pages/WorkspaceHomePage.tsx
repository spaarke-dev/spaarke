/**
 * WorkspaceHomePage — external user's landing page for the Secure Project Workspace.
 *
 * Shows:
 *   - My Projects grid (from external context endpoint, GET /api/v1/external/me)
 *   - Recent Activity feed (last-modified events across accessible projects)
 *   - Upcoming Events/Tasks section (next 5 items with due dates)
 *   - Notifications panel (system notifications: invitation/access changes)
 *
 * All data comes from pre-computed Dataverse records — no real-time AI calls.
 *
 * Design decisions:
 *   - Two-column layout on wide viewports; single column on narrow
 *   - Projects section spans full width (primary content)
 *   - Activity + Upcoming stacked in the right column beside Notifications
 *   - Fluent UI v9 design tokens used exclusively (ADR-021, no hard-coded colors)
 *   - Fluent UI v9 DataGrid used for project list (ADR-012 pattern;
 *     @spaarke/ui-components DatasetGrid components are PCF-specific)
 *
 * See: docs/architecture/power-pages-spa-guide.md
 */

import * as React from "react";
import { useState, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  MessageBar,
  MessageBarBody,
  MessageBarActions,
  Button,
  Badge,
  Divider,
  DataGrid,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridBody,
  DataGridRow,
  DataGridCell,
  TableColumnDefinition,
  createTableColumn,
  TableCellLayout,
} from "@fluentui/react-components";
import {
  FolderRegular,
  CalendarRegular,
  AlertRegular,
  ArrowClockwiseRegular,
  CheckmarkCircleRegular,
  InfoRegular,
} from "@fluentui/react-icons";

import { useExternalContext } from "../hooks/useExternalContext";
import { getProjects, getEvents } from "../api/web-api-client";
import type { ODataEvent, ODataProject } from "../api/web-api-client";
import { PageContainer, SectionCard } from "../components";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  layout: {
    display: "grid",
    gridTemplateColumns: "1fr",
    gap: tokens.spacingVerticalL,
    "@media (min-width: 900px)": {
      gridTemplateColumns: "1fr 340px",
    },
  },
  projectsSection: {
    // Spans full width on narrow; left column on wide
    "@media (min-width: 900px)": {
      gridColumn: "1 / 2",
    },
  },
  rightColumn: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalL,
    "@media (min-width: 900px)": {
      gridColumn: "2 / 3",
      gridRow: "1 / 3",
    },
  },
  pageHeader: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalM,
  },
  welcomeText: {
    color: tokens.colorNeutralForeground3,
  },
  accessBadgeViewOnly: {
    backgroundColor: tokens.colorNeutralBackground4,
    color: tokens.colorNeutralForeground2,
  },
  accessBadgeCollaborate: {
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground2,
  },
  accessBadgeFullAccess: {
    backgroundColor: tokens.colorStatusSuccessBackground1,
    color: tokens.colorStatusSuccessForeground1,
  },
  projectNameCell: {
    cursor: "pointer",
    color: tokens.colorBrandForeground1,
    ":hover": {
      textDecorationLine: "underline",
    },
  },
  activityList: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalS,
  },
  activityItem: {
    display: "flex",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalS,
    paddingBottom: tokens.spacingVerticalS,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    ":last-child": {
      borderBottomStyle: "none",
    },
  },
  activityIcon: {
    color: tokens.colorNeutralForeground3,
    marginTop: "2px",
    flexShrink: 0,
  },
  activityContent: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  activityMeta: {
    color: tokens.colorNeutralForeground3,
  },
  upcomingList: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalS,
  },
  upcomingItem: {
    display: "flex",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalS,
    paddingBottom: tokens.spacingVerticalS,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    ":last-child": {
      borderBottomStyle: "none",
    },
  },
  upcomingIcon: {
    color: tokens.colorBrandForeground1,
    marginTop: "2px",
    flexShrink: 0,
  },
  upcomingOverdueIcon: {
    color: tokens.colorStatusDangerForeground1,
    marginTop: "2px",
    flexShrink: 0,
  },
  upcomingContent: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  dueDateNormal: {
    color: tokens.colorNeutralForeground3,
  },
  dueDateOverdue: {
    color: tokens.colorStatusDangerForeground1,
  },
  notificationList: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalS,
  },
  notificationItem: {
    display: "flex",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalS,
    paddingBottom: tokens.spacingVerticalS,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    ":last-child": {
      borderBottomStyle: "none",
    },
  },
  notificationIcon: {
    color: tokens.colorBrandForeground1,
    marginTop: "2px",
    flexShrink: 0,
  },
  notificationContent: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  notificationMeta: {
    color: tokens.colorNeutralForeground3,
  },
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalXL,
    gap: tokens.spacingVerticalS,
    textAlign: "center",
  },
  emptyStateIcon: {
    color: tokens.colorNeutralForeground4,
    fontSize: "32px",
  },
  emptyStateText: {
    color: tokens.colorNeutralForeground3,
  },
  spinnerContainer: {
    display: "flex",
    justifyContent: "center",
    padding: tokens.spacingVerticalL,
  },
  sectionTitleRow: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
});

// ---------------------------------------------------------------------------
// Types for My Projects grid rows
// ---------------------------------------------------------------------------

interface ProjectRow {
  projectId: string;
  name: string;
  reference: string;
  accessLevel: string;
  lastActivity: string;
}

// ---------------------------------------------------------------------------
// Types for Notifications (simulated from access context changes)
// ---------------------------------------------------------------------------

interface NotificationItem {
  id: string;
  message: string;
  detail: string;
  date: string;
}

// ---------------------------------------------------------------------------
// Helper utilities
// ---------------------------------------------------------------------------

/**
 * Format an access level string label for display.
 * Values come from ExternalUserContextResponse.projects[].accessLevel.
 */
function formatAccessLevel(raw: string): string {
  switch (raw) {
    case "ViewOnly":
      return "View Only";
    case "Collaborate":
      return "Collaborate";
    case "FullAccess":
      return "Full Access";
    default:
      return raw;
  }
}

/**
 * Format a Dataverse ISO date string into a readable relative or absolute date.
 * Falls back to "—" when the value is null/undefined.
 */
function formatRelativeDate(isoDate: string | null | undefined): string {
  if (!isoDate) return "—";
  const date = new Date(isoDate);
  if (isNaN(date.getTime())) return "—";

  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24));

  if (diffDays === 0) return "Today";
  if (diffDays === 1) return "Yesterday";
  if (diffDays < 7) return `${diffDays} days ago`;

  return date.toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

/**
 * Format a due date for upcoming items.
 * Returns { label, isOverdue } to allow conditional styling.
 */
function formatDueDate(isoDate: string | null | undefined): { label: string; isOverdue: boolean } {
  if (!isoDate) return { label: "No due date", isOverdue: false };
  const date = new Date(isoDate);
  if (isNaN(date.getTime())) return { label: "Invalid date", isOverdue: false };

  const now = new Date();
  const isOverdue = date < now;

  const formatted = date.toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });

  return { label: isOverdue ? `Overdue — ${formatted}` : formatted, isOverdue };
}

// ---------------------------------------------------------------------------
// My Projects columns definition
// ---------------------------------------------------------------------------

function buildProjectColumns(
  onProjectClick: (projectId: string) => void,
  styles: ReturnType<typeof useStyles>
): TableColumnDefinition<ProjectRow>[] {
  return [
    createTableColumn<ProjectRow>({
      columnId: "name",
      compare: (a, b) => a.name.localeCompare(b.name),
      renderHeaderCell: () => "Project Name",
      renderCell: (item) => (
        <TableCellLayout>
          <Text
            className={styles.projectNameCell}
            onClick={() => onProjectClick(item.projectId)}
          >
            {item.name}
          </Text>
        </TableCellLayout>
      ),
    }),
    createTableColumn<ProjectRow>({
      columnId: "reference",
      compare: (a, b) => a.reference.localeCompare(b.reference),
      renderHeaderCell: () => "Reference",
      renderCell: (item) => (
        <TableCellLayout>
          <Text size={200} style={{ fontFamily: tokens.fontFamilyMonospace }}>
            {item.reference}
          </Text>
        </TableCellLayout>
      ),
    }),
    createTableColumn<ProjectRow>({
      columnId: "accessLevel",
      compare: (a, b) => a.accessLevel.localeCompare(b.accessLevel),
      renderHeaderCell: () => "Access Level",
      renderCell: (item) => {
        let badgeClass = styles.accessBadgeViewOnly;
        if (item.accessLevel === "Collaborate") badgeClass = styles.accessBadgeCollaborate;
        if (item.accessLevel === "Full Access") badgeClass = styles.accessBadgeFullAccess;
        return (
          <TableCellLayout>
            <Badge
              className={badgeClass}
              appearance="filled"
              size="medium"
              shape="rounded"
            >
              {item.accessLevel}
            </Badge>
          </TableCellLayout>
        );
      },
    }),
    createTableColumn<ProjectRow>({
      columnId: "lastActivity",
      compare: (a, b) => a.lastActivity.localeCompare(b.lastActivity),
      renderHeaderCell: () => "Last Activity",
      renderCell: (item) => (
        <TableCellLayout>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            {item.lastActivity}
          </Text>
        </TableCellLayout>
      ),
    }),
  ];
}

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

/** Spinner centred inside a section card */
const SectionSpinner: React.FC<{ label: string }> = ({ label }) => {
  const styles = useStyles();
  return (
    <div className={styles.spinnerContainer}>
      <Spinner size="small" label={label} />
    </div>
  );
};

interface EmptyStateProps {
  message: string;
  icon?: React.ReactElement;
}

/** Empty state shown when a section has no data */
const EmptyState: React.FC<EmptyStateProps> = ({ message, icon }) => {
  const styles = useStyles();
  return (
    <div className={styles.emptyState}>
      {icon && <span className={styles.emptyStateIcon}>{icon}</span>}
      <Text size={300} className={styles.emptyStateText}>
        {message}
      </Text>
    </div>
  );
};

// ---------------------------------------------------------------------------
// My Projects section
// ---------------------------------------------------------------------------

interface MyProjectsSectionProps {
  rows: ProjectRow[];
  isLoading: boolean;
  onProjectClick: (projectId: string) => void;
}

const MyProjectsSection: React.FC<MyProjectsSectionProps> = ({
  rows,
  isLoading,
  onProjectClick,
}) => {
  const styles = useStyles();
  const columns = buildProjectColumns(onProjectClick, styles);

  return (
    <SectionCard title={`My Projects${rows.length > 0 ? ` (${rows.length})` : ""}`}>
      {isLoading ? (
        <SectionSpinner label="Loading projects..." />
      ) : rows.length === 0 ? (
        <EmptyState
          message="You do not have access to any projects yet. Contact your administrator to request access."
          icon={<FolderRegular />}
        />
      ) : (
        <DataGrid
          items={rows}
          columns={columns}
          sortable
          getRowId={(item) => item.projectId}
          style={{ width: "100%" }}
          size="small"
        >
          <DataGridHeader>
            <DataGridRow>
              {({ renderHeaderCell }) => (
                <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
              )}
            </DataGridRow>
          </DataGridHeader>
          <DataGridBody<ProjectRow>>
            {({ item, rowId }) => (
              <DataGridRow<ProjectRow> key={rowId}>
                {({ renderCell }) => (
                  <DataGridCell>{renderCell(item)}</DataGridCell>
                )}
              </DataGridRow>
            )}
          </DataGridBody>
        </DataGrid>
      )}
    </SectionCard>
  );
};

// ---------------------------------------------------------------------------
// Recent Activity section
// ---------------------------------------------------------------------------

interface RecentActivityItem {
  id: string;
  name: string;
  projectName: string;
  relativeDate: string;
}

interface RecentActivitySectionProps {
  items: RecentActivityItem[];
  isLoading: boolean;
}

const RecentActivitySection: React.FC<RecentActivitySectionProps> = ({
  items,
  isLoading,
}) => {
  const styles = useStyles();

  return (
    <SectionCard title="Recent Activity">
      {isLoading ? (
        <SectionSpinner label="Loading activity..." />
      ) : items.length === 0 ? (
        <EmptyState
          message="No recent activity found across your projects."
          icon={<CalendarRegular />}
        />
      ) : (
        <div className={styles.activityList}>
          {items.map((item) => (
            <div key={item.id} className={styles.activityItem}>
              <CalendarRegular className={styles.activityIcon} fontSize={16} />
              <div className={styles.activityContent}>
                <Text size={300} weight="medium" truncate wrap={false} style={{ display: "block" }}>
                  {item.name}
                </Text>
                <Text size={200} className={styles.activityMeta}>
                  {item.projectName} · {item.relativeDate}
                </Text>
              </div>
            </div>
          ))}
        </div>
      )}
    </SectionCard>
  );
};

// ---------------------------------------------------------------------------
// Upcoming Events/Tasks section
// ---------------------------------------------------------------------------

interface UpcomingItem {
  id: string;
  name: string;
  projectName: string;
  dueDate: string | null | undefined;
  isTodo: boolean;
}

interface UpcomingSectionProps {
  items: UpcomingItem[];
  isLoading: boolean;
}

const UpcomingSection: React.FC<UpcomingSectionProps> = ({ items, isLoading }) => {
  const styles = useStyles();

  return (
    <SectionCard title="Upcoming Events & Tasks">
      {isLoading ? (
        <SectionSpinner label="Loading upcoming items..." />
      ) : items.length === 0 ? (
        <EmptyState
          message="No upcoming events or tasks in the next 30 days."
          icon={<CheckmarkCircleRegular />}
        />
      ) : (
        <div className={styles.upcomingList}>
          {items.map((item) => {
            const { label, isOverdue } = formatDueDate(item.dueDate);
            return (
              <div key={item.id} className={styles.upcomingItem}>
                <CalendarRegular
                  className={isOverdue ? styles.upcomingOverdueIcon : styles.upcomingIcon}
                  fontSize={16}
                />
                <div className={styles.upcomingContent}>
                  <Text size={300} weight="medium" truncate wrap={false} style={{ display: "block" }}>
                    {item.name}
                  </Text>
                  <Text size={200} className={isOverdue ? styles.dueDateOverdue : styles.dueDateNormal}>
                    {label}
                  </Text>
                  <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                    {item.projectName}
                  </Text>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </SectionCard>
  );
};

// ---------------------------------------------------------------------------
// Notifications panel
// ---------------------------------------------------------------------------

interface NotificationsPanelProps {
  items: NotificationItem[];
}

const NotificationsPanel: React.FC<NotificationsPanelProps> = ({ items }) => {
  const styles = useStyles();

  return (
    <SectionCard title="Notifications">
      {items.length === 0 ? (
        <EmptyState
          message="No notifications at this time."
          icon={<AlertRegular />}
        />
      ) : (
        <div className={styles.notificationList}>
          {items.map((item) => (
            <div key={item.id} className={styles.notificationItem}>
              <InfoRegular className={styles.notificationIcon} fontSize={16} />
              <div className={styles.notificationContent}>
                <Text size={300} weight="medium" style={{ display: "block" }}>
                  {item.message}
                </Text>
                <Text size={200} style={{ display: "block" }}>
                  {item.detail}
                </Text>
                <Text size={200} className={styles.notificationMeta}>
                  {item.date}
                </Text>
              </div>
            </div>
          ))}
          <Divider />
        </div>
      )}
    </SectionCard>
  );
};

// ---------------------------------------------------------------------------
// Main WorkspaceHomePage component
// ---------------------------------------------------------------------------

/**
 * WorkspaceHomePage — external user landing page for the Secure Project Workspace SPA.
 *
 * Layout (wide viewport):
 * ┌─────────────────────────────────┬───────────────┐
 * │ My Projects (DataGrid, full     │ Notifications  │
 * │ width left col)                 │               │
 * ├─────────────────────────────────│               │
 * │ Recent Activity   Upcoming      │               │
 * └─────────────────────────────────┴───────────────┘
 */
export const WorkspaceHomePage: React.FC = () => {
  const styles = useStyles();
  const navigate = useNavigate();

  // ── Context (projects + user identity) ──────────────────────────────────
  const { context, isLoading: contextLoading, error: contextError, refresh } = useExternalContext();

  // ── Recent Activity (events across all accessible projects, last modified) ──
  const [recentActivity, setRecentActivity] = useState<RecentActivityItem[]>([]);
  const [activityLoading, setActivityLoading] = useState<boolean>(false);

  // ── Upcoming Events/Tasks ────────────────────────────────────────────────
  const [upcomingItems, setUpcomingItems] = useState<UpcomingItem[]>([]);
  const [upcomingLoading, setUpcomingLoading] = useState<boolean>(false);

  // ── Notifications ────────────────────────────────────────────────────────
  // Notifications are derived from access context changes (invitation/grant).
  // Currently shown as a simulated list since the Dataverse model does not have
  // a dedicated notification table. Post-MVP: query sprk_communication records.
  const [notifications] = useState<NotificationItem[]>([]);

  // ── Project details (name, reference, last modified) ────────────────────
  const [projectDetails, setProjectDetails] = useState<ODataProject[]>([]);
  const [projectsLoading, setProjectsLoading] = useState<boolean>(false);

  useEffect(() => {
    if (!context || context.projects.length === 0) {
      setProjectDetails([]);
      return;
    }

    let cancelled = false;

    const fetchProjectDetails = async () => {
      setProjectsLoading(true);
      try {
        // getProjects() is scoped by Power Pages table permissions to only the
        // projects the authenticated contact can access — no additional filter needed.
        const projects = await getProjects({
          $select: "sprk_projectid,sprk_name,sprk_referencenumber,modifiedon",
          $orderby: "sprk_name asc",
        });
        if (!cancelled) {
          setProjectDetails(projects);
        }
      } catch {
        // Non-fatal — project rows will fall back to IDs if details unavailable
        if (!cancelled) setProjectDetails([]);
      } finally {
        if (!cancelled) setProjectsLoading(false);
      }
    };

    void fetchProjectDetails();
    return () => { cancelled = true; };
  }, [context]);

  // ── Derived: Project rows ────────────────────────────────────────────────
  const projectRows: ProjectRow[] = React.useMemo(() => {
    if (!context) return [];

    // Build a lookup map from project details fetched from the Web API
    const detailMap = new Map<string, ODataProject>(
      projectDetails.map((p) => [p.sprk_projectid, p])
    );

    return context.projects.map((p) => {
      const detail = detailMap.get(p.projectId);
      return {
        projectId: p.projectId,
        name: detail?.sprk_name ?? p.projectId,
        reference: detail?.sprk_referencenumber ?? "—",
        accessLevel: formatAccessLevel(p.accessLevel),
        lastActivity: formatRelativeDate(detail?.modifiedon),
      };
    });
  }, [context, projectDetails]);

  // ── Fetch events for activity + upcoming ────────────────────────────────
  useEffect(() => {
    if (!context || context.projects.length === 0) return;

    let cancelled = false;

    const fetchEvents = async () => {
      setActivityLoading(true);
      setUpcomingLoading(true);

      try {
        // Fetch recent events from the first (or all) accessible projects.
        // We use the last-modified date to build the Recent Activity feed, and
        // filter by due date > now for the Upcoming section.
        //
        // Note: The Power Pages table permission chain limits results to only
        // records the current contact is authorised to see. No additional
        // client-side filtering is required.

        const allEventPromises = context.projects.map((p) =>
          getEvents(p.projectId, {
            $select:
              "sprk_eventid,sprk_name,sprk_duedate,sprk_status,sprk_todoflag,_sprk_projectid_value,createdon",
            $orderby: "createdon desc",
            $top: 20,
          }).then((events) =>
            events.map((e) => ({ ...e, _resolvedProjectId: p.projectId }))
          )
        );

        const allEventsNested = await Promise.all(allEventPromises);
        if (cancelled) return;

        const allEvents = allEventsNested.flat();

        // Recent Activity: last 10 items by createdon desc
        const sortedByModified = [...allEvents].sort((a, b) => {
          const aDate = a.createdon ? new Date(a.createdon).getTime() : 0;
          const bDate = b.createdon ? new Date(b.createdon).getTime() : 0;
          return bDate - aDate;
        });

        const activityItems: RecentActivityItem[] = sortedByModified
          .slice(0, 10)
          .map((e) => ({
            id: e.sprk_eventid,
            name: e.sprk_name,
            projectName: e._sprk_projectid_value ?? "Unknown Project",
            relativeDate: formatRelativeDate(e.createdon),
          }));

        setRecentActivity(activityItems);

        // Upcoming: events with a future (or today) due date, next 5
        const now = new Date();
        const upcoming: UpcomingItem[] = allEvents
          .filter((e) => {
            if (!e.sprk_duedate) return false;
            const due = new Date(e.sprk_duedate);
            return !isNaN(due.getTime());
          })
          .sort((a, b) => {
            const aDate = a.sprk_duedate ? new Date(a.sprk_duedate).getTime() : 0;
            const bDate = b.sprk_duedate ? new Date(b.sprk_duedate).getTime() : 0;
            return aDate - bDate;
          })
          .filter((e) => {
            // Show overdue items and future items (next 30 days)
            const due = new Date(e.sprk_duedate!);
            const diffDays =
              (due.getTime() - now.getTime()) / (1000 * 60 * 60 * 24);
            return diffDays < 30; // within 30 days or overdue
          })
          .slice(0, 5)
          .map((e) => ({
            id: e.sprk_eventid,
            name: e.sprk_name,
            projectName: e._sprk_projectid_value ?? "Unknown Project",
            dueDate: e.sprk_duedate,
            isTodo: e.sprk_todoflag === true,
          }));

        setUpcomingItems(upcoming);
      } catch {
        // Activity and upcoming items are supplementary — don't block the page
        // on failure. Sections will render empty states gracefully.
        setRecentActivity([]);
        setUpcomingItems([]);
      } finally {
        if (!cancelled) {
          setActivityLoading(false);
          setUpcomingLoading(false);
        }
      }
    };

    void fetchEvents();

    return () => {
      cancelled = true;
    };
  }, [context]);

  // ── Navigate to project page ─────────────────────────────────────────────
  const handleProjectClick = (projectId: string) => {
    navigate(`/project/${projectId}`);
  };

  // ── Render ───────────────────────────────────────────────────────────────

  // Full page error (context failed to load)
  if (contextError) {
    return (
      <PageContainer title="My Workspace">
        <MessageBar intent="error">
          <MessageBarBody>{contextError}</MessageBarBody>
          <MessageBarActions>
            <Button
              appearance="transparent"
              icon={<ArrowClockwiseRegular />}
              onClick={refresh}
            >
              Retry
            </Button>
          </MessageBarActions>
        </MessageBar>
      </PageContainer>
    );
  }

  const displayName = context?.email ?? "";

  return (
    <PageContainer>
      {/* Page header */}
      <div className={styles.pageHeader}>
        <Text size={700} weight="semibold" as="h1">
          My Workspace
        </Text>
        {displayName && (
          <Text size={400} className={styles.welcomeText}>
            Welcome back, {displayName}
          </Text>
        )}
      </div>

      {/* Two-column layout */}
      <div className={styles.layout}>
        {/* Left: My Projects (full-width project grid) */}
        <div className={styles.projectsSection}>
          <MyProjectsSection
            rows={projectRows}
            isLoading={contextLoading || projectsLoading}
            onProjectClick={handleProjectClick}
          />
        </div>

        {/* Right: Notifications panel (stacked, spans both rows on wide) */}
        <div className={styles.rightColumn}>
          <NotificationsPanel items={notifications} />
        </div>

        {/* Bottom-left row: Recent Activity + Upcoming */}
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "1fr 1fr",
            gap: tokens.spacingHorizontalL,
          }}
        >
          <RecentActivitySection
            items={recentActivity}
            isLoading={activityLoading}
          />
          <UpcomingSection
            items={upcomingItems}
            isLoading={upcomingLoading}
          />
        </div>
      </div>
    </PageContainer>
  );
};

export default WorkspaceHomePage;
