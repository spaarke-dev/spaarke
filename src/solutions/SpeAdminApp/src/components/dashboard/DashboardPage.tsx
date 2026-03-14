import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
  Tooltip,
  Badge,
  Divider,
} from "@fluentui/react-components";
import {
  ArrowClockwise20Regular,
  Storage20Regular,
  CheckmarkCircle20Regular,
  DataBarVertical20Regular,
  Clock20Regular,
} from "@fluentui/react-icons";
import { speApiClient } from "../../services/speApiClient";
import { useBuContext } from "../../contexts/BuContext";
import type { DashboardMetrics, ContainerMetrics } from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021: makeStyles + Fluent design tokens; dark mode automatic)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingVerticalXL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    height: "100%",
    boxSizing: "border-box",
    overflowY: "auto",
    backgroundColor: tokens.colorNeutralBackground1,
  },

  pageHeader: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalM,
    flexShrink: 0,
  },

  pageTitle: {
    color: tokens.colorNeutralForeground1,
  },

  headerRight: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },

  lastRefreshed: {
    color: tokens.colorNeutralForeground3,
  },

  // ── Metric Cards ────────────────────────────────────────────────────────

  metricsGrid: {
    display: "grid",
    gridTemplateColumns: "repeat(auto-fill, minmax(220px, 1fr))",
    gap: tokens.spacingVerticalM,
    flexShrink: 0,
  },

  metricCard: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightWidth: "1px",
    borderRightStyle: "solid",
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftWidth: "1px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorNeutralStroke2,
    minHeight: "100px",
  },

  metricCardHeader: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground2,
  },

  metricCardIcon: {
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },

  metricCardValue: {
    color: tokens.colorNeutralForeground1,
    lineHeight: "1",
  },

  metricCardLabel: {
    color: tokens.colorNeutralForeground3,
  },

  // ── Activity Grid ───────────────────────────────────────────────────────

  section: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    flex: "1 1 auto",
    minHeight: 0,
  },

  sectionHeader: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexShrink: 0,
  },

  sectionTitle: {
    color: tokens.colorNeutralForeground1,
  },

  activityTable: {
    width: "100%",
    borderCollapse: "collapse",
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightWidth: "1px",
    borderRightStyle: "solid",
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftWidth: "1px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorNeutralStroke2,
    overflow: "hidden",
  },

  tableHeaderRow: {
    backgroundColor: tokens.colorNeutralBackground3,
  },

  tableHeaderCell: {
    padding: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    textAlign: "left",
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
  },

  tableRow: {
    ":nth-child(even)": {
      backgroundColor: tokens.colorNeutralBackground1,
    },
  },

  tableCell: {
    padding: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke1,
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase200,
    whiteSpace: "nowrap",
    overflow: "hidden",
    textOverflow: "ellipsis",
    maxWidth: "200px",
  },

  // ── Empty / Loading states ──────────────────────────────────────────────

  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground3,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightWidth: "1px",
    borderRightStyle: "solid",
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftWidth: "1px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorNeutralStroke2,
  },

  loadingState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalXXL,
  },

  noConfigBanner: {
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightWidth: "1px",
    borderRightStyle: "solid",
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftWidth: "1px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorNeutralStroke2,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Utilities
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Format bytes into a human-readable storage string.
 * e.g. 1536 → "1.5 KB", 2097152 → "2 MB"
 */
function formatBytes(bytes: number): string {
  if (bytes === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  const value = bytes / Math.pow(1024, i);
  return `${value % 1 === 0 ? value : value.toFixed(1)} ${units[i]}`;
}

/**
 * Format an ISO date string to a human-readable local time.
 * Returns "—" if the value is missing or invalid.
 */
function formatDateTime(iso: string | undefined): string {
  if (!iso) return "—";
  try {
    return new Intl.DateTimeFormat(undefined, {
      dateStyle: "short",
      timeStyle: "short",
    }).format(new Date(iso));
  } catch {
    return iso;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Sub-components
// ─────────────────────────────────────────────────────────────────────────────

interface MetricCardProps {
  icon: React.ReactElement;
  label: string;
  value: string | number;
  subtext?: string;
}

/**
 * MetricCard — a single KPI card in the metrics grid.
 *
 * Uses Fluent design tokens exclusively for colors/spacing (ADR-021).
 * Dark mode is automatic via the FluentProvider theme in App.tsx.
 */
const MetricCard: React.FC<MetricCardProps> = ({ icon, label, value, subtext }) => {
  const styles = useStyles();
  return (
    <div className={styles.metricCard} role="region" aria-label={label}>
      <div className={styles.metricCardHeader}>
        <span className={styles.metricCardIcon}>{icon}</span>
        <Text size={200} weight="semibold" className={styles.metricCardLabel}>
          {label}
        </Text>
      </div>
      <Text size={700} weight="bold" className={styles.metricCardValue}>
        {value}
      </Text>
      {subtext && (
        <Text size={100} className={styles.metricCardLabel}>
          {subtext}
        </Text>
      )}
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Activity Grid (recent container list)
// ─────────────────────────────────────────────────────────────────────────────

interface RecentActivityGridProps {
  containers: ContainerMetrics[];
}

/**
 * RecentActivityGrid — displays per-container stats from the dashboard metrics.
 *
 * Uses a plain HTML table with Fluent design tokens for styling (ADR-021).
 * ADR-012: UniversalDatasetGrid from @spaarke/ui-components is designed for
 * full-featured dataset browsing with view switching. For this read-only
 * summary display (5-10 rows, no selection, no filtering), a simple table
 * is the appropriate choice and avoids unnecessary complexity.
 *
 * If the dashboard grows to support sortable columns, filtering, or pagination,
 * migrate to UniversalDatasetGrid at that point.
 */
const RecentActivityGrid: React.FC<RecentActivityGridProps> = ({ containers }) => {
  const styles = useStyles();

  if (containers.length === 0) {
    return (
      <div className={styles.emptyState} role="status">
        <Storage20Regular style={{ fontSize: "32px", opacity: 0.4 }} />
        <Text size={300} style={{ color: "inherit" }}>
          No containers to display
        </Text>
        <Text size={200} style={{ color: "inherit" }}>
          Container data will appear here after the first dashboard refresh.
        </Text>
      </div>
    );
  }

  return (
    <table className={styles.activityTable} aria-label="Container activity">
      <thead>
        <tr className={styles.tableHeaderRow}>
          <th className={styles.tableHeaderCell}>Container</th>
          <th className={styles.tableHeaderCell}>Status</th>
          <th className={styles.tableHeaderCell}>Storage Used</th>
          <th className={styles.tableHeaderCell}>Items</th>
          <th className={styles.tableHeaderCell}>Last Activity</th>
        </tr>
      </thead>
      <tbody>
        {containers.map((c) => (
          <tr key={c.containerId} className={styles.tableRow}>
            <td className={styles.tableCell} title={c.displayName}>
              {c.displayName || c.containerId}
            </td>
            <td className={styles.tableCell}>
              <Badge
                appearance="tint"
                color={
                  c.status === "active"
                    ? "success"
                    : c.status === "inactive"
                    ? "warning"
                    : "danger"
                }
                size="small"
              >
                {c.status}
              </Badge>
            </td>
            <td className={styles.tableCell}>{formatBytes(c.storageUsedInBytes)}</td>
            <td className={styles.tableCell}>{c.itemCount.toLocaleString()}</td>
            <td className={styles.tableCell}>{formatDateTime(c.lastActivityDateTime)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// DashboardPage
// ─────────────────────────────────────────────────────────────────────────────

/**
 * DashboardPage — the default landing page of the SPE Admin App.
 *
 * Provides an at-a-glance overview of the SPE environment:
 * - Key metrics cards: container count, storage used, active containers
 * - Recent activity grid: per-container stats
 * - Refresh button: triggers POST /api/spe/dashboard/refresh
 *
 * Data is served from the server-side background-sync cache
 * (SpeDashboardSyncService). If no config is selected, prompts the user
 * to select one via the BU/config picker (future task).
 *
 * Dark mode: uses Fluent v9 design tokens only (ADR-021). Theme is applied
 * by the parent FluentProvider in App.tsx — no extra wiring needed here.
 *
 * ADR-012: Simple table used for the activity grid (see RecentActivityGrid
 * comment for rationale vs. UniversalDatasetGrid).
 */
export const DashboardPage: React.FC = () => {
  const styles = useStyles();
  const { selectedConfig } = useBuContext();

  // ── Data State ─────────────────────────────────────────────────────────────

  const [metrics, setMetrics] = React.useState<DashboardMetrics | null>(null);
  const [isLoading, setIsLoading] = React.useState(false);
  const [isRefreshing, setIsRefreshing] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // ── Load Metrics ───────────────────────────────────────────────────────────

  /**
   * Load (or reload) metrics from GET /api/spe/dashboard/metrics?configId={id}.
   * Silently fails if configId is not available — user sees the "select config" banner.
   */
  const loadMetrics = React.useCallback(async (configId: string) => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await speApiClient.dashboard.getMetrics(configId);
      setMetrics(data);
    } catch (err) {
      const message =
        err instanceof Error ? err.message : "Failed to load dashboard metrics.";
      setError(message);
    } finally {
      setIsLoading(false);
    }
  }, []);

  // Load metrics on mount and when the selected config changes.
  React.useEffect(() => {
    if (!selectedConfig?.id) {
      setMetrics(null);
      return;
    }
    void loadMetrics(selectedConfig.id);
  }, [selectedConfig?.id, loadMetrics]);

  // ── Refresh ────────────────────────────────────────────────────────────────

  /**
   * Trigger a manual cache refresh via POST /api/spe/dashboard/refresh.
   * Waits for the server to return updated metrics (up to 30 s server-side).
   */
  const handleRefresh = React.useCallback(async () => {
    if (!selectedConfig?.id || isRefreshing) return;

    setIsRefreshing(true);
    setError(null);
    try {
      const data = await speApiClient.dashboard.refresh(selectedConfig.id);
      setMetrics(data);
    } catch (err) {
      const message =
        err instanceof Error ? err.message : "Dashboard refresh failed.";
      setError(message);
      // On refresh failure, fall back to re-fetching the last cached value
      void loadMetrics(selectedConfig.id);
    } finally {
      setIsRefreshing(false);
    }
  }, [selectedConfig?.id, isRefreshing, loadMetrics]);

  // ── No Config Selected ─────────────────────────────────────────────────────

  if (!selectedConfig) {
    return (
      <div className={styles.root}>
        <div className={styles.pageHeader}>
          <Text size={600} weight="semibold" className={styles.pageTitle}>
            Dashboard
          </Text>
        </div>

        <div className={styles.noConfigBanner} role="status">
          <Text size={400} weight="semibold" block>
            No Container Type Config selected
          </Text>
          <Text size={300} style={{ color: tokens.colorNeutralForeground2 }} block>
            Select a Business Unit and Container Type Config to view dashboard metrics.
            Use the BU selector in the navigation panel to get started.
          </Text>
        </div>
      </div>
    );
  }

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      {/* ── Page Header ── */}
      <div className={styles.pageHeader}>
        <Text size={600} weight="semibold" className={styles.pageTitle}>
          Dashboard
        </Text>

        <div className={styles.headerRight}>
          {metrics?.lastRefreshedDateTime && (
            <Tooltip
              content={`Last refreshed: ${formatDateTime(metrics.lastRefreshedDateTime)}`}
              relationship="description"
            >
              <span style={{ display: "flex", alignItems: "center", gap: tokens.spacingHorizontalXS }}>
                <Clock20Regular style={{ color: tokens.colorNeutralForeground3 }} />
                <Text size={200} className={styles.lastRefreshed}>
                  {formatDateTime(metrics.lastRefreshedDateTime)}
                </Text>
              </span>
            </Tooltip>
          )}

          <Button
            appearance="secondary"
            icon={
              isRefreshing ? (
                <Spinner size="extra-tiny" />
              ) : (
                <ArrowClockwise20Regular />
              )
            }
            onClick={handleRefresh}
            disabled={isRefreshing || isLoading}
            aria-label="Refresh dashboard metrics"
          >
            {isRefreshing ? "Refreshing…" : "Refresh"}
          </Button>
        </div>
      </div>

      {/* ── Error Banner ── */}
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>
            <Text size={300}>{error}</Text>
          </MessageBarBody>
        </MessageBar>
      )}

      {/* ── Loading State ── */}
      {isLoading && !metrics && (
        <div className={styles.loadingState} role="status" aria-label="Loading dashboard metrics">
          <Spinner size="medium" label="Loading dashboard metrics…" />
        </div>
      )}

      {/* ── Metrics Cards ── */}
      {metrics && (
        <>
          <div
            className={styles.metricsGrid}
            role="region"
            aria-label="Key metrics"
          >
            <MetricCard
              icon={<Storage20Regular />}
              label="Total Containers"
              value={metrics.totalContainerCount.toLocaleString()}
              subtext={`${metrics.deletedContainerCount} deleted`}
            />
            <MetricCard
              icon={<CheckmarkCircle20Regular />}
              label="Active Containers"
              value={metrics.activeContainerCount.toLocaleString()}
              subtext={
                metrics.totalContainerCount > 0
                  ? `${Math.round(
                      (metrics.activeContainerCount / metrics.totalContainerCount) * 100
                    )}% of total`
                  : undefined
              }
            />
            <MetricCard
              icon={<DataBarVertical20Regular />}
              label="Storage Used"
              value={formatBytes(metrics.totalStorageUsedInBytes)}
              subtext="Across all containers"
            />
          </div>

          <Divider />

          {/* ── Recent Activity Grid ── */}
          <section className={styles.section}>
            <div className={styles.sectionHeader}>
              <Text size={400} weight="semibold" className={styles.sectionTitle}>
                Containers
              </Text>
              {metrics.containers.length > 0 && (
                <Badge appearance="tint" color="informative" size="small">
                  {metrics.containers.length}
                </Badge>
              )}
            </div>

            <RecentActivityGrid containers={metrics.containers} />
          </section>
        </>
      )}

      {/* ── No Data Yet (after successful load but no metrics cached) ── */}
      {!isLoading && !metrics && !error && (
        <div className={styles.emptyState} role="status">
          <DataBarVertical20Regular style={{ fontSize: "32px", opacity: 0.4 }} />
          <Text size={300} style={{ color: "inherit" }}>
            No metrics available yet
          </Text>
          <Text size={200} style={{ color: "inherit" }}>
            Click Refresh to trigger the first data sync.
          </Text>
          <Button
            appearance="primary"
            icon={<ArrowClockwise20Regular />}
            onClick={handleRefresh}
            disabled={isRefreshing}
          >
            Refresh Now
          </Button>
        </div>
      )}
    </div>
  );
};
