import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
  Badge,
  Tooltip,
  DataGrid,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridBody,
  DataGridRow,
  DataGridCell,
  TableColumnDefinition,
  createTableColumn,
  TableCellLayout,
  Skeleton,
  SkeletonItem,
  Divider,
} from "@fluentui/react-components";
import {
  ArrowClockwise20Regular,
  Shield20Regular,
  ShieldError20Regular,
} from "@fluentui/react-icons";
import type { SecurityAlert, AlertSeverity, AlertStatus } from "../../types/spe";
import { speApiClient } from "../../services/speApiClient";
import { useBuContext } from "../../contexts/BuContext";
import { SecureScoreCard } from "./SecureScoreCard";
import type { SecureScore } from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /**
   * Full-height page container (flex column).
   * Fits inside AppShell's contentInner scroll area.
   * Background token adapts to light/dark/high-contrast (ADR-021).
   */
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },

  /** Page header row: title + refresh button */
  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalM,
    flexShrink: 0,
  },

  headerTitle: {
    flex: "1 1 auto",
    color: tokens.colorNeutralForeground1,
  },

  /** Context breadcrumb below header */
  breadcrumb: {
    paddingLeft: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalS,
  },

  /**
   * Scrollable content area — contains both the score card and alerts grid.
   * Uses overflow auto so the page scrolls as a whole on small viewports.
   */
  content: {
    flex: "1 1 auto",
    overflowY: "auto",
    overflowX: "hidden",
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingVerticalL,
    paddingTop: tokens.spacingVerticalM,
  },

  /**
   * Section header row above each major section (score card, alerts grid).
   */
  sectionHeader: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    marginBottom: tokens.spacingVerticalXS,
  },

  sectionIcon: {
    display: "flex",
    alignItems: "center",
    color: tokens.colorNeutralForeground2,
  },

  sectionTitle: {
    color: tokens.colorNeutralForeground1,
  },

  /** DataGrid wrapper for the alerts table */
  gridWrapper: {
    flex: "0 0 auto",
    overflowX: "auto",
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftWidth: "1px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorNeutralStroke2,
    borderRightWidth: "1px",
    borderRightStyle: "solid",
    borderRightColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
  },

  /** Severity badge cell — flex for alignment */
  severityCell: {
    display: "flex",
    alignItems: "center",
  },

  /** Status badge cell — flex for alignment */
  statusCell: {
    display: "flex",
    alignItems: "center",
  },

  /** Truncate long title text in grid cells */
  cellText: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    maxWidth: "280px",
  },

  /** Loading / empty state centered overlay */
  stateContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalXXL,
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground2,
  },

  /** No-config prompt container */
  noConfigContainer: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground2,
  },

  noConfigIcon: {
    color: tokens.colorNeutralForeground3,
    display: "flex",
    alignItems: "center",
  },

  /** Alerts grid skeleton row */
  skeletonRow: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalM,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Map alert severity to a Fluent v9 Badge color.
 * Uses semantic design tokens only — adapts to dark mode.
 *
 * Severity mapping (Graph API values):
 *   critical → danger
 *   high     → danger
 *   medium   → warning
 *   low      → informative
 *   informational / unknown → subtle
 */
function severityBadgeColor(
  severity: AlertSeverity
): "danger" | "warning" | "informative" | "subtle" {
  switch (severity) {
    case "high":
      return "danger";
    case "medium":
      return "warning";
    case "low":
      return "informative";
    case "informational":
    case "unknown":
    default:
      return "subtle";
  }
}

/**
 * Map alert status to a Fluent v9 Badge color.
 * Resolved alerts use success; others use neutral tones.
 */
function statusBadgeColor(
  status: AlertStatus
): "success" | "warning" | "informative" | "subtle" {
  switch (status) {
    case "newAlert":
      return "warning";
    case "inProgress":
      return "informative";
    case "resolved":
      return "success";
    case "unknown":
    default:
      return "subtle";
  }
}

/**
 * Format an alert status for display.
 */
function formatStatus(status: AlertStatus): string {
  switch (status) {
    case "newAlert": return "New";
    case "inProgress": return "In Progress";
    case "resolved": return "Resolved";
    case "unknown": return "Unknown";
    default: return status;
  }
}

/**
 * Format severity for display — capitalize first letter.
 */
function formatSeverity(severity: AlertSeverity): string {
  if (!severity) return "Unknown";
  return severity.charAt(0).toUpperCase() + severity.slice(1);
}

