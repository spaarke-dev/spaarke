/**
 * WorkspaceHomePage — external user's landing page for the Secure External Workspace.
 *
 * Layout (matches mock-up):
 * ┌──────────────────────────────────────────────────────────────┐
 * │ My Workspace                          [Todo ✓] [Bell 🔔]    │
 * │ Welcome back, {email}                                         │
 * ├──────────────────┬───────────────────────────────────────────┤
 * │ Recent Activity  │  Upcoming Events & Tasks                  │
 * ├──────────────────┴───────────────────────────────────────────┤
 * │ My Projects (N)                                               │
 * ├───────────────────────────────────────────────────────────────┤
 * │ My Matters                                                    │
 * ├───────────────────────────────────────────────────────────────┤
 * │ My Documents (N)                                              │
 * └───────────────────────────────────────────────────────────────┘
 *
 * Notifications are accessed via the bell icon popover (not a sidebar panel).
 *
 * Fluent UI v9 design tokens used exclusively (ADR-021, no hard-coded colors).
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
  DataGrid,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridBody,
  DataGridRow,
  DataGridCell,
  TableColumnDefinition,
  createTableColumn,
  TableCellLayout,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  Tooltip,
} from "@fluentui/react-components";
import {
  FolderRegular,
  CalendarRegular,
  AlertRegular,
  ArrowClockwiseRegular,
  CheckmarkCircleRegular,
  InfoRegular,
  AlertBadgeRegular,
} from "@fluentui/react-icons";

import { useExternalContext } from "../hooks/useExternalContext";
import { getProjects, getEvents, getDocuments } from "../api/web-api-client";
import type { ODataEvent, ODataProject, ODataDocument } from "../api/web-api-client";
import { PageContainer } from "../components/PageContainer";
import { SectionCard } from "../components/SectionCard";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  pageHeaderRow: {
    display: "flex",
    alignItems: "flex-start",
    justifyContent: "space-between",
    marginBottom: tokens.spacingVerticalM,
  },
  pageHeaderLeft: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
  },
  pageHeaderIcons: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXS,
  },
  welcomeText: {
    color: tokens.colorNeutralForeground3,
  },
  twoColGrid: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: tokens.spacingHorizontalL,
    "@media (max-width: 768px)": {
      gridTemplateColumns: "1fr",
    },
  },
  // Access level badge colors
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
    ":hover": { textDecorationLine: "underline" },
  },
  // Activity / Upcoming list items
  itemList: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalS,
  },
  itemRow: {
    display: "flex",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalS,
    paddingBottom: tokens.spacingVerticalS,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    ":last-child": { borderBottomStyle: "none" },
    cursor: "pointer",
    borderRadius: tokens.borderRadiusMedium,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  itemIcon: {
    color: tokens.colorNeutralForeground3,
    marginTop: "2px",
    flexShrink: 0,
  },
  itemIconBrand: {
    color: tokens.colorBrandForeground1,
    marginTop: "2px",
    flexShrink: 0,
  },
  itemIconDanger: {
    color: tokens.colorStatusDangerForeground1,
    marginTop: "2px",
    flexShrink: 0,
  },
  itemContent: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  itemMeta: { color: tokens.colorNeutralForeground3 },
  dueDateOverdue: { color: tokens.colorStatusDangerForeground1 },
  // Document list
  docRow: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    ":last-child": { borderBottomStyle: "none" },
    cursor: "pointer",
    borderRadius: tokens.borderRadiusMedium,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  docIcon: {
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  docInfo: {
    display: "flex",
    flexDirection: "column",
    gap: "2px",
    flex: "1",
    minWidth: 0,
  },
  docMeta: { color: tokens.colorNeutralForeground3 },
  // Notifications popover
  notifPopover: {
    minWidth: "300px",
    maxWidth: "400px",
  },
  notifEmpty: {
    padding: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
  },
  notifItem: {
    display: "flex",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    ":last-child": { borderBottomStyle: "none" },
  },
  // Misc
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalXL,
    gap: tokens.spacingVerticalS,
    textAlign: "center",
  },
  emptyStateText: { color: tokens.colorNeutralForeground3 },
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
// Types
// ---------------------------------------------------------------------------

interface ProjectRow {
  projectId: string;
  name: string;
  reference: string;
  accessLevel: string;
  lastActivity: string;
}

interface ActivityItem {
  id: string;
  name: string;
  projectName: string;
  projectId: string;
  relativeDate: string;
}

interface TaggedDocument extends ODataDocument {
  _resolvedProjectId: string;
}

interface UpcomingItem {
  id: string;
  name: string;
  projectName: string;
  dueDate: string | null | undefined;
}

interface NotificationItem {
  id: string;
  message: string;
  detail: string;
  date: string;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function formatAccessLevel(raw: string): string {
  switch (raw) {
    case "ViewOnly": return "View Only";
    case "Collaborate": return "Collaborate";
    case "FullAccess": return "Full Access";
    default: return raw;
  }
}

function formatRelativeDate(isoDate: string | null | undefined): string {
  if (!isoDate) return "—";
  const date = new Date(isoDate);
  if (isNaN(date.getTime())) return "—";
  const now = new Date();
  const diffDays = Math.floor((now.getTime() - date.getTime()) / (1000 * 60 * 60 * 24));
  if (diffDays === 0) return "Today";
  if (diffDays === 1) return "Yesterday";
  if (diffDays < 7) return `${diffDays} days ago`;
  return date.toLocaleDateString("en-US", { year: "numeric", month: "short", day: "numeric" });
}

function formatDueDate(isoDate: string | null | undefined): { label: string; isOverdue: boolean } {
  if (!isoDate) return { label: "No due date", isOverdue: false };
  const date = new Date(isoDate);
  if (isNaN(date.getTime())) return { label: "Invalid date", isOverdue: false };
  const isOverdue = date < new Date();
  const formatted = date.toLocaleDateString("en-US", { year: "numeric", month: "short", day: "numeric" });
  return { label: isOverdue ? `Overdue — ${formatted}` : formatted, isOverdue };
}

function formatDocDate(isoDate: string | null | undefined): string {
  if (!isoDate) return "";
  const d = new Date(isoDate);
  if (isNaN(d.getTime())) return "";
  return d.toLocaleDateString("en-US", { year: "numeric", month: "short", day: "numeric" });
}

// ---------------------------------------------------------------------------
// Project columns
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
          <Text className={styles.projectNameCell} onClick={() => onProjectClick(item.projectId)}>
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
          <Text size={200} style={{ fontFamily: tokens.fontFamilyMonospace }}>{item.reference}</Text>
        </TableCellLayout>
      ),
    }),
    createTableColumn<ProjectRow>({
      columnId: "accessLevel",
      compare: (a, b) => a.accessLevel.localeCompare(b.accessLevel),
      renderHeaderCell: () => "Access Level",
      renderCell: (item) => {
        let cls = styles.accessBadgeViewOnly;
        if (item.accessLevel === "Collaborate") cls = styles.accessBadgeCollaborate;
        if (item.accessLevel === "Full Access") cls = styles.accessBadgeFullAccess;
        return (
          <TableCellLayout>
            <Badge className={cls} appearance="filled" size="medium" shape="rounded">
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
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>{item.lastActivity}</Text>
        </TableCellLayout>
      ),
    }),
  ];
}

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

const SectionSpinner: React.FC<{ label: string }> = ({ label }) => {
  const styles = useStyles();
  return (
    <div className={styles.spinnerContainer}>
      <Spinner size="small" label={label} />
    </div>
  );
};

const EmptyState: React.FC<{ message: string; icon?: React.ReactElement }> = ({ message, icon }) => {
  const styles = useStyles();
  return (
    <div className={styles.emptyState}>
      {icon && <span style={{ color: tokens.colorNeutralForeground4, fontSize: "32px" }}>{icon}</span>}
      <Text size={300} className={styles.emptyStateText}>{message}</Text>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Notifications popover
// ---------------------------------------------------------------------------

const NotificationsPopover: React.FC<{ items: NotificationItem[] }> = ({ items }) => {
  const styles = useStyles();
  const [open, setOpen] = useState(false);

  return (
    <Popover open={open} onOpenChange={(_e, data) => setOpen(data.open)} positioning="below-end">
      <PopoverTrigger disableButtonEnhancement>
        <Tooltip content="Notifications" relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={<AlertRegular />}
            aria-label={`Notifications${items.length > 0 ? ` (${items.length})` : ""}`}
          />
        </Tooltip>
      </PopoverTrigger>
      <PopoverSurface className={styles.notifPopover}>
        <Text size={400} weight="semibold" as="h2" style={{ display: "block", marginBottom: tokens.spacingVerticalS }}>
          Notifications
        </Text>
        {items.length === 0 ? (
          <div className={styles.notifEmpty}>
            <Text size={300}>No notifications at this time.</Text>
          </div>
        ) : (
          items.map((item) => (
            <div key={item.id} className={styles.notifItem}>
              <InfoRegular fontSize={16} style={{ color: tokens.colorBrandForeground1, marginTop: "2px", flexShrink: 0 }} />
              <div>
                <Text size={300} weight="medium" style={{ display: "block" }}>{item.message}</Text>
                <Text size={200} style={{ display: "block" }}>{item.detail}</Text>
                <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>{item.date}</Text>
              </div>
            </div>
          ))
        )}
      </PopoverSurface>
    </Popover>
  );
};

// ---------------------------------------------------------------------------
// Recent Activity section
// ---------------------------------------------------------------------------

const RecentActivitySection: React.FC<{
  items: ActivityItem[];
  isLoading: boolean;
  onItemClick: (projectId: string) => void;
}> = ({ items, isLoading, onItemClick }) => {
  const styles = useStyles();
  return (
    <SectionCard title="Recent Activity">
      {isLoading ? (
        <SectionSpinner label="Loading activity..." />
      ) : items.length === 0 ? (
        <EmptyState message="No recent activity found across your projects." icon={<CalendarRegular />} />
      ) : (
        <div className={styles.itemList}>
          {items.map((item) => (
            <div
              key={item.id}
              className={styles.itemRow}
              onClick={() => onItemClick(item.projectId)}
              role="button"
              tabIndex={0}
              onKeyDown={(e) => e.key === "Enter" && onItemClick(item.projectId)}
            >
              <CalendarRegular className={styles.itemIcon} fontSize={16} />
              <div className={styles.itemContent}>
                <Text size={300} weight="medium" truncate wrap={false} style={{ display: "block" }}>
                  {item.name}
                </Text>
                <Text size={200} className={styles.itemMeta}>
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
// Upcoming Events & Tasks section
// ---------------------------------------------------------------------------

const UpcomingSection: React.FC<{ items: UpcomingItem[]; isLoading: boolean }> = ({
  items,
  isLoading,
}) => {
  const styles = useStyles();
  return (
    <SectionCard title="Upcoming Events & Tasks">
      {isLoading ? (
        <SectionSpinner label="Loading upcoming items..." />
      ) : items.length === 0 ? (
        <EmptyState message="No upcoming events or tasks in the next 30 days." icon={<CheckmarkCircleRegular />} />
      ) : (
        <div className={styles.itemList}>
          {items.map((item) => {
            const { label, isOverdue } = formatDueDate(item.dueDate);
            return (
              <div key={item.id} className={styles.itemRow}>
                <CalendarRegular
                  className={isOverdue ? styles.itemIconDanger : styles.itemIconBrand}
                  fontSize={16}
                />
                <div className={styles.itemContent}>
                  <Text size={300} weight="medium" truncate wrap={false} style={{ display: "block" }}>
                    {item.name}
                  </Text>
                  <Text size={200} className={isOverdue ? styles.dueDateOverdue : styles.itemMeta}>
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
// My Projects grid
// ---------------------------------------------------------------------------

const MyProjectsSection: React.FC<{
  rows: ProjectRow[];
  isLoading: boolean;
  onProjectClick: (id: string) => void;
}> = ({ rows, isLoading, onProjectClick }) => {
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
                {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
              </DataGridRow>
            )}
          </DataGridBody>
        </DataGrid>
      )}
    </SectionCard>
  );
};

// ---------------------------------------------------------------------------
// My Matters — placeholder (matter-level access is a future capability)
// ---------------------------------------------------------------------------

const MyMattersSection: React.FC = () => (
  <SectionCard title="My Matters">
    <div style={{ padding: tokens.spacingVerticalM, color: tokens.colorNeutralForeground3 }}>
      <Text size={300}>Matter-level workspace access is coming soon.</Text>
    </div>
  </SectionCard>
);

// ---------------------------------------------------------------------------
// My Documents — flat list of documents across all accessible projects
// ---------------------------------------------------------------------------

const MyDocumentsSection: React.FC<{
  docs: TaggedDocument[];
  isLoading: boolean;
  onItemClick: (projectId: string) => void;
}> = ({ docs, isLoading, onItemClick }) => {
  const styles = useStyles();

  return (
    <SectionCard title={`My Documents${docs.length > 0 ? ` (${docs.length})` : ""}`}>
      {isLoading ? (
        <SectionSpinner label="Loading documents..." />
      ) : docs.length === 0 ? (
        <EmptyState
          message="No documents found across your projects."
          icon={<FolderRegular />}
        />
      ) : (
        <div>
          {docs.slice(0, 10).map((doc) => (
            <div
              key={doc.sprk_documentid}
              className={styles.docRow}
              onClick={() => onItemClick(doc._resolvedProjectId)}
              role="button"
              tabIndex={0}
              onKeyDown={(e) => e.key === "Enter" && onItemClick(doc._resolvedProjectId)}
            >
              <FolderRegular className={styles.docIcon} fontSize={20} />
              <div className={styles.docInfo}>
                <Text size={300} weight="medium" truncate wrap={false} style={{ display: "block" }}>
                  {doc.sprk_name}
                </Text>
                <Text size={200} className={styles.docMeta}>
                  {doc.sprk_documenttype && (
                    <Badge appearance="tint" size="small" style={{ marginRight: tokens.spacingHorizontalXS }}>
                      {doc.sprk_documenttype}
                    </Badge>
                  )}
                  {doc.createdon && `Created: ${formatDocDate(doc.createdon)}`}
                </Text>
              </div>
            </div>
          ))}
          {docs.length > 10 && (
            <Text
              size={200}
              style={{ color: tokens.colorNeutralForeground3, paddingTop: tokens.spacingVerticalS, display: "block" }}
            >
              Showing 10 of {docs.length} documents
            </Text>
          )}
        </div>
      )}
    </SectionCard>
  );
};

// ---------------------------------------------------------------------------
// Main WorkspaceHomePage
// ---------------------------------------------------------------------------

export const WorkspaceHomePage: React.FC = () => {
  const styles = useStyles();
  const navigate = useNavigate();

  const { context, isLoading: contextLoading, error: contextError, refresh } = useExternalContext();

  const [recentActivity, setRecentActivity] = useState<ActivityItem[]>([]);
  const [activityLoading, setActivityLoading] = useState(false);
  const [upcomingItems, setUpcomingItems] = useState<UpcomingItem[]>([]);
  const [upcomingLoading, setUpcomingLoading] = useState(false);
  const [projectDetails, setProjectDetails] = useState<ODataProject[]>([]);
  const [projectsLoading, setProjectsLoading] = useState(false);
  const [allDocs, setAllDocs] = useState<TaggedDocument[]>([]);
  const [docsLoading, setDocsLoading] = useState(false);

  // Notifications — placeholder (future: query sprk_communication)
  const [notifications] = useState<NotificationItem[]>([]);

  // Fetch project detail records
  useEffect(() => {
    if (!context || context.projects.length === 0) { setProjectDetails([]); return; }
    let cancelled = false;
    setProjectsLoading(true);
    getProjects({ $select: "sprk_projectid,sprk_name,sprk_referencenumber,modifiedon", $orderby: "sprk_name asc" })
      .then((projects) => { if (!cancelled) setProjectDetails(projects); })
      .catch(() => { if (!cancelled) setProjectDetails([]); })
      .finally(() => { if (!cancelled) setProjectsLoading(false); });
    return () => { cancelled = true; };
  }, [context]);

  // Derived project rows
  const projectRows: ProjectRow[] = React.useMemo(() => {
    if (!context) return [];
    const detailMap = new Map(projectDetails.map((p) => [p.sprk_projectid, p]));
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

  // Fetch events for activity + upcoming
  useEffect(() => {
    if (!context || context.projects.length === 0) return;
    let cancelled = false;
    setActivityLoading(true);
    setUpcomingLoading(true);

    const fetchAll = async () => {
      try {
        const nested = await Promise.all(
          context.projects.map((p) =>
            getEvents(p.projectId, {
              $select: "sprk_eventid,sprk_name,sprk_duedate,sprk_todoflag,_sprk_projectid_value,createdon",
              $orderby: "createdon desc",
              $top: 20,
            }).then((evts) => evts.map((e) => ({ ...e, _resolvedProjectId: p.projectId })))
          )
        );
        if (cancelled) return;
        const all = nested.flat();

        const sorted = [...all].sort((a, b) =>
          (b.createdon ? new Date(b.createdon).getTime() : 0) -
          (a.createdon ? new Date(a.createdon).getTime() : 0)
        );
        setRecentActivity(
          sorted.slice(0, 10).map((e) => ({
            id: e.sprk_eventid,
            name: e.sprk_name,
            projectName: e._sprk_projectid_value ?? "Unknown Project",
            projectId: e._resolvedProjectId,
            relativeDate: formatRelativeDate(e.createdon),
          }))
        );

        const now = new Date();
        setUpcomingItems(
          all
            .filter((e) => e.sprk_duedate && !isNaN(new Date(e.sprk_duedate).getTime()))
            .sort((a, b) =>
              new Date(a.sprk_duedate!).getTime() - new Date(b.sprk_duedate!).getTime()
            )
            .filter((e) => (new Date(e.sprk_duedate!).getTime() - now.getTime()) / (1000 * 60 * 60 * 24) < 30)
            .slice(0, 5)
            .map((e) => ({
              id: e.sprk_eventid,
              name: e.sprk_name,
              projectName: e._sprk_projectid_value ?? "Unknown Project",
              dueDate: e.sprk_duedate,
            }))
        );
      } catch {
        setRecentActivity([]);
        setUpcomingItems([]);
      } finally {
        if (!cancelled) { setActivityLoading(false); setUpcomingLoading(false); }
      }
    };
    void fetchAll();
    return () => { cancelled = true; };
  }, [context]);

  // Fetch documents across all accessible projects
  useEffect(() => {
    if (!context || context.projects.length === 0) { setAllDocs([]); return; }
    let cancelled = false;
    setDocsLoading(true);

    Promise.all(
      context.projects.map((p) =>
        getDocuments(p.projectId, {
          $select: "sprk_documentid,sprk_name,sprk_documenttype,createdon,modifiedon",
          $orderby: "createdon desc",
          $top: 20,
        }).then((docs) => docs.map((d) => ({ ...d, _resolvedProjectId: p.projectId })))
      )
    )
      .then((nested) => {
        if (!cancelled) {
          const flat = nested.flat().sort((a, b) =>
            (b.createdon ? new Date(b.createdon).getTime() : 0) -
            (a.createdon ? new Date(a.createdon).getTime() : 0)
          );
          setAllDocs(flat);
        }
      })
      .catch(() => { if (!cancelled) setAllDocs([]); })
      .finally(() => { if (!cancelled) setDocsLoading(false); });

    return () => { cancelled = true; };
  }, [context]);

  const handleProjectClick = (projectId: string) => navigate(`/project/${projectId}`);

  if (contextError) {
    return (
      <PageContainer title="My Workspace">
        <MessageBar intent="error">
          <MessageBarBody>{contextError}</MessageBarBody>
          <MessageBarActions>
            <Button appearance="transparent" icon={<ArrowClockwiseRegular />} onClick={refresh}>
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
      {/* Page header with notification icons right-justified */}
      <div className={styles.pageHeaderRow}>
        <div className={styles.pageHeaderLeft}>
          <Text size={700} weight="semibold" as="h1">
            My Workspace
          </Text>
          {displayName && (
            <Text size={400} className={styles.welcomeText}>
              Welcome back, {displayName}
            </Text>
          )}
        </div>

        <div className={styles.pageHeaderIcons}>
          <Tooltip content="Tasks" relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<CheckmarkCircleRegular />}
              aria-label="Tasks"
            />
          </Tooltip>
          <NotificationsPopover items={notifications} />
        </div>
      </div>

      {/* Row 1: Recent Activity + Upcoming (2-column) */}
      <div className={styles.twoColGrid}>
        <RecentActivitySection items={recentActivity} isLoading={activityLoading} onItemClick={handleProjectClick} />
        <UpcomingSection items={upcomingItems} isLoading={upcomingLoading} />
      </div>

      {/* Row 2: My Projects */}
      <MyProjectsSection
        rows={projectRows}
        isLoading={contextLoading || projectsLoading}
        onProjectClick={handleProjectClick}
      />

      {/* Row 3: My Matters */}
      <MyMattersSection />

      {/* Row 4: My Documents */}
      <MyDocumentsSection docs={allDocs} isLoading={docsLoading} onItemClick={handleProjectClick} />
    </PageContainer>
  );
};

export default WorkspaceHomePage;