/**
 * Format an ISO timestamp to a compact local date/time string.
 */
function formatTimestamp(iso: string): string {
  try {
    return new Date(iso).toLocaleString(undefined, {
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return iso;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Column Definitions
// ─────────────────────────────────────────────────────────────────────────────

type AlertColumn = TableColumnDefinition<SecurityAlert>;

/**
 * Build DataGrid column definitions for the security alerts table.
 * Columns: title, severity (badge), category, status (badge), created date.
 */
function buildColumns(styles: ReturnType<typeof useStyles>): AlertColumn[] {
  return [
    createTableColumn<SecurityAlert>({
      columnId: "title",
      compare: (a, b) => (a.title ?? "").localeCompare(b.title ?? ""),
      renderHeaderCell: () => "Alert Title",
      renderCell: (item) => (
        <TableCellLayout>
          <Tooltip content={item.description ?? item.title} relationship="description">
            <Text className={styles.cellText} size={300}>
              {item.title}
            </Text>
          </Tooltip>
        </TableCellLayout>
      ),
    }),

    createTableColumn<SecurityAlert>({
      columnId: "severity",
      compare: (a, b) => {
        // Sort order: high > medium > low > informational > unknown
        const order: Record<AlertSeverity, number> = {
          high: 0,
          medium: 1,
          low: 2,
          informational: 3,
          unknown: 4,
        };
        return (order[a.severity] ?? 99) - (order[b.severity] ?? 99);
      },
      renderHeaderCell: () => "Severity",
      renderCell: (item) => (
        <TableCellLayout>
          <div className={styles.severityCell}>
            <Badge
              color={severityBadgeColor(item.severity)}
              appearance="filled"
              size="small"
              aria-label={`Severity: ${formatSeverity(item.severity)}`}
            >
              {formatSeverity(item.severity)}
            </Badge>
          </div>
        </TableCellLayout>
      ),
    }),

    createTableColumn<SecurityAlert>({
      columnId: "category",
      compare: (a, b) => (a.category ?? "").localeCompare(b.category ?? ""),
      renderHeaderCell: () => "Category",
      renderCell: (item) => (
        <TableCellLayout>
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            {item.category ?? "—"}
          </Text>
        </TableCellLayout>
      ),
    }),

    createTableColumn<SecurityAlert>({
      columnId: "status",
      compare: (a, b) => a.status.localeCompare(b.status),
      renderHeaderCell: () => "Status",
      renderCell: (item) => (
        <TableCellLayout>
          <div className={styles.statusCell}>
            <Badge
              color={statusBadgeColor(item.status)}
              appearance="tint"
              size="small"
              aria-label={`Status: ${formatStatus(item.status)}`}
            >
              {formatStatus(item.status)}
            </Badge>
          </div>
        </TableCellLayout>
      ),
    }),

    createTableColumn<SecurityAlert>({
      columnId: "createdDateTime",
      compare: (a, b) =>
        new Date(a.createdDateTime).getTime() -
        new Date(b.createdDateTime).getTime(),
      renderHeaderCell: () => "Created",
      renderCell: (item) => (
        <TableCellLayout>
          <Tooltip content={item.createdDateTime} relationship="description">
            <Text size={200} style={{ whiteSpace: "nowrap", color: tokens.colorNeutralForeground2 }}>
              {formatTimestamp(item.createdDateTime)}
            </Text>
          </Tooltip>
        </TableCellLayout>
      ),
    }),
  ];
}

// ─────────────────────────────────────────────────────────────────────────────
// SecurityPage Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SecurityPage — read-only security dashboard for the SPE Admin App.
 *
 * Displays two sections:
 *   1. SecureScoreCard — current/max score as a percentage with progress bar
 *   2. Security Alerts grid — sortable table of Microsoft 365 security alerts
 *      with severity badges and status indicators
 *
 * Both data sources are fetched in parallel on mount (Promise.allSettled).
 * Each section handles its own loading skeleton and empty/error state.
 *
 * This is a read-only view — security remediation happens in the Microsoft
 * 365 Security Center (security.microsoft.com).
 *
 * ADR compliance:
 *   - ADR-021: Fluent v9 makeStyles + design tokens; dark mode via tokens; no hard-coded colors
 *   - ADR-012: speApiClient.security.* for API; no direct fetch()
 *   - ADR-006: Code Page pattern; no PCF APIs
 */
export const SecurityPage: React.FC = () => {
  const styles = useStyles();
  const { selectedConfig, selectedBu } = useBuContext();

  // ── Data state — alerts ──────────────────────────────────────────────────

  const [alerts, setAlerts] = React.useState<SecurityAlert[]>([]);
  const [isAlertsLoading, setIsAlertsLoading] = React.useState(false);
  const [alertsError, setAlertsError] = React.useState<string | null>(null);

  // ── Data state — secure score ────────────────────────────────────────────

  const [score, setScore] = React.useState<SecureScore | null>(null);
  const [isScoreLoading, setIsScoreLoading] = React.useState(false);
  const [scoreError, setScoreError] = React.useState<string | null>(null);

  // ── Column definitions ──────────────────────────────────────────────────

  const columns = React.useMemo(() => buildColumns(styles), [styles]);

  // ── Parallel data fetching ──────────────────────────────────────────────

  /**
   * Fetch both alerts and secure score in parallel using Promise.allSettled.
   * Each result is handled independently so a failure in one does not
   * prevent the other from displaying.
   */
  const loadData = React.useCallback(async () => {
    if (!selectedConfig) return;

    const configId = selectedConfig.id;

    // Start both loaders simultaneously
    setIsAlertsLoading(true);
    setIsScoreLoading(true);
    setAlertsError(null);
    setScoreError(null);

    const [alertsResult, scoreResult] = await Promise.allSettled([
      speApiClient.security.listAlerts(configId),
      speApiClient.security.getScore(configId),
    ]);

    // Handle alerts result
    if (alertsResult.status === "fulfilled") {
      setAlerts(alertsResult.value ?? []);
    } else {
      const msg =
        alertsResult.reason instanceof Error
          ? alertsResult.reason.message
          : "Failed to load security alerts.";
      setAlertsError(msg);
      setAlerts([]);
    }
    setIsAlertsLoading(false);

    // Handle score result
    if (scoreResult.status === "fulfilled") {
      setScore(scoreResult.value ?? null);
    } else {
      const msg =
        scoreResult.reason instanceof Error
          ? scoreResult.reason.message
          : "Failed to load Secure Score.";
      setScoreError(msg);
      setScore(null);
    }
    setIsScoreLoading(false);
  }, [selectedConfig]);

  // Load on mount and when selected config changes
  React.useEffect(() => {
    if (selectedConfig) {
      void loadData();
    } else {
      // Reset state when config is cleared
      setAlerts([]);
      setScore(null);
      setAlertsError(null);
      setScoreError(null);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedConfig?.id]);

  const handleRefresh = React.useCallback(() => {
    void loadData();
  }, [loadData]);

  const isAnyLoading = isAlertsLoading || isScoreLoading;

  // ── Render helpers ──────────────────────────────────────────────────────

  /** No config selected prompt */
  const renderNoConfig = () => (
    <div className={styles.noConfigContainer}>
      <span className={styles.noConfigIcon} style={{ fontSize: "40px" }}>
        <Shield20Regular />
      </span>
      <Text size={400} weight="semibold">
        No Config Selected
      </Text>
      <Text
        size={300}
        style={{
          color: tokens.colorNeutralForeground2,
          textAlign: "center",
          maxWidth: "360px",
        }}
      >
        Select a Business Unit and Container Type Config using the BU picker to
        view security information.
        {selectedBu && !selectedConfig
          ? ` Business Unit "${selectedBu.name}" is selected — please also select a Config.`
          : ""}
      </Text>
    </div>
  );

  /** Alerts skeleton loading state */
  const renderAlertsLoading = () => (
    <div>
      {[0, 1, 2, 3, 4].map((i) => (
        <div key={i} className={styles.skeletonRow}>
          <Skeleton>
            <SkeletonItem size={12} style={{ width: `${60 + (i % 3) * 15}%` }} />
          </Skeleton>
        </div>
      ))}
    </div>
  );

  /** Alerts error state */
  const renderAlertsError = () => (
    <MessageBar intent="error">
      <MessageBarBody>
        {alertsError}
        <Button
          appearance="transparent"
          size="small"
          onClick={handleRefresh}
          style={{ marginLeft: tokens.spacingHorizontalS }}
        >
          Retry
        </Button>
      </MessageBarBody>
    </MessageBar>
  );

  /** Score error state */
  const renderScoreError = () => (
    <MessageBar intent="error">
      <MessageBarBody>
        {scoreError}
        <Button
          appearance="transparent"
          size="small"
          onClick={handleRefresh}
          style={{ marginLeft: tokens.spacingHorizontalS }}
        >
          Retry
        </Button>
      </MessageBarBody>
    </MessageBar>
  );

  /** Alerts empty state */
  const renderAlertsEmpty = () => (
    <div className={styles.stateContainer}>
      <ShieldError20Regular style={{ fontSize: "32px", color: tokens.colorNeutralForeground3 }} />
      <Text size={400} weight="semibold">
        No Security Alerts
      </Text>
      <Text size={300} style={{ color: tokens.colorNeutralForeground2, textAlign: "center" }}>
        No active security alerts were found for this configuration.
        This indicates a healthy security posture.
      </Text>
    </div>
  );

  // ── Main render ─────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      {/* ── Page Header ── */}
      <div className={styles.header}>
        <Text className={styles.headerTitle} size={500} weight="semibold">
          Security
        </Text>
        {selectedConfig && (
          <Tooltip content="Refresh security data" relationship="label">
            <Button
              appearance="subtle"
              icon={<ArrowClockwise20Regular />}
              onClick={handleRefresh}
              disabled={isAnyLoading}
              aria-label="Refresh security data"
            />
          </Tooltip>
        )}
      </div>

      {/* ── Config context breadcrumb ── */}
      {selectedConfig && (
        <div className={styles.breadcrumb}>
          <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
            {selectedBu?.name && `${selectedBu.name} / `}
            {selectedConfig.name}
          </Text>
        </div>
      )}

      <Divider style={{ flexShrink: 0 }} />

      {/* ── No config selected ── */}
      {!selectedConfig && renderNoConfig()}

      {/* ── Content (score card + alerts) — only when config is selected ── */}
      {selectedConfig && (
        <div className={styles.content}>

          {/* ── Section 1: Secure Score ── */}
          <div>
            <div className={styles.sectionHeader}>
              <span className={styles.sectionIcon}>
                <Shield20Regular />
              </span>
              <Text size={400} weight="semibold" className={styles.sectionTitle}>
                Secure Score
              </Text>
            </div>

            {/* Score error */}
            {scoreError && !isScoreLoading && renderScoreError()}

            {/* Score card — shows skeleton while loading, real data when ready */}
            {!scoreError && (
              <SecureScoreCard score={score} isLoading={isScoreLoading} />
            )}
          </div>

          {/* ── Section 2: Security Alerts ── */}
          <div>
            <div className={styles.sectionHeader}>
              <span className={styles.sectionIcon}>
                <ShieldError20Regular />
              </span>
              <Text size={400} weight="semibold" className={styles.sectionTitle}>
                Security Alerts
              </Text>
              {!isAlertsLoading && alerts.length > 0 && (
                <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                  ({alerts.length})
                </Text>
              )}
            </div>

            {/* Alerts error */}
            {alertsError && !isAlertsLoading && renderAlertsError()}

            {/* Alerts loading skeleton */}
            {isAlertsLoading && !alertsError && (
              <div className={styles.gridWrapper}>
                {renderAlertsLoading()}
              </div>
            )}

            {/* Alerts empty state */}
            {!isAlertsLoading && !alertsError && alerts.length === 0 && (
              <div className={styles.gridWrapper}>
                {renderAlertsEmpty()}
              </div>
            )}

            {/* Alerts data grid */}
            {!isAlertsLoading && !alertsError && alerts.length > 0 && (
              <div className={styles.gridWrapper}>
                <DataGrid
                  items={alerts}
                  columns={columns}
                  sortable
                  getRowId={(item: SecurityAlert) => item.id}
                  style={{ width: "100%" }}
                  aria-label="Security alerts"
                >
                  <DataGridHeader>
                    <DataGridRow>
                      {({ renderHeaderCell }) => (
                        <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
                      )}
                    </DataGridRow>
                  </DataGridHeader>
                  <DataGridBody<SecurityAlert>>
                    {({ item, rowId }) => (
                      <DataGridRow<SecurityAlert> key={rowId}>
                        {({ renderCell }) => (
                          <DataGridCell>{renderCell(item)}</DataGridCell>
                        )}
                      </DataGridRow>
                    )}
                  </DataGridBody>
                </DataGrid>
              </div>
            )}
          </div>

          {/* Read-only disclaimer */}
          <Text
            size={100}
            style={{
              color: tokens.colorNeutralForeground4,
              textAlign: "center",
              paddingBottom: tokens.spacingVerticalM,
            }}
          >
            Security data is read-only. Remediation and alert management are performed
            in the Microsoft 365 Security Center (security.microsoft.com).
          </Text>
        </div>
      )}
    </div>
  );
};
